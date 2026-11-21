using Ked.Presentation.Core;

namespace St.Authoring.Reading;

/// <summary>고치기 전과 뒤에서 달라진 것 하나.</summary>
/// <param name="Anchor">어느 앵커에서.</param>
/// <param name="Added">더해졌는가(참) 빠졌는가(거짓).</param>
/// <param name="Text">보이는 글 — 커맨드와 인자, 지연, 또는 간격.</param>
public sealed record LineChange(string Anchor, bool Added, string Text);

/// <summary>
/// 줄 하나를 고치기 전과 뒤로 맞댄다(docs/PLAN.md §4.6) — 앵커마다 빠진 것과 더해진 것.
/// 값만 바뀐 커맨드는 빠진 것 하나와 더해진 것 하나로 나온다. <c>pause</c>는 커맨드의 지연으로 보인다.
/// </summary>
public static class LineDiff
{
    public static IReadOnlyList<LineChange> Compare(LineRead before, LineRead after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        List<LineChange> changes = [];
        List<string> anchors = before.Steps.Select(s => s.Name).Concat(after.Steps.Select(s => s.Name)).Distinct().ToList();

        foreach (string anchor in anchors)
        {
            List<string> old = Entries(before.Step(anchor));
            List<string> now = Entries(after.Step(anchor));

            // 같은 글은 하나씩 짝지어 지운다 — 남은 것이 달라진 것이다.
            foreach (string entry in old.ToList())
            {
                if (now.Remove(entry))
                {
                    old.Remove(entry);
                }
            }

            changes.AddRange(old.Select(entry => new LineChange(anchor, Added: false, entry)));
            changes.AddRange(now.Select(entry => new LineChange(anchor, Added: true, entry)));
        }

        return changes;
    }

    private static List<string> Entries(LineReadStep? step)
    {
        List<string> entries = [];

        if (step is null)
        {
            return entries;
        }

        foreach (LineReadCommand command in step.Commands)
        {
            if (command.Name == YarnCommandTextExtractor.PauseCommand)
            {
                continue;
            }

            string delay = command.Delay > 0f ? $"  +{TimeText.Format(command.Delay)}" : string.Empty;
            entries.Add($"{command.Name} {string.Join(' ', command.Args)}".TrimEnd() + delay);
        }

        if (step.HasGap)
        {
            entries.Add($"간격 {TimeText.Format(step.Gap)}");
        }

        return entries;
    }
}
