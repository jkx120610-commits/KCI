using KCI.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace KCI.Controls;

public sealed class HistoryChart : FrameworkElement
{
    private readonly List<double> _values = [];
    private int _capacity = 90;

    public HistoryChart()
    {
        Loaded += (_, _) => ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public void Clear()
    {
        _values.Clear();
        InvalidateVisual();
    }

    public void Push(double value)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value))
        {
            _values.Add(Math.Clamp(value, 0, 100));
            while (_values.Count > _capacity)
            {
                _values.RemoveAt(0);
            }
            InvalidateVisual();
        }
    }

    public void SetCapacity(int capacity)
    {
        _capacity = Math.Max(10, capacity);
        while (_values.Count > _capacity)
        {
            _values.RemoveAt(0);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 20 || height < 20)
        {
            return;
        }

        var gridBrush = TryFindResource("ChartGridBrush") as Brush
                        ?? new SolidColorBrush(Color.FromRgb(32, 43, 53));
        var textBrush = TryFindResource("ChartTextBrush") as Brush ?? Brushes.White;
        var gridPen = new Pen(gridBrush, 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = height * i / 4d;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(width, y));
        }
        for (var i = 0; i <= 6; i++)
        {
            var x = width * i / 6d;
            drawingContext.DrawLine(gridPen, new Point(x, 0), new Point(x, height));
        }

        if (_values.Count < 2)
        {
            return;
        }

        var points = new List<Point>(_values.Count);
        for (var i = 0; i < _values.Count; i++)
        {
            var x = width * i / Math.Max(1, _capacity - 1);
            var y = height - height * _values[i] / 100d;
            points.Add(new Point(x, y));
        }

        var line = new PathFigure { StartPoint = points[0], IsClosed = false };
        for (var i = 1; i < points.Count; i++)
        {
            line.Segments.Add(new LineSegment(points[i], true));
        }
        var lineGeometry = new PathGeometry();
        lineGeometry.Figures.Add(line);

        var color = (Accent as SolidColorBrush)?.Color ?? Color.FromRgb(78, 161, 255);
        var fillColor = Color.FromArgb(42, color.R, color.G, color.B);
        var fill = new PathFigure { StartPoint = new Point(points[0].X, height), IsClosed = true };
        foreach (var point in points)
        {
            fill.Segments.Add(new LineSegment(point, true));
        }
        fill.Segments.Add(new LineSegment(new Point(points[^1].X, height), true));
        var fillGeometry = new PathGeometry();
        fillGeometry.Figures.Add(fill);

        drawingContext.DrawGeometry(new SolidColorBrush(fillColor), null, fillGeometry);
        drawingContext.DrawGeometry(null, new Pen(Accent, 2.2), lineGeometry);

        var current = _values[^1];
        var text = new FormattedText(current.ToString("0.0", CultureInfo.InvariantCulture),
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 13, textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point(width - text.Width - 8, 6));
    }

    private void OnThemeChanged() => InvalidateVisual();
}
