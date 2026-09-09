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

  if (path === "check" && req.method === "GET") {
    const authHeader = req.headers.get("authorization");
    if (!authHeader) {
      return new Response(JSON.stringify({ is_admin: false }), {
        headers: { ...CORS, "Content-Type": "application/json" }
      });
    }

    const token = authHeader.replace("Bearer ", "");
    const { data: { user }, error } = await supabase.auth.getUser(token);

    if (error || !user) {
      return new Response(JSON.stringify({ is_admin: false }), {
        headers: { ...CORS, "Content-Type": "application/json" }
      });
    }

    const { data: adminRow } = await supabase
      .from("admins")
      .select("user_id")
      .eq("user_id", user.id)
      .single();

    return new Response(JSON.stringify({ is_admin: !!adminRow }), {
      headers: { ...CORS, "Content-Type": "application/json" }
    });
  }

  return new Response(JSON.stringify({ error: "Rota não encontrada" }), {
    status: 404,
    headers: { ...CORS, "Content-Type": "application/json" }
  });
});
