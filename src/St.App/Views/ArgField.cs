using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.App.Views;

/// <summary>칸 하나를 어떻게 세울지.</summary>
/// <param name="Enabled">고칠 수 있는가 — 읽기만인 줄이면 잠근다.</param>
/// <param name="Live">글이 바뀔 때마다 알리는가(넣기 전에 채우는 칸). 아니면 Enter를 치거나 포커스가 떠날 때 알린다(이미 있는 커맨드의 칸).</param>
/// <param name="Candidates">값의 후보 — 누르는 딱지로 선다.</param>
/// <param name="Fill">카드의 채울 자리 이름(표정 · 인물 …). 빈 칸에 흐리게 보인다.</param>
internal sealed record ArgFieldOptions(bool Enabled = true, bool Live = false, IReadOnlyList<string>? Candidates = null, string? Fill = null);

/// <summary>
/// 커맨드의 인자 칸 하나를 화면의 칸으로 — 갈래가 모양을 정한다(docs/PLAN.md §4.5 · §4.6):
/// 낱말 칸은 체크, 정해진 낱말은 고르는 칸, 이징은 곡선이 딸린 고르는 칸, 그 밖은 글 칸(후보가 있으면 딱지가 딸린다).
/// 이미 있는 커맨드를 고치는 칸과 넣기 전에 채우는 칸이 같은 것을 쓴다.
/// </summary>
internal static class ArgField
{
    private const string Unwritten = "(적지 않음)";
    private const int ShownCandidates = 14;

    // 이만큼 끌 때마다 한 걸음.
    private const double ScrubPixelsPerStep = 6;

    /// <summary>칸을 세운다.</summary>
    /// <param name="written">대본에 적힌 글(또는 채우고 있는 글). 적지 않았으면 빈 글.</param>
    /// <param name="commit">값이 바뀌었다 — null이면 그 칸을 비운다(적지 않는다).</param>
    public static Control Create(CommandArgSpec spec, string written, ArgFieldOptions options, Action<string?> commit)
    {
        if (spec.Flag && spec.Choices.Count > 0)
        {
            CheckBox box = new() { Content = spec.Choices[0], IsChecked = written.Length > 0, IsEnabled = options.Enabled };
            box.IsCheckedChanged += (_, _) => commit(box.IsChecked == true ? spec.Choices[0] : null);
            return box;
        }

        if (spec.Kind == ArgKind.Ease)
        {
            return EaseField(written, options, commit);
        }

        if (spec.Choices.Count > 0)
        {
            return ChoiceField(spec, written, options, commit);
        }

        return TextField(spec, written, options, commit);
    }

    /// <summary>칸의 이름을 옆으로 끌어 값을 바꾸게 한다 — 수로 읽히는 칸만. 끄는 동안 글이 따라오고 놓을 때 알린다.</summary>
    public static void AttachScrub(TextBlock caption, CommandArgSpec spec, Control cell)
    {
        if (!ValueScrub.CanScrub(spec) || TextBoxOf(cell) is not { IsEnabled: true } box)
        {
            return;
        }

        caption.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        ToolTip.SetTip(caption, "이름을 옆으로 끌면 값이 바뀐다 — Shift를 누르면 잘게");

        double? from = null;
        string start = string.Empty;

        caption.PointerPressed += (_, e) =>
        {
            from = e.GetPosition(caption).X;
            start = box.Text ?? string.Empty;
            e.Pointer.Capture(caption);
            e.Handled = true;
        };

        caption.PointerMoved += (_, e) =>
        {
            if (from is { } origin)
            {
                int steps = (int)Math.Round((e.GetPosition(caption).X - origin) / ScrubPixelsPerStep);
                box.Text = ValueScrub.Step(spec, start, steps, e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ?? start;
            }
        };

        caption.PointerReleased += (_, e) =>
        {
            if (from is null)
            {
                return;
            }

            from = null;
            e.Pointer.Capture(null);
            Commit(box);
        };
    }

    /// <summary>
    /// 사람이 하듯 칸에 적는다(테스트 손잡이) — 글 칸은 적고 Enter, 고르는 칸은 그 낱말을 고르고, 낱말 칸은 "켬" · "끔".
    /// 잠긴 칸이거나 그런 낱말이 없으면 false.
    /// </summary>
    public static bool TrySet(Control cell, string text)
    {
        switch (Editor(cell))
        {
            case TextBox box when box.IsEnabled:
                box.Text = text;
                Commit(box);
                return true;

            case ComboBox combo when combo.IsEnabled:
                combo.SelectedItem = text.Length == 0 ? Unwritten : text;
                return Equals(combo.SelectedItem, text.Length == 0 ? Unwritten : text);

            case CheckBox check when check.IsEnabled:
                check.IsChecked = text == "켬";
                return true;

            default:
                return false;
        }
    }

    /// <summary>칸의 이름을 걸음 수만큼 끈 것과 같다(테스트 손잡이). 끌 수 없는 칸이면 false.</summary>
    public static bool TryScrub(CommandArgSpec spec, Control cell, int steps, bool fine)
    {
        if (TextBoxOf(cell) is not { IsEnabled: true } box || ValueScrub.Step(spec, box.Text, steps, fine) is not { } moved)
        {
            return false;
        }

        box.Text = moved;
        Commit(box);
        return true;
    }

    // 글 칸에 Enter를 친 것과 같다 — 칸이 제 손으로 알린다.
    private static void Commit(TextBox box) => box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

    // 후보나 곡선이 딸린 칸은 한 덩이로 싸여 있다 — 그 안의 고치는 칸.
    private static Control Editor(Control cell) => cell is Panel panel ? panel.Children.FirstOrDefault(c => c is TextBox or ComboBox or CheckBox) ?? cell : cell;

    private static TextBox? TextBoxOf(Control cell) => Editor(cell) as TextBox;

    private static Control TextField(CommandArgSpec spec, string written, ArgFieldOptions options, Action<string?> commit)
    {
        string placeholder = options.Fill is { } fill ? $"{fill} — 채운다"
            : spec.Default is { Length: > 0 } fallback ? $"{fallback} — 적지 않음"
            : spec.Optional ? "적지 않음" : "꼭 적는다";

        TextBox box = new() { Text = written, PlaceholderText = placeholder, IsEnabled = options.Enabled, FontFamily = Palette.Mono, FontSize = 13 };
        string last = written;

        void Send()
        {
            string now = (box.Text ?? string.Empty).Trim();

            if (now != last)
            {
                last = now;
                commit(now.Length == 0 ? null : now);
            }
        }

        if (options.Live)
        {
            box.TextChanged += (_, _) => Send();
        }
        else
        {
            box.LostFocus += (_, _) => Send();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Send();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && !options.Live)
            {
                box.Text = written;
                last = written;
                e.Handled = true;
            }
        };

        if (!options.Enabled || options.Candidates is not { Count: > 0 } candidates)
        {
            return box;
        }

        // 후보를 누르면 그 값이 된다.
        WrapPanel chips = new();

        foreach (string candidate in candidates.Take(ShownCandidates))
        {
            bool current = candidate == written;
            Border chip = Palette.Chip(candidate, current ? null : Palette.Dim, current ? Palette.Picked : null, Palette.Mono);
            chip.Margin = new Thickness(0, 0, 4, 3);
            chip.Cursor = new Cursor(StandardCursorType.Hand);
            chip.PointerPressed += (_, _) =>
            {
                box.Text = candidate;
                Send();
            };
            chips.Children.Add(chip);
        }

        if (candidates.Count > ShownCandidates)
        {
            chips.Children.Add(new TextBlock { Text = $"… {candidates.Count - ShownCandidates}개 더", FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center });
        }

        return new StackPanel { Spacing = 3, Children = { box, chips } };
    }

    private static Control ChoiceField(CommandArgSpec spec, string written, ArgFieldOptions options, Action<string?> commit)
    {
        List<string> items = [.. spec.Choices];

        if (spec.Optional)
        {
            items.Insert(0, Unwritten);
        }

        ComboBox combo = new()
        {
            ItemsSource = items,
            SelectedIndex = written.Length > 0 ? items.FindIndex(i => string.Equals(i, written, StringComparison.OrdinalIgnoreCase)) : spec.Optional ? 0 : -1,
            IsEnabled = options.Enabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string picked)
            {
                commit(picked == Unwritten ? null : picked);
            }
        };

        return combo;
    }

    // 이징 — 이름을 고르면 곁의 곡선이 그 모양을 보인다. 표에 없는 이름이 적혀 있으면 그것도 목록에 남긴다.
    private static Control EaseField(string written, ArgFieldOptions options, Action<string?> commit)
    {
        List<string> items = [Unwritten, .. Enum.GetNames<EaseKind>()];

        if (written.Length > 0 && !items.Contains(written))
        {
            items.Add(written);
        }

        ComboBox combo = new()
        {
            ItemsSource = items,
            SelectedItem = written.Length > 0 ? written : Unwritten,
            IsEnabled = options.Enabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        EaseCurve curve = new() { Kind = Enum.TryParse(written, out EaseKind kind) ? kind : null };

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string picked)
            {
                curve.Kind = Enum.TryParse(picked, out EaseKind now) ? now : null;
                commit(picked == Unwritten ? null : picked);
            }
        };

        DockPanel.SetDock(curve, Dock.Right);
        curve.Margin = new Thickness(6, 0, 0, 0);
        return new DockPanel { Children = { curve, combo } };
    }
}
