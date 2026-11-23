using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Catalog;

namespace St.App;

/// <summary>
/// 카탈로그 칸(K7-1 — docs/PLAN.md §2.4) — 고른 종류의 어휘 전부와 카드가 늘 보인다(D14). 줄 · 앵커 · 커맨드를 고르지 않아도 선다.
/// 인스펙터 곁의 접히는 기둥이다(U5) — 고른 커맨드의 칸과 함께 보며 바꾼다. 카드는 ID · 이름만 보인다(D17).
/// 무엇을 보일지는 도메인 <see cref="CatalogIndex"/>가 셈한다.
/// </summary>
public partial class MainWindow
{
    private const double DefaultCatalogWidth = 280;

    private readonly StackPanel _catalogList = new() { Spacing = 2 };
    private readonly TextBox _catalogQuery = new() { PlaceholderText = "커맨드 · 카드 찾기 — 이름 · 설명", FontSize = 12 };
    private readonly Dictionary<CommandKind, ToggleButton> _catalogKindButtons = [];
    private readonly ToggleButton _catalogToggle = new() { Content = "카탈로그", IsChecked = true };
    private Control? _catalogColumn;
    private CommandKind _catalogKind = CommandKind.Acting;
    private double _catalogWidth = DefaultCatalogWidth;
    private bool _catalogOpen = true;
    private IReadOnlyList<CatalogEntry> _catalogShown = [];

    /// <summary>카탈로그 칸이 펴져 있는가.</summary>
    internal bool CatalogOpen => _catalogOpen;

    /// <summary>카탈로그 칸이 보이는 종류.</summary>
    internal CommandKind CatalogKind => _catalogKind;

    /// <summary>카탈로그 칸에 지금 보이는 항목 — 커맨드가 먼저, 카드가 뒤.</summary>
    internal IReadOnlyList<CatalogEntry> CatalogShown => _catalogShown;

    /// <summary>카탈로그 칸의 폭 — 접어 두었으면 편 때의 폭.</summary>
    internal double CatalogWidth => _catalogOpen ? _body.ColumnDefinitions[6].Width.Value : _catalogWidth;

    private Control BuildCatalogPanel()
    {
        Button fold = new() { Content = "접기", Padding = new Thickness(8, 2), FontSize = 11 };
        fold.Click += (_, _) => SetCatalogOpen(false);
        ToolTip.SetTip(fold, "F2 — 카탈로그 칸을 접고 편다");

        DockPanel title = new() { LastChildFill = true };
        DockPanel.SetDock(fold, Dock.Right);
        title.Children.Add(fold);
        title.Children.Add(new TextBlock { Text = "카탈로그", FontWeight = FontWeight.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

        WrapPanel kinds = new() { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };

        foreach (CommandKind kind in DirectorKinds)
        {
            ToggleButton button = new()
            {
                IsChecked = kind == _catalogKind,
                Padding = new Thickness(8, 3),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children = { Palette.Dot(kind), new TextBlock { Text = CommandKinds.Label(kind), FontSize = 12 } },
                },
            };

            // 하나만 켜진다 — 누른 것이 켜지고 나머지는 꺼진다. 켜진 것을 다시 눌러도 꺼지지 않는다.
            button.Click += (_, _) => ChooseCatalogKind(kind);
            _catalogKindButtons.Add(kind, button);
            kinds.Children.Add(button);
        }

        _catalogQuery.TextChanged += (_, _) => RefreshCatalogColumn();

        StackPanel head = new() { Spacing = 6, Margin = new Thickness(10, 8, 10, 6), Children = { title, kinds, _catalogQuery } };
        DockPanel.SetDock(head, Dock.Top);

        _catalogToggle.IsCheckedChanged += (_, _) => SetCatalogOpen(_catalogToggle.IsChecked == true);
        ToolTip.SetTip(_catalogToggle, "F2 — 있는 커맨드와 카드를 보는 칸을 접고 편다");

        DockPanel panel = new()
        {
            Children =
            {
                head,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new Border { Padding = new Thickness(10, 0, 10, 10), Child = _catalogList },
                },
            },
        };

        _catalogColumn = new Border { BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = Palette.Line, Child = panel };
        RefreshCatalogColumn();
        return _catalogColumn;
    }

    /// <summary>카탈로그 칸이 보일 종류를 고른다.</summary>
    internal void ChooseCatalogKind(CommandKind kind)
    {
        _catalogKind = kind;

        foreach ((CommandKind each, ToggleButton button) in _catalogKindButtons)
        {
            button.IsChecked = each == kind;
        }

        RefreshCatalogColumn();
    }

    // TextChanged는 디스패처를 한 번 돈 뒤에 온다 — 테스트가 바로 읽을 수 있게 여기서 채운다(줄 찾기의 SearchFor와 같다).
    /// <summary>카탈로그 칸의 찾기 칸에 적는다 — 사람이 적은 것과 같다.</summary>
    internal void FindInCatalog(string text)
    {
        _catalogQuery.Text = text;
        RefreshCatalogColumn();
    }

    /// <summary>카탈로그 칸을 펴거나 접는다 — 접으면 폭을 기억해 두었다가 펼 때 돌려준다.</summary>
    internal void SetCatalogOpen(bool open)
    {
        if (open == _catalogOpen)
        {
            _catalogToggle.IsChecked = open;
            return;
        }

        if (!open)
        {
            _catalogWidth = _body.ColumnDefinitions[6].Width.Value;
        }

        _catalogOpen = open;
        _body.ColumnDefinitions[5].Width = new GridLength(open ? 4 : 0);
        _body.ColumnDefinitions[6].Width = new GridLength(open ? _catalogWidth : 0);

        if (_catalogColumn is not null)
        {
            _catalogColumn.IsVisible = open;
        }

        _catalogToggle.IsChecked = open;
    }

    private bool ToggleCatalog()
    {
        SetCatalogOpen(!_catalogOpen);
        return true;
    }

    /// <summary>카탈로그 칸의 폭을 정한다 — 너무 좁은 값은 받지 않는다. 접어 두었으면 펼 때 쓴다.</summary>
    internal void SetCatalogWidth(double width)
    {
        if (width < MinSide)
        {
            return;
        }

        _catalogWidth = width;

        if (_catalogOpen)
        {
            _body.ColumnDefinitions[6].Width = new GridLength(width);
        }
    }

    /// <summary>카탈로그 칸을 다시 짓는다 — 종류 · 찾는 낱말 · 연 대본(명세 표) · 연 카드가 바뀌면.</summary>
    private void RefreshCatalogColumn()
    {
        string query = _catalogQuery.Text ?? string.Empty;
        bool finding = !string.IsNullOrWhiteSpace(query);

        _catalogShown = CatalogIndex.Find(Specs, _catalog, _catalogKind, DirectorKinds, query);
        _catalogList.Children.Clear();

        List<CatalogEntry> commands = _catalogShown.Where(e => !e.IsCard).ToList();
        List<CatalogEntry> cards = _catalogShown.Where(e => e.IsCard).ToList();

        if (finding)
        {
            _catalogList.Children.Add(FormGrid.Hint($"여섯 종류에서 찾았다 — 커맨드 {commands.Count} · 카드 {cards.Count}", Palette.Faint));
        }

        _catalogList.Children.Add(SectionTitle(finding ? "커맨드" : $"{CommandKinds.Label(_catalogKind)}의 커맨드 {commands.Count}"));

        foreach (CatalogEntry entry in commands)
        {
            _catalogList.Children.Add(CommandRow(entry, finding));
        }

        _catalogList.Children.Add(SectionTitle(finding ? "카드" : $"카드 {cards.Count}"));

        if (_catalog is null)
        {
            _catalogList.Children.Add(FormGrid.Hint("위의 \"카드 폴더…\"로 카탈로그를 고르면 이 종류의 카드가 여기 선다.", Palette.Faint));
        }
        else if (cards.Count == 0 && !finding)
        {
            _catalogList.Children.Add(FormGrid.Hint("이 종류의 카드는 없다.", Palette.Faint));
        }

        foreach (CatalogEntry entry in cards)
        {
            _catalogList.Children.Add(CardRow(entry, finding));
        }
    }

    private static TextBlock SectionTitle(string text)
        => new() { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Dim, Margin = new Thickness(0, 10, 0, 2) };

    private static Control CommandRow(CatalogEntry entry, bool showKind)
    {
        StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

        if (showKind)
        {
            head.Children.Add(Palette.Dot(entry.Kind));
        }

        head.Children.Add(new TextBlock { Text = entry.Name, FontFamily = Palette.Mono, FontSize = 12, FontWeight = FontWeight.SemiBold });

        StackPanel body = new() { Spacing = 1, Children = { head } };

        if (entry.Summary.Length > 0)
        {
            body.Children.Add(new TextBlock { Text = entry.Summary, FontSize = 11, Foreground = Palette.Dim, TextWrapping = TextWrapping.Wrap });
        }

        if (entry.Fields.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = string.Join(" · ", entry.Fields), FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        }

        return new Border { Padding = new Thickness(6, 4), CornerRadius = new CornerRadius(4), Child = body };
    }

    private static Control CardRow(CatalogEntry entry, bool showKind)
    {
        StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

        if (showKind)
        {
            head.Children.Add(Palette.Dot(entry.Kind));
        }

        head.Children.Add(Palette.Chip(entry.Id, font: Palette.Mono));
        head.Children.Add(new TextBlock { Text = entry.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        StackPanel body = new() { Spacing = 2, Children = { head } };

        if (entry.Fields.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = string.Join(" · ", entry.Fields), FontSize = 11, FontFamily = Palette.Mono, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        }

        return new Border { Padding = new Thickness(6, 4), CornerRadius = new CornerRadius(4), Child = body };
    }
}
