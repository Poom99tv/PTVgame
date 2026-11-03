// deno test --allow-env server/supabase/functions/_shared/paypal_test.ts
// Mocks PayPal + Supabase over fetch to check that VIP is granted only for an exact match.
Deno.env.set("SUPABASE_URL", "https://example.supabase.co");
Deno.env.set("SUPABASE_ANON_KEY", "anon");
Deno.env.set("SUPABASE_SERVICE_ROLE_KEY", "service");
Deno.env.set("PAYPAL_CLIENT_ID", "id");
Deno.env.set("PAYPAL_SECRET", "secret");
const { captureAndApply, HttpError } = await import("./paypal.ts");

function assert(c: boolean, m: string) { if (!c) throw new Error(m); }
const ORDER = { id: "11111111-1111-1111-1111-111111111111", order_no: 7, buyer_id: "b", amount: "20.00", currency: "USD", method: "paypal", status: "pending", plan_code: "monthly", days: 30, paypal_order_id: "PP1" };

function mock(paypalOrder: any, captureStatus = 201) {
  const calls: string[] = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    calls.push((init?.method ?? "GET") + " " + url);
    if (url.includes("/v1/oauth2/token")) return new Response(JSON.stringify({ access_token: "t", expires_in: 3600 }));
    if (url.endsWith("/capture")) return new Response(JSON.stringify(captureStatus === 201 ? paypalOrder : { name: "UNPROCESSABLE_ENTITY" }), { status: captureStatus });
    if (url.includes("/v2/checkout/orders/")) return new Response(JSON.stringify(paypalOrder));
    if (url.includes("/rest/v1/rpc/apply_paid_order")) return new Response(JSON.stringify({ status: "paid" }));
    if (url.includes("/rest/v1/audit_log")) return new Response("", { status: 201 });
    return new Response("{}", { status: 404 });
  }) as typeof fetch;
  return calls;
}
const paid = (value: string, currency = "USD", custom = ORDER.id) => ({
  status: "COMPLETED",
  purchase_units: [{ custom_id: custom, payments: { captures: [{ id: "CAP1", status: "COMPLETED", amount: { value, currency_code: currency } }] } }],
});

Deno.test("exact payment grants VIP", async () => {
  const calls = mock(paid("20.00"));
  const r = await captureAndApply(ORDER);
  assert(r.status === "paid", "should be paid");
  assert(calls.some((c) => c.includes("apply_paid_order")), "apply_paid_order called");
});
Deno.test("already captured (422) falls back to GET", async () => {
  const calls = mock(paid("20.00"), 422);
  assert((await captureAndApply(ORDER)).status === "paid", "paid via GET");
  assert(calls.some((c) => c.startsWith("GET") && c.endsWith("/v2/checkout/orders/PP1")), "GET used");
});
for (const [name, po] of [["lower amount", paid("0.01")], ["wrong currency", paid("20.00", "THB")], ["other order id", paid("20.00", "USD", "other")]] as const) {
  Deno.test(`${name} is rejected`, async () => {
    const calls = mock(po);
    let threw = false;
    try { await captureAndApply(ORDER); } catch (e) { threw = e instanceof HttpError; }
    assert(threw, "must throw");
    assert(!calls.some((c) => c.includes("apply_paid_order")), "VIP must not be granted");
  });
}
Deno.test("not yet approved waits", async () => {
  const calls = mock({ status: "PAYER_ACTION_REQUIRED" }, 422);
  assert((await captureAndApply(ORDER)).status === "waiting", "waiting");
  assert(!calls.some((c) => c.includes("apply_paid_order")), "no grant");
});
Deno.test("pending capture waits", async () => {
  const po = paid("20.00"); po.purchase_units[0].payments.captures[0].status = "PENDING";
  mock(po);
  assert((await captureAndApply(ORDER)).status === "waiting", "waiting");
});
