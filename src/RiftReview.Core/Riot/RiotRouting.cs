namespace RiftReview.Core.Riot;

public static class RiotRouting
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["na1"] = "americas", ["br1"] = "americas", ["la1"] = "americas", ["la2"] = "americas", ["oc1"] = "americas",
        ["euw1"] = "europe", ["eun1"] = "europe", ["tr1"] = "europe", ["ru"] = "europe",
        ["kr"] = "asia", ["jp1"] = "asia",
    };

    public static string RegionalFor(string platform) =>
        Map.TryGetValue(platform, out var r) ? r
        : throw new ArgumentException($"Unknown platform '{platform}'", nameof(platform));

    public static string PlatformHost(string platform) => $"https://{platform.ToLowerInvariant()}.api.riotgames.com";
    public static string RegionalHost(string platform) => $"https://{RegionalFor(platform)}.api.riotgames.com";
}
