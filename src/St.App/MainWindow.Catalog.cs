using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Catalog;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 카탈로그 칸(K7-1 — docs/PLAN.md §2.4) — 고른 종류의 어휘 전부와 카드가 늘 보인다(D14). 줄 · 앵커 · 커맨드를 고르지 않아도 선다.
/// 인스펙터 곁의 접히는 기둥이다(U5) — 고른 커맨드의 칸과 함께 보며 바꾼다. 카드는 ID · 이름만 보인다(D17).
/// 항목을 누르면 고른 줄의 더하기 칸이 그것으로 선다(K7-2) — 넣는 길은 더하기(K4-3)의 것 그대로다.
/// 커맨드를 하나 고른 채면 커맨드마다 "바꾸기"가 선다(K7-3) — 제자리에서 바꾸고(<see cref="ReplaceCommand"/>) 슬롯 · 길이 · 이징을 옮겨 적는다.
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

    // 바꾸기 — 고른 커맨드와, 꼭 적는 칸이 비어 바로 바꾸지 못해 채우고 있는 새 커맨드.
    private CommandAddress? _replaceTarget;
    private PendingCommand? _replaceWith;

    // 카탈로그 칸이 "바꾸기"를 보인 대상 — 바뀌면 칸을 다시 짓는다.
    private string? _catalogReplaceKey;

    /// <summary>바꾸려고 채우고 있는 새 커맨드. 없으면 null.</summary>
    internal PendingCommand? ReplacePending => _replaceWith;

    /// <summary>바꿀 수 있는 커맨드 — 고칠 수 있는 줄에서 하나만 골랐을 때 그것.</summary>
    private OutlineCommand? ReplaceableCommand
        => _picked.Count == 1 && ShownOutline is { Editable: true } outline
            ? outline.Steps.SelectMany(s => s.Commands).FirstOrDefault(c => c.Address == _picked[0])
            : null;

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
    /// <summary>
    /// 카탈로그의 항목(커맨드 이름이나 카드 ID)을 누른 것과 같다 — 고른 줄의 더하기 칸이 그것으로 선다(K7-2).
    /// 넣을 앵커는 <b>고른 커맨드의 앵커 → 고른 앵커 → <c>start</c></b> 차례로 미리 고른다 — 칸에서 바꿀 수 있다.
    /// 칸에 보이지 않는 항목, 고른 줄이 없거나 읽기만인 줄이면 세우지 않고 false — 까닭은 상태 줄이나 오른쪽에 보인다.
    /// </summary>
    internal bool AddFromCatalog(string id)
    {
        if (_catalogShown.FirstOrDefault(e => e.Id == id) is not { } entry)
        {
            return false;
        }

        if (ShownOutline is not { } outline)
        {
            _status.Text = $"줄을 먼저 고른다 — {entry.Id}를 넣을 줄이 없다";
            return false;
        }

        if (!outline.Editable)
        {
            _editMessage = $"{entry.Id}를 넣지 못한다 — 읽기만인 줄이다: {DirectorText.Clean(outline.ReadOnlyReason)}";
            RefreshInspector();
            return false;
        }

        string anchor = PickedCommand?.Anchor ?? PickedAnchor ?? LineBeatSchedule.StartBeat;

        if (outline.Steps.All(s => s.Anchor != anchor))
        {
            anchor = outline.Steps[0].Anchor;
        }

        _replacingCard = null;
        _addKind = entry.Kind;
        _addFromCards = entry.IsCard;
        _addChoice = entry.Id;
        _addPart = 0;

        // 앵커를 고르면 더하기 칸이 새로 지어진다 — 고른 것(_addChoice)으로.
        _addFor = null;
        PickStep(anchor);
        return true;
    }

    // ── 바꾸기(K7-3) ────────────────────────────────────────────────────────

    /// <summary>
    /// 고른 커맨드를 카탈로그의 그 커맨드로 바꾼다 — "바꾸기"를 누른 것과 같다. 슬롯 · 길이 · 이징을 옮겨 적고(<see cref="CommandSwap"/>),
    /// 꼭 적는 칸이 다 차 있으면 바로 바꾼다(되돌리기 한 칸). 비어 있으면 오른쪽에 채울 칸이 서고 null이다 — 채워서 <see cref="CommitReplace"/>.
    /// 하나만 고르지 않았거나 · 읽기만인 줄이거나 · 칸에 보이지 않거나 · 같은 커맨드면 아무것도 하지 않고 null.
    /// </summary>
    internal EditOutcome? ReplaceFromCatalog(string name)
    {
        if (ReplaceableCommand is not { } command
            || name == command.Name
            || _catalogShown.All(e => e.IsCard || e.Id != name)
            || !Specs.TryGet(name, out StageCommandSpec spec))
        {
            return null;
        }

        PendingCommand pending = new(spec, command.Address.Anchor);
        CommandSwap.Carry(command.Args, spec).CopyTo(pending.Values, 0);

        _replaceTarget = command.Address;
        _replaceWith = pending;
        _editMessage = null;

        if (Complete(pending))
        {
            return CommitReplace();
        }

        RefreshInspector();
        return null;
    }

    /// <summary>채운 새 커맨드로 바꾼다 — 거절되면 채우던 칸이 그대로 남고 까닭이 오른쪽에 뜬다.</summary>
    internal EditOutcome? CommitReplace()
    {
        if (_replaceTarget is not { } target || _replaceWith is not { } pending)
        {
            return null;
        }

        if (!LineEditor.TryComposeArguments(pending.Spec, pending.Values, out IReadOnlyList<string> arguments, out string error))
        {
            _editMessage = $"바꾸지 않았다 — {error}";
            RefreshInspector();
            return null;
        }

        EditOutcome? outcome = ApplyEdit(new ReplaceCommand(target, pending.Spec.Name, arguments));

        if (outcome is { Ok: true })
        {
            CancelReplace();
        }

        return outcome;
    }

    /// <summary>채우던 바꾸기를 그만둔다.</summary>
    internal void CancelReplace()
    {
        _replaceTarget = null;
        _replaceWith = null;
        RefreshInspector();
    }

    // 꼭 적는 칸이 모두 찼는가.
    private static bool Complete(PendingCommand pending)
        => pending.Spec.Args.Select((arg, i) => arg.Optional || pending.Values[i].Length > 0).All(filled => filled);

    // 고른 것이 바뀌면 채우던 바꾸기는 버린다.
    private void ForgetStaleReplace()
    {
        if (_replaceTarget is { } target && (_picked.Count != 1 || _picked[0] != target))
        {
            _replaceTarget = null;
            _replaceWith = null;
        }
    }

    // 바꿀 수 있는 커맨드가 바뀌면 카탈로그 칸을 다시 짓는다 — 커맨드마다 "바꾸기"가 서고 지고, 그 커맨드의 종류로 옮겨 간다.
    private void SyncCatalogReplace()
    {
        OutlineCommand? command = ReplaceableCommand;
        CardGroup? card = ShownOutline is { Editable: true } ? PickedCardGroup : null;
        string? key = command is null && card is null ? null : $"{command?.Address}|{command?.Name}|{card?.Tag}";

        if (key == _catalogReplaceKey)
        {
            return;
        }

        _catalogReplaceKey = key;

        // 바꿀 후보는 대개 같은 종류다 — 고른 커맨드의 종류로, 카드를 골랐으면 그 카드의 종류로 옮겨 간다(찾는 중이 아니면).
        CommandKind? follow = command?.Kind ?? (card is null ? null : _catalog?.Cards.FirstOrDefault(c => c.Id == card.CardId)?.Kind);

        if (follow is { } kind && string.IsNullOrWhiteSpace(_catalogQuery.Text) && kind != _catalogKind)
        {
            ChooseCatalogKind(kind);
            return;
        }

        RefreshCatalogColumn();
    }

    private void BuildReplaceSection(OutlineCommand command, int build)
    {
        if (_replaceTarget != command.Address || _replaceWith is not { } pending)
        {
            return;
        }

        _inspector.Children.Add(new Border
        {
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            Child = new TextBlock { Text = $"{command.Name} → {pending.Spec.Name}로 바꾸기", FontWeight = FontWeight.SemiBold, FontSize = 13 },
        });
        _inspector.Children.Add(FormGrid.Hint("슬롯 · 길이 · 이징은 옮겨 적었다. 앵커와 지연은 그대로다. 꼭 적는 칸을 채워 \"바꾸기\".", Palette.Faint));
        _inspector.Children.Add(BuildPending(pending, [pending.Anchor], build));

        Button replace = new() { Content = "바꾸기" };
        replace.Click += (_, _) => CommitReplace();

        Button cancel = new() { Content = "그만두기" };
        cancel.Click += (_, _) => CancelReplace();

        _inspector.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Children = { replace, cancel } });
    }

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

        _catalogList.Children.Add(FormGrid.Hint("누르면 고른 줄에 넣을 칸이 오른쪽에 선다 — 고른 커맨드의 앵커, 고른 앵커, 없으면 start에.", Palette.Faint));

        OutlineCommand? replaceable = ReplaceableCommand;
        CardGroup? replaceableCard = ShownOutline is { Editable: true } ? PickedCardGroup : null;

        if (replaceableCard is not null)
        {
            _catalogList.Children.Add(FormGrid.Hint($"고른 카드 {replaceableCard.Tag}를 다른 카드로 바꾸려면 그 카드의 \"바꾸기\" — 슬롯과 앵커를 옮겨 적는다.", Palette.Dim));
        }

        if (replaceable is not null)
        {
            _catalogList.Children.Add(FormGrid.Hint($"고른 {replaceable.Name}를 다른 커맨드로 바꾸려면 그 줄의 \"바꾸기\" — 슬롯 · 길이 · 이징을 옮겨 적는다.", Palette.Dim));
        }

        if (finding)
        {
            _catalogList.Children.Add(FormGrid.Hint($"여섯 종류에서 찾았다 — 커맨드 {commands.Count} · 카드 {cards.Count}", Palette.Faint));
        }

        _catalogList.Children.Add(SectionTitle(finding ? "커맨드" : $"{CommandKinds.Label(_catalogKind)}의 커맨드 {commands.Count}"));

        foreach (CatalogEntry entry in commands)
        {
            _catalogList.Children.Add(Pressable(CommandRow(entry, finding, replaceable?.Name), entry));
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
            _catalogList.Children.Add(Pressable(CardRow(entry, finding, replaceableCard), entry));
        }
    }

    // 누르면 넣을 칸이 선다. 투명한 바탕이 있어야 빈 곳을 눌러도 잡힌다.
    private Control Pressable(Border row, CatalogEntry entry)
    {
        row.Background = Brushes.Transparent;
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerPressed += (_, _) => AddFromCatalog(entry.Id);
        row.PointerEntered += (_, _) => row.Background = Palette.Raised;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    private static TextBlock SectionTitle(string text)
        => new() { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.Dim, Margin = new Thickness(0, 10, 0, 2) };

    private Border CommandRow(CatalogEntry entry, bool showKind, string? replacing)
    {
        StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

        // 고른 커맨드를 이것으로 바꾼다 — 단추가 누르기를 받아 줄의 누르기(넣기)는 일어나지 않는다.
        if (replacing is not null && replacing != entry.Id)
        {
            Button replace = new() { Content = "바꾸기", Padding = new Thickness(6, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            replace.Click += (_, _) => ReplaceFromCatalog(entry.Id);
            ToolTip.SetTip(replace, $"고른 {replacing}를 {entry.Id}로 바꾼다 — 되돌리기 한 칸");
            head.Children.Add(replace);
        }

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

    private Border CardRow(CatalogEntry entry, bool showKind, CardGroup? replacing)
    {
        StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

        // 고른 카드를 이 카드로 바꾼다(K7-5) — 단추가 누르기를 받아 줄의 누르기(넣기)는 일어나지 않는다.
        if (replacing is not null && replacing.CardId != entry.Id)
        {
            Button replace = new() { Content = "바꾸기", Padding = new Thickness(6, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            replace.Click += (_, _) => ReplaceCardFromCatalog(entry.Id);
            ToolTip.SetTip(replace, $"고른 카드 {replacing.Tag}를 {entry.Id}로 바꾼다 — 칸을 채워 넣으면 되돌리기 한 칸");
            head.Children.Add(replace);
        }

        if (showKind)
        {
            head.Children.Add(Palette.Dot(entry.Kind));
        }

        head.Children.Add(Palette.Chip(entry.Id, font: Palette.Mono));
        head.Children.Add(new TextBlock { Text = entry.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        if (WikiButton(entry.Id) is { } wiki)
        {
            head.Children.Add(wiki);
        }

        StackPanel body = new() { Spacing = 2, Children = { head } };

        if (entry.Fields.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = string.Join(" · ", entry.Fields), FontSize = 11, FontFamily = Palette.Mono, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        }

        return new Border { Padding = new Thickness(6, 4), CornerRadius = new CornerRadius(4), Child = body };
    }
}
