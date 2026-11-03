// PayPal webhook (backup path): grants VIP even if the buyer closed the launcher after paying.
// Subscribe to CHECKOUT.ORDER.APPROVED and PAYMENT.CAPTURE.COMPLETED in the PayPal developer dashboard.
// Every event is verified with PayPal before anything is trusted.
import { captureAndApply, handle, json, paypal, serviceClient } from "../_shared/paypal.ts";

handle(async (req) => {
  const raw = await req.text();
  let event: any;
  try { event = JSON.parse(raw); } catch { return json({ error: "bad json" }, 400); }

  const webhookId = Deno.env.get("PAYPAL_WEBHOOK_ID");
  if (!webhookId) return json({ error: "webhook not configured" }, 503);
  const h = (k: string) => req.headers.get(k) ?? "";
  const verify = await paypal("/v1/notifications/verify-webhook-signature", "POST", {
    auth_algo: h("paypal-auth-algo"), cert_url: h("paypal-cert-url"), transmission_id: h("paypal-transmission-id"),
    transmission_sig: h("paypal-transmission-sig"), transmission_time: h("paypal-transmission-time"),
    webhook_id: webhookId, webhook_event: event,
  });
  if (verify.data?.verification_status !== "SUCCESS") return json({ error: "invalid signature" }, 400);

  let paypalOrderId: string | undefined;
  if (event.event_type === "CHECKOUT.ORDER.APPROVED") paypalOrderId = event.resource?.id;
  else if (event.event_type === "PAYMENT.CAPTURE.COMPLETED") paypalOrderId = event.resource?.supplementary_data?.related_ids?.order_id;
  else return json({ ignored: event.event_type });
  if (!paypalOrderId) return json({ ignored: "no order id" });

  const { data: order } = await serviceClient().from("orders").select("*").eq("paypal_order_id", paypalOrderId).maybeSingle();
  if (!order) return json({ ignored: "unknown order" });
  return json(await captureAndApply(order));
});
