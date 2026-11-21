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
        RefreshInspector();
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
            Child = new TextBlock { Text = $"{step.Anchor}에 더하기", FontWeight = FontWeight.SemiBold, FontSize = 13 },
        });

        // 종류 — 연출가의 넷.
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

        Grid pickers = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        AddField(pickers, "종류", kinds);
        AddField(pickers, "무엇을", source);
        AddField(pickers, _addFromCards ? "카드" : "커맨드", choice);
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
            _inspector.Children.Add(Hint($"간격 — {anchor}에 {TimeText.Format(DurationToken.TryParseSeconds(duration, out float seconds) ? seconds : 0f)}", Palette.Dim));
        }

        if (Pending.Count > 0 || PendingGaps.Count > 0)
        {
            Button insert = new() { Content = _addFromCards ? "이 카드 넣기" : "넣기", Margin = new Thickness(0, 6, 0, 0) };
            insert.Click += (_, _) => InsertPending();
            _inspector.Children.Add(insert);
        }
    }

    private void BuildCardNotes(Card card, int build)
    {
        if (card.Intent.Length > 0)
        {
            _inspector.Children.Add(Hint(card.Intent));
        }

        if (card.When.Length > 0)
        {
            _inspector.Children.Add(Hint($"언제 — {card.When}", Palette.Faint));
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

            Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            AddField(grid, "묶음", parts);
            _inspector.Children.Add(grid);
        }

        CardPart part = card.Parts[Math.Clamp(_addPart, 0, card.Parts.Count - 1)];

        if (part.Example.Length > 0)
        {
            _inspector.Children.Add(Hint($"본보기의 줄 — {part.Example}", Palette.Faint));
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

        Grid fields = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

        ComboBox anchor = new() { ItemsSource = anchors, SelectedItem = pending.Anchor, HorizontalAlignment = HorizontalAlignment.Stretch };
        anchor.SelectionChanged += (_, _) =>
        {
            if (build == _inspectorBuild && anchor.SelectedItem is string picked)
            {
                pending.Anchor = picked;
            }
        };
        AddField(fields, "앵커", anchor);

        for (int i = 0; i < pending.Spec.Args.Count; i++)
        {
            AddField(fields, pending.Spec.Args[i].Label, PendingEditor(pending, i, build), pending.Spec.Args[i].Note);
        }

        return new Border
        {
            Background = Palette.Panel,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Child = new StackPanel { Spacing = 4, Children = { head, fields } },
        };
    }

    private Control PendingEditor(PendingCommand pending, int index, int build)
    {
        CommandArgSpec spec = pending.Spec.Args[index];

        if (spec.Flag && spec.Choices.Count > 0)
        {
            CheckBox box = new() { Content = spec.Choices[0], IsChecked = pending.Values[index] == spec.Choices[0] };
            box.IsCheckedChanged += (_, _) => pending.Values[index] = box.IsChecked == true ? spec.Choices[0] : string.Empty;
            return box;
        }

        if (spec.Choices.Count > 0)
        {
            const string unwritten = "(적지 않음)";
            List<string> items = [.. spec.Choices];

            if (spec.Optional)
            {
                items.Insert(0, unwritten);
            }

            ComboBox combo = new()
            {
                ItemsSource = items,
                SelectedIndex = pending.Values[index].Length > 0 ? items.IndexOf(pending.Values[index]) : spec.Optional ? 0 : -1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            combo.SelectionChanged += (_, _) => pending.Values[index] = combo.SelectedItem is string picked && picked != unwritten ? picked : string.Empty;
            return combo;
        }

        string placeholder = pending.Placeholders[index] is { } name ? $"{name} — 채운다"
            : spec.Default is { Length: > 0 } fallback ? $"{fallback} — 적지 않음"
            : spec.Optional ? "적지 않음" : "꼭 적는다";

        TextBox text = new() { Text = pending.Values[index], PlaceholderText = placeholder, FontFamily = Palette.Mono, FontSize = 13 };
        // TextChanged는 디스패처를 한 번 돈 뒤에 온다 — 그 사이 칸을 다시 지었으면 옛 칸의 글은 버린다.
        text.TextChanged += (_, _) =>
        {
            if (build == _inspectorBuild)
            {
                pending.Values[index] = (text.Text ?? string.Empty).Trim();
            }
        };

        IReadOnlyList<string> candidates = Candidates(spec);

        if (candidates.Count == 0)
        {
            return text;
        }

        // 후보를 누르면 그 값이 칸에 적힌다(넣기 전이라 파일은 그대로다).
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                text,
                CandidateChips(candidates, pending.Values[index], value =>
                {
                    if (build == _inspectorBuild)
                    {
                        pending.Values[index] = value;
                        RefreshInspector();
                    }
                }),
            },
        };
    }

    // ── 고르기 ──────────────────────────────────────────────────────────────

    private IReadOnlyList<(string Id, string Label)> AddChoices()
        => _addFromCards
            ? _catalog?.OfKind(_addKind).Select(c => (c.Id, $"{c.Id}  {c.Name}")).ToList() ?? []
            : StageCommandSpecTable.Default.OfKind(_addKind).Where(s => !s.Immediate).Select(s => (s.Name, $"{s.Name}  —  {s.Summary}")).ToList();

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
            if (StageCommandSpecTable.Default.TryGet(_addChoice, out StageCommandSpec spec))
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

        foreach (CardCommand command in part.Commands)
        {
            if (!StageCommandSpecTable.Default.TryGet(command.Name, out StageCommandSpec spec))
            {
                continue;
            }

            PendingCommand pending = new(spec, Place(command.Anchor));
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
            PendingGaps.Add((Place(gap.Anchor), gap.Duration));
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
        List<LineEdit> edits = [];

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
            // 넣었으면 채우던 것을 비운다 — 같은 것을 또 넣으려면 다시 고른다.
            _addChoice = null;
            Pending.Clear();
            PendingGaps.Clear();
            RefreshInspector();
        }

        return outcome;
    }
}
