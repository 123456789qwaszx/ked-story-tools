using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;

namespace St.App.Views;

/// <summary>화면의 색과 작은 조각 — 종류마다 색 하나. 목록의 점 · 필터의 단추 · 커맨드 줄의 띠가 같은 색을 쓴다.</summary>
internal static class Palette
{
    public static readonly IBrush Panel = Brush.Parse("#1E1F22");
    public static readonly IBrush Raised = Brush.Parse("#2A2C30");
    public static readonly IBrush Line = Brush.Parse("#33888888");
    public static readonly IBrush Dim = Brush.Parse("#9AA0A6");
    public static readonly IBrush Faint = Brush.Parse("#6B7076");
    public static readonly IBrush Warning = Brush.Parse("#E0B15A");
    public static readonly IBrush Edited = Brush.Parse("#6FCF97");
    public static readonly IBrush Picked = Brush.Parse("#334A90D9");

    public static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Menlo,monospace");

    /// <summary>종류의 색. 연출가의 종류가 아닌 것(세우기 · 소리 · 시간)은 회색이다.</summary>
    public static IBrush Of(CommandKind kind) => kind switch
    {
        CommandKind.Camera => Brush.Parse("#5AA9E6"),
        CommandKind.Placement => Brush.Parse("#7BC67B"),
        CommandKind.Expression => Brush.Parse("#F07FA8"),
        CommandKind.Acting => Brush.Parse("#F2A65A"),
        CommandKind.Emoji => Brush.Parse("#4FD1C5"),
        CommandKind.Screen => Brush.Parse("#C58AF9"),
        _ => Faint,
    };

    /// <summary>둥근 딱지 — 값 · 간격 · 앵커 이름.</summary>
    public static Border Chip(string text, IBrush? foreground = null, IBrush? background = null, FontFamily? font = null)
    {
        TextBlock label = new() { Text = text, FontSize = 12 };

        // null을 넣으면 테마의 글자색이 지워진다 — 준 것만 얹는다.
        if (foreground is not null)
        {
            label.Foreground = foreground;
        }

        if (font is not null)
        {
            label.FontFamily = font;
        }

        return new Border
        {
            Background = background ?? Raised,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = label,
        };
    }

    /// <summary>종류의 색 점.</summary>
    public static Border Dot(CommandKind kind, double size = 8)
        => new()
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = Of(kind),
            VerticalAlignment = VerticalAlignment.Center,
        };
}
