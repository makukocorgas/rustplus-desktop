// Edge Function: team-feature/heartbeat
// Pasta: supabase/functions/team-feature/index.ts
// Gere presença de equipa e eleição de Chat Master

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const HEARTBEAT_TTL_SECONDS = 30; // tempo máximo sem heartbeat para ser considerado offline

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
  // Robusto a variações de prefixo (com ou sem /functions/v1/team-feature) —
  // usa sempre o último segmento não vazio do caminho como nome da rota.
  const segments = url.pathname.split("/").filter(Boolean);
  const path = segments.length > 0 ? segments[segments.length - 1] : "";

  // ── POST /heartbeat ── Actualiza presença e devolve estado do master
  if (path === "heartbeat" && req.method === "POST") {
    const body = await req.json();
    const { steam_id, display_name, server_key, server_name,
      team_key, team_order_index, wants_chat_alerts, wants_chat_commands } = body;

    if (!steam_id || !server_key || !team_key) {
      return json({ error: "steam_id, server_key, team_key obrigatórios" }, 400);
    }

    const now = new Date();
    const expiresAt = new Date(now.getTime() + HEARTBEAT_TTL_SECONDS * 1000);

    // Verificar se é premium
    const { data: profile } = await supabase
      .from("user_profiles")
      .select("subscription_tier, is_manual_supporter, premium_until")
      .eq("steam_id", steam_id)
      .single();

    const isPremium = profile && (
      profile.is_manual_supporter ||
      (profile.subscription_tier !== "free" && profile.subscription_tier !== "guest") ||
      (profile.premium_until && new Date(profile.premium_until) > now)
    );

    // Upsert presença
    await supabase.from("team_feature_presence").upsert({
      steam_id,
      display_name: display_name ?? "",
      server_key,
      server_name: server_name ?? "",
      team_key,
      team_order_index: team_order_index ?? 0,
      wants_chat_alerts: !!wants_chat_alerts,
      wants_chat_commands: !!wants_chat_commands,
      expires_at: expiresAt.toISOString(),
      updated_at: now.toISOString(),
    }, { onConflict: "steam_id" });

    // Limpar entradas expiradas para esta equipa
    await supabase.from("team_feature_presence")
      .delete()
      .lt("expires_at", now.toISOString())
      .eq("server_key", server_key)
      .eq("team_key", team_key);

    // Buscar membros activos desta equipa
    const { data: members } = await supabase
      .from("team_feature_presence")
      .select("*")
      .eq("server_key", server_key)
      .eq("team_key", team_key)
      .gt("expires_at", now.toISOString())
      .order("team_order_index", { ascending: true });

    if (!members || members.length === 0) {
      return json({
        server_key,
        team_key,
        master_steam_id: steam_id,
        master_name: display_name ?? "",
        master_is_premium: !!isPremium,
        premium_sponsor_steam_id: isPremium ? steam_id : null,
        controls_chat_alerts: !!wants_chat_alerts,
        controls_chat_commands: !!wants_chat_commands,
        elected_at: now.toISOString(),
        expires_at: expiresAt.toISOString(),
      });
    }

    // Eleger master: primeiro membro premium por ordem, ou primeiro membro
    let master = members.find(m => {
      // Verificar se é premium
      return m.steam_id === steam_id && isPremium;
    }) ?? members[0];

    // Verificar premium de cada membro
    const { data: profiles } = await supabase
      .from("user_profiles")
      .select("steam_id, subscription_tier, is_manual_supporter, premium_until")
      .in("steam_id", members.map(m => m.steam_id));

    const premiumMap = new Map(profiles?.map(p => [p.steam_id, p]) ?? []);

    const premiumMember = members.find(m => {
      const p = premiumMap.get(m.steam_id);
      return p && (
        p.is_manual_supporter ||
        (p.subscription_tier !== "free" && p.subscription_tier !== "guest") ||
        (p.premium_until && new Date(p.premium_until) > now)
      );
    });

    if (premiumMember) master = premiumMember;

    const masterProfile = premiumMap.get(master.steam_id);
    const masterIsPremium = !!(masterProfile && (
      masterProfile.is_manual_supporter ||
      (masterProfile.subscription_tier !== "free" && masterProfile.subscription_tier !== "guest") ||
      (masterProfile.premium_until && new Date(masterProfile.premium_until) > now)
    ));

    return json({
      server_key,
      server_name: server_name ?? "",
      team_key,
      master_steam_id: master.steam_id,
      master_name: master.display_name,
      master_is_premium: masterIsPremium,
      premium_sponsor_steam_id: masterIsPremium ? master.steam_id : null,
      controls_chat_alerts: master.wants_chat_alerts,
      controls_chat_commands: master.wants_chat_commands,
      elected_at: master.updated_at,
      expires_at: master.expires_at,
      updated_at: now.toISOString(),
    });
  }

  // ── GET /master?server_key=xxx&team_key=xxx ── Estado actual do master
  if (path === "master" && req.method === "GET") {
    const serverKey = url.searchParams.get("server_key");
    const teamKey = url.searchParams.get("team_key");

    if (!serverKey || !teamKey) {
      return json({ error: "server_key e team_key obrigatórios" }, 400);
    }

    const now = new Date();
    const { data: members } = await supabase
      .from("team_feature_presence")
      .select("*")
      .eq("server_key", serverKey)
      .eq("team_key", teamKey)
      .gt("expires_at", now.toISOString())
      .order("team_order_index", { ascending: true });

    if (!members || members.length === 0) return json(null);

    // Verificar premium
    const { data: profiles } = await supabase
      .from("user_profiles")
      .select("steam_id, subscription_tier, is_manual_supporter, premium_until")
      .in("steam_id", members.map(m => m.steam_id));

    const premiumMap = new Map(profiles?.map(p => [p.steam_id, p]) ?? []);
    let master = members[0];
    const premiumMember = members.find(m => {
      const p = premiumMap.get(m.steam_id);
      return p && (
        p.is_manual_supporter ||
        (p.subscription_tier !== "free" && p.subscription_tier !== "guest") ||
        (p.premium_until && new Date(p.premium_until) > now)
      );
    });
    if (premiumMember) master = premiumMember;

    const masterProfile = premiumMap.get(master.steam_id);
    const masterIsPremium = !!(masterProfile && (
      masterProfile.is_manual_supporter ||
      (masterProfile.subscription_tier !== "free" && masterProfile.subscription_tier !== "guest") ||
      (masterProfile.premium_until && new Date(masterProfile.premium_until) > now)
    ));

    return json({
      server_key: serverKey,
      team_key: teamKey,
      master_steam_id: master.steam_id,
      master_name: master.display_name,
      master_is_premium: masterIsPremium,
      premium_sponsor_steam_id: masterIsPremium ? master.steam_id : null,
      controls_chat_alerts: master.wants_chat_alerts,
      controls_chat_commands: master.wants_chat_commands,
      elected_at: master.updated_at,
      expires_at: master.expires_at,
    });
  }

  // ── GET /has-master?server_key=xxx&steam_id=xxx ──
  if (path === "has-master" && req.method === "GET") {
    const serverKey = url.searchParams.get("server_key");
    const steamId = url.searchParams.get("steam_id");

    if (!serverKey || !steamId) {
      return json({ error: "server_key e steam_id obrigatórios" }, 400);
    }

    const now = new Date();
    const { data: members } = await supabase
      .from("team_feature_presence")
      .select("steam_id, team_key, team_order_index, expires_at")
      .eq("server_key", serverKey)
      .gt("expires_at", now.toISOString());

    if (!members || members.length === 0) return json({ has_master: false });

    // Encontrar equipa do steam_id
    const myEntry = members.find(m => m.steam_id === steamId);
    if (!myEntry) return json({ has_master: false });

    const teamMembers = members.filter(m => m.team_key === myEntry.team_key);
    if (teamMembers.length === 0) return json({ has_master: false });

    // Verificar se o membro com menor índice é premium
    const { data: profiles } = await supabase
      .from("user_profiles")
      .select("steam_id, subscription_tier, is_manual_supporter, premium_until")
      .in("steam_id", teamMembers.map(m => m.steam_id));

    const premiumMap = new Map(profiles?.map(p => [p.steam_id, p]) ?? []);
    const hasPremiumMaster = teamMembers.some(m => {
      const p = premiumMap.get(m.steam_id);
      return p && (
        p.is_manual_supporter ||
        (p.subscription_tier !== "free" && p.subscription_tier !== "guest") ||
        (p.premium_until && new Date(p.premium_until) > now)
      );
    });

    return json({ has_master: hasPremiumMaster });
  }

  return json({ error: "Rota não encontrada" }, 404);
});
