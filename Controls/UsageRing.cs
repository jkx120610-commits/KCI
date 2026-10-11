using KCI.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace KCI.Controls;

public sealed class UsageRing : FrameworkElement
{
    public UsageRing()
    {
        Loaded += (_, _) => ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageRing),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(UsageRing),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(UsageRing),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(UsageRing),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 10)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var thickness = Math.Max(6, size * 0.065);
        var radius = size / 2 - thickness - 2;
        var trackBrush = TryFindResource("RingTrackBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(35, 47, 58));
        var textBrush = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
        var secondaryBrush = TryFindResource("TextSecondaryBrush") as Brush
                             ?? new SolidColorBrush(Color.FromRgb(148, 164, 179));
        var basePen = new Pen(trackBrush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var accentPen = new Pen(Accent, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        drawingContext.DrawEllipse(null, basePen, center, radius, radius);
        if (!double.IsNaN(Value) && !double.IsInfinity(Value))
        {
            var normalized = Math.Clamp(Value, 0, 100) / 100d;
            var start = new Point(center.X, center.Y - radius);
            var endAngle = -90 + normalized * 360;
            var end = PointOnCircle(center, radius, endAngle);
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end,
                new Size(radius, radius), 0, normalized > 0.5,
                SweepDirection.Clockwise, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            drawingContext.DrawGeometry(null, accentPen, geometry);
        }

        var valueText = double.IsNaN(Value) || double.IsInfinity(Value)
            ? "--"
            : Value.ToString("0.#", CultureInfo.InvariantCulture) + Unit;
        var value = new FormattedText(valueText, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal,
                FontWeights.SemiBold, FontStretches.Normal),
            Math.Max(18, size * 0.17), textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var title = new FormattedText(Title, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal,
                FontWeights.Normal, FontStretches.Normal),
            Math.Max(11, size * 0.08), secondaryBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        drawingContext.DrawText(value,
            new Point(center.X - value.Width / 2, center.Y - value.Height / 2 - 4));
        drawingContext.DrawText(title,
            new Point(center.X - title.Width / 2, center.Y + value.Height / 2 + 1));
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private void OnThemeChanged() => InvalidateVisual();
}
