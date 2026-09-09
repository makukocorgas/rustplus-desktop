// Edge Function: user-profile
// Pasta: supabase/functions/user-profile/index.ts
// Gere perfis de utilizador: GET, POST, /claim, /touch, /consent, /presence, /limits

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
    headers: { ...CORS, "Content-Type": "application/json" }
  });
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);
  const path = url.pathname.replace(/^.*\/user-profile\/?/, "").replace(/^\/+/, "");


  // ── GET /limits ── Devolve os limites de todos os tiers
  if (path === "limits" && req.method === "GET") {
    const { data, error } = await supabase.from("tier_limits").select("*");
    if (error) return json({ error: error.message }, 500);
    return json(data);
  }

  // ── GET /user-profile?steam_id=xxx ── Devolve perfil pelo steam_id
  if (path === "" && req.method === "GET") {
    const steamId = url.searchParams.get("steam_id");
    if (!steamId) return json({ error: "steam_id obrigatório" }, 400);

    const { data, error } = await supabase
      .from("user_profiles")
      .select("*")
      .eq("steam_id", steamId)
      .single();

    if (error || !data) return json({ profile: null });
    return json({ profile: data });
  }

  // ── POST /claim ── Liga um perfil guest a uma conta Discord/Email
  if (path === "claim" && req.method === "POST") {
    const body = await req.json();
    const { steam_id } = body;
    if (!steam_id) return json({ error: "steam_id obrigatório" }, 400);

    // Verificar token de autenticação
    const authHeader = req.headers.get("authorization");
    if (!authHeader) return json({ error: "Não autenticado" }, 401);

    const token = authHeader.replace("Bearer ", "");
    const { data: { user }, error: authError } = await supabase.auth.getUser(token);
    if (authError || !user) return json({ error: "Token inválido" }, 401);

    // Actualizar o perfil existente para ligar ao novo user_id
    const { data, error } = await supabase
      .from("user_profiles")
      .update({
        user_id: user.id,
        last_active_at: new Date().toISOString(),
      })
      .eq("steam_id", steam_id)
      .select("*");

    if (error) return json({ error: error.message }, 500);
    return json(data ?? []);
  }

  // ── POST /touch ── Actualiza last_active_at
  if (path === "touch" && req.method === "POST") {
    const body = await req.json();
    const { steam_id } = body;
    if (!steam_id) return json({ error: "steam_id obrigatório" }, 400);

    await supabase.from("user_profiles")
      .update({ last_active_at: new Date().toISOString() })
      .eq("steam_id", steam_id);

    return json({ ok: true });
  }

  // ── POST /consent ── Actualiza sync_accepted
  if (path === "consent" && req.method === "POST") {
    const body = await req.json();
    const { steam_id, sync_accepted } = body;
    if (!steam_id) return json({ error: "steam_id obrigatório" }, 400);

    await supabase.from("user_profiles")
      .update({ sync_accepted: !!sync_accepted })
      .eq("steam_id", steam_id);

    return json({ ok: true });
  }

  // ── POST /presence ── Actualiza estado online/servidor/equipa
  if (path === "presence" && req.method === "POST") {
    const body = await req.json();
    const { steam_id, is_online, current_server_key, current_server_name,
      team_member_count, team_members_json } = body;

    if (!steam_id) return json({ error: "steam_id obrigatório" }, 400);

    const update: Record<string, unknown> = {
      is_online: !!is_online,
      last_active_at: new Date().toISOString(),
    };
    if (current_server_key !== undefined) update.current_server_key = current_server_key;
    if (current_server_name !== undefined) update.current_server_name = current_server_name;
    if (team_member_count !== undefined) update.team_member_count = team_member_count;
    if (team_members_json !== undefined) update.team_members_json = team_members_json;

    await supabase.from("user_profiles").update(update).eq("steam_id", steam_id);
    return json({ ok: true });
  }

  // ── POST /user-profile ── Cria ou actualiza perfil
  if (path === "" && req.method === "POST") {
    const body = await req.json();
    const { steam_id, user_id, discord_id, discord_name,
      subscription_tier, sync_accepted, last_active_at, is_online } = body;

    if (!steam_id) return json({ error: "steam_id obrigatório" }, 400);

    const profileData = {
      steam_id,
      ...(user_id && { user_id }),
      ...(discord_id && { discord_id }),
      ...(discord_name && { discord_name }),
      subscription_tier: subscription_tier ?? "free",
      sync_accepted: sync_accepted ?? false,
      is_online: is_online ?? false,
      last_active_at: last_active_at ?? new Date().toISOString(),
    };

    const { data, error } = await supabase
      .from("user_profiles")
      .upsert(profileData, { onConflict: "steam_id" })
      .select("*");

    if (error) return json({ error: error.message }, 500);
    return json(data?.[0] ?? {});
  }

  return json({ error: "Rota não encontrada" }, 404);
});
