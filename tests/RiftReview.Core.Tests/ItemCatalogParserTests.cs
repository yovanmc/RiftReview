using RiftReview.Core.DataDragon;

namespace RiftReview.Core.Tests;

public class ItemCatalogParserTests
{
    // Minimal fixture: one legendary, one boots, one component, one consumable, one trinket,
    // one 6-digit mode-variant. Mirrors the real item.json field shapes.
    private const string Fixture = """
    {
      "type":"item","version":"test",
      "data":{
        "3157":{"name":"Zhonya's Hourglass","gold":{"total":3250,"purchasable":true},
                "from":["1058","2420"],"depth":3,"tags":["Armor","SpellDamage"],
                "maps":{"11":true,"12":true}},
        "3158":{"name":"Ionian Boots of Lucidity","gold":{"total":900,"purchasable":true},
                "into":["3171"],"tags":["Boots","CooldownReduction"],"maps":{"11":true}},
        "1028":{"name":"Ruby Crystal","gold":{"total":400,"purchasable":true},
                "into":["3068","1011"],"tags":["Health"],"maps":{"11":true}},
        "2003":{"name":"Health Potion","gold":{"total":50,"purchasable":true},
                "tags":["Consumable","Lane"],"maps":{"11":true}},
        "3340":{"name":"Stealth Ward","gold":{"total":0,"purchasable":true},
                "tags":["Trinket","Vision"],"maps":{"11":true}},
        "323040":{"name":"Seraph's (Arena)","gold":{"total":2900,"purchasable":true},
                "tags":["Mana"],"maps":{"30":true}}
      }
    }
    """;

    [Fact]
    public void Parse_keeps_only_completed_sr_legendaries()
    {
        var cat = ItemCatalogParser.Parse(Fixture);
        Assert.Contains(3157, cat.CompletedItemIds);          // legendary -> kept
        Assert.DoesNotContain(3158, cat.CompletedItemIds);    // boots -> dropped (tag + into)
        Assert.DoesNotContain(1028, cat.CompletedItemIds);    // component -> dropped (into + price)
        Assert.DoesNotContain(2003, cat.CompletedItemIds);    // consumable -> dropped
        Assert.DoesNotContain(3340, cat.CompletedItemIds);    // trinket -> dropped
        Assert.DoesNotContain(323040, cat.CompletedItemIds);  // 6-digit mode variant -> dropped
    }

    [Fact]
    public void Parse_maps_names_and_is_malformed_tolerant()
    {
        var cat = ItemCatalogParser.Parse(Fixture);
        Assert.Equal("Zhonya's Hourglass", cat.Names[3157]);
        Assert.Empty(ItemCatalogParser.Parse("not json").CompletedItemIds);  // never throws
    }
}
