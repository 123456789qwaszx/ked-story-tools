using Ked.Presentation.Core;
using St.Authoring.Reading;

namespace St.Authoring.Editing;

/// <summary>복사해 둔 커맨드 하나 — 어느 앵커의 것이었는지와 함께.</summary>
/// <param name="Anchor">복사한 줄에서의 앵커 이름.</param>
/// <param name="AnchorKind">그 앵커의 갈래 — 붙이는 줄에 같은 이름의 앵커가 없을 때 자리를 정한다.</param>
/// <param name="Card">카드에서 온 커맨드면 그 출처(<c>E04</c> · <c>F02#2</c>). 붙여도 따라간다.</param>
public sealed record CommandClip(string Anchor, LineAnchorKind AnchorKind, string Name, IReadOnlyList<string> Args, string? Card);

/// <summary>
/// 커맨드를 줄에서 줄로 옮긴다(docs/PLAN.md §4.6) — 복사한 것을 조작(더하기)으로 바꾼다.
/// 지연(<c>pause</c>)과 간격은 따라가지 않는다 — 붙인 줄에서 다시 맞춘다.
/// </summary>
public static class CommandClips
{
    /// <summary>그 줄에서 고른 커맨드를 복사한다 — 줄에 쓴 차례로.</summary>
    public static IReadOnlyList<CommandClip> Copy(string text, LineRead line, IReadOnlySet<CommandAddress> picked)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(picked);

        IReadOnlyDictionary<int, string> cards = LineEditor.CardTags(text, line);
        List<CommandClip> clips = [];

        foreach (LineReadStep step in line.Steps)
        {
            foreach (LineReadCommand command in step.Commands)
            {
                if (CommandAddress.Of(line, command) is { } address && picked.Contains(address))
                {
                    clips.Add(new CommandClip(step.Name, step.Kind, command.Name, [.. command.Args], cards.GetValueOrDefault(command.Line)));
                }
            }
        }

        return clips;
    }

    /// <summary>
    /// 복사한 것을 그 줄에 붙이는 조작 — 같은 이름의 앵커가 있으면 거기에, 없으면 start · end는 제자리에,
    /// 표지(앞 앵커 · 글 표지)는 <paramref name="fallbackAnchor"/>에 붙는다. 표지를 새로 만들지 않는다.
    /// </summary>
    public static IReadOnlyList<LineEdit> Paste(
        string lineId, IReadOnlyList<string> anchors, IReadOnlyList<CommandClip> clips, string fallbackAnchor)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(clips);

        List<LineEdit> edits = [];

        foreach (CommandClip clip in clips)
        {
            string anchor = anchors.Contains(clip.Anchor) ? clip.Anchor
                : clip.AnchorKind == LineAnchorKind.Start ? LineBeatSchedule.StartBeat
                : clip.AnchorKind == LineAnchorKind.End ? LineBeatSchedule.EndBeat
                : fallbackAnchor;

            edits.Add(new AddCommand(lineId, anchor, clip.Name, clip.Args, Card: clip.Card));
        }

        return edits;
    }

    /// <summary>
    /// 고른 커맨드를 모두 빼는 조작 — 한 덩이로 한다. 같은 앵커 · 같은 종류에서는 뒤의 것부터 빼야
    /// 앞의 것의 순번이 밀리지 않는다.
    /// </summary>
    public static IReadOnlyList<LineEdit> Remove(IEnumerable<CommandAddress> picked)
    {
        ArgumentNullException.ThrowIfNull(picked);
        return picked.OrderByDescending(a => a.Index).Select(a => (LineEdit)new RemoveCommand(a)).ToList();
    }
}
