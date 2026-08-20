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
    public sealed class RetirementProjectionChart : FrameworkElement
    {
        private RetirementProjection? _projection;
        private double _zoom = 1d;
        private Vector _panOffset;
        private bool _isPanning;
        private Point _panStart;
        private Vector _panOrigin;

        public void ShowProjection(RetirementProjection projection)
        {
            _projection = projection;
            InvalidateVisual();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_projection == null || e.Delta == 0)
            {
                return;
            }
            const double zoomStep = 1.12d;
            var cursor = e.GetPosition(this);
            var contentPoint = new Point((cursor.X - _panOffset.X) / _zoom, (cursor.Y - _panOffset.Y) / _zoom);
            var factor = e.Delta > 0 ? zoomStep : 1d / zoomStep;
            var nextZoom = Math.Max(0.55d, Math.Min(2.25d, _zoom * factor));
            _zoom = nextZoom;
            _panOffset = cursor - new Point(contentPoint.X * _zoom, contentPoint.Y * _zoom);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (_projection == null) return;
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
            if (!_isPanning || e.RightButton != MouseButtonState.Pressed) return;
            _panOffset = _panOrigin + (e.GetPosition(this) - _panStart);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (!_isPanning) return;
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

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 38)), null, new Rect(RenderSize));
            if (_projection == null || _projection.Points.Count < 2 || ActualWidth < 300 || ActualHeight < 180)
            {
                DrawCenteredText(context, "Completá tus datos para proyectar el crecimiento", 13, Color.FromRgb(148, 163, 184));
                return;
            }

            var plot = new Rect(66, 24, Math.Max(1, ActualWidth - 92), Math.Max(1, ActualHeight - 72));
            var maximumYear = Math.Max(1d, _projection.Points.Max(point => point.Year));
            var maximumValue = Math.Max(_projection.TargetRealUsd, _projection.Points.Max(point => point.TotalRealUsd));
            maximumValue = Math.Max(1d, maximumValue * 1.08d);

            context.PushTransform(new MatrixTransform(_zoom, 0d, 0d, _zoom, _panOffset.X, _panOffset.Y));
            DrawGrid(context, plot, maximumYear, maximumValue);
            DrawTarget(context, plot, maximumYear, maximumValue);
            DrawArea(context, plot, maximumYear, maximumValue);
            DrawSeries(context, plot, maximumYear, maximumValue, point => point.BondsRealUsd, Color.FromRgb(241, 185, 85), 1.7);
            DrawSeries(context, plot, maximumYear, maximumValue, point => point.StocksRealUsd, Color.FromRgb(91, 141, 239), 1.9);
            DrawSeries(context, plot, maximumYear, maximumValue, point => point.TotalRealUsd, Color.FromRgb(24, 191, 162), 3.1);
            DrawLegend(context, plot);
            context.Pop();
        }

        private void DrawGrid(DrawingContext context, Rect plot, double maximumYear, double maximumValue)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(39, 53, 77)), 1);
            for (var index = 0; index <= 4; index++)
            {
                var y = plot.Bottom - plot.Height * index / 4d;
                context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                var value = maximumValue * index / 4d;
                DrawText(context, CompactMoney(value), 9, FontWeights.Normal, Color.FromRgb(128, 145, 170), new Point(5, y - 7));

                var x = plot.Left + plot.Width * index / 4d;
                context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var year = maximumYear * index / 4d;
                var label = year >= 10d ? $"{year:0} a" : $"{year:0.#} a";
                var measured = MeasureText(label, 9, FontWeights.Normal);
                DrawText(context, label, 9, FontWeights.Normal, Color.FromRgb(128, 145, 170), new Point(x - measured.Width / 2, plot.Bottom + 9));
            }
        }

        private void DrawTarget(DrawingContext context, Rect plot, double maximumYear, double maximumValue)
        {
            if (_projection == null)
            {
                return;
            }
            var y = MapY(_projection.TargetRealUsd, plot, maximumValue);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(242, 153, 74)), 1.5) { DashStyle = DashStyles.Dash };
            context.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            const string label = "OBJETIVO REAL";
            var size = MeasureText(label, 8, FontWeights.Bold);
            context.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(49, 38, 23)),
                null,
                new Rect(plot.Right - size.Width - 13, y - 20, size.Width + 13, 17),
                6,
                6);
            DrawText(context, label, 8, FontWeights.Bold, Color.FromRgb(242, 169, 74), new Point(plot.Right - size.Width - 7, y - 18));
        }

        private void DrawArea(DrawingContext context, Rect plot, double maximumYear, double maximumValue)
        {
            if (_projection == null)
            {
                return;
            }
            var points = _projection.Points;
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                var first = Map(points[0], point => point.TotalRealUsd, plot, maximumYear, maximumValue);
                drawing.BeginFigure(new Point(first.X, plot.Bottom), true, true);
                drawing.LineTo(first, true, false);
                for (var index = 1; index < points.Count; index++)
                {
                    drawing.LineTo(Map(points[index], point => point.TotalRealUsd, plot, maximumYear, maximumValue), true, false);
                }
                drawing.LineTo(new Point(plot.Right, plot.Bottom), true, false);
            }
            geometry.Freeze();
            var fill = new LinearGradientBrush(
                Color.FromArgb(80, 24, 191, 162),
                Color.FromArgb(3, 24, 191, 162),
                90);
            context.DrawGeometry(fill, null, geometry);
        }

        private void DrawSeries(
            DrawingContext context,
            Rect plot,
            double maximumYear,
            double maximumValue,
            Func<RetirementProjectionPoint, double> selector,
            Color color,
            double thickness)
        {
            if (_projection == null)
            {
                return;
            }
            var points = _projection.Points;
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

            var interval = Math.Max(1, points.Count / 18);
            for (var index = 0; index < points.Count; index += interval)
            {
                var point = Map(points[index], selector, plot, maximumYear, maximumValue);
                context.DrawEllipse(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.FromRgb(15, 23, 38)), 1), point, 2.7, 2.7);
            }
        }

        private void DrawLegend(DrawingContext context, Rect plot)
        {
            var items = new[]
            {
                ("TOTAL REAL", Color.FromRgb(24, 191, 162)),
                ("ACCIONES", Color.FromRgb(91, 141, 239)),
                ("BONOS", Color.FromRgb(241, 185, 85))
            };
            var x = plot.Left + 8;
            foreach (var item in items)
            {
                context.DrawEllipse(new SolidColorBrush(item.Item2), null, new Point(x, plot.Top + 7), 3.5, 3.5);
                DrawText(context, item.Item1, 8, FontWeights.Bold, Color.FromRgb(174, 188, 209), new Point(x + 8, plot.Top + 1));
                x += MeasureText(item.Item1, 8, FontWeights.Bold).Width + 31;
            }
        }

        private static Point Map(
            RetirementProjectionPoint point,
            Func<RetirementProjectionPoint, double> selector,
            Rect plot,
            double maximumYear,
            double maximumValue) =>
            new Point(
                plot.Left + point.Year / maximumYear * plot.Width,
                MapY(selector(point), plot, maximumValue));

        private static double MapY(double value, Rect plot, double maximumValue) =>
            plot.Bottom - Math.Max(0d, value) / maximumValue * plot.Height;

        private static string CompactMoney(double amount)
        {
            if (amount >= 1000000d) return $"${amount / 1000000d:0.#}M";
            if (amount >= 1000d) return $"${amount / 1000d:0.#}k";
            return $"${amount:0}";
        }

        private void DrawCenteredText(DrawingContext context, string text, double size, Color color)
        {
            var measured = MeasureText(text, size, FontWeights.SemiBold);
            DrawText(context, text, size, FontWeights.SemiBold, color, new Point((ActualWidth - measured.Width) / 2, (ActualHeight - measured.Height) / 2));
        }

        private FormattedText MeasureText(string text, double size, FontWeight weight) =>
            new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                size,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

        private void DrawText(DrawingContext context, string text, double size, FontWeight weight, Color color, Point origin)
        {
            var formatted = MeasureText(text, size, weight);
            formatted.SetForegroundBrush(new SolidColorBrush(color));
            context.DrawText(formatted, origin);
        }
    }
}
