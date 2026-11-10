-- Run on a disposable database after all migrations. All test changes roll back.
begin;
create function pg_temp.check(ok boolean,msg text) returns void language plpgsql as $$
begin if ok is distinct from true then raise exception 'CHECK FAILED: %',msg; end if; end $$;
create function pg_temp.blocked(q text) returns void language plpgsql as $$
begin execute q; raise exception 'UNEXPECTED SUCCESS'; exception when others then if sqlerrm='UNEXPECTED SUCCESS' then raise; end if; end $$;
select pg_temp.check((select days=30 and price_thb=180 and price_usd=7 and active and game_id='ptvfood' from public.plans where code='g_ptvfood'),'confirmed retail terms');
insert into auth.users values
 ('00000000-0000-0000-0000-00000000f001','food-buyer@example.test','{"name":"Buyer"}','{}'),
 ('00000000-0000-0000-0000-00000000f002','food-admin@example.test','{"name":"Admin"}','{}'),
 ('00000000-0000-0000-0000-00000000f003','food-agent@example.test','{"name":"Agent"}','{}');
update public.profiles set role='admin' where id='00000000-0000-0000-0000-00000000f002';
update public.profiles set role='agent',wallet_thb=1000 where id='00000000-0000-0000-0000-00000000f003';
update public.settings set value='0812345678' where key='promptpay_id';
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-00000000f001',true);
set local role authenticated;
select pg_temp.blocked($q$select public.play_ticket_claims('ptvfood')$q$);
select public.create_order('g_ptvfood','promptpay');
reset role;
select pg_temp.check((select amount=180 and days=30 from public.orders where buyer_id='00000000-0000-0000-0000-00000000f001'),'PromptPay charges 180 THB');
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-00000000f002',true);
select public.admin_approve_order((select id from public.orders where buyer_id='00000000-0000-0000-0000-00000000f001'));
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-00000000f001',true);
set local role authenticated;
select pg_temp.check(public.play_ticket_claims('ptvfood')->>'game'='ptvfood','paid game gets ticket');
select pg_temp.blocked($q$select public.play_ticket_claims('ptvup')$q$);
reset role;
select pg_temp.check((select until between now()+interval '29 days' and now()+interval '31 days' from public.game_access where user_id='00000000-0000-0000-0000-00000000f001' and game_id='ptvfood'),'30-day entitlement');
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-00000000f003',true);
select public.wallet_buy('g_ptvfood','vip',(select ptv_id from public.profiles where id='00000000-0000-0000-0000-00000000f001'),1);
select public.wallet_buy('g_ptvfood','codes',null,1);
select pg_temp.check((select wallet_thb=730 from public.profiles where id='00000000-0000-0000-0000-00000000f003'),'agent policy charges 135 per item');
select set_config('request.jwt.claim.sub','00000000-0000-0000-0000-00000000f001',true);
select public.redeem_code((select code from public.vip_codes where owner_id='00000000-0000-0000-0000-00000000f003'));
select pg_temp.check((select until>now()+interval '89 days' from public.game_access where user_id='00000000-0000-0000-0000-00000000f001' and game_id='ptvfood'),'wallet and code each stack 30 days');
select pg_temp.check((select count(*)=1 from public.game_access where user_id='00000000-0000-0000-0000-00000000f001'),'purchase unlocks only Grilled Squid');
rollback;
