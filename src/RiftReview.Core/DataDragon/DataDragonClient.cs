using System.Text.Json;

namespace RiftReview.Core.DataDragon;

public sealed class DataDragonClient
{
    private const string Base = "https://ddragon.leagueoflegends.com";
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly Dictionary<int, string> _names = new();
    private IReadOnlyDictionary<int, string> _itemNames = new Dictionary<int, string>();
    private IReadOnlySet<int> _completedItems = new HashSet<int>();
    public string Version { get; private set; } = "";

    public DataDragonClient(HttpClient http, string cacheDir) { _http = http; _cacheDir = cacheDir; }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_names.Count > 0) return;
        var versions = JsonSerializer.Deserialize<List<string>>(
            await _http.GetStringAsync($"{Base}/api/versions.json", ct))!;
        Version = versions[0];
        var champJson = await GetChampionJsonAsync(Version, ct);
        using var doc = JsonDocument.Parse(champJson);
        foreach (var c in doc.RootElement.GetProperty("data").EnumerateObject())
            if (int.TryParse(c.Value.GetProperty("key").GetString(), out var id))
                _names[id] = c.Value.GetProperty("name").GetString() ?? c.Name;

        try
        {
            var itemJson = await GetItemJsonAsync(Version, ct);
            var catalog = ItemCatalogParser.Parse(itemJson);
            _itemNames = catalog.Names;
            _completedItems = catalog.CompletedItemIds;
        }
        catch { /* offline / no cache -> item lookups degrade to fallbacks; champion names still work */ }
    }

    private async Task<string> GetChampionJsonAsync(string version, CancellationToken ct)
    {
        var cacheFile = Path.Combine(_cacheDir, version, "champion.json");
        try { if (File.Exists(cacheFile)) return await File.ReadAllTextAsync(cacheFile, ct); }
        catch { /* fall through to network on any cache-read error */ }

        var json = await _http.GetStringAsync($"{Base}/cdn/{version}/data/en_US/champion.json", ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            await File.WriteAllTextAsync(cacheFile, json, ct);
        }
        catch { /* cache write is best-effort; never break load on a disk error */ }
        return json;
    }

    private async Task<string> GetItemJsonAsync(string version, CancellationToken ct)
    {
        var path = Path.Combine(_cacheDir, version, "item.json");
        try { if (File.Exists(path)) return await File.ReadAllTextAsync(path, ct); } catch { }
        var json = await _http.GetStringAsync($"{Base}/cdn/{version}/data/en_US/item.json", ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json, ct);
        }
        catch { }
        return json;
    }

    public string ChampionName(int championId) => _names.TryGetValue(championId, out var n) ? n : $"Champ {championId}";
    public string ChampionIconUrl(string iconBasename) => $"{Base}/cdn/{Version}/img/champion/{iconBasename}.png";

    public string ItemName(int itemId) =>
        _itemNames.TryGetValue(itemId, out var n) ? n : $"Item {itemId}";

    public bool IsCompletedItem(int itemId) => _completedItems.Contains(itemId);

    public IReadOnlySet<int> CompletedItemIds => _completedItems;

    /// <summary>True once item data has loaded (false offline-with-no-cache).</summary>
    public bool HasItemData => _completedItems.Count > 0;
}
