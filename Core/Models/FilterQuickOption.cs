namespace RSSMagnetCatcher.Core.Models;

public sealed record FilterQuickOption(string Label, string Clause)
{
    public static IReadOnlyList<FilterQuickOption> All { get; } =
    [
        new("GBK|CHS|简", "GBK|CHS|简"),
        new("BIG5|CHT|繁", "BIG5|CHT|繁"),
        new("720p", "720p"),
        new("1080p", "1080p"),
        new("1080p+", "1080p|1080i|1440p|2160p|4k|uhd"),
        new("2160p|4K", "2160p|4k|uhd"),
        new("HEVC|H265", "HEVC|H265"),
        new("AVC|H264", "AVC|H264"),
        new("字幕|内嵌|外挂", "字幕|内嵌|外挂")
    ];
}
