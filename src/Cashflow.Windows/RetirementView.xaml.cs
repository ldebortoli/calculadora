using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cashflow.Core.Input;
using Cashflow.Windows.Data;

namespace Cashflow.Windows
{
    public partial class RetirementView : UserControl
    {
        private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("es-AR");
        private readonly ScenarioDocument _document;
        private readonly ScenarioStore _store;
        private readonly RetirementCalculator _calculator = new RetirementCalculator();
        private readonly UsInflationService _inflationService = new UsInflationService();
        private readonly List<IncomeEditor> _incomeEditors = new List<IncomeEditor>();
        private readonly List<ReserveEditor> _reserveEditors = new List<ReserveEditor>();
        private bool _initialized;
        private bool _refreshing;

        public RetirementView(ScenarioDocument document, ScenarioStore store)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _document.Retirement.EnsurePlanningCollections();
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
            LoadSettings();
            UpdateInflationStatus();
            RenderProjection();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (TrySaveInputs(false))
            {
                TrySave();
            }
        }

        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveInputs(true))
            {
                return;
            }
            TrySave();
            RenderProjection();
            SaveStatusText.Text = "Guardado localmente · " + DateTime.Now.ToString("HH:mm");
        }

        private void AddIncome_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveInputs(true))
            {
                return;
            }
            var settings = _document.Retirement;
            settings.MonthlyIncomes.Add(new RetirementIncomeSettings
            {
                Name = "Trabajo " + (settings.MonthlyIncomes.Count + 1),
                MonthlyAmountCents = 0
            });
            BuildIncomeEditors();
            TrySave();
            RenderProjection();
        }

        private void RemoveIncome_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is RetirementIncomeSettings income) ||
                _document.Retirement.MonthlyIncomes.Count <= 1 || !TrySaveInputs(true))
            {
                return;
            }
            _document.Retirement.MonthlyIncomes.Remove(income);
            BuildIncomeEditors();
            TrySave();
            RenderProjection();
        }

        private void AddReserve_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveInputs(true))
            {
                return;
            }
            var settings = _document.Retirement;
            var customCount = settings.Reserves.Count(reserve => string.IsNullOrEmpty(reserve.Kind));
            settings.Reserves.Add(new RetirementReserveSettings
            {
                Name = "Nuevo objetivo " + (customCount + 1)
            });
            BuildReserveEditors();
            TrySave();
            RenderProjection();
        }

        private void RemoveReserve_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is RetirementReserveSettings reserve) ||
                !string.IsNullOrEmpty(reserve.Kind) || !TrySaveInputs(true))
            {
                return;
            }
            _document.Retirement.Reserves.Remove(reserve);
            BuildReserveEditors();
            TrySave();
            RenderProjection();
        }

        private async void RefreshInflation_Click(object sender, RoutedEventArgs e)
        {
            if (_refreshing || !TrySaveInputs(true))
            {
                return;
            }

            _refreshing = true;
            RefreshInflationButton.IsEnabled = false;
            InflationStatusText.Text = "Consultando CPI-U en el U.S. Bureau of Labor Statistics…";
            try
            {
                var quote = await _inflationService.GetLatestAsync();
                var settings = _document.Retirement;
                settings.UsInflationPercentage = quote.Percentage;
                settings.InflationPeriod = quote.Period;
                settings.InflationFetchedAt = quote.FetchedAt;
                settings.InflationSource = quote.Source;
                InflationBox.Text = quote.Percentage.ToString("0.##", CultureInfo.CurrentCulture);
                TrySave();
                UpdateInflationStatus();
                RenderProjection();
            }
            catch (Exception exception) when (
                exception is HttpRequestException ||
                exception is TaskCanceledException ||
                exception is JsonException ||
                exception is FormatException ||
                exception is InvalidOperationException)
            {
                InflationStatusText.Text = "No se pudo actualizar el BLS. Se conserva el porcentaje guardado.";
            }
            finally
            {
                _refreshing = false;
                RefreshInflationButton.IsEnabled = true;
            }
        }

        private void LoadSettings()
        {
            var settings = _document.Retirement;
            settings.EnsurePlanningCollections();
            InitialStocksBox.Text = FormatMoneyInput(settings.InitialStocksCents);
            InitialBondsBox.Text = FormatMoneyInput(settings.InitialBondsCents);
            OrdinaryExpensesBox.Text = FormatMoneyInput(settings.OrdinaryMonthlyExpensesCents);
            AnnualVacationExpensesBox.Text = FormatMoneyInput(settings.AnnualVacationExpensesCents);
            UpdateVacationProration(settings.AnnualVacationExpensesCents);
            MusicSessionExpenseBox.Text = FormatMoneyInput(settings.MusicSessionMonthlyExpenseCents);
            ExtraExpensesBox.Text = FormatMoneyInput(settings.ExtraMonthlyExpensesCents);
            ExtraMonthsBox.Text = settings.ExtraExpenseMonths.ToString(CultureInfo.CurrentCulture);
            TargetInvestedBox.Text = FormatMoneyInput(settings.TargetInvestedCents);
            StockTargetBox.Text = FormatMoneyInput(settings.TargetStocksCents ?? 0);
            StockReturnBox.Text = FormatInput(settings.StockAnnualReturnPercentage);
            BondReturnBox.Text = FormatInput(settings.BondAnnualReturnPercentage);
            WithdrawalRateBox.Text = FormatInput(settings.WithdrawalRatePercentage);
            InflationBox.Text = FormatInput(settings.UsInflationPercentage);
            RunwayTargetYearsBox.Text = settings.EmergencyRunwayTargetYears.ToString(CultureInfo.CurrentCulture);
            BuildIncomeEditors();
            BuildReserveEditors();
        }

        private void BuildIncomeEditors()
        {
            IncomeRowsPanel.Children.Clear();
            _incomeEditors.Clear();
            var incomes = _document.Retirement.MonthlyIncomes;
            for (var index = 0; index < incomes.Count; index++)
            {
                var income = incomes[index];
                var card = CreateEditorCard(index == 0 ? new Thickness(0) : new Thickness(0, 7, 0, 0));
                var root = new StackPanel();
                card.Child = root;

                var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 7) };
                if (incomes.Count > 1)
                {
                    var remove = CreateRemoveButton("Quitar", income, RemoveIncome_Click);
                    DockPanel.SetDock(remove, Dock.Right);
                    header.Children.Add(remove);
                }
                header.Children.Add(new TextBlock
                {
                    Text = "INGRESO " + (index + 1),
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 228, 200)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                root.Children.Add(header);

                var nameBox = new TextBox { Text = income.Name };
                var amountBox = new TextBox { Text = FormatMoneyInput(income.MonthlyAmountCents) };
                amountBox.LostFocus += MoneyBox_LostFocus;
                root.Children.Add(CreateFieldPair("Nombre del trabajo", nameBox, "Monto mensual", amountBox));
                _incomeEditors.Add(new IncomeEditor(income, nameBox, amountBox));
                IncomeRowsPanel.Children.Add(card);
            }
        }

        private void BuildReserveEditors()
        {
            ReserveRowsPanel.Children.Clear();
            _reserveEditors.Clear();
            var reserves = _document.Retirement.Reserves;
            for (var index = 0; index < reserves.Count; index++)
            {
                var reserve = reserves[index];
                var card = CreateEditorCard(index == 0 ? new Thickness(0) : new Thickness(0, 8, 0, 0));
                var root = new StackPanel();
                card.Child = root;

                var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 7) };
                if (string.IsNullOrEmpty(reserve.Kind))
                {
                    var remove = CreateRemoveButton("Quitar", reserve, RemoveReserve_Click);
                    DockPanel.SetDock(remove, Dock.Right);
                    header.Children.Add(remove);
                }
                header.Children.Add(new TextBlock
                {
                    Text = "PRIORIDAD " + (index + 1),
                    Foreground = new SolidColorBrush(Color.FromRgb(241, 185, 85)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                root.Children.Add(header);

                var nameBox = new TextBox { Text = reserve.Name, Margin = new Thickness(0, 0, 0, 7) };
                root.Children.Add(CreateField("Nombre de la reserva u objetivo", nameBox));
                var currentBox = new TextBox { Text = FormatMoneyInput(reserve.CurrentCents) };
                var targetBox = new TextBox { Text = FormatMoneyInput(reserve.TargetCents) };
                var startBox = new TextBox { Text = reserve.StartAfterMonths.ToString(CultureInfo.CurrentCulture) };
                var capBox = new TextBox { Text = FormatMoneyInput(reserve.MonthlyCapCents) };
                currentBox.LostFocus += MoneyBox_LostFocus;
                targetBox.LostFocus += MoneyBox_LostFocus;
                capBox.LostFocus += MoneyBox_LostFocus;
                root.Children.Add(CreateFieldPair("Ya tengo", currentBox, "Objetivo", targetBox));
                var timingPair = CreateFieldPair("Empezar en N meses", startBox, "Máximo por mes", capBox);
                timingPair.Margin = new Thickness(0, 7, 0, 0);
                root.Children.Add(timingPair);
                _reserveEditors.Add(new ReserveEditor(reserve, nameBox, currentBox, targetBox, startBox, capBox));
                ReserveRowsPanel.Children.Add(card);
            }
        }

        private Border CreateEditorCard(Thickness margin) => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(21, 34, 54)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 56, 82)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = margin
        };

        private Button CreateRemoveButton(string label, object tag, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Content = label,
                Tag = tag,
                Style = TryFindResource("GhostButton") as Style,
                Padding = new Thickness(7, 2, 7, 2),
                FontSize = 9,
                MinHeight = 0
            };
            button.Click += handler;
            return button;
        }

        private static StackPanel CreateField(string label, FrameworkElement control)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(145, 160, 183)),
                FontSize = 9,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(control);
            return panel;
        }

        private static Grid CreateFieldPair(
            string firstLabel,
            FrameworkElement firstControl,
            string secondLabel,
            FrameworkElement secondControl)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var first = CreateField(firstLabel, firstControl);
            var second = CreateField(secondLabel, secondControl);
            Grid.SetColumn(first, 0);
            Grid.SetColumn(second, 2);
            grid.Children.Add(first);
            grid.Children.Add(second);
            return grid;
        }

        private bool TrySaveInputs(bool showErrors)
        {
            if (!TryReadDynamicInputs(showErrors, out var incomes, out var reserves))
            {
                return false;
            }
            if (!TryMoney(InitialStocksBox.Text, out var initialStocks) ||
                !TryMoney(InitialBondsBox.Text, out var initialBonds) ||
                !TryMoney(OrdinaryExpensesBox.Text, out var ordinary) ||
                !TryMoney(AnnualVacationExpensesBox.Text, out var annualVacation) ||
                !TryMoney(MusicSessionExpenseBox.Text, out var musicSession) ||
                !TryMoney(ExtraExpensesBox.Text, out var extra))
            {
                return ValidationError("Los montos deben ser números mayores o iguales que cero.", showErrors);
            }
            if (!TryPositiveMoney(TargetInvestedBox.Text, out var target))
            {
                return ValidationError("El objetivo invertido debe ser mayor que cero.", showErrors);
            }
            if (!TryMoney(StockTargetBox.Text, out var stockTarget) || stockTarget > target)
            {
                return ValidationError("El objetivo en acciones debe estar entre cero y el objetivo invertido total.", showErrors);
            }
            if (!int.TryParse(ExtraMonthsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var extraMonths) ||
                extraMonths < 0 || extraMonths > RetirementCalculator.MaximumProjectionYears * 12)
            {
                return ValidationError("Los meses de gasto extra deben ser un entero entre 0 y 1200.", showErrors);
            }
            if (!int.TryParse(RunwayTargetYearsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var runwayTargetYears) ||
                runwayTargetYears < 1 || runwayTargetYears > RetirementCalculator.MaximumProjectionYears)
            {
                return ValidationError("El horizonte de autonomía debe ser un entero entre 1 y 100 años.", showErrors);
            }
            if (!TryRange(StockReturnBox.Text, -99.99m, 100m, out var stockReturn) ||
                !TryRange(BondReturnBox.Text, -99.99m, 100m, out var bondReturn))
            {
                return ValidationError("Los retornos anuales deben estar entre -99,99% y 100%.", showErrors);
            }
            if (!TryRange(InflationBox.Text, -99.99m, 100m, out var inflation))
            {
                return ValidationError("La inflación debe estar entre -99,99% y 100%.", showErrors);
            }
            if (!TryRange(WithdrawalRateBox.Text, 0m, 100m, out var withdrawal))
            {
                return ValidationError("La tasa de retiro debe estar entre 0% y 100%.", showErrors);
            }

            foreach (var income in incomes)
            {
                income.Model.Name = income.Name;
                income.Model.MonthlyAmountCents = income.AmountCents;
            }
            foreach (var reserve in reserves)
            {
                reserve.Model.Name = reserve.Name;
                reserve.Model.CurrentCents = reserve.CurrentCents;
                reserve.Model.TargetCents = reserve.TargetCents;
                reserve.Model.StartAfterMonths = reserve.StartAfterMonths;
                reserve.Model.MonthlyCapCents = reserve.MonthlyCapCents;
            }

            var settings = _document.Retirement;
            settings.InitialStocksCents = initialStocks;
            settings.InitialBondsCents = initialBonds;
            settings.MonthlyIncomeCents = 0;
            settings.OrdinaryMonthlyExpensesCents = ordinary;
            settings.AnnualVacationExpensesCents = annualVacation;
            settings.MusicSessionMonthlyExpenseCents = musicSession;
            settings.ExtraMonthlyExpensesCents = extra;
            settings.ExtraExpenseMonths = extraMonths;
            settings.CashFlowReserveCurrentCents = 0;
            settings.CashFlowReserveTargetCents = 0;
            settings.CashFlowReserveStartAfterMonths = 0;
            settings.CashFlowReserveMonthlyCapCents = 0;
            settings.VacationReserveCurrentCents = 0;
            settings.VacationReserveTargetCents = 0;
            settings.VacationReserveStartAfterMonths = 0;
            settings.VacationReserveMonthlyCapCents = 0;
            settings.TargetInvestedCents = target;
            settings.TargetStocksCents = stockTarget;
            settings.StockAllocationPercentage = target > 0
                ? decimal.Round(stockTarget * 100m / target, 4, MidpointRounding.AwayFromZero)
                : 0m;
            settings.StockAnnualReturnPercentage = stockReturn;
            settings.BondAnnualReturnPercentage = bondReturn;
            settings.UsInflationPercentage = inflation;
            settings.WithdrawalRatePercentage = withdrawal;
            settings.EmergencyRunwayTargetYears = runwayTargetYears;
            UpdateVacationProration(annualVacation);
            return true;
        }

        private bool TryReadDynamicInputs(
            bool showErrors,
            out List<IncomeInput> incomes,
            out List<ReserveInput> reserves)
        {
            incomes = new List<IncomeInput>();
            reserves = new List<ReserveInput>();
            foreach (var editor in _incomeEditors)
            {
                var name = editor.NameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return ValidationError("Cada ingreso debe tener un nombre.", showErrors);
                }
                if (!TryMoney(editor.AmountBox.Text, out var amount))
                {
                    return ValidationError("Cada ingreso mensual debe ser un monto mayor o igual que cero.", showErrors);
                }
                incomes.Add(new IncomeInput(editor.Model, name, amount));
            }
            foreach (var editor in _reserveEditors)
            {
                var name = editor.NameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return ValidationError("Cada reserva debe tener un nombre.", showErrors);
                }
                if (!TryMoney(editor.CurrentBox.Text, out var current) ||
                    !TryMoney(editor.TargetBox.Text, out var target) ||
                    !TryMoney(editor.CapBox.Text, out var cap))
                {
                    return ValidationError("Los montos de las reservas deben ser mayores o iguales que cero.", showErrors);
                }
                if (!int.TryParse(editor.StartBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var start) ||
                    start < 0 || start > RetirementCalculator.MaximumProjectionYears * 12)
                {
                    return ValidationError("El inicio de cada reserva debe ser un entero entre 0 y 1200 meses.", showErrors);
                }
                reserves.Add(new ReserveInput(editor.Model, name, current, target, start, cap));
            }
            return true;
        }

        private void RenderProjection()
        {
            RetirementProjection projection;
            try
            {
                projection = _calculator.Calculate(_document.Retirement);
            }
            catch (ArgumentException exception)
            {
                ProjectionStatusText.Text = exception.Message;
                return;
            }

            SurplusNormalText.Text = FormatMoney(projection.MonthlySurplusAfterExtraExpenses);
            SurplusExtraText.Text = "Durante gastos extra: " + FormatMoney(projection.MonthlySurplusDuringExtraExpenses);
            SetSurplusColor(SurplusNormalText, projection.MonthlySurplusAfterExtraExpenses);
            MonthlyWithdrawalText.Text = FormatMoney(projection.MonthlyWithdrawalRealUsd) + " / mes";
            NominalWithdrawalText.Text = projection.ReachedTarget
                ? $"Equivale a {FormatMoney(projection.MonthlyWithdrawalNominalAtTargetUsd)} nominales al llegar"
                : $"{_document.Retirement.WithdrawalRatePercentage:0.##}% anual dividido 12";

            if (projection.ReachedTarget && projection.EstimatedTargetDate.HasValue && projection.MonthsToTarget.HasValue)
            {
                TargetDateText.Text = projection.EstimatedTargetDate.Value.ToString("MMMM yyyy", MoneyCulture);
                TargetDateDetailText.Text = $"{projection.MonthsToTarget.Value} meses · objetivo nominal estimado {FormatMoney(projection.TargetNominalAtGoalUsd)}";
            }
            else
            {
                TargetDateText.Text = "Más de 100 años";
                TargetDateDetailText.Text = "El objetivo no se alcanza con estos supuestos dentro del horizonte calculado.";
            }

            StocksResultText.Text = FormatMoney(projection.FinalStocksRealUsd);
            BondsResultText.Text = FormatMoney(projection.FinalBondsRealUsd);
            AllocationText.Text = "Objetivo: " + FormatMoney(projection.StockTargetRealUsd);
            BondAllocationText.Text = "Reciben aportes después de completar acciones";
            ContributionsText.Text = "Aportes nuevos: " + FormatMoney(projection.TotalNewContributionsUsd);
            GrowthText.Text = "Rendimiento nominal: " + FormatMoney(projection.TotalNominalGrowthUsd);

            var activeGoals = projection.ReserveGoals.Where(goal => goal.TargetUsd > 0d).ToList();
            var completedGoals = activeGoals.Count(goal => goal.ReachedMonth.HasValue);
            ReserveGoalSummaryText.Text = activeGoals.Count == 0
                ? "SIN OBJETIVOS ACTIVOS"
                : $"{completedGoals}/{activeGoals.Count} COMPLETOS";
            ReserveGoalsChart.Height = Math.Max(230d, activeGoals.Count * 58d + 72d);
            ReserveGoalsChart.ShowProjection(projection);
            ReserveStatusText.Text = activeGoals.Count == 0
                ? "Reservas: todavía no hay objetivos con monto configurado."
                : string.Join("   ·   ", activeGoals.Select(BuildReserveStatus)) +
                  $"   ·   Reservado durante la proyección: {FormatMoney(projection.TotalReservedUsd)}";

            RenderRunway(projection.Runway);
            ProjectionStatusText.Text = projection.ReachedTarget
                ? "El gráfico principal termina en el primer mes en que la cartera supera el objetivo en dólares reales."
                : "Se muestran 100 años de proyección. Aumentá el aporte, ajustá el objetivo o revisá los supuestos para alcanzarlo antes.";
            ProjectionChart.ShowProjection(projection);
        }

        private void RenderRunway(RetirementRunway runway)
        {
            if (runway.InitialMonthlyExpenseUsd <= 0d)
            {
                RunwayDurationText.Text = "Sin límite calculable";
                RunwayDetailText.Text = "El gasto ordinario mensual es 0,00 USD; no existe un mes de incumplimiento dentro del modelo.";
            }
            else if (runway.FailureMonth.HasValue && runway.EstimatedFailureDate.HasValue)
            {
                RunwayDurationText.Text = FormatDuration(runway.MonthsCovered);
                RunwayDetailText.Text = $"En {runway.EstimatedFailureDate.Value.ToString("MMMM yyyy", MoneyCulture)} el patrimonio ya no alcanza para cubrir el gasto ordinario completo de ese mes.";
            }
            else
            {
                RunwayDurationText.Text = "Más de 100 años";
                RunwayDetailText.Text = "El patrimonio todavía cubre el gasto ordinario ajustado por inflación al terminar el horizonte de 100 años.";
            }

            var firstInvestment = _document.Retirement.BondAnnualReturnPercentage <= _document.Retirement.StockAnnualReturnPercentage
                ? "bonos antes que acciones"
                : "acciones antes que bonos";
            RunwayAssetsText.Text =
                $"Inicio: {FormatMoney(runway.InitialLiquidReservesUsd)} líquidos + {FormatMoney(runway.InitialInvestedUsd)} invertidos · orden: reservas, {firstInvestment}.";
            if (runway.RequiredMonthlyReductionUsd > 0.005d)
            {
                RunwayAdjustmentText.Text =
                    $"Para cubrir {runway.TargetYears} años, reducí al menos {FormatMoney(runway.RequiredMonthlyReductionUsd)} por mes, hasta {FormatMoney(runway.SustainableMonthlyExpenseUsd)}.";
            }
            else
            {
                var margin = Math.Max(0d, runway.SustainableMonthlyExpenseUsd - runway.InitialMonthlyExpenseUsd);
                RunwayAdjustmentText.Text =
                    $"Tus gastos ya cubren {runway.TargetYears} años. Margen mensual estimado: {FormatMoney(margin)}.";
            }
            RunwayChart.ShowRunway(runway);
        }

        private void MoneyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && TryMoney(textBox.Text, out var cents))
            {
                textBox.Text = FormatMoneyInput(cents);
                if (ReferenceEquals(textBox, AnnualVacationExpensesBox))
                {
                    UpdateVacationProration(cents);
                }
            }
        }

        private void UpdateVacationProration(long annualCents)
        {
            VacationMonthlyProrationText.Text = FormatMoney((double)RetirementSettings.FromCents(annualCents) / 12d);
        }

        private static string BuildReserveStatus(RetirementReserveGoal goal)
        {
            var timing = goal.ReachedMonth.HasValue
                ? goal.ReachedMonth.Value == 0
                    ? "completa hoy"
                    : $"completa en {goal.EstimatedCompletionDate!.Value.ToString("MMM yyyy", MoneyCulture)}"
                : "pendiente después de 100 años";
            return $"{goal.Name}: {FormatMoney(goal.FinalUsd)} / {FormatMoney(goal.TargetUsd)} · {timing}";
        }

        private void UpdateInflationStatus()
        {
            var settings = _document.Retirement;
            var period = settings.InflationPeriod.HasValue
                ? settings.InflationPeriod.Value.ToString("MMMM 'de' yyyy", MoneyCulture)
                : "sin período";
            var fetched = settings.InflationFetchedAt.HasValue
                ? " · consulta " + settings.InflationFetchedAt.Value.ToLocalTime().ToString("dd/MM HH:mm")
                : " · valor inicial incluido";
            InflationStatusText.Text = $"CPI-U interanual: {period}{fetched}. {settings.InflationSource}";
        }

        private void TrySave()
        {
            try
            {
                _store.Save(_document);
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                SaveStatusText.Text = "No se pudieron guardar los cambios locales.";
            }
        }

        private bool ValidationError(string message, bool show)
        {
            if (show)
            {
                MessageBox.Show(message, "Revisá los datos", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return false;
        }

        private static void SetSurplusColor(TextBlock text, double value) =>
            text.Foreground = new SolidColorBrush(value >= 0d
                ? Color.FromRgb(231, 237, 247)
                : Color.FromRgb(255, 154, 168));

        private static bool TryMoney(string text, out long cents)
        {
            cents = 0;
            if (!DecimalInputParser.TryParse(text, out var value) || value < 0m)
            {
                return false;
            }
            try
            {
                cents = RetirementSettings.ToCents(value);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryPositiveMoney(string text, out long cents) =>
            TryMoney(text, out cents) && cents > 0;

        private static bool TryRange(string text, decimal minimum, decimal maximum, out decimal value) =>
            DecimalInputParser.TryParse(text, out value) && value >= minimum && value <= maximum;

        private static string FormatInput(decimal value) =>
            value.ToString("0.##", CultureInfo.CurrentCulture);

        private static string FormatMoneyInput(long cents) =>
            RetirementSettings.FromCents(cents).ToString("#,0.00", MoneyCulture);

        private static string FormatMoney(double value) =>
            value.ToString("N2", MoneyCulture) + " USD";

        private static string FormatDuration(int months)
        {
            if (months <= 0)
            {
                return "Menos de un mes";
            }
            var years = months / 12;
            var remainingMonths = months % 12;
            if (years == 0)
            {
                return months == 1 ? "1 mes" : months + " meses";
            }
            if (remainingMonths == 0)
            {
                return years == 1 ? "1 año" : years + " años";
            }
            return $"{years} {(years == 1 ? "año" : "años")} y {remainingMonths} {(remainingMonths == 1 ? "mes" : "meses")}";
        }

        private sealed class IncomeEditor
        {
            public IncomeEditor(RetirementIncomeSettings model, TextBox nameBox, TextBox amountBox)
            {
                Model = model;
                NameBox = nameBox;
                AmountBox = amountBox;
            }

            public RetirementIncomeSettings Model { get; }
            public TextBox NameBox { get; }
            public TextBox AmountBox { get; }
        }

        private sealed class ReserveEditor
        {
            public ReserveEditor(
                RetirementReserveSettings model,
                TextBox nameBox,
                TextBox currentBox,
                TextBox targetBox,
                TextBox startBox,
                TextBox capBox)
            {
                Model = model;
                NameBox = nameBox;
                CurrentBox = currentBox;
                TargetBox = targetBox;
                StartBox = startBox;
                CapBox = capBox;
            }

            public RetirementReserveSettings Model { get; }
            public TextBox NameBox { get; }
            public TextBox CurrentBox { get; }
            public TextBox TargetBox { get; }
            public TextBox StartBox { get; }
            public TextBox CapBox { get; }
        }

        private sealed class IncomeInput
        {
            public IncomeInput(RetirementIncomeSettings model, string name, long amountCents)
            {
                Model = model;
                Name = name;
                AmountCents = amountCents;
            }

            public RetirementIncomeSettings Model { get; }
            public string Name { get; }
            public long AmountCents { get; }
        }

        private sealed class ReserveInput
        {
            public ReserveInput(
                RetirementReserveSettings model,
                string name,
                long currentCents,
                long targetCents,
                int startAfterMonths,
                long monthlyCapCents)
            {
                Model = model;
                Name = name;
                CurrentCents = currentCents;
                TargetCents = targetCents;
                StartAfterMonths = startAfterMonths;
                MonthlyCapCents = monthlyCapCents;
            }

            public RetirementReserveSettings Model { get; }
            public string Name { get; }
            public long CurrentCents { get; }
            public long TargetCents { get; }
            public int StartAfterMonths { get; }
            public long MonthlyCapCents { get; }
        }
    }
}
