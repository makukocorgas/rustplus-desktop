// Edge Function: admin
// Pasta: supabase/functions/admin/index.ts

import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-key, content-type, apikey, x-client-version",
};

serve(async (req) => {
  if (req.method === "OPTIONS") return new Response(null, { headers: CORS });

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const url = new URL(req.url);
  const path = url.pathname.replace(/^\/functions\/v1\/admin\/?/, "").replace(/^\/+/, "");

  function json(data: unknown, status = 200) {
    return new Response(JSON.stringify(data), {
      status,
      headers: { ...CORS, "Content-Type": "application/json" },
    });
  }

  // Every route below this line needs the caller to actually be an admin — checked
  // once here rather than repeated per route.
  async function requireAdmin(): Promise<{ userId: string } | Response> {
    const authHeader = req.headers.get("authorization");
    if (!authHeader) return json({ error: "Não autenticado" }, 401);

    const token = authHeader.replace("Bearer ", "");
    const { data: { user }, error } = await supabase.auth.getUser(token);
    if (error || !user) return json({ error: "Não autenticado" }, 401);

    const { data: adminRow } = await supabase
      .from("admins")
      .select("user_id")
      .eq("user_id", user.id)
      .single();

    if (!adminRow) return json({ error: "Acesso negado" }, 403);
    return { userId: user.id };
  }

  if (path === "check" && req.method === "GET") {
    const authHeader = req.headers.get("authorization");
    if (!authHeader) return json({ is_admin: false });

    const token = authHeader.replace("Bearer ", "");
    const { data: { user }, error } = await supabase.auth.getUser(token);
    if (error || !user) return json({ is_admin: false });

    const { data: adminRow } = await supabase
      .from("admins")
      .select("user_id")
      .eq("user_id", user.id)
      .single();

    return json({ is_admin: !!adminRow });
  }

  // GET /users — every player profile, for the desktop admin panel's user grid.
  if (path === "users" && req.method === "GET") {
    const admin = await requireAdmin();
    if (admin instanceof Response) return admin;

    const { data, error } = await supabase
      .from("user_profiles")
      .select("*")
      .order("last_active_at", { ascending: false });

    if (error) return json({ error: error.message }, 500);
    return json(data ?? []);
  }

  // POST /set-supporter — the manual override toggle on the same grid.
  if (path === "set-supporter" && req.method === "POST") {
    const admin = await requireAdmin();
    if (admin instanceof Response) return admin;

    const body = await req.json();
    const { steam_id, is_manual_supporter } = body;
    if (!steam_id) return json({ error: "steam_id obrigatório" }, 400);

    const { error } = await supabase
      .from("user_profiles")
      .update({
        is_manual_supporter: !!is_manual_supporter,
        manual_premium_at: is_manual_supporter ? new Date().toISOString() : null,
      })
      .eq("steam_id", steam_id);

    if (error) return json({ error: error.message }, 500);
    return json({ ok: true });
  }

  return json({ error: "Rota não encontrada" }, 404);
});
