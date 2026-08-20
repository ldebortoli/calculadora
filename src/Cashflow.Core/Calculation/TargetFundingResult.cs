namespace Cashflow.Core.Calculation
{
    public sealed class TargetFundingResult
    {
        public decimal RequiredInputAmount { get; set; }
        public decimal SourceDebitAmount { get; set; }
        public RouteResult Route { get; set; } = null!;
    }
}
