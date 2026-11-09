\set ON_ERROR_STOP 1
insert into auth.users values ('00000000-0000-0000-0000-0000000000d1','v1@x.com','{"name":"V"}','{"provider":"email"}'),('00000000-0000-0000-0000-0000000000d2','f1@x.com','{"name":"F"}','{"provider":"email"}');
insert into public.game_access(user_id, game_id, until) values ('00000000-0000-0000-0000-0000000000d1', 'zombierush', now() + interval '3 days');
create function pg_temp.as_user(uid text) returns void language sql as $$ select set_config('request.jwt.claim.sub', uid, false) $$;
create function pg_temp.expect_error(q text, msg text) returns void language plpgsql as $$
begin execute q; raise exception 'EXPECTED ERROR: %', msg; exception when others then
  if sqlerrm like 'EXPECTED ERROR%' then raise; end if; raise notice 'ok blocked (%): %', msg, sqlerrm; end $$;
create function pg_temp.check(ok boolean, msg text) returns void language plpgsql as $$
begin if not ok then raise exception 'CHECK FAILED: %', msg; end if; raise notice 'ok %', msg; end $$;
select pg_temp.as_user(''); set role anon;
select pg_temp.expect_error($q$select public.play_ticket_claims('zombierush')$q$, 'anon');
select pg_temp.expect_error('select * from public.signing_keys', 'anon cannot read keys');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000d1'); set role authenticated;
select pg_temp.check((public.play_ticket_claims('zombierush')->>'game')='zombierush', 'vip gets claims');
select pg_temp.expect_error($q$select public.play_ticket_claims('../x')$q$, 'bad game id');
select pg_temp.expect_error('select * from public.signing_keys', 'user cannot read keys');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000d2'); set role authenticated;
select pg_temp.expect_error($q$select public.play_ticket_claims('zombierush')$q$, 'free user needs VIP');
reset role;
update public.profiles set role='admin' where id='00000000-0000-0000-0000-0000000000d2';
select pg_temp.as_user('00000000-0000-0000-0000-0000000000d2'); set role authenticated;
select pg_temp.check((public.play_ticket_claims('zombierush')->>'admin')::boolean, 'admin plays');
reset role;
select 'ALL PLAY TICKET TESTS PASSED';
