namespace St.Authoring.Composing;

/// <summary>대사 글 안의 박자 하나 — 화면이 글 위에 그리고 옮긴다.</summary>
/// <param name="Index">그 줄의 박자 가운데 몇 번째인가 — 옮길 때 이것으로 가리킨다.</param>
/// <param name="Name">박자의 이름 — <c>&lt;&lt;at 이름 …&gt;&gt;</c>이 이 이름을 가리킨다.</param>
/// <param name="Position">화자를 뺀 글에서의 자리 — 이 글자가 드러나기 직전에 열린다. 글의 길이면 글 끝이다.</param>
/// <param name="Movable">옮길 수 있는가 — 글 첫머리의 박자(대사창이 열리기 전의 앞 앵커)는 그 자리에 둔다.</param>
public sealed record BeatSpot(int Index, string Name, int Position, bool Movable);

/// <summary>대사 줄 하나의 글과 그 안의 박자.</summary>
/// <param name="Speaker">화자 — 없으면 빈 글.</param>
/// <param name="Body">화자와 박자 표시를 뺀 글.</param>
public sealed record BeatLine(string Speaker, string Body, IReadOnlyList<BeatSpot> Beats);

/// <summary>
/// 박자 놓기(K8 I5 — docs/PLAN.md §2.5 L5 · P4) — 대사 줄의 박자 표시를 글의 다른 자리로 옮긴다. 글은 바뀌지 않는다.
/// 글이 바뀌어 줄 끝으로 밀린 박자를 사람이 다시 놓는 길이다. 순수 함수 — 물리 줄을 받아 물리 줄을 낸다.
/// </summary>
public static class BeatPlacement
{
    /// <summary>그 대사 줄의 글과 박자.</summary>
    public static BeatLine Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        LineText text = LineText.Parse(line);

        return new BeatLine(
            text.Speaker,
            text.Body,
            text.Beats.Select((b, i) => new BeatSpot(i, b.Name, Math.Max(0, b.Position - text.BodyStart), b.Position > text.BodyStart)).ToList());
    }

    /// <summary>
    /// 그 박자를 <paramref name="position"/>(화자를 뺀 글에서 1 ~ 글의 길이)으로 옮긴 줄 — 줄 끝 문자는 받은 대로다.
    /// 옮길 수 없으면(글 첫머리의 박자 · 글 밖의 자리 · 옮기면 글이 달라지는 자리) null.
    /// </summary>
    public static string? Move(string line, int index, int position)
    {
        ArgumentNullException.ThrowIfNull(line);

        LineText text = LineText.Parse(line);
        return text.WithBeatAt(index, position) is { } moved ? moved + text.Eol : null;
    }
}
