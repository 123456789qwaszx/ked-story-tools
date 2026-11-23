using Ked.Presentation.Core;
using St.Authoring.Reading;

namespace St.Authoring.Editing;

/// <summary>
/// 바꾸기(K7-3)의 칸 옮겨 적기 — 고른 커맨드를 다른 커맨드로 바꿀 때 새 커맨드의 칸에 미리 적어 둘 값을 셈한다.
/// 옮기는 것은 <b>슬롯 · 길이 · 이징</b>뿐이다 — 이 셋은 어느 커맨드에서나 뜻이 같다. 거리 · 이름 같은 칸은 커맨드마다 뜻이 달라
/// (<c>place</c>의 가로는 자리, <c>move_by</c>의 가로는 옮기는 양) 옮기지 않는다. 같은 갈래끼리 차례대로 짝짓고, 대본에 적힌 값만 옮긴다
/// (적지 않아 생략값으로 가던 칸은 새 커맨드의 생략값으로 간다).
/// </summary>
public static class CommandSwap
{
    /// <summary>옮겨 적는 칸의 갈래.</summary>
    public static readonly IReadOnlyList<ArgKind> Carried = [ArgKind.Slot, ArgKind.Duration, ArgKind.Ease];

    /// <summary>새 커맨드의 칸마다(명세 표의 차례) 옮겨 적을 글 — 옮길 값이 없으면 빈 글.</summary>
    public static string[] Carry(IReadOnlyList<OutlineArg> from, StageCommandSpec to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        string[] values = new string[to.Args.Count];
        Array.Fill(values, string.Empty);

        foreach (ArgKind kind in Carried)
        {
            List<OutlineArg> sources = from.Where(a => a.Kind == kind).ToList();
            int next = 0;

            for (int i = 0; i < to.Args.Count && next < sources.Count; i++)
            {
                if (to.Args[i].Kind != kind)
                {
                    continue;
                }

                OutlineArg source = sources[next++];

                if (source.Written)
                {
                    values[i] = source.Value;
                }
            }
        }

        return values;
    }
}
