using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public sealed class RulePickerExpressionService
{
    private readonly RuleMatchService _ruleMatchService;

    public RulePickerExpressionService(RuleMatchService ruleMatchService)
    {
        _ruleMatchService = ruleMatchService;
    }

    public bool TryBuildExpression(
        IEnumerable<string> selectedClauses,
        string customInclude,
        string customExclude,
        out string expression,
        out string error)
    {
        var includeExpression = BuildIncludeExpression(selectedClauses, customInclude);
        expression = _ruleMatchService.BuildExpression(new FilterRule
        {
            IncludeExpression = includeExpression,
            ExcludeExpression = customExclude.Trim()
        });
        return _ruleMatchService.TryValidate(expression, out error);
    }

    public string BuildIncludeExpression(IEnumerable<string> selectedClauses, string customInclude)
    {
        var expression = string.Empty;
        foreach (var clause in selectedClauses.Where(clause => !string.IsNullOrWhiteSpace(clause)))
        {
            expression = _ruleMatchService.AppendClause(expression, clause);
        }

        var trimmedCustomInclude = customInclude.Trim();
        return string.IsNullOrWhiteSpace(trimmedCustomInclude)
            ? expression
            : string.IsNullOrWhiteSpace(expression)
                ? trimmedCustomInclude
                : $"{expression};{trimmedCustomInclude}";
    }
}
