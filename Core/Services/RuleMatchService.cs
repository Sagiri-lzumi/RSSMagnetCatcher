using System.Text.RegularExpressions;
using RSSMagnetCatcher.Core.Models;

namespace RSSMagnetCatcher.Core.Services;

public sealed class RuleMatchService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public bool TryValidate(string expression, out string error)
    {
        return TryCreateClauses(expression, out _, out error);
    }

    public bool IsMatch(string expression, string? text)
    {
        if (!TryCreateClauses(expression, out var clauses, out var error))
        {
            throw new ArgumentException(error, nameof(expression));
        }

        try
        {
            var searchableText = text ?? string.Empty;
            return clauses.All(clause =>
            {
                var matched = clause.Regex.IsMatch(searchableText);
                return clause.IsExcluded ? !matched : matched;
            });
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public string BuildExpression(FilterRule rule)
    {
        var include = rule.IncludeExpression.Trim();
        var exclude = rule.ExcludeExpression.Trim();

        if (string.IsNullOrWhiteSpace(exclude))
        {
            return include;
        }

        return string.IsNullOrWhiteSpace(include)
            ? $"!({exclude})"
            : $"{include};!({exclude})";
    }

    public string AppendClause(string expression, string clause)
    {
        var wrappedClause = $"({clause.Trim()})";
        return string.IsNullOrWhiteSpace(expression)
            ? wrappedClause
            : $"{expression.Trim()};{wrappedClause}";
    }

    private static bool TryCreateClauses(
        string expression,
        out IReadOnlyList<RuleClause> clauses,
        out string error)
    {
        clauses = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        var parsedClauses = new List<RuleClause>();
        foreach (var rawClause in expression.Split(';'))
        {
            var clause = rawClause.Trim();
            if (string.IsNullOrWhiteSpace(clause))
            {
                error = "条件表达式中存在空白子条件。";
                return false;
            }

            var isExcluded = clause.StartsWith('!');
            if (isExcluded)
            {
                clause = clause[1..].Trim();
            }

            clause = RemoveOuterParentheses(clause);
            if (string.IsNullOrWhiteSpace(clause))
            {
                error = "条件表达式中存在空白子条件。";
                return false;
            }

            try
            {
                parsedClauses.Add(new RuleClause(
                    isExcluded,
                    new Regex(clause, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout)));
            }
            catch (ArgumentException exception)
            {
                error = $"无效正则：{exception.Message}";
                return false;
            }
        }

        clauses = parsedClauses;
        return true;
    }

    private static string RemoveOuterParentheses(string clause)
    {
        while (clause.Length >= 2 && clause[0] == '(' && clause[^1] == ')' && IsWrapped(clause))
        {
            clause = clause[1..^1].Trim();
        }

        return clause;
    }

    private static bool IsWrapped(string clause)
    {
        var depth = 0;
        for (var index = 0; index < clause.Length; index++)
        {
            var character = clause[index];
            if (character == '(' && !IsEscaped(clause, index))
            {
                depth++;
            }
            else if (character == ')' && !IsEscaped(clause, index))
            {
                depth--;
                if (depth == 0 && index < clause.Length - 1)
                {
                    return false;
                }
            }

            if (depth < 0)
            {
                return false;
            }
        }

        return depth == 0;
    }

    private static bool IsEscaped(string value, int index)
    {
        var backslashCount = 0;
        for (var current = index - 1; current >= 0 && value[current] == '\\'; current--)
        {
            backslashCount++;
        }

        return backslashCount % 2 == 1;
    }

    private sealed record RuleClause(bool IsExcluded, Regex Regex);
}
