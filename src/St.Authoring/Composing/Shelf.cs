using System.Text.RegularExpressions;

namespace St.Authoring.Composing;

/// <summary>보관소의 줄 하나 — 대사 층에서 사라진 줄과 거기 붙어 있던 연출(C3).</summary>
/// <param name="LineId">줄 ID — 접두사가 든 전체(<c>line:abc</c>).</param>
/// <param name="Node">그 줄이 있던 노드.</param>
/// <param name="Speaker">그때의 화자.</param>
/// <param name="Body">그때의 글 — 박자 표시와 태그를 뺀 것.</param>
/// <param name="Commands">묶음에 든 명령의 수.</param>
/// <param name="Lead">묶음의 원문 — 명령 · 주석 · 빈 줄.</param>
/// <param name="Line">옛 대사 줄의 원문(줄 끝 문자 없이).</param>
public sealed record ShelvedLine(string LineId, string Node, string Speaker, string Body, int Commands, string Lead, string Line);

/// <summary>
/// 보관소(K8 I6 — docs/PLAN.md §2.5 C3) — <c>&lt;장면&gt;.retired.yarn</c>을 읽고, 보관한 연출을 다른 줄로 옮길 글을 짓는다.
/// 작가가 줄을 지우고 새 줄로 다시 쓰면 줄 ID가 달라 연출이 저절로 돌아오지 않는다 — 사람이 "이 줄로 옮기기"로 겨눈다
/// (마야의 실패한 편집을 다시 겨누는 것과 같다). 순수 함수다.
/// </summary>
public static partial class Shelf
{
    // 묶음의 명령이 가리키는 앵커 — <<at 이름 …>> · <<gap 이름 …>>.
    [GeneratedRegex(@"^\s*<<\s*(?:at|gap)\s+(?<name>[^\s>]+)")]
    private static partial Regex AnchorPattern();

    /// <summary>보관소 파일에 든 줄 — 파일의 차례.</summary>
    public static IReadOnlyList<ShelvedLine> Read(string retiredText)
    {
        ArgumentNullException.ThrowIfNull(retiredText);

        List<ShelvedLine> lines = [];

        foreach (LayerNode node in LayerScript.Parse(retiredText).Nodes)
        {
            foreach (LayerItem item in node.Items)
            {
                if (item.Line is { LineId: { } lineId } line)
                {
                    lines.Add(new ShelvedLine(
                        lineId, node.Title, line.Speaker, line.Body, CodeLines(item.Lead).Count(), item.Lead, item.Raw.TrimEnd('\n', '\r')));
                }
            }
        }

        return lines;
    }

    /// <summary>그 줄을 뺀 보관소 — 비게 된 노드는 걷는다. 남은 것이 없으면 빈 글(파일을 걷는다).</summary>
    public static string Without(string retiredText, string lineId)
    {
        ArgumentNullException.ThrowIfNull(retiredText);
        ArgumentNullException.ThrowIfNull(lineId);

        LayerScript shelf = LayerScript.Parse(retiredText);

        foreach (LayerNode node in shelf.Nodes)
        {
            node.Items.RemoveAll(i => i.LineId == lineId);
        }

        LayerComposer.Prune(shelf);
        return shelf.Nodes.Count == 0 && shelf.Trailer.Trim().Length == 0 ? string.Empty : shelf.ToText();
    }

    /// <summary>
    /// 보관한 연출을 그 줄에 옮겨 붙일 글 — 묶음(그 줄의 지금 묶음 <b>뒤에 덧붙인다</b>)과 갈아 적을 대사 줄(줄 끝 문자 없이).
    /// 묶음의 명령이 가리키는 박자가 그 줄의 글에 없으면 표지를 함께 가져온다: 글 첫머리의 것은 첫머리에, 그 밖의 것은
    /// <b>줄 끝에</b> — 자리는 사람이 다시 놓는다(L5와 같다). 그 줄의 글은 바뀌지 않는다.
    /// </summary>
    public static (string Lead, string Line) Onto(string targetLine, ShelvedLine shelved)
    {
        ArgumentNullException.ThrowIfNull(targetLine);
        ArgumentNullException.ThrowIfNull(shelved);

        LineText target = LineText.Parse(targetLine);
        LineText old = LineText.Parse(shelved.Line);

        HashSet<string> used = CodeLines(shelved.Lead)
            .Select(l => AnchorPattern().Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> has = target.Beats.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
        List<Beat> missing = old.Beats.Where(b => used.Contains(b.Name) && has.Add(b.Name)).ToList();

        string line = target.WithBeats(
            string.Concat(missing.Where(b => b.Position <= old.BodyStart).Select(b => b.Raw)),
            string.Concat(missing.Where(b => b.Position > old.BodyStart).Select(b => b.Raw)));

        // 묶음 머리의 빈 줄은 두고 온다 — 그 줄의 지금 묶음에 바로 잇는다.
        string lead = shelved.Lead.TrimStart('\n', '\r');
        return (lead.Length == 0 || lead.EndsWith('\n') ? lead : lead + "\n", line);
    }

    // 묶음에서 명령이 든 줄 — 주석과 빈 줄을 뺀 것.
    private static IEnumerable<string> CodeLines(string lead)
        => lead.Split('\n').Select(Code).Where(l => l.TrimStart().StartsWith("<<", StringComparison.Ordinal));

    private static string Code(string line)
    {
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }
}
