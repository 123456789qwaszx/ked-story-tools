using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.Authoring.Reading;

namespace St.App.Views;

/// <summary>
/// 고른 모션의 커브를 타임라인 아래에 편다(docs/PLAN.md §4.8) — 커브마다 한 줄, 가로는 타임라인과 같은 시간이다
/// (막대가 시작하는 때부터 그 길이만큼). 읽기만 한다: 값은 코어의 <see cref="CurveFunctions"/>로 셈한 그대로다.
/// </summary>
internal sealed class MotionCurveView : Control
{
    public const double RowHeight = 46;
    private const double Pad = 6;

    private static readonly IBrush Back = Brush.Parse("#17181B");
    private static readonly IBrush Lane = Brush.Parse("#202226");
    private static readonly IBrush Rule = Brush.Parse("#44888888");
    private static readonly IBrush Ink = Brush.Parse("#C8CCD0");
    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly Typeface MonoFace = new(Palette.Mono);

    private MotionEntry? _motion;
    private float _start;
    private Func<TimelineLayout?> _layout = () => null;

    /// <summary>보일 모션과 그 막대가 시작하는 때(초), 타임라인의 자리 셈을 넣는다. 모션이 null이면 비운다.</summary>
    public void Show(MotionEntry? motion, float start, Func<TimelineLayout?> layout)
    {
        _motion = motion;
        _start = start;
        _layout = layout;
        IsVisible = motion is { Curves.Count: > 0 };
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_motion is not { Curves.Count: > 0 } motion)
        {
            return default;
        }

        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : _layout()?.Width ?? 0;
        return new Size(width, motion.Curves.Count * RowHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_motion is not { Curves.Count: > 0 } motion || _layout() is not { } layout)
        {
            return;
        }

        context.DrawRectangle(Back, null, new Rect(0, 0, Bounds.Width, Bounds.Height), 4, 4);

        double left = layout.X(_start);
        double right = layout.X(_start + motion.Clip.Duration);

        for (int i = 0; i < motion.Curves.Count; i++)
        {
            MotionCurve curve = motion.Curves[i];
            double top = i * RowHeight;
            Rect lane = new(TimelineLayout.LabelWidth, top + 2, Math.Max(0, Bounds.Width - TimelineLayout.LabelWidth), RowHeight - 4);

            context.DrawRectangle(Lane, null, lane);
            Text(context, curve.Label, new Point(10, top + 6), 11, Ink);
            Text(context, ShortNode(curve.Node), new Point(10, top + 22), 9, Palette.Faint, MonoFace);

            // 세로 — 키의 값이 다 들게, 중립값(위치 · 회전 0, 크기 1)도 함께.
            float neutral = MotionMath.Neutral(curve.Kind);
            (float min, float max) = curve.Range;
            min = Math.Min(min, neutral);
            max = Math.Max(max, neutral);
            float span = Math.Max(max - min, 1e-4f);
            double Y(float value) => lane.Bottom - Pad - (value - min) / span * (lane.Height - 2 * Pad);

            context.DrawLine(new Pen(Rule, 1, DashStyle.Dash), new Point(left, Y(neutral)), new Point(right, Y(neutral)));
            context.DrawLine(new Pen(Rule, 1), new Point(left, lane.Top), new Point(left, lane.Bottom));
            context.DrawLine(new Pen(Rule, 1), new Point(right, lane.Top), new Point(right, lane.Bottom));

            if (right - left >= 2)
            {
                StreamGeometry path = new();

                using (StreamGeometryContext pen = path.Open())
                {
                    for (double x = left; x <= right; x += 2)
                    {
                        Point point = new(x, Y(curve.Evaluate(layout.Seconds(x) - _start)));

                        if (x == left)
                        {
                            pen.BeginFigure(point, isFilled: false);
                        }
                        else
                        {
                            pen.LineTo(point);
                        }
                    }

                    pen.LineTo(new Point(right, Y(curve.Evaluate(motion.Clip.Duration))));
                    pen.EndFigure(isClosed: false);
                }

                context.DrawGeometry(null, new Pen(Palette.Of(CommandKind.Acting), 1.5), path);
            }

            foreach (CurveKey key in curve.Keys)
            {
                context.DrawEllipse(Ink, null, new Point(layout.X(_start + key.Time), Y(key.Value)), 2, 2);
            }

            // 값의 범위 — 오른쪽 끝 바깥에.
            Text(context, Number(max), new Point(right + 4, lane.Top + 1), 9, Palette.Faint, MonoFace);
            Text(context, Number(min), new Point(right + 4, lane.Bottom - 12), 9, Palette.Faint, MonoFace);
        }
    }

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // 리그의 노드 이름은 길다 — 끝의 두 마디만.
    private static string ShortNode(string node)
    {
        string[] parts = node.Split('_');
        return parts.Length <= 2 ? node : string.Join('_', parts[^2..]);
    }

    private static void Text(DrawingContext context, string text, Point origin, double size, IBrush brush, Typeface? face = null)
        => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face ?? Face, size, brush), origin);
}
