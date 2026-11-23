using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using St.App.Views;

namespace St.App;

/// <summary>
/// 단축키 — 표 하나가 정본이다: 누른 키를 여기서 찾아 하고, F1의 창도 이 표를 그대로 보인다.
/// 글 칸에 적는 동안에는 글 칸의 것(지우기 · 복사 · 붙이기 · Esc)을 가로채지 않는다.
/// </summary>
public partial class MainWindow
{
    /// <summary>단축키 하나.</summary>
    /// <param name="InText">글 칸에 적는 동안에도 듣는가.</param>
    internal sealed record Shortcut(Key Key, KeyModifiers Modifiers, string Keys, string Does, bool InText, Func<MainWindow, bool> Run);

    /// <summary>단축키의 표 — F1의 창에 이 차례로 선다.</summary>
    internal static readonly IReadOnlyList<Shortcut> Shortcuts =
    [
        new(Key.F1, KeyModifiers.None, "F1", "이 표를 연다(설정 창의 단축키)", true, w => w.ShowSettings(SettingsTab.Shortcuts) is not null),
        new(Key.Z, KeyModifiers.Control, "Ctrl+Z", "되돌리기 — 마지막 고침을 되돌려 파일에 쓴다", false, w => w.Undo()),
        new(Key.Y, KeyModifiers.Control, "Ctrl+Y", "다시 하기", false, w => w.Redo()),
        new(Key.Z, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+Z", "다시 하기", false, w => w.Redo()),
        new(Key.F2, KeyModifiers.None, "F2", "카탈로그 칸(있는 커맨드와 카드)을 접고 편다", true, w => w.ToggleCatalog()),
        new(Key.F5, KeyModifiers.None, "F5", "디스크의 대본을 다시 읽는다", true, w => w.RefreshFromDisk()),
        new(Key.F, KeyModifiers.Control, "Ctrl+F", "이 장면에서 줄 찾기", true, w => w._search.Focus()),
        new(Key.Up, KeyModifiers.Alt, "Alt+↑", "앞 줄로", true, w => w.MoveLine(-1)),
        new(Key.Down, KeyModifiers.Alt, "Alt+↓", "다음 줄로", true, w => w.MoveLine(+1)),
        new(Key.Up, KeyModifiers.Alt | KeyModifiers.Shift, "Alt+Shift+↑", "켜 둔 종류의 연출이 있는 앞 줄로", true, w => w.MoveLine(-1, withKind: true)),
        new(Key.Down, KeyModifiers.Alt | KeyModifiers.Shift, "Alt+Shift+↓", "켜 둔 종류의 연출이 있는 다음 줄로", true, w => w.MoveLine(+1, withKind: true)),
        new(Key.Delete, KeyModifiers.None, "Delete", "고른 커맨드를 뺀다(여럿이면 한꺼번에 — 되돌리기 한 칸)", false, w => w.RemovePicked() is not null),
        new(Key.C, KeyModifiers.Control, "Ctrl+C", "고른 커맨드를 복사한다", false, w => w.CopyPicked() > 0),
        new(Key.V, KeyModifiers.Control, "Ctrl+V", "복사한 커맨드를 이 줄에 붙인다 — 같은 이름의 앵커에, 없으면 고른 앵커(없으면 start)에", false, w => w.PastePicked() is not null),
        new(Key.Escape, KeyModifiers.None, "Esc", "고른 것을 놓는다", false, w => w.ReleasePicks()),
    ];

    // 표에는 없지만 손으로 하는 것 — F1의 창에 함께 보인다.
    internal static readonly (string Keys, string Does)[] Gestures =
    [
        ("누르기", "커맨드나 앵커를 고른다"),
        ("Ctrl+누르기", "고른 것에 하나를 더하거나 뺀다"),
        ("Shift+누르기", "마지막에 고른 것부터 여기까지 고른다"),
        ("칸의 이름을 끌기", "수로 읽히는 칸의 값을 바꾼다 — Shift를 누르면 잘게"),
        ("타임라인의 막대 끌기", "지연(그 막대만) — Shift를 누르면 뒤의 커맨드도 함께"),
        ("막대의 오른쪽 끝 끌기", "길이"),
        ("앵커의 세로줄 끌기", "앞 앵커의 간격 — 노란 줄은 넘김 전의 머묾"),
        ("Ctrl+굴리기", "타임라인을 넓히고 좁힌다"),
        ("에피소드의 머리 누르기", "그 에피소드를 접고 편다"),
    ];

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!e.Handled && PressKey(e.Key, e.KeyModifiers, inText: e.Source is TextBox))
        {
            e.Handled = true;
        }
    }

    /// <summary>키를 누른 것과 같다 — 표에서 찾아 한다. 한 일이 있으면 true.</summary>
    internal bool PressKey(Key key, KeyModifiers modifiers = KeyModifiers.None, bool inText = false)
    {
        // 맥의 Cmd도 Ctrl로 듣는다.
        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            modifiers = (modifiers & ~KeyModifiers.Meta) | KeyModifiers.Control;
        }

        Shortcut? shortcut = Shortcuts.FirstOrDefault(s => s.Key == key && s.Modifiers == modifiers);
        return shortcut is not null && (shortcut.InText || !inText) && shortcut.Run(this);
    }

    private bool ReleasePicks()
    {
        if (_picked.Count == 0 && PickedAnchor is null)
        {
            return false;
        }

        ClearPicks();
        return true;
    }
}
