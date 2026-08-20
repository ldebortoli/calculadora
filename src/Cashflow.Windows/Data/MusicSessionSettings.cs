using System;

namespace Cashflow.Windows.Data
{
    public sealed class MusicSessionSettings
    {
        public decimal TargetUsd { get; set; } = 400m;
        public bool AutoRefreshEnabled { get; set; } = true;
        public int RefreshMinutes { get; set; } = 10;

        public decimal CashUsdPerUsdc { get; set; } = 1m;
        public decimal CashUsdPerUsdt { get; set; } = 1m;
        public decimal PersonFeePercentage { get; set; }
        public decimal? BinanceUsdcTransferFee { get; set; }
        public decimal? BinanceUsdtTransferFee { get; set; }
        public decimal OfficialPurchaseExtraPercentage { get; set; }
        public bool OfficialPurchaseAvailable { get; set; }

        public decimal? BlueBuy { get; set; }
        public decimal? BlueSell { get; set; }
        public DateTimeOffset? BlueUpdatedAt { get; set; }
        public decimal? OfficialBuy { get; set; }
        public decimal? OfficialSell { get; set; }
        public DateTimeOffset? OfficialUpdatedAt { get; set; }
        public DateTimeOffset? InternetFetchedAt { get; set; }
        public string InternetSource { get; set; } = "DolarAPI";
    }
}
