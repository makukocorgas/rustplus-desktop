import { serve } from "https://deno.land/std@0.168.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.39.8";

const supabaseUrl = Deno.env.get("SUPABASE_URL") || "";
const supabaseServiceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") || "";
const knownAnonKeys = [
  Deno.env.get("SUPABASE_ANON_KEY") || "",
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imlwb3NyYWJjaGx2cGZwZHltZ21qIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODIyNDc0MzcsImV4cCI6MjA5NzgyMzQzN30.XbumfrRPCooBh_f1ZxpbpNB1NhSNWMxHGzuO-rpEarA",
  "sb_publishable_o5yHv76T4wV_Fekg1SXZEw_cAuIsLmO",
].filter((k) => k.length > 0);
const supabase = createClient(supabaseUrl, supabaseServiceKey);

serve(async (req) => {
  if (req.method !== "POST") {
    return new Response("Method not allowed", { status: 405 });
  }

  // Auth: accept service key, any known anon/publishable key, or a valid user/guest JWT
  const authHeader = req.headers.get("Authorization") || "";
  const token = authHeader.replace("Bearer ", "").trim();
  const apikey = req.headers.get("apikey") || "";
  let isAuthorized = false;
  if (token === supabaseServiceKey || apikey === supabaseServiceKey) {
    isAuthorized = true;
  } else if (knownAnonKeys.includes(token) || knownAnonKeys.includes(apikey)) {
    isAuthorized = true;
  } else if (token) {
    const { data: { user }, error: authError } = await supabase.auth.getUser(token);
    if (!authError && user) {
      isAuthorized = true;
    }
  }
  if (!isAuthorized) {
    return new Response("Unauthorized", { status: 401 });
  }

  try {
    const form = await req.formData();
    const file = form.get("file");
    const channelId = form.get("channel_id");
    const interactionToken = form.get("interaction_token");
    const applicationId = form.get("application_id");

    if (!(file instanceof File)) {
      return new Response("Missing file", { status: 400 });
    }

    // Path 1: this is a response to a Discord slash-command interaction (e.g. /map).
    // The client has no channel_id for this — it must reply via the interaction's
    // own follow-up webhook, which needs no bot token.
    if (typeof interactionToken === "string" && interactionToken.length > 0 &&
        typeof applicationId === "string" && applicationId.length > 0) {
      const discordForm = new FormData();
      discordForm.append("files[0]", file, file.name || "map.jpg");

      const discordResponse = await fetch(
        `https://discord.com/api/v10/webhooks/${applicationId}/${interactionToken}/messages/@original`,
        {
          method: "PATCH",
          body: discordForm,
        },
      );

      if (!discordResponse.ok) {
        const errorText = await discordResponse.text();
        return new Response(`Discord API error: ${errorText}`, { status: 502 });
      }

      return new Response(JSON.stringify({ success: true }), {
        headers: { "Content-Type": "application/json" },
      });
    }

    // Path 2: manual "Send to Discord" button — post directly to a known, registered channel.
    if (!channelId || typeof channelId !== "string") {
      return new Response("Missing channel_id (and no interaction_token/application_id provided)", { status: 400 });
    }

    const { data: channelConfig, error: channelConfigError } = await supabase
      .from("discord_channels_config")
      .select("guild_id")
      .eq("channel_id", channelId)
      .limit(1)
      .maybeSingle();
    if (channelConfigError || !channelConfig) {
      return new Response("Discord channel is not configured for Rust+ Desktop bot notifications", { status: 403 });
    }

    const botToken = Deno.env.get("DISCORD_BOT_TOKEN") || "";
    if (!botToken) {
      return new Response("DISCORD_BOT_TOKEN not configured", { status: 500 });
    }

    const discordForm = new FormData();
    discordForm.append("payload_json", JSON.stringify({ content: "" }));
    discordForm.append("files[0]", file, file.name || "map.jpg");

    const discordResponse = await fetch(
      `https://discord.com/api/v10/channels/${channelId}/messages`,
      {
        method: "POST",
        headers: {
          Authorization: `Bot ${botToken}`,
        },
        body: discordForm,
      },
    );

    if (!discordResponse.ok) {
      const errorText = await discordResponse.text();
      return new Response(`Discord API error: ${errorText}`, { status: 502 });
    }

    return new Response(JSON.stringify({ success: true }), {
      headers: { "Content-Type": "application/json" },
    });
  } catch (err) {
    return new Response(`Server error: ${err instanceof Error ? err.message : String(err)}`, { status: 500 });
  }
});
