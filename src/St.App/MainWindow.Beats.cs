using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using St.App.Views;
using St.Authoring.Composing;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 박자 놓기(K8 I5 — docs/PLAN.md §2.5 L5) — 고른 줄의 글을 글자마다 펴고, 박자를 골라 놓을 글자를 누른다.
/// 박자의 자리는 시간이 아니라 "어느 글자 앞"이다 — 그 글자가 드러나기 직전에 그 박자의 커맨드가 열린다.
/// 글은 바뀌지 않는다(<see cref="EditSession.PlaceBeat"/>). 다시 볼 줄에서는 새 글 위에 놓는다 — 놓으면 그 줄은 확인된다(C7).
/// </summary>
public partial class MainWindow
{
    private int? _pickedBeat;

    /// <summary>가운데에 편 줄의 글과 박자 — 옮길 박자가 없으면 null(칸이 서지 않는다).</summary>
    internal BeatLine? ShownBeats { get; private set; }

    /// <summary>놓으려고 고른 박자의 이름. 고르지 않았으면 null.</summary>
    internal string? PickedBeat => _pickedBeat is { } index ? ShownBeats?.Beats.FirstOrDefault(b => b.Index == index)?.Name : null;

    /// <summary>박자를 놓는 칸 — 옮길 박자가 있는 줄에만 선다.</summary>
    private Control? BuildBeats(LibraryLine line, bool otherLine)
    {
        if (otherLine)
        {
            _pickedBeat = null;
        }

        ShownBeats = _session?.BeatsOf(line) is { } beats && beats.Beats.Any(b => b.Movable) ? beats : null;

        if (ShownBeats is not { } shown)
        {
            _pickedBeat = null;
            return null;
        }

        List<BeatSpot> movable = shown.Beats.Where(b => b.Movable).ToList();

        // 옮길 박자가 하나뿐이면 고를 것이 없다.
        if (_pickedBeat is not { } picked || movable.All(b => b.Index != picked))
        {
            _pickedBeat = movable.Count == 1 ? movable[0].Index : null;
        }

        WrapPanel chips = new() { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };

        foreach (BeatSpot beat in movable)
        {
            ToggleButton chip = new()
            {
                IsChecked = beat.Index == _pickedBeat,
                Padding = new Thickness(8, 2),
                Content = new TextBlock
                {
                    Text = beat.Position >= shown.Body.Length ? $"{beat.Name} · 글 끝" : $"{beat.Name} · {beat.Position}번째 글자 앞",
                    FontFamily = Palette.Mono,
                    FontSize = 12,
                },
            };

            int index = beat.Index;
            chip.Click += (_, _) =>
            {
                _pickedBeat = index;
                ShowSelectedLine();
            };
            chips.Children.Add(chip);
        }

        // 글자마다 누를 자리 — 그 글자 앞에 놓는다. 이미 놓인 박자는 그 자리에 이름으로 선다.
        WrapPanel letters = new() { Orientation = Orientation.Horizontal };

        for (int i = 0; i <= shown.Body.Length; i++)
        {
            foreach (BeatSpot beat in shown.Beats.Where(b => b.Position == i || (i == shown.Body.Length && b.Position > i)))
            {
                letters.Children.Add(new Border
                {
                    Background = beat.Index == _pickedBeat ? Palette.Warning : Palette.Line,
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 0),
                    Margin = new Thickness(1, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = beat.Name, FontFamily = Palette.Mono, FontSize = 10, Foreground = beat.Index == _pickedBeat ? Brushes.Black : Palette.Dim },
                });
            }

            bool end = i == shown.Body.Length;
            int position = i;

            TextBlock letter = new() { Text = end ? "⏎ 글 끝" : shown.Body[i].ToString(), FontSize = end ? 11 : 16, VerticalAlignment = VerticalAlignment.Center };

            if (end)
            {
                letter.Foreground = Palette.Faint;
            }

            Border spot = new() { Background = Brushes.Transparent, Padding = new Thickness(end ? 6 : 1, 2), Child = letter };

            // 글 첫머리(0)에는 놓지 않는다 — 앞 앵커의 자리다.
            if (i >= 1)
            {
                spot.Cursor = new Cursor(StandardCursorType.Hand);
                spot.PointerPressed += (_, _) => PlaceBeatAt(position);
            }

            letters.Children.Add(spot);
        }

        return new Border
        {
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = ShownReview is null
                            ? "박자 놓기 — 박자를 고르고 놓을 글자를 누른다. 그 글자가 드러나기 직전에 열린다"
                            : "박자 놓기 — 새 글 위에 놓는다. 놓으면 이 줄은 확인된다",
                        FontSize = 12,
                        Foreground = Palette.Dim,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    chips,
                    letters,
                },
            },
        };
    }

    // ── 조작 · 테스트 손잡이 ────────────────────────────────────────────────

    /// <summary>놓을 박자를 이름으로 고른다. 옮길 수 있는 그 이름의 박자가 없으면 false.</summary>
    internal bool PickBeat(string name)
    {
        if (ShownBeats?.Beats.FirstOrDefault(b => b.Movable && b.Name == name) is not { } beat)
        {
            return false;
        }

        _pickedBeat = beat.Index;
        ShowSelectedLine();
        return true;
    }

    /// <summary>
    /// 고른 박자를 그 글자 앞에 놓는다 — <paramref name="position"/>은 화자를 뺀 글에서의 자리(1 ~ 글의 길이, 길이면 글 끝).
    /// 파일에 쓰고 되돌리기 한 칸이다. 놓지 못했으면 false.
    /// </summary>
    internal bool PlaceBeatAt(int position)
    {
        if (_session is null || _lines.SelectedItem is not LibraryLine line)
        {
            return false;
        }

        if (_pickedBeat is not { } beat)
        {
            _status.Text = "놓을 박자를 먼저 고른다";
            return false;
        }

        string? name = PickedBeat;
        bool review = ShownReview is not null;
        EditOutcome outcome = _session.PlaceBeat(line, beat, position);

        if (!outcome.Ok)
        {
            _status.Text = $"놓지 못했다 — {DirectorText.Clean(outcome.Error)}";
            return false;
        }

        if (!outcome.Changed)
        {
            return false;
        }

        ReloadKeepingSelection();
        _status.Text = _session.ComposeProblem is { } held
            ? $"연출 층에 썼다 — {held}"
            : $"박자 {name}를 놓았다{(review ? " — 이 줄을 새 글로 확인했다" : string.Empty)} · 되돌리기 Ctrl+Z";
        return true;
    }
}
