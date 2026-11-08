-- Sell each game on its own instead of one VIP pass for everything.
-- * every game is a row in public.plans with game_id set (code g_<game id>), so orders, PromptPay, PayPal,
--   agent codes, wallet purchases and commissions keep working unchanged
-- * buying a game adds days to public.game_access for that game; a VIP plan (game_id null) still adds VIP
--   time, and VIP that people already have keeps opening every game until it runs out
-- * the old monthly / yearly VIP plans are switched off for new sales
-- * admin can change each game's name, days and prices (admin_update_plan) and give days per game

alter table public.plans add column if not exists game_id text unique;

create table if not exists public.game_access (
  user_id uuid not null references public.profiles(id) on delete cascade,
  game_id text not null,
  until   timestamptz not null,
  primary key (user_id, game_id)
);
alter table public.game_access enable row level security;   -- no policies: read through my_games()
revoke all on public.game_access from anon, authenticated;
grant all on public.game_access to service_role;

-- Prices from the owner (THB / USD, 30 days). Agent prices start at 75% like the old VIP plan; change them in Admin.
insert into public.plans(code, name, days, price_usd, list_price_usd, agent_price_usd, price_thb, agent_price_thb, sort, game_id) values
  ('g_roblox-box-1', 'Per Fun Box', 30, 25.00, null, 19.00, 690.00, 520.00, 10, 'roblox-box-1'),
  ('g_roblox-unnamed', 'Fun Ladder', 30, 20.00, null, 15.00, 650.00, 490.00, 11, 'roblox-unnamed'),
  ('g_roblox-pty-jump', 'PTY jump', 30, 25.00, null, 19.00, 690.00, 520.00, 12, 'roblox-pty-jump'),
  ('g_roblox-box-2', 'Gear Box', 30, 10.00, null, 8.00, 320.00, 240.00, 13, 'roblox-box-2'),
  ('g_ptvcar', 'PTVCar', 30, 20.00, null, 15.00, 650.00, 490.00, 14, 'ptvcar'),
  ('g_sumo', 'PTVvs', 30, 25.00, null, 19.00, 690.00, 520.00, 15, 'sumo'),
  ('g_paintworld', 'Paint World', 30, 20.00, null, 15.00, 650.00, 490.00, 16, 'paintworld'),
  ('g_ptvboat', 'PTVBoat', 30, 25.00, null, 19.00, 690.00, 520.00, 17, 'ptvboat'),
  ('g_ptvegg', 'Acorn Rush', 30, 25.00, null, 19.00, 690.00, 520.00, 18, 'ptvegg'),
  ('g_zombierush', 'Zombie Rush', 30, 25.00, null, 19.00, 690.00, 520.00, 19, 'zombierush'),
  ('g_ptvup', 'PTVUP', 30, 25.00, null, 19.00, 690.00, 520.00, 20, 'ptvup'),
  ('g_ptvrunner', 'PTVRunner', 30, 25.00, null, 19.00, 690.00, 520.00, 21, 'ptvrunner')
on conflict (code) do update set name = excluded.name, price_usd = excluded.price_usd, price_thb = excluded.price_thb,
  agent_price_usd = excluded.agent_price_usd, agent_price_thb = excluded.agent_price_thb, game_id = excluded.game_id, active = true;
update public.plans set active = false where game_id is null;   -- stop selling the all-games VIP pass

-- Add days of a plan to a user: a game plan extends that game, a VIP plan extends VIP. Returns the new end time.
create or replace function public.grant_plan_days(p_user uuid, p_plan text, p_days int) returns timestamptz
language plpgsql security definer set search_path = public as $$
declare g text; t timestamptz;
begin
  select game_id into g from public.plans where code = p_plan;
  if g is null then
    update public.profiles set vip_until = greatest(coalesce(vip_until, now()), now()) + make_interval(days => p_days)
     where id = p_user returning vip_until into t;
  else
    insert into public.game_access(user_id, game_id, until) values (p_user, g, now() + make_interval(days => p_days))
    on conflict (user_id, game_id) do update set until = greatest(public.game_access.until, now()) + make_interval(days => p_days)
    returning until into t;
  end if;
  return t;
end $$;
revoke execute on function public.grant_plan_days(uuid, text, int) from public, anon, authenticated;

-- ---------------------------------------------------------------- payment completion (game aware)
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
    perform public.grant_plan_days(o.target_id, o.plan_code, o.days);
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

-- ---------------------------------------------------------------- redeem a code (VIP or a game)
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
  until := public.grant_plan_days(me.id, v.plan_code, v.days);
  select * into owner from public.profiles where id = v.owner_id;
  if (me.referred_by is null or not public.binding_active(me.referred_at)) and owner.role in ('agent','admin') and owner.id <> me.id and me.role = 'user' then
    update public.profiles set referred_by = owner.id, referred_at = now() where id = me.id;
  end if;
  insert into public.audit_log(actor_id, action, target_id, detail)
  values (me.id, 'code_redeemed', me.id, json_build_object('code', v.code, 'days', v.days, 'owner', owner.ptv_id)::jsonb);
  return json_build_object('days', v.days, 'vip_until', until, 'game_id', (select game_id from public.plans where code = v.plan_code),
    'game_name', (select name from public.plans where code = v.plan_code));
end $$;

-- ---------------------------------------------------------------- what the signed-in player owns
create or replace function public.my_games() returns json
language sql stable security definer set search_path = public as $$
  select coalesce(json_object_agg(game_id, until), '{}'::json) from public.game_access
   where user_id = auth.uid() and until > now()
$$;
revoke execute on function public.my_games() from public, anon;
grant execute on function public.my_games() to authenticated;

-- ---------------------------------------------------------------- play tickets now check the game too
create or replace function public.play_ticket_claims(p_game text) returns json
language plpgsql stable security definer set search_path = public as $$
declare p public.profiles; g timestamptz; until timestamptz;
begin
  if auth.uid() is null then raise exception 'sign in required'; end if;
  if p_game is null or p_game !~ '^[a-z0-9][a-z0-9_-]{0,39}$' then raise exception 'invalid game'; end if;
  select * into p from public.profiles where id = auth.uid();
  if p.id is null then raise exception 'account not found'; end if;
  if p.banned then raise exception 'account suspended'; end if;
  select a.until into g from public.game_access a where a.user_id = p.id and a.game_id = p_game;
  until := greatest(case when p.vip_until > now() then p.vip_until end, case when g > now() then g end);
  if p.role <> 'admin' and until is null then raise exception 'game not purchased'; end if;
  -- vip_until carries "plays this game until", which is what games check
  return json_build_object('game', p_game, 'ptv_id', p.ptv_id, 'name', p.display_name, 'role', p.role,
    'vip_until', until, 'admin', p.role = 'admin');
end $$;

-- ---------------------------------------------------------------- admin: prices, days, names, game time
drop function if exists public.admin_update_plan(text, numeric, numeric, numeric, numeric, numeric, boolean);
create or replace function public.admin_update_plan(p_code text, p_price_usd numeric, p_list_price_usd numeric, p_agent_price_usd numeric,
  p_price_thb numeric, p_agent_price_thb numeric, p_active boolean, p_days int default null, p_name text default null) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  if p_days is not null and (p_days < 1 or p_days > 3650) then raise exception 'days must be 1-3650'; end if;
  update public.plans set price_usd=p_price_usd, list_price_usd=p_list_price_usd, agent_price_usd=p_agent_price_usd,
         price_thb=p_price_thb, agent_price_thb=p_agent_price_thb, active=p_active,
         days=coalesce(p_days, days), name=coalesce(nullif(trim(p_name),''), name) where code = p_code;
  if not found then raise exception 'plan not found'; end if;
  perform public.log_action('update_plan', null, json_build_object('plan',p_code,'usd',p_price_usd,'agent_usd',p_agent_price_usd,'thb',p_price_thb,
    'agent_thb',p_agent_price_thb,'days',p_days,'active',p_active)::jsonb);
end $$;

create or replace function public.admin_grant_game(p_user uuid, p_game text, p_days int) returns timestamptz
language plpgsql security definer set search_path = public as $$
declare t timestamptz;
begin
  perform public.require_admin();
  if p_days is null or p_days = 0 or abs(p_days) > 3650 then raise exception 'days must be 1-3650 (negative to remove)'; end if;
  if not exists(select 1 from public.plans where game_id = p_game) then raise exception 'game not found'; end if;
  insert into public.game_access(user_id, game_id, until) values (p_user, p_game, now() + make_interval(days => p_days))
  on conflict (user_id, game_id) do update set until = greatest(public.game_access.until, now()) + make_interval(days => p_days)
  returning until into t;
  perform public.log_action('grant_game', p_user, json_build_object('game',p_game,'days',p_days)::jsonb);
  return t;
end $$;

create or replace function public.admin_user_games(p_user uuid) returns json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return coalesce((select json_object_agg(game_id, until) from public.game_access where user_id = p_user and until > now()), '{}'::json);
end $$;

revoke execute on function public.admin_grant_game(uuid, text, int) from public, anon;
revoke execute on function public.admin_user_games(uuid) from public, anon;
grant execute on function public.admin_update_plan(text, numeric, numeric, numeric, numeric, numeric, boolean, int, text) to authenticated;
grant execute on function public.admin_grant_game(uuid, text, int) to authenticated;
grant execute on function public.admin_user_games(uuid) to authenticated;
grant execute on function public.grant_plan_days(uuid, text, int) to service_role;
