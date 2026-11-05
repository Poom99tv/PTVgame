-- Play tickets: the Launcher asks the play-ticket Edge Function for a short-lived, signed ticket before it
-- starts a VIP game, and the game checks the signature with the public key built into it. This file holds
-- the claims check (run as the signed-in user) and the private table the function keeps its key pair in.

create table if not exists public.signing_keys (
  id          text primary key,
  private_jwk jsonb not null,
  public_jwk  jsonb not null,
  created_at  timestamptz not null default now()
);
alter table public.signing_keys enable row level security;   -- no policies: only the service role reads it
revoke all on public.signing_keys from anon, authenticated;
grant all on public.signing_keys to service_role;

-- Who is asking and may they play this game? Raises unless the account is VIP (or admin) and not suspended.
create or replace function public.play_ticket_claims(p_game text) returns json
language plpgsql stable security definer set search_path = public as $$
declare p public.profiles;
begin
  if auth.uid() is null then raise exception 'sign in required'; end if;
  if p_game is null or p_game !~ '^[a-z0-9][a-z0-9_-]{0,39}$' then raise exception 'invalid game'; end if;
  select * into p from public.profiles where id = auth.uid();
  if p.id is null then raise exception 'account not found'; end if;
  if p.banned then raise exception 'account suspended'; end if;
  if p.role <> 'admin' and (p.vip_until is null or p.vip_until <= now()) then raise exception 'VIP required'; end if;
  return json_build_object('game', p_game, 'ptv_id', p.ptv_id, 'name', p.display_name, 'role', p.role,
    'vip_until', p.vip_until, 'admin', p.role = 'admin');
end $$;
revoke execute on function public.play_ticket_claims(text) from public, anon;
grant execute on function public.play_ticket_claims(text) to authenticated;
