using System.Windows;
using System.Windows.Media;

namespace ARMpulse.Windows.Controls;

public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(double[]), typeof(HistoryChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SecondaryValuesProperty = DependencyProperty.Register(nameof(SecondaryValues), typeof(double[]), typeof(HistoryChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(HistoryChart), new FrameworkPropertyMetadata(Brushes.MediumSeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    public double[]? Values { get => (double[]?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double[]? SecondaryValues { get => (double[]?)GetValue(SecondaryValuesProperty); set => SetValue(SecondaryValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth; var h = ActualHeight;
        if (w < 2 || h < 2) return;
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), 1);
        for (var i = 0; i <= 4; i++) dc.DrawLine(grid, new(0, h * i / 4), new(w, h * i / 4));
        for (var i = 0; i <= 8; i++) dc.DrawLine(grid, new(w * i / 8, 0), new(w * i / 8, h));
        DrawSeries(dc, Values, Stroke, w, h, true);
        DrawSeries(dc, SecondaryValues, new SolidColorBrush(Color.FromRgb(168, 148, 250)), w, h, false);
    }

    private static void DrawSeries(DrawingContext dc, double[]? values, Brush stroke, double w, double h, bool fill)
    {
        if (values is null || values.Length < 2) return;
        // Missing readings split the line rather than becoming invented zeroes.
        var index = 0;
        while (index < values.Length)
        {
            while (index < values.Length && !double.IsFinite(values[index])) index++;
            if (index == values.Length) break;
            var start = index;
            while (index < values.Length && double.IsFinite(values[index])) index++;
            if (index - start < 2) continue;
            Point At(int i) => new(w * i / (values.Length - 1), 2 + (h - 4) * (1 - Math.Clamp(values[i], 0, 100) / 100));
            var line = new StreamGeometry();
            using (var c = line.Open())
            {
                c.BeginFigure(At(start), false, false);
                for (var i = start + 1; i < index; i++) c.LineTo(At(i), true, false);
            }
            line.Freeze();
            if (fill)
            {
                var area = new StreamGeometry();
                using (var c = area.Open())
                {
                    c.BeginFigure(new(At(start).X, h), true, true);
                    for (var i = start; i < index; i++) c.LineTo(At(i), true, false);
                    c.LineTo(new(At(index - 1).X, h), true, false);
                }
                var brush = stroke.Clone(); brush.Opacity = 0.10;
                dc.DrawGeometry(brush, null, area);
            }
            dc.DrawGeometry(null, new Pen(stroke, 2), line);
        }
    }
}
