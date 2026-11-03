// POST { order_id } — called by the launcher after PayPal redirects back.
// Only the buyer can trigger it; PayPal's own record decides whether VIP is granted.
import { captureAndApply, handle, json, requireUser, serviceClient, HttpError } from "../_shared/paypal.ts";

handle(async (req) => {
  const userId = await requireUser(req);
  const body = await req.json().catch(() => ({}));
  const { data: order } = await serviceClient().from("orders").select("*").eq("id", String(body.order_id ?? "")).maybeSingle();
  if (!order || order.buyer_id !== userId) throw new HttpError(404, "order not found");
  if (order.method !== "paypal" && order.method !== "card") throw new HttpError(400, "not a PayPal order");
  return json(await captureAndApply(order));
});
