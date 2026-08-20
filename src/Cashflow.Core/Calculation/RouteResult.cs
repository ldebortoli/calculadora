using System.Collections.Generic;
using System.Linq;

namespace Cashflow.Core.Calculation
{
    public sealed class RouteResult
    {
        public IReadOnlyList<RouteStepResult> Steps { get; set; } = new List<RouteStepResult>();
        public decimal FinalAmount { get; set; }
        public string DestinationCurrency { get; set; } = string.Empty;
        public decimal SourceBudgetAmount { get; set; }
        public decimal SourceDebitedAmount { get; set; }
        public decimal SourceRemainder => SourceBudgetAmount - SourceDebitedAmount;
        public string PathLabel => string.Join("  →  ", Steps.Select(step => step.From.Name).Concat(new[] { Steps.Last().To.Name }));
        public IReadOnlyCollection<string> RouteIds => Steps.Select(step => step.Route.Id).ToArray();
    }
}
