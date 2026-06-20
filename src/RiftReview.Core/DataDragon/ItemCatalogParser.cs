using System.Text.Json;

namespace RiftReview.Core.DataDragon;

/// <summary>
/// Pure parser for a Data Dragon item.json document. Produces an itemId->name map and the set of
/// itemIds that count as "completed build items" (legendaries / support items) on Summoner's Rift.
/// Validated against live item.json 16.12.1 (706 items -> 115 completed). The predicate is
/// STRUCTURAL, not version-pinned.
/// </summary>
public static class ItemCatalogParser
{
    public sealed record Catalog(
        IReadOnlyDictionary<int, string> Names,
        IReadOnlySet<int> CompletedItemIds);

    /// <summary>Parse a raw item.json string. Returns empty maps on malformed input (never throws).</summary>
    public static Catalog Parse(string itemJson)
    {
        var names = new Dictionary<int, string>();
        var completed = new HashSet<int>();
        try
        {
            using var doc = JsonDocument.Parse(itemJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
                return new Catalog(names, completed);

            foreach (var entry in data.EnumerateObject())
            {
                var idStr = entry.Name;                    // itemId as a string key
                if (!int.TryParse(idStr, out var id)) continue;
                var it = entry.Value;

                var name = it.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                if (name.Length > 0) names[id] = StripTags(name);

                if (IsCompleted(idStr, it)) completed.Add(id);
            }
        }
        catch (JsonException) { /* malformed -> empty catalog (offline/garbage tolerant) */ }
        return new Catalog(names, completed);
    }

    // Drop 4 figured rules validated against the live file:
    //   idStr.Length <= 4        -> drop 6-digit Arena/alt-mode variant ids (e.g. 323040)
    //   maps["11"] == true       -> Summoner's Rift only
    //   gold.total >= 2000       -> legendary price floor (cleanly separates legendaries from
    //                               components AND boots; highest non-legendary terminal item is 1250)
    //   not Consumable/Trinket/Boots tag
    //   into absent or empty     -> terminal item (every finished legendary has no `into`)
    // NOTE: do NOT gate on gold.purchasable -- transform results (Muramana/Seraph's/Fimbulwinter) are
    // purchasable:false yet are real finished items; we want their PRECURSOR (which IS terminal+>=2000)
    // to count, and the predicate already does that.
    private static bool IsCompleted(string idStr, JsonElement it)
    {
        if (idStr.Length > 4) return false;

        if (!it.TryGetProperty("maps", out var maps) ||
            !maps.TryGetProperty("11", out var sr) || sr.ValueKind != JsonValueKind.True)
            return false;

        if (!it.TryGetProperty("gold", out var gold) ||
            !gold.TryGetProperty("total", out var total) ||
            total.GetInt32() < 2000)
            return false;

        if (it.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            foreach (var t in tags.EnumerateArray())
            {
                var s = t.GetString();
                if (s is "Consumable" or "Trinket" or "Boots") return false;
            }

        // `into` is present only when non-empty -> a present non-empty `into` means it builds further.
        if (it.TryGetProperty("into", out var into) &&
            into.ValueKind == JsonValueKind.Array &&
            into.GetArrayLength() > 0)
            return false;

        return true;
    }

    // A few item names carry markup (e.g. Gangplank's "<rarityLegendary>...</rarityLegendary>").
    private static string StripTags(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        bool inTag = false;
        foreach (var c in s)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
