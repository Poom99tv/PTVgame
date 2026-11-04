\set ON_ERROR_STOP 1
insert into auth.users values
 ('00000000-0000-0000-0000-0000000000a1','s1@x.com','{"name":"Streamer1"}','{"provider":"email"}'),
 ('00000000-0000-0000-0000-0000000000a2','s2@x.com','{"name":"Streamer2"}','{"provider":"email"}');
create function pg_temp.as_user(uid text) returns void language sql as $$ select set_config('request.jwt.claim.sub', uid, false) $$;
create function pg_temp.expect_error(q text, msg text) returns void language plpgsql as $$
begin execute q; raise exception 'EXPECTED ERROR: %', msg; exception when others then
  if sqlerrm like 'EXPECTED ERROR%' then raise; end if; raise notice 'ok blocked (%): %', msg, sqlerrm; end $$;
create function pg_temp.check(ok boolean, msg text) returns void language plpgsql as $$
begin if not ok then raise exception 'CHECK FAILED: %', msg; end if; raise notice 'ok %', msg; end $$;
-- anon cannot heartbeat, can list
select pg_temp.as_user(''); set role anon;
select pg_temp.expect_error('select public.live_heartbeat(''x1'',''a'',null,''g'',''t'',1)', 'anon heartbeat');
select pg_temp.expect_error('select * from public.live_sessions', 'table hidden from anon');
select pg_temp.check((select count(*) from public.live_now())=0, 'empty list');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000a1'); set role authenticated;
select pg_temp.expect_error('select public.live_heartbeat(''bad name!'',''a'',null,''g'',''t'',1)', 'username validated');
select public.live_heartbeat('@poomtv99','Poom','https://p16.tiktokcdn.com/a.jpg','PTVRunner','เล่นเกม',120);
select public.live_heartbeat('poomtv99','Poom','javascript:alert(1)','PTVRunner','เล่นเกม',150);
select pg_temp.expect_error('select * from public.live_sessions', 'table hidden from users');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000a2'); set role authenticated;
select public.live_heartbeat('other.one','Two',null,'','',5);
reset role;
update public.live_sessions set last_seen=now()-interval '10 minutes' where user_id='00000000-0000-0000-0000-0000000000a2';
set role anon;
select pg_temp.check((select count(*) from public.live_now())=1, 'stale session hidden');
select pg_temp.check((select avatar_url from public.live_now())='https://p16.tiktokcdn.com/a.jpg', 'bad avatar ignored, old kept');
select pg_temp.check((select viewers from public.live_now())=150, 'viewers updated');
select pg_temp.check((public.site_stats()->>'live')::int=1, 'stats');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000a1'); set role authenticated; select public.live_stop(); reset role;
set role anon; select pg_temp.check((select count(*) from public.live_now())=0, 'stopped'); reset role;
select 'ALL LIVE TESTS PASSED';
