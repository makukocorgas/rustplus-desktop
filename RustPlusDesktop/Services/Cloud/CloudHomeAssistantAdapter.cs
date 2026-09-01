using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Manages the per-user Home Assistant API token against our own Supabase functions.
    ///
    /// The token authenticates the <c>home-assistant</c> edge function's <c>switch/*</c>
    /// endpoints, so this is effectively API-key management: the user views it (to paste
    /// into their configuration.yaml), regenerates it, or revokes it. What that token
    /// actually unlocks — turning a switch on the Rust server the user is connected to —
    /// is <see cref="HomeAssistantRelay"/>'s job, since only the desktop app itself holds
    /// the live Rust+ connection a command needs.
    /// </summary>
    public static class CloudHomeAssistantAdapter
    {
        /// <summary>The current Home Assistant token, or null when none has been generated.</summary>
        public static async Task<string?> GetTokenAsync()
        {
            var body = await SupabaseAuthManager.CallEdgeFunctionAsync("home-assistant/token", HttpMethod.Get);
            return ExtractToken(body);
        }

        /// <summary>Generate a fresh token (invalidating any previous one) and return it.</summary>
        public static async Task<string?> RegenerateTokenAsync()
        {
            var body = await SupabaseAuthManager.CallEdgeFunctionAsync("home-assistant/token/regenerate", HttpMethod.Post);
            return ExtractToken(body);
        }

        /// <summary>Remove the token, disabling the switch endpoints for this account.</summary>
        public static Task RevokeAsync() =>
            SupabaseAuthManager.CallEdgeFunctionAsync("home-assistant/token", HttpMethod.Delete);

        private static string? ExtractToken(string body)
        {
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return null;

            return data.TryGetProperty("api_token", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;
        }
    }
}
