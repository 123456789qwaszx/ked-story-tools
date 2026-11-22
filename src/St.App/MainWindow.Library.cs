using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

public partial class MainWindow
{
    private const int ExcerptLength = 34;

    private readonly ComboBox _chapters = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _scenes = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _episodes = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _search = new() { PlaceholderText = "이 장면에서 줄 찾기 — 대사 글 · 화자 · LineId" };
    private readonly Dictionary<CommandKind, ToggleButton> _kindButtons = [];
    private readonly CheckBox _onlyWithKind = new() { Content = "고른 종류가 있는 줄만", FontSize = 12 };
    private readonly ListBox _lines = new();
    private readonly TextBlock _listCount = new() { FontSize = 11, Foreground = Palette.Dim };

    private bool _refreshing;

    // 접어 둔 에피소드(Id) — 머리만 보인다.
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    // 마지막으로 고른 줄 — 에피소드의 머리를 눌러 접고 펼 때 고른 줄을 돌려놓는다.
    private LibraryLine? _currentLine;

    /// <summary>목록의 에피소드 머리 — 그 아래에 그 에피소드의 줄이 선다.</summary>
    private sealed record EpisodeHeader(StoryEpisode Episode, int Total, bool Collapsed);

    /// <summary>종류 필터에서 켜진 종류. 처음에는 모두 켜져 있다.</summary>
    internal IReadOnlySet<CommandKind> Kinds
        => _kindButtons.Where(b => b.Value.IsChecked == true).Select(b => b.Key).ToHashSet();

    /// <summary>고른 챕터. 대본을 열지 않았으면 null.</summary>
    internal StoryChapter? Chapter
        => Library is not null && _chapters.SelectedIndex >= 0 && _chapters.SelectedIndex < Library.Chapters.Count
            ? Library.Chapters[_chapters.SelectedIndex]
            : null;

    /// <summary>고른 장면 — 그 장면의 에피소드가 한 번에 목록에 선다.</summary>
    internal StoryScene? Scene
        => Chapter is { } chapter && _scenes.SelectedIndex >= 0 && _scenes.SelectedIndex < chapter.Scenes.Count
            ? chapter.Scenes[_scenes.SelectedIndex]
            : null;

    /// <summary>하나만 골라 보는 에피소드. "전체"면 null.</summary>
    internal StoryEpisode? Episode
        => Scene is { } scene && _episodes.SelectedIndex >= 1 && _episodes.SelectedIndex <= scene.Episodes.Count
            ? scene.Episodes[_episodes.SelectedIndex - 1]
            : null;

    /// <summary>목록에 선 에피소드 — 찾기와 필터를 지난 줄만 든 채로.</summary>
    internal IReadOnlyList<StoryEpisode> ListedEpisodes { get; private set; } = [];

    /// <summary>목록에 보이는 줄 — 에피소드의 차례대로.</summary>
    internal IReadOnlyList<LibraryLine> ListedLines => ListedEpisodes.SelectMany(e => e.Lines).ToList();

    private Control BuildLibraryPanel()
    {
        _chapters.SelectionChanged += (_, _) => OnChapterPicked();
        _scenes.SelectionChanged += (_, _) => OnScenePicked();
        _episodes.SelectionChanged += (_, _) => RefreshLibrary();
        _search.TextChanged += (_, _) => RefreshLibrary();
        _onlyWithKind.IsCheckedChanged += (_, _) => RefreshLibrary();
        _lines.SelectionChanged += (_, _) =>
        {
            if (_refreshing)
            {
                return;
            }

            // 에피소드의 머리는 고르는 줄이 아니다 — 누르면 접고 편다.
            if (_lines.SelectedItem is EpisodeHeader header)
            {
                ToggleEpisode(header.Episode.Id);
                return;
            }

            _currentLine = _lines.SelectedItem as LibraryLine;
            ShowSelectedLine();
        };

        // 목록을 갈아 끼우는 사이에 틀이 빈 항목으로 불릴 수 있다.
        _lines.ItemTemplate = new FuncDataTemplate<object>(
            (item, _) => item switch
            {
                EpisodeHeader header => BuildEpisodeRow(header),
                LibraryLine line => BuildLineRow(line, _session?.IsEdited(line) == true),
                _ => new Control(),
            },
            supportsRecycling: false);

        // 종류가 여섯이라 왼쪽 칸이 좁으면 다음 줄로 넘긴다.
        WrapPanel kinds = new() { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };

        foreach (CommandKind kind in DirectorKinds)
        {
            ToggleButton button = new()
            {
                IsChecked = true,
                Padding = new Thickness(8, 3),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children = { Palette.Dot(kind), new TextBlock { Text = CommandKinds.Label(kind), FontSize = 12 } },
                },
            };

            button.IsCheckedChanged += (_, _) =>
            {
                RefreshLibrary();
                ShowSelectedLine();
            };

            _kindButtons.Add(kind, button);
            kinds.Children.Add(button);
        }

        Grid pickers = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        AddPicker(pickers, 0, "챕터", _chapters);
        AddPicker(pickers, 1, "장면", _scenes);
        AddPicker(pickers, 2, "에피소드", _episodes);

        StackPanel head = new()
        {
            Spacing = 6,
            Margin = new Thickness(8, 8, 8, 4),
            Children = { pickers, _search, kinds, _onlyWithKind, _listCount },
        };
        DockPanel.SetDock(head, Dock.Top);

        return new DockPanel { Children = { head, _lines } };
    }

    private static void AddPicker(Grid grid, int row, string label, ComboBox picker)
    {
        TextBlock caption = new()
        {
            Text = label,
            FontSize = 12,
            Foreground = Palette.Dim,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        picker.Margin = new Thickness(0, 0, 0, 4);

        Grid.SetRow(caption, row);
        Grid.SetRow(picker, row);
        Grid.SetColumn(picker, 1);
        grid.Children.Add(caption);
        grid.Children.Add(picker);
    }

    /// <summary>대본을 새로 열었다 — 챕터의 목록을 채우고 첫 챕터 · 첫 장면을 고른다.</summary>
    private void ResetPickers()
    {
        _refreshing = true;

        try
        {
            _chapters.ItemsSource = Library?.Chapters.Select(c => $"{c.Name}  ·  장면 {c.Scenes.Count}").ToList() ?? [];
            _chapters.SelectedIndex = Library is { Chapters.Count: > 0 } ? 0 : -1;
            FillScenes();
        }
        finally
        {
            _refreshing = false;
        }

        RefreshLibrary();
    }

    private void OnChapterPicked()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            FillScenes();
        }
        finally
        {
            _refreshing = false;
        }

        RefreshLibrary();
    }

    private void OnScenePicked()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            FillEpisodes();
        }
        finally
        {
            _refreshing = false;
        }

        RefreshLibrary();
    }

    private void FillScenes(string? keepScene = null, string? keepEpisode = null)
    {
        _scenes.ItemsSource = Chapter?.Scenes.Select(s => $"{s.Name}  ·  에피소드 {s.Episodes.Count} · {s.LineCount}줄").ToList() ?? [];

        int kept = keepScene is null ? -1 : Chapter?.Scenes.ToList().FindIndex(s => s.Id == keepScene) ?? -1;
        _scenes.SelectedIndex = kept >= 0 ? kept : Chapter is { Scenes.Count: > 0 } ? 0 : -1;
        FillEpisodes(kept >= 0 ? keepEpisode : null);
    }

    private void FillEpisodes(string? keepEpisode = null)
    {
        List<string> names = ["전체"];
        names.AddRange(Scene?.Episodes.Select(e => $"{e.Title}  ·  {(e.Missing ? "대본에 없다" : $"{e.Lines.Count}줄")}") ?? []);
        _episodes.ItemsSource = names;

        int kept = keepEpisode is null ? -1 : Scene?.Episodes.ToList().FindIndex(e => e.Id == keepEpisode) ?? -1;
        _episodes.SelectedIndex = kept + 1;
    }

    /// <summary>
    /// 대본을 새로 읽었다(고쳤거나 · 되돌렸거나 · 바깥에서 바뀌었다) — 고르고 있던 챕터 · 장면 · 에피소드 · 줄을 그대로 다시 고른다.
    /// </summary>
    private void ReloadKeepingSelection()
    {
        string? chapter = Chapter?.Id;
        string? scene = Scene?.Id;
        string? episode = Episode?.Id;
        string? line = (_lines.SelectedItem as LibraryLine)?.Id;

        // 목록을 갈아 끼우는 동안 고른 줄이 잠깐 비고, 그때 가운데가 고른 커맨드를 놓는다 — 쥐고 있다가 돌려준다.
        (List<CommandAddress> commands, string? anchor, string? shown) = (_picked.ToList(), PickedAnchor, _shownLine);

        _refreshing = true;

        try
        {
            _chapters.ItemsSource = Library?.Chapters.Select(c => $"{c.Name}  ·  장면 {c.Scenes.Count}").ToList() ?? [];

            int kept = chapter is null ? -1 : Library?.Chapters.ToList().FindIndex(c => c.Id == chapter) ?? -1;
            _chapters.SelectedIndex = kept >= 0 ? kept : Library is { Chapters.Count: > 0 } ? 0 : -1;
            FillScenes(kept >= 0 ? scene : null, episode);
        }
        finally
        {
            _refreshing = false;
        }

        RefreshLibrary();

        if (line is not null)
        {
            _picked.Clear();
            _picked.AddRange(commands);
            (PickedAnchor, _shownLine) = (anchor, shown);
            SelectLine(line);
        }
    }

    /// <summary>고른 장면을 찾기와 필터로 걸러 목록을 다시 채운다. 고른 줄이 남아 있으면 그대로 고른 채다.</summary>
    private void RefreshLibrary()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            LibraryLine? selected = _lines.SelectedItem as LibraryLine ?? _currentLine;
            StoryScene? scene = Scene;

            ListedEpisodes = scene?.Search(_search.Text, _onlyWithKind.IsChecked == true ? Kinds : null, Episode?.Id) ?? [];

            List<object> items = [];

            foreach (StoryEpisode episode in ListedEpisodes)
            {
                bool collapsed = _collapsed.Contains(episode.Id);
                items.Add(new EpisodeHeader(episode, scene!.Episodes.First(e => e.Id == episode.Id).Lines.Count, collapsed));

                if (!collapsed)
                {
                    items.AddRange(episode.Lines);
                }
            }

            _lines.ItemsSource = items;
            _lines.SelectedItem = _currentLine = selected is not null && items.Contains(selected) ? selected : null;

            int shown = ListedEpisodes.Sum(e => e.Lines.Count);
            _listCount.Text = scene is null ? string.Empty : $"{shown}줄 / 이 장면 {scene.LineCount}줄 · 에피소드 {ListedEpisodes.Count}/{scene.Episodes.Count}";
            _status.Text = Library switch
            {
                null => "대본 폴더를 연다 — 챕터와 장면을 고르면 그 장면의 에피소드가 한 번에 선다",
                { Problems.Count: > 0 } => $"문제 {Library.Problems.Count}개 — {Library.Problems[0]}",
                _ => $"{Library.Root} — 챕터 {Library.Chapters.Count}개 · 대사 줄 {Library.LineCount}개",
            };
            RefreshUndoButtons();
        }
        finally
        {
            _refreshing = false;
        }

        ShowSelectedLine();
    }

    private static Control BuildEpisodeRow(EpisodeHeader header)
    {
        StoryEpisode episode = header.Episode;
        string count = episode.Missing ? "대본에 이 노드가 없다"
            : episode.Lines.Count == header.Total ? $"{header.Total}줄"
            : $"{episode.Lines.Count} / {header.Total}줄";

        return new Border
        {
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 6, 0, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = header.Collapsed ? "▸" : "▾", FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = episode.Title, FontWeight = FontWeight.SemiBold, FontSize = 12, Foreground = Palette.Dim },
                    new TextBlock { Text = episode.NodeName, FontFamily = Palette.Mono, FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = count, FontSize = 11, Foreground = episode.Missing ? Palette.Warning : Palette.Faint, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
    }

    private static Control BuildLineRow(LibraryLine line, bool edited)
    {
        string body = line.Read.Body.Length <= ExcerptLength ? line.Read.Body : string.Concat(line.Read.Body.AsSpan(0, ExcerptLength), "…");

        StackPanel first = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

        if (line.Read.Speaker.Length > 0)
        {
            first.Children.Add(new TextBlock { Text = line.Read.Speaker, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        }

        first.Children.Add(new TextBlock { Text = body, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });

        StackPanel second = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        second.Children.Add(new TextBlock
        {
            Text = line.Id ?? "LineId 없음",
            FontFamily = Palette.Mono,
            FontSize = 11,
            Foreground = line.Id is null ? Palette.Warning : Palette.Faint,
            VerticalAlignment = VerticalAlignment.Center,
        });

        foreach (CommandKind kind in DirectorKinds)
        {
            int count = line.Count(kind);

            if (count == 0)
            {
                continue;
            }

            second.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Children = { Palette.Dot(kind, 7), new TextBlock { Text = count.ToString(), FontSize = 11, Foreground = Palette.Dim } },
            });
        }

        if (!line.Editable)
        {
            second.Children.Add(new TextBlock { Text = "읽기만", FontSize = 11, Foreground = Palette.Warning });
        }

        if (edited)
        {
            second.Children.Add(new TextBlock { Text = "● 고침", FontSize = 11, Foreground = Palette.Edited });
        }

        return new StackPanel { Spacing = 2, Margin = new Thickness(0, 2), Children = { first, second } };
    }

    // ── 테스트 손잡이 ───────────────────────────────────────────────────────

    /// <summary>챕터를 Id로 고른다. 없으면 false.</summary>
    internal bool SelectChapter(string chapterId)
    {
        int index = Library?.Chapters.ToList().FindIndex(c => c.Id == chapterId) ?? -1;
        _chapters.SelectedIndex = index;
        return index >= 0;
    }

    /// <summary>고른 챕터의 장면을 Id로 고른다. 없으면 false.</summary>
    internal bool SelectScene(string sceneId)
    {
        int index = Chapter?.Scenes.ToList().FindIndex(s => s.Id == sceneId) ?? -1;
        _scenes.SelectedIndex = index;
        return index >= 0;
    }

    /// <summary>에피소드 하나만 본다. null이면 장면의 에피소드 전체.</summary>
    internal bool SelectEpisode(string? episodeId)
    {
        int index = episodeId is null ? 0 : (Scene?.Episodes.ToList().FindIndex(e => e.Id == episodeId) ?? -1) + 1;
        _episodes.SelectedIndex = index;
        return index > 0 || episodeId is null;
    }

    // TextChanged는 디스패처를 한 번 돈 뒤에 온다 — 테스트가 바로 읽을 수 있게 여기서 채운다.
    internal void SearchFor(string text)
    {
        _search.Text = text;
        RefreshLibrary();
    }

    internal void SetKinds(params CommandKind[] kinds)
    {
        foreach ((CommandKind kind, ToggleButton button) in _kindButtons)
        {
            button.IsChecked = kinds.Contains(kind);
        }
    }

    internal void SetOnlyWithKind(bool on) => _onlyWithKind.IsChecked = on;

    /// <summary>LineId로 목록의 줄을 고른다. 목록에 없으면 false.</summary>
    internal bool SelectLine(string lineId)
    {
        LibraryLine? line = VisibleLines.FirstOrDefault(l => l.Id == lineId);
        _lines.SelectedItem = line;
        return line is not null;
    }

    /// <summary>목록에 실제로 선 줄 — 접은 에피소드의 줄은 빠진다.</summary>
    internal IReadOnlyList<LibraryLine> VisibleLines => (_lines.ItemsSource as IEnumerable<object>)?.OfType<LibraryLine>().ToList() ?? [];

    /// <summary>에피소드를 접거나 편다 — 접으면 머리만 남는다. 고른 줄이 그 안에 있었으면 놓는다.</summary>
    internal void ToggleEpisode(string episodeId)
    {
        if (!_collapsed.Remove(episodeId))
        {
            _collapsed.Add(episodeId);
        }

        RefreshLibrary();
    }

    /// <summary>접어 둔 에피소드인가.</summary>
    internal bool IsCollapsed(string episodeId) => _collapsed.Contains(episodeId);

    /// <summary>
    /// 목록에서 고른 줄의 앞(-1)이나 뒤(+1) 줄로 옮긴다. <paramref name="withKind"/>이면 켜 둔 종류의 커맨드가 있는 줄까지 건너뛴다.
    /// 더 갈 데가 없으면 false.
    /// </summary>
    internal bool MoveLine(int direction, bool withKind = false)
    {
        IReadOnlyList<LibraryLine> lines = VisibleLines;
        IReadOnlySet<CommandKind> kinds = Kinds;
        int index = _lines.SelectedItem is LibraryLine now ? lines.ToList().IndexOf(now) : direction > 0 ? -1 : lines.Count;

        for (int i = index + direction; i >= 0 && i < lines.Count; i += direction)
        {
            if (!withKind || kinds.Any(k => lines[i].Count(k) > 0))
            {
                _lines.SelectedItem = lines[i];
                _lines.ScrollIntoView(lines[i]);
                return true;
            }
        }

        return false;
    }
}
