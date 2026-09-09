// Edge Function: feature-flags
// Public, admin-controlled on/off switches for optional integrations. No auth: the same feed
// answers the same way for every account, so there is nothing here to scope to a caller.
//
// GET feature-flags -> { data: { <key>: { enabled, status_note } } }

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-key, content-type, apikey, x-client-version",
};

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { ...CORS, "Content-Type": "application/json" },
  });
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });
  if (req.method !== "GET") return json({ error: "Método não suportado" }, 405);

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const { data, error } = await supabase.from("feature_flags").select("key, enabled, status_note");
  if (error) return json({ error: error.message }, 500);

  const flags: Record<string, { enabled: boolean; status_note: string | null }> = {};
  for (const row of data ?? []) {
    flags[row.key] = { enabled: row.enabled, status_note: row.status_note ?? null };
  }

  return json({ data: flags });
});
