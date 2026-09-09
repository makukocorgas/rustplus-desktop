using System.Threading.Tasks;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Single auth entry point for the UI. Delegates to <see cref="SupabaseAuthManager"/> —
    /// the only backend this build talks to — so sign-in/out call sites don't need to know
    /// which class actually holds the session.
    /// </summary>
    public static class CloudAuth
    {
        /// <summary>True when an authenticated account session exists.</summary>
        public static bool IsAuthenticated => SupabaseAuthManager.IsAuthenticated;

        /// <summary>True when cloud calls can actually be made right now.</summary>
        public static bool IsCloudAvailable => SupabaseAuthManager.Client != null;

        /// <summary>Initialise the backend (restore a persisted session).</summary>
        public static Task InitializeAsync() => SupabaseAuthManager.InitializeAsync();

        public static async Task<(bool Success, string? Error)> LoginWithDiscordAsync()
        {
            bool ok = await SupabaseAuthManager.LoginWithDiscordAsync();
            return (ok, ok ? null : "Discord sign-in was canceled or failed.");
        }

        public static Task<(bool Success, string? Error)> LoginWithEmailAsync(string email, string password) =>
            SupabaseAuthManager.LoginWithEmailAsync(email, password);

        public static Task LogoutAsync() => SupabaseAuthManager.LogoutAsync();
    }
}
