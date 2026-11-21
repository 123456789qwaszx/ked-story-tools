using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App.Views;

/// <summary>
/// 줄 하나의 타임라인(docs/PLAN.md §4.6) — 가로가 시간(프레임), 세로가 종류의 트랙이다.
/// 그리기와 입력뿐이다: 자리 셈은 <see cref="TimelineLayout"/>, 끌어 고친 것을 조작으로 바꾸는 일은 <see cref="TimelineEdits"/>가 한다.
///
///   막대를 옆으로 끌면 지연 — 그 막대만 옮긴다. Shift를 누르고 끌면 같은 앵커의 뒤 커맨드가 함께 움직인다.
///   막대의 오른쪽 끝을 끌면 길이, 앵커의 세로줄을 끌면 앞 앵커의 간격, 맨 끝의 줄을 끌면 넘김 전의 머묾.
///   끄는 동안에는 그림만 따라오고, 놓을 때 한 번 고친다 — 끄는 내내 파일에 쓰지 않는다.
/// </summary>
internal sealed class TimelineView : Control
{
    // 이만큼도 움직이지 않고 놓으면 누른 것이다(끈 것이 아니다).
    private const double ClickSlop = 3;

    // 아직 자리를 잡지 못했을 때의 폭.
    private const double DefaultWidth = 800;

    private static readonly IBrush Back = Brush.Parse("#17181B");
    private static readonly IBrush Lane = Brush.Parse("#202226");
    private static readonly IBrush Rule = Brush.Parse("#44888888");
    private static readonly IBrush Ink = Brush.Parse("#C8CCD0");
    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly Typeface MonoFace = new(Palette.Mono);

    private LineTimeline? _timeline;
    private IReadOnlyList<CommandKind> _tracks = [];
    private IReadOnlySet<CommandAddress> _picked = new HashSet<CommandAddress>();
    private KeyModifiers _pressedWith;

    private TimelineHit _drag = TimelineHit.None;
    private double _dragFrom;
    private double _dragTo;
    private bool _dragTogether;

    /// <summary>막대를 눌렀다 — 그 커맨드를 고른다(Ctrl · Shift면 여럿).</summary>
    public event Action<CommandAddress, KeyModifiers>? BarPicked;

    /// <summary>끌어서 고쳤다 — 조작들과, 끈 막대의 주소(막대가 아니면 null).</summary>
    public event Action<IReadOnlyList<LineEdit>, CommandAddress?>? Edited;

    /// <summary>보일 줄 · 트랙의 종류 · 고른 커맨드들을 넣는다.</summary>
    public void Show(LineTimeline? timeline, IReadOnlyList<CommandKind> tracks, IReadOnlySet<CommandAddress> picked)
    {
        _timeline = timeline;
        _tracks = tracks;
        _picked = picked;
        _drag = TimelineHit.None;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>지금의 자리 셈. 보일 줄이 없으면 null.</summary>
    internal TimelineLayout? Layout
        => _timeline is null ? null : new TimelineLayout(_timeline, _tracks, Bounds.Width > 0 ? Bounds.Width : double.IsNaN(Width) ? DefaultWidth : Width);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_timeline is null)
        {
            return default;
        }

        // 가로로 구르는 자리 안에서는 폭이 끝없다 — 넓힌 폭(Width)이 있으면 그것, 없으면 기본 폭.
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : double.IsNaN(Width) ? DefaultWidth : Width;
        return new Size(width, new TimelineLayout(_timeline, _tracks, width).Height);
    }

    // ── 입력 ────────────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Layout is not { } layout)
        {
            return;
        }

        Point point = e.GetPosition(this);
        _drag = layout.HitTest(point);
        _dragFrom = _dragTo = point.X;
        _dragTogether = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _pressedWith = e.KeyModifiers;

        if (_drag.Kind != TimelineHitKind.None)
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);

        if (_drag.Kind != TimelineHitKind.None)
        {
            _dragTo = point.X;
            InvalidateVisual();
            return;
        }

        Cursor = Layout?.HitTest(point).Kind switch
        {
            TimelineHitKind.Bar => new Cursor(StandardCursorType.Hand),
            TimelineHitKind.BarEnd or TimelineHitKind.Anchor or TimelineHitKind.Ready => new Cursor(StandardCursorType.SizeWestEast),
            _ => Cursor.Default,
        };
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        TimelineHit drag = _drag;
        _drag = TimelineHit.None;
        e.Pointer.Capture(null);

        if (drag.Kind == TimelineHitKind.None || _timeline is null)
        {
            return;
        }

        CommandAddress? bar = drag.Kind is TimelineHitKind.Bar or TimelineHitKind.BarEnd ? _timeline.Bars[drag.Index].Address : null;

        if (Math.Abs(_dragTo - _dragFrom) < ClickSlop)
        {
            if (bar is { } address)
            {
                BarPicked?.Invoke(address, _pressedWith);
            }

            InvalidateVisual();
            return;
        }

        IReadOnlyList<LineEdit> edits = Drag(drag, _dragFrom, _dragTo, _dragTogether);

        if (edits.Count > 0)
        {
            Edited?.Invoke(edits, bar);
        }
        else
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 잡은 것을 가로로 끌었을 때의 조작 — 놓을 때 이것이 고쳐진다. 바뀌는 것이 없으면 빈 목록.
    /// <paramref name="together"/>이면 막대를 옮길 때 뒤의 커맨드가 함께 움직인다(Shift).
    /// </summary>
    internal IReadOnlyList<LineEdit> Drag(TimelineHit hit, double fromX, double toX, bool together)
    {
        if (_timeline is not { } timeline || Layout is not { } layout)
        {
            return [];
        }

        float delta = (float)((toX - fromX) / layout.PixelsPerSecond);

        switch (hit.Kind)
        {
            case TimelineHitKind.Bar:
            {
                TimelineBar bar = timeline.Bars[hit.Index];
                return TimelineEdits.MoveBar(timeline.Line, bar.Address, bar.Delay + delta, alone: !together);
            }

            case TimelineHitKind.BarEnd:
            {
                TimelineBar bar = timeline.Bars[hit.Index];
                return TimelineEdits.ResizeBar(timeline.Line, bar.Address, (bar.Duration ?? 0f) + delta) is { } resize ? [resize] : [];
            }

            case TimelineHitKind.Anchor:
                return TimelineEdits.MoveAnchor(timeline, hit.Index, timeline.Anchors[hit.Index].Open + delta) is { } gap ? [gap] : [];

            case TimelineHitKind.Ready:
                return TimelineEdits.MoveReady(timeline, timeline.Ready + delta) is { } hold ? [hold] : [];

            default:
                return [];
        }
    }

    // ── 그리기 ──────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Layout is not { } layout)
        {
            return;
        }

        LineTimeline timeline = layout.Timeline;
        context.DrawRectangle(Back, null, new Rect(0, 0, Bounds.Width, layout.Height), 4, 4);

        DrawRuler(context, layout);

        // 트랙 — 이름과 바탕.
        foreach (CommandKind kind in layout.Tracks)
        {
            (double top, double height) = layout.Track(kind);
            context.DrawRectangle(Lane, null, new Rect(TimelineLayout.LabelWidth, top, Bounds.Width - TimelineLayout.LabelWidth, height));
            context.DrawEllipse(Palette.Of(kind), null, new Point(10, top + height / 2), 3.5, 3.5);
            Text(context, CommandKinds.Label(kind), new Point(20, top + height / 2 - 8), 11, Ink);
        }

        DrawDialogue(context, layout);

        // 앵커의 세로줄 — 글에 걸린 것(어림)은 점선이다.
        float dragDelta = (float)((_dragTo - _dragFrom) / layout.PixelsPerSecond);

        for (int i = 0; i < timeline.Anchors.Count; i++)
        {
            TimelineAnchor anchor = timeline.Anchors[i];
            double x = layout.X(anchor.Open + (_drag is { Kind: TimelineHitKind.Anchor } && _drag.Index == i ? dragDelta : 0f));
            Pen pen = new(Rule, 1, anchor.Estimated ? DashStyle.Dash : null);
            context.DrawLine(pen, new Point(x, 0), new Point(x, layout.Height));
            Text(context, anchor.Name, new Point(x + 3, 3), 11, Ink, MonoFace);
        }

        // 줄의 끝 — 넘김을 받는 때.
        double ready = layout.X(timeline.Ready + (_drag.Kind == TimelineHitKind.Ready ? dragDelta : 0f));
        context.DrawLine(new Pen(Palette.Warning, 1.5), new Point(ready, 0), new Point(ready, layout.Height));

        for (int i = 0; i < timeline.Bars.Count; i++)
        {
            DrawBar(context, layout, i, dragDelta);
        }
    }

    private void DrawRuler(DrawingContext context, TimelineLayout layout)
    {
        // 눈금의 간격 — 글자가 겹치지 않을 만큼 넓은 것 가운데 가장 촘촘한 것(프레임).
        int[] steps = [1, 2, 5, 10, 15, 30, 60, 150, 300];
        double framePixels = layout.PixelsPerSecond / DurationToken.FramesPerSecond;
        int step = steps.FirstOrDefault(s => s * framePixels >= 44, steps[^1]);
        int last = (int)Math.Ceiling(layout.Seconds(Bounds.Width) * DurationToken.FramesPerSecond);

        for (int frame = 0; frame <= last; frame += step)
        {
            double x = layout.X(frame / DurationToken.FramesPerSecond);
            context.DrawLine(new Pen(Rule, 1), new Point(x, TimelineLayout.RulerHeight - 5), new Point(x, TimelineLayout.RulerHeight));
            Text(context, frame.ToString(CultureInfo.InvariantCulture), new Point(x + 2, TimelineLayout.RulerHeight - 12), 9, Palette.Faint, MonoFace);
        }

        Text(context, "프레임", new Point(8, TimelineLayout.RulerHeight - 14), 10, Palette.Faint);
    }

    private void DrawDialogue(DrawingContext context, TimelineLayout layout)
    {
        LineTimeline timeline = layout.Timeline;
        Text(context, "대사", new Point(20, layout.TextTop + 4), 11, Ink);

        for (int i = 0; i < timeline.Anchors.Count; i++)
        {
            TimelineAnchor anchor = timeline.Anchors[i];

            if (anchor.Text.Length == 0)
            {
                continue;
            }

            // 그 조각이 쳐지는 동안 — 다음 앵커가 열릴 때(마지막 조각은 타자기가 끝날 때)까지.
            float until = i + 1 < timeline.Anchors.Count ? timeline.Anchors[i + 1].Open : timeline.TypingEnd;
            double left = layout.X(anchor.Open);
            double width = Math.Max(4, layout.X(until) - left - 2);

            context.DrawRectangle(Lane, null, new Rect(left, layout.TextTop + 3, width, TimelineLayout.TextHeight - 6), 3, 3);

            using (context.PushClip(new Rect(left, layout.TextTop, width, TimelineLayout.TextHeight)))
            {
                Text(context, anchor.Text.Trim(), new Point(left + 4, layout.TextTop + 5), 11, Palette.Dim);
            }
        }
    }

    private void DrawBar(DrawingContext context, TimelineLayout layout, int index, float dragDelta)
    {
        TimelineBar bar = layout.Timeline.Bars[index];
        Rect rect = layout.BarRect(index);

        if (rect.Width <= 0)
        {
            return;
        }

        // 끄는 동안 — 몸을 끌면 통째로, 끝을 끌면 폭만 따라온다. 더 앞선 기다림보다 앞으로는 가지 않는다.
        if (_drag.Index == index && _drag.Kind == TimelineHitKind.Bar)
        {
            double shift = Math.Max(dragDelta, bar.Floor - bar.Delay) * layout.PixelsPerSecond;
            rect = rect.Translate(new Vector(shift, 0));
        }
        else if (_drag.Index == index && _drag.Kind == TimelineHitKind.BarEnd)
        {
            rect = rect.WithWidth(Math.Max(TimelineLayout.PointWidth, rect.Width + dragDelta * layout.PixelsPerSecond));
        }

        IBrush color = Palette.Of(bar.Kind);
        bool picked = _picked.Contains(bar.Address);
        Pen? outline = picked ? new Pen(Brushes.White, 1.5) : null;

        if (bar.Duration is null)
        {
            // 길이를 모른다 — 점(마름모)으로.
            Point center = rect.Center;
            double half = rect.Height / 2;
            StreamGeometry diamond = new();

            using (StreamGeometryContext path = diamond.Open())
            {
                path.BeginFigure(new Point(rect.Left, center.Y), isFilled: true);
                path.LineTo(new Point(rect.Left + half, rect.Top));
                path.LineTo(new Point(rect.Left + 2 * half, center.Y));
                path.LineTo(new Point(rect.Left + half, rect.Bottom));
                path.EndFigure(isClosed: true);
            }

            context.DrawGeometry(color, outline, diamond);
            Text(context, bar.Name, new Point(rect.Left + 2 * half + 3, rect.Top), 10, Ink, MonoFace);
            return;
        }

        context.DrawRectangle(color, outline, rect, 3, 3);

        using (context.PushClip(rect))
        {
            Text(context, bar.Name, new Point(rect.Left + 4, rect.Top), 10, Brushes.Black, MonoFace);
        }
    }

    private static void Text(DrawingContext context, string text, Point origin, double size, IBrush brush, Typeface? face = null)
        => context.DrawText(
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face ?? Face, size, brush), origin);
}
