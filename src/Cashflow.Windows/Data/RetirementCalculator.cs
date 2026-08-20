using System;
using System.Collections.Generic;
using System.Linq;

namespace Cashflow.Windows.Data
{
    public sealed class RetirementCalculator
    {
        public const int MaximumProjectionYears = 100;

        public RetirementProjection Calculate(RetirementSettings settings)
        {
            settings.EnsurePlanningCollections();
            Validate(settings);

            var monthlyIncome = settings.MonthlyIncomes.Sum(income => ToDollars(income.MonthlyAmountCents));
            var ordinaryExpenses = ToDollars(settings.OrdinaryMonthlyExpensesCents);
            var musicExpense = ToDollars(settings.MusicSessionMonthlyExpenseCents);
            var extraExpense = ToDollars(settings.ExtraMonthlyExpensesCents);
            var reserveGoals = CalculateReserveGoals(settings, monthlyIncome, ordinaryExpenses, musicExpense, extraExpense);

            var allocation = (double)settings.StockAllocationPercentage / 100d;
            var stocks = ToDollars(settings.InitialStocksCents);
            var bonds = ToDollars(settings.InitialBondsCents);
            var initial = stocks + bonds;
            var target = ToDollars(settings.TargetInvestedCents);
            var stockMonthlyRate = MonthlyEquivalent(settings.StockAnnualReturnPercentage);
            var bondMonthlyRate = MonthlyEquivalent(settings.BondAnnualReturnPercentage);
            var inflationMonthlyRate = MonthlyEquivalent(settings.UsInflationPercentage);
            var inflationFactor = 1d;
            var contributions = 0d;
            var reserves = settings.Reserves.Select(ReserveState.FromSettings).ToList();
            var points = new List<RetirementProjectionPoint>();

            AddPoint(points, 0, stocks, bonds, inflationFactor);
            var reachedMonth = stocks + bonds >= target ? 0 : (int?)null;
            var maximumMonths = MaximumProjectionYears * 12;
            for (var month = 1; month <= maximumMonths && !reachedMonth.HasValue; month++)
            {
                stocks *= 1d + stockMonthlyRate;
                bonds *= 1d + bondMonthlyRate;
                inflationFactor *= 1d + inflationMonthlyRate;

                var extra = month <= settings.ExtraExpenseMonths ? extraExpense : 0d;
                var available = Math.Max(0d, monthlyIncome - ordinaryExpenses - musicExpense - extra);
                foreach (var reserve in reserves)
                {
                    var reserved = AllocateReserve(
                        available,
                        reserve.Current,
                        reserve.Target,
                        reserve.MonthlyCap,
                        month,
                        reserve.StartAfterMonths);
                    reserve.Current += reserved;
                    available -= reserved;
                }

                stocks += available * allocation;
                bonds += available * (1d - allocation);
                contributions += available;

                var realTotal = (stocks + bonds) / inflationFactor;
                if (month % 12 == 0 || realTotal >= target)
                {
                    AddPoint(points, month, stocks, bonds, inflationFactor);
                }
                if (realTotal >= target)
                {
                    reachedMonth = month;
                }
            }

            var finalPoint = points[points.Count - 1];
            var reached = reachedMonth.HasValue;
            var monthlyWithdrawalReal = target * (double)settings.WithdrawalRatePercentage / 100d / 12d;
            var targetInflationFactor = reached ? finalPoint.InflationFactor : 1d;

            return new RetirementProjection
            {
                ReachedTarget = reached,
                MonthsToTarget = reachedMonth,
                EstimatedTargetDate = reachedMonth.HasValue ? DateTime.Today.AddMonths(reachedMonth.Value) : (DateTime?)null,
                TotalMonthlyIncomeUsd = monthlyIncome,
                MonthlySurplusDuringExtraExpenses = monthlyIncome - ordinaryExpenses - musicExpense - extraExpense,
                MonthlySurplusAfterExtraExpenses = monthlyIncome - ordinaryExpenses - musicExpense,
                MonthlyWithdrawalRealUsd = monthlyWithdrawalReal,
                MonthlyWithdrawalNominalAtTargetUsd = monthlyWithdrawalReal * targetInflationFactor,
                TargetRealUsd = target,
                TargetNominalAtGoalUsd = target * targetInflationFactor,
                FinalStocksRealUsd = finalPoint.StocksRealUsd,
                FinalBondsRealUsd = finalPoint.BondsRealUsd,
                TotalNewContributionsUsd = contributions,
                TotalNominalGrowthUsd = finalPoint.TotalNominalUsd - initial - contributions,
                TotalReservedUsd = reserveGoals.Sum(goal => Math.Max(0d, goal.FinalUsd - goal.InitialCurrentUsd)),
                ReserveGoals = reserveGoals,
                Runway = CalculateRunway(settings),
                Points = points
            };
        }

        private static IReadOnlyList<RetirementReserveGoal> CalculateReserveGoals(
            RetirementSettings settings,
            double monthlyIncome,
            double ordinaryExpenses,
            double musicExpense,
            double extraExpense)
        {
            var states = settings.Reserves.Select(ReserveState.FromSettings).ToList();
            foreach (var state in states)
            {
                if (state.Target > 0d && state.Current >= state.Target)
                {
                    state.ReachedMonth = 0;
                }
            }

            var maximumMonths = MaximumProjectionYears * 12;
            for (var month = 1; month <= maximumMonths && states.Any(state => state.Target > state.Current); month++)
            {
                var extra = month <= settings.ExtraExpenseMonths ? extraExpense : 0d;
                var available = Math.Max(0d, monthlyIncome - ordinaryExpenses - musicExpense - extra);
                foreach (var state in states)
                {
                    var reserved = AllocateReserve(
                        available,
                        state.Current,
                        state.Target,
                        state.MonthlyCap,
                        month,
                        state.StartAfterMonths);
                    state.Current += reserved;
                    available -= reserved;
                    if (!state.ReachedMonth.HasValue && state.Target > 0d && state.Current >= state.Target)
                    {
                        state.ReachedMonth = month;
                    }
                }
            }

            return states.Select(state => new RetirementReserveGoal
            {
                Name = state.Name,
                InitialCurrentUsd = state.InitialCurrent,
                FinalUsd = state.Current,
                TargetUsd = state.Target,
                StartAfterMonths = state.StartAfterMonths,
                MonthlyCapUsd = state.MonthlyCap,
                ReachedMonth = state.ReachedMonth,
                EstimatedCompletionDate = state.ReachedMonth.HasValue
                    ? DateTime.Today.AddMonths(state.ReachedMonth.Value)
                    : (DateTime?)null
            }).ToList();
        }

        private static RetirementRunway CalculateRunway(RetirementSettings settings)
        {
            var stocks = ToDollars(settings.InitialStocksCents);
            var bonds = ToDollars(settings.InitialBondsCents);
            var liquidReserves = settings.Reserves.Sum(reserve => ToDollars(reserve.CurrentCents));
            var initialLiquidReserves = liquidReserves;
            var ordinaryExpense = ToDollars(settings.OrdinaryMonthlyExpensesCents);
            var stockMonthlyRate = MonthlyEquivalent(settings.StockAnnualReturnPercentage);
            var bondMonthlyRate = MonthlyEquivalent(settings.BondAnnualReturnPercentage);
            var inflationMonthlyRate = MonthlyEquivalent(settings.UsInflationPercentage);
            var inflationFactor = 1d;
            var points = new List<RetirementRunwayPoint>();
            AddRunwayPoint(points, 0, liquidReserves, bonds, stocks, ordinaryExpense);

            int? failureMonth = null;
            int? reservesExhaustedMonth = liquidReserves <= 0d ? 0 : (int?)null;
            int? investmentsUsedMonth = null;
            var maximumMonths = MaximumProjectionYears * 12;
            for (var month = 1; month <= maximumMonths; month++)
            {
                stocks *= 1d + stockMonthlyRate;
                bonds *= 1d + bondMonthlyRate;
                inflationFactor *= 1d + inflationMonthlyRate;
                var monthlyExpense = ordinaryExpense * inflationFactor;
                var available = liquidReserves + stocks + bonds;
                if (available + 0.000001d < monthlyExpense)
                {
                    failureMonth = month;
                    break;
                }

                var remainingExpense = monthlyExpense;
                Spend(ref liquidReserves, ref remainingExpense);
                if (!reservesExhaustedMonth.HasValue && liquidReserves <= 0.000001d)
                {
                    reservesExhaustedMonth = month;
                }
                if (remainingExpense > 0d)
                {
                    investmentsUsedMonth ??= month;
                    if (settings.BondAnnualReturnPercentage <= settings.StockAnnualReturnPercentage)
                    {
                        Spend(ref bonds, ref remainingExpense);
                        Spend(ref stocks, ref remainingExpense);
                    }
                    else
                    {
                        Spend(ref stocks, ref remainingExpense);
                        Spend(ref bonds, ref remainingExpense);
                    }
                }

                AddRunwayPoint(points, month, liquidReserves, bonds, stocks, monthlyExpense);
            }

            return new RetirementRunway
            {
                MonthsCovered = failureMonth.HasValue ? failureMonth.Value - 1 : maximumMonths,
                FailureMonth = failureMonth,
                EstimatedFailureDate = failureMonth.HasValue ? DateTime.Today.AddMonths(failureMonth.Value) : (DateTime?)null,
                ReachesProjectionHorizon = !failureMonth.HasValue,
                InitialLiquidReservesUsd = initialLiquidReserves,
                InitialInvestedUsd = ToDollars(settings.InitialStocksCents + settings.InitialBondsCents),
                InitialMonthlyExpenseUsd = ordinaryExpense,
                ReservesExhaustedMonth = reservesExhaustedMonth,
                InvestmentsUsedMonth = investmentsUsedMonth,
                Points = points
            };
        }

        private static void Validate(RetirementSettings settings)
        {
            if (settings.InitialStocksCents < 0 || settings.InitialBondsCents < 0 ||
                settings.OrdinaryMonthlyExpensesCents < 0 || settings.MusicSessionMonthlyExpenseCents < 0 ||
                settings.ExtraMonthlyExpensesCents < 0 ||
                settings.MonthlyIncomes.Any(income => income.MonthlyAmountCents < 0) ||
                settings.Reserves.Any(reserve => reserve.CurrentCents < 0 || reserve.TargetCents < 0 || reserve.MonthlyCapCents < 0))
            {
                throw new ArgumentException("Los importes no pueden ser negativos.");
            }
            if (settings.TargetInvestedCents <= 0)
            {
                throw new ArgumentException("El objetivo invertido debe ser mayor que cero.");
            }
            if (settings.ExtraExpenseMonths < 0 || settings.ExtraExpenseMonths > MaximumProjectionYears * 12)
            {
                throw new ArgumentException("La duración de gastos extra debe estar entre 0 y 1200 meses.");
            }
            if (settings.Reserves.Any(reserve => reserve.StartAfterMonths < 0 || reserve.StartAfterMonths > MaximumProjectionYears * 12))
            {
                throw new ArgumentException("El inicio de una reserva debe estar entre 0 y 1200 meses.");
            }
            ValidatePercentage(settings.StockAllocationPercentage, 0m, 100m, "La proporción de acciones");
            ValidatePercentage(settings.StockAnnualReturnPercentage, -99.99m, 100m, "El retorno de acciones");
            ValidatePercentage(settings.BondAnnualReturnPercentage, -99.99m, 100m, "El retorno de bonos");
            ValidatePercentage(settings.UsInflationPercentage, -99.99m, 100m, "La inflación");
            ValidatePercentage(settings.WithdrawalRatePercentage, 0m, 100m, "La tasa de retiro");
        }

        private static double AllocateReserve(
            double available,
            double current,
            double target,
            double monthlyCap,
            int month,
            int startAfterMonths)
        {
            if (available <= 0d || target <= current || month <= startAfterMonths)
            {
                return 0d;
            }
            var cap = monthlyCap > 0d ? monthlyCap : available;
            return Math.Min(available, Math.Min(target - current, cap));
        }

        private static void Spend(ref double source, ref double amount)
        {
            var spent = Math.Min(source, amount);
            source -= spent;
            amount -= spent;
        }

        private static double ToDollars(long cents) => cents / 100d;

        private static void AddPoint(
            ICollection<RetirementProjectionPoint> points,
            int month,
            double stocks,
            double bonds,
            double inflationFactor)
        {
            points.Add(new RetirementProjectionPoint
            {
                Month = month,
                Year = month / 12d,
                StocksRealUsd = stocks / inflationFactor,
                BondsRealUsd = bonds / inflationFactor,
                TotalRealUsd = (stocks + bonds) / inflationFactor,
                TotalNominalUsd = stocks + bonds,
                InflationFactor = inflationFactor
            });
        }

        private static void AddRunwayPoint(
            ICollection<RetirementRunwayPoint> points,
            int month,
            double reserves,
            double bonds,
            double stocks,
            double monthlyExpense)
        {
            points.Add(new RetirementRunwayPoint
            {
                Month = month,
                Year = month / 12d,
                LiquidReservesUsd = Math.Max(0d, reserves),
                BondsUsd = Math.Max(0d, bonds),
                StocksUsd = Math.Max(0d, stocks),
                TotalUsd = Math.Max(0d, reserves + bonds + stocks),
                MonthlyExpenseUsd = monthlyExpense
            });
        }

        private static double MonthlyEquivalent(decimal annualPercentage) =>
            Math.Pow(1d + (double)annualPercentage / 100d, 1d / 12d) - 1d;

        private static void ValidatePercentage(decimal value, decimal minimum, decimal maximum, string label)
        {
            if (value < minimum || value > maximum)
            {
                throw new ArgumentException($"{label} debe estar entre {minimum:0.##}% y {maximum:0.##}%.");
            }
        }

        private sealed class ReserveState
        {
            public string Name { get; set; } = string.Empty;
            public double InitialCurrent { get; set; }
            public double Current { get; set; }
            public double Target { get; set; }
            public int StartAfterMonths { get; set; }
            public double MonthlyCap { get; set; }
            public int? ReachedMonth { get; set; }

            public static ReserveState FromSettings(RetirementReserveSettings reserve) => new ReserveState
            {
                Name = reserve.Name,
                InitialCurrent = ToDollars(reserve.CurrentCents),
                Current = ToDollars(reserve.CurrentCents),
                Target = ToDollars(reserve.TargetCents),
                StartAfterMonths = reserve.StartAfterMonths,
                MonthlyCap = ToDollars(reserve.MonthlyCapCents)
            };
        }
    }

    public sealed class RetirementProjection
    {
        public bool ReachedTarget { get; set; }
        public int? MonthsToTarget { get; set; }
        public DateTime? EstimatedTargetDate { get; set; }
        public double TotalMonthlyIncomeUsd { get; set; }
        public double MonthlySurplusDuringExtraExpenses { get; set; }
        public double MonthlySurplusAfterExtraExpenses { get; set; }
        public double MonthlyWithdrawalRealUsd { get; set; }
        public double MonthlyWithdrawalNominalAtTargetUsd { get; set; }
        public double TargetRealUsd { get; set; }
        public double TargetNominalAtGoalUsd { get; set; }
        public double FinalStocksRealUsd { get; set; }
        public double FinalBondsRealUsd { get; set; }
        public double TotalNewContributionsUsd { get; set; }
        public double TotalNominalGrowthUsd { get; set; }
        public double TotalReservedUsd { get; set; }
        public IReadOnlyList<RetirementReserveGoal> ReserveGoals { get; set; } = Array.Empty<RetirementReserveGoal>();
        public RetirementRunway Runway { get; set; } = new RetirementRunway();
        public IReadOnlyList<RetirementProjectionPoint> Points { get; set; } = Array.Empty<RetirementProjectionPoint>();
    }

    public sealed class RetirementProjectionPoint
    {
        public int Month { get; set; }
        public double Year { get; set; }
        public double StocksRealUsd { get; set; }
        public double BondsRealUsd { get; set; }
        public double TotalRealUsd { get; set; }
        public double TotalNominalUsd { get; set; }
        public double InflationFactor { get; set; }
    }

    public sealed class RetirementReserveGoal
    {
        public string Name { get; set; } = string.Empty;
        public double InitialCurrentUsd { get; set; }
        public double FinalUsd { get; set; }
        public double TargetUsd { get; set; }
        public int StartAfterMonths { get; set; }
        public double MonthlyCapUsd { get; set; }
        public int? ReachedMonth { get; set; }
        public DateTime? EstimatedCompletionDate { get; set; }
    }

    public sealed class RetirementRunway
    {
        public int MonthsCovered { get; set; }
        public int? FailureMonth { get; set; }
        public DateTime? EstimatedFailureDate { get; set; }
        public bool ReachesProjectionHorizon { get; set; }
        public double InitialLiquidReservesUsd { get; set; }
        public double InitialInvestedUsd { get; set; }
        public double InitialMonthlyExpenseUsd { get; set; }
        public int? ReservesExhaustedMonth { get; set; }
        public int? InvestmentsUsedMonth { get; set; }
        public IReadOnlyList<RetirementRunwayPoint> Points { get; set; } = Array.Empty<RetirementRunwayPoint>();
    }

    public sealed class RetirementRunwayPoint
    {
        public int Month { get; set; }
        public double Year { get; set; }
        public double LiquidReservesUsd { get; set; }
        public double BondsUsd { get; set; }
        public double StocksUsd { get; set; }
        public double TotalUsd { get; set; }
        public double MonthlyExpenseUsd { get; set; }
    }
}
