using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using St.App.Views;
using St.Authoring.Composing;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 층의 할 일(K8 I4 — docs/PLAN.md §2.5 C7) — 연출 층 폴더를 열었을 때만 선다.
/// 줄 목록의 딱지(다시 볼 것 · 박자 다시 놓을 것 · 연출 없음)와 그것으로 거르는 "할 일" 필터, 고른 줄의 옛 글과 새 글 · 「확인」,
/// 손으로 고친 게임 yarn의 「덮어쓰기」(C6). 상태는 파일에 적지 않는다 — 두 층을 견주어 나온다(<see cref="EditSession.StateOf"/>).
/// </summary>
public partial class MainWindow
{
    /// <summary>줄 하나의 할 일 — "할 일" 필터의 단추이기도 하다.</summary>
    internal enum Todo
    {
        /// <summary>작가가 글이나 화자를 고쳤다.</summary>
        Review,

        /// <summary>글이 바뀌어 말 도중의 박자가 줄 끝으로 갔다 — 다시 볼 것에 딸린다.</summary>
        Beats,

        /// <summary>커맨드가 하나도 없는 줄.</summary>
        NoStaging,
    }

    private static readonly (Todo Todo, string Label)[] TodoLabels =
    [
        (Todo.Review, "다시 볼 것"),
        (Todo.Beats, "박자 다시 놓을 것"),
        (Todo.NoStaging, "연출 없음"),
    ];

    private readonly Dictionary<Todo, (ToggleButton Button, TextBlock Text)> _todoButtons = [];
    private readonly WrapPanel _todoRow = new() { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4, IsVisible = false };
    private readonly TextBlock _heldText = new() { FontSize = 12, Foreground = Palette.Warning, TextWrapping = TextWrapping.Wrap };
    private readonly Button _overwrite = new() { Content = "덮어쓰기", FontSize = 12, Padding = new Thickness(8, 3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _heldBar = new() { Spacing = 4, IsVisible = false };

    /// <summary>"할 일" 필터에서 켜진 것 — 하나라도 켜면 그 할 일이 있는 줄만 목록에 선다.</summary>
    internal IReadOnlySet<Todo> Todos => _todoButtons.Where(b => b.Value.Button.IsChecked == true).Select(b => b.Key).ToHashSet();

    /// <summary>고른 장면에서 그 할 일이 있는 줄의 수 — 단추에 적힌 수.</summary>
    internal IReadOnlyDictionary<Todo, int> TodoCounts { get; private set; } = new Dictionary<Todo, int>();

    /// <summary>가운데에 편 줄의 다시 볼 것 — 옛 글과 새 글. 없으면 null.</summary>
    internal LineCompare? ShownReview { get; private set; }

    /// <summary>"할 일" 필터가 서 있는가 — 연출 층을 열었을 때만.</summary>
    internal bool TodoShown => _todoRow.IsVisible;

    /// <summary>덮어쓰기 단추가 서 있는가 — 손으로 고친 게임 yarn에 쓰지 못했을 때만.</summary>
    internal bool OverwriteShown => _heldBar.IsVisible && _overwrite.IsVisible;

    /// <summary>"할 일" 필터와 덮어쓰기의 자리 — 줄 목록의 머리에 든다.</summary>
    private IEnumerable<Control> BuildTodoControls()
    {
        foreach ((Todo todo, string label) in TodoLabels)
        {
            TextBlock text = new() { Text = label, FontSize = 12 };
            ToggleButton button = new() { Padding = new Thickness(8, 3), Content = text };
            button.IsCheckedChanged += (_, _) => RefreshLibrary();
            _todoButtons.Add(todo, (button, text));
            _todoRow.Children.Add(button);
        }

        _overwrite.Click += (_, _) => OverwriteGame();
        _heldBar.Children.Add(_heldText);
        _heldBar.Children.Add(_overwrite);
        return [_todoRow, _heldBar];
    }

    private bool Has(LibraryLine line, Todo todo)
        => _session?.StateOf(line) is { } state && todo switch
        {
            Todo.Review => state.Review is not null,
            Todo.Beats => state.BeatsMoved,
            _ => state.NoStaging,
        };

    /// <summary>켜 둔 할 일이 있는 줄만 남긴다 — 켠 것이 없으면 그대로.</summary>
    private IReadOnlyList<StoryEpisode> FilterTodo(IReadOnlyList<StoryEpisode> episodes)
    {
        IReadOnlySet<Todo> todos = Todos;

        if (_session?.Layers is null || todos.Count == 0)
        {
            return episodes;
        }

        return episodes
            .Select(e => e with { Lines = e.Lines.Where(l => todos.Any(t => Has(l, t))).ToList() })
            .Where(e => e.Lines.Count > 0)
            .ToList();
    }

    /// <summary>단추의 수와 덮어쓰기의 자리를 지금의 장면 · 지금의 합치기에 맞춘다.</summary>
    private void RefreshTodo(StoryScene? scene)
    {
        bool layered = _session?.Layers is not null;
        _todoRow.IsVisible = layered;

        List<LibraryLine> lines = layered && scene is not null ? scene.Episodes.SelectMany(e => e.Lines).ToList() : [];
        Dictionary<Todo, int> counts = [];

        foreach ((Todo todo, string label) in TodoLabels)
        {
            int count = lines.Count(l => Has(l, todo));
            counts[todo] = count;
            _todoButtons[todo].Text.Text = $"{label} {count}";
            _todoButtons[todo].Text.Foreground = count > 0 ? Palette.Warning : Palette.Faint;
        }

        TodoCounts = counts;

        _heldBar.IsVisible = _session?.ComposeProblem is not null;
        _heldText.Text = _session?.ComposeProblem;
        _overwrite.IsVisible = _session?.Composed?.Held == true;
    }

    /// <summary>줄 목록의 딱지.</summary>
    private void AddTodoBadges(StackPanel row, LibraryLine line)
    {
        if (_session?.StateOf(line) is not { } state)
        {
            return;
        }

        if (state.Review is not null)
        {
            row.Children.Add(new TextBlock { Text = state.BeatsMoved ? "다시 볼 것 · 박자" : "다시 볼 것", FontSize = 11, Foreground = Palette.Warning });
        }

        if (state.NoStaging)
        {
            row.Children.Add(new TextBlock { Text = "연출 없음", FontSize = 11, Foreground = Palette.Dim });
        }
    }

    /// <summary>고른 줄의 다시 볼 것 — 옛 글과 새 글을 나란히, 그 아래 「확인」. 볼 것이 없으면 null.</summary>
    private Control? BuildReview(LibraryLine line)
    {
        ShownReview = _session?.StateOf(line)?.Review;

        if (ShownReview is not { } review)
        {
            return null;
        }

        Grid texts = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        AddText(texts, 0, "옛 글", review.OldText, Palette.Dim);
        AddText(texts, 1, "새 글", review.NewText, null);

        Button confirm = new() { Content = "확인 — 새 글로", FontSize = 12, Padding = new Thickness(10, 4), HorizontalAlignment = HorizontalAlignment.Left };
        confirm.Click += (_, _) => ConfirmLine();

        StackPanel panel = new()
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "작가가 이 줄을 고쳤다 — 게임에는 새 글이 들어갔고, 연출은 옛 글에 맞춘 그대로다", FontSize = 12, Foreground = Palette.Warning, TextWrapping = TextWrapping.Wrap },
                texts,
            },
        };

        if (review.BeatsMoved)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "말 도중의 박자는 자리를 잃어 줄 끝으로 갔다 — 확인하면 거기에 굳는다",
                FontSize = 12,
                Foreground = Palette.Dim,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        panel.Children.Add(confirm);

        return new Border
        {
            BorderBrush = Palette.Warning,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Child = panel,
        };

        static void AddText(Grid grid, int row, string label, string text, IBrush? brush)
        {
            TextBlock caption = new() { Text = label, FontSize = 11, Foreground = Palette.Faint, Margin = new Thickness(0, 2, 10, 2) };
            TextBlock body = new() { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2) };

            if (brush is not null)
            {
                body.Foreground = brush;
            }

            Grid.SetRow(caption, row);
            Grid.SetRow(body, row);
            Grid.SetColumn(body, 1);
            grid.Children.Add(caption);
            grid.Children.Add(body);
        }
    }

    // ── 조작 · 테스트 손잡이 ────────────────────────────────────────────────

    /// <summary>"할 일" 필터를 그 할 일만 켠 것으로 맞춘다 — 주지 않으면 모두 끈다.</summary>
    internal void SetTodos(params Todo[] todos)
    {
        foreach ((Todo todo, (ToggleButton button, _)) in _todoButtons)
        {
            button.IsChecked = todos.Contains(todo);
        }
    }

    /// <summary>고른 줄을 확인한다 — 연출 층의 대사를 새 글로 갈아 적는다(되돌리기 한 칸). 볼 것이 없었으면 false.</summary>
    internal bool ConfirmLine()
    {
        if (_session is null || _lines.SelectedItem is not LibraryLine line)
        {
            return false;
        }

        bool beats = _session.StateOf(line)?.BeatsMoved == true;
        EditOutcome outcome = _session.Accept(line);

        if (!outcome.Ok)
        {
            _status.Text = $"확인하지 못했다 — {DirectorText.Clean(outcome.Error)}";
            return false;
        }

        if (!outcome.Changed)
        {
            return false;
        }

        ReloadKeepingSelection();
        _status.Text = beats
            ? "확인했다 — 말 도중의 박자는 줄 끝에 남았다 · 되돌리기 Ctrl+Z"
            : "확인했다 — 연출 층의 대사를 새 글로 갈았다 · 되돌리기 Ctrl+Z";
        return true;
    }

    /// <summary>손으로 고친 게임 yarn을 덮어 다시 합친다(C6). 다 들어갔으면 true.</summary>
    internal bool OverwriteGame()
    {
        if (_session?.Layers is null)
        {
            return false;
        }

        bool done = _session.Overwrite();
        ReloadKeepingSelection();
        _status.Text = done ? "게임 yarn을 덮어썼다 — 손으로 고친 것은 사라졌다" : _session.ComposeProblem ?? string.Empty;
        return done;
    }

    // ── 보관소 (K8 I6 · C3) ─────────────────────────────────────────────────

    private bool _shelfOpen;

    /// <summary>보관소의 줄 — 대사 층에서 사라진 줄과 그 연출. 층이 없으면 빈 목록.</summary>
    internal IReadOnlyList<EditSession.ShelfEntry> ShelfLines { get; private set; } = [];

    /// <summary>
    /// 보관소의 칸 — 고른 줄 아래에 접힌 채 선다. 펴면 보관한 줄마다 옛 글과 「이 줄로 옮기기」.
    /// 작가가 줄을 지우고 새 줄로 다시 썼을 때(줄 ID가 달라 저절로 돌아오지 않는다) 연출을 새 줄로 겨눈다.
    /// </summary>
    private Control? BuildShelf()
    {
        ShelfLines = _session?.Shelved() ?? [];

        if (ShelfLines.Count == 0)
        {
            return null;
        }

        Button toggle = new()
        {
            Content = $"{(_shelfOpen ? "▾" : "▸")} 보관소 {ShelfLines.Count} — 대사 층에서 사라진 줄의 연출",
            FontSize = 12,
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        toggle.Click += (_, _) => SetShelfOpen(!_shelfOpen);

        StackPanel panel = new() { Spacing = 8, Children = { toggle } };

        if (_shelfOpen)
        {
            foreach (EditSession.ShelfEntry entry in ShelfLines)
            {
                ShelvedLine shelved = entry.Line;
                Button adopt = new() { Content = "이 줄로 옮기기", FontSize = 12, Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center };
                adopt.Click += (_, _) => AdoptShelved(CommandAddress.ShortLineId(shelved.LineId));

                StackPanel text = new()
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = shelved.Speaker.Length > 0 ? $"{shelved.Speaker}  {shelved.Body}" : shelved.Body,
                            FontSize = 13,
                            Foreground = Palette.Dim,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new TextBlock
                        {
                            Text = $"{CommandAddress.ShortLineId(shelved.LineId)} · {shelved.Node} · 커맨드 {shelved.Commands}개",
                            FontFamily = Palette.Mono,
                            FontSize = 11,
                            Foreground = Palette.Faint,
                        },
                    },
                };

                DockPanel row = new();
                DockPanel.SetDock(adopt, Dock.Right);
                row.Children.Add(adopt);
                row.Children.Add(text);
                panel.Children.Add(row);
            }

            panel.Children.Add(new TextBlock
            {
                Text = "고른 줄의 연출 뒤에 덧붙는다. 묶음이 가리키는 박자가 그 줄에 없으면 표지가 줄 끝에 선다 — 박자 놓기로 다시 놓는다",
                FontSize = 11,
                Foreground = Palette.Faint,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Child = panel };
    }

    /// <summary>보관소의 칸을 펴거나 접는다.</summary>
    internal void SetShelfOpen(bool open)
    {
        _shelfOpen = open;
        ShowSelectedLine();
    }

    /// <summary>보관소의 그 줄(LineId)의 연출을 고른 줄로 옮긴다 — 되돌리기 한 칸. 옮기지 못했으면 false.</summary>
    internal bool AdoptShelved(string lineId)
    {
        if (_session is null || _lines.SelectedItem is not LibraryLine line
            || ShelfLines.FirstOrDefault(e => CommandAddress.ShortLineId(e.Line.LineId) == lineId) is not { } entry)
        {
            return false;
        }

        EditOutcome outcome = _session.Adopt(line, entry);

        if (outcome is not { Ok: true, Changed: true })
        {
            _status.Text = $"옮기지 못했다 — {DirectorText.Clean(outcome.Error)}";
            return false;
        }

        ReloadKeepingSelection();
        _status.Text = _session.ComposeProblem is { } held
            ? $"연출 층에 썼다 — {held}"
            : "보관소의 연출을 이 줄에 붙였다 — 없던 박자는 줄 끝에 섰다 · 되돌리기 Ctrl+Z";
        return true;
    }
}
