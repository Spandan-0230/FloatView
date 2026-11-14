using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FloatView.Controls;

/// <summary>
/// Interactive region selector. Draws a dimmed backdrop with a clear
/// selection window, eight resize handles, and a live size readout.
/// Works entirely in DIPs inside its own bounds.
/// </summary>
public sealed class SelectionSurface : FrameworkElement
{
    private const double HandleHitSlop = 9.0;
    private const double HandleVisualSize = 7.0;

    private enum Zone
    {
        None,
        New,
        Move,
        N,
        S,
        E,
        W,
        NE,
        NW,
        SE,
        SW,
    }

    private Zone _zone = Zone.None;
    private Point _dragStart;
    private Rect _startRect;
    private Rect _rectBeforeNew;
    private bool _dragging;

    public SelectionSurface()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    #region Dependency properties

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection),
        typeof(Rect),
        typeof(SelectionSurface),
        new FrameworkPropertyMetadata(
            Rect.Empty,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectionChanged));

    public Rect Selection
    {
        get => (Rect)GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public static readonly DependencyProperty BoundsProperty = DependencyProperty.Register(
        nameof(Bounds),
        typeof(Rect),
        typeof(SelectionSurface),
        new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Area the selection is constrained to. Empty means the full element.</summary>
    public Rect Bounds
    {
        get => (Rect)GetValue(BoundsProperty);
        set => SetValue(BoundsProperty, value);
    }

    public static readonly DependencyProperty MinSizeProperty = DependencyProperty.Register(
        nameof(MinSize),
        typeof(double),
        typeof(SelectionSurface),
        new FrameworkPropertyMetadata(40.0));

    public double MinSize
    {
        get => (double)GetValue(MinSizeProperty);
        set => SetValue(MinSizeProperty, value);
    }

    public event EventHandler? SelectionChanged;

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SelectionSurface)d).SelectionChanged?.Invoke(d, EventArgs.Empty);

    #endregion

    private Rect EffectiveBounds =>
        Bounds.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0
            ? new Rect(0, 0, ActualWidth, ActualHeight)
            : Bounds;

    /// <summary>Places the initial selection, clamped inside the given bounds.</summary>
    public void Initialize(Rect bounds, Rect desired)
    {
        Bounds = bounds;
        Selection = ClampRect(desired, bounds, MinSize);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        if (full.Width <= 0 || full.Height <= 0)
            return;

        var selection = Selection;
        var accent = TryFindResource("BrushAccent") as Brush ?? new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF));
        var dim = TryFindResource("BrushOverlayDim") as Brush ?? new SolidColorBrush(Color.FromArgb(0x8C, 0x04, 0x04, 0x0A));

        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
        {
            dc.DrawRectangle(dim, null, full);
            return;
        }

        var dimGeometry = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(full),
            new RectangleGeometry(selection));
        dc.DrawGeometry(dim, null, dimGeometry);

        var accentPen = new Pen(accent, 1.6);
        accentPen.Freeze();
        dc.DrawRectangle(null, accentPen, selection);

        var handleFill = Brushes.White;
        var handlePen = new Pen(accent, 1.2);
        handlePen.Freeze();

        foreach (var corner in HandleCenters(selection))
        {
            var handleRect = new Rect(
                corner.X - HandleVisualSize / 2,
                corner.Y - HandleVisualSize / 2,
                HandleVisualSize,
                HandleVisualSize);
            dc.DrawRoundedRectangle(handleFill, handlePen, handleRect, 2, 2);
        }

        DrawSizeLabel(dc, selection, accent);
    }

    private void DrawSizeLabel(DrawingContext dc, Rect selection, Brush accent)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var text = new FormattedText(
            $"{Math.Round(selection.Width)} × {Math.Round(selection.Height)}",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10.5,
            Brushes.White,
            dpi.PixelsPerDip);

        var paddingX = 7.0;
        var paddingY = 3.0;
        var boxWidth = text.Width + paddingX * 2;
        var boxHeight = text.Height + paddingY * 2;

        var x = Math.Clamp(selection.Left, 4, Math.Max(4, ActualWidth - boxWidth - 4));
        var y = selection.Top - boxHeight - 7;
        if (y < 4)
            y = selection.Top + 7;

        var background = new SolidColorBrush(Color.FromArgb(0xD9, 0x0C, 0x0C, 0x12));
        background.Freeze();

        dc.DrawRoundedRectangle(background, null, new Rect(x, y, boxWidth, boxHeight), 5, 5);
        dc.DrawText(text, new Point(x + paddingX, y + paddingY));
    }

    private static IEnumerable<Point> HandleCenters(Rect r)
    {
        var midX = r.Left + r.Width / 2;
        var midY = r.Top + r.Height / 2;

        yield return new Point(r.Left, r.Top);
        yield return new Point(midX, r.Top);
        yield return new Point(r.Right, r.Top);
        yield return new Point(r.Left, midY);
        yield return new Point(r.Right, midY);
        yield return new Point(r.Left, r.Bottom);
        yield return new Point(midX, r.Bottom);
        yield return new Point(r.Right, r.Bottom);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var point = e.GetPosition(this);
        var bounds = EffectiveBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        Focus();
        _dragStart = ClampPoint(point, bounds);
        _dragging = true;
        CaptureMouse();

        var zone = HitTestZone(point, Selection);
        if (zone == Zone.None)
        {
            _rectBeforeNew = Selection;
            _zone = Zone.New;
            Selection = new Rect(_dragStart, new Size(0, 0));
        }
        else
        {
            _zone = zone;
            _startRect = Selection;
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var point = e.GetPosition(this);
        var bounds = EffectiveBounds;

        if (!_dragging)
        {
            Cursor = CursorForZone(HitTestZone(point, Selection));
            return;
        }

        var clamped = ClampPoint(point, bounds);
        if (_zone == Zone.New)
        {
            Selection = ClampRect(Normalize(_dragStart, clamped), bounds, MinSize);
            return;
        }

        var candidate = _zone == Zone.Move
            ? MoveRect(_startRect, clamped.X - _dragStart.X, clamped.Y - _dragStart.Y, bounds)
            : ResizeRect(_startRect, clamped, _zone, bounds, MinSize);

        Selection = candidate;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_dragging)
            return;

        _dragging = false;
        _zone = Zone.None;
        ReleaseMouseCapture();

        if (Selection.Width < MinSize || Selection.Height < MinSize)
        {
            if (!_rectBeforeNew.IsEmpty)
                Selection = _rectBeforeNew;
            else if (!Selection.IsEmpty)
                Selection = ClampRect(new Rect(Selection.X, Selection.Y, MinSize, MinSize), EffectiveBounds, MinSize);
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
        _zone = Zone.None;
    }

    private static Rect MoveRect(Rect rect, double dx, double dy, Rect bounds)
    {
        var x = Math.Clamp(rect.X + dx, bounds.Left, bounds.Right - rect.Width);
        var y = Math.Clamp(rect.Y + dy, bounds.Top, bounds.Bottom - rect.Height);
        return new Rect(x, y, rect.Width, rect.Height);
    }

    private static Rect ResizeRect(Rect rect, Point p, Zone zone, Rect bounds, double minSize)
    {
        var left = rect.Left;
        var top = rect.Top;
        var right = rect.Right;
        var bottom = rect.Bottom;

        var movingLeft = zone is Zone.W or Zone.NW or Zone.SW;
        var movingRight = zone is Zone.E or Zone.NE or Zone.SE;
        var movingTop = zone is Zone.N or Zone.NW or Zone.NE;
        var movingBottom = zone is Zone.S or Zone.SW or Zone.SE;

        if (movingLeft)
            left = Math.Clamp(p.X, bounds.Left, right - minSize);
        if (movingRight)
            right = Math.Clamp(p.X, left + minSize, bounds.Right);
        if (movingTop)
            top = Math.Clamp(p.Y, bounds.Top, bottom - minSize);
        if (movingBottom)
            bottom = Math.Clamp(p.Y, top + minSize, bounds.Bottom);

        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private static Rect Normalize(Point a, Point b) => new(a, b);

    private static Rect ClampRect(Rect rect, Rect bounds, double minSize)
    {
        if (bounds.Width < minSize || bounds.Height < minSize)
            return Rect.Intersect(rect, bounds);

        var x = Math.Clamp(rect.X, bounds.Left, bounds.Right - minSize);
        var y = Math.Clamp(rect.Y, bounds.Top, bounds.Bottom - minSize);
        var width = Math.Clamp(rect.Width, minSize, bounds.Right - x);
        var height = Math.Clamp(rect.Height, minSize, bounds.Bottom - y);
        return new Rect(x, y, width, height);
    }

    private static Point ClampPoint(Point p, Rect bounds) => new(
        Math.Clamp(p.X, bounds.Left, bounds.Right),
        Math.Clamp(p.Y, bounds.Top, bounds.Bottom));

    private static Zone HitTestZone(Point p, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return Zone.None;

        bool nearLeft = Math.Abs(p.X - rect.Left) <= HandleHitSlop;
        bool nearRight = Math.Abs(p.X - rect.Right) <= HandleHitSlop;
        bool nearTop = Math.Abs(p.Y - rect.Top) <= HandleHitSlop;
        bool nearBottom = Math.Abs(p.Y - rect.Bottom) <= HandleHitSlop;
        bool insideX = p.X >= rect.Left - HandleHitSlop && p.X <= rect.Right + HandleHitSlop;
        bool insideY = p.Y >= rect.Top - HandleHitSlop && p.Y <= rect.Bottom + HandleHitSlop;

        if (nearLeft && nearTop) return Zone.NW;
        if (nearRight && nearTop) return Zone.NE;
        if (nearLeft && nearBottom) return Zone.SW;
        if (nearRight && nearBottom) return Zone.SE;

        if (nearLeft && insideY) return Zone.W;
        if (nearRight && insideY) return Zone.E;
        if (nearTop && insideX) return Zone.N;
        if (nearBottom && insideX) return Zone.S;

        return rect.Contains(p) ? Zone.Move : Zone.None;
    }

    private static Cursor CursorForZone(Zone zone) => zone switch
    {
        Zone.NW or Zone.SE => Cursors.SizeNWSE,
        Zone.NE or Zone.SW => Cursors.SizeNESW,
        Zone.N or Zone.S => Cursors.SizeNS,
        Zone.E or Zone.W => Cursors.SizeWE,
        Zone.Move => Cursors.SizeAll,
        _ => Cursors.Cross,
    };
}
