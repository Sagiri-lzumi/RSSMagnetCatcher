using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class ClipboardExportServiceTests
{
    [Fact]
    public void BuildClipboardText_UsesCrLfWithoutTrailingBlankLine()
    {
        var service = new ClipboardExportService();
        var text = service.BuildClipboardText(
        [
            CreateItem("second", "magnet:?xt=urn:btih:BBB222", "2026-06-01T10:02:00+08:00"),
            CreateItem("first", "magnet:?xt=urn:btih:AAA111", "2026-06-01T10:01:00+08:00")
        ]);

        Assert.Equal(
            "magnet:?xt=urn:btih:AAA111\r\nmagnet:?xt=urn:btih:BBB222",
            text);
    }

    [Fact]
    public void BuildClipboardMagnets_DeduplicatesByInfoHashAndKeepsOldest()
    {
        var service = new ClipboardExportService();
        var duplicateNewer = CreateItem("same", "magnet:?xt=urn:btih:SAME222&dn=newer", "2026-06-01T10:03:00+08:00");
        duplicateNewer.InfoHash = "SAME";
        var duplicateOlder = CreateItem("same_old", "magnet:?xt=urn:btih:SAME111&dn=older", "2026-06-01T10:01:00+08:00");
        duplicateOlder.InfoHash = "same";

        var magnets = service.BuildClipboardMagnets(
        [
            duplicateNewer,
            CreateItem("unique", "magnet:?xt=urn:btih:UNIQUE", "2026-06-01T10:02:00+08:00"),
            duplicateOlder
        ]);

        Assert.Equal(
        [
            "magnet:?xt=urn:btih:SAME111&dn=older",
            "magnet:?xt=urn:btih:UNIQUE"
        ], magnets);
    }

    [Fact]
    public void SelectUnexported_ReturnsMagnetsRegardlessOfFilterStatus()
    {
        var service = new ClipboardExportService();
        var filtered = CreateItem("filtered", "magnet:?xt=urn:btih:BBB222", "2026-06-01T10:02:00+08:00");
        filtered.MatchStatus = MatchStatuses.Filtered;
        var exported = CreateItem("exported", "magnet:?xt=urn:btih:CCC333", "2026-06-01T10:03:00+08:00");
        exported.IsExported = true;
        var items = service.SelectUnexported(
        [
            CreateItem("ready", "magnet:?xt=urn:btih:AAA111", "2026-06-01T10:01:00+08:00"),
            filtered,
            exported
        ]);

        Assert.Equal(["ready", "filtered"], items.Select(item => item.Id));
    }

    [Fact]
    public void SelectMatching_CanChooseOnlyUnexportedOrIncludePreviouslyExported()
    {
        var service = new ClipboardExportService();
        var exported = CreateItem("exported", "magnet:?xt=urn:btih:BBB222", "2026-06-01T10:02:00+08:00");
        exported.IsExported = true;
        var items = new[]
        {
            CreateItem("ready", "magnet:?xt=urn:btih:AAA111", "2026-06-01T10:01:00+08:00"),
            exported
        };

        Assert.Equal(["ready"], service.SelectMatching(items, _ => true, true).Select(item => item.Id));
        Assert.Equal(["ready", "exported"], service.SelectMatching(items, _ => true, false).Select(item => item.Id));
    }

    private static MagnetItem CreateItem(string id, string magnet, string foundAt)
    {
        return new MagnetItem
        {
            Id = id,
            Magnet = magnet,
            InfoHash = id,
            FoundAt = DateTimeOffset.Parse(foundAt)
        };
    }
}
