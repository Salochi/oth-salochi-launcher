using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Salochi.Launcher.Views;

/// <summary>
/// Shows one line of content at its full width, clipped to the space it gets. When the content is wider than that, it
/// waits, glides to its end, waits and glides back, over and over, so a long name can be read whole. When it fits,
/// nothing moves.
/// </summary>
public sealed class Marquee : Decorator
{
    private const double Speed = 32;   // pixels a second
    private const double Hold = 1.6;   // seconds at either end
    private const double Fade = 18;    // pixels of fade on an edge with more text behind it

    private readonly TranslateTransform _shift = new();
    private readonly GradientStop _left = new(Colors.White, 0), _leftIn = new(Colors.White, 0);
    private readonly GradientStop _rightIn = new(Colors.White, 1), _right = new(Colors.White, 1);
    private readonly LinearGradientBrush _mask;
    private readonly Stopwatch _clock = new();
    private DispatcherTimer? _timer;
    private double _overflow, _width;

    public Marquee()
    {
        _mask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { _left, _leftIn, _rightIn, _right },
        };
    }

    static Marquee()
    {
        ClipToBoundsProperty.OverrideDefaultValue<Marquee>(true);
        ChildProperty.Changed.AddClassHandler<Marquee>((m, e) =>
        {
            if (e.OldValue is Control old) old.RenderTransform = null;
            if (e.NewValue is Control now) now.RenderTransform = m._shift;
        });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child == null) return default;
        Child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var want = Child.DesiredSize;
        return new Size(Math.Min(want.Width, availableSize.Width), want.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child == null) return finalSize;
        double full = Math.Max(Child.DesiredSize.Width, finalSize.Width);
        Child.Arrange(new Rect(0, 0, full, finalSize.Height));
        double overflow = Math.Max(0, Math.Ceiling(full - finalSize.Width));
        if (overflow != _overflow || finalSize.Width != _width)
        {
            _overflow = overflow;
            _width = finalSize.Width;
            Restart();
        }
        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop();
    }

    private void Restart()
    {
        Stop();
        _shift.X = 0;
        OpacityMask = null;
        if (_overflow < 1 || VisualRoot == null) return;
        double f = _width > 0 ? Math.Min(0.25, Fade / _width) : 0;
        _leftIn.Offset = f;
        _rightIn.Offset = 1 - f;
        OpacityMask = _mask;
        Edges(0);
        _clock.Restart();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        _timer.Start();
    }

    private void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _clock.Reset();
    }

    private void Step()
    {
        double move = _overflow / Speed;
        double t = _clock.Elapsed.TotalSeconds % (2 * Hold + 2 * move);
        double at; // 0: the start showing, 1: the end showing
        if (t < Hold) at = 0;
        else if (t < Hold + move) at = Ease((t - Hold) / move);
        else if (t < 2 * Hold + move) at = 1;
        else at = 1 - Ease((t - 2 * Hold - move) / move);
        double x = Math.Round(at * _overflow);
        _shift.X = -x;
        Edges(x);
    }

    // each edge fades out while there is text past it, and is sharp once that end of the text shows
    private void Edges(double x)
    {
        _left.Color = Color.FromArgb((byte)(255 * (1 - Math.Min(1, x / Fade))), 255, 255, 255);
        _right.Color = Color.FromArgb((byte)(255 * (1 - Math.Min(1, (_overflow - x) / Fade))), 255, 255, 255);
    }

    // slow out of either end, slow into the other
    private static double Ease(double x) => x * x * (3 - 2 * x);
}
