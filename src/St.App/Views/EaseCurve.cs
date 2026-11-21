using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ked.Presentation.Core;

namespace St.App.Views;

/// <summary>이징 하나의 곡선 — 가로가 시간, 세로가 진행. 코어의 이징 함수(재생과 같은 것)로 그린다.</summary>
internal sealed class EaseCurve : Control
{
    private const int Samples = 32;

    private EaseKind? _kind;

    public EaseCurve()
    {
        Width = 54;
        Height = 30;
    }

    /// <summary>그릴 이징. null이면 곡선 없이 바탕만 — 적지 않아 그 커맨드의 기본 이징으로 간다.</summary>
    public EaseKind? Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect box = new(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1);
        context.DrawRectangle(Palette.Panel, new Pen(Palette.Line, 1), box, 3, 3);

        if (_kind is not { } kind)
        {
            return;
        }

        // 튀는 이징(Back · Elastic)은 0 ~ 1을 넘는다 — 위아래에 여유를 둔다.
        const double margin = 0.22;
        Point At(float t)
        {
            double value = EaseFunctions.Evaluate(kind, t);
            double y = box.Bottom - (((value + margin) / (1 + (2 * margin))) * box.Height);
            return new Point(box.Left + (t * box.Width), Math.Clamp(y, box.Top, box.Bottom));
        }

        StreamGeometry curve = new();

        using (StreamGeometryContext path = curve.Open())
        {
            path.BeginFigure(At(0f), isFilled: false);

            for (int i = 1; i <= Samples; i++)
            {
                path.LineTo(At(i / (float)Samples));
            }

            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, new Pen(Palette.Edited, 1.5), curve);
    }
}
