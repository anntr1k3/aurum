using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace Aurum.App.Controls;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(28, 148, 163, 184)), 1);
        guidePen.Freeze();
        drawingContext.DrawLine(guidePen, new Point(0, ActualHeight - 0.5), new Point(ActualWidth, ActualHeight - 0.5));

        if (Values is null)
        {
            return;
        }

        var maximum = Maximum > 0 ? Maximum : 100;

        // Fast zero-allocation path for IList<double> (e.g. ObservableCollection<double>)
        if (Values is IList<double> doubleList)
        {
            var count = doubleList.Count;
            if (count < 2) return;

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var index = 0; index < count; index++)
                {
                    var x = index * ActualWidth / (count - 1);
                    var normalized = Math.Clamp(doubleList[index] / maximum, 0, 1);
                    var y = ActualHeight - (normalized * (ActualHeight - 2)) - 1;
                    if (index == 0)
                    {
                        context.BeginFigure(new Point(x, y), false, false);
                    }
                    else
                    {
                        context.LineTo(new Point(x, y), true, false);
                    }
                }
            }

            geometry.Freeze();
            var pen = new Pen(Stroke, 1.6)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();
            drawingContext.DrawGeometry(null, pen, geometry);
            return;
        }

        var values = Values.Cast<object>()
            .Select(static value => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length < 2)
        {
            return;
        }

        var fallbackGeometry = new StreamGeometry();
        using (var context = fallbackGeometry.Open())
        {
            for (var index = 0; index < values.Length; index++)
            {
                var x = index * ActualWidth / (values.Length - 1);
                var normalized = Math.Clamp(values[index] / maximum, 0, 1);
                var y = ActualHeight - (normalized * (ActualHeight - 2)) - 1;
                if (index == 0)
                {
                    context.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    context.LineTo(new Point(x, y), true, false);
                }
            }
        }

        fallbackGeometry.Freeze();
        var fallbackPen = new Pen(Stroke, 1.6)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        fallbackPen.Freeze();
        drawingContext.DrawGeometry(null, fallbackPen, fallbackGeometry);
    }

    private static void OnValuesChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var sparkline = (Sparkline)target;
        if (args.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= sparkline.OnCollectionChanged;
        }

        if (args.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += sparkline.OnCollectionChanged;
        }

        sparkline.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => InvalidateVisual();
}
