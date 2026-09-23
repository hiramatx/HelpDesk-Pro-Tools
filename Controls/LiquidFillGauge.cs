using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace HelpDesk_Pro_Tools.Controls;

/// <summary>
/// Circular "liquid fill" gauge: an animated wave rises to <see cref="Value"/> percent.
/// The percentage text is drawn twice so it stays readable above and below the liquid.
/// </summary>
public class LiquidFillGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LiquidFillGauge, double>(nameof(Value));

    // Liquid colour by usage: 0-50 = Normal, 51-85 = Warning, 86-100 = Critical.
    public static readonly StyledProperty<IBrush?> NormalBrushProperty =
        AvaloniaProperty.Register<LiquidFillGauge, IBrush?>(nameof(NormalBrush), Brushes.Green);

    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<LiquidFillGauge, IBrush?>(nameof(WarningBrush), Brushes.Gold);

    public static readonly StyledProperty<IBrush?> CriticalBrushProperty =
        AvaloniaProperty.Register<LiquidFillGauge, IBrush?>(nameof(CriticalBrush), Brushes.Red);

    public static readonly StyledProperty<double> WarningThresholdProperty =
        AvaloniaProperty.Register<LiquidFillGauge, double>(nameof(WarningThreshold), 50);

    public static readonly StyledProperty<double> CriticalThresholdProperty =
        AvaloniaProperty.Register<LiquidFillGauge, double>(nameof(CriticalThreshold), 85);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<LiquidFillGauge, IBrush?>(nameof(TrackBrush), Brushes.WhiteSmoke);

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<LiquidFillGauge, IBrush?>(nameof(TextBrush), Brushes.Black);

    /// <summary>Colour of the percentage where it overlaps the liquid.</summary>
    public static readonly StyledProperty<IBrush?> LiquidTextBrushProperty =
        AvaloniaProperty.Register<LiquidFillGauge, IBrush?>(nameof(LiquidTextBrush), Brushes.White);

    /// <summary>Text colour over the Warning (yellow) liquid, where white is hard to read.</summary>
    public static readonly StyledProperty<IBrush?> WarningLiquidTextBrushProperty =
        AvaloniaProperty.Register<LiquidFillGauge, IBrush?>(nameof(WarningLiquidTextBrush), Brushes.Black);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? NormalBrush { get => GetValue(NormalBrushProperty); set => SetValue(NormalBrushProperty, value); }
    public IBrush? WarningBrush { get => GetValue(WarningBrushProperty); set => SetValue(WarningBrushProperty, value); }
    public IBrush? CriticalBrush { get => GetValue(CriticalBrushProperty); set => SetValue(CriticalBrushProperty, value); }
    public double WarningThreshold { get => GetValue(WarningThresholdProperty); set => SetValue(WarningThresholdProperty, value); }
    public double CriticalThreshold { get => GetValue(CriticalThresholdProperty); set => SetValue(CriticalThresholdProperty, value); }
    public IBrush? WarningLiquidTextBrush { get => GetValue(WarningLiquidTextBrushProperty); set => SetValue(WarningLiquidTextBrushProperty, value); }

    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public IBrush? LiquidTextBrush { get => GetValue(LiquidTextBrushProperty); set => SetValue(LiquidTextBrushProperty, value); }

    static LiquidFillGauge()
    {
        AffectsRender<LiquidFillGauge>(NormalBrushProperty, WarningBrushProperty, CriticalBrushProperty,
            WarningThresholdProperty, CriticalThresholdProperty,
            TrackBrushProperty, TextBrushProperty, LiquidTextBrushProperty, WarningLiquidTextBrushProperty);
    }

    private readonly DispatcherTimer _timer;
    private double _phase;
    private double _displayed; // eases toward Value for the "filling up" animation

    public LiquidFillGauge()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Tick()
    {
        _phase = (_phase + 0.09) % (Math.PI * 2);
        var target = Math.Clamp(Value, 0, 100);
        _displayed += (target - _displayed) * 0.08;
        if (Math.Abs(target - _displayed) < 0.05) _displayed = target;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var s = Math.Min(
            double.IsInfinity(availableSize.Width) ? 140 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 140 : availableSize.Height);
        return new Size(s, s);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var outer = size / 2 - 2;
        var inner = outer - 7;

        // Colour follows the real value (rounded to match the displayed %), not the fill animation.
        var percent = Math.Round(Math.Clamp(Value, 0, 100));
        var isWarning = percent > WarningThreshold && percent <= CriticalThreshold;
        var liquid = percent > CriticalThreshold ? CriticalBrush
            : isWarning ? WarningBrush
            : NormalBrush;
        var liquidText = isWarning ? WarningLiquidTextBrush : LiquidTextBrush;

        // Outer ring
        context.DrawEllipse(null, new Pen(liquid, 3), center, outer, outer);

        var innerRect = new Rect(center.X - inner, center.Y - inner, inner * 2, inner * 2);
        var level = innerRect.Bottom - innerRect.Height * (_displayed / 100);
        var amplitude = _displayed is <= 0.5 or >= 99.5 ? 0 : inner * 0.06;

        var label = $"{Math.Round(_displayed):0}%";
        var text = CreateText(label, inner * 0.42, TextBrush);
        var textOrigin = new Point(center.X - text.Width / 2, center.Y - text.Height / 2);

        using (context.PushGeometryClip(new EllipseGeometry(innerRect)))
        {
            context.DrawRectangle(TrackBrush, null, innerRect);

            // Back wave (lighter, offset) then front wave
            var back = BuildWave(innerRect, level, amplitude, _phase + Math.PI * 0.8, inner * 1.6);
            using (context.PushOpacity(0.35))
                context.DrawGeometry(liquid, null, back);

            var front = BuildWave(innerRect, level, amplitude, _phase, inner * 1.3);
            context.DrawGeometry(liquid, null, front);

            // Text above the liquid...
            context.DrawText(text, textOrigin);

            // ...and in LiquidTextBrush where the liquid covers it
            using (context.PushGeometryClip(front))
                context.DrawText(CreateText(label, inner * 0.42, liquidText), textOrigin);
        }
    }

    private static StreamGeometry BuildWave(Rect area, double level, double amplitude, double phase, double wavelength)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        ctx.BeginFigure(new Point(area.Left, area.Bottom), true);
        for (var x = area.Left; x <= area.Right + 2; x += 2)
        {
            var y = level + amplitude * Math.Sin((x - area.Left) / wavelength * Math.PI * 2 + phase);
            ctx.LineTo(new Point(x, y));
        }
        ctx.LineTo(new Point(area.Right + 2, area.Bottom));
        ctx.EndFigure(true);

        return geometry;
    }

    private FormattedText CreateText(string text, double size, IBrush? brush) =>
        new(text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            Math.Max(size, 8),
            brush ?? Brushes.Black);
}
