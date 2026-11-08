\set ON_ERROR_STOP 1
update public.plans set active = true where game_id is null;  -- these tests cover the VIP plans
update public.settings set value='true' where key='online_pay_enabled';  -- these tests cover PayPal orders
-- users: admin A, agent G, customer C1, customer C2, agent2 H
insert into auth.users values
 ('00000000-0000-0000-0000-00000000000a','admin@x.com','{"full_name":"Admin"}','{"provider":"email"}'),
 ('00000000-0000-0000-0000-00000000000b','agent@x.com','{"name":"AgentG"}','{"provider":"email"}'),
 ('00000000-0000-0000-0000-00000000000c','c1@x.com','{"name":"Cust1"}','{"provider":"email"}'),
 ('00000000-0000-0000-0000-00000000000d','c2@x.com','{"name":"Cust2"}','{"provider":"email"}'),
 ('00000000-0000-0000-0000-00000000000e','h@x.com','{"name":"AgentH"}','{"provider":"email"}');
update public.profiles set role='admin' where id='00000000-0000-0000-0000-00000000000a';
create function pg_temp.as_user(uid text) returns void language sql as $$ select set_config('request.jwt.claim.sub', uid, false) $$;
create function pg_temp.expect_error(q text, msg text) returns void language plpgsql as $$
begin execute q; raise exception 'EXPECTED ERROR: %', msg; exception when others then
  if sqlerrm like 'EXPECTED ERROR%' then raise; end if; raise notice 'ok blocked (%): %', msg, sqlerrm; end $$;
create function pg_temp.check(ok boolean, msg text) returns void language plpgsql as $$
begin if not ok then raise exception 'CHECK FAILED: %', msg; end if; raise notice 'ok %', msg; end $$;

update public.settings set value='0812345678' where key='promptpay_id';

-- admin makes G and H agents -> codes generated
select pg_temp.as_user('00000000-0000-0000-0000-00000000000a'); set role authenticated;
select public.admin_set_role('00000000-0000-0000-0000-00000000000b','agent');
select public.admin_set_role('00000000-0000-0000-0000-00000000000e','agent');
reset role;
select agent_code as gcode from public.profiles where id='00000000-0000-0000-0000-00000000000b' \gset
select agent_code as hcode from public.profiles where id='00000000-0000-0000-0000-00000000000e' \gset
select ptv_id as c1ptv from public.profiles where id='00000000-0000-0000-0000-00000000000c' \gset
select pg_temp.check(length(:'gcode')=6, 'agent code generated');

-- C1 checks + uses G's discount code via PromptPay (690 * 0.9 = 621)
select pg_temp.as_user('00000000-0000-0000-0000-00000000000c'); set role authenticated;
select pg_temp.check((public.check_discount_code(lower(:'gcode'))->>'discount_pct')::int = 10, 'discount preview (case-insensitive)');
select pg_temp.expect_error('select public.check_discount_code(''ZZZZZZ'')', 'bad code');
select id as o1 from public.create_order('monthly','promptpay',null,'vip',1,:'gcode') \gset
select pg_temp.check((select amount from public.orders where id=:'o1')=621.00, '10% off first purchase');
select pg_temp.expect_error(format('select public.create_order(%L,%L,null,%L,1,%L)','monthly','paypal','vip',:'gcode'), 'only one discounted order at a time');
select pg_temp.expect_error(format('select public.create_order(%L,%L,null,%L,5,null)','monthly','promptpay','codes'), 'customers cannot buy code batches');
select pg_temp.expect_error('update public.profiles set wallet_thb = 99999', 'cannot edit wallet');
select pg_temp.expect_error('select public.my_agent()', 'customer has no agent dashboard');
reset role;
-- admin approves -> C1 bound to G, commission 621-520 = 101 THB
select pg_temp.as_user('00000000-0000-0000-0000-00000000000a'); set role authenticated;
select public.admin_approve_order(:'o1');
reset role;
select pg_temp.check((select referred_by from public.profiles where id='00000000-0000-0000-0000-00000000000c')='00000000-0000-0000-0000-00000000000b', 'customer bound to agent G');
select pg_temp.check((select discount_used from public.profiles where id='00000000-0000-0000-0000-00000000000c'), 'discount marked used');
select pg_temp.check((select wallet_thb from public.profiles where id='00000000-0000-0000-0000-00000000000b')=101.00, 'commission 101 THB');

-- C1 cannot reuse discount, cannot switch to agent H; later full-price purchase still pays G (USD: (20-15)*34.5 = 172.50)
select pg_temp.as_user('00000000-0000-0000-0000-00000000000c'); set role authenticated;
select pg_temp.expect_error(format('select public.check_discount_code(%L)',:'gcode'), 'discount once per ID');
select pg_temp.expect_error(format('select public.check_discount_code(%L)',:'hcode'), 'cannot rebind to other agent');
select id as o2 from public.create_order('monthly','paypal') \gset
reset role; set role service_role;
select status from public.apply_paid_order(:'o2','CAP-1',null);
reset role;
select pg_temp.check((select wallet_thb from public.profiles where id='00000000-0000-0000-0000-00000000000b')=273.50, 'repeat purchase commission 172.50 (USD converted)');

-- agent G: dashboard, buy 3 yearly codes with wallet? (5690*3 too much) -> buy 1 monthly code batch of 2? 520*2=1040 > 273.5 -> fails
select pg_temp.as_user('00000000-0000-0000-0000-00000000000b'); set role authenticated;
select pg_temp.check((public.my_agent()->>'wallet_thb')::numeric = 273.50, 'agent sees wallet');
select pg_temp.check(json_array_length(public.my_agent()->'customers') = 1, 'agent sees 1 customer');
select pg_temp.expect_error('select public.wallet_buy(''monthly'',''codes'',null,2)', 'insufficient wallet');
reset role;
-- admin bonus so the agent can spend
select pg_temp.as_user('00000000-0000-0000-0000-00000000000a'); set role authenticated;
select public.admin_adjust_wallet('00000000-0000-0000-0000-00000000000b', 2000, 'test bonus');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-00000000000b'); set role authenticated;
select kind, quantity, status from public.wallet_buy('monthly','codes',null,2);
select pg_temp.check((public.my_agent()->>'wallet_thb')::numeric = 1233.50, 'wallet debited 1040');
select pg_temp.check(json_array_length(public.my_agent()->'codes') = 2, 'two codes created');
select status from public.wallet_buy('monthly','vip',:c1ptv,1);
select pg_temp.check((public.my_agent()->>'wallet_thb')::numeric = 713.50, 'wallet gift to customer debited 520');
-- code batch paid by QR also works
select id as o3 from public.create_order('yearly','promptpay',null,'codes',3,null) \gset
select pg_temp.check((select amount from public.orders where id=:'o3')=17070.00, 'code batch at agent THB price x3');
-- withdrawal
select pg_temp.expect_error('select public.request_withdrawal(50,''promptpay'',''0812345678'')', 'below minimum');
select pg_temp.expect_error('select public.request_withdrawal(99999,''promptpay'',''0812345678'')', 'more than balance');
select public.request_withdrawal(500,'promptpay','PromptPay 0812345678 AgentG');
select pg_temp.expect_error('select public.request_withdrawal(100,''promptpay'',''0812345678'')', 'one pending withdrawal');
select pg_temp.check((public.my_agent()->>'wallet_thb')::numeric = 213.50, 'withdraw held');
reset role;
select code as code1 from public.vip_codes where owner_id='00000000-0000-0000-0000-00000000000b' order by code limit 1 \gset

-- C2 (unbound) redeems G's code -> VIP 30 days, bound to G; reuse fails
select pg_temp.as_user('00000000-0000-0000-0000-00000000000d'); set role authenticated;
select pg_temp.check((public.redeem_code(lower(:'code1'))->>'days')::int = 30, 'code redeemed');
select pg_temp.expect_error(format('select public.redeem_code(%L)',:'code1'), 'code single use');
select pg_temp.expect_error('select public.redeem_code(''PTV-AAAA-AAAA-AAAA'')', 'unknown code');
select pg_temp.expect_error('select * from public.vip_codes', 'codes table hidden');
select pg_temp.expect_error('select public.wallet_buy(''monthly'')', 'customer cannot use wallet');
reset role;
select pg_temp.check((select referred_by from public.profiles where id='00000000-0000-0000-0000-00000000000d')='00000000-0000-0000-0000-00000000000b', 'redeem binds customer to agent');
select pg_temp.check((select vip_until > now()+interval '29 days' from public.profiles where id='00000000-0000-0000-0000-00000000000d'), 'customer VIP from code');

-- admin processes withdrawal: reject -> refund, then paid flow
select id as w1 from public.withdrawals limit 1 \gset
select pg_temp.as_user('00000000-0000-0000-0000-00000000000a'); set role authenticated;
select pg_temp.check((select count(*) from public.admin_list_withdrawals('requested'))=1, 'admin sees request');
select public.admin_process_withdrawal(:'w1', false, 'wrong account');
reset role;
select pg_temp.check((select wallet_thb from public.profiles where id='00000000-0000-0000-0000-00000000000b')=713.50, 'rejected withdrawal refunded');
select pg_temp.as_user('00000000-0000-0000-0000-00000000000b'); set role authenticated;
select public.request_withdrawal(300,'bank','KBank 123-4-56789-0 AgentG');
reset role;
select id as w2 from public.withdrawals where status='requested' \gset
select pg_temp.as_user('00000000-0000-0000-0000-00000000000a'); set role authenticated;
select public.admin_process_withdrawal(:'w2', true, 'โอนแล้ว');
select pg_temp.expect_error(format('select public.admin_process_withdrawal(%L,true,null)',:'w2'), 'cannot process twice');
select pg_temp.check((public.admin_stats()->>'withdrawals')::int = 0, 'stats show no pending withdrawals');
reset role;
select pg_temp.check((select wallet_thb from public.profiles where id='00000000-0000-0000-0000-00000000000b')=413.50, 'paid withdrawal stays debited');
select pg_temp.check((select sum(amount_thb) from public.wallet_ledger where agent_id='00000000-0000-0000-0000-00000000000b')=413.50, 'ledger sums to wallet');

-- agent buying for himself or customer gets no commission; agent cannot use discount codes
select pg_temp.as_user('00000000-0000-0000-0000-00000000000e'); set role authenticated;
select pg_temp.expect_error(format('select public.check_discount_code(%L)',:'gcode'), 'agents cannot use discount code');
reset role;
set role service_role; select public.agent_download_click(:'gcode') as dl; reset role;
select pg_temp.check((select count(*) from public.download_clicks)=1, 'download click recorded');
select 'ALL AGENT TESTS PASSED';
-- 6-month link: expire C1's link, buy again -> no commission; C1 can then be linked by another agent's code redeem
update public.profiles set referred_at = now() - interval '7 months' where id='00000000-0000-0000-0000-00000000000c';
select wallet_thb as gbefore from public.profiles where id='00000000-0000-0000-0000-00000000000b' \gset
select pg_temp.as_user('00000000-0000-0000-0000-00000000000c'); set role authenticated;
select pg_temp.check((public.my_account()->>'agent_name') is null, 'expired link hidden from customer');
select id as o9 from public.create_order('monthly','paypal') \gset
reset role; set role service_role; select status from public.apply_paid_order(:'o9','CAP-9',null); reset role;
select pg_temp.check((select wallet_thb from public.profiles where id='00000000-0000-0000-0000-00000000000b')=:gbefore, 'no commission after 6 months');
-- agent H gives C1 a code -> C1 relinked to H
select pg_temp.as_user('00000000-0000-0000-0000-00000000000a'); set role authenticated;
select public.admin_adjust_wallet('00000000-0000-0000-0000-00000000000e', 600, 'test');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-00000000000e'); set role authenticated;
select status from public.wallet_buy('monthly','codes',null,1);
reset role;
select code as hcode1 from public.vip_codes where owner_id='00000000-0000-0000-0000-00000000000e' limit 1 \gset
select pg_temp.as_user('00000000-0000-0000-0000-00000000000c'); set role authenticated;
select public.redeem_code(:'hcode1');
reset role;
select pg_temp.check((select referred_by from public.profiles where id='00000000-0000-0000-0000-00000000000c')='00000000-0000-0000-0000-00000000000e', 'expired link can move to new agent');
select 'ALL EXPIRY TESTS PASSED';
