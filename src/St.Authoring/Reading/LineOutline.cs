using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.Authoring.Reading;

/// <summary>커맨드의 인자 칸 하나 — 화면에 보이는 이름과 값. 적지 않아 생략값으로 가는 칸은 <see cref="Written"/>이 거짓이다.</summary>
public sealed record OutlineArg(string Name, string Label, ArgKind Kind, string Value, bool Written);

/// <summary>스텝에 보이는 커맨드 하나. 주소로 고친다(K4-2).</summary>
public sealed record OutlineCommand(
    CommandAddress Address,
    string Name,
    string Summary,
    CommandKind Kind,
    float Delay,
    float? Duration,
    IReadOnlyList<OutlineArg> Args);

/// <summary>줄의 스텝 하나 — 앵커 · 대사 조각 · 간격 · 고른 종류의 커맨드.</summary>
/// <param name="Gap">간격(초). 적지 않았으면 null.</param>
/// <param name="Others">고른 종류가 아니어서 보이지 않는 커맨드의 수 — 종류마다. <c>pause</c>는 세지 않는다(지연으로 보인다).</param>
/// <param name="Unknown">명세 표에 없어 종류를 모르는 커맨드의 이름.</param>
public sealed record OutlineStep(
    string Anchor,
    LineAnchorKind Kind,
    string Text,
    float? Gap,
    IReadOnlyList<OutlineCommand> Commands,
    IReadOnlyDictionary<CommandKind, int> Others,
    IReadOnlyList<string> Unknown);

/// <summary>
/// 줄 하나를 화면에 펴 보일 꼴 — 스텝의 목록에 종류 필터를 건 것(docs/PLAN.md §4.5).
/// 앵커는 커맨드가 없어도 모두 든다(거기에 더할 수 있다). <c>pause</c>는 줄로 보이지 않고 뒤 커맨드의 지연으로 보인다 —
/// 연출가는 그 낱말을 보지 않는다.
/// </summary>
public sealed class LineOutline
{
    private LineOutline(LineRead line, bool editable, string readOnlyReason, IReadOnlyList<OutlineStep> steps)
    {
        Line = line;
        Editable = editable;
        ReadOnlyReason = readOnlyReason;
        Steps = steps;
    }

    public LineRead Line { get; }

    public bool Editable { get; }

    /// <summary>고칠 수 없는 까닭. 고칠 수 있으면 빈 글.</summary>
    public string ReadOnlyReason { get; }

    public IReadOnlyList<OutlineStep> Steps { get; }

    /// <summary>보이는 커맨드의 수.</summary>
    public int ShownCommands => Steps.Sum(s => s.Commands.Count);

    public static LineOutline Build(string text, LineRead line, IReadOnlySet<CommandKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(kinds);

        bool editable = LineEditor.IsEditable(line, out string reason);
        List<string> anchors = LineEditor.Anchors(text, line).Concat(line.Steps.Select(s => s.Name)).Distinct().ToList();
        int start = anchors.IndexOf(LineBeatSchedule.StartBeat);
        List<OutlineStep> steps = [];

        for (int i = 0; i < anchors.Count; i++)
        {
            string anchor = anchors[i];
            LineReadStep? step = line.Step(anchor);

            // 커맨드가 없는 표지는 줄 읽기의 스텝에 없다 — 갈래는 start의 앞인가 뒤인가로 가른다.
            LineAnchorKind kind = step?.Kind
                ?? (anchor == LineBeatSchedule.StartBeat ? LineAnchorKind.Start
                    : anchor == LineBeatSchedule.EndBeat ? LineAnchorKind.End
                    : i < start ? LineAnchorKind.Pre : LineAnchorKind.Text);

            List<OutlineCommand> commands = [];
            Dictionary<CommandKind, int> others = [];
            List<string> unknown = [];

            foreach (LineReadCommand command in step?.Commands ?? [])
            {
                if (command.Spec is null || CommandAddress.Of(line, command) is not { } address)
                {
                    unknown.Add(command.Name);
                    continue;
                }

                if (command.Spec.Kind == CommandKind.Time)
                {
                    continue;
                }

                if (!kinds.Contains(command.Spec.Kind))
                {
                    others[command.Spec.Kind] = others.GetValueOrDefault(command.Spec.Kind) + 1;
                    continue;
                }

                commands.Add(new OutlineCommand(
                    address, command.Name, command.Spec.Summary, command.Spec.Kind, command.Delay, command.Duration,
                    command.Bound.Where(b => !string.IsNullOrEmpty(b.Value))
                        .Select(b => new OutlineArg(b.Spec.Name, b.Spec.Label, b.Spec.Kind, b.Value, b.Token is not null))
                        .ToList()));
            }

            steps.Add(new OutlineStep(
                anchor, kind, step?.Text ?? string.Empty, step is { HasGap: true } ? step.Gap : null, commands, others, unknown));
        }

        return new LineOutline(line, editable, reason, steps);
    }
}
