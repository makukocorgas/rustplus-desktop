using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private static readonly HttpClient _discordMapHttpClient = new HttpClient();

    public async Task<string> GetCurrentMapScreenshotBase64Async()
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                int width = (int)WebViewHost.ActualWidth;
                int height = (int)WebViewHost.ActualHeight;

                if (width <= 0 || height <= 0) return "";

                var rtb = new RenderTargetBitmap(width, height, 96d, 96d, PixelFormats.Pbgra32);
                
                // Render the entire WebViewHost which contains the map, grid, and overlays with their current transforms
                rtb.Render(WebViewHost);

                var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                AppendLog($"[Screenshot] Error capturing current map: {ex.Message}");
                return "";
            }
        });
    }

    public async Task<string> GetFullMapScreenshotBase64Async()
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (ImgMap.Source == null) return "";

                // Use the original map image dimensions as a base
                double mapW = ImgMap.Source.Width;
                double mapH = ImgMap.Source.Height;
                
                // If the map is huge, we might want to scale it down slightly for Discord, e.g. max 2500px
                double scale = 1.0;
                if (mapW > 2500) scale = 2500.0 / mapW;
                
                int renderW = (int)(mapW * scale);
                int renderH = (int)(mapH * scale);

                var visual = new DrawingVisual();
                using (var ctx = visual.RenderOpen())
                {
                    // 1. Draw base Map Image
                    ctx.DrawImage(ImgMap.Source, new Rect(0, 0, renderW, renderH));

                    // 2. Draw Grid and Overlays. Since they are Canvases whose children are absolutely positioned 
                    // based on world coords mapped to original ImgMap size, we can draw them with a VisualBrush.
                    // However, VisualBrush might not render children that are outside the current 'viewport' of the UI correctly
                    // if virtualization is happening, but Canvas does not virtualize. 
                    // We need to temporarily remove the transform if we were to render them directly, but VisualBrush 
                    // captures the element's rendering. Wait, VisualBrush captures the *rendered* element, so if it's currently
                    // clipped or transformed by the parent, it might be an issue.
                    // A better approach in WPF for off-screen rendering of an entire Canvas without affecting UI is using a VisualBrush 
                    // of the Canvas *itself* (not its transformed parent).
                    
                    var gridBrush = new VisualBrush(GridLayer) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
                    var overlayBrush = new VisualBrush(Overlay) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };

                    // We apply the scale transform to the context so the overlays scale down matching the map
                    ctx.PushTransform(new ScaleTransform(scale, scale));
                    ctx.DrawRectangle(gridBrush, null, new Rect(0, 0, mapW, mapH));
                    ctx.DrawRectangle(overlayBrush, null, new Rect(0, 0, mapW, mapH));
                    ctx.Pop();
                }

                var rtb = new RenderTargetBitmap(renderW, renderH, 96d, 96d, PixelFormats.Pbgra32);
                rtb.Render(visual);

                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                AppendLog($"[Screenshot] Error capturing full map: {ex.Message}");
                return "";
            }
        });
    }

    public async Task<bool> UploadMapScreenshotToDiscordAsync(string base64Image, string? interactionToken, string? applicationId, string? channelId)
    {
        try
        {
            if (string.IsNullOrEmpty(base64Image)) return false;

            byte[] imageBytes = Convert.FromBase64String(base64Image);

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(fileContent, "file", "map.jpg");

            if (!string.IsNullOrEmpty(interactionToken))
                content.Add(new StringContent(interactionToken), "interaction_token");
            if (!string.IsNullOrEmpty(applicationId))
                content.Add(new StringContent(applicationId), "application_id");
            if (!string.IsNullOrEmpty(channelId))
                content.Add(new StringContent(channelId), "channel_id");

            if (RustPlusDesk.Services.Auth.SupabaseAuthManager.IsUpgradeRequiredSnackbarShown)
            {
                AppendLog("[DiscordBot] Skipping map upload: application update is required.");
                return false;
            }

            var url = $"{RustPlusDesk.Services.Data.DataManager.SUPABASE_URL.TrimEnd('/')}/functions/v1/discord-send-map";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("apikey", RustPlusDesk.Services.Data.DataManager.SUPABASE_ANON_KEY);
            request.Headers.Add("X-Client-Version", RustPlusDesk.Helpers.VersionHelper.GetClientVersion());
            
            var token = RustPlusDesk.Services.Auth.SupabaseAuthManager.Client?.Auth?.CurrentSession?.AccessToken;
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            request.Content = content;

            var response = await _discordMapHttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                AppendLog($"[DiscordBot] Map upload failed: {err}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"[DiscordBot] Map upload exception: {ex.Message}");
            return false;
        }
    }

    private async void BtnSendMapToDiscord_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Selected == null) return;
        
        BtnSendMapToDiscord.IsEnabled = false;
        var oldContent = BtnSendMapToDiscord.Content;
        BtnSendMapToDiscord.Content = "Sending...";
        
        try
        {
            // Get guild_id for this Steam ID
            string? guildId = null;
            string? informationChannelId = null;

            try
            {
                if (!string.IsNullOrEmpty(_vm.SteamId64))
                {
                    var guildRes = await RustPlusDesk.Services.Auth.SupabaseAuthManager.Client
                        .From<RustPlusDesk.Models.DiscordBotSettingsModel>()
                        .Filter("owner_steam_id", Postgrest.Constants.Operator.Equals, _vm.SteamId64)
                        .Get();
                    guildId = guildRes.Models?.FirstOrDefault()?.GuildId;
                }

                if (!string.IsNullOrEmpty(guildId))
                {
                    var guildChannelsRes = await RustPlusDesk.Services.Auth.SupabaseAuthManager.Client
                        .From<RustPlusDesk.Models.DiscordGuildChannelsModel>()
                        .Filter("guild_id", Postgrest.Constants.Operator.Equals, guildId)
                        .Get();
                    informationChannelId = guildChannelsRes.Models?.FirstOrDefault()?.InformationId;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Screenshot] Error getting channel config: {ex.Message}");
            }

            if (string.IsNullOrEmpty(guildId))
            {
                MessageBox.Show("Discord bot not configured. Use /setup in your Discord server first.", "Bot Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Capture map screenshot
            var base64 = await GetCurrentMapScreenshotBase64Async();
            if (string.IsNullOrEmpty(base64))
            {
                AppendLog("[Screenshot] Failed to capture map.");
                return;
            }

            // Queue map command with image data for the bot to pick up
            var payload = new Newtonsoft.Json.Linq.JObject
            {
                ["image_base64"] = base64,
                ["channel_id"] = informationChannelId ?? "",
                ["server_name"] = _vm.Selected?.Name ?? "Rust Server"
            };

            await RustPlusDesk.Services.Auth.SupabaseAuthManager.Client
                .From<RustPlusDesk.Models.BotCommandsQueueModel>()
                .Insert(new RustPlusDesk.Models.BotCommandsQueueModel
                {
                    GuildId = guildId,
                    CommandType = "map_screenshot",
                    Payload = payload,
                    Status = "pending"
                });

            AppendLog("Map screenshot queued for Discord — bot will post it shortly.");
        }
        catch (Exception ex)
        {
            AppendLog($"[DiscordBot] Map send error: {ex.Message}");
        }
        finally
        {
            BtnSendMapToDiscord.Content = oldContent;
            BtnSendMapToDiscord.IsEnabled = true;
        }
    }
}
