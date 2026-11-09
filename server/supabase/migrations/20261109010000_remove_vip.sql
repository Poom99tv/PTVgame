-- Remove the all-games VIP pass completely; everything is sold per game.
-- * VIP time people still have is turned into the same time on every game, so nobody loses anything
-- * VIP codes or VIP plans that still exist give their days on every game instead of VIP
-- * admin "+VIP" is replaced by "+game" (admin_grant_game); old launchers get a clear message
-- * agents, wallets, codes and commissions keep working, now per game

-- 1. VIP time left -> the same end time on every game on sale
insert into public.game_access(user_id, game_id, until)
select p.id, pl.game_id, p.vip_until
  from public.profiles p cross join public.plans pl
 where p.vip_until > now() and pl.game_id is not null and pl.active
on conflict (user_id, game_id) do update set until = greatest(public.game_access.until, excluded.until);
update public.profiles set vip_until = null where vip_until is not null;
update public.plans set active = false where game_id is null;

-- 2. a plan without a game (old VIP plans / VIP codes) now adds its days to every game on sale
create or replace function public.grant_plan_days(p_user uuid, p_plan text, p_days int) returns timestamptz
language plpgsql security definer set search_path = public as $$
declare g text; t timestamptz;
begin
  select game_id into g from public.plans where code = p_plan;
  if g is null then
    insert into public.game_access(user_id, game_id, until)
    select p_user, pl.game_id, now() + make_interval(days => p_days) from public.plans pl where pl.game_id is not null and pl.active
    on conflict (user_id, game_id) do update set until = greatest(public.game_access.until, now()) + make_interval(days => p_days);
    select max(until) into t from public.game_access where user_id = p_user;
  else
    insert into public.game_access(user_id, game_id, until) values (p_user, g, now() + make_interval(days => p_days))
    on conflict (user_id, game_id) do update set until = greatest(public.game_access.until, now()) + make_interval(days => p_days)
    returning until into t;
  end if;
  return t;
end $$;

-- 3. no more VIP grants by hand
create or replace function public.admin_grant_vip(p_user uuid, p_days int) returns void
language plpgsql security definer set search_path = public as $$
begin
  perform public.require_admin();
  raise exception 'VIP ถูกยกเลิกแล้ว ใช้ปุ่ม "+ เกม" เพื่อให้วันเล่นทีละเกม (อัปเดต Launcher เป็นเวอร์ชันล่าสุด)';
end $$;

-- 4. play tickets: only the bought game (or admin)
create or replace function public.play_ticket_claims(p_game text) returns json
language plpgsql stable security definer set search_path = public as $$
declare p public.profiles; g timestamptz;
begin
  if auth.uid() is null then raise exception 'sign in required'; end if;
  if p_game is null or p_game !~ '^[a-z0-9][a-z0-9_-]{0,39}$' then raise exception 'invalid game'; end if;
  select * into p from public.profiles where id = auth.uid();
  if p.id is null then raise exception 'account not found'; end if;
  if p.banned then raise exception 'account suspended'; end if;
  select a.until into g from public.game_access a where a.user_id = p.id and a.game_id = p_game and a.until > now();
  if p.role <> 'admin' and g is null then raise exception 'game not purchased'; end if;
  -- vip_until carries "plays this game until", which is what the games check
  return json_build_object('game', p_game, 'ptv_id', p.ptv_id, 'name', p.display_name, 'role', p.role, 'vip_until', g, 'admin', p.role = 'admin');
end $$;

-- 5. admin overview counts players who own a game instead of VIP
create or replace function public.admin_stats() returns json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return json_build_object(
    'users',  (select count(*) from public.profiles),
    'vip',    (select count(distinct user_id) from public.game_access where until > now()),   -- players with a game (key kept for old clients)
    'buyers', (select count(distinct user_id) from public.game_access where until > now()),
    'games_owned', (select count(*) from public.game_access where until > now()),
    'agents', (select count(*) from public.profiles where role = 'agent'),
    'review', (select count(*) from public.orders where status = 'review'),
    'pending',(select count(*) from public.orders where status = 'pending'),
    'withdrawals', (select count(*) from public.withdrawals where status = 'requested'),
    'wallets_thb', (select coalesce(sum(wallet_thb),0) from public.profiles),
    'revenue_usd_30d', (select coalesce(sum(amount),0) from public.orders where status='paid' and currency='USD' and method <> 'wallet' and paid_at > now() - interval '30 days'),
    'revenue_thb_30d', (select coalesce(sum(amount),0) from public.orders where status='paid' and currency='THB' and method <> 'wallet' and paid_at > now() - interval '30 days'),
    'new_users_7d', (select count(*) from public.profiles where created_at > now() - interval '7 days'));
end $$;

-- 6. games owned by many users at once (admin user list)
create or replace function public.admin_users_games(p_users uuid[]) returns json
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_admin();
  return coalesce((select json_object_agg(user_id, games) from (
    select user_id, json_object_agg(game_id, until) games from public.game_access
     where user_id = any(p_users) and until > now() group by user_id) x), '{}'::json);
end $$;
revoke execute on function public.admin_users_games(uuid[]) from public, anon;
grant execute on function public.admin_users_games(uuid[]) to authenticated;

-- 7. what a player owns, readable for the agent dashboard / launcher (unchanged shape)
create or replace function public.my_games() returns json
language sql stable security definer set search_path = public as $$
  select coalesce(json_object_agg(game_id, until), '{}'::json) from public.game_access
   where user_id = auth.uid() and until > now()
$$;
