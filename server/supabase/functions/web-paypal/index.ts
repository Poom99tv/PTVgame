// web-paypal: PayPal / card checkout for the ptvgame.com website (the launcher keeps using paypal-create).
// One file so it can be pasted into the Supabase dashboard editor. Uses the same PAYPAL_* secrets.
//
//   POST { action: "create", plan, method: "paypal" | "card", target_ptv_id?, kind?, quantity?, discount_code? }
//     -> { order_id, order_no, approve_url }   PayPal sends the buyer back to https://ptvgame.com/app/?paypal=<order_id>
//   POST { action: "capture", order_id }
//     -> { status: "paid" | "waiting" | "failed" }
//
// The price always comes from SQL (create_order); the game is unlocked by apply_paid_order only after
// PayPal reports a completed capture whose amount, currency and custom_id match our order.
import { createClient } from "npm:@supabase/supabase-js@2";

const URL_ = Deno.env.get("SUPABASE_URL")!;
const ANON = Deno.env.get("SUPABASE_ANON_KEY")!;
const SERVICE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const PAYPAL_BASE = (Deno.env.get("PAYPAL_ENV") ?? "sandbox").toLowerCase() === "live" ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
const SITE = "https://ptvgame.com/app/";
const ORIGINS = ["https://ptvgame.com", "https://www.ptvgame.com", "https://poom99tv.github.io"];

class HttpError extends Error { constructor(public status: number, message: string) { super(message); } }
const cors = (req: Request) => {
  const o = req.headers.get("Origin") ?? "";
  return { "Access-Control-Allow-Origin": ORIGINS.includes(o) ? o : ORIGINS[0], "Access-Control-Allow-Headers": "authorization, apikey, content-type", "Access-Control-Allow-Methods": "POST, OPTIONS", Vary: "Origin" };
};
const service = () => createClient(URL_, SERVICE, { auth: { persistSession: false } });

let token: { value: string; expires: number } | null = null;
async function accessToken() {
  if (token && token.expires > Date.now() + 60_000) return token.value;
  const id = Deno.env.get("PAYPAL_CLIENT_ID"), secret = Deno.env.get("PAYPAL_SECRET");
  if (!id || !secret) throw new HttpError(503, "PayPal is not configured on the server");
  const r = await fetch(`${PAYPAL_BASE}/v1/oauth2/token`, { method: "POST", headers: { Authorization: "Basic " + btoa(`${id}:${secret}`), "Content-Type": "application/x-www-form-urlencoded" }, body: "grant_type=client_credentials" });
  if (!r.ok) throw new HttpError(502, "PayPal authentication failed");
  const j = await r.json(); token = { value: j.access_token, expires: Date.now() + j.expires_in * 1000 }; return token.value;
}
async function paypal(path: string, method: string, body?: unknown, requestId?: string) {
  const headers: Record<string, string> = { Authorization: `Bearer ${await accessToken()}`, "Content-Type": "application/json", Prefer: "return=representation" };
  if (requestId) headers["PayPal-Request-Id"] = requestId;
  const r = await fetch(PAYPAL_BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await r.text(); let data: any = {}; try { data = text ? JSON.parse(text) : {}; } catch { data = { raw: text }; }
  return { status: r.status, data };
}

async function create(req: Request, body: any) {
  const method = body.method === "card" ? "card" : body.method === "paypal" ? "paypal" : null;
  if (!method) throw new HttpError(400, "method must be paypal or card");
  const user = createClient(URL_, ANON, { auth: { persistSession: false }, global: { headers: { Authorization: req.headers.get("Authorization") ?? "" } } });
  const { data: order, error } = await user.rpc("create_order", {
    p_plan: String(body.plan ?? ""), p_method: method,
    p_target_ptv_id: body.target_ptv_id ? Number(body.target_ptv_id) : null,
    p_kind: body.kind === "codes" ? "codes" : "vip",
    p_quantity: Number.isInteger(body.quantity) ? body.quantity : 1,
    p_discount_code: typeof body.discount_code === "string" && body.discount_code.trim() ? body.discount_code.trim() : null,
  });
  if (error || !order) throw new HttpError(400, error?.message ?? "could not create order");
  const { data: plan } = await service().from("plans").select("name").eq("code", order.plan_code).maybeSingle();
  const back = `${SITE}?paypal=${order.id}`;
  const res = await paypal("/v2/checkout/orders", "POST", {
    intent: "CAPTURE",
    purchase_units: [{
      reference_id: order.id, custom_id: order.id, invoice_id: `PTV-${order.order_no}`,
      description: `PTVgame ${plan?.name ?? order.plan_code}${order.kind === "codes" ? ` codes x${order.quantity}` : ` (${order.days} days)`}`.slice(0, 127),
      amount: { currency_code: order.currency, value: Number(order.amount).toFixed(2) },
    }],
    payment_source: { paypal: { experience_context: { brand_name: "PTVgame", user_action: "PAY_NOW", shipping_preference: "NO_SHIPPING",
      landing_page: method === "card" ? "GUEST_CHECKOUT" : "LOGIN", return_url: back, cancel_url: `${SITE}?paypal_cancel=${order.id}` } } },
  }, `web-create-${order.id}`);
  if (res.status >= 300 || !res.data?.id) {
    await service().from("orders").update({ status: "cancelled", note: "PayPal create failed" }).eq("id", order.id);
    throw new HttpError(502, "PayPal could not create the payment");
  }
  const link = (res.data.links ?? []).find((l: any) => l.rel === "payer-action" || l.rel === "approve");
  await service().from("orders").update({ paypal_order_id: res.data.id }).eq("id", order.id);
  return { order_id: order.id, order_no: order.order_no, approve_url: link?.href };
}

async function capture(req: Request, body: any) {
  const jwt = (req.headers.get("Authorization") ?? "").replace(/^Bearer\s+/i, "");
  const { data: u } = await service().auth.getUser(jwt);
  if (!u?.user) throw new HttpError(401, "session expired, sign in again");
  const { data: order } = await service().from("orders").select("*").eq("id", String(body.order_id ?? "")).maybeSingle();
  if (!order || order.buyer_id !== u.user.id) throw new HttpError(404, "order not found");
  if (order.method !== "paypal" && order.method !== "card") throw new HttpError(400, "not a PayPal order");
  if (order.status === "paid") return { status: "paid" };
  if (!order.paypal_order_id) throw new HttpError(400, "order has no PayPal payment");
  let res = await paypal(`/v2/checkout/orders/${order.paypal_order_id}/capture`, "POST", {}, `capture-${order.id}`);
  if (res.status === 422 || res.status === 409) res = await paypal(`/v2/checkout/orders/${order.paypal_order_id}`, "GET");
  const po = res.data;
  if (["APPROVED", "CREATED", "PAYER_ACTION_REQUIRED"].includes(po?.status)) return { status: "waiting" };
  const unit = po?.purchase_units?.[0]; const cap = unit?.payments?.captures?.[0];
  if (po?.status !== "COMPLETED" || !cap) return { status: "failed" };
  if (cap.status === "PENDING") return { status: "waiting" };
  if (cap.status !== "COMPLETED") return { status: "failed" };
  const ok = (unit.custom_id ?? cap.custom_id) === order.id && cap.amount?.currency_code === order.currency
    && Number(cap.amount?.value).toFixed(2) === Number(order.amount).toFixed(2);
  if (!ok) {
    await service().from("audit_log").insert({ action: "paypal_mismatch", detail: { order: order.id, paypal: order.paypal_order_id } });
    throw new HttpError(409, "PayPal payment does not match the order, contact admin");
  }
  const { error } = await service().rpc("apply_paid_order", { p_order: order.id, p_capture_id: cap.id, p_actor: null });
  if (error) throw new HttpError(500, error.message);
  return { status: "paid" };
}

Deno.serve(async (req) => {
  const h = { "Content-Type": "application/json", "Cache-Control": "no-store", ...cors(req) };
  if (req.method === "OPTIONS") return new Response(null, { status: 204, headers: cors(req) });
  try {
    if (req.method !== "POST") throw new HttpError(405, "POST only");
    const body = await req.json().catch(() => ({}));
    const out = body.action === "capture" ? await capture(req, body) : await create(req, body);
    return new Response(JSON.stringify(out), { headers: h });
  } catch (e) {
    const status = e instanceof HttpError ? e.status : 500;
    if (!(e instanceof HttpError)) console.error(e);
    return new Response(JSON.stringify({ error: e instanceof HttpError ? e.message : "server error" }), { status, headers: h });
  }
});
