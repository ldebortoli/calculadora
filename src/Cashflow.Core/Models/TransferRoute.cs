using System;

namespace Cashflow.Core.Models
{
    public sealed class TransferRoute
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FromNodeId { get; set; } = string.Empty;
        public string ToNodeId { get; set; } = string.Empty;
        public string Label { get; set; } = "Transferencia";
        public decimal PercentageFee { get; set; }
        public decimal? PercentageFeeMinimum { get; set; }
        public decimal? PercentageFeeMaximum { get; set; }
        public decimal FixedFee { get; set; }
        public FeeApplicationMode FeeApplication { get; set; }
        public decimal TradingFeePercentage { get; set; }
        public decimal OutputPercentageFee { get; set; }
        public decimal? InputAmountStep { get; set; }
        public decimal? MinimumInputAmount { get; set; }
        public decimal? MaximumInputAmount { get; set; }
        public decimal? MinimumOutputAmount { get; set; }
        public decimal ExchangeRate { get; set; } = 1m;
        public bool ExchangeRateConfigured { get; set; } = true;
        public bool ExchangeRateIsManual { get; set; }
        public string? ManualExchangeRateKey { get; set; }
        public DateTimeOffset? ManualExchangeRateUpdatedAt { get; set; }
        public string? LiveQuoteKey { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
