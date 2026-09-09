// ============================================================
// Edge Function: discord-bot-interactions (v4 — channel cleanup)
// ============================================================

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const DISCORD_BOT_TOKEN    = Deno.env.get("DISCORD_BOT_TOKEN")!;
const SUPABASE_URL         = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const DISCORD_API          = "https://discord.com/api/v10";

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-key, content-type, apikey, x-client-version",
};

const CHANNEL_COLORS: Record<string, number> = {
  events:      0xFF8C00,
  raid:        0xFF0000,
  chat:        0x5865F2,
  teamchat:    0x57F287,
  trackers:    0x3498DB,
  information: 0x2ECC71,
  shop:        0x27AE60,
};

// Regras por ordem de especificidade — mais específico primeiro
const EVENT_ICON_RULES: Array<{ match: RegExp; icon: string }> = [
  { match: /small oil rig/i,                             icon: "🛢️" },
  { match: /large oil rig/i,                             icon: "🏭" },
  { match: /oil rig/i,                                   icon: "🛢️" },
  { match: /patrol heli/i,                               icon: "🚁" },
  { match: /\bheli\b/i,                                  icon: "🚁" },
  { match: /cargo ship/i,                                icon: "🚢" },
  { match: /bradley/i,                                   icon: "🛡️" },
  { match: /deep sea|deepsea/i,                          icon: "🌊" },
  { match: /travelling vendor|traveling vendor|vendor/i, icon: "🛒" },
  { match: /ch-?47|chinook/i,                            icon: "🚁" },
  { match: /supply\s*drop/i,                             icon: "📦" },
  { match: /locked crate|crate unlock/i,                 icon: "📦" },
  { match: /timer:/i,                                    icon: "⏱️" },
  { match: /upkeep/i,                                    icon: "🏠" },
  { match: /trade alert/i,                               icon: "🛒" },
  { match: /alarm/i,                                     icon: "🚨" },
  { match: /raid/i,                                      icon: "🚨" },
];

function detectEventIcon(message: string): string {
  for (const rule of EVENT_ICON_RULES) {
    if (rule.match.test(message)) return rule.icon;
  }
  return "📡";
}

function startsWithEmoji(text: string): boolean {
  return /^[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}\u{2190}-\u{21FF}\u{1F000}-\u{1FFFF}]/u.test(text.trimStart());
}

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { ...CORS, "Content-Type": "application/json" },
  });
}

async function discordRequest(
  method: string,
  path: string,
  body?: unknown
): Promise<{ ok: boolean; data?: unknown; error?: string; status?: number }> {
  try {
    const res = await fetch(`${DISCORD_API}${path}`, {
      method,
      headers: {
        Authorization: `Bot ${DISCORD_BOT_TOKEN}`,
        "Content-Type": "application/json",
      },
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    let data: unknown;
    try { data = JSON.parse(text); } catch { data = text; }
    if (!res.ok) {
      console.error(`[Discord] ${method} ${path} → ${res.status}: ${text}`);
      return { ok: false, error: text, status: res.status };
    }
    return { ok: true, data, status: res.status };
  } catch (e) {
    return { ok: false, error: String(e) };
  }
}

// Cache simples do user id do bot (para overwrites de permissões TTS)
let _botUserId: string | null = null;
async function getBotUserId(): Promise<string | null> {
  if (_botUserId) return _botUserId;
  const res = await discordRequest("GET", "/users/@me");
  if (res.ok) _botUserId = (res.data as { id: string })?.id ?? null;
  return _botUserId;
}

// VIEW_CHANNEL(1024) + SEND_MESSAGES(2048) + MANAGE_MESSAGES(8192) + SEND_TTS_MESSAGES(4096)
const BOT_CHANNEL_PERMS = "15360";

async function createChannel(
  guildId: string,
  name: string,
  parentId: string,
  topic?: string,
  readOnly = false
): Promise<string | null> {
  const overwrites: Array<Record<string, unknown>> = readOnly ? [
    { id: guildId, type: 0, deny: "2048", allow: "1024" },
  ] : [];

  const botId = await getBotUserId();
  if (botId) {
    // Garante que o bot consegue sempre enviar mensagens + TTS neste canal,
    // independentemente das permissões do cargo do bot a nível do servidor.
    overwrites.push({ id: botId, type: 1, allow: BOT_CHANNEL_PERMS, deny: "0" });
  }

  const res = await discordRequest("POST", `/guilds/${guildId}/channels`, {
    name, type: 0, parent_id: parentId, topic: topic ?? "",
    permission_overwrites: overwrites,
  });
  if (!res.ok) return null;
  return (res.data as { id: string })?.id ?? null;
}

async function sendMessage(
  channelId: string,
  content: string,
  embed?: object,
  tts = false
): Promise<{ ok: boolean; messageId?: string; error?: string }> {
  const body: Record<string, unknown> = { tts };
  if (content) body.content = content;
  if (embed) body.embeds = [embed];
  const res = await discordRequest("POST", `/channels/${channelId}/messages`, body);
  if (!res.ok) return { ok: false, error: res.error };
  return { ok: true, messageId: (res.data as { id: string })?.id };
}

function cleanForTts(message: string): string {
  return message
    .replace(/\*\*/g, "")
    .replace(/[*_~`]/g, "")
    .replace(/<@!?[0-9]+>/g, "")
    .replace(/@here|@everyone/g, "")
    .replace(/[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}\u{2190}-\u{21FF}️]/gu, "")
    .replace(/\s{2,}/g, " ")
    .trim();
}

function buildEmbed(
  type: string,
  description: string,
  fields?: Array<{ name: string; value: string; inline?: boolean }>
): object {
  const color = CHANNEL_COLORS[type] ?? 0x95A5A6;
  const embed: Record<string, unknown> = {
    color,
    description,
    footer: { text: `RustPlusBot • ${new Date().toUTCString()}` },
  };
  if (fields && fields.length > 0) embed.fields = fields;
  return embed;
}

function prefixIcon(message: string, notificationType: string): string {
  if (startsWithEmoji(message)) return message;
  const icon = detectEventIcon(message);
  console.log(`[notify] detected icon=${icon} for: ${message.substring(0, 80)}`);
  return `${icon} ${message}`;
}

async function setupGuild(
  guildId: string,
  ownerSteamId: string,
  supabase: ReturnType<typeof createClient>
): Promise<{ ok: boolean; error?: string }> {
  console.log(`[setup] Setting up guild ${guildId} for ${ownerSteamId}`);

  const { data: existing } = await supabase
    .from("discord_guild_channels")
    .select("guild_id, category_id")
    .eq("guild_id", guildId)
    .maybeSingle();

  if (existing?.category_id) {
    const catCheck = await discordRequest("GET", `/channels/${existing.category_id}`);
    if (catCheck.ok) {
      console.log(`[setup] Guild ${guildId} already configured, skipping.`);
      return { ok: true };
    }
    console.log(`[setup] Category missing, recreating channels for ${guildId}`);
  }

  const catRes = await discordRequest("POST", `/guilds/${guildId}/channels`, {
    name: "🎮 RUSTPLUSBOT", type: 4,
  });
  if (!catRes.ok) return { ok: false, error: `Failed to create category: ${catRes.error}` };
  const categoryId = (catRes.data as { id: string }).id;

  const channelDefs = [
    { key: "information", name: "ℹ️-information", topic: "Server information and status",                       readOnly: true  },
    { key: "settings",    name: "⚙️-settings",    topic: "Bot settings — use /setup to configure",              readOnly: false },
    { key: "commands",    name: "💻-commands",     topic: "Run in-game commands from Discord — /commands",      readOnly: false },
    { key: "events",      name: "📡-events",       topic: "In-game event notifications",                        readOnly: true  },
    { key: "teamchat",    name: "💬-teamchat",     topic: "Bidirectional Rust team chat ↔ Discord",             readOnly: false },
    { key: "trackers",    name: "🎯-trackers",     topic: "BattleMetrics player trackers — connect/disconnect", readOnly: true  },
    { key: "raid",        name: "🚨-raid",         topic: "Raid alerts — @here when your base is raided!",      readOnly: true  },
    { key: "shop",        name: "🛒-shop",         topic: "Vending machine trade alerts",                       readOnly: true  },
  ];

  const channelIds: Record<string, string> = {};
  for (const def of channelDefs) {
    const id = await createChannel(guildId, def.name, categoryId, def.topic, def.readOnly);
    if (!id) { console.error(`[setup] Failed to create channel ${def.name}`); continue; }
    channelIds[def.key] = id;
    console.log(`[setup] Created #${def.name} → ${id}`);
    await new Promise(r => setTimeout(r, 300));
  }

  await supabase.from("discord_guild_channels").upsert({
    guild_id:       guildId,
    category_id:    categoryId,
    information_id: channelIds.information ?? null,
    settings_id:    channelIds.settings    ?? null,
    commands_id:    channelIds.commands    ?? null,
    events_id:      channelIds.events      ?? null,
    teamchat_id:    channelIds.teamchat    ?? null,
    trackers_id:    channelIds.trackers    ?? null,
    raid_id:        channelIds.raid        ?? null,
    shop_id:        channelIds.shop        ?? null,
    updated_at:     new Date().toISOString(),
  }, { onConflict: "guild_id" });

  const notificationMappings = [
    { type: "events",   channelId: channelIds.events,      tts: false, audio: false },
    { type: "raid",     channelId: channelIds.raid,        tts: true,  audio: true  },
    { type: "chat",     channelId: channelIds.teamchat,    tts: false, audio: false },
    { type: "teamchat", channelId: channelIds.teamchat,    tts: false, audio: false },
    { type: "shop",     channelId: channelIds.shop,        tts: false, audio: false },
    { type: "trackers", channelId: channelIds.trackers,    tts: false, audio: false },
    { type: "information", channelId: channelIds.information, tts: false, audio: false },
  ];

  for (const mapping of notificationMappings) {
    if (!mapping.channelId) continue;
    await supabase.from("discord_channels_config").upsert({
      guild_id:            guildId,
      notification_type:   mapping.type,
      channel_id:          mapping.channelId,
      tts_enabled:         mapping.tts,
      audio_alert_enabled: mapping.audio,
    }, { onConflict: "guild_id,notification_type" });
  }

  await supabase.from("discord_bot_settings").upsert({
    guild_id:                 guildId,
    owner_steam_id:           ownerSteamId,
    commands_enabled:         true,
    allowed_command_role_ids: "",
    updated_at:               new Date().toISOString(),
  }, { onConflict: "guild_id" });

  if (channelIds.information) {
    const embed = buildEmbed("information", [
      "**Todos os canais foram criados com sucesso.**", "",
      "📡 **#events** — Cargo, Heli, Bradley, Oil Rig, Deepsea, Vendor, Timers",
      "🛒 **#shop** — Alertas de trade em vending machines",
      "💬 **#teamchat** — Chat bidirecional com a equipa in-game",
      "🎯 **#trackers** — BattleMetrics player tracking (connect/disconnect)",
      "🚨 **#raid** — Alertas de raid (TTS activado)",
      "💻 **#commands** — `/setup steamid:<o_teu_steamid64>` para activar", "",
      "Abre a app **RustPlus Desktop** e conecta ao teu servidor para começar.",
    ].join("\n"));
    await sendMessage(channelIds.information, "", embed);
  }

  if (channelIds.commands) {
    const embed = buildEmbed("events", [
      "**Comandos disponíveis (slash commands):**", "",
      "`/setup steamid:<STEAMID64>` — Associa o teu Steam ID a este servidor",
      "`/time` — Hora do servidor", "`/pop` — Jogadores online",
      "`/heli` — Estado do Patrol Helicopter", "`/cargo` — Estado do Cargo Ship",
      "`/oilrig` — Estado dos Oil Rigs", "`/deepsea` — Estado do Deepsea",
      "`/vendor` — Traveling Vendor", "`/upkeep` — Upkeep das bases",
      "`/devicelist` — Lista de Smart Switches",
      "`/switch device:<nome>` — Liga/desliga um switch",
      "`/map` — Screenshot do mapa", "`/commands` — Esta lista",
    ].join("\n"));
    await sendMessage(channelIds.commands, "", embed);
  }

  if (channelIds.teamchat) {
    const embed = buildEmbed("teamchat", [
      "Tudo o que escreveres aqui aparece no chat da equipa in-game.",
      "Tudo o que a tua equipa escrever in-game aparece aqui.", "",
      "🟢 **Bot conectado e a ouvir.**",
    ].join("\n"));
    await sendMessage(channelIds.teamchat, "", embed);
  }

  console.log(`[setup] Guild ${guildId} setup complete.`);
  return { ok: true };
}

async function getGuildChannels(
  guildId: string,
  supabase: ReturnType<typeof createClient>
): Promise<Record<string, string | null> | null> {
  const { data, error } = await supabase
    .from("discord_guild_channels")
    .select("*")
    .eq("guild_id", guildId)
    .maybeSingle();
  if (error || !data) return null;
  return data;
}

async function sendNotification(
  guildId: string,
  notificationType: string,
  message: string,
  tts: boolean,
  supabase: ReturnType<typeof createClient>,
  serverName?: string
): Promise<{ ok: boolean; error?: string }> {
  const channels = await getGuildChannels(guildId, supabase);
  if (!channels) return { ok: false, error: `Guild ${guildId} not configured.` };

  const typeToColumn: Record<string, string> = {
    events:      "events_id",
    raid:        "raid_id",
    chat:        "teamchat_id",
    teamchat:    "teamchat_id",
    shop:        "shop_id",
    trackers:    "trackers_id",
    information: "information_id",
  };

  const column = typeToColumn[notificationType] ?? "events_id";
  const channelId = (channels[column] ?? channels["events_id"]) as string | null;
  if (!channelId) return { ok: false, error: `No channel for type: ${notificationType}` };

  const raidPrefix = notificationType === "raid" ? "@here " : "";
  const iconMessage = prefixIcon(message, notificationType);
  const spoken = raidPrefix + iconMessage;
  // O nome do servidor só entra no embed — nunca no texto falado/TTS.
  const embedText = serverName ? `${spoken}\n\`${serverName}\`` : spoken;
  const embed = buildEmbed(notificationType, embedText);
  const ttsContent = tts ? cleanForTts(spoken) : "";

  return await sendMessage(channelId, ttsContent, embed, tts);
}

// Reaplica ao bot, nos canais já existentes de uma guild, a permissão
// SEND_TTS_MESSAGES (entre outras) — corrige guilds configuradas antes
// desta permissão ter passado a ser incluída na criação dos canais.
async function fixChannelPermissions(
  guildId: string,
  supabase: ReturnType<typeof createClient>
): Promise<{ ok: boolean; fixed?: string[]; error?: string }> {
  const channels = await getGuildChannels(guildId, supabase);
  if (!channels) return { ok: false, error: `Guild ${guildId} not configured.` };

  const botId = await getBotUserId();
  if (!botId) return { ok: false, error: "Failed to resolve bot user id." };

  const channelKeys = ["information_id", "settings_id", "commands_id", "events_id", "teamchat_id", "trackers_id", "raid_id", "shop_id"];
  const fixed: string[] = [];

  for (const key of channelKeys) {
    const channelId = channels[key] as string | null;
    if (!channelId) continue;

    const res = await discordRequest("PUT", `/channels/${channelId}/permissions/${botId}`, {
      type: 1,
      allow: BOT_CHANNEL_PERMS,
      deny: "0",
    });
    if (res.ok) fixed.push(channelId);
    else console.error(`[fix-permissions] Failed for channel ${channelId}: ${res.error}`);
  }

  return { ok: true, fixed };
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });
  if (req.method !== "POST") return json({ error: "Método não suportado" }, 405);

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);
  console.log(`[request] method=${req.method} pathname=${url.pathname}`);
  const path = url.pathname
    .replace(/^.*\/discord-bot-interactions\/?/, "")
    .replace(/^\/+/, "");

  const body = await req.json().catch(() => ({}));

  if (path === "setup") {
    const { guild_id, owner_steam_id } = body;
    if (!guild_id) return json({ error: "guild_id obrigatório" }, 400);
    const result = await setupGuild(guild_id, owner_steam_id ?? "unknown", supabase);
    return json(result, result.ok ? 200 : 500);
  }

  if (path === "fix-permissions") {
    const { guild_id } = body;
    if (!guild_id) return json({ error: "guild_id obrigatório" }, 400);
    const result = await fixChannelPermissions(guild_id, supabase);
    return json(result, result.ok ? 200 : 500);
  }

  if (path === "notify" || path === "") {
    const { guild_id, channel_id, content, notification_type = "events", tts = false, server_name } = body;
    if (!content) return json({ error: "content obrigatório" }, 400);

    if (channel_id) {
      const iconMessage = prefixIcon(content, notification_type);
      const embedText = server_name ? `${iconMessage}\n\`${server_name}\`` : iconMessage;
      const embed = buildEmbed(notification_type, embedText);
      const ttsContent = tts ? cleanForTts(iconMessage) : "";
      const result = await sendMessage(channel_id, ttsContent, embed, tts);
      return json(result, result.ok ? 200 : 502);
    }

    if (!guild_id) {
      const { data: settings } = await supabase
        .from("discord_bot_settings").select("guild_id").limit(1).maybeSingle();
      if (!settings?.guild_id) return json({ error: "guild_id ou channel_id obrigatório" }, 400);
      const result = await sendNotification(settings.guild_id, notification_type, content, tts, supabase, server_name);
      return json(result, result.ok ? 200 : 502);
    }

    const result = await sendNotification(guild_id, notification_type, content, tts, supabase, server_name);
    return json(result, result.ok ? 200 : 502);
  }

  if (path === "teamchat-from-discord") {
    const { guild_id, author, message, author_avatar } = body;
    if (!guild_id || !message) return json({ error: "guild_id e message obrigatórios" }, 400);

    const { data: settings } = await supabase
      .from("discord_bot_settings").select("owner_steam_id").eq("guild_id", guild_id).maybeSingle();
    if (!settings) return json({ error: "Guild não configurado" }, 404);

    const { error } = await supabase.from("bot_commands_queue").insert({
      guild_id,
      command_type: "send_teamchat",
      payload: { message, author: author ?? "Discord", author_avatar: author_avatar ?? null },
      status: "pending",
      created_at: new Date().toISOString(),
      updated_at: new Date().toISOString(),
    });

    if (error) { console.error("[teamchat] Insert error:", error.message); return json({ error: error.message }, 500); }
    console.log(`[teamchat] Queued message from ${author}: ${message}`);
    return json({ ok: true });
  }

  if (path === "command-response") {
    const { guild_id, response_message, tts = false } = body;
    if (!guild_id || !response_message) return json({ error: "guild_id e response_message obrigatórios" }, 400);

    const channels = await getGuildChannels(guild_id, supabase);
    if (!channels) return json({ error: "Guild não configurado" }, 404);

    const channelId = (channels["commands_id"] ?? channels["events_id"]) as string | null;
    if (!channelId) return json({ error: "Canal commands não configurado" }, 404);

    const embed = buildEmbed("events", response_message);
    const ttsContent = tts ? cleanForTts(response_message) : "";
    return json(await sendMessage(channelId, ttsContent, embed, tts));
  }

  if (path === "map-upload") {
    const { guild_id, channel_id, image_base64, interaction_token, application_id, server_name } = body;
    if (!image_base64) return json({ ok: false, error: "image_base64 obrigatório" }, 400);

    let targetChannelId = channel_id;
    if (!targetChannelId && guild_id) {
      const channels = await getGuildChannels(guild_id, supabase);
      targetChannelId = channels?.["information_id"] ?? channels?.["events_id"];
    }

    const caption = server_name ? `🗺️ **Mapa de ${server_name}**` : "🗺️ **Mapa do servidor**";

    if (interaction_token && application_id) {
      try {
        const binary = Uint8Array.from(atob(image_base64), c => c.charCodeAt(0));
        const formData = new FormData();
        formData.append("payload_json", JSON.stringify({ content: caption }));
        formData.append("files[0]", new Blob([binary], { type: "image/png" }), "map.png");
        await fetch(`${DISCORD_API}/webhooks/${application_id}/${interaction_token}`, { method: "POST", body: formData });
        return json({ ok: true });
      } catch (e) { return json({ ok: false, error: String(e) }, 500); }
    }

    if (!targetChannelId) return json({ ok: false, error: "channel_id ou guild_id obrigatório" }, 400);

    try {
      const binary = Uint8Array.from(atob(image_base64), c => c.charCodeAt(0));
      const formData = new FormData();
      formData.append("payload_json", JSON.stringify({ content: caption }));
      formData.append("files[0]", new Blob([binary], { type: "image/png" }), "map.png");
      const res = await fetch(`${DISCORD_API}/channels/${targetChannelId}/messages`, {
        method: "POST",
        headers: { Authorization: `Bot ${DISCORD_BOT_TOKEN}` },
        body: formData,
      });
      return json({ ok: res.ok }, res.ok ? 200 : 502);
    } catch (e) { return json({ ok: false, error: String(e) }, 500); }
  }

  if (path === "get-channels") {
    const { guild_id } = body;
    if (!guild_id) return json({ error: "guild_id obrigatório" }, 400);
    const channels = await getGuildChannels(guild_id, supabase);
    if (!channels) return json({ error: "Guild não configurado." }, 404);
    return json(channels);
  }

  return json({ error: `Rota '${path}' não encontrada` }, 404);
});
