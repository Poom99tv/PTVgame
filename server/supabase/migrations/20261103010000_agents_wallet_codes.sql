-- PTVgame agents v2: agent codes + first-purchase discount (binds customer to agent),
-- commissions into an agent wallet (THB), VIP code batches, wallet spending, withdrawals.
-- Run after 20261103000000_accounts.sql.

alter type public.pay_method add value if not exists 'wallet';

-- ---------------------------------------------------------------- columns / tables
alter table public.profiles add column if not exists agent_code    text unique;
alter table public.profiles add column if not exists referred_by   uuid references public.profiles(id) on delete set null;
alter table public.profiles add column if not exists referred_at   timestamptz;
alter table public.profiles add column if not exists discount_used boolean not null default false;
alter table public.profiles add column if not exists wallet_thb    numeric(12,2) not null default 0 check (wallet_thb >= 0);

alter table public.orders add column if not exists kind             text not null default 'vip' check (kind in ('vip','codes'));
alter table public.orders add column if not exists quantity         int  not null default 1 check (quantity between 1 and 100);
alter table public.orders add column if not exists discount_code    text;
alter table public.orders add column if not exists discount_pct     int  not null default 0;
alter table public.orders add column if not exists commission_agent uuid references public.profiles(id) on delete set null;
alter table public.orders add column if not exists commission_thb   numeric(12,2) not null default 0;

create table if not exists public.wallet_ledger (
  id         bigint generated always as identity primary key,
  agent_id   uuid not null references public.profiles(id) on delete cascade,
  amount_thb numeric(12,2) not null,                     -- + credit, - debit
  kind       text not null check (kind in ('commission','spend','withdraw','withdraw_refund','adjust')),
  order_id   uuid references public.orders(id) on delete set null,
  note       text,
  created_at timestamptz not null default now()
);
create index if not exists wallet_ledger_agent_idx on public.wallet_ledger(agent_id, created_at desc);

create table if not exists public.vip_codes (
  code        text primary key,
  plan_code   text not null references public.plans(code),
  days        int  not null,
  owner_id    uuid not null references public.profiles(id) on delete cascade,
  order_id    uuid references public.orders(id) on delete set null,
  created_at  timestamptz not null default now(),
  redeemed_by uuid references public.profiles(id) on delete set null,
  redeemed_at timestamptz
);
create index if not exists vip_codes_owner_idx on public.vip_codes(owner_id, created_at desc);

create table if not exists public.withdrawals (
  id           uuid primary key default gen_random_uuid(),
  agent_id     uuid not null references public.profiles(id) on delete cascade,
  amount_thb   numeric(12,2) not null check (amount_thb > 0),
  method       text not null check (method in ('promptpay','bank')),
  account      text not null check (char_length(account) between 5 and 200),   -- e.g. "PromptPay 08x..." or "KBank 123-4-56789-0 Name"
  status       text not null default 'requested' check (status in ('requested','paid','rejected')),
  admin_note   text,
  created_at   timestamptz not null default now(),
  processed_at timestamptz,
  processed_by uuid references public.profiles(id)
);

create table if not exists public.download_clicks (
  id         bigint generated always as identity primary key,
  agent_id   uuid references public.profiles(id) on delete cascade,
  created_at timestamptz not null default now()
);

insert into public.settings(key,value) values
  ('usd_thb_rate','34.50'),     -- converts USD commissions into the THB wallet
  ('discount_pct','10'),        -- first purchase with an agent code
  ('download_url','https://github.com/Poom99tv/PTVgame/releases/download/launcher-v1.9.0/PTVgame-Windows-v1.9.0.zip'),  -- update on each launcher release (Admin > Prices)
  ('min_withdrawal_thb','100'),
  ('commission_months','6')     -- how long a customer stays linked to an agent
on conflict (key) do nothing;

alter table public.wallet_ledger   enable row level security;
alter table public.vip_codes       enable row level security;
alter table public.withdrawals     enable row level security;
alter table public.download_clicks enable row level security;
revoke all on public.wallet_ledger, public.vip_codes, public.withdrawals, public.download_clicks from anon, authenticated;
grant all on public.wallet_ledger, public.vip_codes, public.withdrawals, public.download_clicks to service_role;

-- ---------------------------------------------------------------- helpers
create or replace function public.setting_num(p_key text, p_default numeric) returns numeric
language sql stable security definer set search_path = public as $$
  select coalesce((select nullif(value,'')::numeric from public.settings where key = p_key), p_default)
$$;

create or replace function public.binding_active(p_referred_at timestamptz) returns boolean
language sql stable security definer set search_path = public as $$
  select p_referred_at is not null and p_referred_at > now() - make_interval(months => public.setting_num('commission_months',6)::int)
$$;

create or replace function public.random_code(p_len int) returns text
language plpgsql volatile set search_path = public, extensions as $$
declare alphabet text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; r bytea := gen_random_bytes(p_len); s text := ''; i int;
begin
  for i in 0 .. p_len - 1 loop s := s || substr(alphabet, (get_byte(r, i) % 32) + 1, 1); end loop;
  return s;
end $$;

create or replace function public.ensure_agent_code(p_user uuid) returns text
language plpgsql security definer set search_path = public as $$
declare c text;
begin
  select agent_code into c from public.profiles where id = p_user;
  if c is not null then return c; end if;
  loop
    c := public.random_code(6);
    exit when not exists(select 1 from public.profiles where agent_code = c);
  end loop;
  update public.profiles set agent_code = c where id = p_user;
  return c;
end $$;

-- give every existing agent/admin a code
do $$ declare r record; begin
  for r in select id from public.profiles where role in ('agent','admin') and agent_code is null loop
    perform public.ensure_agent_code(r.id);
  end loop;
end $$;

-- ---------------------------------------------------------------- discount code check (store preview)
create or replace function public.check_discount_code(p_code text) returns json
language plpgsql stable security definer set search_path = public as $$
declare me public.profiles; ag public.profiles;
begin
  select * into me from public.profiles where id = auth.uid();
  if me.id is null then raise exception 'not signed in'; end if;
  select * into ag from public.profiles where agent_code = upper(trim(p_code)) and role in ('agent','admin') and not banned;
  if ag.id is null then raise exception 'discount code not found'; end if;
  if ag.id = me.id then raise exception 'you cannot use your own code'; end if;
  if me.role in ('agent','admin') then raise exception 'agents already get agent prices'; end if;
  if me.discount_used then raise exception 'discount already used on this ID'; end if;
  if me.referred_by is not null and me.referred_by <> ag.id and public.binding_active(me.referred_at) then raise exception 'this ID is already linked to another agent'; end if;
  return json_build_object('agent_name', ag.display_name, 'agent_code', ag.agent_code, 'discount_pct', public.setting_num('discount_pct',10)::int);
end $$;

-- ---------------------------------------------------------------- orders (replaces v1 signature)
drop function if exists public.create_order(text, text, bigint);
create or replace function public.create_order(p_plan text, p_method text, p_target_ptv_id bigint default null,
  p_kind text default 'vip', p_quantity int default 1, p_discount_code text default null)
returns public.orders
language plpgsql security definer set search_path = public as $$
declare me public.profiles; tgt public.profiles; ag public.profiles; pl public.plans; o public.orders; m public.pay_method;
        agent boolean; pp text; pending int; base numeric; pct int := 0; dcode text;
begin
  select * into me from public.profiles where id = auth.uid();
  if me.id is null then raise exception 'not signed in'; end if;
  if me.banned then raise exception 'account suspended'; end if;
  m := p_method::public.pay_method;
  if m in ('manual','wallet') then raise exception 'invalid method'; end if;
  select * into pl from public.plans where code = p_plan and active;
  if pl.code is null then raise exception 'plan not available'; end if;
  if p_kind not in ('vip','codes') then raise exception 'invalid order kind'; end if;
  agent := me.role in ('agent','admin');

  if p_kind = 'codes' then
    if not agent then raise exception 'agents only'; end if;
    if p_quantity < 1 or p_quantity > 100 then raise exception 'quantity must be 1-100'; end if;
    tgt := me;
  else
    if p_quantity <> 1 then raise exception 'invalid quantity'; end if;
    if p_target_ptv_id is null or p_target_ptv_id = me.ptv_id then
      tgt := me;
    else
      if not agent then raise exception 'only agents can buy VIP for another ID'; end if;
      select * into tgt from public.profiles where ptv_id = p_target_ptv_id;
      if tgt.id is null then raise exception 'PTV ID not found'; end if;
      if tgt.banned then raise exception 'target account suspended'; end if;
    end if;
  end if;

  select count(*) into pending from public.orders where buyer_id = me.id and status in ('pending','review');
  if pending >= coalesce((select value::int from public.settings where key='max_pending_orders'),5) then
    raise exception 'too many unfinished orders, cancel or finish one first';
  end if;

  -- first-purchase agent discount (customers only, once per ID; binds the ID to that agent when paid)
  if nullif(trim(coalesce(p_discount_code,'')),'') is not null and p_kind = 'vip' then
    perform public.check_discount_code(p_discount_code);
    if exists(select 1 from public.orders where buyer_id = me.id and discount_code is not null and status in ('pending','review')) then
      raise exception 'finish or cancel your other discounted order first';
    end if;
    select * into ag from public.profiles where agent_code = upper(trim(p_discount_code));
    pct := public.setting_num('discount_pct',10)::int; dcode := ag.agent_code;
  end if;

  base := case when m = 'promptpay' then (case when agent then pl.agent_price_thb else pl.price_thb end)
               else (case when agent then pl.agent_price_usd else pl.price_usd end) end;
  o.amount   := round(base * p_quantity * (100 - pct) / 100.0, 2);
  o.currency := case when m = 'promptpay' then 'THB' else 'USD' end;
  if m = 'promptpay' then
    select value into pp from public.settings where key = 'promptpay_id';
    o.promptpay_payload := public.promptpay_payload(pp, o.amount);
  end if;

  insert into public.orders(buyer_id,target_id,plan_code,days,amount,currency,method,agent_price,promptpay_payload,kind,quantity,discount_code,discount_pct,commission_agent)
  values (me.id, tgt.id, pl.code, pl.days, o.amount, o.currency, m, agent, o.promptpay_payload, p_kind, p_quantity, dcode, pct,
          case when agent then null when ag.id is not null then ag.id when public.binding_active(me.referred_at) then me.referred_by else null end)
  returning * into o;
  return o;
end $$;

-- ---------------------------------------------------------------- payment completion (replaces v1 body)
create or replace function public.apply_paid_order(p_order uuid, p_capture_id text default null, p_actor uuid default null)
returns public.orders
language plpgsql security definer set search_path = public as $$
declare o public.orders; pl public.plans; buyer public.profiles; agent_price numeric; comm numeric := 0; i int; c text;
begin
  select * into o from public.orders where id = p_order for update;
  if o.id is null then raise exception 'order not found'; end if;
  if o.status = 'paid' then return o; end if;
  if o.status in ('cancelled') and p_capture_id is null then raise exception 'order was cancelled'; end if;
  select * into pl from public.plans where code = o.plan_code;
  select * into buyer from public.profiles where id = o.buyer_id for update;

  if o.kind = 'codes' then
    for i in 1 .. o.quantity loop
      loop
        c := 'PTV-' || public.random_code(4) || '-' || public.random_code(4) || '-' || public.random_code(4);
        exit when not exists(select 1 from public.vip_codes where code = c);
      end loop;
      insert into public.vip_codes(code, plan_code, days, owner_id, order_id) values (c, o.plan_code, o.days, o.buyer_id, o.id);
    end loop;
  else
    update public.profiles set vip_until = greatest(coalesce(vip_until, now()), now()) + make_interval(days => o.days)
     where id = o.target_id;
  end if;

  -- bind customer to agent on first discounted purchase (6-month link; renews an expired link)
  if o.discount_code is not null and o.commission_agent is not null then
    update public.profiles set discount_used = true,
           referred_at = case when referred_by = o.commission_agent and public.binding_active(referred_at) then referred_at else now() end,
           referred_by = case when referred_by is null or not public.binding_active(referred_at) then o.commission_agent else referred_by end
     where id = o.buyer_id;
    select * into buyer from public.profiles where id = o.buyer_id;
  end if;

  -- commission: what the customer paid minus the agent price, credited in THB, only while the link is active
  if o.commission_agent is not null and o.method <> 'wallet'
     and buyer.referred_by = o.commission_agent and public.binding_active(buyer.referred_at) then
    agent_price := case when o.currency = 'THB' then pl.agent_price_thb else pl.agent_price_usd end * o.quantity;
    comm := greatest(o.amount - agent_price, 0);
    if o.currency = 'USD' then comm := comm * public.setting_num('usd_thb_rate', 34.5); end if;
    comm := round(comm, 2);
    if comm > 0 and exists(select 1 from public.profiles where id = o.commission_agent and role in ('agent','admin')) then
      update public.profiles set wallet_thb = wallet_thb + comm where id = o.commission_agent;
      insert into public.wallet_ledger(agent_id, amount_thb, kind, order_id, note)
      values (o.commission_agent, comm, 'commission', o.id, 'ค่าคอม #' || o.order_no || ' จาก ID ' || buyer.ptv_id);
    else
      comm := 0;
    end if;
  end if;

  update public.orders set status = 'paid', paid_at = now(), paypal_capture_id = coalesce(p_capture_id, paypal_capture_id),
         commission_thb = comm,
         reviewed_by = coalesce(p_actor, reviewed_by), reviewed_at = case when p_actor is null then reviewed_at else now() end
   where id = o.id returning * into o;
  insert into public.audit_log(actor_id, action, target_id, detail)
  values (p_actor, 'order_paid', o.target_id, json_build_object('order',o.id,'order_no',o.order_no,'amount',o.amount,'currency',o.currency,
          'method',o.method,'kind',o.kind,'quantity',o.quantity,'commission_thb',comm)::jsonb);
  return o;
end $$;

-- ---------------------------------------------------------------- agent wallet purchases (instant)
create or replace function public.wallet_buy(p_plan text, p_kind text default 'vip', p_target_ptv_id bigint default null, p_quantity int default 1)
returns public.orders
language plpgsql security definer set search_path = public as $$
declare me public.profiles; tgt public.profiles; pl public.plans; o public.orders; cost numeric;
begin
  select * into me from public.profiles where id = auth.uid() for update;
  if me.id is null then raise exception 'not signed in'; end if;
  if me.banned then raise exception 'account suspended'; end if;
  if me.role not in ('agent','admin') then raise exception 'agents only'; end if;
  select * into pl from public.plans where code = p_plan and active;
  if pl.code is null then raise exception 'plan not available'; end if;
  if p_kind = 'codes' then
    if p_quantity < 1 or p_quantity > 100 then raise exception 'quantity must be 1-100'; end if;
    tgt := me;
  elsif p_kind = 'vip' then
    p_quantity := 1;
    if p_target_ptv_id is null then tgt := me;
    else select * into tgt from public.profiles where ptv_id = p_target_ptv_id;
         if tgt.id is null then raise exception 'PTV ID not found'; end if;
         if tgt.banned then raise exception 'target account suspended'; end if;
    end if;
  else raise exception 'invalid order kind'; end if;
  cost := pl.agent_price_thb * p_quantity;
  if me.wallet_thb < cost then raise exception 'not enough wallet balance'; end if;
  update public.profiles set wallet_thb = wallet_thb - cost where id = me.id;
  insert into public.orders(buyer_id,target_id,plan_code,days,amount,currency,method,agent_price,kind,quantity)
  values (me.id, tgt.id, pl.code, pl.days, cost, 'THB', 'wallet', true, p_kind, p_quantity) returning * into o;
  insert into public.wallet_ledger(agent_id, amount_thb, kind, order_id, note)
  values (me.id, -cost, 'spend', o.id, case when p_kind='codes' then 'ซื้อโค้ด VIP x' || p_quantity else 'ส่ง VIP ให้ ID ' || tgt.ptv_id end);
  return public.apply_paid_order(o.id, null, me.id);
end $$;

-- ---------------------------------------------------------------- redeem a VIP code
create or replace function public.redeem_code(p_code text) returns json
language plpgsql security definer set search_path = public as $$
declare me public.profiles; v public.vip_codes; owner public.profiles; until timestamptz;
begin
  select * into me from public.profiles where id = auth.uid();
  if me.id is null then raise exception 'not signed in'; end if;
  if me.banned then raise exception 'account suspended'; end if;
  select * into v from public.vip_codes where code = upper(trim(p_code)) for update;
  if v.code is null then raise exception 'VIP code not found'; end if;
  if v.redeemed_by is not null then raise exception 'VIP code already used'; end if;
  update public.vip_codes set redeemed_by = me.id, redeemed_at = now() where code = v.code;
  update public.profiles set vip_until = greatest(coalesce(vip_until, now()), now()) + make_interval(days => v.days)
   where id = me.id returning vip_until into until;
  select * into owner from public.profiles where id = v.owner_id;
  if (me.referred_by is null or not public.binding_active(me.referred_at)) and owner.role in ('agent','admin') and owner.id <> me.id and me.role = 'user' then
    update public.profiles set referred_by = owner.id, referred_at = now() where id = me.id;
  end if;
  insert into public.audit_log(actor_id, action, target_id, detail)
  values (me.id, 'code_redeemed', me.id, json_build_object('code', v.code, 'days', v.days, 'owner', owner.ptv_id)::jsonb);
  return json_build_object('days', v.days, 'vip_until', until);
end $$;

-- ---------------------------------------------------------------- agent dashboard
create or replace function public.my_agent() returns json
language plpgsql security definer set search_path = public as $$
declare me public.profiles;
begin
  select * into me from public.profiles where id = auth.uid();
  if me.id is null or me.role not in ('agent','admin') then raise exception 'agents only'; end if;
  if me.agent_code is null then perform public.ensure_agent_code(me.id); select * into me from public.profiles where id = me.id; end if;
  return json_build_object(
    'agent_code', me.agent_code,
    'wallet_thb', me.wallet_thb,
    'discount_pct', public.setting_num('discount_pct',10)::int,
    'clicks', (select count(*) from public.download_clicks where agent_id = me.id),
    'customers', coalesce((select json_agg(x order by x.referred_at desc) from (
        select c.ptv_id, c.display_name, c.avatar_url, c.referred_at, c.vip_until,
               c.referred_at + make_interval(months => public.setting_num('commission_months',6)::int) as linked_until,
               public.binding_active(c.referred_at) as active,
               (select coalesce(sum(commission_thb),0) from public.orders o where o.buyer_id = c.id and o.commission_agent = me.id and o.status='paid') as earned_thb
        from public.profiles c where c.referred_by = me.id limit 500) x), '[]'::json),
    'ledger', coalesce((select json_agg(l order by l.created_at desc) from (
        select amount_thb, kind, note, created_at from public.wallet_ledger where agent_id = me.id order by created_at desc limit 100) l), '[]'::json),
    'codes', coalesce((select json_agg(v order by v.created_at desc) from (
        select vc.code, vc.plan_code, vc.days, vc.created_at, vc.redeemed_at, r.ptv_id as redeemed_ptv_id, r.display_name as redeemed_name
        from public.vip_codes vc left join public.profiles r on r.id = vc.redeemed_by
        where vc.owner_id = me.id order by vc.created_at desc limit 500) v), '[]'::json),
    'withdrawals', coalesce((select json_agg(w order by w.created_at desc) from (
        select id, amount_thb, method, account, status, admin_note, created_at, processed_at
        from public.withdrawals where agent_id = me.id order by created_at desc limit 50) w), '[]'::json),
    'min_withdrawal_thb', public.setting_num('min_withdrawal_thb',100),
    'commission_months', public.setting_num('commission_months',6)::int,
    'usd_thb_rate', public.setting_num('usd_thb_rate',34.5));
end $$;

create or replace function public.request_withdrawal(p_amount numeric, p_method text, p_account text) returns void
language plpgsql security definer set search_path = public as $$
declare me public.profiles; w uuid;
begin
  select * into me from public.profiles where id = auth.uid() for update;
  if me.id is null or me.role not in ('agent','admin') then raise exception 'agents only'; end if;
  if me.banned then raise exception 'account suspended'; end if;
  p_amount := round(p_amount, 2);
  if p_amount < public.setting_num('min_withdrawal_thb',100) then raise exception 'amount is below the minimum withdrawal'; end if;
  if p_amount > me.wallet_thb then raise exception 'not enough wallet balance'; end if;
  if exists(select 1 from public.withdrawals where agent_id = me.id and status = 'requested') then raise exception 'you already have a pending withdrawal'; end if;
  update public.profiles set wallet_thb = wallet_thb - p_amount where id = me.id;
  insert into public.withdrawals(agent_id, amount_thb, method, account) values (me.id, p_amount, p_method, trim(p_account)) returning id into w;
  insert into public.wallet_ledger(agent_id, amount_thb, kind, note) values (me.id, -p_amount, 'withdraw', 'ขอถอนเงิน');
end $$;

-- my_account v2: adds agent link + wallet
create or replace function public.my_account() returns json
language plpgsql security definer set search_path = public as $$
declare p public.profiles; em text; ag public.profiles;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  update public.profiles set last_seen = now() where id = auth.uid() returning * into p;
  if p.id is null then raise exception 'profile missing'; end if;
  select email into em from auth.users where id = auth.uid();
  select * into ag from public.profiles where id = p.referred_by;
  return json_build_object('id',p.id,'ptv_id',p.ptv_id,'display_name',p.display_name,'avatar_url',p.avatar_url,
    'provider',p.provider,'role',p.role,'vip_until',p.vip_until,'is_vip',(p.vip_until is not null and p.vip_until > now()) or p.role='admin',
    'banned',p.banned,'email',em,'server_time',now(),
    'agent_code',p.agent_code,'wallet_thb',p.wallet_thb,'discount_used',p.discount_used,
    'agent_name',case when public.binding_active(p.referred_at) then ag.display_name end,
    'agent_ptv_id',case when public.binding_active(p.referred_at) then ag.ptv_id end,
    'agent_until',p.referred_at + make_interval(months => public.setting_num('commission_months',6)::int),
    'discount_pct',public.setting_num('discount_pct',10)::int,'usd_thb_rate',public.setting_num('usd_thb_rate',34.5));
end $$;

-- ---------------------------------------------------------------- admin
create or replace function public.admin_set_role(p_user uuid, p_role text) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  if p_user = auth.uid() and p_role <> 'admin' then raise exception 'you cannot remove your own admin role'; end if;
  update public.profiles set role = p_role::public.user_role where id = p_user;
  if p_role in ('agent','admin') then perform public.ensure_agent_code(p_user); end if;
  perform public.log_action('set_role', p_user, json_build_object('role',p_role)::jsonb);
end $$;

create or replace function public.admin_list_withdrawals(p_status text default 'requested') returns setof json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return query select json_build_object('id',w.id,'amount_thb',w.amount_thb,'method',w.method,'account',w.account,'status',w.status,
      'admin_note',w.admin_note,'created_at',w.created_at,'processed_at',w.processed_at,
      'agent_ptv_id',a.ptv_id,'agent_name',a.display_name,'wallet_thb',a.wallet_thb)
    from public.withdrawals w join public.profiles a on a.id = w.agent_id
    where coalesce(p_status,'') = '' or w.status = p_status order by w.created_at desc limit 200;
end $$;

create or replace function public.admin_process_withdrawal(p_id uuid, p_paid boolean, p_note text) returns void
language plpgsql security definer set search_path = public as $$
declare w public.withdrawals;
begin
  perform public.require_admin();
  select * into w from public.withdrawals where id = p_id for update;
  if w.id is null or w.status <> 'requested' then raise exception 'withdrawal is not pending'; end if;
  update public.withdrawals set status = case when p_paid then 'paid' else 'rejected' end, admin_note = left(p_note,300),
         processed_at = now(), processed_by = auth.uid() where id = p_id;
  if not p_paid then
    update public.profiles set wallet_thb = wallet_thb + w.amount_thb where id = w.agent_id;
    insert into public.wallet_ledger(agent_id, amount_thb, kind, note) values (w.agent_id, w.amount_thb, 'withdraw_refund', 'คืนเงิน: ' || coalesce(p_note,''));
  end if;
  perform public.log_action(case when p_paid then 'withdrawal_paid' else 'withdrawal_rejected' end, w.agent_id,
          json_build_object('amount_thb', w.amount_thb)::jsonb);
end $$;

-- admin adjusts a wallet (bonus or correction); keeps the ledger honest
create or replace function public.admin_adjust_wallet(p_user uuid, p_amount numeric, p_note text) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  update public.profiles set wallet_thb = wallet_thb + round(p_amount,2) where id = p_user;
  insert into public.wallet_ledger(agent_id, amount_thb, kind, note) values (p_user, round(p_amount,2), 'adjust', coalesce(p_note,'แอดมินปรับยอด'));
  perform public.log_action('adjust_wallet', p_user, json_build_object('amount_thb', p_amount)::jsonb);
end $$;

-- link/unlink a customer to an agent (admin fixes mistakes)
create or replace function public.admin_set_referrer(p_user uuid, p_agent_ptv_id bigint) returns void
language plpgsql security definer set search_path = public as $$
declare ag uuid;
begin
  perform public.require_admin();
  if p_agent_ptv_id is not null then
    select id into ag from public.profiles where ptv_id = p_agent_ptv_id and role in ('agent','admin');
    if ag is null then raise exception 'agent not found'; end if;
  end if;
  update public.profiles set referred_by = ag, referred_at = case when ag is null then null else now() end where id = p_user;
  perform public.log_action('set_referrer', p_user, json_build_object('agent_ptv_id', p_agent_ptv_id)::jsonb);
end $$;

-- admin user list v2 (adds agent info)
create or replace function public.admin_list_users(p_search text default '', p_limit int default 50, p_offset int default 0)
returns setof json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return query
  select json_build_object('id',p.id,'ptv_id',p.ptv_id,'display_name',p.display_name,'avatar_url',p.avatar_url,'provider',p.provider,
           'role',p.role,'vip_until',p.vip_until,'banned',p.banned,'created_at',p.created_at,'last_seen',p.last_seen,'email',u.email,
           'agent_code',p.agent_code,'wallet_thb',p.wallet_thb,'agent_name',ag.display_name,'agent_ptv_id',ag.ptv_id,
           'customers',(select count(*) from public.profiles c where c.referred_by = p.id))
  from public.profiles p left join auth.users u on u.id = p.id left join public.profiles ag on ag.id = p.referred_by
  where coalesce(p_search,'') = '' or p.ptv_id::text = trim(p_search) or p.display_name ilike '%'||p_search||'%'
        or u.email ilike '%'||p_search||'%' or p.agent_code = upper(trim(p_search))
  order by p.created_at desc limit least(greatest(p_limit,1),200) offset greatest(p_offset,0);
end $$;

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
    'withdrawals', (select count(*) from public.withdrawals where status = 'requested'),
    'wallets_thb', (select coalesce(sum(wallet_thb),0) from public.profiles),
    'revenue_usd_30d', (select coalesce(sum(amount),0) from public.orders where status='paid' and currency='USD' and method <> 'wallet' and paid_at > now() - interval '30 days'),
    'revenue_thb_30d', (select coalesce(sum(amount),0) from public.orders where status='paid' and currency='THB' and method <> 'wallet' and paid_at > now() - interval '30 days'),
    'new_users_7d', (select count(*) from public.profiles where created_at > now() - interval '7 days'));
end $$;

-- download link: record a click for the agent (called by the dl edge function with the service role)
create or replace function public.agent_download_click(p_code text) returns text
language plpgsql security definer set search_path = public as $$
declare ag uuid;
begin
  select id into ag from public.profiles where agent_code = upper(trim(coalesce(p_code,''))) and role in ('agent','admin');
  if ag is not null then insert into public.download_clicks(agent_id) values (ag); end if;
  return (select value from public.settings where key = 'download_url');
end $$;

-- order lists v2 (kind, quantity, discount, commission)
create or replace function public.my_orders() returns setof json
language sql stable security definer set search_path = public as $$
  select json_build_object('id',o.id,'order_no',o.order_no,'plan_code',o.plan_code,'days',o.days,'amount',o.amount,'currency',o.currency,
    'method',o.method,'status',o.status,'created_at',o.created_at,'paid_at',o.paid_at,'note',o.note,'promptpay_payload',o.promptpay_payload,
    'target_ptv_id',t.ptv_id,'target_name',t.display_name,'kind',o.kind,'quantity',o.quantity,'discount_pct',o.discount_pct)
  from public.orders o join public.profiles t on t.id = o.target_id
  where o.buyer_id = auth.uid() order by o.created_at desc limit 100
$$;

create or replace function public.admin_list_orders(p_status text default null, p_limit int default 50, p_offset int default 0)
returns setof json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return query
  select json_build_object('id',o.id,'order_no',o.order_no,'plan_code',o.plan_code,'days',o.days,'amount',o.amount,'currency',o.currency,
           'method',o.method,'status',o.status,'agent_price',o.agent_price,'slip_path',o.slip_path,'paypal_order_id',o.paypal_order_id,
           'created_at',o.created_at,'paid_at',o.paid_at,'note',o.note,'kind',o.kind,'quantity',o.quantity,
           'discount_code',o.discount_code,'discount_pct',o.discount_pct,'commission_thb',o.commission_thb,'commission_agent_name',ca.display_name,
           'buyer_ptv_id',b.ptv_id,'buyer_name',b.display_name,'target_ptv_id',t.ptv_id,'target_name',t.display_name)
  from public.orders o join public.profiles b on b.id = o.buyer_id join public.profiles t on t.id = o.target_id
       left join public.profiles ca on ca.id = o.commission_agent
  where p_status is null or p_status = '' or o.status::text = p_status
  order by o.created_at desc limit least(greatest(p_limit,1),200) offset greatest(p_offset,0);
end $$;

-- ---------------------------------------------------------------- grants
grant execute on all functions in schema public to anon, authenticated, service_role;
revoke execute on function public.apply_paid_order(uuid,text,uuid) from public, anon, authenticated;
revoke execute on function public.ensure_agent_code(uuid) from public, anon, authenticated;
revoke execute on function public.agent_download_click(text) from public, anon, authenticated;
revoke execute on function public.log_action(text,uuid,jsonb) from public, anon, authenticated;
revoke execute on function public.new_ptv_id() from public, anon, authenticated;
revoke execute on function public.handle_new_user() from public, anon, authenticated;
grant  execute on function public.apply_paid_order(uuid,text,uuid) to service_role;
grant  execute on function public.agent_download_click(text) to service_role;
