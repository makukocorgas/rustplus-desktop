// Edge Function: home-assistant
// Lets a user's own Home Assistant control their paired Rust smart switches through a REST
// switch entity, and lets the desktop app manage the token that authenticates it.
//
// Two different callers, two different bearers:
//   - GET/POST/DELETE home-assistant/token, GET home-assistant/commands, POST home-assistant/commands/ack,
//     POST home-assistant/state are called BY THE DESKTOP APP with the normal Supabase session JWT.
//   - GET/POST home-assistant/switch/{entityId} are called BY HOME ASSISTANT ITSELF, bearing the
//     per-user token from configuration.yaml — an opaque string, looked up in ha_tokens.
//
// Home Assistant never talks to the Rust game server directly (nothing outside the desktop app
// does). A switch call instead drops a row in ha_commands; the desktop app polls that list
// while connected and executes it over its own Rust+ connection, acknowledges what it managed
// to run, then reports the result back into ha_device_states so the next GET reflects reality.

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

function bearerToken(req: Request): string {
  return (req.headers.get("authorization") ?? "").replace("Bearer ", "").trim();
}

function getSteamIdFromJwt(req: Request): string | null {
  try {
    const token = bearerToken(req);
    const parts = token.split(".");
    if (parts.length !== 3) return null;
    const padded = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const payload = JSON.parse(atob(padded));
    return payload.steam_id ?? payload.user_metadata?.steam_id ?? null;
  } catch {
    return null;
  }
}

function randomToken(): string {
  const bytes = new Uint8Array(24);
  crypto.getRandomValues(bytes);
  return Array.from(bytes).map((b) => b.toString(16).padStart(2, "0")).join("");
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);
  const segments = url.pathname.split("/").filter(Boolean);
  const slugIdx = segments.indexOf("home-assistant");
  const route = slugIdx >= 0 ? segments.slice(slugIdx + 1) : segments;

  // ── Home Assistant's own calls: authenticated by the per-user token, not a Supabase JWT ──
  if (route[0] === "switch" && route.length === 2) {
    const entityId = route[1];
    const token = bearerToken(req);
    if (!token) return json({ error: "Não autenticado" }, 401);

    const { data: tokenRow } = await supabase.from("ha_tokens").select("steam_id").eq("token", token).maybeSingle();
    if (!tokenRow) return json({ error: "Não autenticado" }, 401);
    const steamId = tokenRow.steam_id;

    if (req.method === "GET") {
      const { data: state } = await supabase.from("ha_device_states")
        .select("is_on").eq("steam_id", steamId).eq("entity_id", entityId).maybeSingle();
      return json({ on: state?.is_on ?? false });
    }

    if (req.method === "POST") {
      const body = await req.json().catch(() => ({}));
      const turnOn = body.on === true || body.on === "true" || body.on === 1;

      const { error } = await supabase.from("ha_commands").insert({
        steam_id: steamId,
        entity_id: entityId,
        turn_on: turnOn,
      });
      if (error) return json({ error: error.message }, 500);
      return json({ on: turnOn });
    }
  }

  // ── Everything below is the desktop app, on its own Supabase session ──
  const steamId = getSteamIdFromJwt(req);
  if (!steamId) return json({ error: "Não autenticado" }, 401);

  // GET/DELETE home-assistant/token, POST home-assistant/token/regenerate
  if (route[0] === "token") {
    if (route.length === 1 && req.method === "GET") {
      const { data } = await supabase.from("ha_tokens").select("token").eq("steam_id", steamId).maybeSingle();
      return json({ data: { api_token: data?.token ?? null } });
    }
    if (route.length === 2 && route[1] === "regenerate" && req.method === "POST") {
      const token = randomToken();
      const { error } = await supabase.from("ha_tokens").upsert({ steam_id: steamId, token, created_at: new Date().toISOString() }, { onConflict: "steam_id" });
      if (error) return json({ error: error.message }, 500);
      return json({ data: { api_token: token } });
    }
    if (route.length === 1 && req.method === "DELETE") {
      const { error } = await supabase.from("ha_tokens").delete().eq("steam_id", steamId);
      if (error) return json({ error: error.message }, 500);
      return json({ ok: true });
    }
  }

  // GET home-assistant/commands — every command waiting for this account. Left in place until
  // acknowledged: a client that is not connected yet could not act on one anyway, and deleting
  // it on read would lose the request rather than leave it for the next successful poll.
  if (route.length === 1 && route[0] === "commands" && req.method === "GET") {
    const { data: rows, error } = await supabase.from("ha_commands").select("*").eq("steam_id", steamId);
    if (error) return json({ error: error.message }, 500);

    return json({
      data: (rows ?? []).map((r) => ({ id: r.id, entity_id: r.entity_id, turn_on: r.turn_on })),
    });
  }

  // POST home-assistant/commands/ack { ids: [...] } — clears commands the app actually executed
  if (route.length === 2 && route[0] === "commands" && route[1] === "ack" && req.method === "POST") {
    const body = await req.json().catch(() => ({}));
    const ids = Array.isArray(body.ids) ? body.ids : [];
    if (ids.length === 0) return json({ ok: true });

    const { error } = await supabase.from("ha_commands").delete().eq("steam_id", steamId).in("id", ids);
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // POST home-assistant/state { entity_id, is_on } — what the app actually observed after acting
  if (route.length === 1 && route[0] === "state" && req.method === "POST") {
    const body = await req.json().catch(() => ({}));
    const entityId = body.entity_id;
    if (entityId === undefined || entityId === null) return json({ error: "entity_id required" }, 400);

    const { error } = await supabase.from("ha_device_states").upsert({
      steam_id: steamId,
      entity_id: entityId,
      is_on: !!body.is_on,
      updated_at: new Date().toISOString(),
    }, { onConflict: "steam_id,entity_id" });
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  return json({ error: "Rota não encontrada" }, 404);
});
