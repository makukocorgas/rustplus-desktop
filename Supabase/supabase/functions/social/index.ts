// Edge Function: social
// The social layer's backend: LFG listings, settings/consent, friends, blocks,
// global chat, DMs, and reports. Routed by path segment after the function slug,
// same convention as discord-bot and player-wipe-tracker. Identity throughout is
// steam_id — there is no separate internal user id, so every "id"/"user_id" the
// client sees IS the steam_id.
//
// GET/PUT  social/settings
// POST     social/consent                        { scope: 'lfg' | 'dm' | 'chat' }
// GET/PUT/DELETE social/lfg/me                    PUT: { mode, blurb, region, language }
// POST     social/lfg/me/renew
// GET      social/lfg/listings                    query: mode, language, region, online
// GET      social/conversations
// GET      social/conversations/{id}
// POST     social/conversations                   { user_id, body, origin }
// POST     social/conversations/{id}/messages      { body }
// POST     social/conversations/{id}/accept
// POST     social/conversations/{id}/decline
// POST     social/conversations/{id}/read
// GET      social/chat                            query: room ('public'|'supporter'), since, limit
// POST     social/chat                             { body, room?, reply_to? }
// GET      social/friends
// POST     social/friends                          { steam_id, body? }
// POST     social/friends/{id}/accept
// POST     social/friends/{id}/decline
// DELETE   social/friends/{id}
// GET      social/blocks
// POST     social/blocks                           { user_id }
// DELETE   social/blocks/{userId}
// POST     social/reports                          { user_id, reason, message_id, note }
//
// Ownership: every write is scoped to the caller's own steam_id, taken from the
// JWT — never trusted from the body.

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

type Profile = { steam_id: string; discord_name: string | null; is_manual_supporter: boolean; premium_until: string | null; is_online: boolean };

async function profilesFor(supabase: ReturnType<typeof createClient>, steamIds: string[]): Promise<Map<string, Profile>> {
  if (steamIds.length === 0) return new Map();
  const { data } = await supabase.from("user_profiles")
    .select("steam_id, discord_name, is_manual_supporter, premium_until, is_online")
    .in("steam_id", steamIds);
  return new Map((data ?? []).map((p: Profile) => [p.steam_id, p]));
}

function userJson(steamId: string, p: Profile | undefined, roles?: string[]) {
  return {
    id: steamId,
    steam_id: steamId,
    display_name: p?.discord_name ?? null,
    name: p?.discord_name ?? null,
    avatar_url: null,
    roles: roles ?? [],
    presence: { is_online: !!p?.is_online },
  };
}

/// Staff badges: who among these steam ids moderates the room, and with which role.
async function rolesFor(supabase: ReturnType<typeof createClient>, steamIds: string[]): Promise<Map<string, string[]>> {
  if (steamIds.length === 0) return new Map();
  const { data } = await supabase.from("social_moderators").select("steam_id, role").in("steam_id", steamIds);
  const map = new Map<string, string[]>();
  for (const row of data ?? []) map.set(row.steam_id, [row.role]);
  return map;
}

/// Same rule as everywhere else supporter status is decided: a manual grant, or a plan that
/// has not lapsed yet.
async function isSupporter(supabase: ReturnType<typeof createClient>, steamId: string): Promise<boolean> {
  const { data } = await supabase.from("user_profiles")
    .select("is_manual_supporter, premium_until").eq("steam_id", steamId).maybeSingle();
  if (!data) return false;
  return !!data.is_manual_supporter || (typeof data.premium_until === "string" && new Date(data.premium_until) > new Date());
}

const REGULAR_CHAT_MAX_LENGTH = 128;
const STAFF_CHAT_MAX_LENGTH = 500;

async function chatMaxLength(supabase: ReturnType<typeof createClient>, steamId: string): Promise<number> {
  const { data } = await supabase.from("social_moderators").select("steam_id").eq("steam_id", steamId).maybeSingle();
  return data ? STAFF_CHAT_MAX_LENGTH : REGULAR_CHAT_MAX_LENGTH;
}

// A line earns a warning rather than an outright refusal for the first few slips; the
// PROFANITY_WARNING_LIMIT-th one converts into an actual timeout via social_sanctions, the same
// table every other sanction lives in — so the client's existing "you are sanctioned" bar covers
// it without a second code path.
const PROFANITY_WARNING_LIMIT = 3;
const PROFANITY_TIMEOUT_MINUTES = 15;

// Deliberately small and boundary-matched: a substring scan would flag "class" for the four
// letters in the middle of it. English and Portuguese covered since both are live in the room.
const PROFANITY_WORDS = [
  "fuck", "shit", "bitch", "asshole", "bastard", "cunt", "dick", "piss", "slut", "whore",
  "faggot", "retard", "nigger", "nigga",
  "caralho", "porra", "merda", "puta", "foda-se", "cabrão", "cabrao", "filho da puta",
  "corno", "otário", "otario", "desgraça", "desgraca",
];
const PROFANITY_PATTERN = new RegExp(
  `\\b(${PROFANITY_WORDS.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|")})\\b`,
  "i",
);

function containsProfanity(text: string): boolean {
  return PROFANITY_PATTERN.test(text);
}

/// Bumps the caller's warning count and, once it reaches the limit, issues a real timeout and
/// resets the counter — a fresh slip after serving a timeout starts the count over, rather than
/// escalating a second sanction on top of one already being served.
async function applyProfanityWarning(
  supabase: ReturnType<typeof createClient>,
  steamId: string,
): Promise<{ number: number; max: number; sanctioned: boolean }> {
  const { data: profile } = await supabase.from("user_profiles")
    .select("profanity_warning_count").eq("steam_id", steamId).maybeSingle();
  const next = (profile?.profanity_warning_count ?? 0) + 1;

  if (next >= PROFANITY_WARNING_LIMIT) {
    await supabase.from("social_sanctions").insert({
      steam_id: steamId,
      kind: "timeout",
      reason: "profanity",
      applied_by_steam_id: null,
      expires_at: new Date(Date.now() + PROFANITY_TIMEOUT_MINUTES * 60 * 1000).toISOString(),
    });
    await supabase.from("user_profiles").update({ profanity_warning_count: 0 }).eq("steam_id", steamId);
    return { number: next, max: PROFANITY_WARNING_LIMIT, sanctioned: true };
  }

  await supabase.from("user_profiles").update({ profanity_warning_count: next }).eq("steam_id", steamId);
  return { number: next, max: PROFANITY_WARNING_LIMIT, sanctioned: false };
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);
  const segments = url.pathname.split("/").filter(Boolean);
  const slugIdx = segments.indexOf("social");
  const route = slugIdx >= 0 ? segments.slice(slugIdx + 1) : segments;

  const steamId = getSteamIdFromJwt(req);
  if (!steamId) return json({ error: "Não autenticado" }, 401);

  // GET/PUT social/settings
  if (route.length === 1 && route[0] === "settings") {
    if (req.method === "GET") {
      const { data } = await supabase.from("social_settings").select("*").eq("steam_id", steamId).maybeSingle();
      return json({
        data: {
          accept_mode: data?.accept_mode ?? "auto",
          lfg_consent: data?.lfg_consent ?? false,
          dm_consent: data?.dm_consent ?? false,
          chat_consent: data?.chat_consent ?? false,
          name_color: data?.name_color ?? null,
        },
      });
    }
    if (req.method === "PUT") {
      const body = await req.json().catch(() => ({}));
      const patch: Record<string, unknown> = { steam_id: steamId, updated_at: new Date().toISOString() };
      if (typeof body.accept_mode === "string") patch.accept_mode = body.accept_mode;
      if (typeof body.name_color === "string") patch.name_color = body.name_color;
      const { error } = await supabase.from("social_settings").upsert(patch, { onConflict: "steam_id" });
      if (error) return json({ error: error.message }, 500);
      return json({ ok: true });
    }
  }

  // POST social/consent { scope: 'lfg' | 'dm' | 'chat' }
  if (route.length === 1 && route[0] === "consent" && req.method === "POST") {
    const body = await req.json().catch(() => ({}));
    const scope = body.scope === "dm" ? "dm_consent" : body.scope === "lfg" ? "lfg_consent" : body.scope === "chat" ? "chat_consent" : null;
    if (!scope) return json({ error: "invalid scope" }, 400);
    const patch: Record<string, unknown> = { steam_id: steamId, updated_at: new Date().toISOString() };
    patch[scope] = true;
    const { error } = await supabase.from("social_settings").upsert(patch, { onConflict: "steam_id" });
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // GET/PUT/DELETE social/lfg/me
  if (route.length === 2 && route[0] === "lfg" && route[1] === "me") {
    if (req.method === "GET") {
      const { data } = await supabase.from("lfg_listings").select("*").eq("steam_id", steamId).gt("expires_at", new Date().toISOString()).maybeSingle();
      if (!data) return json({ error: "not_found" }, 404);
      return json({ data });
    }
    if (req.method === "PUT") {
      const { data: settings } = await supabase.from("social_settings").select("lfg_consent").eq("steam_id", steamId).maybeSingle();
      if (!settings?.lfg_consent) return json({ error: "consent_required" }, 409);

      const body = await req.json().catch(() => ({}));
      const mode = body.mode === "lfm" ? "lfm" : "lfg";

      const { data: profile } = await supabase.from("user_profiles")
        .select("current_server_key, current_server_name, team_member_count")
        .eq("steam_id", steamId).maybeSingle();

      const row = {
        steam_id: steamId,
        mode,
        blurb: typeof body.blurb === "string" ? body.blurb.slice(0, 280) : null,
        region: typeof body.region === "string" ? body.region : null,
        language: typeof body.language === "string" ? body.language : null,
        server_key: profile?.current_server_key ?? null,
        server_name: profile?.current_server_name ?? null,
        group_size: profile?.team_member_count ?? null,
        updated_at: new Date().toISOString(),
        expires_at: new Date(Date.now() + 2 * 24 * 60 * 60 * 1000).toISOString(),
      };
      const { error } = await supabase.from("lfg_listings").upsert(row, { onConflict: "steam_id" });
      if (error) return json({ error: error.message }, 500);
      return json({ ok: true });
    }
    if (req.method === "DELETE") {
      const { error } = await supabase.from("lfg_listings").delete().eq("steam_id", steamId);
      if (error) return json({ error: error.message }, 500);
      return json({ ok: true });
    }
  }

  // POST social/lfg/me/renew
  if (route.length === 3 && route[0] === "lfg" && route[1] === "me" && route[2] === "renew" && req.method === "POST") {
    const { error } = await supabase.from("lfg_listings")
      .update({ expires_at: new Date(Date.now() + 2 * 24 * 60 * 60 * 1000).toISOString() })
      .eq("steam_id", steamId);
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // GET social/lfg/listings
  if (route.length === 2 && route[0] === "lfg" && route[1] === "listings" && req.method === "GET") {
    let query = supabase.from("lfg_listings").select("*").gt("expires_at", new Date().toISOString()).order("updated_at", { ascending: false }).limit(200);
    const mode = url.searchParams.get("mode");
    const region = url.searchParams.get("region");
    const language = url.searchParams.get("language");
    if (mode) query = query.eq("mode", mode);
    if (region) query = query.eq("region", region);
    if (language) query = query.eq("language", language);
    const { data, error } = await query;
    if (error) return json({ error: error.message }, 500);

    const { data: blocks } = await supabase.from("social_blocks")
      .select("blocker_steam_id, blocked_steam_id")
      .or(`blocker_steam_id.eq.${steamId},blocked_steam_id.eq.${steamId}`);
    const hidden = new Set<string>();
    for (const b of blocks ?? []) hidden.add(b.blocker_steam_id === steamId ? b.blocked_steam_id : b.blocker_steam_id);

    let rows = (data ?? []).filter((l: { steam_id: string }) => !hidden.has(l.steam_id));
    const steamIds = rows.map((l: { steam_id: string }) => l.steam_id);
    const profileMap = await profilesFor(supabase, steamIds);
    const roleMap = await rolesFor(supabase, steamIds);

    if (url.searchParams.get("online") === "1") {
      rows = rows.filter((l: { steam_id: string }) => profileMap.get(l.steam_id)?.is_online);
    }

    const now = new Date();
    const entries = rows.map((l: Record<string, unknown>) => {
      const p = profileMap.get(l.steam_id as string);
      const isSupporter = !!p?.is_manual_supporter || (typeof p?.premium_until === "string" && new Date(p.premium_until) > now);
      const u = userJson(l.steam_id as string, p, roleMap.get(l.steam_id as string));
      (u as Record<string, unknown>).presence = { ...(u.presence as object), language: l.language ?? null };
      return {
        id: l.steam_id, mode: l.mode, blurb: l.blurb, region: l.region, server_name: l.server_name,
        is_supporter: isSupporter, user: u, team: { name: null, members_count: l.group_size ?? null },
      };
    });

    return json({ data: { data: entries } });
  }

  // GET social/conversations — the inbox list
  if (route.length === 1 && route[0] === "conversations" && req.method === "GET") {
    const { data: convos, error } = await supabase.from("social_conversations")
      .select("*")
      .or(`participant_a_steam_id.eq.${steamId},participant_b_steam_id.eq.${steamId}`)
      .order("updated_at", { ascending: false });
    if (error) return json({ error: error.message }, 500);

    const others = (convos ?? []).map((c) => (c.participant_a_steam_id === steamId ? c.participant_b_steam_id : c.participant_a_steam_id));
    const profileMap = await profilesFor(supabase, others);

    const rows = [];
    for (const c of convos ?? []) {
      const other = c.participant_a_steam_id === steamId ? c.participant_b_steam_id : c.participant_a_steam_id;
      const { count } = await supabase.from("social_dm_messages")
        .select("id", { count: "exact", head: true })
        .eq("conversation_id", c.id)
        .neq("sender_steam_id", steamId)
        .is("read_at", null);
      const { data: lastMsg } = await supabase.from("social_dm_messages")
        .select("created_at").eq("conversation_id", c.id).order("created_at", { ascending: false }).limit(1).maybeSingle();

      rows.push({
        id: c.id,
        state: c.state,
        unread_count: count ?? 0,
        last_message_at: lastMsg?.created_at ?? c.updated_at,
        members: [
          { user_id: steamId, user: userJson(steamId, profileMap.get(steamId)) },
          { user_id: other, user: userJson(other, profileMap.get(other)) },
        ],
      });
    }

    return json({ data: { data: rows } });
  }

  // GET social/conversations/{id}
  if (route.length === 2 && route[0] === "conversations" && req.method === "GET") {
    const convoId = route[1];
    const { data: convo } = await supabase.from("social_conversations").select("*").eq("id", convoId).maybeSingle();
    if (!convo || (convo.participant_a_steam_id !== steamId && convo.participant_b_steam_id !== steamId)) {
      return json({ error: "not_found" }, 404);
    }
    const { data: messages } = await supabase.from("social_dm_messages")
      .select("*").eq("conversation_id", convoId).order("created_at", { ascending: true });

    const senderIds = [...new Set((messages ?? []).map((m) => m.sender_steam_id))];
    const profileMap = await profilesFor(supabase, senderIds);

    const rows = (messages ?? []).map((m) => ({
      id: m.id, body: m.body, sender_id: m.sender_steam_id,
      sender: userJson(m.sender_steam_id, profileMap.get(m.sender_steam_id)),
      created_at: m.created_at,
    }));

    return json({ data: { messages: { data: rows } } });
  }

  // POST social/conversations { user_id, body, origin }
  if (route.length === 1 && route[0] === "conversations" && req.method === "POST") {
    const body = await req.json().catch(() => ({}));
    const otherId = typeof body.user_id === "string" ? body.user_id : null;
    const text = typeof body.body === "string" ? body.body.trim() : "";
    if (!otherId || !text) return json({ error: "user_id and body are required" }, 400);
    if (otherId === steamId) return json({ error: "cannot message yourself" }, 400);

    const { data: blocked } = await supabase.from("social_blocks")
      .select("blocker_steam_id")
      .or(`and(blocker_steam_id.eq.${steamId},blocked_steam_id.eq.${otherId}),and(blocker_steam_id.eq.${otherId},blocked_steam_id.eq.${steamId})`)
      .maybeSingle();
    if (blocked) return json({ error: "blocked" }, 403);

    const a = steamId < otherId ? steamId : otherId;
    const b = steamId < otherId ? otherId : steamId;
    const { data: existing } = await supabase.from("social_conversations")
      .select("*").eq("participant_a_steam_id", a).eq("participant_b_steam_id", b).maybeSingle();

    let convoId: string;
    if (existing) {
      convoId = existing.id;
      await supabase.from("social_conversations").update({ updated_at: new Date().toISOString() }).eq("id", convoId);
    } else {
      const { data: recipientSettings } = await supabase.from("social_settings").select("accept_mode").eq("steam_id", otherId).maybeSingle();
      const acceptMode = recipientSettings?.accept_mode ?? "auto";
      if (acceptMode === "off") return json({ error: "not_accepting_messages" }, 403);

      const { data: created, error: createErr } = await supabase.from("social_conversations").insert({
        participant_a_steam_id: a, participant_b_steam_id: b,
        state: acceptMode === "approval" ? "pending" : "accepted",
        initiator_steam_id: steamId,
      }).select("id").single();
      if (createErr || !created) return json({ error: createErr?.message ?? "failed" }, 500);
      convoId = created.id;
    }

    const { error: msgErr } = await supabase.from("social_dm_messages").insert({ conversation_id: convoId, sender_steam_id: steamId, body: text });
    if (msgErr) return json({ error: msgErr.message }, 500);
    return json({ ok: true, data: { id: convoId } });
  }

  // POST social/conversations/{id}/messages { body }
  if (route.length === 3 && route[0] === "conversations" && route[2] === "messages" && req.method === "POST") {
    const convoId = route[1];
    const { data: convo } = await supabase.from("social_conversations").select("*").eq("id", convoId).maybeSingle();
    if (!convo || (convo.participant_a_steam_id !== steamId && convo.participant_b_steam_id !== steamId)) return json({ error: "not_found" }, 404);
    if (convo.state !== "accepted") return json({ error: "thread_not_accepted" }, 409);

    const body = await req.json().catch(() => ({}));
    const text = typeof body.body === "string" ? body.body.trim() : "";
    if (!text) return json({ error: "body is required" }, 400);

    const { error } = await supabase.from("social_dm_messages").insert({ conversation_id: convoId, sender_steam_id: steamId, body: text });
    if (error) return json({ error: error.message }, 500);
    await supabase.from("social_conversations").update({ updated_at: new Date().toISOString() }).eq("id", convoId);
    return json({ ok: true });
  }

  // POST social/conversations/{id}/accept | decline | read
  if (route.length === 3 && route[0] === "conversations" && ["accept", "decline", "read"].includes(route[2]) && req.method === "POST") {
    const convoId = route[1];
    const action = route[2];
    const { data: convo } = await supabase.from("social_conversations").select("*").eq("id", convoId).maybeSingle();
    if (!convo || (convo.participant_a_steam_id !== steamId && convo.participant_b_steam_id !== steamId)) return json({ error: "not_found" }, 404);

    if (action === "accept") {
      await supabase.from("social_conversations").update({ state: "accepted" }).eq("id", convoId);
    } else if (action === "decline") {
      await supabase.from("social_conversations").update({ state: "declined" }).eq("id", convoId);
    } else if (action === "read") {
      await supabase.from("social_dm_messages").update({ read_at: new Date().toISOString() })
        .eq("conversation_id", convoId).neq("sender_steam_id", steamId).is("read_at", null);
    }
    return json({ ok: true });
  }

  // GET social/chat?room=public|supporter&since=<iso>&limit=<n>
  if (route.length === 1 && route[0] === "chat" && req.method === "GET") {
    const room = url.searchParams.get("room") ?? "public";
    const since = url.searchParams.get("since");
    const limitParam = parseInt(url.searchParams.get("limit") ?? "", 10);
    const limit = Number.isFinite(limitParam) && limitParam > 0 ? Math.min(limitParam, 200) : 50;

    // The supporter room's content never reaches a non-supporter — not filtered client-side,
    // not sent at all. The tab still opens for everyone; it just shows the offer instead of
    // the room, using the flag below rather than an empty list that looks merely quiet.
    const callerIsSupporter = await isSupporter(supabase, steamId);
    const canReadRoom = room !== "supporter" || callerIsSupporter;

    // deno-lint-ignore no-explicit-any
    let rows: any[] = [];
    if (canReadRoom) {
      let query = supabase.from("social_chat_messages")
        .select("*").eq("room", room).is("deleted_at", null);
      query = since
        ? query.gt("created_at", since).order("created_at", { ascending: true }).limit(200)
        : query.order("created_at", { ascending: false }).limit(limit);
      const { data, error } = await query;
      if (error) return json({ error: error.message }, 500);
      rows = data ?? [];
    }

    const ordered = since ? rows : rows.reverse();
    const senderIds = [...new Set(ordered.map((m) => m.sender_steam_id))];
    const profileMap = await profilesFor(supabase, senderIds);
    const roleMap = await rolesFor(supabase, senderIds);

    const lines = ordered.map((m) => ({
      id: m.id, body: m.body, sender_id: m.sender_steam_id,
      sender: userJson(m.sender_steam_id, profileMap.get(m.sender_steam_id), roleMap.get(m.sender_steam_id)),
      reply_to: m.reply_to_id,
      created_at: m.created_at,
    }));

    const { data: sanction } = await supabase.from("social_sanctions")
      .select("*").eq("steam_id", steamId).gt("expires_at", new Date().toISOString())
      .order("created_at", { ascending: false }).limit(1).maybeSingle();

    const { data: roomSettings } = await supabase.from("chat_rooms").select("slow_mode_seconds").eq("room", room).maybeSingle();

    return json({
      data: lines,
      meta: {
        sanction: sanction ? { kind: sanction.kind, reason: sanction.reason ?? "", expires_at: sanction.expires_at } : null,
        slow_mode_seconds: roomSettings?.slow_mode_seconds ?? 0,
        supporter_room: callerIsSupporter,
        max_length: await chatMaxLength(supabase, steamId),
      },
    });
  }

  // POST social/chat { body, room?, reply_to? }
  //
  // Refused for one of several reasons, distinguished in the error code so the client can tell
  // the user something useful rather than a bare "could not send": consent_required (rules not
  // read), sanctioned (timed out or banned), account_too_new (anti-spam wait), empty (nothing
  // left after cleaning), link_not_allowed (the public room carries no links), duplicate (the
  // same line, again, within the window).
  if (route.length === 1 && route[0] === "chat" && req.method === "POST") {
    const { data: settings } = await supabase.from("social_settings").select("chat_consent").eq("steam_id", steamId).maybeSingle();
    if (!settings?.chat_consent) return json({ error: "consent_required" }, 409);

    const { data: sanction } = await supabase.from("social_sanctions")
      .select("id").eq("steam_id", steamId).gt("expires_at", new Date().toISOString()).limit(1).maybeSingle();
    if (sanction) return json({ error: "sanctioned" }, 403);

    const { data: moderatorRow } = await supabase.from("social_moderators").select("steam_id").eq("steam_id", steamId).maybeSingle();
    const isModerator = !!moderatorRow;

    const body = await req.json().catch(() => ({}));
    const maxLength = isModerator ? STAFF_CHAT_MAX_LENGTH : REGULAR_CHAT_MAX_LENGTH;
    const text = typeof body.body === "string" ? body.body.trim().slice(0, maxLength) : "";
    if (!text) return json({ error: "empty" }, 400);

    // Staff are exempt: moderating the room sometimes means quoting exactly what got someone
    // sanctioned. Everyone else's slip is refused and counted rather than posted and deleted —
    // the line never reaches the room at all.
    if (!isModerator && containsProfanity(text)) {
      const warning = await applyProfanityWarning(supabase, steamId);
      if (warning.sanctioned) return json({ error: "sanctioned" }, 403);
      return json({ error: `profanity_warning:${warning.number}:${warning.max}` }, 400);
    }

    // A short wait for a brand-new profile row — enough to cost a spammer time without holding
    // up a real account for more than a minute or two.
    const { data: profile } = await supabase.from("user_profiles").select("created_at").eq("steam_id", steamId).maybeSingle();
    if (profile?.created_at && Date.now() - new Date(profile.created_at).getTime() < 30 * 60 * 1000) {
      return json({ error: "account_too_new" }, 403);
    }

    if (/\bhttps?:\/\/|\bwww\.[^\s]+\.[a-z]{2,}|\b[a-z0-9-]+\.(gg|com|net|org|io|me|tv|co)\b/i.test(text)) {
      return json({ error: "link_not_allowed" }, 400);
    }

    const room = typeof body.room === "string" ? body.room : "public";

    // The compose box for this room is hidden client-side unless the caller can already see
    // it opened; this is the check that actually matters.
    if (room === "supporter" && !(await isSupporter(supabase, steamId))) {
      return json({ error: "supporter_required" }, 403);
    }

    const { data: recent } = await supabase.from("social_chat_messages")
      .select("id").eq("room", room).eq("sender_steam_id", steamId).eq("body", text)
      .gt("created_at", new Date(Date.now() - 30 * 1000).toISOString()).limit(1).maybeSingle();
    if (recent) return json({ error: "duplicate" }, 409);

    // Slow mode: staff post through it freely, everyone else waits out the room's cooldown
    // since their own last line here, regardless of what that line said.
    if (!isModerator) {
      const { data: roomSettings } = await supabase.from("chat_rooms").select("slow_mode_seconds").eq("room", room).maybeSingle();
      const slowModeSeconds = roomSettings?.slow_mode_seconds ?? 0;
      if (slowModeSeconds > 0) {
        const { data: lastOwn } = await supabase.from("social_chat_messages")
          .select("id").eq("room", room).eq("sender_steam_id", steamId)
          .gt("created_at", new Date(Date.now() - slowModeSeconds * 1000).toISOString()).limit(1).maybeSingle();
        if (lastOwn) return json({ error: "duplicate" }, 409);
      }
    }

    const { error } = await supabase.from("social_chat_messages").insert({
      room,
      sender_steam_id: steamId,
      body: text,
      reply_to_id: typeof body.reply_to === "string" ? body.reply_to : null,
    });
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // GET social/friends — the list, and the requests waiting on either side of it
  if (route.length === 1 && route[0] === "friends" && req.method === "GET") {
    const { data: rows, error } = await supabase.from("social_friendships")
      .select("*")
      .or(`requester_steam_id.eq.${steamId},addressee_steam_id.eq.${steamId}`)
      .order("created_at", { ascending: false });
    if (error) return json({ error: error.message }, 500);

    const otherIds = (rows ?? []).map((r) => r.requester_steam_id === steamId ? r.addressee_steam_id : r.requester_steam_id);
    const profileMap = await profilesFor(supabase, otherIds);
    const roleMap = await rolesFor(supabase, otherIds);

    // Where somebody plays is only shared once the friendship is mutual - a pending request
    // reveals a name and an online dot, not a server to go find them on.
    const { data: presenceRows } = otherIds.length
      ? await supabase.from("user_profiles").select("steam_id, current_server_name, team_member_count").in("steam_id", otherIds)
      : { data: [] as { steam_id: string; current_server_name: string | null; team_member_count: number | null }[] };
    const presenceMap = new Map((presenceRows ?? []).map((p) => [p.steam_id, p]));

    const friends: unknown[] = [];
    const incoming: unknown[] = [];
    const outgoing: unknown[] = [];

    for (const r of rows ?? []) {
      if (r.status === "declined") continue;

      const otherId = r.requester_steam_id === steamId ? r.addressee_steam_id : r.requester_steam_id;
      const accepted = r.status === "accepted";
      const presence = accepted ? presenceMap.get(otherId) : undefined;

      const entry = {
        id: r.id,
        state: r.status,
        requested_by_me: r.requester_steam_id === steamId,
        user: userJson(otherId, profileMap.get(otherId), roleMap.get(otherId)),
        server: presence?.current_server_name ?? null,
        team_size: presence?.team_member_count ?? 0,
      };

      if (accepted) friends.push(entry);
      else if (r.addressee_steam_id === steamId) incoming.push(entry);
      else outgoing.push(entry);
    }

    return json({ data: { friends, incoming, outgoing } });
  }

  // POST social/friends { steam_id, body? }
  if (route.length === 1 && route[0] === "friends" && req.method === "POST") {
    const body = await req.json().catch(() => ({}));
    const targetId = typeof body.steam_id === "string" ? body.steam_id : null;
    if (!targetId) return json({ error: "steam_id required" }, 400);
    if (targetId === steamId) return json({ error: "cannot friend yourself" }, 400);

    const { data: targetProfile } = await supabase.from("user_profiles").select("steam_id").eq("steam_id", targetId).maybeSingle();
    if (!targetProfile) return json({ error: "not_found" }, 404);

    const { data: blocked } = await supabase.from("social_blocks")
      .select("blocker_steam_id")
      .or(`and(blocker_steam_id.eq.${steamId},blocked_steam_id.eq.${targetId}),and(blocker_steam_id.eq.${targetId},blocked_steam_id.eq.${steamId})`)
      .maybeSingle();
    if (blocked) return json({ error: "blocked" }, 403);

    const { data: existing } = await supabase.from("social_friendships")
      .select("*")
      .or(`and(requester_steam_id.eq.${steamId},addressee_steam_id.eq.${targetId}),and(requester_steam_id.eq.${targetId},addressee_steam_id.eq.${steamId})`)
      .maybeSingle();

    if (existing) {
      if (existing.status === "accepted") return json({ error: "already_friends" }, 409);
      if (existing.status === "pending") return json({ error: "pending" }, 409);
      if (existing.status === "declined") return json({ error: "declined" }, 403);
    }

    const { error } = await supabase.from("social_friendships").insert({
      requester_steam_id: steamId,
      addressee_steam_id: targetId,
      status: "pending",
    });
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // POST social/friends/{id}/accept | decline — only the addressee gets to answer
  if (route.length === 3 && route[0] === "friends" && ["accept", "decline"].includes(route[2]) && req.method === "POST") {
    const friendshipId = route[1];
    const action = route[2];

    const { data: row } = await supabase.from("social_friendships").select("*").eq("id", friendshipId).maybeSingle();
    if (!row || row.addressee_steam_id !== steamId) return json({ error: "not_found" }, 404);
    if (row.status !== "pending") return json({ error: "not_pending" }, 409);

    const { error } = await supabase.from("social_friendships")
      .update({ status: action === "accept" ? "accepted" : "declined", updated_at: new Date().toISOString() })
      .eq("id", friendshipId);
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // DELETE social/friends/{id} — removes a friend, or withdraws a request either side sent
  if (route.length === 2 && route[0] === "friends" && req.method === "DELETE") {
    const friendshipId = route[1];

    const { data: row } = await supabase.from("social_friendships").select("*").eq("id", friendshipId).maybeSingle();
    if (!row || (row.requester_steam_id !== steamId && row.addressee_steam_id !== steamId)) return json({ error: "not_found" }, 404);

    const { error } = await supabase.from("social_friendships").delete().eq("id", friendshipId);
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // GET social/blocks — the list, so a block can be undone
  if (route.length === 1 && route[0] === "blocks" && req.method === "GET") {
    const { data: blocks, error } = await supabase.from("social_blocks")
      .select("*").eq("blocker_steam_id", steamId).order("created_at", { ascending: false });
    if (error) return json({ error: error.message }, 500);

    const targetIds = (blocks ?? []).map((b) => b.blocked_steam_id);
    const profileMap = await profilesFor(supabase, targetIds);

    const rows = (blocks ?? []).map((b) => ({
      blocked_id: b.blocked_steam_id,
      blocked: userJson(b.blocked_steam_id, profileMap.get(b.blocked_steam_id)),
      created_at: b.created_at,
    }));

    return json({ data: rows });
  }

  // POST social/blocks { user_id }
  if (route.length === 1 && route[0] === "blocks" && req.method === "POST") {
    const body = await req.json().catch(() => ({}));
    const target = typeof body.user_id === "string" ? body.user_id : null;
    if (!target) return json({ error: "user_id required" }, 400);
    const { error } = await supabase.from("social_blocks").upsert({ blocker_steam_id: steamId, blocked_steam_id: target }, { onConflict: "blocker_steam_id,blocked_steam_id" });
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // DELETE social/blocks/{userId} — a repeat is treated as already done, not an error
  if (route.length === 2 && route[0] === "blocks" && req.method === "DELETE") {
    const target = route[1];
    const { error } = await supabase.from("social_blocks")
      .delete().eq("blocker_steam_id", steamId).eq("blocked_steam_id", target);
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  // POST social/reports { user_id, reason, message_id, note }
  if (route.length === 1 && route[0] === "reports" && req.method === "POST") {
    const body = await req.json().catch(() => ({}));
    const { error } = await supabase.from("social_reports").insert({
      reporter_steam_id: steamId,
      target_steam_id: typeof body.user_id === "string" ? body.user_id : null,
      target_message_id: typeof body.message_id === "string" ? body.message_id : null,
      reason: typeof body.reason === "string" ? body.reason : "unspecified",
      details: typeof body.note === "string" ? body.note : null,
    });
    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  return json({ error: "Rota não encontrada" }, 404);
});
