using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Xml;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public sealed class FeedDiagnosticsService
{
    public string ClassifyFailure(Exception exception, string? responseContent = null)
    {
        if (exception is RssFetchException)
        {
            return FeedDiagnosticCategories.HttpError;
        }

        if (exception is NonRssXmlException)
        {
            return FeedDiagnosticCategories.NonXml;
        }

        if (ContainsException<AuthenticationException>(exception))
        {
            return FeedDiagnosticCategories.HttpsFailure;
        }

        if (FindException<SocketException>(exception) is { } socketException
            && socketException.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain)
        {
            return FeedDiagnosticCategories.DnsFailure;
        }

        if (exception is HttpRequestException || ContainsException<SocketException>(exception))
        {
            return FeedDiagnosticCategories.NetworkFailure;
        }

        if (exception is XmlException)
        {
            return LooksLikeXml(responseContent)
                ? FeedDiagnosticCategories.XmlParseError
                : FeedDiagnosticCategories.NonXml;
        }

        return FeedDiagnosticCategories.UnknownFailure;
    }

    public string GetDisplayName(string category)
    {
        return category switch
        {
            FeedDiagnosticCategories.NetworkFailure => "网络失败",
            FeedDiagnosticCategories.DnsFailure => "DNS 失败",
            FeedDiagnosticCategories.HttpsFailure => "HTTPS 失败",
            FeedDiagnosticCategories.HttpError => "HTTP 异常",
            FeedDiagnosticCategories.NonXml => "非 XML",
            FeedDiagnosticCategories.XmlParseError => "XML 解析失败",
            FeedDiagnosticCategories.NoItems => "无条目",
            FeedDiagnosticCategories.NoMagnet => "无 magnet",
            FeedDiagnosticCategories.TorrentOnly => "torrent only",
            FeedDiagnosticCategories.Filtered => "被条件过滤",
            FeedDiagnosticCategories.UnknownFailure => "未知失败",
            _ => string.IsNullOrWhiteSpace(category) ? "正常" : category
        };
    }

    public string BuildFailureReport(
        IEnumerable<FeedConfig> feeds,
        IReadOnlyDictionary<string, FeedState> states)
    {
        var report = new StringBuilder();
        foreach (var feed in feeds)
        {
            if (!states.TryGetValue(feed.Id, out var state)
                || !string.Equals(state.LastStatus, "failed", StringComparison.Ordinal))
            {
                continue;
            }

            if (report.Length > 0)
            {
                report.AppendLine();
            }

            report.AppendLine($"订阅：{feed.Name}");
            report.AppendLine($"地址：{feed.Url}");
            report.AppendLine($"分类：{GetDisplayName(state.LastErrorCategory)}");
            report.AppendLine($"HTTP：{state.HttpStatusCode?.ToString() ?? "-"}");
            report.AppendLine($"连续失败：{state.ConsecutiveFailCount}");
            report.AppendLine($"最后检查：{FormatTime(state.LastCheckedAt)}");
            report.Append($"错误：{state.LastError}");
        }

        return report.ToString();
    }

    private static string FormatTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    }

    private static bool LooksLikeXml(string? content)
    {
        return !string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith('<');
    }

    private static bool ContainsException<T>(Exception exception)
        where T : Exception
    {
        return FindException<T>(exception) is not null;
    }

    private static T? FindException<T>(Exception exception)
        where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
