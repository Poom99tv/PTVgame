// GET /functions/v1/dl?ref=AGENTCODE — an agent's personal download link.
// Records the click for that agent, then redirects to the launcher download page.
// Customers still enter the agent code in the store (10% off first purchase) to link their ID.
import { createClient } from "npm:@supabase/supabase-js@2";

const FALLBACK = "https://github.com/Poom99tv/PTVgame/releases/latest";

Deno.serve(async (req) => {
  const ref = (new URL(req.url).searchParams.get("ref") ?? "").replace(/[^A-Za-z0-9]/g, "").slice(0, 12);
  let target = FALLBACK;
  try {
    const db = createClient(Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!, { auth: { persistSession: false } });
    const { data } = await db.rpc("agent_download_click", { p_code: ref });
    if (typeof data === "string" && data.startsWith("https://")) target = data;
  } catch (e) {
    console.error(e);
  }
  return new Response(null, { status: 302, headers: { Location: target, "Cache-Control": "no-store" } });
});
