using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

namespace RiftReview.Core.Tests;

public class BuildExtractorTests
{
    private static readonly IReadOnlySet<int> CompletedIds =
        new HashSet<int> { 6655, 3089, 3157 };

    // Build a timeline with a mix of events across two frames.
    private static TimelineDto BuildTimeline()
    {
        // Frame 0: pid 3 buys completed (6655), then component (1028), then consumable (2003)
        //           pid 7 (enemy) buys completed (3157) — must be ignored
        var frame0 = new FrameDto(
            60000L,
            new Dictionary<string, ParticipantFrameDto>
            {
                ["3"] = new(3, 0, 0, 0),
                ["7"] = new(7, 0, 0, 0),
            },
            new List<EventDto>
            {
                new("ITEM_PURCHASED", 10000L, null, null, ParticipantId: 3, ItemId: 6655),
                new("ITEM_PURCHASED", 20000L, null, null, ParticipantId: 3, ItemId: 1028),  // component
                new("ITEM_PURCHASED", 30000L, null, null, ParticipantId: 3, ItemId: 2003),  // consumable
                new("ITEM_PURCHASED", 40000L, null, null, ParticipantId: 7, ItemId: 3157),  // enemy
            });

        // Frame 1: pid 3 buys completed (3089), then re-buys 6655 (duplicate — must be deduped)
        var frame1 = new FrameDto(
            120000L,
            new Dictionary<string, ParticipantFrameDto>
            {
                ["3"] = new(3, 0, 0, 0),
            },
            new List<EventDto>
            {
                new("ITEM_PURCHASED", 70000L, null, null, ParticipantId: 3, ItemId: 3089),
                new("ITEM_PURCHASED", 80000L, null, null, ParticipantId: 3, ItemId: 6655),  // re-buy, dupe
            });

        return new TimelineDto(
            new TimelineMetadata("TL1", new List<string> { "p1", "p2", "myPuuid", "p4" }),
            new TimelineInfo(60000, new List<FrameDto> { frame0, frame1 }));
    }

    [Fact]
    public void CompletedItemsPurchased_filters_and_dedupes_correctly()
    {
        var tl = BuildTimeline();
        var result = BuildExtractor.CompletedItemsPurchased(tl, myParticipantId: 3, CompletedIds);

        // Only 6655 and 3089 — component/consumable excluded, enemy excluded, dupe excluded
        Assert.Equal(new[] { 6655, 3089 }, result);
    }

    [Fact]
    public void CompletedItemsPurchased_returns_empty_for_wrong_participant()
    {
        var tl = BuildTimeline();
        var result = BuildExtractor.CompletedItemsPurchased(tl, myParticipantId: 99, CompletedIds);
        Assert.Empty(result);
    }

    [Fact]
    public void MyParticipantId_resolves_via_metadata_participants()
    {
        // Metadata.Participants: ["p1","p2","myPuuid","p4"] → "myPuuid" is at index 2 → participantId 3
        var tl = BuildTimeline();
        var pid = BuildExtractor.MyParticipantId(tl, "myPuuid");
        Assert.Equal(3, pid);
    }

    [Fact]
    public void MyParticipantId_metadata_path_index_to_pid_mapping()
    {
        // Explicitly: build a timeline with Metadata.Participants listing myPuuid at index 2 → expect 3
        var tl = new TimelineDto(
            new TimelineMetadata("TL_META", new List<string> { "a", "b", "myPuuid_test", "c" }),
            new TimelineInfo(60000, new List<FrameDto>()));
        var pid = BuildExtractor.MyParticipantId(tl, "myPuuid_test");
        Assert.Equal(3, pid);
    }

    [Fact]
    public void MyParticipantId_falls_back_to_info_participants_when_not_in_metadata()
    {
        // Metadata has PUUIDs that don't include mine; Info.Participants has a mapping
        var tl = new TimelineDto(
            new TimelineMetadata("TL2", new List<string> { "other1", "other2" }),
            new TimelineInfo(60000, new List<FrameDto>(),
                Participants: new List<TimelineParticipantDto>
                {
                    new(5, "fallback_puuid"),
                    new(8, "another_puuid"),
                }));
        var pid = BuildExtractor.MyParticipantId(tl, "fallback_puuid");
        Assert.Equal(5, pid);
    }

    [Fact]
    public void MyParticipantId_returns_null_when_puuid_not_found()
    {
        var tl = BuildTimeline();
        var pid = BuildExtractor.MyParticipantId(tl, "unknown_puuid");
        Assert.Null(pid);
    }

    [Fact]
    public void MyParticipantId_returns_null_for_empty_puuid()
    {
        var tl = BuildTimeline();
        Assert.Null(BuildExtractor.MyParticipantId(tl, ""));
        Assert.Null(BuildExtractor.MyParticipantId(tl, null!));
    }

    [Fact]
    public void CompletedItemsPurchased_returns_empty_for_null_frames()
    {
        var tl = new TimelineDto(
            new TimelineMetadata("TL3", new List<string>()),
            new TimelineInfo(60000, null!));
        var result = BuildExtractor.CompletedItemsPurchased(tl, 1, CompletedIds);
        Assert.Empty(result);
    }
}
