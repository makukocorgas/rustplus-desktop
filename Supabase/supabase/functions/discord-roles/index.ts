// Edge Function: discord-roles
// Pasta: supabase/functions/discord-roles/index.ts
// Sincroniza roles Discord com o tier de subscrição no Supabase
// No teu fork: define DISCORD_PREMIUM_ROLE_ID e DISCORD_GUILD_ID nas env vars

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const DISCORD_BOT_TOKEN = Deno.env.get("DISCORD_BOT_TOKEN") ?? "";
const DISCORD_GUILD_ID = Deno.env.get("DISCORD_GUILD_ID") ?? "";
const DISCORD_PREMIUM_ROLE_ID = Deno.env.get("DISCORD_PREMIUM_ROLE_ID") ?? "";

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

async function getDiscordUserGuilds(providerToken: string): Promise<string[]> {
  const res = await fetch("https://discord.com/api/v10/users/@me/guilds", {
    headers: { Authorization: `Bearer ${providerToken}` }
  });
  if (!res.ok) return [];
  const guilds = await res.json();
  return guilds.map((g: { id: string }) => g.id);
}

async function getDiscordUserRoles(discordUserId: string): Promise<string[]> {
  if (!DISCORD_BOT_TOKEN || !DISCORD_GUILD_ID) return [];
  try {
    const res = await fetch(
      `https://discord.com/api/v10/guilds/${DISCORD_GUILD_ID}/members/${discordUserId}`,
      { headers: { Authorization: `Bot ${DISCORD_BOT_TOKEN}` } }
    );
    if (!res.ok) return [];
    const member = await res.json();
    return member.roles ?? [];
  } catch {
    return [];
  }
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);

  // Verificar autenticação
  const authHeader = req.headers.get("authorization");
  if (!authHeader) return json({ error: "Não autenticado" }, 401);

  const token = authHeader.replace("Bearer ", "");
  const { data: { user }, error: authError } = await supabase.auth.getUser(token);
  if (authError || !user) return json({ error: "Token inválido" }, 401);

  // Obter discord_id do utilizador
  const discordIdentity = user.identities?.find(i => i.provider === "discord");
  const discordId = discordIdentity?.id ?? user.user_metadata?.["provider_id"];

  if (!discordId) return json({ error: "Conta Discord não ligada" }, 400);

  // Se não houver Discord bot token ou guild configurada, assumir free tier
  if (!DISCORD_BOT_TOKEN || !DISCORD_GUILD_ID || !DISCORD_PREMIUM_ROLE_ID) {
    console.log("[discord-roles] Sem configuração Discord. A manter tier actual.");
    return json({ tier: "free", is_premium: false, message: "Discord não configurado" });
  }

  // Verificar roles Discord
  const roles = await getDiscordUserRoles(discordId);
  const isPremium = roles.includes(DISCORD_PREMIUM_ROLE_ID);
  const newTier = isPremium ? "supporter" : "free";
  const premiumUntil = isPremium ? new Date(Date.now() + 35 * 24 * 60 * 60 * 1000).toISOString() : null;

  // Actualizar perfil
  const { error: updateError } = await supabase
    .from("user_profiles")
    .update({
      subscription_tier: newTier,
      ...(premiumUntil && { premium_until: premiumUntil }),
      last_active_at: new Date().toISOString(),
    })
    .eq("user_id", user.id);

  if (updateError) {
    console.error("[discord-roles] Erro ao actualizar tier:", updateError.message);
    return json({ error: updateError.message }, 500);
  }

  console.log(`[discord-roles] Discord ${discordId} → tier=${newTier} is_premium=${isPremium}`);
  return json({ tier: newTier, is_premium: isPremium });
});
