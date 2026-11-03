// Shared helpers for the PayPal edge functions (Deno / Supabase Edge Runtime).
import { createClient, SupabaseClient } from "npm:@supabase/supabase-js@2";

export const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
export const ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;
const SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const PAYPAL_ENV = (Deno.env.get("PAYPAL_ENV") ?? "sandbox").toLowerCase();
const PAYPAL_BASE = PAYPAL_ENV === "live" ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

export function serviceClient(): SupabaseClient {
  return createClient(SUPABASE_URL, SERVICE_KEY, { auth: { persistSession: false } });
}

/** Client that runs SQL functions as the signed-in launcher user (RLS + auth.uid() apply). */
export function userClient(req: Request): SupabaseClient {
  return createClient(SUPABASE_URL, ANON_KEY, {
    auth: { persistSession: false },
    global: { headers: { Authorization: req.headers.get("Authorization") ?? "" } },
  });
}

export async function requireUser(req: Request): Promise<string> {
  const token = (req.headers.get("Authorization") ?? "").replace(/^Bearer\s+/i, "");
  if (!token) throw new HttpError(401, "sign in required");
  const { data, error } = await serviceClient().auth.getUser(token);
  if (error || !data.user) throw new HttpError(401, "session expired, sign in again");
  return data.user.id;
}

export class HttpError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

let cachedToken: { value: string; expires: number } | null = null;
async function accessToken(): Promise<string> {
  if (cachedToken && cachedToken.expires > Date.now() + 60_000) return cachedToken.value;
  const id = Deno.env.get("PAYPAL_CLIENT_ID"), secret = Deno.env.get("PAYPAL_SECRET");
  if (!id || !secret) throw new HttpError(503, "PayPal is not configured on the server");
  const r = await fetch(`${PAYPAL_BASE}/v1/oauth2/token`, {
    method: "POST",
    headers: { Authorization: "Basic " + btoa(`${id}:${secret}`), "Content-Type": "application/x-www-form-urlencoded" },
    body: "grant_type=client_credentials",
  });
  if (!r.ok) throw new HttpError(502, "PayPal authentication failed");
  const j = await r.json();
  cachedToken = { value: j.access_token, expires: Date.now() + j.expires_in * 1000 };
  return cachedToken.value;
}

export async function paypal(path: string, method: string, body?: unknown, requestId?: string): Promise<{ status: number; data: any }> {
  const headers: Record<string, string> = { Authorization: `Bearer ${await accessToken()}`, "Content-Type": "application/json", Prefer: "return=representation" };
  if (requestId) headers["PayPal-Request-Id"] = requestId;
  const r = await fetch(PAYPAL_BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await r.text();
  let data: any = {};
  try { data = text ? JSON.parse(text) : {}; } catch { data = { raw: text }; }
  return { status: r.status, data };
}

/** Only loopback URLs handled by the launcher are allowed as PayPal return targets. */
export function safeReturnUrl(url: unknown): string {
  if (typeof url !== "string") throw new HttpError(400, "return_url required");
  const u = new URL(url);
  if (u.protocol !== "http:" || !["127.0.0.1", "localhost"].includes(u.hostname)) throw new HttpError(400, "return_url must be the launcher loopback address");
  return u.toString();
}

export interface OrderRow {
  id: string; order_no: number; buyer_id: string; amount: string | number; currency: string; method: string;
  status: string; plan_code: string; days: number; paypal_order_id: string | null;
}

/**
 * Captures (if needed) and verifies a PayPal order against our order row, then grants VIP.
 * Safe to call more than once: PayPal-Request-Id makes the capture idempotent and
 * apply_paid_order returns early for orders that are already paid.
 */
export async function captureAndApply(order: OrderRow): Promise<{ status: string }> {
  if (order.status === "paid") return { status: "paid" };
  if (!order.paypal_order_id) throw new HttpError(400, "order has no PayPal payment");

  let res = await paypal(`/v2/checkout/orders/${order.paypal_order_id}/capture`, "POST", {}, `capture-${order.id}`);
  if (res.status === 422 || res.status === 409) {
    // already captured or not yet approved: read the current state instead
    res = await paypal(`/v2/checkout/orders/${order.paypal_order_id}`, "GET");
  }
  const po = res.data;
  if (po?.status === "APPROVED" || po?.status === "CREATED" || po?.status === "PAYER_ACTION_REQUIRED") return { status: "waiting" };
  const unit = po?.purchase_units?.[0];
  const capture = unit?.payments?.captures?.[0];
  if (po?.status !== "COMPLETED" || !capture) return { status: "failed" };
  if (capture.status === "PENDING") return { status: "waiting" };                 // e.g. e-check / review
  if (capture.status !== "COMPLETED") return { status: "failed" };

  const expected = Number(order.amount).toFixed(2);
  const ok = (unit.custom_id ?? capture.custom_id) === order.id
    && capture.amount?.currency_code === order.currency
    && Number(capture.amount?.value).toFixed(2) === expected;
  if (!ok) {
    await serviceClient().from("audit_log").insert({ action: "paypal_mismatch", detail: { order: order.id, paypal: order.paypal_order_id } });
    throw new HttpError(409, "PayPal payment does not match the order, contact admin");
  }
  const { error } = await serviceClient().rpc("apply_paid_order", { p_order: order.id, p_capture_id: capture.id, p_actor: null });
  if (error) throw new HttpError(500, error.message);
  return { status: "paid" };
}

export function handle(fn: (req: Request) => Promise<Response>) {
  Deno.serve(async (req) => {
    try {
      if (req.method !== "POST") return json({ error: "POST only" }, 405);
      return await fn(req);
    } catch (e) {
      if (e instanceof HttpError) return json({ error: e.message }, e.status);
      console.error(e);
      return json({ error: "server error" }, 500);
    }
  });
}
