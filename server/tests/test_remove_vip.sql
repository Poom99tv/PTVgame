\set ON_ERROR_STOP 1
-- run the VIP conversion again on data that has VIP time, as the live database will
insert into auth.users values ('00000000-0000-0000-0000-0000000000f1','old@x.com','{"name":"OldVip"}','{"provider":"email"}'),('00000000-0000-0000-0000-0000000000f2','free@x.com','{"name":"Free"}','{"provider":"email"}');
update public.profiles set vip_until = now() + interval '12 days' where id = '00000000-0000-0000-0000-0000000000f1';
insert into public.game_access values ('00000000-0000-0000-0000-0000000000f1','zombierush', now() + interval '40 days');
create function pg_temp.check(ok boolean, msg text) returns void language plpgsql as $$
begin if not ok then raise exception 'CHECK FAILED: %', msg; end if; raise notice 'ok %', msg; end $$;
\i supabase/migrations/20261109010000_remove_vip.sql
select pg_temp.check((select count(*) from public.game_access where user_id='00000000-0000-0000-0000-0000000000f1') = 12, 'VIP turned into all 12 games');
select pg_temp.check((select until from public.game_access where user_id='00000000-0000-0000-0000-0000000000f1' and game_id='ptvboat') between now() + interval '11 days' and now() + interval '13 days', 'same end date as VIP');
select pg_temp.check((select until from public.game_access where user_id='00000000-0000-0000-0000-0000000000f1' and game_id='zombierush') > now() + interval '39 days', 'longer game time kept');
select pg_temp.check(not exists(select 1 from public.game_access where user_id='00000000-0000-0000-0000-0000000000f2'), 'free user gets nothing');
select pg_temp.check(not exists(select 1 from public.profiles where vip_until is not null), 'no VIP left');
select pg_temp.check(not exists(select 1 from public.plans where game_id is null and active), 'no VIP plan on sale');
select 'ALL REMOVE VIP TESTS PASSED';
