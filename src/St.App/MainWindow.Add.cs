using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Catalog;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 더하기(K4-3) — 고른 앵커에 그 종류의 어휘에서 커맨드 하나를, 또는 카드 한 장을 펼쳐 넣는다(docs/PLAN.md §4.4).
/// 넣기 전에 칸을 채운다 — 카드의 채울 자리(표정 · 인물 …)와 슬롯은 줄마다 다르다.
/// 카드 한 장은 조작 여럿이지만 되돌리기 한 칸이고, 카드에서 온 커맨드는 줄 끝에 출처(<c>// card:ID</c>)를 든다.
/// </summary>
public partial class MainWindow
{
    /// <summary>넣으려고 채우고 있는 커맨드 하나.</summary>
    internal sealed class PendingCommand(StageCommandSpec spec, string anchor)
    {
        public StageCommandSpec Spec { get; } = spec;

        /// <summary>넣을 앵커 — 카드가 적은 앵커가 이 줄에 없으면 고른 앵커다.</summary>
        public string Anchor { get; set; } = anchor;

        /// <summary>칸마다 적을 글. 빈 글이면 적지 않는다.</summary>
        public string[] Values { get; } = new string[spec.Args.Count];

        /// <summary>카드의 채울 자리 이름(표정 · 인물 …) — 채워야 하는 칸에만.</summary>
        public string?[] Placeholders { get; } = new string?[spec.Args.Count];
    }

    private CardCatalog? _catalog;

    // 연 대본의 명세 표 — 열지 않았으면 코어의 것.
    private StageCommandSpecTable Specs => _session?.Specs ?? StageCommandSpecTable.Default;
    private CommandKind _addKind = CommandKind.Acting;
    private bool _addFromCards;
    private string? _addChoice;
    private int _addPart;
    private string? _addFor;

    /// <summary>넣으려고 채우고 있는 커맨드 — 어휘면 하나, 카드면 그 묶음의 것 전부.</summary>
    internal List<PendingCommand> Pending { get; } = [];

    /// <summary>카드가 함께 두는 간격(앵커 · 길이).</summary>
    internal List<(string Anchor, string Duration)> PendingGaps { get; } = [];

    /// <summary>연 카탈로그. 고르지 않았으면 null.</summary>
    internal CardCatalog? Catalog => _catalog;

    // ── 카탈로그 ────────────────────────────────────────────────────────────

    private async Task PickCatalogAsync()
    {
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "카드 카탈로그 폴더(cards.json · kinds.json이 든 곳)를 고른다", AllowMultiple = false });

        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            SetCatalog(path);
        }
    }

    /// <summary>카탈로그 폴더를 열고 이 대본에 기억해 둔다. 읽지 못하면 까닭을 상태에 적고 false.</summary>
    internal bool SetCatalog(string folder)
    {
        try
        {
            _catalog = CardCatalog.Load(folder);
        }
        catch (InvalidDataException exception)
        {
            _status.Text = $"카드를 읽지 못했다 — {exception.Message}";
            return false;
        }

        if (_session is not null)
        {
            _settings.Catalogs[_session.Root] = folder;
            _settings.Save(_settingsPath);
        }

        _status.Text = $"카드 {_catalog.Cards.Count}장을 읽었다 — {folder}";

        // 카드의 이름과 "일부"는 카탈로그로 안다 — 줄을 다시 편다.
        ShowSelectedLine();
        RefreshCatalogColumn();
        return true;
    }

    private void LoadRememberedCatalog()
    {
        _catalog = null;

        if (_session is not null && _settings.Catalogs.TryGetValue(_session.Root, out string? folder) && Directory.Exists(folder))
        {
            try
            {
                _catalog = CardCatalog.Load(folder);
            }
            catch (InvalidDataException)
            {
                // 기억해 둔 폴더가 바뀌었다 — 카드 없이 연다. "카드 폴더…"로 다시 고른다.
            }
        }

        // 대본이 바뀌면 명세 표(포크의 것일 수 있다)와 카드가 함께 바뀐다.
        RefreshCatalogColumn();
    }

    // ── 더하기의 칸 ─────────────────────────────────────────────────────────

    private void BuildAddSection(OutlineStep step, bool editable)
    {
        if (!editable)
        {
            return;
        }

        // 다른 앵커로 옮겼으면 채우던 것을 새 앵커에 맞춰 다시 짓는다.
        string target = $"{_shownLine}|{step.Anchor}";

        if (_addFor != target)
        {
            _addFor = target;
            RebuildPending(step.Anchor);
        }

        int build = _inspectorBuild;

        _inspector.Children.Add(new Border
        {
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            Child = new TextBlock
            {
                Text = _replacingCard is { } old ? $"카드 {old.Tag}를 바꾸기 — {step.Anchor}에" : $"{step.Anchor}에 더하기",
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
            },
        });

        if (_replacingCard is { } replacing)
        {
            _inspector.Children.Add(FormGrid.Hint(
                $"넣으면 {replacing.Tag}의 커맨드 {replacing.Commands.Count}개를 빼고 이 카드를 넣는다 — 되돌리기 한 칸. 슬롯과 앵커는 {replacing.Tag}의 것을 옮겨 적었다 — 옛 카드가 있던 자리에 선다. 간격 · 지연은 건드리지 않는다.",
                Palette.Dim));
        }

        // 종류 — 연출가의 종류.
        ComboBox kinds = new()
        {
            ItemsSource = DirectorKinds.Select(CommandKinds.Label).ToList(),
            SelectedIndex = Array.IndexOf(DirectorKinds, _addKind),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        kinds.SelectionChanged += (_, _) =>
        {
            if (build == _inspectorBuild && kinds.SelectedIndex >= 0)
            {
                ChooseAddKind(DirectorKinds[kinds.SelectedIndex]);
            }
        };

        // 어휘에서 하나, 또는 카드 한 장.
        ComboBox source = new()
        {
            ItemsSource = new[] { "커맨드", _catalog is null ? "카드 — 위의 \"카드 폴더…\"를 먼저 고른다" : "카드" },
            SelectedIndex = _addFromCards ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        source.SelectionChanged += (_, _) =>
        {
            if (build == _inspectorBuild)
            {
                ChooseAddSource(source.SelectedIndex == 1);
            }
        };

        List<string> ids = AddChoices().Select(c => c.Id).ToList();
        ComboBox choice = new()
        {
            ItemsSource = AddChoices().Select(c => c.Label).ToList(),
            SelectedIndex = _addChoice is null ? -1 : ids.IndexOf(_addChoice),
            PlaceholderText = _addFromCards ? "카드를 고른다" : "커맨드를 고른다",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        choice.SelectionChanged += (_, _) =>
        {
            if (build == _inspectorBuild && choice.SelectedIndex >= 0)
            {
                ChooseAdd(ids[choice.SelectedIndex]);
            }
        };

        FormGrid pickers = new();
        pickers.Add("종류", kinds);
        pickers.Add("무엇을", source);
        pickers.Add(_addFromCards ? "카드" : "커맨드", choice);
        _inspector.Children.Add(pickers);

        if (_addFromCards && PickedCard() is { } card)
        {
            BuildCardNotes(card, build);
        }

        IReadOnlyList<string> anchors = ShownOutline!.Steps.Select(s => s.Anchor).ToList();

        for (int i = 0; i < Pending.Count; i++)
        {
            _inspector.Children.Add(BuildPending(Pending[i], anchors, build));
        }

        foreach ((string anchor, string duration) in PendingGaps)
        {
            _inspector.Children.Add(FormGrid.Hint($"간격 — {anchor}에 {TimeText.Format(DurationToken.TryParseSeconds(duration, out float seconds) ? seconds : 0f)}", Palette.Dim));
        }

        if (Pending.Count > 0 || PendingGaps.Count > 0)
        {
            Button insert = new() { Content = _replacingCard is not null ? "바꾸기" : _addFromCards ? "이 카드 넣기" : "넣기" };
            insert.Click += (_, _) => InsertPending();

            StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Children = { insert } };

            if (_replacingCard is not null)
            {
                Button cancel = new() { Content = "그만두기" };
                cancel.Click += (_, _) => CancelCardReplace();
                buttons.Children.Add(cancel);
            }

            _inspector.Children.Add(buttons);
        }
    }

    // 카드의 글(의도 · 언제)은 보이지 않는다 — 팀의 위키에 있다(D17 · U10). 위키로 가는 단추와, 넣기에 쓰는 꼴(묶음 · 본보기의 줄)만.
    private void BuildCardNotes(Card card, int build)
    {
        if (WikiButton(card.Id) is { } wiki)
        {
            _inspector.Children.Add(wiki);
        }

        if (card.Parts.Count > 1)
        {
            ComboBox parts = new()
            {
                ItemsSource = card.Parts.Select(p => p.Title).ToList(),
                SelectedIndex = Math.Clamp(_addPart, 0, card.Parts.Count - 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            parts.SelectionChanged += (_, _) =>
            {
                if (build == _inspectorBuild && parts.SelectedIndex >= 0)
                {
                    ChooseCardPart(parts.SelectedIndex);
                }
            };

            FormGrid grid = new();
            grid.Add("묶음", parts);
            _inspector.Children.Add(grid);
        }

        CardPart part = card.Parts[Math.Clamp(_addPart, 0, card.Parts.Count - 1)];

        if (part.Example.Length > 0)
        {
            _inspector.Children.Add(FormGrid.Hint($"본보기의 줄 — {part.Example}", Palette.Faint));
        }
    }

    private Control BuildPending(PendingCommand pending, IReadOnlyList<string> anchors, int build)
    {
        StackPanel head = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                Palette.Dot(pending.Spec.Kind),
                new TextBlock { Text = pending.Spec.Name, FontFamily = Palette.Mono, FontSize = 13, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = pending.Spec.Summary, FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };

        FormGrid fields = new();

        ComboBox anchor = new() { ItemsSource = anchors, SelectedItem = pending.Anchor, HorizontalAlignment = HorizontalAlignment.Stretch };
        anchor.SelectionChanged += (_, _) =>
        {
            if (build == _inspectorBuild && anchor.SelectedItem is string picked)
            {
                pending.Anchor = picked;
            }
        };
        fields.Add("앵커", anchor);

        for (int i = 0; i < pending.Spec.Args.Count; i++)
        {
            int index = i;
            CommandArgSpec spec = pending.Spec.Args[i];

            // 넣기 전이라 파일은 그대로다 — 적는 대로 채우던 것에 담긴다. 다시 지은 뒤에 오는 옛 칸의 글은 버린다.
            Control editor = ArgField.Create(
                spec, pending.Values[i],
                new ArgFieldOptions(Live: true, Candidates: Candidates(spec), Fill: pending.Placeholders[i], Describe: SlotExplainer(spec, ShownOutline?.Line.Line ?? 0)),
                value =>
                {
                    if (build == _inspectorBuild)
                    {
                        pending.Values[index] = value ?? string.Empty;
                    }
                });

            fields.Add(spec.Label, editor, spec.Note, spec);
        }

        return new Border
        {
            Background = Palette.Panel,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Child = new StackPanel { Spacing = 4, Children = { head, fields } },
        };
    }

    // ── 고르기 ──────────────────────────────────────────────────────────────

    private IReadOnlyList<(string Id, string Label)> AddChoices()
        => _addFromCards
            ? _catalog?.OfKind(_addKind).Select(c => (c.Id, $"{c.Id}  {c.Name}")).ToList() ?? []
            : Specs.OfKind(_addKind).Where(s => !s.Immediate).Select(s => (s.Name, $"{s.Name}  —  {s.Summary}")).ToList();

    private Card? PickedCard() => _addFromCards ? _catalog?.Cards.FirstOrDefault(c => c.Id == _addChoice) : null;

    /// <summary>더할 것의 종류를 고른다 — 그 종류의 어휘와 카드만 보인다.</summary>
    internal void ChooseAddKind(CommandKind kind)
    {
        _addKind = kind;
        _addChoice = null;
        _addPart = 0;
        RebuildPendingForPicked();
    }

    /// <summary>어휘에서 고르는가(false), 카드에서 고르는가(true). 카탈로그가 없으면 카드는 고를 수 없다.</summary>
    internal void ChooseAddSource(bool cards)
    {
        _addFromCards = cards && _catalog is not null;
        _addChoice = null;
        _addPart = 0;
        RebuildPendingForPicked();
    }

    /// <summary>커맨드 이름이나 카드 ID를 고른다 — 그 칸이 선다. 그 종류에 없으면 false.</summary>
    internal bool ChooseAdd(string id)
    {
        if (AddChoices().All(c => c.Id != id))
        {
            return false;
        }

        _addChoice = id;
        _addPart = 0;
        RebuildPendingForPicked();
        return true;
    }

    /// <summary>카드의 묶음을 고른다(줄 머리 · 말이 꺾이는 대목 …).</summary>
    internal void ChooseCardPart(int part)
    {
        _addPart = part;
        RebuildPendingForPicked();
    }

    private void RebuildPendingForPicked()
    {
        if (PickedAnchor is { } anchor)
        {
            RebuildPending(anchor);
        }

        RefreshInspector();
    }

    private void RebuildPending(string pickedAnchor)
    {
        Pending.Clear();
        PendingGaps.Clear();

        if (_addChoice is null || ShownOutline is null)
        {
            return;
        }

        HashSet<string> anchors = ShownOutline.Steps.Select(s => s.Anchor).ToHashSet();
        string Place(string anchor) => anchors.Contains(anchor) ? anchor : pickedAnchor;

        if (!_addFromCards)
        {
            if (Specs.TryGet(_addChoice, out StageCommandSpec spec))
            {
                PendingCommand pending = new(spec, pickedAnchor);
                Array.Fill(pending.Values, string.Empty);
                Pending.Add(pending);
            }

            return;
        }

        if (PickedCard() is not { Parts.Count: > 0 } card)
        {
            return;
        }

        CardPart part = card.Parts[Math.Clamp(_addPart, 0, card.Parts.Count - 1)];

        // 다른 카드로 바꾸는 중이면 새 카드의 앵커를 옛 카드의 것으로 옮겨 적는다 — 바꾼 카드는 옛 카드가 있던 자리에 선다(K7-5).
        IReadOnlyDictionary<string, string> anchorMap = _replacingCard is { } replaced
            ? CardSwap.InOrder(CardSwap.Anchors(replaced), part.Commands.Where(c => Specs.TryGet(c.Name, out _)).Select(c => c.Anchor).ToList())
            : new Dictionary<string, string>();
        string Moved(string anchor) => anchorMap.TryGetValue(anchor, out string? to) ? to : Place(anchor);

        foreach (CardCommand command in part.Commands)
        {
            if (!Specs.TryGet(command.Name, out StageCommandSpec spec))
            {
                continue;
            }

            PendingCommand pending = new(spec, Moved(command.Anchor));
            Array.Fill(pending.Values, string.Empty);

            // 카드의 인자를 칸에 맞춘다 — 채울 자리는 비워 두고 이름만 남긴다.
            List<BoundArg> bound = spec.Bind(command.Args.Select(a => a.Text).ToList());

            for (int i = 0; i < bound.Count; i++)
            {
                if (bound[i].Index < 0 || bound[i].Index >= command.Args.Count)
                {
                    continue;
                }

                CardToken token = command.Args[bound[i].Index];

                if (token.Placeholder)
                {
                    pending.Placeholders[i] = token.Text;
                }
                else
                {
                    pending.Values[i] = token.Text;
                }
            }

            Pending.Add(pending);
        }

        foreach (CardGap gap in part.Gaps)
        {
            PendingGaps.Add((Moved(gap.Anchor), gap.Duration));
        }

        // 다른 카드로 바꾸는 중이면 슬롯을 옛 카드의 것으로 옮겨 적는다(K7-5).
        if (_replacingCard is { } old && _lines.SelectedItem is LibraryLine line)
        {
            List<string> fresh = Pending
                .SelectMany(p => p.Spec.Args.Select((a, i) => (a, i)).Where(x => x.a.Kind == ArgKind.Slot && p.Values[x.i].Length > 0).Select(x => p.Values[x.i]))
                .ToList();
            IReadOnlyDictionary<string, string> map = CardSwap.InOrder(CardSwap.Slots(line.Read, old), fresh);

            foreach (PendingCommand pending in Pending)
            {
                for (int i = 0; i < pending.Spec.Args.Count; i++)
                {
                    if (pending.Spec.Args[i].Kind == ArgKind.Slot && map.TryGetValue(pending.Values[i], out string? slot))
                    {
                        pending.Values[i] = slot;
                    }
                }
            }
        }
    }

    /// <summary>채우고 있는 커맨드의 칸 하나에 적는다(칸의 차례는 명세 표의 것) — 사람이 칸에 적은 것과 같다.</summary>
    internal void FillPending(int command, int arg, string value)
    {
        Pending[command].Values[arg] = value;
        RefreshInspector();
    }

    /// <summary>채우고 있는 커맨드를 넣을 앵커를 바꾼다.</summary>
    internal void PlacePending(int command, string anchor)
    {
        Pending[command].Anchor = anchor;
        RefreshInspector();
    }

    /// <summary>
    /// 채운 것을 넣는다 — 한 덩이로(되돌리기 한 칸). 넣은 뒤에도 앵커를 고른 채다.
    /// 칸이 덜 채워져 조작을 짓지 못하면 null이고 까닭이 <see cref="EditMessage"/>에 남는다.
    /// </summary>
    internal EditOutcome? InsertPending()
    {
        if (ShownOutline is null || _lines.SelectedItem is not LibraryLine line || (Pending.Count == 0 && PendingGaps.Count == 0))
        {
            return null;
        }

        string lineId = ShownOutline.Line.LineId;
        string? tag = PickedCard() is { } card ? LineEditor.NextCardTag(line.File.Script.Text, line.Read, card.Id) : null;

        // 다른 카드로 바꾸는 중이면 옛 카드를 먼저 뺀다 — 한 덩이(되돌리기 한 칸).
        CardGroup? replacing = _replacingCard;
        List<LineEdit> edits = replacing is null ? [] : [.. CardSwap.Remove(replacing)];

        foreach (PendingCommand pending in Pending)
        {
            if (!LineEditor.TryComposeArguments(pending.Spec, pending.Values, out IReadOnlyList<string> arguments, out string error))
            {
                _editMessage = $"넣지 않았다 — {error}";
                RefreshInspector();
                return null;
            }

            edits.Add(new AddCommand(lineId, pending.Anchor, pending.Spec.Name, arguments, Card: tag));
        }

        foreach ((string anchor, string duration) in PendingGaps)
        {
            edits.Add(new SetGap(lineId, anchor, duration));
        }

        EditOutcome? outcome = ApplyEdits(edits, keepStep: true);

        if (outcome is { Ok: true, Changed: true })
        {
            if (replacing is not null)
            {
                _replacingCard = null;
                _status.Text = $"카드 {replacing.Tag}를 {PickedCard()?.Id ?? "새 카드"}로 바꿨다 — 간격 · 지연은 건드리지 않았다 · 되돌리기 Ctrl+Z";
            }

            // 넣었으면 채우던 것을 비운다 — 같은 것을 또 넣으려면 다시 고른다.
            _addChoice = null;
            Pending.Clear();
            PendingGaps.Clear();
            RefreshInspector();
        }

        return outcome;
    }
}
