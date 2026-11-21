using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Reading;

namespace St.App;

public partial class MainWindow
{
    private const int ExcerptLength = 34;

    private readonly TextBox _search = new() { PlaceholderText = "줄 찾기 — 대사 글 · 화자 · LineId" };
    private readonly Dictionary<CommandKind, ToggleButton> _kindButtons = [];
    private readonly CheckBox _onlyWithKind = new() { Content = "고른 종류가 있는 줄만", FontSize = 12 };
    private readonly ListBox _lines = new();
    private readonly TextBlock _listCount = new() { FontSize = 11, Foreground = Palette.Dim };

    private bool _refreshing;

    /// <summary>종류 필터에서 켜진 종류. 처음에는 넷 모두다.</summary>
    internal IReadOnlySet<CommandKind> Kinds
        => _kindButtons.Where(b => b.Value.IsChecked == true).Select(b => b.Key).ToHashSet();

    /// <summary>목록에 보이는 줄 — 찾기와 필터를 지난 것.</summary>
    internal IReadOnlyList<LibraryLine> ListedLines => (IReadOnlyList<LibraryLine>?)_lines.ItemsSource ?? [];

    private Control BuildLibraryPanel()
    {
        _search.TextChanged += (_, _) => RefreshLibrary();
        _onlyWithKind.IsCheckedChanged += (_, _) => RefreshLibrary();
        _lines.SelectionChanged += (_, _) => ShowSelectedLine();
        // 목록을 갈아 끼우는 사이에 틀이 빈 항목으로 불릴 수 있다.
        _lines.ItemTemplate = new FuncDataTemplate<LibraryLine>(
            (line, _) => line is null ? new Control() : BuildLineRow(line), supportsRecycling: false);

        StackPanel kinds = new() { Orientation = Orientation.Horizontal, Spacing = 4 };

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

        StackPanel head = new() { Spacing = 6, Margin = new Thickness(8, 8, 8, 4), Children = { _search, kinds, _onlyWithKind, _listCount } };
        DockPanel.SetDock(head, Dock.Top);

        return new DockPanel { Children = { head, _lines } };
    }

    /// <summary>찾기와 필터로 목록을 다시 채운다. 고른 줄이 남아 있으면 그대로 고른 채다.</summary>
    private void RefreshLibrary()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            LibraryLine? selected = _lines.SelectedItem as LibraryLine;
            IReadOnlyList<LibraryLine> found = _library?.Search(_search.Text, _onlyWithKind.IsChecked == true ? Kinds : null) ?? [];

            _lines.ItemsSource = found;
            _lines.SelectedItem = selected is not null && found.Contains(selected) ? selected : null;

            _listCount.Text = _library is null ? string.Empty : $"{found.Count}줄 / 전체 {_library.Lines.Count}줄 · 파일 {_library.Files.Count}개";
            _status.Text = _library switch
            {
                null => "대본 폴더를 연다 — 줄을 고르면 스텝과 커맨드가 종류별로 보인다",
                { Problems.Count: > 0 } => $"읽지 못한 파일 {_library.Problems.Count}개 — {_library.Problems[0]}",
                _ => $"{_library.Root} — 대사 줄 {_library.Lines.Count}개",
            };
        }
        finally
        {
            _refreshing = false;
        }

        ShowSelectedLine();
    }

    private static Control BuildLineRow(LibraryLine line)
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

        return new StackPanel { Spacing = 2, Margin = new Thickness(0, 2), Children = { first, second } };
    }

    // ── 테스트 손잡이 ───────────────────────────────────────────────────────

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
        LibraryLine? line = ListedLines.FirstOrDefault(l => l.Id == lineId);
        _lines.SelectedItem = line;
        return line is not null;
    }
}
