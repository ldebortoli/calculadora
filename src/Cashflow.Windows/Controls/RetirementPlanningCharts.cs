using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Controls
{
    public abstract class InteractiveRetirementChart : FrameworkElement
    {
        private double _zoom = 1d;
        private Vector _panOffset;
        private bool _isPanning;
        private Point _panStart;
        private Vector _panOrigin;

        protected abstract bool HasChartData { get; }

        protected double ChartZoom => _zoom;

        protected MatrixTransform ChartTransform =>
            new MatrixTransform(_zoom, 0d, 0d, _zoom, _panOffset.X, _panOffset.Y);

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!HasChartData || e.Delta == 0)
            {
                return;
            }
            if (e.Delta < 0 && _zoom <= 1d)
            {
                return;
            }
            const double zoomStep = 1.12d;
            var cursor = e.GetPosition(this);
            var contentPoint = new Point((cursor.X - _panOffset.X) / _zoom, (cursor.Y - _panOffset.Y) / _zoom);
            var factor = e.Delta > 0 ? zoomStep : 1d / zoomStep;
            _zoom = Math.Max(1d, Math.Min(2.25d, _zoom * factor));
            _panOffset = cursor - new Point(contentPoint.X * _zoom, contentPoint.Y * _zoom);
            CoercePan();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (!HasChartData || _zoom <= 1d)
            {
                return;
            }
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _panOrigin = _panOffset;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isPanning || e.RightButton != MouseButtonState.Pressed)
            {
                return;
            }
            _panOffset = _panOrigin + (e.GetPosition(this) - _panStart);
            CoercePan();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (!_isPanning)
            {
                return;
            }
            _isPanning = false;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _isPanning = false;
            Cursor = Cursors.Arrow;
        }

        protected void DrawCenteredText(DrawingContext context, string text, double size, Color color)
        {
            var measured = MeasureText(text, size, FontWeights.SemiBold);
            DrawText(context, text, size, FontWeights.SemiBold, color, new Point(
                Math.Max(12d, (ActualWidth - measured.Width) / 2d),
                Math.Max(12d, (ActualHeight - measured.Height) / 2d)));
        }

        protected FormattedText MeasureText(string text, double size, FontWeight weight) =>
            new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                size,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

        protected void DrawText(
            DrawingContext context,
            string text,
            double size,
            FontWeight weight,
            Color color,
            Point origin)
        {
            var formatted = MeasureText(text, size, weight);
            formatted.SetForegroundBrush(new SolidColorBrush(color));
            context.DrawText(formatted, origin);
        }

        protected static string CompactMoney(double amount)
        {
            if (amount >= 1000000d) return $"${amount / 1000000d:0.#}M";
            if (amount >= 1000d) return $"${amount / 1000d:0.#}k";
            return $"${amount:0}";
        }

        protected Point ToChartPoint(Point point) =>
            new Point((point.X - _panOffset.X) / _zoom, (point.Y - _panOffset.Y) / _zoom);

        private void CoercePan()
        {
            if (_zoom <= 1d)
            {
                _zoom = 1d;
                _panOffset = default;
                return;
            }
            _panOffset.X = Math.Max(ActualWidth * (1d - _zoom), Math.Min(0d, _panOffset.X));
            _panOffset.Y = Math.Max(ActualHeight * (1d - _zoom), Math.Min(0d, _panOffset.Y));
        }
    }

    public sealed class RetirementReserveTimelineChart : InteractiveRetirementChart
    {
        private IReadOnlyList<RetirementReserveGoal> _goals = Array.Empty<RetirementReserveGoal>();
        private RetirementReserveGoal? _selectedGoal;

        protected override bool HasChartData => _goals.Count > 0;

        public void ShowProjection(RetirementProjection projection)
        {
            _goals = projection.ReserveGoals.Where(goal => goal.TargetUsd > 0d).ToList();
            _selectedGoal = null;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!HasChartData || ActualWidth < 420d || ActualHeight < 170d)
            {
                return;
            }
            var plot = new Rect(190d, 48d, Math.Max(1d, ActualWidth - 220d), Math.Max(1d, ActualHeight - 76d));
            var cursor = ToChartPoint(e.GetPosition(this));
            if (cursor.Y < plot.Top || cursor.Y > plot.Bottom)
            {
                return;
            }
            var row = Math.Max(0, Math.Min(_goals.Count - 1, (int)((cursor.Y - plot.Top) / (plot.Height / _goals.Count))));
            _selectedGoal = _goals[row];
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 38)), null, new Rect(RenderSize));
            if (!HasChartData || ActualWidth < 420d || ActualHeight < 170d)
            {
                DrawCenteredText(context, "Agregá objetivos de reserva para ver su calendario", 12, Color.FromRgb(148, 163, 184));
                return;
            }

            context.PushTransform(ChartTransform);
            var plot = new Rect(190d, 48d, Math.Max(1d, ActualWidth - 220d), Math.Max(1d, ActualHeight - 76d));
            var unresolved = _goals.Any(goal => !goal.ReachedMonth.HasValue);
            var maximumReached = _goals.Where(goal => goal.ReachedMonth.HasValue)
                .Select(goal => goal.ReachedMonth!.Value)
                .DefaultIfEmpty(0)
                .Max();
            var maximumStart = _goals.Select(goal => goal.StartAfterMonths).DefaultIfEmpty(0).Max();
            var maximumMonth = unresolved
                ? RetirementCalculator.MaximumProjectionYears * 12
                : Math.Max(12, Math.Max(maximumReached, maximumStart));

            DrawAxis(context, plot, maximumMonth);
            var rowHeight = plot.Height / _goals.Count;
            var colors = new[]
            {
                Color.FromRgb(24, 191, 162),
                Color.FromRgb(91, 141, 239),
                Color.FromRgb(241, 185, 85),
                Color.FromRgb(238, 113, 124),
                Color.FromRgb(126, 203, 238)
            };

            for (var index = 0; index < _goals.Count; index++)
            {
                var goal = _goals[index];
                var y = plot.Top + rowHeight * index + rowHeight / 2d;
                var color = colors[index % colors.Length];
                DrawGoal(context, goal, plot, y, maximumMonth, color);
            }
            context.Pop();
            DrawSelectionCard(context);
        }

        private void DrawSelectionCard(DrawingContext context)
        {
            if (_selectedGoal == null)
            {
                return;
            }
            var width = Math.Min(320d, Math.Max(230d, ActualWidth - 24d));
            var x = Math.Max(12d, ActualWidth - width - 12d);
            context.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(244, 19, 31, 49)),
                new Pen(new SolidColorBrush(Color.FromRgb(54, 73, 102)), 1d),
                new Rect(x, 8d, width, 60d),
                9d,
                9d);
            DrawText(context, _selectedGoal.Name, 10, FontWeights.Bold, Color.FromRgb(100, 228, 200), new Point(x + 11d, 16d));
            DrawText(context, $"Actual {ExactMoney(_selectedGoal.InitialCurrentUsd)} · Objetivo {ExactMoney(_selectedGoal.TargetUsd)}", 9, FontWeights.Normal, Color.FromRgb(222, 230, 241), new Point(x + 11d, 33d));
            var timing = _selectedGoal.EstimatedCompletionDate.HasValue
                ? "Cumplimiento: " + _selectedGoal.EstimatedCompletionDate.Value.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-AR"))
                : "No se completa dentro de 100 años";
            DrawText(context, timing, 8, FontWeights.Normal, Color.FromRgb(174, 188, 209), new Point(x + 11d, 49d));
        }

        private static string ExactMoney(double amount) =>
            amount.ToString("N2", CultureInfo.GetCultureInfo("es-AR")) + " USD";

        private void DrawAxis(DrawingContext context, Rect plot, int maximumMonth)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(39, 53, 77)), 1d);
            for (var index = 0; index <= 4; index++)
            {
                var month = maximumMonth * index / 4;
                var x = plot.Left + plot.Width * index / 4d;
                context.DrawLine(gridPen, new Point(x, plot.Top - 12d), new Point(x, plot.Bottom));
                var label = month == 0
                    ? "HOY"
                    : DateTime.Today.AddMonths(month).ToString("MMM yy", CultureInfo.GetCultureInfo("es-AR")).ToUpperInvariant();
                var measured = MeasureText(label, 8, FontWeights.Bold);
                DrawText(context, label, 8, FontWeights.Bold, Color.FromRgb(128, 145, 170), new Point(x - measured.Width / 2d, 17d));
            }
        }

        private void DrawGoal(
            DrawingContext context,
            RetirementReserveGoal goal,
            Rect plot,
            double y,
            int maximumMonth,
            Color color)
        {
            var name = goal.Name.Length > 27 ? goal.Name.Substring(0, 26) + "…" : goal.Name;
            DrawText(context, name, 10, FontWeights.SemiBold, Color.FromRgb(222, 230, 241), new Point(14d, y - 16d));
            DrawText(
                context,
                $"{CompactMoney(goal.InitialCurrentUsd)} / {CompactMoney(goal.TargetUsd)}",
                8,
                FontWeights.Normal,
                Color.FromRgb(139, 155, 179),
                new Point(14d, y + 1d));

            context.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(29, 42, 62)),
                null,
                new Rect(plot.Left, y - 5d, plot.Width, 10d),
                5d,
                5d);

            var startX = plot.Left + Math.Min(maximumMonth, goal.StartAfterMonths) / (double)maximumMonth * plot.Width;
            var endMonth = goal.ReachedMonth ?? maximumMonth;
            var endX = plot.Left + Math.Min(maximumMonth, endMonth) / (double)maximumMonth * plot.Width;
            var width = Math.Max(4d, endX - startX);
            var brush = new LinearGradientBrush(Color.FromArgb(210, color.R, color.G, color.B), Color.FromArgb(90, color.R, color.G, color.B), 0d);
            context.DrawRoundedRectangle(brush, null, new Rect(startX, y - 5d, width, 10d), 5d, 5d);

            if (goal.ReachedMonth.HasValue)
            {
                context.DrawEllipse(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.FromRgb(15, 23, 38)), 2d), new Point(endX, y), 5d, 5d);
                var timing = goal.ReachedMonth.Value == 0
                    ? "COMPLETA HOY"
                    : goal.EstimatedCompletionDate!.Value.ToString("MMM yyyy", CultureInfo.GetCultureInfo("es-AR")).ToUpperInvariant();
                var measured = MeasureText(timing, 8, FontWeights.Bold);
                var labelX = Math.Max(plot.Left, Math.Min(plot.Right - measured.Width, endX - measured.Width / 2d));
                DrawText(context, timing, 8, FontWeights.Bold, color, new Point(labelX, y + 10d));
            }
            else
            {
                const string pending = "NO SE COMPLETA EN 100 AÑOS";
                var measured = MeasureText(pending, 8, FontWeights.Bold);
                DrawText(context, pending, 8, FontWeights.Bold, Color.FromRgb(238, 113, 124), new Point(plot.Right - measured.Width, y + 10d));
            }
        }
    }

    public sealed class RetirementRunwayChart : InteractiveRetirementChart
    {
        private RetirementRunway? _runway;
        private RetirementRunwayPoint? _selectedPoint;

        protected override bool HasChartData => _runway != null && _runway.Points.Count > 1;

        public void ShowRunway(RetirementRunway runway)
        {
            _runway = runway;
            _selectedPoint = null;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!HasChartData || _runway == null || ActualWidth < 320d || ActualHeight < 180d)
            {
                return;
            }
            var plot = new Rect(66d, 26d, Math.Max(1d, ActualWidth - 92d), Math.Max(1d, ActualHeight - 72d));
            var maximumYear = Math.Max(_runway.TargetYears, _runway.Points[_runway.Points.Count - 1].Year);
            var cursor = ToChartPoint(e.GetPosition(this));
            if (!plot.Contains(cursor))
            {
                return;
            }
            var selectedYear = (cursor.X - plot.Left) / plot.Width * maximumYear;
            _selectedPoint = _runway.Points.OrderBy(point => Math.Abs(point.Year - selectedYear)).First();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 38)), null, new Rect(RenderSize));
            if (!HasChartData || ActualWidth < 320d || ActualHeight < 180d)
            {
                DrawCenteredText(context, "Ingresá capital, reservas y gastos ordinarios para calcular tu autonomía", 12, Color.FromRgb(148, 163, 184));
                return;
            }

            var runway = _runway!;
            var plot = new Rect(66d, 26d, Math.Max(1d, ActualWidth - 92d), Math.Max(1d, ActualHeight - 72d));
            var maximumYear = Math.Max(runway.TargetYears, runway.Points[runway.Points.Count - 1].Year);
            var maximumValue = Math.Max(1d, runway.Points.Max(point => point.TotalUsd) * 1.08d);

            context.PushTransform(ChartTransform);
            DrawGrid(context, plot, maximumYear, maximumValue);
            DrawTargetHorizon(context, runway, plot, maximumYear);
            DrawArea(context, runway.Points, plot, maximumYear, maximumValue);
            DrawSeries(context, runway.Points, plot, maximumYear, maximumValue, point => point.StocksUsd, Color.FromRgb(91, 141, 239), 1.9d);
            DrawSeries(context, runway.Points, plot, maximumYear, maximumValue, point => point.BondsUsd, Color.FromRgb(241, 185, 85), 1.8d);
            DrawSeries(context, runway.Points, plot, maximumYear, maximumValue, point => point.LiquidReservesUsd, Color.FromRgb(126, 203, 238), 2d);
            DrawSeries(context, runway.Points, plot, maximumYear, maximumValue, point => point.TotalUsd, Color.FromRgb(238, 113, 124), 3d);
            DrawSampleMarkers(context, runway.Points, plot, maximumYear, maximumValue);
            DrawSelection(context, plot, maximumYear, maximumValue);
            DrawLegend(context, plot);
            context.Pop();
            DrawSelectionCard(context);
        }

        private void DrawTargetHorizon(DrawingContext context, RetirementRunway runway, Rect plot, double maximumYear)
        {
            var x = plot.Left + runway.TargetYears / maximumYear * plot.Width;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 228, 200)), 1.4d) { DashStyle = DashStyles.Dash };
            context.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = runway.TargetYears + " AÑOS";
            var measured = MeasureText(label, 8, FontWeights.Bold);
            DrawText(context, label, 8, FontWeights.Bold, Color.FromRgb(100, 228, 200), new Point(Math.Max(plot.Left, x - measured.Width - 5d), plot.Top + 17d));
        }

        private void DrawSampleMarkers(
            DrawingContext context,
            IReadOnlyList<RetirementRunwayPoint> points,
            Rect plot,
            double maximumYear,
            double maximumValue)
        {
            var interval = Math.Max(1, points.Count / 20);
            for (var index = 0; index < points.Count; index += interval)
            {
                var point = Map(points[index], item => item.TotalUsd, plot, maximumYear, maximumValue);
                context.DrawEllipse(
                    new SolidColorBrush(Color.FromRgb(238, 113, 124)),
                    new Pen(new SolidColorBrush(Color.FromRgb(15, 23, 38)), 1d),
                    point,
                    2.8d,
                    2.8d);
            }
        }

        private void DrawSelection(DrawingContext context, Rect plot, double maximumYear, double maximumValue)
        {
            if (_selectedPoint == null)
            {
                return;
            }
            var total = Map(_selectedPoint, point => point.TotalUsd, plot, maximumYear, maximumValue);
            context.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(160, 174, 188, 209)), 1d) { DashStyle = DashStyles.Dash },
                new Point(total.X, plot.Top),
                new Point(total.X, plot.Bottom));
            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(238, 113, 124)),
                new Pen(Brushes.White, 1d),
                total,
                5d,
                5d);
        }

        private void DrawSelectionCard(DrawingContext context)
        {
            if (_selectedPoint == null)
            {
                return;
            }
            var width = Math.Min(338d, Math.Max(245d, ActualWidth - 24d));
            var x = Math.Max(12d, ActualWidth - width - 12d);
            context.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(244, 19, 31, 49)),
                new Pen(new SolidColorBrush(Color.FromRgb(54, 73, 102)), 1d),
                new Rect(x, 10d, width, 75d),
                9d,
                9d);
            DrawText(context, $"MES {_selectedPoint.Month} · AÑO {_selectedPoint.Year:0.#}", 9, FontWeights.Bold, Color.FromRgb(238, 113, 124), new Point(x + 11d, 18d));
            DrawText(context, "Total " + ExactMoney(_selectedPoint.TotalUsd), 11, FontWeights.SemiBold, Color.FromRgb(231, 237, 247), new Point(x + 11d, 35d));
            DrawText(context, $"Reservas {ExactMoney(_selectedPoint.LiquidReservesUsd)} · Bonos {ExactMoney(_selectedPoint.BondsUsd)}", 8, FontWeights.Normal, Color.FromRgb(174, 188, 209), new Point(x + 11d, 54d));
            DrawText(context, $"Acciones {ExactMoney(_selectedPoint.StocksUsd)} · Gasto del mes {ExactMoney(_selectedPoint.MonthlyExpenseUsd)}", 8, FontWeights.Normal, Color.FromRgb(174, 188, 209), new Point(x + 11d, 68d));
        }

        private static string ExactMoney(double amount) =>
            amount.ToString("N2", CultureInfo.GetCultureInfo("es-AR")) + " USD";

        private void DrawGrid(DrawingContext context, Rect plot, double maximumYear, double maximumValue)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(39, 53, 77)), 1d);
            for (var index = 0; index <= 4; index++)
            {
                var y = plot.Bottom - plot.Height * index / 4d;
                context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                DrawText(context, CompactMoney(maximumValue * index / 4d), 9, FontWeights.Normal, Color.FromRgb(128, 145, 170), new Point(5d, y - 7d));

                var x = plot.Left + plot.Width * index / 4d;
                context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var year = maximumYear * index / 4d;
                var label = year < 1d ? $"{year * 12d:0} m" : $"{year:0.#} a";
                var measured = MeasureText(label, 9, FontWeights.Normal);
                DrawText(context, label, 9, FontWeights.Normal, Color.FromRgb(128, 145, 170), new Point(x - measured.Width / 2d, plot.Bottom + 9d));
            }
        }

        private void DrawArea(
            DrawingContext context,
            IReadOnlyList<RetirementRunwayPoint> points,
            Rect plot,
            double maximumYear,
            double maximumValue)
        {
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                var first = Map(points[0], point => point.TotalUsd, plot, maximumYear, maximumValue);
                drawing.BeginFigure(new Point(first.X, plot.Bottom), true, true);
                drawing.LineTo(first, true, false);
                for (var index = 1; index < points.Count; index++)
                {
                    drawing.LineTo(Map(points[index], point => point.TotalUsd, plot, maximumYear, maximumValue), true, false);
                }
                drawing.LineTo(new Point(plot.Right, plot.Bottom), true, false);
            }
            geometry.Freeze();
            context.DrawGeometry(
                new LinearGradientBrush(Color.FromArgb(65, 238, 113, 124), Color.FromArgb(2, 238, 113, 124), 90d),
                null,
                geometry);
        }

        private void DrawSeries(
            DrawingContext context,
            IReadOnlyList<RetirementRunwayPoint> points,
            Rect plot,
            double maximumYear,
            double maximumValue,
            Func<RetirementRunwayPoint, double> selector,
            Color color,
            double thickness)
        {
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(Map(points[0], selector, plot, maximumYear, maximumValue), false, false);
                for (var index = 1; index < points.Count; index++)
                {
                    drawing.LineTo(Map(points[index], selector, plot, maximumYear, maximumValue), true, false);
                }
            }
            geometry.Freeze();
            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness), geometry);
        }

        private void DrawLegend(DrawingContext context, Rect plot)
        {
            var items = new[]
            {
                ("TOTAL", Color.FromRgb(238, 113, 124)),
                ("RESERVAS", Color.FromRgb(126, 203, 238)),
                ("BONOS", Color.FromRgb(241, 185, 85)),
                ("ACCIONES", Color.FromRgb(91, 141, 239))
            };
            var x = plot.Left + 8d;
            foreach (var item in items)
            {
                context.DrawEllipse(new SolidColorBrush(item.Item2), null, new Point(x, plot.Top + 7d), 3.5d, 3.5d);
                DrawText(context, item.Item1, 8, FontWeights.Bold, Color.FromRgb(174, 188, 209), new Point(x + 8d, plot.Top + 1d));
                x += MeasureText(item.Item1, 8, FontWeights.Bold).Width + 31d;
            }
        }

        private static Point Map(
            RetirementRunwayPoint point,
            Func<RetirementRunwayPoint, double> selector,
            Rect plot,
            double maximumYear,
            double maximumValue) =>
            new Point(
                plot.Left + point.Year / maximumYear * plot.Width,
                plot.Bottom - Math.Max(0d, selector(point)) / maximumValue * plot.Height);
    }
}
