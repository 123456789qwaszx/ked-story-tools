using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Reading;

namespace St.App;

public partial class MainWindow
{
    private readonly StackPanel _lineHost = new() { Margin = new Thickness(18, 14), Spacing = 10 };

    /// <summary>가운데에 펴 보인 줄. 고른 줄이 없으면 null.</summary>
    internal LineOutline? ShownOutline { get; private set; }

    private Control BuildLinePanel()
        => new Border
        {
            Background = Palette.Panel,
            Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _lineHost },
        };

    /// <summary>고른 줄을 종류 필터로 걸러 가운데에 편다.</summary>
    private void ShowSelectedLine()
    {
        _lineHost.Children.Clear();
        ShownOutline = null;

        if (_lines.SelectedItem is not LibraryLine line)
        {
            _lineHost.Children.Add(new TextBlock
            {
                Text = _library is null ? "대본 폴더를 열면 챕터와 장면이 왼쪽에 선다." : "왼쪽에서 줄을 고른다.",
                Foreground = Palette.Dim,
                Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        LineOutline outline = LineOutline.Build(line.File.Script.Text, line.Read, Kinds);
        ShownOutline = outline;

        _lineHost.Children.Add(BuildLineHeader(line, outline));

        foreach (OutlineStep step in outline.Steps)
        {
            _lineHost.Children.Add(BuildStep(step));
        }

        foreach (string problem in line.Read.Problems)
        {
            _lineHost.Children.Add(new TextBlock { Text = $"문제 — {problem}", Foreground = Palette.Warning, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
    }

    private static Control BuildLineHeader(LibraryLine line, LineOutline outline)
    {
        StackPanel header = new() { Spacing = 4 };

        if (line.Read.Speaker.Length > 0)
        {
            header.Children.Add(new TextBlock { Text = line.Read.Speaker, FontWeight = FontWeight.SemiBold, FontSize = 15 });
        }

        header.Children.Add(new TextBlock { Text = line.Read.Body, FontSize = 17, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock
        {
            Text = $"{line.Id ?? "LineId 없음"} · {line.Read.NodeName} · {Path.GetFileName(line.File.Script.Path)} {line.Read.Line}행",
            FontFamily = Palette.Mono,
            FontSize = 11,
            Foreground = Palette.Faint,
        });

        if (!outline.Editable)
        {
            header.Children.Add(new Border
            {
                Background = Brush.Parse("#33E0B15A"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock { Text = $"읽기만 — {outline.ReadOnlyReason}", Foreground = Palette.Warning, FontSize = 12, TextWrapping = TextWrapping.Wrap },
            });
        }

        return header;
    }

    private static Control BuildStep(OutlineStep step)
    {
        StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(Palette.Chip(step.Anchor, font: Palette.Mono));
        head.Children.Add(new TextBlock { Text = AnchorLabel(step.Kind), FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center });

        if (step.Text.Length > 0)
        {
            head.Children.Add(new TextBlock { Text = $"“{step.Text.Trim()}”", FontSize = 13, Foreground = Palette.Dim, VerticalAlignment = VerticalAlignment.Center });
        }

        if (step.Gap is { } gap)
        {
            head.Children.Add(Palette.Chip($"간격 {TimeText.Format(gap)}", Palette.Dim));
        }

        StackPanel block = new() { Spacing = 3, Children = { head } };

        foreach (OutlineCommand command in step.Commands)
        {
            block.Children.Add(BuildCommand(command));
        }

        if (step.Others.Count > 0 || step.Unknown.Count > 0)
        {
            IEnumerable<string> parts = step.Others
                .OrderBy(o => o.Key)
                .Select(o => $"{CommandKinds.Label(o.Key)} {o.Value}")
                .Concat(step.Unknown.Count > 0 ? [$"명세 표에 없는 것 {step.Unknown.Count}({string.Join(" · ", step.Unknown.Distinct())})"] : []);

            block.Children.Add(new TextBlock
            {
                Text = $"보이지 않는 것 — {string.Join(" · ", parts)}",
                FontSize = 11,
                Foreground = Palette.Faint,
                Margin = new Thickness(26, 0, 0, 0),
            });
        }

        return new Border
        {
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            Child = block,
        };
    }

    private static Control BuildCommand(OutlineCommand command)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0, 0, 0) };

        row.Children.Add(new Border { Width = 3, Background = Palette.Of(command.Kind), CornerRadius = new CornerRadius(1.5) });
        row.Children.Add(new TextBlock
        {
            Text = command.Delay > 0f ? $"+{TimeText.Format(command.Delay)}" : "+0",
            Width = 96,
            FontFamily = Palette.Mono,
            FontSize = 12,
            Foreground = command.Delay > 0f ? Palette.Dim : Palette.Faint,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock { Text = command.Name, Width = 130, FontFamily = Palette.Mono, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

        foreach (OutlineArg arg in command.Args)
        {
            // 적지 않아 생략값으로 가는 칸은 흐리게 — 값은 같지만 대본에는 없다.
            row.Children.Add(Palette.Chip($"{arg.Label} {arg.Value}", arg.Written ? null : Palette.Faint, arg.Written ? null : Brushes.Transparent));
        }

        ToolTip.SetTip(row, $"{command.Summary}\n{command.Address}");
        return row;
    }

    private static string AnchorLabel(LineAnchorKind kind) => kind switch
    {
        LineAnchorKind.Pre => "대사창이 열리기 전",
        LineAnchorKind.Start => "대사창이 열리며",
        LineAnchorKind.Text => "이 글자에 닿을 때",
        _ => "글이 다 보인 뒤",
    };
}
