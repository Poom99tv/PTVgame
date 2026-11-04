-- Game To Earn settings in the cloud: the same presets / state the Launcher uses, so they can be
-- edited on the website and follow the account to every computer. Only the owner can read or write.

create table if not exists public.gte_profiles (
  user_id    uuid primary key references public.profiles(id) on delete cascade,
  presets    jsonb not null default '[]'::jsonb,
  state      jsonb not null default '{}'::jsonb,
  updated_at timestamptz not null default now(),
  updated_by text not null default '' check (char_length(updated_by) <= 20)
);
alter table public.gte_profiles enable row level security;   -- no policies: functions only
revoke all on public.gte_profiles from anon, authenticated;
grant all on public.gte_profiles to service_role;

create or replace function public.gte_get() returns json language plpgsql stable security definer set search_path = public as $$
declare r public.gte_profiles;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  select * into r from public.gte_profiles where user_id = auth.uid();
  if not found then return null; end if;
  return json_build_object('presets', r.presets, 'state', r.state, 'updated_at', r.updated_at, 'updated_by', r.updated_by);
end $$;

create or replace function public.gte_save(p_presets jsonb, p_state jsonb, p_source text) returns json
language plpgsql security definer set search_path = public as $$
declare t timestamptz := now();
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  if jsonb_typeof(p_presets) <> 'array' or jsonb_array_length(p_presets) < 1 or jsonb_array_length(p_presets) > 50 then raise exception 'presets must be a list of 1-50 items'; end if;
  if p_state is not null and jsonb_typeof(p_state) <> 'object' then raise exception 'invalid state'; end if;
  if pg_column_size(p_presets) + coalesce(pg_column_size(p_state),0) > 262144 then raise exception 'settings too large'; end if;
  insert into public.gte_profiles(user_id, presets, state, updated_at, updated_by)
  values (auth.uid(), p_presets, coalesce(p_state,'{}'::jsonb), t, left(coalesce(p_source,''),20))
  on conflict (user_id) do update set presets = excluded.presets, state = excluded.state, updated_at = t, updated_by = excluded.updated_by;
  return json_build_object('updated_at', t);
end $$;

revoke execute on function public.gte_get() from public, anon;
revoke execute on function public.gte_save(jsonb,jsonb,text) from public, anon;
grant  execute on function public.gte_get() to authenticated;
grant  execute on function public.gte_save(jsonb,jsonb,text) to authenticated;
