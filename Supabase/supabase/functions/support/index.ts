// Edge Function: support
// Support tickets and the notification centre, from the client's side. Identity is steam_id,
// taken from the JWT — never trusted from the body. Attachments live in the private
// "support-attachments" storage bucket and are only ever handed out as short-lived signed URLs
// after an ownership check, never served off a public bucket URL.
//
// GET  support/tickets                        -> { data: TicketSummary[] }
// GET  support/tickets/meta                   -> { categories, appealable_sanction }
// GET  support/tickets/{id}                   -> { data: TicketDetail }
// POST support/tickets                        multipart: category, subject, body, sanction_id?, attachments[]?
// POST support/tickets/{id}/messages          multipart: body, attachments[]?
// POST support/tickets/{id}/read
// GET  support/notifications                  -> { data: NotificationItem[] }
// GET  support/notifications/unread-count     -> { data: { unread } }
// POST support/notifications/{id}/read
// POST support/notifications/read-all

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const ATTACHMENTS_BUCKET = "support-attachments";
const MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;
const SIGNED_URL_TTL_SECONDS = 60 * 10;

const CATEGORIES = ["bug", "account", "billing", "abuse_report", "feature_request", "other"];

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

// deno-lint-ignore no-explicit-any
async function isStaff(supabase: any, steamId: string): Promise<boolean> {
  const { data } = await supabase.from("social_moderators").select("steam_id").eq("steam_id", steamId).maybeSingle();
  return !!data;
}

// deno-lint-ignore no-explicit-any
async function uploadAttachments(supabase: any, form: FormData, ticketId: string | null, messageId: string | null) {
  const files = form.getAll("attachments[]").filter((f): f is File => f instanceof File);
  for (const file of files) {
    if (file.size <= 0 || file.size > MAX_ATTACHMENT_BYTES) continue;

    const storagePath = `${ticketId ?? messageId}/${crypto.randomUUID()}-${file.name}`;
    const bytes = new Uint8Array(await file.arrayBuffer());
    const { error: uploadError } = await supabase.storage.from(ATTACHMENTS_BUCKET).upload(storagePath, bytes, {
      contentType: file.type || "application/octet-stream",
    });
    if (uploadError) continue;

    await supabase.from("support_attachments").insert({
      ticket_id: ticketId,
      message_id: messageId,
      storage_path: storagePath,
      name: file.name,
      size: file.size,
      mime: file.type || "application/octet-stream",
    });
  }
}

// deno-lint-ignore no-explicit-any
async function attachmentsFor(supabase: any, column: "ticket_id" | "message_id", id: string) {
  const { data } = await supabase.from("support_attachments").select("id, name, size, mime, storage_path").eq(column, id);
  const rows = data ?? [];

  const withUrls = await Promise.all(rows.map(async (a: { id: string; name: string; size: number; mime: string; storage_path: string }) => {
    const { data: signed } = await supabase.storage.from(ATTACHMENTS_BUCKET)
      .createSignedUrl(a.storage_path, SIGNED_URL_TTL_SECONDS);
    return { id: a.id, name: a.name, size: a.size, mime: a.mime, url: signed?.signedUrl ?? null };
  }));

  return withUrls;
}

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);
  const segments = url.pathname.split("/").filter(Boolean);
  const slugIdx = segments.indexOf("support");
  const route = slugIdx >= 0 ? segments.slice(slugIdx + 1) : segments;

  const steamId = getSteamIdFromJwt(req);
  if (!steamId) return json({ error: "Não autenticado" }, 401);

  // GET support/tickets/meta — categories + the caller's own active mute, if any, to appeal
  if (route.length === 2 && route[0] === "tickets" && route[1] === "meta" && req.method === "GET") {
    const { data: sanction } = await supabase.from("social_sanctions")
      .select("id, kind, reason, expires_at")
      .eq("steam_id", steamId)
      .gt("expires_at", new Date().toISOString())
      .order("created_at", { ascending: false })
      .limit(1)
      .maybeSingle();

    return json({
      categories: CATEGORIES,
      appealable_sanction: sanction
        ? { id: sanction.id, kind: sanction.kind, reason: sanction.reason, expires_at: sanction.expires_at }
        : null,
    });
  }

  // GET support/tickets — the caller's own tickets, newest activity first
  if (route.length === 1 && route[0] === "tickets" && req.method === "GET") {
    const { data: tickets, error } = await supabase.from("support_tickets")
      .select("id, category, status, resolution, subject, updated_at")
      .eq("steam_id", steamId)
      .order("updated_at", { ascending: false });
    if (error) return json({ error: error.message }, 500);

    const { data: reads } = await supabase.from("support_ticket_reads")
      .select("ticket_id, read_at").eq("steam_id", steamId);
    const readMap = new Map((reads ?? []).map((r: { ticket_id: string; read_at: string }) => [r.ticket_id, r.read_at]));

    const data = (tickets ?? []).map((t: { id: string; category: string; status: string; resolution: string | null; subject: string; updated_at: string }) => {
      const readAt = readMap.get(t.id) as string | undefined;
      return {
        id: t.id,
        category: t.category,
        status: t.status,
        resolution: t.resolution,
        subject: t.subject,
        has_unread: !readAt || new Date(readAt) < new Date(t.updated_at),
        last_activity_at: t.updated_at,
      };
    });

    return json({ data });
  }

  // GET support/tickets/{id} — full thread, own tickets only
  if (route.length === 2 && route[0] === "tickets" && route[1] !== "meta" && req.method === "GET") {
    const ticketId = route[1];
    const { data: ticket } = await supabase.from("support_tickets").select("*").eq("id", ticketId).eq("steam_id", steamId).maybeSingle();
    if (!ticket) return json({ error: "não encontrado" }, 404);

    const { data: messageRows } = await supabase.from("support_ticket_messages")
      .select("id, author_steam_id, is_staff, kind, body, created_at")
      .eq("ticket_id", ticketId)
      .order("created_at", { ascending: true });

    const messages = await Promise.all((messageRows ?? []).map(async (m: { id: string; author_steam_id: string; is_staff: boolean; kind: string; body: string; created_at: string }) => {
      const { data: authorProfile } = await supabase.from("user_profiles").select("discord_name").eq("steam_id", m.author_steam_id).maybeSingle();
      return {
        id: m.id,
        kind: m.kind,
        body: m.body,
        author: { name: authorProfile?.discord_name ?? null },
        is_staff: m.is_staff,
        attachments: await attachmentsFor(supabase, "message_id", m.id),
        created_at: m.created_at,
      };
    }));

    return json({
      data: {
        id: ticket.id,
        category: ticket.category,
        status: ticket.status,
        resolution: ticket.resolution,
        subject: ticket.subject,
        body: ticket.body,
        attachments: await attachmentsFor(supabase, "ticket_id", ticket.id),
        messages,
        created_at: ticket.created_at,
      },
    });
  }

  // POST support/tickets — multipart: category, subject, body, sanction_id?, attachments[]?
  if (route.length === 1 && route[0] === "tickets" && req.method === "POST") {
    const form = await req.formData();
    const category = String(form.get("category") ?? "other");
    const subject = String(form.get("subject") ?? "").trim();
    const body = String(form.get("body") ?? "").trim();
    const sanctionId = form.get("sanction_id");

    if (!subject || !body) return json({ error: "subject and body required" }, 400);

    const { data: inserted, error } = await supabase.from("support_tickets").insert({
      steam_id: steamId,
      category: CATEGORIES.includes(category) ? category : "other",
      subject,
      body,
      sanction_id: typeof sanctionId === "string" && sanctionId ? sanctionId : null,
    }).select("id").single();
    if (error || !inserted) return json({ error: error?.message ?? "insert failed" }, 500);

    await uploadAttachments(supabase, form, inserted.id, null);
    await supabase.from("support_ticket_reads").upsert({ ticket_id: inserted.id, steam_id: steamId, read_at: new Date().toISOString() });

    return json({ ok: true, data: { id: inserted.id } });
  }

  // POST support/tickets/{id}/messages — multipart: body, attachments[]?
  if (route.length === 3 && route[0] === "tickets" && route[2] === "messages" && req.method === "POST") {
    const ticketId = route[1];
    const { data: ticket } = await supabase.from("support_tickets").select("id, status").eq("id", ticketId).eq("steam_id", steamId).maybeSingle();
    if (!ticket) return json({ error: "não encontrado" }, 404);
    if (ticket.status === "resolved" || ticket.status === "closed") return json({ error: "ticket_closed" }, 409);

    const form = await req.formData();
    const body = String(form.get("body") ?? "").trim();
    if (!body) return json({ error: "empty" }, 400);

    const { data: message, error } = await supabase.from("support_ticket_messages").insert({
      ticket_id: ticketId,
      author_steam_id: steamId,
      is_staff: await isStaff(supabase, steamId),
      kind: "message",
      body,
    }).select("id").single();
    if (error || !message) return json({ error: error?.message ?? "insert failed" }, 500);

    await uploadAttachments(supabase, form, null, message.id);
    await supabase.from("support_tickets").update({ updated_at: new Date().toISOString() }).eq("id", ticketId);
    await supabase.from("support_ticket_reads").upsert({ ticket_id: ticketId, steam_id: steamId, read_at: new Date().toISOString() });

    return json({ ok: true, data: { id: message.id } });
  }

  // POST support/tickets/{id}/read
  if (route.length === 3 && route[0] === "tickets" && route[2] === "read" && req.method === "POST") {
    const ticketId = route[1];
    await supabase.from("support_ticket_reads").upsert({ ticket_id: ticketId, steam_id: steamId, read_at: new Date().toISOString() });
    return json({ ok: true });
  }

  // GET support/notifications
  if (route.length === 1 && route[0] === "notifications" && req.method === "GET") {
    const { data, error } = await supabase.from("support_notifications")
      .select("*").eq("steam_id", steamId).order("created_at", { ascending: false }).limit(100);
    if (error) return json({ error: error.message }, 500);
    return json({ data });
  }

  // GET support/notifications/unread-count
  if (route.length === 2 && route[0] === "notifications" && route[1] === "unread-count" && req.method === "GET") {
    const { count } = await supabase.from("support_notifications")
      .select("id", { count: "exact", head: true }).eq("steam_id", steamId).is("read_at", null);
    return json({ data: { unread: count ?? 0 } });
  }

  // POST support/notifications/read-all
  if (route.length === 2 && route[0] === "notifications" && route[1] === "read-all" && req.method === "POST") {
    await supabase.from("support_notifications").update({ read_at: new Date().toISOString() }).eq("steam_id", steamId).is("read_at", null);
    return json({ ok: true });
  }

  // POST support/notifications/{id}/read
  if (route.length === 3 && route[0] === "notifications" && route[2] === "read" && req.method === "POST") {
    await supabase.from("support_notifications").update({ read_at: new Date().toISOString() }).eq("id", route[1]).eq("steam_id", steamId);
    return json({ ok: true });
  }

  return json({ error: "Rota não encontrada" }, 404);
});
