// Edge Function: auth-handshake
import { serve } from "https://deno.land/std@0.177.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const OVERLAY_SYNC_SECRET_HEX = Deno.env.get("OVERLAY_SYNC_SECRET_HEX")!;
const MIN_CLIENT_VERSION = Deno.env.get("MIN_CLIENT_VERSION") ?? "1.0.0";
const JWT_SECRET = Deno.env.get("GUEST_JWT_SECRET") ?? Deno.env.get("SUPABASE_JWT_SECRET")!;

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-key, content-type, apikey, x-client-version",
};

function hexToBytes(hex: string): Uint8Array {
  const bytes = new Uint8Array(hex.length / 2);
  for (let i = 0; i < hex.length; i += 2) {
    bytes[i / 2] = parseInt(hex.substring(i, i + 2), 16);
  }
  return bytes;
}

async function hmacSha256Hex(keyHex: string, data: string): Promise<string> {
  const keyBytes = hexToBytes(keyHex);
  const cryptoKey = await crypto.subtle.importKey(
    "raw", keyBytes, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]
  );
  const signature = await crypto.subtle.sign(
    "HMAC", cryptoKey, new TextEncoder().encode(data)
  );
  return Array.from(new Uint8Array(signature))
    .map(b => b.toString(16).padStart(2, "0"))
    .join("");
}

async function verifyRsaSignature(
  publicKeyB64: string,
  data: string,
  signatureB64: string
): Promise<boolean> {
  try {
    const pubKeyBytes = Uint8Array.from(atob(publicKeyB64), c => c.charCodeAt(0));
    const cryptoKey = await crypto.subtle.importKey(
      "spki", pubKeyBytes,
      { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
      false, ["verify"]
    );
    const sigBytes = Uint8Array.from(atob(signatureB64), c => c.charCodeAt(0));
    return await crypto.subtle.verify(
      "RSASSA-PKCS1-v1_5", cryptoKey, sigBytes, new TextEncoder().encode(data)
    );
  } catch {
    return false;
  }
}

async function createGuestJwt(steamId: string): Promise<string> {
  const now = Math.floor(Date.now() / 1000);
  const exp = now + 7 * 24 * 60 * 60;
  const header = btoa(JSON.stringify({ alg: "HS256", typ: "JWT" }))
    .replace(/=/g, "").replace(/\+/g, "-").replace(/\//g, "_");
  const payload = btoa(JSON.stringify({
    sub: `guest:${steamId}`,
    role: "authenticated",
    steam_id: steamId,
    tier: "guest",
    iat: now,
    exp
  })).replace(/=/g, "").replace(/\+/g, "-").replace(/\//g, "_");

  const sigInput = `${header}.${payload}`;
  const key = await crypto.subtle.importKey(
    "raw", new TextEncoder().encode(JWT_SECRET),
    { name: "HMAC", hash: "SHA-256" }, false, ["sign"]
  );
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(sigInput));
  const sigB64 = btoa(String.fromCharCode(...new Uint8Array(sig)))
    .replace(/=/g, "").replace(/\+/g, "-").replace(/\//g, "_");

  return `${sigInput}.${sigB64}`;
}

function generateMnemonic(): string {
  const words = ["alpha","bravo","charlie","delta","echo","foxtrot","golf",
    "hotel","india","juliet","kilo","lima","mike","november","oscar",
    "papa","quebec","romeo","sierra","tango","uniform","victor","whiskey",
    "xray","yankee","zulu"];
  return Array.from({ length: 6 }, () => words[Math.floor(Math.random() * words.length)]).join("-");
}

function checkVersion(clientVersion: string | null): boolean {
  if (!clientVersion) return false;
  const parse = (v: string) => v.split(".").map(Number);
  const [ma, mi, p] = parse(clientVersion);
  const [rma, rmi, rp] = parse(MIN_CLIENT_VERSION);
  if (ma !== rma) return ma > rma;
  if (mi !== rmi) return mi > rmi;
  return p >= rp;
}

serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response(null, { headers: CORS });
  }

  const clientVersion = req.headers.get("X-Client-Version");
  if (!checkVersion(clientVersion)) {
    return new Response(JSON.stringify({
      error: "upgrade_required",
      message: `Precisas de atualizar a app para v${MIN_CLIENT_VERSION} ou superior.`,
      upgrade_url: `https://github.com/${Deno.env.get("GITHUB_REPO_OWNER") ?? "makukocorgas"}/rustplus-desktop/releases/latest`
    }), { status: 426, headers: { ...CORS, "Content-Type": "application/json" } });
  }

  const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_KEY);
  const body = await req.json();
  const { steam_id } = body;

  if (!steam_id) {
    return new Response(JSON.stringify({ error: "steam_id obrigatório" }), {
      status: 400, headers: { ...CORS, "Content-Type": "application/json" }
    });
  }

  // ── REGISTO ──
  if (body.client_public_key && body.hmac_signature && body.timestamp) {
    const { client_public_key, hmac_signature, timestamp, client_hash = "" } = body;

    console.log("[handshake] Registration attempt:", steam_id);
    console.log("[handshake] OVERLAY_SYNC_SECRET_HEX length:", OVERLAY_SYNC_SECRET_HEX?.length ?? 0);
    console.log("[handshake] JWT_SECRET length:", JWT_SECRET?.length ?? 0);

    const tsMs = parseInt(timestamp);
    if (Math.abs(Date.now() - tsMs) > 5 * 60 * 1000) {
      console.log("[handshake] Timestamp expired:", Math.abs(Date.now() - tsMs), "ms");
      return new Response(JSON.stringify({ error: "Timestamp inválido ou expirado" }), {
        status: 400, headers: { ...CORS, "Content-Type": "application/json" }
      });
    }

    const expectedHmac = await hmacSha256Hex(
      OVERLAY_SYNC_SECRET_HEX,
      `${steam_id}${timestamp}${client_public_key}${client_hash}`
    );
    console.log("[handshake] HMAC match:", expectedHmac === hmac_signature);

    if (expectedHmac !== hmac_signature) {
      console.log("[handshake] HMAC mismatch — rejecting");
      return new Response(JSON.stringify({ error: "Assinatura HMAC inválida" }), {
        status: 401, headers: { ...CORS, "Content-Type": "application/json" }
      });
    }

    const recoveryCode = generateMnemonic();
    const recoveryHash = await hmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, recoveryCode + steam_id);

    await supabase.from("user_profiles").upsert({
      steam_id,
      subscription_tier: "guest",
      sync_accepted: false,
      last_active_at: new Date().toISOString(),
    }, { onConflict: "steam_id", ignoreDuplicates: true });

    await supabase.from("guest_keys").upsert({
      steam_id,
      public_key_b64: client_public_key,
      recovery_hash: recoveryHash,
      updated_at: new Date().toISOString(),
    }, { onConflict: "steam_id" });

    const token = await createGuestJwt(steam_id);
    console.log("[handshake] Token generated, length:", token?.length ?? 0);
    return new Response(JSON.stringify({ token, recovery_code: recoveryCode }), {
      headers: { ...CORS, "Content-Type": "application/json" }
    });
  }

  // ── REFRESH ──
  if (body.signature && body.nonce && body.timestamp) {
    const { signature, nonce, timestamp, client_hash = "" } = body;

    const { data: keyRow } = await supabase
      .from("guest_keys")
      .select("public_key_b64")
      .eq("steam_id", steam_id)
      .single();

    if (!keyRow) {
      return new Response(JSON.stringify({ error: "Chave pública não encontrada. Regista-te novamente." }), {
        status: 404, headers: { ...CORS, "Content-Type": "application/json" }
      });
    }

    const valid = await verifyRsaSignature(
      keyRow.public_key_b64,
      timestamp + nonce + client_hash,
      signature
    );

    if (!valid) {
      return new Response(JSON.stringify({ error: "Assinatura RSA inválida" }), {
        status: 401, headers: { ...CORS, "Content-Type": "application/json" }
      });
    }

    const token = await createGuestJwt(steam_id);
    return new Response(JSON.stringify({ token }), {
      headers: { ...CORS, "Content-Type": "application/json" }
    });
  }

  // ── RECUPERAÇÃO ──
  if (body.new_public_key && body.recovery_signature && body.mnemonic_token) {
    const { new_public_key, mnemonic_token } = body;

    const { data: keyRow } = await supabase
      .from("guest_keys")
      .select("recovery_hash")
      .eq("steam_id", steam_id)
      .single();

    if (!keyRow) {
      return new Response(JSON.stringify({ error: "Conta não encontrada" }), {
        status: 404, headers: { ...CORS, "Content-Type": "application/json" }
      });
    }

    const expectedHash = await hmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, mnemonic_token + steam_id);
    if (expectedHash !== keyRow.recovery_hash) {
      return new Response(JSON.stringify({ error: "Código de recuperação inválido" }), {
        status: 401, headers: { ...CORS, "Content-Type": "application/json" }
      });
    }

    const newRecoveryCode = generateMnemonic();
    const newRecoveryHash = await hmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, newRecoveryCode + steam_id);

    await supabase.from("guest_keys").update({
      public_key_b64: new_public_key,
      recovery_hash: newRecoveryHash,
      updated_at: new Date().toISOString(),
    }).eq("steam_id", steam_id);

    const token = await createGuestJwt(steam_id);
    return new Response(JSON.stringify({ token, new_recovery_code: newRecoveryCode }), {
      headers: { ...CORS, "Content-Type": "application/json" }
    });
  }

  return new Response(JSON.stringify({ error: "Pedido inválido" }), {
    status: 400, headers: { ...CORS, "Content-Type": "application/json" }
  });
});