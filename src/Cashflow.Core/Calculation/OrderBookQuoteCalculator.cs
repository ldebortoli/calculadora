using System;
using System.Collections.Generic;

namespace Cashflow.Core.Calculation
{
    public static class OrderBookQuoteCalculator
    {
        public static decimal RateForSellingBase(
            IReadOnlyList<(decimal Price, decimal Quantity)> bids,
            decimal baseAmount)
        {
            if (bids == null) throw new ArgumentNullException(nameof(bids));
            if (baseAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(baseAmount));

            var remaining = baseAmount;
            var quoteReceived = 0m;
            foreach (var level in bids)
            {
                ValidateLevel(level);
                var quantity = Math.Min(remaining, level.Quantity);
                quoteReceived += quantity * level.Price;
                remaining -= quantity;
                if (remaining == 0m)
                {
                    return quoteReceived / baseAmount;
                }
            }

            throw new InvalidOperationException("El libro no tiene profundidad suficiente para ese monto.");
        }

        public static decimal RateForBuyingBase(
            IReadOnlyList<(decimal Price, decimal Quantity)> asks,
            decimal quoteAmount)
        {
            if (asks == null) throw new ArgumentNullException(nameof(asks));
            if (quoteAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(quoteAmount));

            var remainingQuote = quoteAmount;
            var baseReceived = 0m;
            foreach (var level in asks)
            {
                ValidateLevel(level);
                var fullLevelCost = level.Price * level.Quantity;
                var quoteSpent = Math.Min(remainingQuote, fullLevelCost);
                baseReceived += quoteSpent / level.Price;
                remainingQuote -= quoteSpent;
                if (remainingQuote == 0m)
                {
                    return baseReceived / quoteAmount;
                }
            }

            throw new InvalidOperationException("El libro no tiene profundidad suficiente para ese monto.");
        }

        private static void ValidateLevel((decimal Price, decimal Quantity) level)
        {
            if (level.Price <= 0m || level.Quantity <= 0m)
            {
                throw new ArgumentException("Cada nivel del libro debe tener precio y cantidad positivos.");
            }
        }
    }
}
