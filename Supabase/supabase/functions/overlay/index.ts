// Edge Function: overlay
// Pasta: supabase/functions/overlay/index.ts
// GET  ?server_key=xxx&steam_id=xxx  → devolve map_overlay, base_markers, smart_devices
// POST { server_key, steam_id, map_overlay?, base_markers?, smart_devices? } → guarda

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

// Extrai steam_id do JWT (guest ou discord)
function getSteamIdFromJwt(req: Request): string | null {
  try {
    const auth = req.headers.get("authorization") ?? "";
    const token = auth.replace("Bearer ", "").trim();
    if (!token) return null;
    const parts = token.split(".");
    if (parts.length !== 3) return null;
    const padded = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const payload = JSON.parse(atob(padded));
    // guest JWT tem steam_id directo; discord JWT pode ter no user_metadata
    return payload.steam_id ?? payload.user_metadata?.steam_id ?? null;
  } catch {
    return null;
  }
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);

  // ── GET: fetch overlay ──
  if (req.method === "GET") {
    const serverKey = url.searchParams.get("server_key");
    const steamId   = url.searchParams.get("steam_id") ?? getSteamIdFromJwt(req);

    if (!serverKey || !steamId) {
      return json({ error: "server_key e steam_id obrigatórios" }, 400);
    }

    const result: Record<string, unknown> = {};

    // map_overlay
    const { data: mapRow } = await supabase
      .from("map_overlays")
      .select("overlay_data, uncompressed_size, updated_at")
      .eq("server_key", serverKey)
      .eq("steam_id", steamId)
      .maybeSingle();

    if (mapRow) result.map_overlay = mapRow;

    // base_markers
    const { data: baseRow } = await supabase
      .from("base_markers")
      .select("marker_data, updated_at")
      .eq("server_key", serverKey)
      .eq("steam_id", steamId)
      .maybeSingle();

    if (baseRow) result.base_markers = baseRow;

    // smart_devices
    const { data: devRow } = await supabase
      .from("smart_devices")
      .select("device_data, updated_at")
      .eq("server_key", serverKey)
      .eq("steam_id", steamId)
      .maybeSingle();

    if (devRow) result.smart_devices = devRow;

    return json(result);
  }

  // ── POST: push overlay ──
  if (req.method === "POST") {
    const body = await req.json();
    const { server_key, steam_id, map_overlay, base_markers, smart_devices } = body;

    if (!server_key || !steam_id) {
      return json({ error: "server_key e steam_id obrigatórios" }, 400);
    }

    const now = new Date().toISOString();

    if (map_overlay) {
      const { error } = await supabase.from("map_overlays").upsert({
        server_key,
        steam_id,
        overlay_data:       map_overlay.overlay_data,
        uncompressed_size:  map_overlay.uncompressed_size ?? 0,
        updated_at: now,
      }, { onConflict: "server_key,steam_id" });

      if (error) {
        console.error("[overlay] map_overlay upsert error:", error.message);
        return json({ error: error.message }, 500);
      }
    }

    if (base_markers) {
      const { error } = await supabase.from("base_markers").upsert({
        server_key,
        steam_id,
        marker_data: base_markers.marker_data,
        updated_at:  now,
      }, { onConflict: "server_key,steam_id" });

      if (error) {
        console.error("[overlay] base_markers upsert error:", error.message);
        return json({ error: error.message }, 500);
      }
    }

    if (smart_devices) {
      const { error } = await supabase.from("smart_devices").upsert({
        server_key,
        steam_id,
        device_data: smart_devices.device_data,
        updated_at:  now,
      }, { onConflict: "server_key,steam_id" });

      if (error) {
        console.error("[overlay] smart_devices upsert error:", error.message);
        return json({ error: error.message }, 500);
      }
    }

    console.log(`[overlay] Saved for ${steam_id} @ ${server_key}`);
    return json({ ok: true });
  }

  return json({ error: "Método não suportado" }, 405);
});