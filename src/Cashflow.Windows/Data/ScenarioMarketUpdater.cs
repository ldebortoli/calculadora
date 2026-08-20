using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cashflow.Core.Calculation;
using Cashflow.Core.Models;

namespace Cashflow.Windows.Data
{
    public sealed class ScenarioMarketUpdater
    {
        private readonly BinanceSpotQuoteService _binance = new BinanceSpotQuoteService();

        public async Task<ScenarioMarketUpdate> UpdateBinanceAsync(
            IEnumerable<CashflowScenario> scenarios,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
            var scenarioList = scenarios.ToArray();
            var sampleRoutes = scenarioList.SelectMany(scenario => scenario.Routes).ToArray();
            var usdcSellRoute = sampleRoutes.FirstOrDefault(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdcForUsdt);
            var usdtArsRoute = sampleRoutes.FirstOrDefault(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs);
            var usdcSellAmount = AmountAvailableForTrade(usdcSellRoute, amount);
            var usdtArsAmount = AmountAvailableForTrade(usdtArsRoute, amount);

            var quotes = await _binance.GetQuotesAsync(usdcSellAmount, usdtArsAmount, cancellationToken);
            var updated = 0;
            foreach (var route in sampleRoutes)
            {
                if (route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdcForUsdt)
                {
                    route.ExchangeRate = quotes.UsdtPerUsdc;
                    route.ExchangeRateConfigured = true;
                    route.InputAmountStep = quotes.UsdcUsdtRules.BaseQuantityStep;
                    route.MinimumInputAmount = quotes.UsdcUsdtRules.MinimumBaseQuantity;
                    route.MinimumOutputAmount = quotes.UsdcUsdtRules.MinimumNotional;
                    updated++;
                }
                else if (route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs)
                {
                    route.ExchangeRate = quotes.ArsPerUsdt;
                    route.ExchangeRateConfigured = true;
                    route.InputAmountStep = quotes.UsdtArsRules.BaseQuantityStep;
                    route.MinimumInputAmount = quotes.UsdtArsRules.MinimumBaseQuantity;
                    route.MinimumOutputAmount = quotes.UsdtArsRules.MinimumNotional;
                    updated++;
                }
            }

            return new ScenarioMarketUpdate
            {
                UpdatedRoutes = updated,
                RetrievedAt = quotes.RetrievedAt
            };
        }

        private static decimal AmountAvailableForTrade(TransferRoute? route, decimal amount)
        {
            if (route == null)
            {
                return amount;
            }

            var tradeableAmount = RouteCalculator.CalculateTransferAmountWithinBudget(route, amount);
            var fee = RouteCalculator.CalculateInputFee(route, tradeableAmount);
            var result = route.FeeApplication == FeeApplicationMode.DeductFromAmount
                ? tradeableAmount - fee
                : tradeableAmount;
            if (result <= 0m)
            {
                throw new InvalidOperationException("El monto no alcanza para una de las operaciones Binance configuradas.");
            }

            return result;
        }
    }

    public sealed class ScenarioMarketUpdate
    {
        public int UpdatedRoutes { get; set; }
        public DateTimeOffset RetrievedAt { get; set; }
    }
}
