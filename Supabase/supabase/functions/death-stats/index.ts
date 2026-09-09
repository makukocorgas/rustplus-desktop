// Edge Function: death-stats
// POST /death-stats           body: {server_key, victim_steam_id, victim_name, pos_x, pos_y, grid,
//                                     location_type, location_name, died_at, spawn_at} -> { ok: true }
// DELETE /death-stats?server_key=xxx  -> { ok: true }  (clears this account's log for a server, e.g. on wipe)
// GET /death-stats?server_key=xxx     -> { data: [...] }
//
// Ownership: scoped to owner_steam_id, taken from the caller's JWT. Best-effort from the client, so
// failures here should never be surfaced as anything louder than a log line on the C# side.

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

function getSteamIdFromJwt(req: Request): string | null {
  try {
    const auth = req.headers.get("authorization") ?? "";
    const token = auth.replace("Bearer ", "").trim();
    if (!token) return null;
    const parts = token.split(".");
    if (parts.length !== 3) return null;
    const padded = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const payload = JSON.parse(atob(padded));
    return payload.steam_id ?? payload.user_metadata?.steam_id ?? null;
  } catch {
    return null;
  }
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);

  const steamId = getSteamIdFromJwt(req);
  if (!steamId) return json({ error: "Não autenticado" }, 401);

  if (req.method === "POST") {
    const body = await req.json();
    const { server_key, victim_steam_id, victim_name, pos_x, pos_y, grid, location_type, location_name, died_at, spawn_at } = body;

    if (!server_key || !victim_steam_id || !died_at) {
      return json({ error: "server_key, victim_steam_id, died_at obrigatórios" }, 400);
    }

    const { error } = await supabase.from("death_logs").insert({
      server_key, victim_steam_id, victim_name: victim_name ?? null,
      pos_x: pos_x ?? null, pos_y: pos_y ?? null, grid: grid ?? null,
      location_type: location_type ?? null, location_name: location_name ?? null,
      died_at, spawn_at: spawn_at ?? null,
      owner_steam_id: steamId,
    });

    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  if (req.method === "GET") {
    const serverKey = url.searchParams.get("server_key");
    if (!serverKey) return json({ error: "server_key obrigatório" }, 400);

    const { data, error } = await supabase
      .from("death_logs")
      .select("*")
      .eq("owner_steam_id", steamId)
      .eq("server_key", serverKey)
      .order("died_at", { ascending: false });

    if (error) return json({ error: error.message }, 500);
    return json({ data: data ?? [] });
  }

  if (req.method === "DELETE") {
    const serverKey = url.searchParams.get("server_key");
    if (!serverKey) return json({ error: "server_key obrigatório" }, 400);

    const { error } = await supabase
      .from("death_logs")
      .delete()
      .eq("owner_steam_id", steamId)
      .eq("server_key", serverKey);

    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  return json({ error: "Método não suportado" }, 405);
});
