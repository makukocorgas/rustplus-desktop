// Edge Function: server-wipe-maps
// POST /server-wipe-maps                                     multipart: server_key, wipe_key, world_size,
//      world_rect_x/y/width/height, ocean_margin, monuments (JSON string), wipe_started_at?, map_image (PNG file) -> { ok: true }
// GET  /server-wipe-maps/{serverKey}/{wipeKey}                -> { data: {...} } | 404
// GET  /server-wipe-maps/{serverKey}/{wipeKey}/image           -> raw PNG bytes | 404
// POST /server-wipe-maps/{serverKey}/{wipeKey}/extra-monuments body: { extra_monuments } -> { ok: true }
//
// Ownership: scoped to owner_steam_id, taken from the caller's JWT.

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const BUCKET = "wipe-maps";

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
  const segments = url.pathname.split("/").filter(Boolean);
  const slugIdx = segments.indexOf("server-wipe-maps");
  const route = slugIdx >= 0 ? segments.slice(slugIdx + 1) : segments;

  const steamId = getSteamIdFromJwt(req);
  if (!steamId) return json({ error: "Não autenticado" }, 401);

  // POST / — multipart upload
  if (route.length === 0 && req.method === "POST") {
    const form = await req.formData();
    const serverKey = form.get("server_key")?.toString();
    const wipeKey = form.get("wipe_key")?.toString();
    if (!serverKey || !wipeKey) return json({ error: "server_key e wipe_key obrigatórios" }, 400);

    const worldSize = form.get("world_size") ? Number(form.get("world_size")) : null;
    const rectX = form.get("world_rect_x") ? Number(form.get("world_rect_x")) : null;
    const rectY = form.get("world_rect_y") ? Number(form.get("world_rect_y")) : null;
    const rectW = form.get("world_rect_width") ? Number(form.get("world_rect_width")) : null;
    const rectH = form.get("world_rect_height") ? Number(form.get("world_rect_height")) : null;
    const oceanMargin = form.get("ocean_margin") ? Number(form.get("ocean_margin")) : null;
    const wipeStartedAt = form.get("wipe_started_at")?.toString() || null;

    let monuments: unknown[] = [];
    const monumentsRaw = form.get("monuments")?.toString();
    if (monumentsRaw) {
      try { monuments = JSON.parse(monumentsRaw); } catch { /* leave empty */ }
    }

    let mapImagePath: string | null = null;
    const file = form.get("map_image");
    if (file instanceof File) {
      const bytes = new Uint8Array(await file.arrayBuffer());
      mapImagePath = `${steamId}/${encodeURIComponent(serverKey)}/${encodeURIComponent(wipeKey)}/map.png`;
      const { error: upErr } = await supabase.storage.from(BUCKET).upload(mapImagePath, bytes, {
        contentType: "image/png",
        upsert: true,
      });
      if (upErr) return json({ error: `upload falhou: ${upErr.message}` }, 500);
    }

    const { data: existing } = await supabase
      .from("server_wipe_maps")
      .select("map_image_path")
      .eq("server_key", serverKey)
      .eq("wipe_key", wipeKey)
      .maybeSingle();

    const { error } = await supabase.from("server_wipe_maps").upsert({
      server_key: serverKey,
      wipe_key: wipeKey,
      world_size: worldSize,
      world_rect_x: rectX,
      world_rect_y: rectY,
      world_rect_width: rectW,
      world_rect_height: rectH,
      ocean_margin: oceanMargin,
      monuments,
      wipe_started_at: wipeStartedAt,
      map_image_path: mapImagePath ?? existing?.map_image_path ?? null,
      owner_steam_id: steamId,
      updated_at: new Date().toISOString(),
    }, { onConflict: "server_key,wipe_key" });

    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // GET /{serverKey}/{wipeKey}/image
  if (route.length === 3 && route[2] === "image" && req.method === "GET") {
    const serverKey = decodeURIComponent(route[0]);
    const wipeKey = decodeURIComponent(route[1]);

    const { data: row } = await supabase
      .from("server_wipe_maps")
      .select("map_image_path")
      .eq("server_key", serverKey)
      .eq("wipe_key", wipeKey)
      .eq("owner_steam_id", steamId)
      .maybeSingle();

    if (!row?.map_image_path) return json({ error: "sem imagem" }, 404);

    const { data: fileData, error } = await supabase.storage.from(BUCKET).download(row.map_image_path);
    if (error || !fileData) return json({ error: "imagem não encontrada" }, 404);

    return new Response(fileData, { headers: { ...CORS, "Content-Type": "image/png" } });
  }

  // GET /{serverKey}/{wipeKey}
  if (route.length === 2 && req.method === "GET") {
    const serverKey = decodeURIComponent(route[0]);
    const wipeKey = decodeURIComponent(route[1]);

    const { data: row } = await supabase
      .from("server_wipe_maps")
      .select("*")
      .eq("server_key", serverKey)
      .eq("wipe_key", wipeKey)
      .eq("owner_steam_id", steamId)
      .maybeSingle();

    if (!row) return json({ error: "não encontrado" }, 404);

    return json({
      data: {
        world_size: row.world_size,
        world_rect_x: row.world_rect_x,
        world_rect_y: row.world_rect_y,
        world_rect_width: row.world_rect_width,
        world_rect_height: row.world_rect_height,
        ocean_margin: row.ocean_margin,
        monuments: row.monuments ?? [],
        extra_monuments: row.extra_monuments ?? [],
        has_map_image: !!row.map_image_path,
        extra_monuments_uploaded_at: row.extra_monuments_uploaded_at,
      },
    });
  }

  // POST /{serverKey}/{wipeKey}/extra-monuments
  if (route.length === 3 && route[2] === "extra-monuments" && req.method === "POST") {
    const serverKey = decodeURIComponent(route[0]);
    const wipeKey = decodeURIComponent(route[1]);
    const body = await req.json();
    const extraMonuments = body?.extra_monuments ?? [];

    const { error } = await supabase
      .from("server_wipe_maps")
      .update({
        extra_monuments: extraMonuments,
        extra_monuments_uploaded_at: new Date().toISOString(),
        updated_at: new Date().toISOString(),
      })
      .eq("server_key", serverKey)
      .eq("wipe_key", wipeKey)
      .eq("owner_steam_id", steamId);

    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  return json({ error: "Rota não encontrada" }, 404);
});
