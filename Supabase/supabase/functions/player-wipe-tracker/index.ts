// Edge Function: player-wipe-tracker
// GET  /bootstrap                                -> { data: { plan_code, limits } }  (entitlements)
// PUT  /days                                    body: CloudDayUploadRequest -> { ok: true }
// GET  /wipes                                   -> { data: CloudArchiveSummary[] }
// GET  /wipes/{archiveId}                       -> { data: CloudArchiveSummary }
// GET  /wipes/{archiveId}/players/{steamId}?from=&to=   -> { data: [{payload, day, player_steam_id, player_name}] }
// DELETE /wipes/{archiveId}                     -> { ok: true }
// DELETE /                                      -> { ok: true }  (wipe everything for this account)
//
// archiveId = encodeURIComponent(server_key) + "~" + encodeURIComponent(wipe_key)
// Ownership: every row is scoped to owner_steam_id, taken from the caller's JWT — never trusted from the body.

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

function encodeArchiveId(serverKey: string, wipeKey: string): string {
  return `${encodeURIComponent(serverKey)}~${encodeURIComponent(wipeKey)}`;
}

function decodeArchiveId(archiveId: string): { serverKey: string; wipeKey: string } | null {
  const idx = archiveId.indexOf("~");
  if (idx < 0) return null;
  return {
    serverKey: decodeURIComponent(archiveId.slice(0, idx)),
    wipeKey: decodeURIComponent(archiveId.slice(idx + 1)),
  };
}

// deno-lint-ignore no-explicit-any
async function buildArchiveSummary(supabase: any, ownerSteamId: string, serverKey: string, wipeKey: string) {
  const { data: rows } = await supabase
    .from("player_wipe_tracker_days")
    .select("player_steam_id, player_name, wipe_started_at, payload, created_at, updated_at")
    .eq("owner_steam_id", ownerSteamId)
    .eq("server_key", serverKey)
    .eq("wipe_key", wipeKey);

  if (!rows || rows.length === 0) return null;

  const players = new Map<string, { day_count: number; player_name: string | null }>();
  let firstObserved: string | null = null;
  let lastObserved: string | null = null;
  let storedBytes = 0;
  let wipeStartedAt: string | null = null;

  for (const row of rows) {
    const p = players.get(row.player_steam_id) ?? { day_count: 0, player_name: row.player_name };
    p.day_count += 1;
    if (row.player_name) p.player_name = row.player_name;
    players.set(row.player_steam_id, p);

    if (!firstObserved || row.created_at < firstObserved) firstObserved = row.created_at;
    if (!lastObserved || row.updated_at > lastObserved) lastObserved = row.updated_at;
    if (!wipeStartedAt && row.wipe_started_at) wipeStartedAt = row.wipe_started_at;
    storedBytes += JSON.stringify(row.payload ?? {}).length;
  }

  const steamIds = [...players.keys()];
  const { data: profiles } = await supabase
    .from("user_profiles")
    .select("steam_id, user_id, discord_name")
    .in("steam_id", steamIds);
  const profileMap = new Map((profiles ?? []).map((p: any) => [p.steam_id, p]));

  const { data: serverRow } = await supabase
    .from("personal_servers")
    .select("server_name")
    .eq("server_key", serverKey)
    .maybeSingle();

  const { data: mapRow } = await supabase
    .from("server_wipe_maps")
    .select("map_image_path")
    .eq("server_key", serverKey)
    .eq("wipe_key", wipeKey)
    .maybeSingle();

  return {
    id: encodeArchiveId(serverKey, wipeKey),
    server_key: serverKey,
    server_name: serverRow?.server_name ?? null,
    wipe_key: wipeKey,
    wipe_started_at: wipeStartedAt,
    first_observed_at: firstObserved,
    last_observed_at: lastObserved,
    player_count: players.size,
    stored_bytes: storedBytes,
    players: steamIds.map((steamId) => {
      const p = players.get(steamId)!;
      const profile = profileMap.get(steamId) as any;
      return {
        steam_id: steamId,
        day_count: p.day_count,
        player_name: p.player_name,
        is_linked: !!profile?.user_id,
        user_id: profile?.user_id ?? null,
        display_name: profile?.discord_name ?? null,
      };
    }),
    has_map: !!mapRow,
    map_url: mapRow ? `${SUPABASE_URL}/functions/v1/server-wipe-maps/${encodeURIComponent(serverKey)}/${encodeURIComponent(wipeKey)}/image` : null,
  };
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);
  const segments = url.pathname.split("/").filter(Boolean);
  const slugIdx = segments.indexOf("player-wipe-tracker");
  const route = slugIdx >= 0 ? segments.slice(slugIdx + 1) : segments;

  const steamId = getSteamIdFromJwt(req);
  if (!steamId) return json({ error: "Não autenticado" }, 401);

  // GET /bootstrap — entitlements for the current account
  if (route.length === 1 && route[0] === "bootstrap" && req.method === "GET") {
    const { data: profile } = await supabase
      .from("user_profiles")
      .select("subscription_tier, is_manual_supporter, premium_until")
      .eq("steam_id", steamId)
      .maybeSingle();

    const now = new Date();
    const isPremium = !!profile && (
      profile.is_manual_supporter ||
      (profile.subscription_tier && profile.subscription_tier !== "free" && profile.subscription_tier !== "guest") ||
      (profile.premium_until && new Date(profile.premium_until) > now)
    );
    const planCode = isPremium ? (profile?.subscription_tier || "supporter") : "free";

    const { data: tier } = await supabase
      .from("tier_limits")
      .select("*")
      .eq("tier_code", planCode)
      .maybeSingle();

    const t = tier ?? {
      max_overlay_kb: null, max_devices: null, max_bases: null, max_screenshots_per_base: null,
      pwt_access: true, pwt_team_tracking: false, pwt_cloud_sync: false, pwt_advanced_views: false,
      pwt_route_replay: false, pwt_export: false,
      pwt_max_tracked_players: 1, pwt_retained_wipes: 1, pwt_cloud_retention_days: 0,
    };

    return json({
      data: {
        plan_code: planCode,
        limits: {
          sync: {
            max_overlay_kb: { value: t.max_overlay_kb },
            max_devices: { value: t.max_devices },
            max_bases: { value: t.max_bases },
            max_screenshots_per_base: { value: t.max_screenshots_per_base },
          },
          player_wipe_tracker: {
            access: { enabled: t.pwt_access },
            team_tracking: { enabled: t.pwt_team_tracking },
            cloud_sync: { enabled: t.pwt_cloud_sync },
            advanced_views: { enabled: t.pwt_advanced_views },
            route_replay: { enabled: t.pwt_route_replay },
            export: { enabled: t.pwt_export },
            max_tracked_players: { value: t.pwt_max_tracked_players },
            retained_wipes: { value: t.pwt_retained_wipes },
            cloud_retention_days: { value: t.pwt_cloud_retention_days },
          },
        },
      },
    });
  }

  // PUT /days
  if (route.length === 1 && route[0] === "days" && req.method === "PUT") {
    const body = await req.json();
    const { server_key, wipe_key, wipe_started_at, player_steam_id, player_name, day, schema_version, payload, checksum } = body;

    if (!server_key || !wipe_key || !player_steam_id || !day || !payload || !checksum) {
      return json({ error: "campos obrigatórios em falta" }, 400);
    }

    const { error } = await supabase.from("player_wipe_tracker_days").upsert({
      server_key, wipe_key,
      wipe_started_at: wipe_started_at ?? null,
      player_steam_id, player_name: player_name ?? null,
      day, schema_version: schema_version ?? 1,
      payload, checksum,
      owner_steam_id: steamId,
      updated_at: new Date().toISOString(),
    }, { onConflict: "server_key,wipe_key,player_steam_id,day" });

    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // GET /wipes
  if (route.length === 1 && route[0] === "wipes" && req.method === "GET") {
    const { data: rows } = await supabase
      .from("player_wipe_tracker_days")
      .select("server_key, wipe_key")
      .eq("owner_steam_id", steamId);

    const seen = new Set<string>();
    const pairs: { serverKey: string; wipeKey: string }[] = [];
    for (const row of rows ?? []) {
      const key = `${row.server_key}::${row.wipe_key}`;
      if (seen.has(key)) continue;
      seen.add(key);
      pairs.push({ serverKey: row.server_key, wipeKey: row.wipe_key });
    }

    const summaries = await Promise.all(
      pairs.map((p) => buildArchiveSummary(supabase, steamId, p.serverKey, p.wipeKey))
    );
    return json({ data: summaries.filter(Boolean) });
  }

  // GET/DELETE /wipes/{archiveId}
  if (route.length === 2 && route[0] === "wipes") {
    const decoded = decodeArchiveId(route[1]);
    if (!decoded) return json({ error: "archiveId inválido" }, 400);

    if (req.method === "GET") {
      const summary = await buildArchiveSummary(supabase, steamId, decoded.serverKey, decoded.wipeKey);
      if (!summary) return json({ error: "não encontrado" }, 404);
      return json({ data: summary });
    }

    if (req.method === "DELETE") {
      const { error } = await supabase.from("player_wipe_tracker_days")
        .delete()
        .eq("owner_steam_id", steamId)
        .eq("server_key", decoded.serverKey)
        .eq("wipe_key", decoded.wipeKey);
      if (error) return json({ error: error.message }, 500);
      return json({ ok: true });
    }
  }

  // GET /wipes/{archiveId}/players/{steamId}?from=&to=
  if (route.length === 4 && route[0] === "wipes" && route[2] === "players" && req.method === "GET") {
    const decoded = decodeArchiveId(route[1]);
    if (!decoded) return json({ error: "archiveId inválido" }, 400);
    const targetSteamId = decodeURIComponent(route[3]);
    const from = url.searchParams.get("from");
    const to = url.searchParams.get("to");

    let query = supabase
      .from("player_wipe_tracker_days")
      .select("payload, day, player_steam_id, player_name")
      .eq("owner_steam_id", steamId)
      .eq("server_key", decoded.serverKey)
      .eq("wipe_key", decoded.wipeKey)
      .eq("player_steam_id", targetSteamId)
      .order("day", { ascending: true });

    if (from) query = query.gte("day", from);
    if (to) query = query.lte("day", to);

    const { data: rows, error } = await query;
    if (error) return json({ error: error.message }, 500);
    return json({ data: rows ?? [] });
  }

  // DELETE / (all archives for this account)
  if (route.length === 0 && req.method === "DELETE") {
    const { error } = await supabase.from("player_wipe_tracker_days").delete().eq("owner_steam_id", steamId);
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  return json({ error: "Rota não encontrada" }, 404);
});
