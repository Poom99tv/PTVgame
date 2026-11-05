// play-ticket: signed, short-lived tickets that let a VIP start a PTVgame game.
//
//   POST /functions/v1/play-ticket  { "game": "zombierush" }   (Authorization: Bearer <user session>)
//     -> { ticket, expires_at }      ticket = base64url(claims JSON) + "." + base64url(RSA-SHA256 signature)
//   GET  /functions/v1/play-ticket?pub=1
//     -> { kid, n, e }               the public key (base64url modulus / exponent) to build into games
//
// The RSA key pair is created on first use and kept in public.signing_keys, which only the service role can
// read, so no secret ever has to be copied by hand. Games verify with the public key only.
import { createClient } from "npm:@supabase/supabase-js@2";

const URL_ = Deno.env.get("SUPABASE_URL")!;
const ANON = Deno.env.get("SUPABASE_ANON_KEY")!;
const SERVICE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const KID = "play-v1";
const TICKET_SECONDS = 600;   // the game must be started within 10 minutes of asking

const ALG = { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" } as const;
const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "authorization, apikey, content-type" };
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", "Cache-Control": "no-store", ...cors } });
const b64url = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

let keyCache: { priv: CryptoKey; pub: JsonWebKey } | null = null;
async function signingKey() {
  if (keyCache) return keyCache;
  const db = createClient(URL_, SERVICE, { auth: { persistSession: false } });
  let { data } = await db.from("signing_keys").select("private_jwk, public_jwk").eq("id", KID).maybeSingle();
  if (!data) {
    const pair = await crypto.subtle.generateKey({ ...ALG, modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]) }, true, ["sign", "verify"]) as CryptoKeyPair;
    const row = { id: KID, private_jwk: await crypto.subtle.exportKey("jwk", pair.privateKey), public_jwk: await crypto.subtle.exportKey("jwk", pair.publicKey) };
    await db.from("signing_keys").upsert(row, { onConflict: "id", ignoreDuplicates: true });
    ({ data } = await db.from("signing_keys").select("private_jwk, public_jwk").eq("id", KID).single());   // whoever won a race
  }
  const priv = await crypto.subtle.importKey("jwk", data!.private_jwk, ALG, false, ["sign"]);
  keyCache = { priv, pub: data!.public_jwk };
  return keyCache;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { status: 204, headers: cors });
  try {
    if (req.method === "GET") {
      const { pub } = await signingKey();
      return json({ kid: KID, alg: "RS256", n: pub.n, e: pub.e });
    }
    if (req.method !== "POST") return json({ error: "method not allowed" }, 405);
    const auth = req.headers.get("Authorization") ?? "";
    if (!/^Bearer\s+\S+/i.test(auth)) return json({ error: "sign in required" }, 401);
    const body = await req.json().catch(() => ({}));
    const game = String(body.game ?? "");
    const user = createClient(URL_, ANON, { auth: { persistSession: false }, global: { headers: { Authorization: auth } } });
    const { data: claims, error } = await user.rpc("play_ticket_claims", { p_game: game });
    if (error) {
      const msg = error.message || "not allowed";
      return json({ error: msg }, /VIP required|suspended/.test(msg) ? 403 : /sign in|JWT/i.test(msg) ? 401 : 400);
    }
    const now = Math.floor(Date.now() / 1000);
    const payload = { v: 1, kid: KID, ...claims, iat: now, exp: now + TICKET_SECONDS, nonce: crypto.randomUUID() };
    const head = b64url(new TextEncoder().encode(JSON.stringify(payload)));
    const { priv } = await signingKey();
    const sig = new Uint8Array(await crypto.subtle.sign(ALG, priv, new TextEncoder().encode(head)));
    return json({ ticket: head + "." + b64url(sig), expires_at: new Date(payload.exp * 1000).toISOString() });
  } catch (e) {
    console.error(e);
    return json({ error: "ticket service error" }, 500);
  }
});
