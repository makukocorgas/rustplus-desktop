using System;
using System.Collections.Generic;

namespace RustPlusDesk.Models.Craft;

public sealed class CraftDataSet
{
    public int SchemaVersion { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public List<CraftItem> Items { get; init; } = [];
}

public sealed class CraftItem
{
    public int ItemId { get; init; }
    public string Shortname { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Workbench tier required to craft this item (1/2/3), or null if none / not applicable.</summary>
    public int? WorkbenchLevelRequired { get; init; }

    /// <summary>Approximate crafting time in seconds. Null when unknown.</summary>
    public double? CraftTimeSeconds { get; init; }

    /// <summary>How many units one craft action produces. Defaults to 1.</summary>
    public int OutputQuantity { get; init; } = 1;

    public List<CraftIngredient> Ingredients { get; init; } = [];

    /// <summary>True for raw/gathered/smelted resources that have no crafting recipe (tree leaves).</summary>
    public bool IsBaseResource { get; init; }

    /// <summary>
    /// True when the numbers were sourced from an already-verified dataset (e.g. raid-data.json).
    /// False when compiled from general knowledge and not yet double-checked against the live game.
    /// </summary>
    public bool Verified { get; init; }

    /// <summary>Free-text provenance note, shown to the user for transparency (not required).</summary>
    public string? Source { get; init; }
}

public sealed class CraftIngredient
{
    public int ItemId { get; init; }
    public string Shortname { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public double Quantity { get; init; }
}

/// <summary>
/// A node in the expanded (recursive) crafting tree for a target item + quantity.
/// Quantity is already resolved for this node (parent multiplier and target quantity applied).
/// </summary>
public sealed record CraftTreeNode(
    CraftItem? Item,
    string Shortname,
    string DisplayName,
    double Quantity,
    int Depth,
    bool IsBaseResource,
    IReadOnlyList<CraftTreeNode> Children);

public sealed record CraftBaseResourceTotal(string Shortname, int ItemId, string DisplayName, double Amount);
