using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Cashflow.Windows.Data
{
    public sealed class RetirementSettings
    {
        public long InitialStocksCents { get; set; }
        public long InitialBondsCents { get; set; }
        public long MonthlyIncomeCents { get; set; }
        public long OrdinaryMonthlyExpensesCents { get; set; }
        public long MusicSessionMonthlyExpenseCents { get; set; }
        public long ExtraMonthlyExpensesCents { get; set; }
        public int ExtraExpenseMonths { get; set; }

        public long CashFlowReserveCurrentCents { get; set; }
        public long CashFlowReserveTargetCents { get; set; }
        public int CashFlowReserveStartAfterMonths { get; set; }
        public long CashFlowReserveMonthlyCapCents { get; set; }
        public long VacationReserveCurrentCents { get; set; }
        public long VacationReserveTargetCents { get; set; }
        public int VacationReserveStartAfterMonths { get; set; }
        public long VacationReserveMonthlyCapCents { get; set; }

        public List<RetirementIncomeSettings> MonthlyIncomes { get; set; } = new List<RetirementIncomeSettings>();
        public List<RetirementReserveSettings> Reserves { get; set; } = new List<RetirementReserveSettings>();

        public long TargetInvestedCents { get; set; } = 50000000;
        public decimal StockAllocationPercentage { get; set; } = 80m;
        public decimal StockAnnualReturnPercentage { get; set; } = 10m;
        public decimal BondAnnualReturnPercentage { get; set; } = 4m;
        public decimal WithdrawalRatePercentage { get; set; } = 3m;

        public decimal UsInflationPercentage { get; set; } = 3.36m;
        public DateTimeOffset? InflationPeriod { get; set; } = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset? InflationFetchedAt { get; set; }
        public string InflationSource { get; set; } = "U.S. Bureau of Labor Statistics · CPI-U All items (CUUR0000SA0)";

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? InitialInvestedUsd { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? MonthlyIncomeUsd { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? OrdinaryMonthlyExpensesUsd { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? ExtraMonthlyExpensesUsd { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? TargetInvestedUsd { get; set; }

        public bool MigrateLegacyMoneyToCents()
        {
            var changed = false;
            if (InitialInvestedUsd.HasValue)
            {
                var total = ToCents(InitialInvestedUsd.Value);
                InitialStocksCents = (long)Math.Round(total * StockAllocationPercentage / 100m, 0, MidpointRounding.AwayFromZero);
                InitialBondsCents = total - InitialStocksCents;
                InitialInvestedUsd = null;
                changed = true;
            }
            if (MonthlyIncomeUsd.HasValue)
            {
                MonthlyIncomeCents = ToCents(MonthlyIncomeUsd.Value);
                MonthlyIncomeUsd = null;
                changed = true;
            }
            if (OrdinaryMonthlyExpensesUsd.HasValue)
            {
                OrdinaryMonthlyExpensesCents = ToCents(OrdinaryMonthlyExpensesUsd.Value);
                OrdinaryMonthlyExpensesUsd = null;
                changed = true;
            }
            if (ExtraMonthlyExpensesUsd.HasValue)
            {
                ExtraMonthlyExpensesCents = ToCents(ExtraMonthlyExpensesUsd.Value);
                ExtraMonthlyExpensesUsd = null;
                changed = true;
            }
            if (TargetInvestedUsd.HasValue)
            {
                TargetInvestedCents = ToCents(TargetInvestedUsd.Value);
                TargetInvestedUsd = null;
                changed = true;
            }
            return changed;
        }

        public bool EnsurePlanningCollections()
        {
            var changed = false;
            if (MonthlyIncomes == null)
            {
                MonthlyIncomes = new List<RetirementIncomeSettings>();
                changed = true;
            }
            if (MonthlyIncomes.Count == 0)
            {
                MonthlyIncomes.Add(new RetirementIncomeSettings
                {
                    Name = "Trabajo 1",
                    MonthlyAmountCents = MonthlyIncomeCents
                });
                changed = true;
            }

            if (Reserves == null)
            {
                Reserves = new List<RetirementReserveSettings>();
                changed = true;
            }
            if (Reserves.Count == 0)
            {
                Reserves.Add(CreateReserve(
                    "cash-flow",
                    "Reserva cash flow",
                    CashFlowReserveCurrentCents,
                    CashFlowReserveTargetCents,
                    CashFlowReserveStartAfterMonths,
                    CashFlowReserveMonthlyCapCents));
                Reserves.Add(CreateReserve("emergency", "Fondo de emergencia líquido", 0, 0, 0, 0));
                Reserves.Add(CreateReserve(
                    "vacation",
                    "Reserva vacaciones",
                    VacationReserveCurrentCents,
                    VacationReserveTargetCents,
                    VacationReserveStartAfterMonths,
                    VacationReserveMonthlyCapCents));
                changed = true;
            }
            else
            {
                changed |= EnsureBuiltInReserve(
                    "cash-flow",
                    "Reserva cash flow",
                    CashFlowReserveCurrentCents,
                    CashFlowReserveTargetCents,
                    CashFlowReserveStartAfterMonths,
                    CashFlowReserveMonthlyCapCents,
                    0);
                var vacationIndex = Reserves.FindIndex(reserve => reserve.Kind == "vacation");
                changed |= EnsureBuiltInReserve(
                    "emergency",
                    "Fondo de emergencia líquido",
                    0,
                    0,
                    0,
                    0,
                    vacationIndex >= 0 ? vacationIndex : Reserves.Count);
                changed |= EnsureBuiltInReserve(
                    "vacation",
                    "Reserva vacaciones",
                    VacationReserveCurrentCents,
                    VacationReserveTargetCents,
                    VacationReserveStartAfterMonths,
                    VacationReserveMonthlyCapCents,
                    Reserves.Count);
            }

            foreach (var income in MonthlyIncomes)
            {
                if (string.IsNullOrWhiteSpace(income.Id))
                {
                    income.Id = Guid.NewGuid().ToString("N");
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(income.Name))
                {
                    income.Name = "Ingreso";
                    changed = true;
                }
            }
            foreach (var reserve in Reserves)
            {
                if (string.IsNullOrWhiteSpace(reserve.Id))
                {
                    reserve.Id = Guid.NewGuid().ToString("N");
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(reserve.Name))
                {
                    reserve.Name = "Reserva";
                    changed = true;
                }
            }

            return changed;
        }

        private bool EnsureBuiltInReserve(
            string kind,
            string name,
            long currentCents,
            long targetCents,
            int startAfterMonths,
            long monthlyCapCents,
            int index)
        {
            if (Reserves.Any(reserve => reserve.Kind == kind))
            {
                return false;
            }
            var reserve = CreateReserve(kind, name, currentCents, targetCents, startAfterMonths, monthlyCapCents);
            Reserves.Insert(Math.Max(0, Math.Min(index, Reserves.Count)), reserve);
            return true;
        }

        private static RetirementReserveSettings CreateReserve(
            string kind,
            string name,
            long currentCents,
            long targetCents,
            int startAfterMonths,
            long monthlyCapCents) =>
            new RetirementReserveSettings
            {
                Kind = kind,
                Name = name,
                CurrentCents = currentCents,
                TargetCents = targetCents,
                StartAfterMonths = startAfterMonths,
                MonthlyCapCents = monthlyCapCents
            };

        public static long ToCents(decimal dollars) =>
            decimal.ToInt64(decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));

        public static decimal FromCents(long cents) => cents / 100m;
    }

    public sealed class RetirementIncomeSettings
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Trabajo";
        public long MonthlyAmountCents { get; set; }
    }

    public sealed class RetirementReserveSettings
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = string.Empty;
        public string Name { get; set; } = "Reserva";
        public long CurrentCents { get; set; }
        public long TargetCents { get; set; }
        public int StartAfterMonths { get; set; }
        public long MonthlyCapCents { get; set; }
    }
}
