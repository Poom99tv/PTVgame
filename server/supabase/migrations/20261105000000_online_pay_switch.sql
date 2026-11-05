-- PayPal / card checkout stays off until the PayPal app is switched to Live. While PAYPAL_ENV is
-- sandbox, PayPal accepts test cards, so leaving it open would hand out VIP for free.
-- Turn on with: select public.admin_set_setting('online_pay_enabled','true');  (or the web admin page)

insert into public.settings(key, value) values ('online_pay_enabled', 'false') on conflict (key) do nothing;

create or replace function public.orders_online_pay_guard() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if new.method::text in ('paypal', 'card')
     and coalesce((select value from public.settings where key = 'online_pay_enabled'), 'false') <> 'true' then
    raise exception 'ยังไม่เปิดรับชำระด้วย PayPal / บัตร กรุณาใช้ QR พร้อมเพย์';
  end if;
  return new;
end $$;

drop trigger if exists orders_online_pay_guard on public.orders;
create trigger orders_online_pay_guard before insert on public.orders
  for each row execute function public.orders_online_pay_guard();

-- the public settings the Launcher reads, so it can hide the PayPal / card buttons
create or replace function public.pay_options() returns json language sql stable security definer set search_path = public as $$
  select json_build_object('online', coalesce((select value from public.settings where key = 'online_pay_enabled'), 'false') = 'true');
$$;
grant execute on function public.pay_options() to anon, authenticated;
