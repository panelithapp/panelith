using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Panelith.App.ViewModels;

namespace Panelith.App.Controls;

/// <summary>
/// The designer's canvas: the live picture of the screen with the selected
/// widget outlined. Click to select, drag to move, drag the corner to resize.
/// </summary>
public sealed class DesignCanvas : Control
{
    /// <summary>The designer this canvas edits.</summary>
    public static readonly StyledProperty<DesignerViewModel?> DesignerProperty =
        AvaloniaProperty.Register<DesignCanvas, DesignerViewModel?>(nameof(Designer));

    private const double Handle = 12d;
    private static readonly IPen Selection = new Pen(new SolidColorBrush(Color.Parse("#3ee0c5")), 1.5d);
    private static readonly IPen Hover = new Pen(new SolidColorBrush(Color.Parse("#803ee0c5")), 1d, new DashStyle([4d, 3d], 0d));
    private static readonly IBrush HandleFill = new SolidColorBrush(Color.Parse("#3ee0c5"));
    private static readonly IBrush Empty = new SolidColorBrush(Color.FromRgb(10, 13, 18));

    private DesignerViewModel? _designer;
    private Point _grab;
    private Panelith.Themes.ThemeRect _grabBounds;
    private bool _moving;
    private bool _resizing;
    private int _hover = -1;

    static DesignCanvas()
    {
        FocusableProperty.OverrideDefaultValue<DesignCanvas>(true);
    }

    /// <inheritdoc cref="DesignerProperty"/>
    public DesignerViewModel? Designer
    {
        get => GetValue(DesignerProperty);
        set => SetValue(DesignerProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var picture = PictureRect();
        if (_designer?.Frame.Bitmap is not { } bitmap)
        {
            context.FillRectangle(Empty, picture);
            return;
        }

        context.DrawImage(bitmap, new Rect(bitmap.Size), picture);
        if (_designer.Document is { } doc)
        {
            if (_hover >= 0 && _hover < doc.Widgets.Count && !_designer.IsChosen(_hover))
            {
                context.DrawRectangle(null, Hover, ToScreen(doc.Widgets[_hover].Bounds, picture));
            }

            foreach (var index in _designer.Chosen())
            {
                if (index == _designer.SelectedIndex)
                {
                    continue;
                }

                context.DrawRectangle(null, Selection, ToScreen(doc.Widgets[index].Bounds, picture));
            }
        }

        if (_designer.SelectedBounds is { } bounds)
        {
            var box = ToScreen(bounds, picture);
            context.DrawRectangle(null, Selection, box);
            context.FillRectangle(HandleFill, new Rect(box.Right - (Handle / 2d), box.Bottom - (Handle / 2d), Handle, Handle));
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var aspect = _designer?.CanvasAspect ?? 4d;
        var width = double.IsInfinity(availableSize.Width) ? 960d : availableSize.Width;
        var height = width / aspect;
        if (!double.IsInfinity(availableSize.Height) && height > availableSize.Height)
        {
            height = availableSize.Height;
            width = height * aspect;
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != DesignerProperty)
        {
            return;
        }

        if (_designer is not null)
        {
            _designer.Frame.Changed -= OnFrameChanged;
            _designer.PropertyChanged -= OnDesignerChanged;
        }

        _designer = change.NewValue as DesignerViewModel;
        if (_designer is not null)
        {
            _designer.Frame.Changed += OnFrameChanged;
            _designer.PropertyChanged += OnDesignerChanged;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_designer?.Document is null)
        {
            return;
        }

        var picture = PictureRect();
        var at = e.GetPosition(this);
        if (_designer.SelectedBounds is { } selected)
        {
            var box = ToScreen(selected, picture);
            if (new Rect(box.Right - Handle, box.Bottom - Handle, Handle * 2d, Handle * 2d).Contains(at))
            {
                StartDrag(at, selected, resizing: true, e);
                return;
            }
        }

        var (x, y) = ToDesign(at, picture);
        var hit = _designer.HitTest(x, y);
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _designer.Select(hit, extend);
        if (!extend && hit >= 0 && _designer.SelectedBounds is { } bounds)
        {
            StartDrag(at, bounds, resizing: false, e);
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_designer?.Document is not { } doc)
        {
            return;
        }

        var picture = PictureRect();
        var at = e.GetPosition(this);
        if (!_moving && !_resizing)
        {
            var (hx, hy) = ToDesign(at, picture);
            var hover = _designer.HitTest(hx, hy);
            if (hover != _hover)
            {
                _hover = hover;
                InvalidateVisual();
            }

            return;
        }

        var scale = doc.Canvas.Width / picture.Width;
        var dx = (at.X - _grab.X) * scale;
        var dy = (at.Y - _grab.Y) * scale;
        var b = _grabBounds;
        if (_moving)
        {
            _designer.SetSelectedBounds(b.X + dx, b.Y + dy, b.Width, b.Height);
        }
        else
        {
            var width = b.Width + dx;
            var height = b.Height + dy;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && b.Height > 0)
            {
                height = width * b.Height / b.Width;
            }

            _designer.SetSelectedBounds(b.X, b.Y, width, height);
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_moving || _resizing)
        {
            _designer?.EndDrag();
        }

        _moving = _resizing = false;
        e.Pointer.Capture(null);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    private void StartDrag(Point at, Panelith.Themes.ThemeRect bounds, bool resizing, PointerPressedEventArgs e)
    {
        _designer!.BeginDrag();
        _grab = at;
        _grabBounds = bounds;
        _resizing = resizing;
        _moving = !resizing;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnFrameChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnDesignerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignerViewModel.CanvasAspect))
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    private Rect PictureRect()
    {
        var aspect = _designer?.CanvasAspect ?? 4d;
        var bounds = Bounds.Size;
        var width = bounds.Width;
        var height = width / aspect;
        if (height > bounds.Height)
        {
            height = bounds.Height;
            width = height * aspect;
        }

        return new Rect((bounds.Width - width) / 2d, (bounds.Height - height) / 2d, width, height);
    }

    private Rect ToScreen(Panelith.Themes.ThemeRect bounds, Rect picture)
    {
        var scale = picture.Width / (_designer?.CanvasWidth ?? 1d);
        return new Rect(picture.X + (bounds.X * scale), picture.Y + (bounds.Y * scale), bounds.Width * scale, bounds.Height * scale);
    }

    private (double X, double Y) ToDesign(Point at, Rect picture)
    {
        var scale = (_designer?.CanvasWidth ?? 1d) / picture.Width;
        return ((at.X - picture.X) * scale, (at.Y - picture.Y) * scale);
    }
}
