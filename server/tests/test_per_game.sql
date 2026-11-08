\set ON_ERROR_STOP 1
update public.settings set value='0812345678' where key='promptpay_id';
insert into auth.users values
 ('00000000-0000-0000-0000-0000000000e1','adm@x.com','{"name":"Adm"}','{"provider":"email"}'),
 ('00000000-0000-0000-0000-0000000000e2','buyer@x.com','{"name":"Buyer"}','{"provider":"email"}'),
 ('00000000-0000-0000-0000-0000000000e3','agent@x.com','{"name":"Agent"}','{"provider":"email"}');
update public.profiles set role='admin' where id='00000000-0000-0000-0000-0000000000e1';
update public.profiles set role='agent', wallet_thb=5000 where id='00000000-0000-0000-0000-0000000000e3';
create function pg_temp.as_user(uid text) returns void language sql as $$ select set_config('request.jwt.claim.sub', uid, false) $$;
create function pg_temp.expect_error(q text, msg text) returns void language plpgsql as $$
begin execute q; raise exception 'EXPECTED ERROR: %', msg; exception when others then
  if sqlerrm like 'EXPECTED ERROR%' then raise; end if; raise notice 'ok blocked (%): %', msg, sqlerrm; end $$;
create function pg_temp.check(ok boolean, msg text) returns void language plpgsql as $$
begin if not ok then raise exception 'CHECK FAILED: %', msg; end if; raise notice 'ok %', msg; end $$;

select pg_temp.check((select count(*) from public.plans where game_id is not null and active) = 11, '11 games on sale');
select pg_temp.check((select price_thb from public.plans where game_id='roblox-box-2') = 320 and (select price_usd from public.plans where game_id='roblox-box-2') = 10, 'Gear Box 320 / $10');
select pg_temp.check(not exists(select 1 from public.plans where game_id is null and active), 'VIP pass no longer sold');

-- buyer orders Zombie Rush with PromptPay, admin approves
select pg_temp.as_user('00000000-0000-0000-0000-0000000000e2'); set role authenticated;
select pg_temp.expect_error($q$select public.create_order('monthly','promptpay')$q$, 'VIP plan not for sale');
select pg_temp.check(public.my_games()::text = '{}', 'owns nothing yet');
select pg_temp.expect_error($q$select public.play_ticket_claims('zombierush')$q$, 'cannot play unpaid game');
select id as o1, amount from public.create_order('g_zombierush','promptpay') \gset
select pg_temp.check(:amount = 690, 'Zombie Rush costs 690');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000e1'); set role authenticated;
select public.admin_approve_order(:'o1');
reset role;
select pg_temp.as_user('00000000-0000-0000-0000-0000000000e2'); set role authenticated;
select pg_temp.check((public.my_games()->>'zombierush')::timestamptz > now() + interval '29 days', 'Zombie Rush unlocked for 30 days');
select pg_temp.check((public.my_account()->>'vip_until') is null, 'no VIP given for a game purchase');
select pg_temp.check((public.play_ticket_claims('zombierush')->>'vip_until')::timestamptz > now(), 'ticket for owned game');
select pg_temp.expect_error($q$select public.play_ticket_claims('ptvboat')$q$, 'other game still locked');
reset role;

-- buying again stacks days; agent wallet sends a game; game codes redeem into that game
select ptv_id as bptv from public.profiles where id='00000000-0000-0000-0000-0000000000e2' \gset
select pg_temp.as_user('00000000-0000-0000-0000-0000000000e3'); set role authenticated;
select status from public.wallet_buy('g_zombierush','vip',:bptv,1);
select kind from public.wallet_buy('g_roblox-box-2','codes',null,1);
reset role;
select code as gcode from public.vip_codes where owner_id='00000000-0000-0000-0000-0000000000e3' limit 1 \gset
select pg_temp.check((select until from public.game_access where user_id='00000000-0000-0000-0000-0000000000e2' and game_id='zombierush') > now() + interval '59 days', 'second purchase adds 30 more days');
select pg_temp.check((select wallet_thb from public.profiles where id='00000000-0000-0000-0000-0000000000e3') = 5000 - 520 - 240, 'agent paid agent prices');
select pg_temp.as_user('00000000-0000-0000-0000-0000000000e2'); set role authenticated;
select pg_temp.check((public.redeem_code(:'gcode')->>'game_id') = 'roblox-box-2', 'code opens Gear Box');
select pg_temp.check((public.my_games()::jsonb ? 'roblox-box-2'), 'Gear Box owned');
select pg_temp.expect_error($q$select public.admin_update_plan('g_ptvboat',1,null,1,1,1,true,7,null)$q$, 'only admin edits prices');
reset role;

-- admin edits days / price and gives game time
select pg_temp.as_user('00000000-0000-0000-0000-0000000000e1'); set role authenticated;
select public.admin_update_plan('g_ptvboat', 30, null, 22, 790, 600, true, 60, 'PTVBoat');
select pg_temp.check((select days from public.plans where code='g_ptvboat') = 60 and (select price_thb from public.plans where code='g_ptvboat') = 790, 'admin changed days and price');
select public.admin_grant_game('00000000-0000-0000-0000-0000000000e2', 'ptvboat', 7);
select pg_temp.check((public.admin_user_games('00000000-0000-0000-0000-0000000000e2')::jsonb) ? 'ptvboat', 'admin gave PTVBoat');
select pg_temp.expect_error($q$select public.admin_grant_game('00000000-0000-0000-0000-0000000000e2','nope',7)$q$, 'unknown game');
reset role;
-- VIP bought before the change still opens every game
update public.profiles set vip_until = now() + interval '5 days' where id='00000000-0000-0000-0000-0000000000e3';
select pg_temp.as_user('00000000-0000-0000-0000-0000000000e3'); set role authenticated;
select pg_temp.check((public.play_ticket_claims('ptvup')->>'vip_until')::timestamptz > now(), 'old VIP plays any game');
reset role;
select pg_temp.check(not exists(select 1 from public.game_access where user_id='00000000-0000-0000-0000-0000000000e1'), 'admin has no game rows');
select 'ALL PER GAME TESTS PASSED';
