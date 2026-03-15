using System.ComponentModel.DataAnnotations;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Configuration options for AI token metering and budget enforcement.
/// </summary>
public sealed class AiMeteringOptions
{
    /// <summary>Per-model token pricing. Key = model name, Value = pricing info.</summary>
    public Dictionary<string, ModelPricing> ModelPricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maximum tokens allowed in a single AI call (0 = no limit). Default: 0.</summary>
    [Range(0, int.MaxValue)]
    public int SingleCallTokenLimit { get; set; }

    /// <summary>Maximum total tokens per budget period (0 = no limit). Default: 0.</summary>
    [Range(0, long.MaxValue)]
    public long PeriodBudgetTokens { get; set; }

    /// <summary>Budget period duration. Default: 1 day.</summary>
    public TimeSpan BudgetPeriod { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>
/// Token pricing for a specific model.
/// </summary>
public sealed class ModelPricing
{
    /// <summary>Cost per 1000 prompt tokens (USD).</summary>
    public decimal PromptPer1K { get; set; }

    /// <summary>Cost per 1000 completion tokens (USD).</summary>
    public decimal CompletionPer1K { get; set; }
}
