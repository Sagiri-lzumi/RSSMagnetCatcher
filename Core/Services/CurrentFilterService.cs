using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed class CurrentFilterService
{
    private readonly AppSettings _settings;
    private readonly JsonConfigStore _configStore;
    private readonly string _settingsPath;
    private readonly RuleMatchService _ruleMatchService;

    public CurrentFilterService(
        AppSettings settings,
        JsonConfigStore configStore,
        string settingsPath,
        RuleMatchService ruleMatchService,
        IEnumerable<FilterRule> rules)
    {
        _settings = settings;
        _configStore = configStore;
        _settingsPath = settingsPath;
        _ruleMatchService = ruleMatchService;

        var initialExpression = settings.LastValidFilterExpression;
        if (initialExpression is null)
        {
            var defaultRule = rules.FirstOrDefault(rule =>
                    rule.Enabled
                    && string.Equals(rule.Id, settings.DefaultRuleId, StringComparison.Ordinal))
                ?? rules.FirstOrDefault(rule => rule.Enabled);
            initialExpression = defaultRule is null
                ? string.Empty
                : ruleMatchService.BuildExpression(defaultRule);
        }

        CurrentExpression = ruleMatchService.TryValidate(initialExpression, out _)
            ? initialExpression
            : string.Empty;
    }

    public event EventHandler? Changed;

    public string CurrentExpression { get; private set; }

    public bool TryApply(string expression, out string error)
    {
        var normalized = expression.Trim();
        if (!_ruleMatchService.TryValidate(normalized, out error))
        {
            return false;
        }

        if (string.Equals(CurrentExpression, normalized, StringComparison.Ordinal))
        {
            return true;
        }

        CurrentExpression = normalized;
        _settings.LastValidFilterExpression = normalized;
        _configStore.Save(_settingsPath, _settings);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryAppend(string clause, out string error)
    {
        return TryApply(_ruleMatchService.AppendClause(CurrentExpression, clause), out error);
    }
}
