-- PTVgame accounts, VIP, agents, payments and admin (Supabase / PostgreSQL)
-- Run once in Supabase SQL Editor, or `supabase db push`.
-- Security model: clients never write roles, VIP time, prices or payment state directly.
-- All of that goes through SECURITY DEFINER functions below, guarded by auth.uid().

create extension if not exists pgcrypto;

-- ---------------------------------------------------------------- types
do $$ begin create type public.user_role as enum ('user','agent','admin'); exception when duplicate_object then null; end $$;
do $$ begin create type public.order_status as enum ('pending','review','paid','rejected','cancelled'); exception when duplicate_object then null; end $$;
do $$ begin create type public.pay_method as enum ('promptpay','paypal','card','manual'); exception when duplicate_object then null; end $$;

-- ---------------------------------------------------------------- tables
create table if not exists public.profiles (
  id           uuid primary key references auth.users(id) on delete cascade,
  ptv_id       bigint not null unique,                       -- 8-digit public ID shown in the launcher
  display_name text   not null default 'Player' check (char_length(display_name) between 1 and 40),
  avatar_url   text   check (avatar_url is null or (avatar_url ~ '^https://' and char_length(avatar_url) < 1000)),
  provider     text,
  role         public.user_role not null default 'user',
  vip_until    timestamptz,
  banned       boolean not null default false,
  created_at   timestamptz not null default now(),
  last_seen    timestamptz
);

create table if not exists public.plans (
  code            text primary key,
  name            text not null,
  days            int  not null check (days > 0),
  price_usd       numeric(10,2) not null check (price_usd > 0),
  list_price_usd  numeric(10,2),                              -- crossed-out price
  agent_price_usd numeric(10,2) not null check (agent_price_usd > 0),
  price_thb       numeric(10,2) not null check (price_thb > 0),
  agent_price_thb numeric(10,2) not null check (agent_price_thb > 0),
  active          boolean not null default true,
  sort            int not null default 0
);

-- Prices requested: monthly $25 -> $20, yearly $300 -> $220.
-- Agent and THB prices are starting values; change them in Launcher > Admin > Prices.
insert into public.plans(code,name,days,price_usd,list_price_usd,agent_price_usd,price_thb,agent_price_thb,sort) values
  ('monthly','VIP รายเดือน',30, 20.00, 25.00, 15.00,  690.00,  520.00,1),
  ('yearly', 'VIP รายปี',  365,220.00,300.00,165.00, 7590.00, 5690.00,2)
on conflict (code) do nothing;

create table if not exists public.settings (
  key   text primary key,
  value text not null default ''
);
insert into public.settings(key,value) values
  ('promptpay_id',''),              -- phone 0812345678, national/tax ID 13 digits, or e-wallet 15 digits
  ('promptpay_name',''),            -- account name shown under the QR
  ('max_pending_orders','5')
on conflict (key) do nothing;

create table if not exists public.orders (
  id                uuid primary key default gen_random_uuid(),
  order_no          bigint generated always as identity unique,
  buyer_id          uuid not null references public.profiles(id) on delete cascade,
  target_id         uuid not null references public.profiles(id) on delete cascade,
  plan_code         text not null references public.plans(code),
  days              int  not null,
  amount            numeric(10,2) not null,
  currency          text not null check (currency in ('USD','THB')),
  method            public.pay_method not null,
  status            public.order_status not null default 'pending',
  agent_price       boolean not null default false,
  promptpay_payload text,
  slip_path         text,
  paypal_order_id   text unique,
  paypal_capture_id text unique,
  note              text,
  created_at        timestamptz not null default now(),
  paid_at           timestamptz,
  reviewed_by       uuid references public.profiles(id),
  reviewed_at       timestamptz
);
create index if not exists orders_buyer_idx  on public.orders(buyer_id, created_at desc);
create index if not exists orders_status_idx on public.orders(status, created_at desc);

create table if not exists public.audit_log (
  id         bigint generated always as identity primary key,
  actor_id   uuid,
  action     text not null,
  target_id  uuid,
  detail     jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now()
);

-- ---------------------------------------------------------------- helpers
create or replace function public.is_admin() returns boolean
language sql stable security definer set search_path = public as $$
  select exists(select 1 from public.profiles where id = auth.uid() and role = 'admin' and not banned)
$$;

create or replace function public.new_ptv_id() returns bigint
language plpgsql volatile set search_path = public as $$
declare candidate bigint;
begin
  loop
    candidate := 10000000 + floor(random() * 90000000)::bigint;   -- 8 digits, not guessable by counting
    exit when not exists(select 1 from public.profiles where ptv_id = candidate);
  end loop;
  return candidate;
end $$;

create or replace function public.log_action(p_action text, p_target uuid, p_detail jsonb)
returns void language sql security definer set search_path = public as $$
  insert into public.audit_log(actor_id, action, target_id, detail) values (auth.uid(), p_action, p_target, coalesce(p_detail,'{}'::jsonb))
$$;

-- New auth user -> profile (name + picture from Google/Facebook when available)
create or replace function public.handle_new_user() returns trigger
language plpgsql security definer set search_path = public as $$
declare
  meta jsonb := coalesce(new.raw_user_meta_data, '{}'::jsonb);
  pic  text  := coalesce(meta->>'avatar_url', meta->>'picture');
  nm   text  := left(coalesce(nullif(trim(meta->>'full_name'),''), nullif(trim(meta->>'name'),''), split_part(coalesce(new.email,'Player'),'@',1), 'Player'), 40);
begin
  if pic is not null and pic !~ '^https://' then pic := null; end if;
  insert into public.profiles(id, ptv_id, display_name, avatar_url, provider)
  values (new.id, public.new_ptv_id(), coalesce(nullif(nm,''),'Player'), pic, coalesce(new.raw_app_meta_data->>'provider','email'))
  on conflict (id) do nothing;
  return new;
end $$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created after insert on auth.users
  for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------- PromptPay (EMVCo QR payload + CRC16/CCITT-FALSE)
create or replace function public.crc16_ccitt(p text) returns text
language plpgsql immutable as $$
declare crc int := 65535; b int; i int; j int; bytes bytea := convert_to(p,'UTF8');
begin
  for i in 0 .. length(bytes)-1 loop
    b := get_byte(bytes, i);
    crc := crc # (b << 8);
    for j in 1..8 loop
      if (crc & 32768) <> 0 then crc := ((crc << 1) # 4129) & 65535; else crc := (crc << 1) & 65535; end if;
    end loop;
  end loop;
  return upper(lpad(to_hex(crc),4,'0'));
end $$;

create or replace function public.emv(tag text, val text) returns text
language sql immutable as $$ select tag || lpad(length(val)::text,2,'0') || val $$;

create or replace function public.promptpay_payload(p_id text, p_amount numeric) returns text
language plpgsql immutable as $$
declare id text := regexp_replace(coalesce(p_id,''),'[^0-9]','','g'); acct text; body text;
begin
  if length(id) = 10 and left(id,1) = '0' then acct := public.emv('01', '0066' || substr(id,2));   -- mobile
  elsif length(id) = 13 then acct := public.emv('02', id);                                      -- national / tax ID
  elsif length(id) = 15 then acct := public.emv('03', id);                                      -- e-wallet
  else raise exception 'PromptPay ID is not configured correctly'; end if;
  body := public.emv('00','01') || public.emv('01','12')
       || public.emv('29', public.emv('00','A000000677010111') || acct)
       || public.emv('53','764') || public.emv('54', to_char(p_amount,'FM999999990.00')) || public.emv('58','TH') || '6304';
  return body || public.crc16_ccitt(body);
end $$;

-- ---------------------------------------------------------------- user-facing functions
create or replace function public.my_account() returns json
language plpgsql security definer set search_path = public as $$
declare p public.profiles; em text;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  update public.profiles set last_seen = now() where id = auth.uid() returning * into p;
  if p.id is null then raise exception 'profile missing'; end if;
  select email into em from auth.users where id = auth.uid();
  return json_build_object('id',p.id,'ptv_id',p.ptv_id,'display_name',p.display_name,'avatar_url',p.avatar_url,
    'provider',p.provider,'role',p.role,'vip_until',p.vip_until,'is_vip',(p.vip_until is not null and p.vip_until > now()) or p.role='admin',
    'banned',p.banned,'email',em,'server_time',now());
end $$;

create or replace function public.update_my_profile(p_display_name text, p_avatar_url text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  if p_avatar_url is not null and p_avatar_url !~ '^https://' then raise exception 'avatar must be https'; end if;
  update public.profiles set display_name = left(trim(p_display_name),40), avatar_url = p_avatar_url where id = auth.uid();
end $$;

-- Agents/admins look up a player by PTV ID before buying VIP for them.
create or replace function public.lookup_player(p_ptv_id bigint) returns json
language plpgsql stable security definer set search_path = public as $$
declare p public.profiles; caller public.user_role;
begin
  select role into caller from public.profiles where id = auth.uid() and not banned;
  if caller is null or caller not in ('agent','admin') then raise exception 'agents only'; end if;
  select * into p from public.profiles where ptv_id = p_ptv_id;
  if p.id is null then return null; end if;
  return json_build_object('ptv_id',p.ptv_id,'display_name',p.display_name,'avatar_url',p.avatar_url,'vip_until',p.vip_until);
end $$;

create or replace function public.create_order(p_plan text, p_method text, p_target_ptv_id bigint default null)
returns public.orders
language plpgsql security definer set search_path = public as $$
declare me public.profiles; tgt public.profiles; pl public.plans; o public.orders; m public.pay_method; agent boolean; pp text; pending int;
begin
  select * into me from public.profiles where id = auth.uid();
  if me.id is null then raise exception 'not signed in'; end if;
  if me.banned then raise exception 'account suspended'; end if;
  m := p_method::public.pay_method;
  if m = 'manual' then raise exception 'invalid method'; end if;
  select * into pl from public.plans where code = p_plan and active;
  if pl.code is null then raise exception 'plan not available'; end if;

  if p_target_ptv_id is null or p_target_ptv_id = me.ptv_id then
    tgt := me;
  else
    if me.role not in ('agent','admin') then raise exception 'only agents can buy VIP for another ID'; end if;
    select * into tgt from public.profiles where ptv_id = p_target_ptv_id;
    if tgt.id is null then raise exception 'PTV ID not found'; end if;
    if tgt.banned then raise exception 'target account suspended'; end if;
  end if;

  select count(*) into pending from public.orders where buyer_id = me.id and status in ('pending','review');
  if pending >= coalesce((select value::int from public.settings where key='max_pending_orders'),5) then
    raise exception 'too many unfinished orders, cancel or finish one first';
  end if;

  agent := me.role in ('agent','admin');
  o.amount   := case when m = 'promptpay' then (case when agent then pl.agent_price_thb else pl.price_thb end)
                     else (case when agent then pl.agent_price_usd else pl.price_usd end) end;
  o.currency := case when m = 'promptpay' then 'THB' else 'USD' end;
  if m = 'promptpay' then
    select value into pp from public.settings where key = 'promptpay_id';
    o.promptpay_payload := public.promptpay_payload(pp, o.amount);
  end if;

  insert into public.orders(buyer_id,target_id,plan_code,days,amount,currency,method,agent_price,promptpay_payload)
  values (me.id, tgt.id, pl.code, pl.days, o.amount, o.currency, m, agent, o.promptpay_payload)
  returning * into o;
  return o;
end $$;

-- Account name shown under the PromptPay QR so buyers can check who they are paying.
create or replace function public.payment_info() returns json
language sql stable security definer set search_path = public as $$
  select json_build_object('promptpay_name', coalesce((select value from public.settings where key = 'promptpay_name'), ''))
$$;

create or replace function public.attach_slip(p_order uuid, p_path text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if p_path is null or p_path not like auth.uid()::text || '/%' then raise exception 'invalid slip path'; end if;
  update public.orders set slip_path = p_path, status = 'review'
   where id = p_order and buyer_id = auth.uid() and method = 'promptpay' and status in ('pending','review');
  if not found then raise exception 'order not found'; end if;
end $$;

create or replace function public.cancel_order(p_order uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  update public.orders set status = 'cancelled' where id = p_order and buyer_id = auth.uid() and status = 'pending';
  if not found then raise exception 'only unpaid orders can be cancelled'; end if;
end $$;

create or replace function public.my_orders() returns setof json
language sql stable security definer set search_path = public as $$
  select json_build_object('id',o.id,'order_no',o.order_no,'plan_code',o.plan_code,'days',o.days,'amount',o.amount,'currency',o.currency,
    'method',o.method,'status',o.status,'created_at',o.created_at,'paid_at',o.paid_at,'note',o.note,'promptpay_payload',o.promptpay_payload,
    'target_ptv_id',t.ptv_id,'target_name',t.display_name)
  from public.orders o join public.profiles t on t.id = o.target_id
  where o.buyer_id = auth.uid() order by o.created_at desc limit 100
$$;

-- ---------------------------------------------------------------- payment completion (internal)
-- Called by admin approval and by the PayPal edge functions (service role). Idempotent.
create or replace function public.apply_paid_order(p_order uuid, p_capture_id text default null, p_actor uuid default null)
returns public.orders
language plpgsql security definer set search_path = public as $$
declare o public.orders;
begin
  select * into o from public.orders where id = p_order for update;
  if o.id is null then raise exception 'order not found'; end if;
  if o.status = 'paid' then return o; end if;
  if o.status in ('cancelled') and p_capture_id is null then raise exception 'order was cancelled'; end if;
  update public.profiles set vip_until = greatest(coalesce(vip_until, now()), now()) + make_interval(days => o.days)
   where id = o.target_id;
  update public.orders set status = 'paid', paid_at = now(), paypal_capture_id = coalesce(p_capture_id, paypal_capture_id),
         reviewed_by = coalesce(p_actor, reviewed_by), reviewed_at = case when p_actor is null then reviewed_at else now() end
   where id = o.id returning * into o;
  insert into public.audit_log(actor_id, action, target_id, detail)
  values (p_actor, 'order_paid', o.target_id, json_build_object('order',o.id,'order_no',o.order_no,'amount',o.amount,'currency',o.currency,'method',o.method)::jsonb);
  return o;
end $$;

-- ---------------------------------------------------------------- admin functions
create or replace function public.require_admin() returns void
language plpgsql stable security definer set search_path = public as $$
begin if not public.is_admin() then raise exception 'admin only'; end if; end $$;

create or replace function public.admin_stats() returns json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return json_build_object(
    'users',  (select count(*) from public.profiles),
    'vip',    (select count(*) from public.profiles where vip_until > now()),
    'agents', (select count(*) from public.profiles where role = 'agent'),
    'review', (select count(*) from public.orders where status = 'review'),
    'pending',(select count(*) from public.orders where status = 'pending'),
    'revenue_usd_30d', (select coalesce(sum(amount),0) from public.orders where status='paid' and currency='USD' and paid_at > now() - interval '30 days'),
    'revenue_thb_30d', (select coalesce(sum(amount),0) from public.orders where status='paid' and currency='THB' and paid_at > now() - interval '30 days'),
    'new_users_7d', (select count(*) from public.profiles where created_at > now() - interval '7 days'));
end $$;

create or replace function public.admin_list_users(p_search text default '', p_limit int default 50, p_offset int default 0)
returns setof json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return query
  select json_build_object('id',p.id,'ptv_id',p.ptv_id,'display_name',p.display_name,'avatar_url',p.avatar_url,'provider',p.provider,
           'role',p.role,'vip_until',p.vip_until,'banned',p.banned,'created_at',p.created_at,'last_seen',p.last_seen,'email',u.email)
  from public.profiles p left join auth.users u on u.id = p.id
  where coalesce(p_search,'') = '' or p.ptv_id::text = trim(p_search) or p.display_name ilike '%'||p_search||'%' or u.email ilike '%'||p_search||'%'
  order by p.created_at desc limit least(greatest(p_limit,1),200) offset greatest(p_offset,0);
end $$;

create or replace function public.admin_list_orders(p_status text default null, p_limit int default 50, p_offset int default 0)
returns setof json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return query
  select json_build_object('id',o.id,'order_no',o.order_no,'plan_code',o.plan_code,'days',o.days,'amount',o.amount,'currency',o.currency,
           'method',o.method,'status',o.status,'agent_price',o.agent_price,'slip_path',o.slip_path,'paypal_order_id',o.paypal_order_id,
           'created_at',o.created_at,'paid_at',o.paid_at,'note',o.note,
           'buyer_ptv_id',b.ptv_id,'buyer_name',b.display_name,'target_ptv_id',t.ptv_id,'target_name',t.display_name)
  from public.orders o join public.profiles b on b.id = o.buyer_id join public.profiles t on t.id = o.target_id
  where p_status is null or p_status = '' or o.status::text = p_status
  order by o.created_at desc limit least(greatest(p_limit,1),200) offset greatest(p_offset,0);
end $$;

create or replace function public.admin_approve_order(p_order uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  perform public.apply_paid_order(p_order, null, auth.uid());
end $$;

create or replace function public.admin_reject_order(p_order uuid, p_reason text) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  update public.orders set status = 'rejected', note = left(p_reason,300), reviewed_by = auth.uid(), reviewed_at = now()
   where id = p_order and status in ('pending','review');
  if not found then raise exception 'order cannot be rejected'; end if;
  perform public.log_action('order_rejected', null, json_build_object('order',p_order,'reason',p_reason)::jsonb);
end $$;

create or replace function public.admin_set_role(p_user uuid, p_role text) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  if p_user = auth.uid() and p_role <> 'admin' then raise exception 'you cannot remove your own admin role'; end if;
  update public.profiles set role = p_role::public.user_role where id = p_user;
  perform public.log_action('set_role', p_user, json_build_object('role',p_role)::jsonb);
end $$;

-- p_days > 0 adds VIP days; p_days = 0 removes VIP.
create or replace function public.admin_grant_vip(p_user uuid, p_days int) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  if p_days < 0 or p_days > 3650 then raise exception 'days must be 0-3650'; end if;
  update public.profiles set vip_until = case when p_days = 0 then null
         else greatest(coalesce(vip_until, now()), now()) + make_interval(days => p_days) end where id = p_user;
  perform public.log_action('grant_vip', p_user, json_build_object('days',p_days)::jsonb);
end $$;

create or replace function public.admin_set_banned(p_user uuid, p_banned boolean) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  if p_user = auth.uid() then raise exception 'you cannot ban yourself'; end if;
  update public.profiles set banned = p_banned where id = p_user;
  perform public.log_action(case when p_banned then 'ban' else 'unban' end, p_user, '{}'::jsonb);
end $$;

create or replace function public.admin_update_plan(p_code text, p_price_usd numeric, p_list_price_usd numeric, p_agent_price_usd numeric,
  p_price_thb numeric, p_agent_price_thb numeric, p_active boolean) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  update public.plans set price_usd=p_price_usd, list_price_usd=p_list_price_usd, agent_price_usd=p_agent_price_usd,
         price_thb=p_price_thb, agent_price_thb=p_agent_price_thb, active=p_active where code = p_code;
  if not found then raise exception 'plan not found'; end if;
  perform public.log_action('update_plan', null, json_build_object('plan',p_code,'usd',p_price_usd,'agent_usd',p_agent_price_usd,'thb',p_price_thb,'agent_thb',p_agent_price_thb)::jsonb);
end $$;

create or replace function public.admin_get_settings() returns json
language plpgsql stable security definer set search_path = public as $$
begin perform public.require_admin(); return (select json_object_agg(key,value) from public.settings); end $$;

create or replace function public.admin_set_setting(p_key text, p_value text) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  if p_key = 'promptpay_id' and p_value <> '' then perform public.promptpay_payload(p_value, 1); end if;   -- validates format
  insert into public.settings(key,value) values (p_key, coalesce(p_value,'')) on conflict (key) do update set value = excluded.value;
  perform public.log_action('set_setting', null, json_build_object('key',p_key)::jsonb);
end $$;

create or replace function public.admin_audit(p_limit int default 100) returns setof json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return query select json_build_object('at',a.created_at,'action',a.action,'detail',a.detail,
      'actor',(select display_name from public.profiles where id=a.actor_id),
      'target_ptv_id',(select ptv_id from public.profiles where id=a.target_id))
    from public.audit_log a order by a.id desc limit least(greatest(p_limit,1),500);
end $$;

-- ---------------------------------------------------------------- row level security
alter table public.profiles  enable row level security;
alter table public.plans     enable row level security;
alter table public.settings  enable row level security;
alter table public.orders    enable row level security;
alter table public.audit_log enable row level security;

drop policy if exists "own profile"   on public.profiles;
drop policy if exists "admin profiles" on public.profiles;
drop policy if exists "plans readable" on public.plans;
drop policy if exists "own orders"     on public.orders;
drop policy if exists "admin orders"   on public.orders;
create policy "own profile"    on public.profiles for select to authenticated using (id = auth.uid());
create policy "admin profiles" on public.profiles for select to authenticated using (public.is_admin());
create policy "plans readable" on public.plans    for select to anon, authenticated using (true);
create policy "own orders"     on public.orders   for select to authenticated using (buyer_id = auth.uid());
create policy "admin orders"   on public.orders   for select to authenticated using (public.is_admin());
-- settings / audit_log: no policies -> only reachable through the admin functions.

-- explicit grants, so this works even when "Automatically expose new tables" is off
grant usage on schema public to anon, authenticated, service_role;
grant select on public.plans to anon, authenticated;
grant select on public.profiles, public.orders to authenticated;
grant all on public.profiles, public.plans, public.settings, public.orders, public.audit_log to service_role;
grant execute on all functions in schema public to anon, authenticated, service_role;

revoke insert, update, delete on public.profiles, public.plans, public.settings, public.orders, public.audit_log from anon, authenticated;
revoke select on public.settings, public.audit_log from anon, authenticated;
revoke select on public.profiles, public.orders from anon;

-- internal functions must not be callable from the launcher
revoke execute on function public.apply_paid_order(uuid,text,uuid) from public, anon, authenticated;
revoke execute on function public.handle_new_user() from public, anon, authenticated;
revoke execute on function public.log_action(text,uuid,jsonb) from public, anon, authenticated;
revoke execute on function public.new_ptv_id() from public, anon, authenticated;
grant  execute on function public.apply_paid_order(uuid,text,uuid) to service_role;

-- ---------------------------------------------------------------- storage (avatars public, slips private)
insert into storage.buckets(id, name, public, file_size_limit, allowed_mime_types)
values ('avatars','avatars', true,  2097152, array['image/png','image/jpeg']),
       ('slips',  'slips',   false, 5242880, array['image/png','image/jpeg'])
on conflict (id) do nothing;

drop policy if exists "avatar upload own" on storage.objects;
drop policy if exists "avatar update own" on storage.objects;
drop policy if exists "avatar delete own" on storage.objects;
drop policy if exists "slip upload own"   on storage.objects;
drop policy if exists "slip read own or admin" on storage.objects;
create policy "avatar upload own" on storage.objects for insert to authenticated
  with check (bucket_id = 'avatars' and (storage.foldername(name))[1] = auth.uid()::text);
create policy "avatar update own" on storage.objects for update to authenticated
  using (bucket_id = 'avatars' and (storage.foldername(name))[1] = auth.uid()::text);
create policy "avatar delete own" on storage.objects for delete to authenticated
  using (bucket_id = 'avatars' and (storage.foldername(name))[1] = auth.uid()::text);
create policy "slip upload own" on storage.objects for insert to authenticated
  with check (bucket_id = 'slips' and (storage.foldername(name))[1] = auth.uid()::text);
create policy "slip read own or admin" on storage.objects for select to authenticated
  using (bucket_id = 'slips' and ((storage.foldername(name))[1] = auth.uid()::text or public.is_admin()));

-- ---------------------------------------------------------------- first admin
-- After you sign up in the launcher once, run (replace the ID shown on your profile page):
--   update public.profiles set role = 'admin' where ptv_id = 12345678;
