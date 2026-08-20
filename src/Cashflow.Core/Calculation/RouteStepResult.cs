using Cashflow.Core.Models;

namespace Cashflow.Core.Calculation
{
    public sealed class RouteStepResult
    {
        public PlatformNode From { get; set; } = null!;
        public PlatformNode To { get; set; } = null!;
        public TransferRoute Route { get; set; } = null!;
        public decimal InputAmount { get; set; }
        public decimal TradeableInputAmount { get; set; }
        public decimal InputRemainder { get; set; }
        public decimal FeeAmount { get; set; }
        public decimal DebitedAmount { get; set; }
        public decimal GrossOutputAmount { get; set; }
        public decimal TradingFeeAmount { get; set; }
        public decimal OutputFeeAmount { get; set; }
        public decimal OutputAmount { get; set; }
    }
}
