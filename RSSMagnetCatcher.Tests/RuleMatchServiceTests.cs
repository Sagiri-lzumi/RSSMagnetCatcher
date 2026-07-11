using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Services;

namespace RSSMagnetCatcher.Tests;

public sealed class RuleMatchServiceTests
{
    private readonly RuleMatchService _service = new();

    [Theory]
    [InlineData("(GBK|CHS|简);(1080p);!(720p)", "Example CHS 1080p", true)]
    [InlineData("(GBK|CHS|简);(1080p);!(720p)", "Example GBK 1080p 720p", false)]
    [InlineData("(GBK|CHS|简);(1080p)", "Example BIG5 1080p", false)]
    [InlineData("", "Anything", true)]
    public void IsMatch_AppliesAndOrAndNot(string expression, string text, bool expected)
    {
        Assert.Equal(expected, _service.IsMatch(expression, text));
    }

    [Fact]
    public void TryValidate_RejectsInvalidRegex()
    {
        Assert.False(_service.TryValidate("(GBK|CHS", out var error));
        Assert.Contains("无效正则", error);
    }

    [Fact]
    public void BuildExpression_CombinesIncludeAndExclude()
    {
        var expression = _service.BuildExpression(new FilterRule
        {
            IncludeExpression = "(GBK|CHS|简);(1080p)",
            ExcludeExpression = "720p|BIG5"
        });

        Assert.Equal("(GBK|CHS|简);(1080p);!(720p|BIG5)", expression);
    }
}
