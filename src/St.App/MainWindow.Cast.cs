using Ked.Presentation.Core;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 슬롯에 누가 서 있는가 — 슬롯 칸(오른쪽)과 슬롯 딱지(가운데)에 마우스를 올리면 뜬다.
/// 셈은 도메인의 <see cref="SlotCast"/>가 한다 — 고른 줄이 든 대본 파일의 앞 커맨드를 차례로 본다.
/// </summary>
public partial class MainWindow
{
    // 같은 파일 · 같은 판 · 같은 자리면 다시 셈하지 않는다 — 한 줄을 그리며 슬롯 딱지마다 묻는다.
    private (string Path, int Revision, int Line, IReadOnlyDictionary<string, CastInfo> Cast)? _cast;

    /// <summary>원문의 그 줄(1부터) 앞에서 슬롯마다 선 캐릭터. 줄을 고르지 않았으면 빈 것.</summary>
    private IReadOnlyDictionary<string, CastInfo> CastBefore(int line)
    {
        if (_session is null || _lines.SelectedItem is not LibraryLine shown)
        {
            return new Dictionary<string, CastInfo>();
        }

        string path = shown.File.Script.Path;

        if (_cast is not { } cached || cached.Path != path || cached.Revision != _session.Revision || cached.Line != line)
        {
            _cast = cached = (path, _session.Revision, line, SlotCast.Before(shown.File.Read, line));
        }

        return cached.Cast;
    }

    /// <summary>그 칸이 슬롯이면, 원문의 그 줄 앞에서 누가 서 있는지 풀어 주는 함수. 슬롯 칸이 아니면 null.</summary>
    private Func<string, string?>? SlotExplainer(CommandArgSpec spec, int line)
        => spec.Kind == ArgKind.Slot ? slot => SlotCast.Describe(CastBefore(line), slot) : null;

    /// <summary>슬롯 딱지 · 칸에 뜨는 글(테스트 손잡이) — 고른 줄에서 그 원문 줄 앞의 그 슬롯.</summary>
    internal string? SlotTip(string slot, int line) => SlotCast.Describe(CastBefore(line), slot);
}
