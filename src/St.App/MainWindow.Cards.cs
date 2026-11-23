using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Editing;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.App;

/// <summary>
/// 카드 묶음(K7-4 — docs/PLAN.md §2.4 D16) — 출처 주석(<c>// card:E04</c>)으로 커맨드를 카드 한 장씩 묶어 본다.
/// 가운데의 커맨드에 카드 딱지가 붙고, 딱지를 누르면 그 카드의 커맨드가 모두 골라진다. "카드로 보기"를 켜면 앵커 아래에 카드 한 줄로 보인다.
/// 무엇이 묶이는지는 도메인 <see cref="CardGroups"/>가 읽는다 — 주석이 있는 줄에서만 선다(U9).
/// 카드 한 장을 고르면(딱지 · 카드 줄) 오른쪽에 카드의 칸이 선다(K7-5) — 통째로 빼기 · 위키 링크. 다른 카드로 바꾸기는 카탈로그의 카드에서.
/// </summary>
public partial class MainWindow
{
    private bool _cardView;

    // 다른 카드로 바꾸는 중이면 그 옛 카드 — 더하기 칸이 새 카드로 서 있고, 넣을 때 옛 카드를 함께 뺀다(되돌리기 한 칸).
    private CardGroup? _replacingCard;
    private readonly HashSet<string> _expandedCards = [];

    /// <summary>고른 줄의 카드 — 처음 나온 차례. 출처 주석이 없으면 빈 목록.</summary>
    internal IReadOnlyList<CardGroup> ShownCards { get; private set; } = [];

    /// <summary>"카드로 보기"가 켜져 있는가.</summary>
    internal bool CardView => _cardView;

    /// <summary>"카드로 보기"에서 펼친 카드의 출처.</summary>
    internal IReadOnlySet<string> ExpandedCards => _expandedCards;

    /// <summary>"카드로 보기"를 켜고 끈다 — 줄을 옮겨도 그대로다.</summary>
    internal void SetCardView(bool on)
    {
        _cardView = on;
        ShowSelectedLine();
    }

    /// <summary>"카드로 보기"에서 카드 한 장을 펴거나 접는다. 그런 카드가 없으면 false.</summary>
    internal bool ToggleCardExpanded(string tag)
    {
        if (ShownCards.All(c => c.Tag != tag))
        {
            return false;
        }

        if (!_expandedCards.Remove(tag))
        {
            _expandedCards.Add(tag);
        }

        ShowSelectedLine();
        return true;
    }

    /// <summary>다른 카드로 바꾸고 있는 옛 카드. 아니면 null.</summary>
    internal CardGroup? ReplacingCard => _replacingCard;

    /// <summary>고른 것이 카드 한 장이면 그 카드 — 그 카드의 (보이는) 커맨드를 모두, 그것만 골랐을 때.</summary>
    internal CardGroup? PickedCardGroup
    {
        get
        {
            if (_picked.Count == 0 || ShownOutline is not { } outline)
            {
                return null;
            }

            HashSet<CommandAddress> shown = outline.Steps.SelectMany(s => s.Commands).Select(c => c.Address).ToHashSet();
            HashSet<CommandAddress> picked = [.. _picked];
            return ShownCards.FirstOrDefault(c => picked.SetEquals(c.Commands.Where(shown.Contains)));
        }
    }

    /// <summary>그 카드의 위키 주소 — 설정의 주소 틀이 비었으면 null(D17).</summary>
    internal string? CardWikiLink(string cardId) => CardWiki.Link(_settings.CardWiki, cardId)?.AbsoluteUri;

    /// <summary>설정의 위키 주소 틀을 바꾼다 — 비우면 링크가 서지 않는다.</summary>
    internal void SetCardWiki(string? template)
    {
        _settings.CardWiki = string.IsNullOrWhiteSpace(template) ? null : template.Trim();
        _settings.Save(_settingsPath);
        RefreshCatalogColumn();
        RefreshInspector();
    }

    // 위키로 가는 단추 — 주소 틀이 없으면 서지 않는다.
    private Control? WikiButton(string cardId)
    {
        if (CardWiki.Link(_settings.CardWiki, cardId) is not { } uri)
        {
            return null;
        }

        Button wiki = new() { Content = "위키", Padding = new Thickness(6, 0), MinHeight = 0, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        wiki.Click += (_, _) => _ = Launcher.LaunchUriAsync(uri);
        ToolTip.SetTip(wiki, $"이 카드의 뜻과 쓰임 — {uri.AbsoluteUri}");
        return wiki;
    }

    /// <summary>
    /// 카드 한 장을 통째로 뺀다(K7-5) — 되돌리기 한 칸. 간격과 지연(<c>pause</c>)은 출처가 없어 건드리지 않고, 남은 것을 상태 줄에 알린다(U7).
    /// 그런 카드가 없거나 읽기만인 줄이면 null.
    /// </summary>
    internal EditOutcome? RemoveCard(string tag)
    {
        if (ShownCards.FirstOrDefault(c => c.Tag == tag) is not { } card || _lines.SelectedItem is not LibraryLine line)
        {
            return null;
        }

        IReadOnlyList<string> left = CardSwap.Leftovers(line.Read, card);
        EditOutcome? outcome = ApplyEdits(CardSwap.Remove(card), keepStep: false);

        if (outcome is { Ok: true, Changed: true })
        {
            _status.Text = left.Count > 0
                ? $"카드 {card.Tag}를 뺐다 — 간격 · 지연은 건드리지 않았다: {string.Join(" · ", left)}"
                : $"카드 {card.Tag}를 뺐다 — 되돌리기 Ctrl+Z";
        }

        return outcome;
    }

    /// <summary>
    /// 고른 카드를 카탈로그의 그 카드로 바꾼다(K7-5) — 더하기 칸이 새 카드로 서고(옛 카드의 첫 앵커를 고른 채), 슬롯은 옛 카드의 것을 옮겨 적는다.
    /// 칸을 채워 넣으면 옛 카드를 빼고 새 카드를 넣는다 — 되돌리기 한 칸. 카드를 고르지 않았거나 같은 카드면 false.
    /// </summary>
    internal bool ReplaceCardFromCatalog(string cardId)
    {
        if (PickedCardGroup is not { } old || old.CardId == cardId || ShownOutline is not { Editable: true })
        {
            return false;
        }

        if (!AddFromCatalog(cardId))
        {
            return false;
        }

        // 앵커는 옛 카드의 첫 앵커 — AddFromCatalog는 고른 것이 없어져 start를 골랐을 수 있다.
        string anchor = old.Commands[0].Anchor;
        _replacingCard = old;
        _addFor = null;
        PickStep(anchor);
        return true;
    }

    /// <summary>다른 카드로 바꾸기를 그만둔다 — 채우던 더하기 칸은 그대로 남는다(그냥 더하기가 된다).</summary>
    internal void CancelCardReplace()
    {
        _replacingCard = null;
        _addFor = null;
        RefreshInspector();
    }

    // 고른 카드의 칸 — 인스펙터 맨 위에.
    private void BuildCardSection(bool editable)
    {
        if (PickedCardGroup is not { } card)
        {
            return;
        }

        StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(Palette.Chip(card.Tag, font: Palette.Mono));
        head.Children.Add(new TextBlock { Text = card.Name ?? "카드", FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        if (WikiButton(card.CardId) is { } wiki)
        {
            head.Children.Add(wiki);
        }

        _inspector.Children.Add(head);
        _inspector.Children.Add(FormGrid.Hint(CardTip(card).Replace("누르면 이 카드의 커맨드를 모두 고른다", "골랐다"), Palette.Faint));

        Button remove = new() { Content = "카드 빼기", IsEnabled = editable };
        remove.Click += (_, _) => RemoveCard(card.Tag);
        ToolTip.SetTip(remove, "이 카드의 커맨드를 모두 뺀다 — 되돌리기 한 칸. 간격 · 지연은 건드리지 않는다");

        _inspector.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { remove } });

        if (editable)
        {
            _inspector.Children.Add(FormGrid.Hint("다른 카드로 바꾸려면 카탈로그의 카드에서 「바꾸기」.", Palette.Faint));
        }

        _inspector.Children.Add(new Border { Height = 1, Background = Palette.Line, Margin = new Thickness(0, 6) });
    }

    /// <summary>카드 한 장의 커맨드를 모두 고른다 — 딱지를 누른 것과 같다. 종류 필터에 가려진 것은 빠진다. 그런 카드가 없으면 false.</summary>
    internal bool PickCard(string tag)
    {
        if (ShownCards.FirstOrDefault(c => c.Tag == tag) is not { } card)
        {
            return false;
        }

        _picked.Clear();
        _picked.AddRange(card.Commands);
        PickedAnchor = null;
        _replacingCard = null;
        _editMessage = null;
        ShowSelectedLine();
        return true;
    }

    // 줄을 펼 때 카드를 읽는다 — 다른 줄로 옮겼으면 펼친 카드를 접는다.
    private void ReadCards(LibraryLine line, bool otherLine)
    {
        if (otherLine)
        {
            _expandedCards.Clear();
            _replacingCard = null;
        }

        ShownCards = CardGroups.Read(line.File.Script.Text, line.File.Read, line.Read, _catalog);
        _expandedCards.IntersectWith(ShownCards.Select(c => c.Tag));
    }

    private CardGroup? CardOf(CommandAddress address) => ShownCards.FirstOrDefault(c => c.Commands.Contains(address));

    // 줄 머리의 "카드로 보기" — 카드가 있을 때만 선다.
    private Control? CardViewToggle()
    {
        if (ShownCards.Count == 0)
        {
            return null;
        }

        ToggleButton toggle = new() { Content = $"카드로 보기 ({ShownCards.Count})", IsChecked = _cardView, FontSize = 11, Padding = new Thickness(6, 1), MinHeight = 0 };
        toggle.IsCheckedChanged += (_, _) => SetCardView(toggle.IsChecked == true);
        ToolTip.SetTip(toggle, "출처 주석(// card:ID)으로 커맨드를 카드 한 장씩 묶어 앵커 아래에 보인다");
        return toggle;
    }

    // 커맨드 줄에 붙는 카드 딱지 — 누르면 그 카드의 커맨드를 모두 고른다. 줄의 누르기(그 커맨드 하나 고르기)는 일어나지 않는다.
    private Control CardChip(CardGroup card)
    {
        Border chip = Palette.Chip(CardLabel(card), Palette.Dim, font: Palette.Mono);
        chip.Cursor = new Cursor(StandardCursorType.Hand);
        chip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            PickCard(card.Tag);
        };
        ToolTip.SetTip(chip, CardTip(card));
        return chip;
    }

    private static string CardLabel(CardGroup card)
        => card.Tag + (card.Name is { } name ? $" {name}" : string.Empty) + (card.Whole == false ? " · 일부" : string.Empty);

    private static string CardTip(CardGroup card)
    {
        List<string> lines = [$"카드 {card.Tag}{(card.Name is { } name ? $" — {name}" : string.Empty)} · 커맨드 {card.Commands.Count}", "누르면 이 카드의 커맨드를 모두 고른다"];

        if (card.Whole == false)
        {
            lines.Add("일부 — 카드의 커맨드 가운데 빠진 것이 있다");
        }
        else if (card.Whole is null && card.Name is not null)
        {
            lines.Add("카드의 어느 묶음과도 맞지 않는다");
        }

        if (card.Elsewhere is { } where)
        {
            lines.Add($"{where}에 이 카드의 다른 묶음이 있다 — 줄을 넘는 짝일 수 있다");
        }

        return string.Join("\n", lines);
    }

    // "카드로 보기"의 앵커 하나 — 카드에서 온 커맨드는 카드 한 줄(펼치면 그 앵커의 커맨드)로, 나머지는 그대로.
    private IEnumerable<Control> CardViewRows(OutlineStep step)
    {
        HashSet<string> done = [];

        foreach (OutlineCommand command in step.Commands)
        {
            if (CardOf(command.Address) is not { } card)
            {
                yield return BuildCommand(command);
                continue;
            }

            if (!done.Add(card.Tag))
            {
                continue;
            }

            List<OutlineCommand> here = step.Commands.Where(c => card.Commands.Contains(c.Address)).ToList();
            yield return CardRow(card, here);

            if (_expandedCards.Contains(card.Tag))
            {
                foreach (OutlineCommand each in here)
                {
                    Control row = BuildCommand(each);
                    row.Margin = new Thickness(22, 0, 0, 0);
                    yield return row;
                }
            }
        }
    }

    private Control CardRow(CardGroup card, List<OutlineCommand> here)
    {
        bool open = _expandedCards.Contains(card.Tag);

        Button fold = new() { Content = open ? "▾" : "▸", Padding = new Thickness(4, 0), MinHeight = 0, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        fold.Click += (_, _) => ToggleCardExpanded(card.Tag);
        ToolTip.SetTip(fold, open ? "접는다" : "이 앵커의 커맨드를 편다");

        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0, 0, 0) };
        row.Children.Add(fold);

        foreach (CommandKind kind in here.Select(c => c.Kind).Distinct())
        {
            row.Children.Add(Palette.Dot(kind));
        }

        row.Children.Add(new TextBlock { Text = card.Tag, FontFamily = Palette.Mono, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        if (card.Name is { } name)
        {
            row.Children.Add(new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        }

        string count = here.Count == card.Commands.Count ? $"커맨드 {here.Count}" : $"이 앵커에 {here.Count} / {card.Commands.Count}";
        row.Children.Add(new TextBlock { Text = count, FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center });

        if (card.Whole == false)
        {
            row.Children.Add(Palette.Chip("일부", Palette.Warning));
        }

        if (card.Elsewhere is { } where)
        {
            row.Children.Add(Palette.Chip($"{where}에 다른 묶음", Palette.Dim));
        }

        bool picked = card.Commands.Count > 0 && card.Commands.All(_picked.Contains) && _picked.Count == card.Commands.Count;
        Control pickable = Pickable(row, picked, _ => PickCard(card.Tag));
        ToolTip.SetTip(pickable, CardTip(card));
        return pickable;
    }
}
