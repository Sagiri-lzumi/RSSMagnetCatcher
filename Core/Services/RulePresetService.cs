using RSSMagnetCatcher.Core.Models;
using RSSMagnetCatcher.Core.Utils;
using RSSMagnetCatcher.Storage;

namespace RSSMagnetCatcher.Core.Services;

public sealed class RulePresetService
{
    private readonly JsonConfigStore _configStore;
    private readonly string _rulesPath;
    private readonly RuleMatchService _ruleMatchService;

    public RulePresetService(
        JsonConfigStore configStore,
        string rulesPath,
        RuleMatchService ruleMatchService)
    {
        _configStore = configStore;
        _rulesPath = rulesPath;
        _ruleMatchService = ruleMatchService;
    }

    public IReadOnlyList<FilterRule> Load()
    {
        return _configStore.Load(_rulesPath, new List<FilterRule>());
    }

    public FilterRule Save(FilterRule rule)
    {
        Validate(rule);
        var rules = Load().ToList();
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            rule.Id = $"rule_{HashHelper.Sha256Hex($"{rule.Name}|{DateTimeOffset.UtcNow:O}")[..12]}";
        }

        var index = rules.FindIndex(existing => string.Equals(existing.Id, rule.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            rules[index] = rule;
        }
        else
        {
            rules.Add(rule);
        }

        _configStore.Save(_rulesPath, rules);
        return rule;
    }

    public bool Delete(string ruleId)
    {
        var rules = Load().ToList();
        var removed = rules.RemoveAll(rule => string.Equals(rule.Id, ruleId, StringComparison.Ordinal)) > 0;
        if (removed)
        {
            _configStore.Save(_rulesPath, rules);
        }

        return removed;
    }

    private void Validate(FilterRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            throw new ArgumentException("规则名称不能为空。", nameof(rule));
        }

        var expression = _ruleMatchService.BuildExpression(rule);
        if (!_ruleMatchService.TryValidate(expression, out var error))
        {
            throw new ArgumentException(error, nameof(rule));
        }
    }
}
