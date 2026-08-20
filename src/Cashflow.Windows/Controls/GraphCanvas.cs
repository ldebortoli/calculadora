using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Cashflow.Core.Models;

namespace Cashflow.Windows.Controls
{
    public sealed class GraphCanvas : FrameworkElement
    {
        private const double NodeWidth = 178;
        private const double NodeHeight = 76;
        private PlatformNode? _draggedNode;
        private Vector _dragOffset;
        private CashflowScenario? _scenario;
        private bool _isPanning;
        private Point _panStart;
        private Vector _panOrigin;
        private Vector _panOffset;
        private double _zoom = 1d;

        public CashflowScenario? Scenario
        {
            get => _scenario;
            set
            {
                if (!ReferenceEquals(_scenario, value))
                {
                    _panOffset = default;
                    _zoom = 1d;
                }
                _scenario = value;
            }
        }
        public string? SelectedNodeId { get; set; }
        public string? SelectedRouteId { get; set; }
        public IReadOnlyCollection<string> HighlightedRouteIds { get; set; } = Array.Empty<string>();

        public event Action<PlatformNode>? NodeSelected;
        public event Action<TransferRoute>? RouteSelected;
        public event Action? GraphChanged;

        public GraphCanvas()
        {
            Focusable = true;
            Cursor = Cursors.Arrow;
        }

        public void RefreshGraph() => InvalidateVisual();

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            DrawGrid(drawingContext);

            if (Scenario == null)
            {
                return;
            }

            drawingContext.PushTransform(new MatrixTransform(_zoom, 0d, 0d, _zoom, _panOffset.X, _panOffset.Y));
            var nodes = Scenario.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            foreach (var route in Scenario.Routes)
            {
                if (nodes.TryGetValue(route.FromNodeId, out var from) && nodes.TryGetValue(route.ToNodeId, out var to))
                {
                    DrawRoute(drawingContext, route, from, to);
                }
            }

            foreach (var node in Scenario.Nodes)
            {
                DrawNode(drawingContext, node);
            }

            if (Scenario.Nodes.Count == 0)
            {
                DrawEmptyState(drawingContext);
            }
            drawingContext.Pop();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            if (Scenario == null) return;

            var point = ToContentPoint(e.GetPosition(this));
            var node = Scenario.Nodes.LastOrDefault(candidate => NodeBounds(candidate).Contains(point));
            if (node != null)
            {
                _draggedNode = node;
                _dragOffset = point - new Point(node.X, node.Y);
                CaptureMouse();
                NodeSelected?.Invoke(node);
                return;
            }

            var route = FindRoute(point);
            if (route != null)
            {
                RouteSelected?.Invoke(route);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isPanning && e.RightButton == MouseButtonState.Pressed)
            {
                _panOffset = _panOrigin + (e.GetPosition(this) - _panStart);
                InvalidateVisual();
                return;
            }
            if (_draggedNode == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var point = ToContentPoint(e.GetPosition(this)) - _dragOffset;
            const double margin = 12d;
            var minimumX = (margin - _panOffset.X) / _zoom;
            var maximumX = (ActualWidth - margin - _panOffset.X) / _zoom - NodeWidth;
            var minimumY = (margin - _panOffset.Y) / _zoom;
            var maximumY = (ActualHeight - margin - _panOffset.Y) / _zoom - NodeHeight;

            _draggedNode.X = Math.Max(minimumX, Math.Min(Math.Max(minimumX, maximumX), point.X));
            _draggedNode.Y = Math.Max(minimumY, Math.Min(Math.Max(minimumY, maximumY), point.Y));
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_draggedNode != null)
            {
                _draggedNode = null;
                ReleaseMouseCapture();
                GraphChanged?.Invoke();
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            Focus();
            if (Scenario == null)
            {
                return;
            }

            var point = ToContentPoint(e.GetPosition(this));
            var overNode = Scenario.Nodes.Any(node => NodeBounds(node).Contains(point));
            if (overNode || FindRoute(point) != null)
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

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Scenario == null || e.Delta == 0)
            {
                return;
            }

            const double zoomStep = 1.12d;
            const double minimumZoom = 0.55d;
            const double maximumZoom = 2.25d;
            var cursor = e.GetPosition(this);
            var contentPoint = ToContentPoint(cursor);
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

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _isPanning = false;
            Cursor = Cursors.Arrow;
        }

        private void DrawGrid(DrawingContext context)
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 38)), null, new Rect(RenderSize));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(38, 51, 74)), 1);
            pen.Freeze();
            var spacing = 24d * _zoom;
            var offsetX = ((_panOffset.X % spacing) + spacing) % spacing;
            var offsetY = ((_panOffset.Y % spacing) + spacing) % spacing;
            for (double x = offsetX; x < ActualWidth; x += spacing)
            {
                for (double y = offsetY; y < ActualHeight; y += spacing)
                {
                    context.DrawEllipse(pen.Brush, null, new Point(x, y), 1, 1);
                }
            }
        }

        private void DrawNode(DrawingContext context, PlatformNode node)
        {
            var bounds = NodeBounds(node);
            var selected = node.Id == SelectedNodeId;
            var hasManualData = Scenario?.Routes.Any(route =>
                route.Enabled && route.FromNodeId == node.Id && route.ExchangeRateIsManual) == true;
            var border = selected
                ? Color.FromRgb(24, 191, 162)
                : hasManualData
                    ? Color.FromRgb(242, 153, 74)
                    : Color.FromRgb(214, 222, 233);
            var fill = node.Kind == NodeKind.Source
                ? Color.FromRgb(16, 49, 45)
                : node.Kind == NodeKind.Destination
                    ? Color.FromRgb(21, 39, 67)
                    : hasManualData
                        ? Color.FromRgb(49, 38, 23)
                        : Color.FromRgb(23, 34, 53);

            context.DrawRoundedRectangle(
                new SolidColorBrush(fill),
                new Pen(new SolidColorBrush(border), selected ? 2.4 : 1.3),
                bounds,
                13,
                13);

            var accent = node.Kind == NodeKind.Source
                ? Color.FromRgb(24, 191, 162)
                : node.Kind == NodeKind.Destination
                    ? Color.FromRgb(78, 132, 230)
                    : Color.FromRgb(136, 151, 174);
            context.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(node.X, node.Y, 6, NodeHeight), 3, 3);

            DrawText(context, Truncate(node.Name, 22), 14, FontWeights.SemiBold, Color.FromRgb(229, 236, 247), new Point(node.X + 18, node.Y + 16));

            var currencyText = node.Currency.ToUpperInvariant();
            var currencySize = MeasureText(currencyText, 11, FontWeights.Bold);
            var pill = new Rect(node.X + 18, node.Y + 45, currencySize.Width + 18, 21);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(36, 49, 71)), null, pill, 10, 10);
            DrawText(context, currencyText, 11, FontWeights.Bold, Color.FromRgb(187, 200, 219), new Point(pill.X + 9, pill.Y + 3));

            var kind = node.Kind == NodeKind.Source ? "ORIGEN" : node.Kind == NodeKind.Destination ? "DESTINO" : "INTERMEDIO";
            var kindSize = MeasureText(kind, 9, FontWeights.SemiBold);
            DrawText(context, kind, 9, FontWeights.SemiBold, Color.FromRgb(137, 153, 178), new Point(node.X + NodeWidth - kindSize.Width - 13, node.Y + 49));

            if (hasManualData)
            {
                var markerCenter = new Point(node.X + NodeWidth - 13, node.Y + 13);
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(242, 153, 74)), new Pen(Brushes.White, 1), markerCenter, 8, 8);
                DrawText(context, "!", 10, FontWeights.Bold, Colors.White, new Point(markerCenter.X - 2, markerCenter.Y - 7));
            }
        }

        private void DrawRoute(DrawingContext context, TransferRoute route, PlatformNode from, PlatformNode to)
        {
            if (!TryGetRouteSegment(route, from, to, out var start, out var end, out var vector)) return;
            var highlighted = HighlightedRouteIds.Contains(route.Id);
            var selected = route.Id == SelectedRouteId;
            var color = selected
                ? Color.FromRgb(242, 153, 74)
                : highlighted
                    ? Color.FromRgb(24, 191, 162)
                    : route.ExchangeRateIsManual
                        ? Color.FromRgb(224, 132, 48)
                        : Color.FromRgb(149, 161, 181);
            var pen = new Pen(new SolidColorBrush(color), highlighted || selected ? 3 : 2);
            if (!route.Enabled) pen.DashStyle = DashStyles.Dash;
            context.DrawLine(pen, start, end);
            DrawArrow(context, end, vector, color);

            if (!selected && !highlighted && (Scenario?.Routes.Count ?? 0) > 6)
            {
                return;
            }

            var label = BuildRouteSummary(route);
            var labelSize = MeasureText(label, 10, FontWeights.SemiBold);
            var midpoint = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
            var labelRect = new Rect(midpoint.X - labelSize.Width / 2 - 7, midpoint.Y - 24, labelSize.Width + 14, 20);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(17, 26, 42)), new Pen(new SolidColorBrush(Color.FromRgb(51, 65, 90)), 1), labelRect, 7, 7);
            DrawText(context, label, 10, FontWeights.SemiBold, color, new Point(labelRect.X + 7, labelRect.Y + 3));
        }

        private static string BuildRouteSummary(TransferRoute route)
        {
            var fee = route.PercentageFee > 0m ? $"{route.PercentageFee:0.##}%" : string.Empty;
            if (route.PercentageFeeMinimum.HasValue || route.PercentageFeeMaximum.HasValue)
            {
                var minimum = route.PercentageFeeMinimum?.ToString("0.##") ?? "0";
                var maximum = route.PercentageFeeMaximum?.ToString("0.##") ?? "∞";
                fee += $" [{minimum}–{maximum}]";
            }

            if (route.FixedFee > 0m)
            {
                fee += (fee.Length > 0 ? " + " : string.Empty) + $"{route.FixedFee:0.##} fijo";
            }

            if (fee.Length > 0 && route.FeeApplication == FeeApplicationMode.ChargeSeparately)
            {
                fee += " aparte";
            }

            if (route.TradingFeePercentage > 0m)
            {
                fee += (fee.Length > 0 ? " + " : string.Empty) + $"trade {route.TradingFeePercentage:0.###}%";
            }

            if (route.OutputPercentageFee > 0m)
            {
                fee += (fee.Length > 0 ? " + " : string.Empty) + $"salida {route.OutputPercentageFee:0.##}%";
            }

            if (fee.Length == 0)
            {
                fee = "sin comisión";
            }

            var amountRange = string.Empty;
            if (route.MinimumInputAmount.HasValue || route.MaximumInputAmount.HasValue)
            {
                var minimum = route.MinimumInputAmount?.ToString("0.##") ?? "0";
                var maximum = route.MaximumInputAmount?.ToString("0.##") ?? "∞";
                amountRange = $"  ·  monto {minimum}–{maximum}";
            }

            if (route.InputAmountStep.HasValue)
            {
                amountRange += $"  ·  paso {route.InputAmountStep:0.########}";
            }

            var manual = route.ExchangeRateIsManual ? "● manual  ·  " : string.Empty;
            return route.ExchangeRateConfigured
                ? $"{manual}{fee}{amountRange}  ·  ×{route.ExchangeRate:0.####}"
                : $"{fee}{amountRange}  ·  cotización pendiente";
        }

        private static void DrawArrow(DrawingContext context, Point tip, Vector direction, Color color)
        {
            var perpendicular = new Vector(-direction.Y, direction.X);
            var back = tip - direction * 12;
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(tip, true, true);
                drawing.LineTo(back + perpendicular * 6, true, false);
                drawing.LineTo(back - perpendicular * 6, true, false);
            }
            geometry.Freeze();
            context.DrawGeometry(new SolidColorBrush(color), null, geometry);
        }

        private void DrawEmptyState(DrawingContext context)
        {
            const string text = "Agrega una plataforma para comenzar";
            var size = MeasureText(text, 16, FontWeights.SemiBold);
            DrawText(context, text, 16, FontWeights.SemiBold, Color.FromRgb(148, 163, 184), new Point((ActualWidth - size.Width) / 2, (ActualHeight - size.Height) / 2));
        }

        private TransferRoute? FindRoute(Point point)
        {
            if (Scenario == null) return null;
            var nodes = Scenario.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            return Scenario.Routes.LastOrDefault(route =>
            {
                if (!nodes.TryGetValue(route.FromNodeId, out var from) || !nodes.TryGetValue(route.ToNodeId, out var to)) return false;
                return TryGetRouteSegment(route, from, to, out var start, out var end, out _) &&
                    DistanceToSegment(point, start, end) <= 9;
            });
        }

        private bool TryGetRouteSegment(
            TransferRoute route,
            PlatformNode from,
            PlatformNode to,
            out Point start,
            out Point end,
            out Vector direction)
        {
            var fromCenter = Center(from);
            var toCenter = Center(to);
            direction = toCenter - fromCenter;
            if (direction.Length < 1)
            {
                start = default;
                end = default;
                return false;
            }

            direction.Normalize();
            var perpendicular = new Vector(-direction.Y, direction.X);
            var hasReverseRoute = Scenario?.Routes.Any(candidate =>
                candidate.Id != route.Id &&
                candidate.FromNodeId == route.ToNodeId &&
                candidate.ToNodeId == route.FromNodeId) == true;
            var laneOffset = hasReverseRoute ? 12d : 0d;
            start = fromCenter + direction * 90 + perpendicular * laneOffset;
            end = toCenter - direction * 90 + perpendicular * laneOffset;
            return true;
        }

        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            var segment = end - start;
            var lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
            if (lengthSquared <= 0.01) return (point - start).Length;
            var projection = ((point.X - start.X) * segment.X + (point.Y - start.Y) * segment.Y) / lengthSquared;
            projection = Math.Max(0, Math.Min(1, projection));
            var closest = start + segment * projection;
            return (point - closest).Length;
        }

        private static Rect NodeBounds(PlatformNode node) => new Rect(node.X, node.Y, NodeWidth, NodeHeight);
        private static Point Center(PlatformNode node) => new Point(node.X + NodeWidth / 2, node.Y + NodeHeight / 2);
        private Point ToContentPoint(Point point) =>
            new Point((point.X - _panOffset.X) / _zoom, (point.Y - _panOffset.Y) / _zoom);

        private FormattedText MeasureText(string text, double size, FontWeight weight) =>
            new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        private void DrawText(DrawingContext context, string text, double size, FontWeight weight, Color color, Point origin)
        {
            var formatted = MeasureText(text, size, weight);
            formatted.SetForegroundBrush(new SolidColorBrush(color));
            context.DrawText(formatted, origin);
        }

        private static string Truncate(string value, int maximum) =>
            value.Length <= maximum ? value : value.Substring(0, maximum - 1) + "…";
    }
}
