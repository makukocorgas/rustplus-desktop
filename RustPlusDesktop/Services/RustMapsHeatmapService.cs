using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services
{
    public static class RustMapsHeatmapService
    {
        private const string ApiBase    = "https://api.rustmaps.com/v4";
        private const string ApiKey     = "fedbde09-e0cd-459b-a7ca-0246e8b111ff";
        private const int    HeatmapRes = 512; // resolução do heatmap gerado

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders =
            {
                { "X-API-Key",  ApiKey },
                { "User-Agent", "RustPlusDesktop/7.1 (github.com/makukocorgas/rustplus-desktop)" },
            }
        };

        // ── Mapeamento de categorias internas → campos da API RustMaps ──
        // A API v4 devolve listas de posições no campo "entities" com "type" e "x","z"
        private static readonly Dictionary<string, string[]> CategoryToEntityTypes = new()
        {
            // Nodes
            ["ores"]        = new[] { "OreStone", "OreSulfur", "OreMetal", "OreHQM" },
            ["wood"]        = new[] { "WoodPile", "CollectableWoodPile" },
            ["logs"]        = new[] { "LogPile" },

            // Food / Collectables
            ["mushroom"]    = new[] { "Mushroom", "CollectableMushroom" },
            ["berries"]     = new[] { "Berry", "GreenBerry", "BlueBerry", "YellowBerry", "WhiteBerry", "RedBerry", "BlackBerry" },
            ["corn"]        = new[] { "Corn", "CollectableCorn" },
            ["pumpkin"]     = new[] { "Pumpkin", "CollectablePumpkin" },
            ["potato"]      = new[] { "Potato", "CollectablePotato" },
            ["wheat"]       = new[] { "Wheat", "CollectableWheat" },

            // Animals
            ["bear"]        = new[] { "Bear", "PolarBear" },
            ["boar"]        = new[] { "Boar" },
            ["chicken"]     = new[] { "Chicken" },
            ["wolf"]        = new[] { "Wolf" },
            ["stag"]        = new[] { "Stag" },
            ["crocodile"]   = new[] { "Crocodile" },
            ["tiger"]       = new[] { "Tiger" },
            ["snake"]       = new[] { "Snake" },

            // Spawns
            ["junkpiles"]   = new[] { "JunkPile", "JunkPileWater" },
            ["rowboat"]     = new[] { "Rowboat", "RHIB" },
            ["modularcar"]  = new[] { "ModularCar", "BasicCar" },
        };

        // ── Resultado da operação ────────────────────────────────────────
        public record HeatmapFetchResult(bool Success, string? Error = null);

        // ── Ponto de entrada principal ───────────────────────────────────
        /// <summary>
        /// Busca os dados da API do RustMaps e gera o map_data.json
        /// na pasta correcta para o perfil dado.
        /// </summary>
        public static async Task<HeatmapFetchResult> FetchAndGenerateAsync(
            string mapId,
            string outputFolderPath,
            int worldSize,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(mapId))
                return new HeatmapFetchResult(false, "mapId vazio");

            try
            {
                progress?.Report("[Heatmap] A contactar API do RustMaps...");

                // 1. Verificar se o map_data.json já existe e é válido
                string dataPath = Path.Combine(outputFolderPath, "map_data.json");
                if (File.Exists(dataPath))
                {
                    // Verificar se já tem heatmaps
                    try
                    {
                        var existing = await File.ReadAllTextAsync(dataPath, ct);
                        using var existDoc = JsonDocument.Parse(existing);
                        if (existDoc.RootElement.TryGetProperty("heatmaps", out var hm) &&
                            hm.ValueKind == JsonValueKind.Object &&
                            hm.EnumerateObject().MoveNext())
                        {
                            progress?.Report("[Heatmap] Dados já existem localmente.");
                            return new HeatmapFetchResult(true);
                        }
                    }
                    catch { }
                }

                // 2. Buscar mapa da API v4
                progress?.Report("[Heatmap] A buscar dados do mapa...");
                var mapData = await FetchMapDataAsync(mapId, ct);
                if (mapData == null)
                    return new HeatmapFetchResult(false, "Mapa não encontrado na API do RustMaps. Verifica se o mapId está correcto.");

                // 3. Extrair entidades e gerar heatmaps
                progress?.Report("[Heatmap] A gerar heatmaps...");
                var entities = ExtractEntities(mapData);
                var heatmaps = GenerateHeatmaps(entities, worldSize);

                // 4. Escrever map_data.json
                progress?.Report("[Heatmap] A guardar dados...");
                Directory.CreateDirectory(outputFolderPath);
                await WriteMapDataJsonAsync(dataPath, mapId, worldSize, heatmaps, ct);

                int count = heatmaps.Count;
                progress?.Report($"[Heatmap] ✅ {count} heatmaps gerados com sucesso.");
                return new HeatmapFetchResult(true);
            }
            catch (OperationCanceledException)
            {
                return new HeatmapFetchResult(false, "Cancelado.");
            }
            catch (HttpRequestException ex)
            {
                return new HeatmapFetchResult(false, $"Erro de rede: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new HeatmapFetchResult(false, $"Erro inesperado: {ex.Message}");
            }
        }

        // ── Buscar dados da API v4 ───────────────────────────────────────
        private static async Task<JsonDocument?> FetchMapDataAsync(string mapId, CancellationToken ct)
        {
            // Tentar endpoint directo por mapId
            try
            {
                var url = $"{ApiBase}/maps/{mapId}";
                var resp = await _http.GetStringAsync(url, ct);
                var doc  = JsonDocument.Parse(resp);

                // A API v4 devolve { "data": { ... } }
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Object)
                    return JsonDocument.Parse(data.GetRawText());

                return doc;
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 404)
            {
                return null;
            }
        }

        // ── Extrair entidades do JSON da API ─────────────────────────────
        private static List<(string type, float x, float z)> ExtractEntities(JsonDocument mapData)
        {
            var result = new List<(string, float, float)>();
            var root   = mapData.RootElement;

            // A API v4 pode ter as entidades em campos diferentes conforme o plano
            // Tentar: "entities", "prefabs", "objects", "nodes"
            foreach (var fieldName in new[] { "entities", "prefabs", "objects", "nodes" })
            {
                if (!root.TryGetProperty(fieldName, out var arr) ||
                    arr.ValueKind != JsonValueKind.Array) continue;

                foreach (var el in arr.EnumerateArray())
                {
                    // Tentar vários formatos de type: "type", "category", "name", "prefabName"
                    string? type = null;
                    foreach (var typeField in new[] { "type", "category", "name", "prefabName", "entityType" })
                    {
                        if (el.TryGetProperty(typeField, out var tf) && tf.ValueKind == JsonValueKind.String)
                        {
                            type = tf.GetString();
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(type)) continue;

                    // Coordenadas: "x"/"z" ou "position.x"/"position.z"
                    float x = 0, z = 0;
                    if (el.TryGetProperty("x", out var xEl)) x = xEl.GetSingle();
                    if (el.TryGetProperty("z", out var zEl)) z = zEl.GetSingle();

                    if (el.TryGetProperty("position", out var pos))
                    {
                        if (pos.TryGetProperty("x", out var px)) x = px.GetSingle();
                        if (pos.TryGetProperty("z", out var pz)) z = pz.GetSingle();
                    }

                    result.Add((type, x, z));
                }
                break; // Usar o primeiro campo encontrado
            }

            // Tentar também campos específicos de heatmap que a API pode devolver directamente
            // como arrays de coordenadas por categoria: { "ores": [[x,z], ...], ... }
            foreach (var (category, _) in CategoryToEntityTypes)
            {
                if (!root.TryGetProperty(category, out var arr) ||
                    arr.ValueKind != JsonValueKind.Array) continue;

                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Array)
                    {
                        var items = el.EnumerateArray().GetEnumerator();
                        if (items.MoveNext()) { float.TryParse(items.Current.ToString(), out float cx); 
                        if (items.MoveNext()) { float.TryParse(items.Current.ToString(), out float cz);
                        result.Add((category + "_direct", cx, cz)); }}
                    }
                    else if (el.ValueKind == JsonValueKind.Object)
                    {
                        float cx = 0, cz = 0;
                        if (el.TryGetProperty("x", out var xp)) cx = xp.GetSingle();
                        if (el.TryGetProperty("z", out var zp)) cz = zp.GetSingle();
                        result.Add((category + "_direct", cx, cz));
                    }
                }
            }

            return result;
        }

        // ── Gerar heatmaps 512x512 por categoria ─────────────────────────
        private static Dictionary<string, byte[]> GenerateHeatmaps(
            List<(string type, float x, float z)> entities,
            int worldSize)
        {
            var heatmaps = new Dictionary<string, byte[]>();
            float half   = worldSize / 2f;

            foreach (var (category, entityTypes) in CategoryToEntityTypes)
            {
                var grid = new byte[HeatmapRes * HeatmapRes];
                bool hasData = false;

                foreach (var (type, x, z) in entities)
                {
                    bool match = false;

                    // Match directo (dados já categorizados)
                    if (type == category + "_direct") match = true;

                    // Match por tipo de entidade
                    if (!match)
                    {
                        foreach (var et in entityTypes)
                        {
                            if (type.Contains(et, StringComparison.OrdinalIgnoreCase))
                            {
                                match = true;
                                break;
                            }
                        }
                    }

                    if (!match) continue;

                    // Converter coordenadas mundo → pixel
                    int px = (int)((x + half) / worldSize * HeatmapRes);
                    int py = (int)((z + half) / worldSize * HeatmapRes);

                    // Pintar com raio de influência (Gaussian-like)
                    int radius = Math.Max(3, worldSize / 600);
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = px + dx;
                            int ny = py + dy;
                            if (nx < 0 || nx >= HeatmapRes || ny < 0 || ny >= HeatmapRes) continue;

                            float dist = MathF.Sqrt(dx * dx + dy * dy);
                            if (dist > radius) continue;

                            float intensity = (1f - dist / radius) * 255f;
                            int   idx       = ny * HeatmapRes + nx;
                            grid[idx] = (byte)Math.Min(255, grid[idx] + intensity);
                        }
                    }
                    hasData = true;
                }

                if (hasData) heatmaps[category] = grid;
            }

            return heatmaps;
        }

        // ── Escrever map_data.json ───────────────────────────────────────
        private static async Task WriteMapDataJsonAsync(
            string path,
            string mapId,
            int worldSize,
            Dictionary<string, byte[]> heatmaps,
            CancellationToken ct)
        {
            using var ms  = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });

            writer.WriteStartObject();
            writer.WriteString("RustMapsMapId", mapId);
            writer.WriteNumber("WorldSize", worldSize);
            writer.WriteString("GeneratedAt", DateTime.UtcNow.ToString("O"));
            writer.WriteString("Source", "RustMapsAPI_v4");

            writer.WritePropertyName("heatmaps");
            writer.WriteStartObject();

            foreach (var (category, grid) in heatmaps)
            {
                writer.WriteString(category, Convert.ToBase64String(grid));
            }

            writer.WriteEndObject(); // heatmaps
            writer.WriteEndObject(); // root

            await writer.FlushAsync(ct);
            await File.WriteAllBytesAsync(path, ms.ToArray(), ct);
        }

        // ── Verificar se o map_data.json já existe e tem heatmaps ────────
        public static bool HasCachedHeatmaps(string folderPath)
        {
            string path = Path.Combine(folderPath, "map_data.json");
            if (!File.Exists(path)) return false;

            try
            {
                using var text = File.OpenRead(path);
                using var doc  = JsonDocument.Parse(text);
                return doc.RootElement.TryGetProperty("heatmaps", out var hm) &&
                       hm.ValueKind == JsonValueKind.Object &&
                       hm.EnumerateObject().MoveNext();
            }
            catch { return false; }
        }

        // ── Limpar cache de um servidor ───────────────────────────────────
        public static void InvalidateCache(string folderPath)
        {
            string path = Path.Combine(folderPath, "map_data.json");
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
