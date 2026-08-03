using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models.Craft;

namespace RustPlusDesk.Services.Craft;

public sealed class CraftDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<CraftDataSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using Stream stream = OpenDataStream();
        var data = await JsonSerializer.DeserializeAsync<CraftDataSet>(stream, JsonOptions, cancellationToken)
                   ?? throw new InvalidDataException("Craft data is empty.");
        Validate(data);
        return data;
    }

    private static Stream OpenDataStream()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Data", "craft-data.json");
        if (File.Exists(filePath))
            return File.OpenRead(filePath);

        Assembly assembly = typeof(CraftDataService).Assembly;
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("Assets.Data.craft-data.json", StringComparison.OrdinalIgnoreCase));
        return resourceName is not null
            ? assembly.GetManifestResourceStream(resourceName)!
            : throw new FileNotFoundException("The packaged craft-data.json asset is missing.", filePath);
    }

    /// <summary>
    /// Validation is intentionally lenient about cross-references: unlike raid-data.json, this dataset is
    /// expected to grow incrementally and an ingredient may point at an item that doesn't have its own
    /// entry yet. The calculator engine treats any such reference as an implicit leaf/base resource.
    /// </summary>
    public static void Validate(CraftDataSet data)
    {
        if (data.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported craft data schema version {data.SchemaVersion}.");
        if (data.Items.Count == 0)
            throw new InvalidDataException("Craft data must contain at least one item.");

        var seenIds = new HashSet<int>();
        foreach (CraftItem item in data.Items)
        {
            if (item.ItemId == 0 || !seenIds.Add(item.ItemId) || string.IsNullOrWhiteSpace(item.DisplayName))
                throw new InvalidDataException("Craft data contains an invalid or duplicate item.");
            if (item.OutputQuantity <= 0)
                throw new InvalidDataException($"Craft item '{item.DisplayName}' has a non-positive output quantity.");
            foreach (CraftIngredient ingredient in item.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ingredient.DisplayName) || !double.IsFinite(ingredient.Quantity) || ingredient.Quantity <= 0)
                    throw new InvalidDataException($"Craft item '{item.DisplayName}' has a malformed ingredient.");
            }
        }
    }
}
