using System.Net;
using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed record TorrentExportItemResult(
    MagnetItem Item,
    bool Succeeded,
    bool Skipped,
    string FilePath,
    string Message);

public sealed record TorrentExportResult(
    string ExportDirectory,
    IReadOnlyList<TorrentExportItemResult> Results)
{
    public int SuccessCount => Results.Count(item => item.Succeeded);

    public int FailureCount => Results.Count(item => !item.Succeeded && !item.Skipped);

    public int SkippedCount => Results.Count(item => item.Skipped);

    public IReadOnlyList<MagnetItem> SuccessfulItems => Results
        .Where(item => item.Succeeded)
        .Select(item => item.Item)
        .ToList();

    public IReadOnlyList<MagnetItem> UnsuccessfulItems => Results
        .Where(item => !item.Succeeded)
        .Select(item => item.Item)
        .ToList();
}

public sealed class TorrentExportService
{
    private readonly DataPaths _paths;
    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _now;

    public TorrentExportService(
        DataPaths paths,
        HttpClient httpClient,
        Func<DateTimeOffset>? now = null)
    {
        _paths = paths;
        _httpClient = httpClient;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<TorrentExportResult> ExportAsync(
        IEnumerable<MagnetItem> sourceItems,
        CancellationToken cancellationToken = default)
    {
        var items = sourceItems
            .Where(item => !string.Equals(item.ProcessingStatus, ProcessingStatuses.Deleted, StringComparison.Ordinal))
            .DistinctBy(item => item.Id)
            .ToList();
        if (items.Count == 0)
        {
            return new TorrentExportResult(string.Empty, []);
        }

        var downloadable = items
            .Where(item => !string.IsNullOrWhiteSpace(item.TorrentUrl))
            .ToList();
        var skipped = items
            .Where(item => string.IsNullOrWhiteSpace(item.TorrentUrl))
            .Select(item => new TorrentExportItemResult(item, false, true, string.Empty, "条目没有 torrent URL。"))
            .ToList();

        if (downloadable.Count == 0)
        {
            return new TorrentExportResult(string.Empty, skipped);
        }

        var exportDirectory = CreateExportDirectory();
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<TorrentExportItemResult>(skipped);
        foreach (var item in downloadable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ExportOneAsync(item, exportDirectory, usedFileNames, cancellationToken));
        }

        if (results.All(item => !item.Succeeded)
            && Directory.Exists(exportDirectory)
            && !Directory.EnumerateFileSystemEntries(exportDirectory).Any())
        {
            Directory.Delete(exportDirectory);
            exportDirectory = string.Empty;
        }

        return new TorrentExportResult(exportDirectory, results);
    }

    private string CreateExportDirectory()
    {
        Directory.CreateDirectory(_paths.TorrentExportDirectory);
        var stamp = _now().ToLocalTime().ToString("yyyyMMddHHmmss");
        var directory = Path.Combine(_paths.TorrentExportDirectory, stamp);
        var suffix = 2;
        while (Directory.Exists(directory))
        {
            directory = Path.Combine(_paths.TorrentExportDirectory, $"{stamp}_{suffix}");
            suffix++;
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private async Task<TorrentExportItemResult> ExportOneAsync(
        MagnetItem item,
        string exportDirectory,
        ISet<string> usedFileNames,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(item.TorrentUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                return new TorrentExportItemResult(item, false, false, string.Empty, "torrent URL 不是有效的 http/https 地址。");
            }

            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TorrentExportItemResult(
                    item,
                    false,
                    false,
                    string.Empty,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                return new TorrentExportItemResult(item, false, false, string.Empty, "下载到的 torrent 文件为空。");
            }

            var fileName = MakeUniqueFileName(BuildSafeFileName(item, uri), usedFileNames, exportDirectory);
            var filePath = Path.Combine(exportDirectory, fileName);
            var tempPath = filePath + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
                File.Move(tempPath, filePath, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            return new TorrentExportItemResult(item, true, false, filePath, "已保存。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TorrentExportItemResult(item, false, false, string.Empty, exception.Message);
        }
    }

    private static string BuildSafeFileName(MagnetItem item, Uri uri)
    {
        var rawName = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(rawName)
            || !rawName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            var title = string.IsNullOrWhiteSpace(item.Title) ? item.Id : item.Title;
            var suffix = string.IsNullOrWhiteSpace(item.InfoHash) ? item.Id : item.InfoHash;
            rawName = $"{title}_{suffix}.torrent";
        }

        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(rawName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray())
            .Trim()
            .Trim('.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = $"{item.Id}.torrent";
        }

        if (!sanitized.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            sanitized += ".torrent";
        }

        const int maxFileNameLength = 160;
        if (sanitized.Length <= maxFileNameLength)
        {
            return sanitized;
        }

        var extension = ".torrent";
        return sanitized[..(maxFileNameLength - extension.Length)] + extension;
    }

    private static string MakeUniqueFileName(
        string fileName,
        ISet<string> usedFileNames,
        string exportDirectory)
    {
        var unique = fileName;
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 2;
        while (!usedFileNames.Add(unique) || File.Exists(Path.Combine(exportDirectory, unique)))
        {
            unique = $"{baseName}_{suffix}{extension}";
            suffix++;
        }

        return unique;
    }
}
