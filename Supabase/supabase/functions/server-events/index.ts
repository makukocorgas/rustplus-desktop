// Edge Function: server-events
// GET  ?server_key=xxx                         -> { events: [{event_type, started_at, expires_at, confirmations, status, recent}] }
// POST /report { server_key, event_type, capture_mode, score, cue_started_at } -> { result }
//
// Mirrors durations in RustPlusDesktop/Services/EventCapabilities.cs::NominalDuration.

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

const NOMINAL_DURATION_MS: Record<string, number> = {
  "cargo": 75 * 60 * 1000,
  "deep-sea": 3 * 60 * 60 * 1000,
  "oil-rig": 80 * 60 * 1000,
};

const CORRELATION_WINDOW_MS = 120 * 1000; // must match LocalTrustToleranceSeconds in CloudEventWatcher.cs
const DUPLICATE_WINDOW_MS = 10 * 1000;
const RATE_LIMIT_PER_HOUR = 30;

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
  const segments = url.pathname.split("/").filter(Boolean);
  const last = segments.length > 0 ? segments[segments.length - 1] : "";

  // ── GET ?server_key=xxx ── current active events for a server
  if (req.method === "GET") {
    const serverKey = url.searchParams.get("server_key");
    if (!serverKey) return json({ error: "server_key obrigatório" }, 400);

    const { data: rows, error } = await supabase
      .from("server_events")
      .select("event_type, started_at, expires_at, confirmations, status, recent")
      .eq("server_key", serverKey)
      .gt("expires_at", new Date().toISOString());

    if (error) return json({ error: error.message }, 500);
    return json({ events: rows ?? [] });
  }

  // ── POST /report ──
  if (last === "report" && req.method === "POST") {
    const body = await req.json();
    const { server_key, event_type, capture_mode, score, cue_started_at } = body;

    if (!server_key || !event_type || !cue_started_at) {
      return json({ error: "server_key, event_type, cue_started_at obrigatórios" }, 400);
    }

    const steamId = getSteamIdFromJwt(req);
    if (!steamId) return json({ result: "rejected_no_profile" });

    const nominalMs = NOMINAL_DURATION_MS[event_type] ?? 30 * 60 * 1000;
    const now = new Date();
    const cueStarted = new Date(cue_started_at);

    // Profile / presence gating
    const { data: profile } = await supabase
      .from("user_profiles")
      .select("sync_accepted, is_online, current_server_key")
      .eq("steam_id", steamId)
      .maybeSingle();

    if (!profile) return json({ result: "rejected_no_profile" });
    if (!profile.sync_accepted) return json({ result: "rejected_cloud_sync_off" });
    if (!profile.is_online || profile.current_server_key !== server_key) {
      return json({ result: "rejected_not_in_game" });
    }

    const { data: presence } = await supabase
      .from("team_feature_presence")
      .select("server_key, expires_at")
      .eq("steam_id", steamId)
      .maybeSingle();

    if (!presence || presence.server_key !== server_key) {
      return json({ result: `rejected_wrong_server:${presence?.server_key ?? "none"}!=${server_key}` });
    }
    if (new Date(presence.expires_at) < now) {
      return json({ result: "rejected_stale_presence" });
    }

    // Rate limit: reports accepted from this steam_id in the last hour
    const hourAgo = new Date(now.getTime() - 60 * 60 * 1000).toISOString();
    const { count: recentCount } = await supabase
      .from("server_events")
      .select("server_key", { count: "exact", head: true })
      .contains("reporters", JSON.stringify([steamId]))
      .gte("updated_at", hourAgo);

    if ((recentCount ?? 0) >= RATE_LIMIT_PER_HOUR) {
      return json({ result: "rejected_rate_limited" });
    }

    const { data: existing } = await supabase
      .from("server_events")
      .select("*")
      .eq("server_key", server_key)
      .eq("event_type", event_type)
      .maybeSingle();

    const stillActive = existing && new Date(existing.expires_at) > now;

    if (stillActive) {
      const existingStarted = new Date(existing.started_at);
      const withinCorrelation = Math.abs(cueStarted.getTime() - existingStarted.getTime()) <= CORRELATION_WINDOW_MS;

      if (!withinCorrelation) {
        return json({ result: "rejected_still_active" });
      }

      const reporters: string[] = Array.isArray(existing.reporters) ? existing.reporters : [];
      const lastRecent: string[] = Array.isArray(existing.recent) ? existing.recent : [];

      if (reporters.includes(steamId)) {
        const lastForThis = lastRecent[lastRecent.length - 1];
        if (lastForThis && now.getTime() - new Date(lastForThis).getTime() < DUPLICATE_WINDOW_MS) {
          return json({ result: "rejected_too_soon" });
        }
      }

      const newReporters = reporters.includes(steamId) ? reporters : [...reporters, steamId];
      const newRecent = [...lastRecent, now.toISOString()].slice(-10);
      const newConfirmations = newReporters.length;
      const newStatus = newConfirmations >= 2 ? "confirmed" : existing.status;

      const { error: updErr } = await supabase
        .from("server_events")
        .update({
          confirmations: newConfirmations,
          status: newStatus,
          reporters: newReporters,
          recent: newRecent,
          updated_at: now.toISOString(),
        })
        .eq("server_key", server_key)
        .eq("event_type", event_type);

      if (updErr) return json({ error: updErr.message }, 500);

      await broadcastRefresh(supabase, server_key);
      return json({ result: "corroborated" });
    }

    // Duplicate-fire guard for a fresh occurrence from the same reporter
    if (existing && !stillActive) {
      const lastRecent: string[] = Array.isArray(existing.recent) ? existing.recent : [];
      const lastForThis = lastRecent[lastRecent.length - 1];
      if (lastForThis && now.getTime() - new Date(lastForThis).getTime() < DUPLICATE_WINDOW_MS) {
        return json({ result: "rejected_too_soon" });
      }
    }

    const expiresAt = new Date(cueStarted.getTime() + nominalMs);
    const { error: insErr } = await supabase.from("server_events").upsert({
      server_key,
      event_type,
      started_at: cueStarted.toISOString(),
      expires_at: expiresAt.toISOString(),
      confirmations: 1,
      status: "pending",
      reporters: [steamId],
      recent: [now.toISOString()],
      created_at: now.toISOString(),
      updated_at: now.toISOString(),
    }, { onConflict: "server_key,event_type" });

    if (insErr) return json({ error: insErr.message }, 500);

    await broadcastRefresh(supabase, server_key);
    return json({ result: "accepted" });
  }

  return json({ error: "Rota não encontrada" }, 404);
});

async function broadcastRefresh(supabase: ReturnType<typeof createClient>, serverKey: string) {
  try {
    const channelName = `server_events:${serverKey.replaceAll(".", "_")}`;
    const channel = supabase.channel(channelName);
    await channel.send({ type: "broadcast", event: "refresh", payload: {} });
    await supabase.removeChannel(channel);
  } catch (e) {
    console.error("[server-events] broadcast failed:", (e as Error).message);
  }
}
