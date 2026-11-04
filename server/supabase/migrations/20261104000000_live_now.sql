-- "กำลังไลฟ์ตอนนี้" for the PTVgame website.
-- The Launcher (Game To Earn) sends a heartbeat every minute while it is connected to a TikTok LIVE,
-- the website lists sessions seen in the last 3 minutes. Streamers can switch it off in the Launcher.

create table if not exists public.live_sessions (
  user_id         uuid primary key references public.profiles(id) on delete cascade,
  tiktok_username text not null check (tiktok_username ~ '^[A-Za-z0-9_.]{2,24}$'),
  display_name    text not null default '' check (char_length(display_name) <= 60),
  avatar_url      text check (avatar_url is null or (avatar_url ~ '^https://' and char_length(avatar_url) < 1200)),
  game            text not null default '' check (char_length(game) <= 60),
  title           text not null default '' check (char_length(title) <= 120),
  viewers         int  not null default 0 check (viewers between 0 and 10000000),
  started_at      timestamptz not null default now(),
  last_seen       timestamptz not null default now()
);
create index if not exists live_sessions_last_seen on public.live_sessions(last_seen desc);

alter table public.live_sessions enable row level security;   -- no policies: only through the functions below
revoke all on public.live_sessions from anon, authenticated;
grant all on public.live_sessions to service_role;

-- heartbeat from the signed-in Launcher (call again every ~60 s while live)
create or replace function public.live_heartbeat(p_username text, p_display text, p_avatar text, p_game text, p_title text, p_viewers int)
returns json language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); u text := ltrim(coalesce(p_username,''),'@'); av text := nullif(trim(coalesce(p_avatar,'')),'');
begin
  if uid is null then raise exception 'not signed in'; end if;
  if exists(select 1 from public.profiles where id = uid and banned) then raise exception 'account banned'; end if;
  if u !~ '^[A-Za-z0-9_.]{2,24}$' then raise exception 'invalid TikTok username'; end if;
  if av is not null and (av !~ '^https://' or char_length(av) >= 1200) then av := null; end if;
  insert into public.live_sessions(user_id, tiktok_username, display_name, avatar_url, game, title, viewers, started_at, last_seen)
  values (uid, u, left(coalesce(p_display,''),60), av, left(coalesce(p_game,''),60), left(coalesce(p_title,''),120), greatest(0, least(coalesce(p_viewers,0), 10000000)), now(), now())
  on conflict (user_id) do update set
    started_at      = case when live_sessions.tiktok_username <> excluded.tiktok_username or live_sessions.last_seen < now() - interval '3 minutes' then now() else live_sessions.started_at end,
    tiktok_username = excluded.tiktok_username, display_name = excluded.display_name,
    avatar_url      = coalesce(excluded.avatar_url, live_sessions.avatar_url),
    game = excluded.game, title = excluded.title, viewers = excluded.viewers, last_seen = now();
  return json_build_object('ok', true);
end $$;

-- LIVE ended / disconnected / streamer turned the website listing off
create or replace function public.live_stop() returns void language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then return; end if;
  delete from public.live_sessions where user_id = auth.uid();
end $$;

-- public list for the website (anon): only what is already public on TikTok
create or replace function public.live_now() returns table(tiktok_username text, display_name text, avatar_url text, game text, title text, viewers int, started_at timestamptz, vip boolean)
language sql stable security definer set search_path = public as $$
  select s.tiktok_username, s.display_name, s.avatar_url, s.game, s.title, s.viewers, s.started_at,
         (p.role = 'admin' or (p.vip_until is not null and p.vip_until > now())) as vip
  from public.live_sessions s join public.profiles p on p.id = s.user_id
  where s.last_seen > now() - interval '3 minutes' and not p.banned
  order by s.viewers desc, s.started_at asc
  limit 60;
$$;

-- public numbers for the website
create or replace function public.site_stats() returns json language sql stable security definer set search_path = public as $$
  select json_build_object(
    'players', (select count(*) from public.profiles),
    'live',    (select count(*) from public.live_sessions s join public.profiles p on p.id = s.user_id where s.last_seen > now() - interval '3 minutes' and not p.banned));
$$;

revoke execute on function public.live_heartbeat(text,text,text,text,text,int) from public, anon;
revoke execute on function public.live_stop() from public, anon;
grant  execute on function public.live_heartbeat(text,text,text,text,text,int) to authenticated;
grant  execute on function public.live_stop() to authenticated;
grant  execute on function public.live_now() to anon, authenticated;
grant  execute on function public.site_stats() to anon, authenticated;
