\set ON_ERROR_STOP 1
update public.plans set active = true where game_id is null;  -- these tests cover the VIP plans
update public.settings set value='0812345678' where key='promptpay_id';
insert into auth.users values ('00000000-0000-0000-0000-0000000000c1','p1@x.com','{"name":"P1"}','{"provider":"email"}');
create function pg_temp.as_user(uid text) returns void language sql as $$ select set_config('request.jwt.claim.sub', uid, false) $$;
create function pg_temp.expect_error(q text, msg text) returns void language plpgsql as $$
begin execute q; raise exception 'EXPECTED ERROR: %', msg; exception when others then
  if sqlerrm like 'EXPECTED ERROR%' then raise; end if; raise notice 'ok blocked (%): %', msg, sqlerrm; end $$;
create function pg_temp.check(ok boolean, msg text) returns void language plpgsql as $$
begin if not ok then raise exception 'CHECK FAILED: %', msg; end if; raise notice 'ok %', msg; end $$;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000c1'); set role authenticated;
select pg_temp.check((public.pay_options()->>'online')::boolean = false, 'online pay off by default');
select pg_temp.expect_error($q$select public.create_order('monthly','paypal')$q$, 'paypal blocked');
select pg_temp.expect_error($q$select public.create_order('monthly','card')$q$, 'card blocked');
select pg_temp.check((public.create_order('monthly','promptpay')).status::text = 'pending', 'promptpay still works');
reset role;
update public.settings set value='true' where key='online_pay_enabled';
select pg_temp.as_user('00000000-0000-0000-0000-0000000000c1'); set role authenticated;
select pg_temp.check((public.create_order('monthly','paypal')).method::text = 'paypal', 'paypal allowed when switched on');
reset role;
select 'ALL ONLINE PAY TESTS PASSED';
