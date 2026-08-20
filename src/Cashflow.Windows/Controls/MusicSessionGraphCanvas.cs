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
    public sealed class MusicSessionGraphCanvas : FrameworkElement
    {
        private const double SourceWidth = 150;
        private const double MethodWidth = 190;
        private const double OutcomeWidth = 155;
        private const double NodeHeight = 58;
        private MusicSessionCalculation? _calculation;
        private decimal _targetUsd;
        private double _zoom = 1d;
        private Vector _panOffset;
        private bool _isPanning;
        private Point _panStart;
        private Vector _panOrigin;

        public void ShowCalculation(MusicSessionCalculation calculation, decimal targetUsd)
        {
            _calculation = calculation;
            _targetUsd = targetUsd;
            InvalidateVisual();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_calculation == null || e.Delta == 0)
            {
                return;
            }

            const double zoomStep = 1.12d;
            const double minimumZoom = 0.55d;
            const double maximumZoom = 2.25d;
            var cursor = e.GetPosition(this);
            var contentPoint = new Point(
                (cursor.X - _panOffset.X) / _zoom,
                (cursor.Y - _panOffset.Y) / _zoom);
            var factor = e.Delta > 0 ? zoomStep : 1d / zoomStep;
            var nextZoom = Math.Max(minimumZoom, Math.Min(maximumZoom, _zoom * factor));
            if (Math.Abs(nextZoom - _zoom) < 0.0001d)
            {
                e.Handled = true;
                return;
            }

            _zoom = nextZoom;
            _panOffset = cursor - new Point(contentPoint.X * _zoom, contentPoint.Y * _zoom);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (_calculation == null)
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

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 38)), null, new Rect(RenderSize));
            DrawGrid(context);

            if (_calculation == null || _calculation.Options.Count == 0 || ActualWidth < 600 || ActualHeight < 260)
            {
                DrawCenteredText(context, "Calculá para ver los caminos como grafo", 14, FontWeights.SemiBold, Color.FromRgb(148, 163, 184));
                return;
            }

            context.PushTransform(new MatrixTransform(_zoom, 0d, 0d, _zoom, _panOffset.X, _panOffset.Y));
            var sourceX = 22d;
            var methodX = Math.Max(sourceX + SourceWidth + 105, ActualWidth * 0.43 - MethodWidth / 2);
            var outcomeX = ActualWidth - OutcomeWidth - 22;
            var methods = BuildMethods()
                .Where(method => _calculation.Options.Any(option => option.Method == method.Name))
                .ToArray();
            var sources = _calculation.Options
                .GroupBy(option => option.Source, StringComparer.Ordinal)
                .OrderBy(group => SourceOrder(group.Key))
                .Select(group => group.Key)
                .ToArray();
            var sourceBounds = BuildSourceBounds(sources, sourceX);
            var methodBounds = BuildMethodBounds(methods, methodX);

            DrawHeader(context, "SALDOS DE ORIGEN", sourceX, Color.FromRgb(108, 121, 143));
            DrawHeader(context, "CAMINO", methodX, Color.FromRgb(108, 121, 143));
            DrawHeader(context, "RESULTADO", outcomeX, Color.FromRgb(108, 121, 143));

            foreach (var method in methods)
            {
                var options = _calculation.Options.Where(option => option.Method == method.Name).ToArray();
                if (options.Length == 0)
                {
                    continue;
                }

                var best = options.OrderBy(option => option.SourceDebitAmount).First();
                foreach (var option in options.OrderBy(option => option.SourceDebitAmount))
                {
                    var isBest = ReferenceEquals(option, best);
                    DrawOptionEdge(
                        context,
                        sourceBounds[option.Source],
                        methodBounds[method.Name],
                        option,
                        method.Color,
                        isBest);
                }

                var outcome = OutcomeBounds(methodBounds[method.Name], outcomeX);
                DrawFlowEdge(context, methodBounds[method.Name], outcome, method.Color, 3d);
                DrawOutcomeNode(context, outcome, method, best);
            }

            foreach (var source in sources)
            {
                var manual = _calculation.Options.Any(option => option.Source == source && option.UsesManualData);
                DrawNode(context, sourceBounds[source], source, "ORIGEN", Color.FromRgb(23, 34, 53), Color.FromRgb(51, 65, 90), manual);
            }

            foreach (var method in methods)
            {
                if (!methodBounds.TryGetValue(method.Name, out var bounds))
                {
                    continue;
                }

                var subtitle = method.Category == MusicSessionCategory.WithoutArs
                    ? "SIN PASAR POR PESOS"
                    : "PESOS BANCARIZADOS";
                DrawNode(context, bounds, method.Name, subtitle, method.Fill, method.Color, false);
            }
            context.Pop();
        }

        private IReadOnlyList<MethodNode> BuildMethods() => new[]
        {
            new MethodNode("Efectivo vía stablecoin", MusicSessionCategory.WithoutArs, Color.FromRgb(24, 191, 162), Color.FromRgb(16, 49, 45)),
            new MethodNode("Pago directo en ARS", MusicSessionCategory.BankedArs, Color.FromRgb(78, 132, 230), Color.FromRgb(21, 39, 67)),
            new MethodNode("Recompra al oficial", MusicSessionCategory.BankedArs, Color.FromRgb(94, 109, 207), Color.FromRgb(31, 34, 71))
        };

        private static int SourceOrder(string source) => source switch
        {
            "GrabrFi" => 0,
            "Wallbit Pro" => 1,
            "Binance USDC" => 2,
            "Binance USDT" => 3,
            _ => 10
        };

        private Dictionary<string, Rect> BuildSourceBounds(IReadOnlyList<string> sources, double x)
        {
            var result = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var top = 44d;
            var bottom = Math.Max(top, ActualHeight - NodeHeight - 18);
            var spacing = sources.Count <= 1 ? 0d : (bottom - top) / (sources.Count - 1);
            for (var index = 0; index < sources.Count; index++)
            {
                result[sources[index]] = new Rect(x, top + spacing * index, SourceWidth, NodeHeight);
            }
            return result;
        }

        private Dictionary<string, Rect> BuildMethodBounds(IReadOnlyList<MethodNode> methods, double x)
        {
            var result = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var top = 54d;
            var bottom = Math.Max(top, ActualHeight - NodeHeight - 28);
            var spacing = methods.Count <= 1 ? 0d : (bottom - top) / (methods.Count - 1);
            for (var index = 0; index < methods.Count; index++)
            {
                result[methods[index].Name] = new Rect(x, top + spacing * index, MethodWidth, NodeHeight);
            }
            return result;
        }

        private static Rect OutcomeBounds(Rect method, double x) =>
            new Rect(x, method.Y, OutcomeWidth, NodeHeight);

        private void DrawOptionEdge(
            DrawingContext context,
            Rect source,
            Rect method,
            MusicSessionOption option,
            Color categoryColor,
            bool isBest)
        {
            var color = isBest ? categoryColor : Color.FromRgb(66, 82, 108);
            DrawFlowEdge(context, source, method, color, isBest ? 3d : 1.15d);
            if (!isBest)
            {
                return;
            }

            var label = $"{option.SourceDebitAmount:N2} {option.SourceCurrency}";
            var labelSize = MeasureText(label, 9, FontWeights.Bold);
            var x = (source.Right + method.Left) / 2 - labelSize.Width / 2;
            var y = (source.Top + source.Height / 2 + method.Top + method.Height / 2) / 2 - 18;
            var labelBounds = new Rect(x - 6, y - 2, labelSize.Width + 12, 18);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(17, 26, 42)), new Pen(new SolidColorBrush(Color.FromRgb(51, 65, 90)), 1), labelBounds, 7, 7);
            DrawText(context, label, 9, FontWeights.Bold, categoryColor, new Point(x, y));
            if (option.UsesManualData)
            {
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(242, 153, 74)), null, new Point(labelBounds.Right + 6, labelBounds.Top + 4), 4, 4);
            }
        }

        private static void DrawFlowEdge(DrawingContext context, Rect from, Rect to, Color color, double thickness)
        {
            var start = new Point(from.Right, from.Top + from.Height / 2);
            var end = new Point(to.Left, to.Top + to.Height / 2);
            var horizontal = Math.Max(34, (end.X - start.X) * 0.45);
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(start, false, false);
                drawing.BezierTo(
                    new Point(start.X + horizontal, start.Y),
                    new Point(end.X - horizontal, end.Y),
                    end,
                    true,
                    false);
            }
            geometry.Freeze();
            var pen = new Pen(new SolidColorBrush(color), thickness);
            context.DrawGeometry(null, pen, geometry);

            var arrow = new StreamGeometry();
            using (var drawing = arrow.Open())
            {
                drawing.BeginFigure(end, true, true);
                drawing.LineTo(new Point(end.X - 9, end.Y - 5), true, false);
                drawing.LineTo(new Point(end.X - 9, end.Y + 5), true, false);
            }
            arrow.Freeze();
            context.DrawGeometry(new SolidColorBrush(color), null, arrow);
        }

        private void DrawOutcomeNode(DrawingContext context, Rect bounds, MethodNode method, MusicSessionOption best)
        {
            string title;
            string subtitle;
            if (method.Name == "Pago directo en ARS")
            {
                title = best.RequiredArs.HasValue ? $"{best.RequiredArs.Value:N0} ARS" : "Pago en ARS";
                subtitle = "PAGO DE LA SESIÓN";
            }
            else
            {
                title = $"{_targetUsd:N2} USD";
                subtitle = "EFECTIVO PARA LA SESIÓN";
            }
            DrawNode(context, bounds, title, subtitle, method.Fill, method.Color, false);
        }

        private void DrawNode(
            DrawingContext context,
            Rect bounds,
            string title,
            string subtitle,
            Color fill,
            Color border,
            bool manual)
        {
            context.DrawRoundedRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(border), 1.4), bounds, 11, 11);
            DrawText(context, Truncate(title, 23), 11, FontWeights.SemiBold, Color.FromRgb(229, 236, 247), new Point(bounds.X + 12, bounds.Y + 11));
            DrawText(context, subtitle, 8, FontWeights.Bold, Color.FromRgb(145, 161, 186), new Point(bounds.X + 12, bounds.Y + 36));
            if (manual)
            {
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(242, 153, 74)), new Pen(Brushes.White, 1), new Point(bounds.Right - 10, bounds.Top + 10), 6, 6);
            }
        }

        private void DrawGrid(DrawingContext context)
        {
            var brush = new SolidColorBrush(Color.FromRgb(38, 51, 74));
            var spacing = 24d * _zoom;
            var offsetX = ((_panOffset.X % spacing) + spacing) % spacing;
            var offsetY = ((_panOffset.Y % spacing) + spacing) % spacing;
            for (double x = offsetX; x < ActualWidth; x += spacing)
            {
                for (double y = offsetY; y < ActualHeight; y += spacing)
                {
                    context.DrawEllipse(brush, null, new Point(x, y), 0.8, 0.8);
                }
            }
        }

        private void DrawHeader(DrawingContext context, string text, double x, Color color) =>
            DrawText(context, text, 9, FontWeights.Bold, color, new Point(x, 15));

        private void DrawCenteredText(DrawingContext context, string text, double size, FontWeight weight, Color color)
        {
            var measured = MeasureText(text, size, weight);
            DrawText(context, text, size, weight, color, new Point((ActualWidth - measured.Width) / 2, (ActualHeight - measured.Height) / 2));
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

        private static string Truncate(string value, int maximum) =>
            value.Length <= maximum ? value : value.Substring(0, maximum - 1) + "…";

        private sealed class MethodNode
        {
            public MethodNode(string name, MusicSessionCategory category, Color color, Color fill)
            {
                Name = name;
                Category = category;
                Color = color;
                Fill = fill;
            }

            public string Name { get; }
            public MusicSessionCategory Category { get; }
            public Color Color { get; }
            public Color Fill { get; }
        }
    }
}
