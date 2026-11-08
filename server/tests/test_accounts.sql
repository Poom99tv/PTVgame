\set ON_ERROR_STOP 1
update public.plans set active = true where game_id is null;  -- these tests cover the VIP plans
update public.settings set value='true' where key='online_pay_enabled';  -- these tests cover PayPal orders
-- users: admin, agent, alice (normal), bob (normal)
insert into auth.users values
 ('00000000-0000-0000-0000-00000000000a','admin@x.com','{"full_name":"Admin Poom"}','{"provider":"email"}'),
 ('00000000-0000-0000-0000-00000000000b','agent@x.com','{"name":"Agent","picture":"https://lh3.googleusercontent.com/a.png"}','{"provider":"google"}'),
 ('00000000-0000-0000-0000-00000000000c','alice@x.com','{}','{"provider":"facebook"}'),
 ('00000000-0000-0000-0000-00000000000d','bob@x.com','{"avatar_url":"http://insecure/x.png"}','{"provider":"email"}');
update public.profiles set role='admin' where id='00000000-0000-0000-0000-00000000000a';
update public.profiles set role='agent' where id='00000000-0000-0000-0000-00000000000b';
select ptv_id as bob_ptv from public.profiles where id='00000000-0000-0000-0000-00000000000d' \gset
select ptv_id as agent_ptv from public.profiles where id='00000000-0000-0000-0000-00000000000b' \gset
select display_name, provider, avatar_url, ptv_id between 10000000 and 99999999 as id8 from public.profiles order by id;

-- PromptPay payload against an independently computed reference (python, see test runner)
select public.promptpay_payload('081-234-5678', 690) as pp_phone;
select public.promptpay_payload('1234567890123', 7590.5) as pp_nid;

create function pg_temp.as_user(uid text) returns void language sql as $$ select set_config('request.jwt.claim.sub', uid, false) $$;
create function pg_temp.expect_error(q text, msg text) returns void language plpgsql as $$
begin execute q; raise exception 'EXPECTED ERROR: %', msg; exception when others then
  if sqlerrm like 'EXPECTED ERROR%' then raise; end if; raise notice 'ok blocked (%): %', msg, sqlerrm; end $$;

-- ---------------------------------------------- normal user
set role authenticated;
select pg_temp.as_user('00000000-0000-0000-0000-00000000000c');
select (public.my_account()->>'role') as alice_role, (public.my_account()->>'is_vip') as alice_vip;
select pg_temp.expect_error($q$update public.profiles set role='admin'$q$, 'user cannot self-promote');
select pg_temp.expect_error($q$update public.profiles set vip_until=now()+interval '9 years'$q$, 'user cannot self-VIP');
select pg_temp.expect_error($q$insert into public.orders(buyer_id,target_id,plan_code,days,amount,currency,method) values (auth.uid(),auth.uid(),'monthly',30,0.01,'USD','paypal')$q$, 'no direct order insert');
select pg_temp.expect_error($q$select public.apply_paid_order(gen_random_uuid())$q$, 'cannot call apply_paid_order');
select pg_temp.expect_error($q$select public.admin_stats()$q$, 'not admin');
select pg_temp.expect_error($q$select public.lookup_player(1)$q$, 'lookup agents only');
select pg_temp.expect_error($q$select * from public.settings$q$, 'settings hidden');
select count(*) as alice_sees_profiles from public.profiles;   -- 1 (own)
select public.update_my_profile('Alice ✨','https://cdn.example/a.png');
select pg_temp.expect_error($q$select public.update_my_profile('x','javascript:alert(1)')$q$, 'avatar must be https');
-- normal user cannot gift
reset role; select pg_temp.as_user('00000000-0000-0000-0000-00000000000a'); set role authenticated;
select public.admin_set_setting('promptpay_id','0812345678');
select pg_temp.expect_error($q$select public.admin_set_setting('promptpay_id','12345')$q$, 'bad promptpay id rejected');
select pg_temp.as_user('00000000-0000-0000-0000-00000000000c');
select pg_temp.expect_error(format('select public.create_order(%L,%L,%s)','monthly','paypal',:agent_ptv), 'normal user cannot gift');
select amount, currency, agent_price from public.create_order('monthly','paypal');   -- 20.00 USD
select amount, currency, left(promptpay_payload,20) from public.create_order('yearly','promptpay'); -- 7590 THB
select pg_temp.expect_error($q$select public.attach_slip((select id from public.orders where method='promptpay' limit 1),'someoneelse/x.jpg')$q$, 'slip path must be own folder');
select public.attach_slip((select id from public.orders where method='promptpay' and buyer_id=auth.uid() limit 1), auth.uid()::text || '/slip1.jpg');
select status from public.orders where method='promptpay';
select count(*) as alice_orders from public.my_orders();
select public.payment_info();

-- ---------------------------------------------- agent gifts to bob at agent price
select pg_temp.as_user('00000000-0000-0000-0000-00000000000b');
select public.lookup_player(:bob_ptv) is not null as agent_sees_bob;
select amount, currency, agent_price, target_id='00000000-0000-0000-0000-00000000000d' as to_bob
  from public.create_order('monthly','promptpay',:bob_ptv);
select pg_temp.expect_error($q$select public.create_order('monthly','paypal',99999999)$q$, 'unknown PTV id');

-- ---------------------------------------------- admin
select pg_temp.as_user('00000000-0000-0000-0000-00000000000a');
select public.admin_stats();
select count(*) as admin_users from public.admin_list_users('');
select count(*) as admin_find_alice from public.admin_list_users('alice');
select public.admin_approve_order((select id from public.orders where status='review' limit 1));
select public.admin_approve_order((select id from public.orders where status='paid' limit 1));  -- idempotent
select public.admin_approve_order((select o.id from public.orders o where o.agent_price limit 1));
reset role;
select display_name, role, vip_until > now() + interval '364 days' as yearly, vip_until > now() + interval '29 days' as monthly from public.profiles order by id;
set role authenticated;
select public.admin_grant_vip('00000000-0000-0000-0000-00000000000c', 0);
select pg_temp.expect_error($q$select public.admin_set_role('00000000-0000-0000-0000-00000000000a','user')$q$, 'admin cannot demote self');
select public.admin_set_role('00000000-0000-0000-0000-00000000000c','agent');
select public.admin_update_plan('monthly',19,25,14,650,500,true);
select public.admin_reject_order((select id from public.orders where status='pending' limit 1),'test reject');
select count(*) as audit_rows from public.admin_audit(50);
select public.admin_get_settings();
reset role;
-- service role (edge function) path
select pg_temp.as_user('00000000-0000-0000-0000-00000000000d'); set role authenticated;
select id as bob_order from public.create_order('monthly','paypal') \gset
reset role; set role service_role;
select status from public.apply_paid_order(:'bob_order','CAPTURE123',null);
reset role;
select 'ALL TESTS PASSED';
