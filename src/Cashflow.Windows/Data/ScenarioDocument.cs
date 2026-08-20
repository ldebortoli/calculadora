using System.Collections.Generic;
using Cashflow.Core.Models;

namespace Cashflow.Windows.Data
{
    public sealed class ScenarioDocument
    {
        public int Version { get; set; } = StarterScenarioFactory.CurrentDocumentVersion;
        public string? ActiveScenarioId { get; set; }
        public List<CashflowScenario> Scenarios { get; set; } = new List<CashflowScenario>();
        public List<ManualExchangeRateSetting> ManualExchangeRates { get; set; } = new List<ManualExchangeRateSetting>();
        public MusicSessionSettings MusicSession { get; set; } = new MusicSessionSettings();
        public RetirementSettings Retirement { get; set; } = new RetirementSettings();
    }

    public sealed class ManualExchangeRateSetting
    {
        public string Key { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string FromCurrency { get; set; } = string.Empty;
        public string ToCurrency { get; set; } = string.Empty;
        public decimal ExchangeRate { get; set; }
        public System.DateTimeOffset? UpdatedAt { get; set; }
    }
}
