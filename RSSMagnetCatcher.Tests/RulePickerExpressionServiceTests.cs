using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class RulePickerExpressionServiceTests
{
    [Fact]
    public void TryBuildExpression_CombinesQuickIncludeAndExcludeClauses()
    {
        var service = new RulePickerExpressionService(new RuleMatchService());

        var succeeded = service.TryBuildExpression(
            ["CHS", "1080p"],
            "HEVC|H265",
            "720p|BIG5",
            out var expression,
            out _);

        Assert.True(succeeded);
        Assert.Equal("(CHS);(1080p);HEVC|H265;!(720p|BIG5)", expression);
    }
}
