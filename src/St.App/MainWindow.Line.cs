using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 가운데 — 고른 줄의 머리 · 타임라인 · 스텝 목록. 커맨드와 앵커를 여기서 고른다.
/// 커맨드는 여럿을 고를 수 있다(Ctrl로 하나씩, Shift로 사이를) — 마지막에 누른 것이 오른쪽에 칸으로 선다.
/// </summary>
public partial class MainWindow
{
    private readonly StackPanel _lineHost = new() { Margin = new Thickness(18, 14), Spacing = 10 };
    private readonly List<CommandAddress> _picked = [];

    private string? _shownLine;
    private bool _comparing;

    /// <summary>가운데에 펴 보인 줄. 고른 줄이 없으면 null.</summary>
    internal LineOutline? ShownOutline { get; private set; }

    /// <summary>가운데에서 고른 커맨드 — 여럿이면 마지막에 누른 것. 오른쪽에서 그 칸을 고친다.</summary>
    internal CommandAddress? PickedCommand => _picked.Count > 0 ? _picked[^1] : null;

    /// <summary>고른 커맨드 전부 — 누른 차례.</summary>
    internal IReadOnlyList<CommandAddress> PickedCommands => _picked;

    /// <summary>가운데에서 고른 스텝(앵커) — 오른쪽에서 그 간격을 고치고 거기에 더한다. 커맨드를 고르면 null이다.</summary>
    internal string? PickedAnchor { get; private set; }

    /// <summary>고치기 전과 맞대어 보고 있는가.</summary>
    internal bool Comparing => _comparing;

    /// <summary>맞대어 보는 중이면 그 달라진 것. 아니면 빈 목록.</summary>
    internal IReadOnlyList<LineChange> ShownChanges { get; private set; } = [];

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
        ShownChanges = [];

        if (_lines.SelectedItem is not LibraryLine line)
        {
            _shownLine = null;
            _picked.Clear();
            PickedAnchor = null;
            ShownTimeline = null;

            _lineHost.Children.Add(new TextBlock
            {
                Text = Library is null ? "대본 폴더를 열면 챕터와 장면이 왼쪽에 선다." : "왼쪽에서 줄을 고른다.",
                Foreground = Palette.Dim,
                Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            RefreshInspector();
            return;
        }

        LineOutline outline = LineOutline.Build(line.File.Script.Text, line.Read, Kinds);
        ShownOutline = outline;

        // 다른 줄로 옮겼으면 고른 것을 놓는다. 같은 줄이면(고친 뒤 다시 읽었다) 그대로 쥔다 —
        // 다만 사라졌거나 필터에 가려진 것은 놓는다.
        string key = $"{line.File.Script.Path}|{line.Read.LineId}";

        if (key != _shownLine)
        {
            _picked.Clear();
            PickedAnchor = null;
            _comparing = false;
        }

        _shownLine = key;

        HashSet<CommandAddress> shown = outline.Steps.SelectMany(s => s.Commands).Select(c => c.Address).ToHashSet();
        _picked.RemoveAll(address => !shown.Contains(address));

        if (PickedAnchor is { } anchor && outline.Steps.All(s => s.Anchor != anchor))
        {
            PickedAnchor = null;
        }

        bool edited = _session?.IsEdited(line) == true;
        _lineHost.Children.Add(BuildLineHeader(line, outline, edited));

        if (_comparing && edited && _session?.Original(line) is { } original)
        {
            ShownChanges = LineDiff.Compare(original.Line, line.Read);
            _lineHost.Children.Add(BuildChanges(ShownChanges));
        }

        _lineHost.Children.Add(BuildTimeline(line));

        foreach (OutlineStep step in outline.Steps)
        {
            _lineHost.Children.Add(BuildStep(step));
        }

        foreach (string problem in line.Read.Problems)
        {
            _lineHost.Children.Add(new TextBlock { Text = $"문제 — {DirectorText.Clean(problem)}", Foreground = Palette.Warning, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }

        RefreshInspector();
    }

    private Control BuildLineHeader(LibraryLine line, LineOutline outline, bool edited)
    {
        StackPanel header = new() { Spacing = 4 };

        if (line.Read.Speaker.Length > 0)
        {
            header.Children.Add(new TextBlock { Text = line.Read.Speaker, FontWeight = FontWeight.SemiBold, FontSize = 15 });
        }

        header.Children.Add(new TextBlock { Text = line.Read.Body, FontSize = 17, TextWrapping = TextWrapping.Wrap });

        StackPanel meta = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        meta.Children.Add(new TextBlock
        {
            Text = $"{line.Id ?? "LineId 없음"} · {line.Read.NodeName} · {Path.GetFileName(line.File.Script.Path)} {line.Read.Line}행",
            FontFamily = Palette.Mono,
            FontSize = 11,
            Foreground = Palette.Faint,
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (edited)
        {
            meta.Children.Add(new TextBlock { Text = "● 고침", FontSize = 11, Foreground = Palette.Edited, VerticalAlignment = VerticalAlignment.Center });

            Button compare = new()
            {
                Content = _comparing ? "맞대기 닫기" : "처음과 맞대기",
                FontSize = 11,
                Padding = new Thickness(6, 1),
                MinHeight = 0,
            };
            compare.Click += (_, _) => SetComparing(!_comparing);
            ToolTip.SetTip(compare, "이 대본을 연 뒤로 이 줄에서 빠진 것과 더해진 것을 본다");
            meta.Children.Add(compare);
        }

        header.Children.Add(meta);

        if (!outline.Editable)
        {
            header.Children.Add(new Border
            {
                Background = Brush.Parse("#33E0B15A"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock { Text = $"읽기만 — {DirectorText.Clean(outline.ReadOnlyReason)}", Foreground = Palette.Warning, FontSize = 12, TextWrapping = TextWrapping.Wrap },
            });
        }

        return header;
    }

    // 고치기 전과 뒤 — 앵커마다 빠진 것(−)과 더해진 것(+).
    private static Control BuildChanges(IReadOnlyList<LineChange> changes)
    {
        StackPanel rows = new() { Spacing = 2 };
        rows.Children.Add(new TextBlock { Text = "처음과 달라진 것 — 빠진 것(−)과 더해진 것(+). 값만 바뀐 커맨드는 둘로 보인다.", FontSize = 11, Foreground = Palette.Faint });

        foreach (LineChange change in changes)
        {
            rows.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = change.Added ? "+" : "−", Width = 10, FontFamily = Palette.Mono, FontSize = 13, Foreground = change.Added ? Palette.Edited : Palette.Warning },
                    Palette.Chip(change.Anchor, Palette.Dim, font: Palette.Mono),
                    new TextBlock { Text = change.Text, FontFamily = Palette.Mono, FontSize = 12, Foreground = change.Added ? Palette.Edited : Palette.Warning, VerticalAlignment = VerticalAlignment.Center },
                },
            });
        }

        return new Border { Background = Palette.Raised, CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8), Child = rows };
    }

    private Control BuildStep(OutlineStep step)
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

        StackPanel block = new() { Spacing = 3 };
        block.Children.Add(Pickable(head, _picked.Count == 0 && PickedAnchor == step.Anchor, _ => PickStep(step.Anchor)));

        foreach (OutlineCommand command in step.Commands)
        {
            block.Children.Add(BuildCommand(command));
        }

        if (step.Others.Count > 0 || step.Unknown.Count > 0)
        {
            IEnumerable<string> parts = step.Others
                .OrderBy(o => o.Key)
                .Select(o => $"{CommandKinds.Label(o.Key)} {o.Value}")
                .Concat(step.Unknown.Count > 0 ? [$"커맨드 표에 없는 것 {step.Unknown.Count}({string.Join(" · ", step.Unknown.Distinct())})"] : []);

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

    private Control BuildCommand(OutlineCommand command)
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

        foreach (OutlineArg arg in command.Args.Where(a => a.Value.Length > 0))
        {
            // 적지 않아 생략값으로 가는 칸은 흐리게 — 값은 같지만 대본에는 없다.
            row.Children.Add(Palette.Chip($"{arg.Label} {arg.Value}", arg.Written ? null : Palette.Faint, arg.Written ? null : Brushes.Transparent));
        }

        Control pickable = Pickable(row, _picked.Contains(command.Address), modifiers => PickCommand(command.Address, modifiers));
        ToolTip.SetTip(pickable, command.Summary);
        return pickable;
    }

    /// <summary>눌러 고르는 줄 — 고른 것은 바탕이 칠해진다.</summary>
    private static Control Pickable(Control content, bool picked, Action<KeyModifiers> pick)
    {
        Border border = new()
        {
            // 투명한 바탕이 있어야 빈 곳을 눌러도 잡힌다.
            Background = picked ? Palette.Picked : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = content,
        };

        border.PointerPressed += (_, e) => pick(e.KeyModifiers);
        return border;
    }

    /// <summary>
    /// 커맨드를 고른다 — 그냥 누르면 그것 하나, Ctrl이면 고른 것에 더하거나 빼고, Shift면 마지막에 고른 것부터 여기까지.
    /// </summary>
    internal void PickCommand(CommandAddress address, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            if (!_picked.Remove(address))
            {
                _picked.Add(address);
            }
        }
        else if (modifiers.HasFlag(KeyModifiers.Shift) && PickedCommand is { } last && ShownOutline is { } outline)
        {
            List<CommandAddress> order = outline.Steps.SelectMany(s => s.Commands).Select(c => c.Address).ToList();
            int from = order.IndexOf(last);
            int to = order.IndexOf(address);

            if (from >= 0 && to >= 0)
            {
                foreach (CommandAddress between in order.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1))
                {
                    _picked.Remove(between);
                    _picked.Add(between);
                }

                // 누른 것이 마지막(칸으로 서는 것)이 되게 한다.
                _picked.Remove(address);
                _picked.Add(address);
            }
        }
        else
        {
            _picked.Clear();
            _picked.Add(address);
        }

        PickedAnchor = null;
        _editMessage = null;
        ShowSelectedLine();
    }

    private void PickStep(string anchor)
    {
        _picked.Clear();
        PickedAnchor = anchor;
        _editMessage = null;
        ShowSelectedLine();
    }

    /// <summary>고른 것을 모두 놓는다.</summary>
    internal void ClearPicks()
    {
        _picked.Clear();
        PickedAnchor = null;
        _editMessage = null;
        ShowSelectedLine();
    }

    /// <summary>고치기 전과 맞대어 보기를 켜고 끈다.</summary>
    internal void SetComparing(bool on)
    {
        _comparing = on;
        ShowSelectedLine();
    }

    private static string AnchorLabel(LineAnchorKind kind) => kind switch
    {
        LineAnchorKind.Pre => "대사창이 열리기 전",
        LineAnchorKind.Start => "대사창이 열리며",
        LineAnchorKind.Text => "이 글자에 닿을 때",
        _ => "글이 다 보인 뒤",
    };
}
