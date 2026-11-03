// POST { plan, method: "paypal" | "card", target_ptv_id?, return_url, cancel_url }
// Creates our order (price decided by SQL, never by the launcher) and a matching PayPal order.
// "card" opens PayPal's guest card form (no PayPal account needed where PayPal supports it).
import { handle, json, paypal, safeReturnUrl, serviceClient, userClient, HttpError } from "../_shared/paypal.ts";

handle(async (req) => {
  const body = await req.json().catch(() => ({}));
  const method = body.method === "card" ? "card" : body.method === "paypal" ? "paypal" : null;
  if (!method) throw new HttpError(400, "method must be paypal or card");
  const returnUrl = safeReturnUrl(body.return_url);
  const cancelUrl = safeReturnUrl(body.cancel_url ?? body.return_url);

  const { data: order, error } = await userClient(req).rpc("create_order", {
    p_plan: String(body.plan ?? ""), p_method: method,
    p_target_ptv_id: body.target_ptv_id ? Number(body.target_ptv_id) : null,
  });
  if (error || !order) throw new HttpError(400, error?.message ?? "could not create order");

  const res = await paypal("/v2/checkout/orders", "POST", {
    intent: "CAPTURE",
    purchase_units: [{
      reference_id: order.id,
      custom_id: order.id,
      invoice_id: `PTV-${order.order_no}`,
      description: `PTVgame VIP ${order.plan_code} (${order.days} days)`,
      amount: { currency_code: order.currency, value: Number(order.amount).toFixed(2) },
    }],
    payment_source: {
      paypal: {
        experience_context: {
          brand_name: "PTVgame",
          user_action: "PAY_NOW",
          shipping_preference: "NO_SHIPPING",
          landing_page: method === "card" ? "GUEST_CHECKOUT" : "LOGIN",
          return_url: returnUrl,
          cancel_url: cancelUrl,
        },
      },
    },
  }, `create-${order.id}`);

  if (res.status >= 300 || !res.data?.id) {
    await serviceClient().from("orders").update({ status: "cancelled", note: "PayPal create failed" }).eq("id", order.id);
    throw new HttpError(502, "PayPal could not create the payment");
  }
  const link = (res.data.links ?? []).find((l: any) => l.rel === "payer-action" || l.rel === "approve");
  await serviceClient().from("orders").update({ paypal_order_id: res.data.id }).eq("id", order.id);
  return json({ order_id: order.id, order_no: order.order_no, amount: order.amount, currency: order.currency, approve_url: link?.href });
});
