-- Grilled Squid uses the existing per-game purchase, wallet, code and play-ticket flows.
-- Retail prices and 30-day term confirmed by the owner. Initial agent prices follow
-- the existing 75% policy; subsequent admin changes to agent prices are preserved.
insert into public.plans
  (code,name,days,price_usd,list_price_usd,agent_price_usd,price_thb,agent_price_thb,sort,game_id,active)
values
  ('g_ptvfood','Grilled Squid',30,7.00,null,5.25,180.00,135.00,9,'ptvfood',true)
on conflict(code) do update set
  name=excluded.name, days=excluded.days, price_usd=excluded.price_usd,
  price_thb=excluded.price_thb, list_price_usd=null, game_id=excluded.game_id,
  sort=excluded.sort, active=true;
