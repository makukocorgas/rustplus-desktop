using System;
using System.Collections.Generic;
using System.Linq;
using RustPlusDesk.Models.Craft;

namespace RustPlusDesk.Services.Craft;

/// <summary>
/// Expands an item's crafting recipe recursively down to base resources, aggregating quantities
/// along the way. Guards against runaway/cyclic data with a depth cap and a visiting-stack check.
/// </summary>
public sealed class CraftCalculatorEngine
{
    private const int MaxDepth = 12;

    private readonly Dictionary<int, CraftItem> _byId;
    private readonly Dictionary<string, CraftItem> _byShortname;

    public CraftCalculatorEngine(CraftDataSet data)
    {
        _byId = data.Items.ToDictionary(item => item.ItemId);
        _byShortname = data.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Shortname))
            .GroupBy(item => item.Shortname, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public CraftItem? Find(int itemId) => itemId != 0 ? _byId.GetValueOrDefault(itemId) : null;
    public CraftItem? Find(string? shortname) =>
        string.IsNullOrWhiteSpace(shortname) ? null : _byShortname.GetValueOrDefault(shortname);

    public CraftTreeNode BuildTree(CraftItem root, double quantity)
    {
        var visitingStack = new HashSet<int>();
        return BuildNode(root.ItemId, root.Shortname, root.DisplayName, quantity, depth: 0, visitingStack);
    }

    private CraftTreeNode BuildNode(int itemId, string shortname, string displayName, double quantity, int depth, HashSet<int> visitingStack)
    {
        CraftItem? item = Find(itemId) ?? Find(shortname);

        bool cyclic = itemId != 0 && !visitingStack.Add(itemId);
        bool isLeaf = item is null || item.IsBaseResource || item.Ingredients.Count == 0 || depth >= MaxDepth || cyclic;

        if (isLeaf)
            return new CraftTreeNode(item, shortname, displayName, quantity, depth, IsBaseResource: true, Children: []);

        // Crafting only happens in whole batches (you can't queue "half a craft"), so round the
        // number of craft actions up to the nearest whole number. This means the actual amount
        // produced can exceed what was requested (e.g. asking for 5 Ammo when OutputQuantity is 3
        // requires 2 craft actions, yielding 6) — that rounded-up total is what actually gets
        // crafted, so it's what we report and what drives the ingredient totals below.
        int outputQuantity = Math.Max(1, item!.OutputQuantity);
        double craftActions = Math.Ceiling(quantity / outputQuantity);
        double actualQuantity = craftActions * outputQuantity;

        var children = new List<CraftTreeNode>(item.Ingredients.Count);
        foreach (CraftIngredient ingredient in item.Ingredients)
        {
            children.Add(BuildNode(
                ingredient.ItemId, ingredient.Shortname, ingredient.DisplayName,
                ingredient.Quantity * craftActions, depth + 1, visitingStack));
        }

        visitingStack.Remove(itemId);
        return new CraftTreeNode(item, shortname, displayName, actualQuantity, depth, IsBaseResource: false, children);
    }

    /// <summary>Sums every leaf (base resource) in the tree by shortname/display name.</summary>
    public static IReadOnlyList<CraftBaseResourceTotal> AggregateBaseResources(CraftTreeNode root)
    {
        var totals = new Dictionary<string, CraftBaseResourceTotal>(StringComparer.OrdinalIgnoreCase);

        void Walk(CraftTreeNode node)
        {
            if (node.IsBaseResource)
            {
                string key = string.IsNullOrWhiteSpace(node.Shortname) ? node.DisplayName : node.Shortname;
                totals[key] = totals.TryGetValue(key, out CraftBaseResourceTotal? existing)
                    ? existing with { Amount = existing.Amount + node.Quantity }
                    : new CraftBaseResourceTotal(node.Shortname, node.Item?.ItemId ?? 0, node.DisplayName, node.Quantity);
                return;
            }

            foreach (CraftTreeNode child in node.Children) Walk(child);
        }

        Walk(root);
        return totals.Values.OrderByDescending(total => total.Amount).ToList();
    }
}
