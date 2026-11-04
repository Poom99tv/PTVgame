\set ON_ERROR_STOP 1
insert into auth.users values ('00000000-0000-0000-0000-0000000000b1','g1@x.com','{"name":"G1"}','{"provider":"email"}'),('00000000-0000-0000-0000-0000000000b2','g2@x.com','{"name":"G2"}','{"provider":"email"}');
create function pg_temp.as_user(uid text) returns void language sql as $$ select set_config('request.jwt.claim.sub', uid, false) $$;
create function pg_temp.expect_error(q text, msg text) returns void language plpgsql as $$
begin execute q; raise exception 'EXPECTED ERROR: %', msg; exception when others then
  if sqlerrm like 'EXPECTED ERROR%' then raise; end if; raise notice 'ok blocked (%): %', msg, sqlerrm; end $$;
create function pg_temp.check(ok boolean, msg text) returns void language plpgsql as $$
begin if not ok then raise exception 'CHECK FAILED: %', msg; end if; raise notice 'ok %', msg; end $$;
select pg_temp.as_user(''); set role anon;
select pg_temp.expect_error('select public.gte_get()', 'anon cannot read');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000b1'); set role authenticated;
select pg_temp.check(public.gte_get() is null, 'nothing yet');
select pg_temp.expect_error($q$select public.gte_save('{}'::jsonb,'{}'::jsonb,'web')$q$, 'presets must be array');
select pg_temp.expect_error($q$select public.gte_save('[]'::jsonb,'{}'::jsonb,'web')$q$, 'at least one preset');
select public.gte_save('[{"name":"A","gift_map":{"rose":{"key":"v"}}}]'::jsonb,'{"username":"poom"}'::jsonb,'web');
select pg_temp.check(public.gte_get()->'presets'->0->>'name'='A', 'saved');
select pg_temp.expect_error('select * from public.gte_profiles', 'table hidden');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000b2'); set role authenticated;
select pg_temp.check(public.gte_get() is null, 'other user cannot see');
reset role;
select 'ALL GTE CLOUD TESTS PASSED';
