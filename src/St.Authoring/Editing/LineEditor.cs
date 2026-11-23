using System.Globalization;
using System.Text.RegularExpressions;
using Ked.Presentation.Core;

namespace St.Authoring.Editing;

/// <summary>
/// 줄 고치기 — yarn 원문과 조작 하나를 받아 고친 원문을 낸다(docs/PLAN.md §4.3 · K3). 순수 함수다.
///
/// 지키는 것:
///   ① 그 줄의 커맨드 묶음(<see cref="LineRead.Block"/>)만 다시 쓴다 — 묶음 밖은 글자 하나 바뀌지 않고,
///      묶음 안에서도 조작이 닿은 한 줄만 바뀐다(주석 · 빈 줄 · 다른 커맨드는 그대로).
///   ② 쓰기 전에 고친 글을 줄 읽기로 다시 읽는다 — 새 문제가 생기거나 다른 줄의 읽기가 달라지면 거절한다.
///   ③ 다시 읽은 값이 고치려던 값과 같아야 한다.
///
/// 새 표기(<c>&lt;&lt;at&gt;&gt;</c> · <c>&lt;&lt;gap&gt;&gt;</c>)만 쓴다 — 묶음에 at 없는 명령(걷힌 <c>&lt;&lt;step&gt;&gt;</c>도 그 하나다)이 든 줄은
/// 읽기만 한다. 그 명령은 실행되지 않는다(런타임 N5) — 앵커를 붙일지 지울지는 원문에서 정한다.
/// 조작 하나는 묶음의 한 줄을 고치거나 · 넣거나 · 뺀다.
/// </summary>
public static partial class LineEditor
{
    /// <summary>줄 읽기에 넘기는 출처 이름 — 문제 글의 줄 번호 앞에 붙는다.</summary>
    internal const string SourceName = "yarn";

    // 길이를 견주는 틈 — 시간 토큰을 적는 자릿수(0.0001초)에 맞춘다.
    private const float Epsilon = 0.0001f;

    // 다시 읽어 맞춰 보는 틈 — 적은 토큰이 float을 지나며 생기는 오차를 덮는다.
    private const float ReadBackTolerance = 0.001f;

    [GeneratedRegex(@"\byarn:\d+")]
    private static partial Regex SourceLinePattern();

    private const string CardTagPrefix = "card:";

    [GeneratedRegex(@"^[A-Za-z0-9_]+(#\d+)?$")]
    private static partial Regex CardTagPattern();

    [GeneratedRegex(@"//\s*card:(?<id>[A-Za-z0-9_]+)(#(?<n>\d+))?\s*$", RegexOptions.Multiline)]
    private static partial Regex CardCommentPattern();

    /// <summary>조작 하나를 한다. 거절하면 <see cref="EditOutcome.Text"/>는 받은 글 그대로다.</summary>
    public static EditOutcome Apply(string text, LineEdit edit, StageCommandSpecTable? specs = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edit);

        try
        {
            return ApplyCore(text, edit, specs ?? StageCommandSpecTable.Default);
        }
        catch (Refusal refusal)
        {
            return EditOutcome.Refused(text, refusal.Message);
        }
    }

    /// <summary>
    /// 조작 여럿을 차례로 한다 — 모두 받아들여져야 고친 글이 나온다(하나라도 거절되면 받은 글 그대로).
    /// 카드 한 장을 펼치는 것처럼 한 덩이인 고침을 되돌리기 한 칸으로 묶을 때 쓴다.
    /// </summary>
    public static EditOutcome ApplyAll(string text, IReadOnlyList<LineEdit> edits, StageCommandSpecTable? specs = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edits);

        string current = text;
        EditOutcome? last = null;
        List<string> warnings = [];
        List<string> removed = [];
        List<string> added = [];

        for (int i = 0; i < edits.Count; i++)
        {
            EditOutcome outcome = Apply(current, edits[i], specs);

            if (!outcome.Ok)
            {
                return EditOutcome.Refused(text, edits.Count == 1 ? outcome.Error : $"{i + 1}번째 — {outcome.Error}");
            }

            if (outcome.Changed)
            {
                current = outcome.Text;
                warnings.AddRange(outcome.Warnings);

                if (outcome.Removed is not null)
                {
                    removed.Add(outcome.Removed);
                }

                if (outcome.Added is not null)
                {
                    added.Add(outcome.Added);
                }
            }

            last = outcome;
        }

        if (last is null || current == text)
        {
            return last ?? EditOutcome.Refused(text, "할 조작이 없다");
        }

        return EditOutcome.Applied(
            current, last.Line!, last.Target,
            removed.Count > 0 ? string.Join('\n', removed) : null,
            added.Count > 0 ? string.Join('\n', added) : null,
            warnings);
    }

    /// <summary>
    /// 칸마다 적을 글(null이나 빈 글이면 적지 않는다)을 대본의 인자 열로 편다 — 더할 커맨드를 지을 때 쓴다.
    /// 뒤 칸을 적었는데 앞 칸이 비어 있으면 생략값으로 채우고, 생략값이 없으면 false와 까닭.
    /// </summary>
    public static bool TryComposeArguments(
        StageCommandSpec spec, IReadOnlyList<string?> values, out IReadOnlyList<string> arguments, out string error)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(values);

        string?[] desired = new string?[spec.Args.Count];

        for (int i = 0; i < desired.Length && i < values.Count; i++)
        {
            desired[i] = string.IsNullOrWhiteSpace(values[i]) ? null : values[i]!.Trim();
        }

        try
        {
            arguments = Compose(spec, desired);
            error = string.Empty;
            return true;
        }
        catch (Refusal refusal)
        {
            arguments = [];
            error = refusal.Message;
            return false;
        }
    }

    /// <summary>
    /// 이 줄의 커맨드 가운데 카드에서 온 것의 출처 — 파일의 줄 번호(<see cref="LineReadCommand.Line"/>) → <c>E04</c> · <c>F02#2</c>.
    /// </summary>
    public static IReadOnlyDictionary<int, string> CardTags(string text, LineRead line)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(line);

        Dictionary<int, string> tags = [];
        string[] lines = text.Substring(line.Block.Start, line.Block.Length).Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            Match match = CardCommentPattern().Match(lines[i].TrimEnd('\r'));

            if (match.Success)
            {
                tags[line.Block.FirstLine + i] = match.Groups["id"].Value + (match.Groups["n"].Success ? $"#{match.Groups["n"].Value}" : string.Empty);
            }
        }

        return tags;
    }

    /// <summary>
    /// 이 줄에 그 카드를 펼칠 때 남길 출처 — 처음이면 카드 ID 그대로, 같은 카드가 이미 있으면 둘째부터 <c>F02#2</c>(P5).
    /// </summary>
    public static string NextCardTag(string text, LineRead line, string cardId)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(cardId);

        int highest = 0;

        foreach (Match match in CardCommentPattern().Matches(text.Substring(line.Block.Start, line.Block.Length).Replace("\r\n", "\n")))
        {
            if (match.Groups["id"].Value == cardId)
            {
                highest = Math.Max(highest, match.Groups["n"].Success ? int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture) : 1);
            }
        }

        return highest == 0 ? cardId : string.Create(CultureInfo.InvariantCulture, $"{cardId}#{highest + 1}");
    }

    /// <summary>
    /// 이 줄을 고칠 수 있는가 — LineId가 있고 묶음에 at 없는 명령(<see cref="LineRead.Unanchored"/>)이 없어야 한다.
    /// 아니면 까닭을 돌려준다. at 없는 명령은 실행되지 않는다(런타임 N5 — 원문 검사의 오류). 이 툴은 묶음의 한 줄씩 고치므로
    /// 그 명령은 고친 뒤에도 남는다 — 실행되게 할지(앵커를 붙인다) 지울지는 원문에서 정한다. 까닭에는 명령의 이름을 적는다
    /// (화면은 그 명령을 그리지 않는다 — 실행되지 않으므로). 노드 끝의 것(암묵 스텝)은 어느 줄에도 속하지 않는다.
    /// </summary>
    public static bool IsEditable(LineRead line, out string reason)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.LineId is null)
        {
            reason = "LineId(#line:…)가 없는 줄이다 — 주소가 없어 고칠 수 없다";
            return false;
        }

        if (line.Unanchored.Count > 0)
        {
            string names = string.Join(" · ", line.Unanchored.Select(c => c.Name));
            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"at 없는 명령이 든 줄이다({line.Unanchored.Count}개 — {names}) — 실행되지 않는다. 원문에서 앵커를 붙이거나(at start) 지운 뒤에 고친다");
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 이 줄에서 커맨드를 둘 수 있는 앵커의 이름 — 열리는 차례로. start · end는 언제나 있고,
    /// 대사 글의 표지는 커맨드가 아직 없어도 든다(줄 읽기의 스텝에는 커맨드나 간격이 있는 것만 오른다).
    /// </summary>
    public static IReadOnlyList<string> Anchors(string text, LineRead line)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(line);
        return AnchorOrder(DialogueLine(text, line.Block.Start + line.Block.Length));
    }

    private static EditOutcome ApplyCore(string text, LineEdit edit, StageCommandSpecTable specs)
    {
        YarnFileRead before = YarnLineReader.ReadFile(text, SourceName, specs);
        string id = YarnLineReader.NormalizeLineId(edit.LineId);

        if (!before.TryGet(id, out LineRead line))
        {
            throw new Refusal($"LineId '{CommandAddress.ShortLineId(id)}'가 이 대본에 없다");
        }

        if (before.Lines.Count(l => l.LineId == line.LineId) > 1)
        {
            throw new Refusal($"LineId '{CommandAddress.ShortLineId(id)}'가 이 대본에 둘이다 — 어느 줄인지 가를 수 없다");
        }

        if (!IsEditable(line, out string reason))
        {
            throw new Refusal(reason);
        }

        Block block = new(text, line);
        IReadOnlyList<string> anchors = AnchorOrder(block.Dialogue);

        Plan? plan = edit switch
        {
            SetValue e => PlanSetValue(e, line, block),
            AddCommand e => PlanAdd(e, line, block, anchors, specs),
            RemoveCommand e => PlanRemove(e, line, block),
            ReplaceCommand e => PlanReplace(e, line, block, specs),
            SetDelay e => PlanSetDelay(e, line, block),
            SetGap e => PlanSetGap(e, line, block, anchors),
            _ => throw new Refusal($"모르는 조작이다: {edit.GetType().Name}"),
        };

        if (plan is null)
        {
            CommandAddress? same = edit switch
            {
                SetValue e => e.Target,
                SetDelay e => e.Target,
                ReplaceCommand e => e.Target,
                _ => null,
            };

            return EditOutcome.Unchanged(text, line, same);
        }

        string rewritten = block.Apply(plan, out string? removed, out string? added);
        string after = string.Concat(text.AsSpan(0, block.Start), rewritten, text.AsSpan(block.Start + block.Length));

        // ② 고친 글을 다시 읽는다 — 깨진 yarn은 내지 않는다.
        YarnFileRead reread = YarnLineReader.ReadFile(after, SourceName, specs);

        if (!reread.TryGet(id, out LineRead now) || reread.Lines.Count != before.Lines.Count)
        {
            throw new Refusal("고친 글에서 대사 줄의 수가 달라졌다 — 쓰지 않았다");
        }

        for (int i = 0; i < before.Lines.Count; i++)
        {
            if (!ReferenceEquals(before.Lines[i], line) && Signature(before.Lines[i]) != Signature(reread.Lines[i]))
            {
                throw new Refusal($"고친 글에서 다른 줄({before.Lines[i].Line}행)의 읽기가 달라졌다 — 쓰지 않았다");
            }
        }

        List<string> warnings = [];

        foreach (string problem in NewProblems(before, reread))
        {
            if (IsUnusedMarker(problem))
            {
                warnings.Add(problem);
                continue;
            }

            throw new Refusal($"깨진 yarn이 된다 — 쓰지 않았다: {problem}");
        }

        // ③ 다시 읽은 값이 고치려던 값이다.
        string? mismatch = plan.Check(now);

        if (mismatch is not null)
        {
            throw new Refusal($"다시 읽은 값이 고친 값과 다르다 — 쓰지 않았다: {mismatch}");
        }

        CommandAddress? target = plan.TargetLine is int targetLine && CommandAt(now, targetLine) is { } landed
            ? CommandAddress.Of(now, landed)
            : null;

        return EditOutcome.Applied(after, now, target, removed, added, warnings);
    }

    // ── 값 바꾸기 ───────────────────────────────────────────────────────────

    private static Plan? PlanSetValue(SetValue edit, LineRead line, Block block)
    {
        LineReadCommand command = Find(line, edit.Target);
        StageCommandSpec spec = command.Spec;
        AtLine at = block.ReadAt(command);

        List<string> surplus = [];
        List<BoundArg> bound = spec.Bind(at.Args, surplus);

        if (surplus.Count > 0)
        {
            throw new Refusal($"{surplus[0]} — 칸에 맞지 않는 커맨드는 고치지 않는다");
        }

        int index = bound.FindIndex(b => b.Spec.Name == edit.Arg);

        if (index < 0)
        {
            throw new Refusal($"<<{spec.Name}>>에 '{edit.Arg}' 칸이 없다 — 있는 칸: {string.Join(" · ", spec.Args.Select(a => a.Name))}");
        }

        CommandArgSpec arg = bound[index].Spec;
        string? value = string.IsNullOrEmpty(edit.Value) ? null : edit.Value;

        if (value is null)
        {
            if (!arg.Optional)
            {
                throw new Refusal($"<<{spec.Name}>>의 '{arg.Label}'({arg.Name}) 칸은 꼭 적는다 — 비울 수 없다");
            }

            if (bound[index].Token is null)
            {
                return null;
            }
        }
        else
        {
            CheckToken(spec, arg, value);

            if (value == bound[index].Value)
            {
                return null;
            }
        }

        string?[] desired = bound.Select(b => b.Token).ToArray();
        desired[index] = value;

        string body = AtBody(at.Beat, spec.Name, Compose(spec, desired));
        string? expected = value ?? arg.Default;

        return new Plan(Change.Replace, command.Line, body, command.Line, now =>
        {
            LineReadCommand? read = CommandAt(now, command.Line);
            string? got = read is not null && read.Name == spec.Name && index < read.Bound.Count ? read.Bound[index].Value : null;
            return got == expected ? null : $"'{arg.Name}' 칸이 '{got ?? "∅"}'다(고친 값 '{expected ?? "∅"}')";
        });
    }

    /// <summary>
    /// 칸마다 적을 글(null이면 적지 않는다)을 대본의 인자 열로 편다. 인자는 자리로 읽히므로 뒤 칸을 적으려면
    /// 앞 칸이 모두 적혀 있어야 한다 — 적지 않은 앞 칸은 표의 생략값으로 채운다(뜻이 같다). 생략값이 없으면 거절한다.
    /// 낱말 칸(Flag)은 그 낱말일 때만 자리를 차지한다.
    /// </summary>
    private static List<string> Compose(StageCommandSpec spec, string?[] desired)
    {
        int last = Array.FindLastIndex(desired, d => d is not null);
        List<string> tokens = [];

        for (int i = 0; i <= last; i++)
        {
            CommandArgSpec arg = spec.Args[i];

            if (arg.Flag)
            {
                if (desired[i] is { } flag)
                {
                    tokens.Add(flag);
                }

                continue;
            }

            string? token = desired[i] ?? arg.Default;

            if (string.IsNullOrEmpty(token))
            {
                CommandArgSpec behind = spec.Args[last];
                throw new Refusal(
                    $"<<{spec.Name}>>의 '{arg.Label}'({arg.Name}) 칸이 비어 있고 생략값도 없다 — " +
                    $"뒤 칸 '{behind.Label}'({behind.Name})이 적혀 있으려면 그 칸부터 적어야 한다");
            }

            tokens.Add(token);
        }

        return tokens;
    }

    // ── 커맨드 더하기 ───────────────────────────────────────────────────────

    private static Plan PlanAdd(
        AddCommand edit, LineRead line, Block block, IReadOnlyList<string> anchors, StageCommandSpecTable specs)
    {
        if (!specs.TryGet(edit.Name, out StageCommandSpec spec))
        {
            throw new Refusal($"<<{edit.Name}>>은 명세 표에 없는 커맨드다 — 종류와 인자 칸을 모르는 것은 더하지 않는다");
        }

        if (spec.Immediate)
        {
            throw new Refusal($"<<{spec.Name}>>은 바로 실행하는 명령이다 — 앵커에 속하지 않아 이 조작으로 더하지 않는다");
        }

        RequireAnchor(anchors, edit.Anchor);

        IReadOnlyList<string> args = edit.Args ?? [];
        CheckArguments(spec, args);

        LineReadStep? step = line.Step(edit.Anchor);
        List<LineReadCommand> sameKind = step is null
            ? []
            : step.Commands.Where(c => c.Spec?.Kind == spec.Kind).OrderBy(c => c.KindIndex).ToList();

        int at;

        if (edit.KindIndex is int kindIndex)
        {
            if (kindIndex < 0 || kindIndex > sameKind.Count)
            {
                throw new Refusal(
                    $"순번 {kindIndex}에 끼울 수 없다 — 앵커 '{edit.Anchor}'의 {CommandKinds.Label(spec.Kind)} 커맨드는 {sameKind.Count}개다(0 ~ {sameKind.Count})");
            }

            at = kindIndex < sameKind.Count ? sameKind[kindIndex].Line : EndOfAnchor(line, step, block, anchors, edit.Anchor);
        }
        else
        {
            at = EndOfAnchor(line, step, block, anchors, edit.Anchor);
        }

        string[] tokens = args.ToArray();
        string? comment = null;

        if (edit.Card is not null)
        {
            if (!CardTagPattern().IsMatch(edit.Card))
            {
                throw new Refusal($"카드의 출처 '{edit.Card}'가 카드 ID(E04 · F02#2)의 꼴이 아니다");
            }

            comment = CardTagPrefix + edit.Card;
        }

        return new Plan(Change.Insert, at, AtBody(edit.Anchor, spec.Name, tokens), at, now =>
        {
            LineReadCommand? read = CommandAt(now, at);
            return read is not null && read.Name == spec.Name && read.Anchor == edit.Anchor && read.Args.SequenceEqual(tokens)
                ? null
                : $"앵커 '{edit.Anchor}'에 <<{spec.Name}>>이 그대로 읽히지 않는다";
        })
        {
            Comment = comment,
        };
    }

    // ── 커맨드 바꾸기 ───────────────────────────────────────────────────────

    private static Plan? PlanReplace(ReplaceCommand edit, LineRead line, Block block, StageCommandSpecTable specs)
    {
        LineReadCommand command = Find(line, edit.Target);
        AtLine at = block.ReadAt(command);

        if (!specs.TryGet(edit.Name, out StageCommandSpec spec))
        {
            throw new Refusal($"<<{edit.Name}>>은 명세 표에 없는 커맨드다 — 종류와 인자 칸을 모르는 것으로는 바꾸지 않는다");
        }

        if (spec.Immediate)
        {
            throw new Refusal($"<<{spec.Name}>>은 바로 실행하는 명령이다 — 앵커에 속하지 않아 바꿔 넣지 않는다");
        }

        string[] tokens = [.. edit.Args ?? []];
        CheckArguments(spec, tokens);

        if (spec.Name == command.Name && tokens.SequenceEqual(at.Args))
        {
            return null;
        }

        return new Plan(Change.Replace, command.Line, AtBody(at.Beat, spec.Name, tokens), command.Line, now =>
        {
            LineReadCommand? read = CommandAt(now, command.Line);
            return read is not null && read.Name == spec.Name && read.Anchor == command.Anchor && read.Args.SequenceEqual(tokens)
                ? null
                : $"앵커 '{command.Anchor}'에 <<{spec.Name}>>이 그대로 읽히지 않는다";
        })
        {
            DropCard = true,
        };
    }

    // 더하거나 바꿔 넣을 인자 — 칸에 맞고, 낱말이 갈래에 맞고, 꼭 적는 칸이 다 적혀야 한다.
    private static void CheckArguments(StageCommandSpec spec, IReadOnlyList<string> args)
    {
        List<string> surplus = [];
        List<BoundArg> bound = spec.Bind(args, surplus);

        if (surplus.Count > 0)
        {
            throw new Refusal($"{surplus[0]} ({string.Join(" · ", spec.Args.Select(a => a.Name))})");
        }

        foreach (BoundArg arg in bound)
        {
            if (arg.Token is not null)
            {
                CheckToken(spec, arg.Spec, arg.Token);
            }
            else if (!arg.Spec.Optional)
            {
                throw new Refusal($"<<{spec.Name}>>의 '{arg.Spec.Label}'({arg.Spec.Name}) 칸은 꼭 적는다");
            }
        }
    }

    // ── 커맨드 빼기 ─────────────────────────────────────────────────────────

    private static Plan PlanRemove(RemoveCommand edit, LineRead line, Block block)
    {
        LineReadCommand command = Find(line, edit.Target);
        block.ReadAt(command);

        int count = CommandCount(line);

        return new Plan(Change.Remove, command.Line, null, null, now =>
            CommandCount(now) == count - 1 ? null : $"커맨드가 {CommandCount(now)}개다(하나를 뺀 {count - 1}개여야 한다)");
    }

    // ── 지연 바꾸기 ─────────────────────────────────────────────────────────

    private static Plan? PlanSetDelay(SetDelay edit, LineRead line, Block block)
    {
        if (!DurationToken.TryParseSeconds(edit.Delay, out float target))
        {
            throw new Refusal($"지연 '{edit.Delay}'가 시간 토큰(15fr · 0.5s)이 아니다");
        }

        LineReadCommand command = Find(line, edit.Target);
        LineReadStep step = line.Step(edit.Target.Anchor);
        int order = step.Commands.IndexOf(command);
        LineReadCommand? previous = order > 0 ? step.Commands[order - 1] : null;
        bool cushioned = previous is { Name: YarnCommandTextExtractor.PauseCommand };

        // 바로 앞의 pause를 뺀 나머지 — 그보다 앞선 기다림은 이 조작이 건드리지 않는다.
        float floor = command.Delay - (cushioned ? previous!.Hold : 0f);
        float need = target - floor;

        if (need < -Epsilon)
        {
            throw new Refusal(
                $"앞선 커맨드의 기다림이 이미 {Seconds(floor)}다 — {Seconds(target)}로 당길 수 없다(앞 커맨드의 지연을 먼저 줄인다)");
        }

        Func<LineRead, int, string?> check = (now, targetLine) =>
        {
            LineReadCommand? read = CommandAt(now, targetLine);
            return read is not null && read.Name == command.Name && MathF.Abs(read.Delay - target) <= ReadBackTolerance
                ? null
                : $"지연이 {(read is null ? "∅" : Seconds(read.Delay))}다(고친 값 {Seconds(target)})";
        };

        if (need <= Epsilon)
        {
            if (!cushioned)
            {
                return null;
            }

            block.ReadAt(previous!);
            int moved = command.Line > previous!.Line ? command.Line - 1 : command.Line;
            return new Plan(Change.Remove, previous.Line, null, moved, now => check(now, moved));
        }

        bool frames = edit.Delay.Trim().EndsWith("fr", StringComparison.OrdinalIgnoreCase);
        string token = FormatDuration(need, frames);

        if (cushioned)
        {
            if (MathF.Abs(need - previous!.Hold) <= Epsilon)
            {
                return null;
            }

            AtLine pause = block.ReadAt(previous);
            return new Plan(
                Change.Replace, previous.Line, AtBody(pause.Beat, YarnCommandTextExtractor.PauseCommand, [token]),
                command.Line, now => check(now, command.Line));
        }

        AtLine at = block.ReadAt(command);
        int pushed = command.Line + 1;

        return new Plan(
            Change.Insert, command.Line, AtBody(at.Beat, YarnCommandTextExtractor.PauseCommand, [token]),
            pushed, now => check(now, pushed));
    }

    // ── 간격 바꾸기 ─────────────────────────────────────────────────────────

    private static Plan? PlanSetGap(SetGap edit, LineRead line, Block block, IReadOnlyList<string> anchors)
    {
        RequireAnchor(anchors, edit.Anchor);

        LineReadStep? step = line.Step(edit.Anchor);
        float seconds = 0f;

        if (!string.IsNullOrWhiteSpace(edit.Gap) && !DurationToken.TryParseSeconds(edit.Gap, out seconds))
        {
            throw new Refusal($"간격 '{edit.Gap}'이 시간 토큰(12fr · 1s)이 아니다");
        }

        if (seconds <= Epsilon)
        {
            if (step is not { HasGap: true })
            {
                return null;
            }

            block.ReadGap(step.GapLine, edit.Anchor);
            return new Plan(Change.Remove, step.GapLine, null, null, now =>
                now.Step(edit.Anchor) is { HasGap: true } ? $"앵커 '{edit.Anchor}'의 간격이 남아 있다" : null);
        }

        string body = $"{LineBeatSchedule.GapCommandName} {edit.Anchor} {edit.Gap!.Trim()}";

        string? Check(LineRead now)
            => now.Step(edit.Anchor) is { HasGap: true } read && MathF.Abs(read.Gap - seconds) <= ReadBackTolerance
                ? null
                : $"앵커 '{edit.Anchor}'의 간격이 {Seconds(seconds)}로 읽히지 않는다";

        if (step is { HasGap: true })
        {
            if (MathF.Abs(step.Gap - seconds) <= Epsilon)
            {
                return null;
            }

            block.ReadGap(step.GapLine, edit.Anchor);
            return new Plan(Change.Replace, step.GapLine, body, null, Check);
        }

        // 간격은 그 앵커의 커맨드 뒤에 적는다(docs/PLAN_line-anchor.md §3의 꼴).
        int at = step is { Commands.Count: > 0 }
            ? step.Commands.Max(c => c.Line) + 1
            : EmptyAnchorSpot(line, block, anchors, edit.Anchor);

        return new Plan(Change.Insert, at, body, null, Check);
    }

    // ── 찾기 · 자리 ─────────────────────────────────────────────────────────

    private static LineReadCommand Find(LineRead line, CommandAddress address)
    {
        LineReadStep? step = line.Step(address.Anchor);

        if (step is null)
        {
            throw new Refusal(
                $"이 줄에 앵커 '{address.Anchor}'의 커맨드가 없다 — 커맨드가 있는 앵커: {string.Join(" · ", line.Steps.Where(s => s.Commands.Count > 0).Select(s => s.Name))}");
        }

        LineReadCommand? command = step.Commands.FirstOrDefault(c => c.Spec?.Kind == address.Kind && c.KindIndex == address.Index);

        if (command is null)
        {
            int count = step.Commands.Count(c => c.Spec?.Kind == address.Kind);
            throw new Refusal($"{address}가 없다 — 앵커 '{address.Anchor}'의 {CommandKinds.Label(address.Kind)} 커맨드는 {count}개다");
        }

        return command;
    }

    private static void RequireAnchor(IReadOnlyList<string> anchors, string anchor)
    {
        if (!anchors.Contains(anchor))
        {
            throw new Refusal(
                $"이 줄에 앵커 '{anchor}'가 없다 — 있는 앵커: {string.Join(" · ", anchors)}. 표지를 새로 만드는 일은 이 툴이 하지 않는다(대사 글을 고쳐야 한다)");
        }
    }

    /// <summary>그 앵커의 맨 뒤 — 커맨드가 있으면 마지막 커맨드의 다음 줄.</summary>
    private static int EndOfAnchor(LineRead line, LineReadStep? step, Block block, IReadOnlyList<string> anchors, string anchor)
        => step is { Commands.Count: > 0 }
            ? step.Commands.Max(c => c.Line) + 1
            : EmptyAnchorSpot(line, block, anchors, anchor);

    /// <summary>
    /// 커맨드가 아직 없는 앵커의 첫 줄이 들어갈 자리 — 묶음이 앵커의 차례대로 읽히게 둔다:
    /// 제 간격 줄이 있으면 그 앞, 없으면 뒤 앵커의 첫 줄 앞, 그것도 없으면 묶음의 마지막 커맨드 줄 다음.
    /// </summary>
    private static int EmptyAnchorSpot(LineRead line, Block block, IReadOnlyList<string> anchors, string anchor)
    {
        if (line.Step(anchor) is { HasGap: true } own)
        {
            return own.GapLine;
        }

        int order = anchors.ToList().IndexOf(anchor);
        int first = int.MaxValue;

        foreach (LineReadStep later in line.Steps)
        {
            if (anchors.ToList().IndexOf(later.Name) <= order)
            {
                continue;
            }

            foreach (LineReadCommand command in later.Commands)
            {
                first = Math.Min(first, command.Line);
            }

            if (later.HasGap)
            {
                first = Math.Min(first, later.GapLine);
            }
        }

        return first == int.MaxValue ? block.AfterLastCommand : first;
    }

    private static LineReadCommand? CommandAt(LineRead line, int fileLine)
        => line.Steps.SelectMany(s => s.Commands).FirstOrDefault(c => c.Line == fileLine);

    private static int CommandCount(LineRead line) => line.Steps.Sum(s => s.Commands.Count);

    private static IReadOnlyList<string> AnchorOrder(string dialogue)
    {
        string code = dialogue;
        int comment = code.IndexOf("//", StringComparison.Ordinal);

        if (comment >= 0)
        {
            code = code[..comment];
        }

        int tags = code.IndexOf('#');

        if (tags >= 0)
        {
            code = code[..tags];
        }

        List<BeatMarker> markers = [];
        string plain = YarnCommandTextExtractor.StripLineMarkers(code.Trim().TrimStart('﻿'), markers);
        int bodyStart = YarnCommandTextExtractor.CharacterPrefixLength(plain);

        for (int i = 0; i < markers.Count; i++)
        {
            markers[i] = new BeatMarker(markers[i].Name, Math.Max(0, markers[i].Position - bodyStart));
        }

        // 차례는 코어가 정한다(재생과 같은 함수) — 표지마다 커맨드가 하나씩 있다고 치고 묻는다.
        List<AtCommand> probes = markers
            .Select(m => m.Name)
            .Where(LineBeatSchedule.IsValidBeatName)
            .Distinct(StringComparer.Ordinal)
            .Select(name => new AtCommand(name, new StageCommand(YarnCommandTextExtractor.PauseCommand, [])))
            .ToList();

        return LineBeatSchedule.BuildAnchors(markers, probes).Select(a => a.Name).ToList();
    }

    private static string DialogueLine(string text, int start)
    {
        int end = text.IndexOf('\n', start);
        return (end < 0 ? text[start..] : text[start..end]).TrimEnd('\r');
    }

    // ── 문제 견주기 ─────────────────────────────────────────────────────────

    /// <summary>고친 뒤에 새로 생긴 문제 — 줄 번호는 밀리므로 떼고 견준다.</summary>
    private static List<string> NewProblems(YarnFileRead before, YarnFileRead after)
    {
        Dictionary<string, int> known = new(StringComparer.Ordinal);

        foreach (string problem in AllProblems(before))
        {
            known[problem] = known.GetValueOrDefault(problem) + 1;
        }

        List<string> fresh = [];

        foreach (string problem in AllProblems(after))
        {
            if (known.GetValueOrDefault(problem) > 0)
            {
                known[problem]--;
            }
            else
            {
                fresh.Add(problem);
            }
        }

        return fresh;
    }

    private static IEnumerable<string> AllProblems(YarnFileRead file)
        => file.Problems.Concat(file.Lines.SelectMany(l => l.Problems)).Select(p => SourceLinePattern().Replace(p, SourceName));

    // 커맨드도 간격도 남지 않은 표지 — 원문 검사에서 경고다(docs/PLAN_line-anchor.md AR7). 표지는 대사 글의 것이라
    // 이 툴이 지울 수 없다. 코어(LineBeatSchedule.BuildAnchors)가 적는 글에 맞춘 것이라, 그 글이 바뀌면
    // 마지막 커맨드를 빼는 시험이 실패해 알린다.
    private static bool IsUnusedMarker(string problem)
        => problem.StartsWith("[beat=", StringComparison.Ordinal) && problem.EndsWith("가 없다 — 무시한다", StringComparison.Ordinal);

    private static string Signature(LineRead line)
        => string.Join('\n', line.Steps.Select(s =>
            string.Create(CultureInfo.InvariantCulture, $"{s.Name}|{s.Kind}|{s.Gap}|{s.Text}|") +
            string.Join(';', s.Commands.Select(c =>
                string.Create(CultureInfo.InvariantCulture, $"{c.Name} {string.Join(' ', c.Args)} +{c.Delay}")))))
           + "\n!" + string.Join(';', line.Immediate.Select(c => $"{c.Name} {string.Join(' ', c.Args)}"));

    // ── 글 ──────────────────────────────────────────────────────────────────

    private static void CheckToken(StageCommandSpec spec, CommandArgSpec arg, string value)
    {
        if (value.Any(char.IsWhiteSpace) || value.Contains('"') ||
            value.Contains("<<", StringComparison.Ordinal) || value.Contains(">>", StringComparison.Ordinal) ||
            value.Contains("//", StringComparison.Ordinal))
        {
            throw new Refusal($"'{value}' — 값에는 공백 · 따옴표 · << · >> · //를 쓸 수 없다");
        }

        if (arg.Kind == ArgKind.Duration && !DurationToken.TryParseSeconds(value, out _))
        {
            throw new Refusal($"<<{spec.Name}>>의 '{arg.Label}'({arg.Name}) 칸은 시간 토큰이다(12fr · 0.4s) — '{value}'는 읽지 못한다");
        }

        if (arg.Choices.Count > 0 && !arg.Choices.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new Refusal($"<<{spec.Name}>>의 '{arg.Label}'({arg.Name}) 칸은 {string.Join(" · ", arg.Choices)} 가운데 하나다 — '{value}'는 아니다");
        }
    }

    private static string AtBody(string beat, string name, IEnumerable<string> tokens)
        => string.Join(' ', new[] { LineBeatSchedule.AtCommandName, beat, name }.Concat(tokens.Select(Quote)));

    // 대본에서 따옴표로 묶여 있던 인자(공백이 든 것)는 다시 묶는다 — 읽을 때 따옴표가 벗겨져 온다.
    private static string Quote(string token) => token.Any(char.IsWhiteSpace) ? $"\"{token}\"" : token;

    /// <summary>초를 시간 토큰으로 — 프레임으로 받았고 프레임에 맞아떨어지면 <c>Nfr</c>, 아니면 <c>N.NNNNs</c>.</summary>
    internal static string FormatDuration(float seconds, bool frames)
    {
        if (frames)
        {
            float count = seconds * DurationToken.FramesPerSecond;
            float rounded = MathF.Round(count);

            if (rounded >= 1f && MathF.Abs(count - rounded) < 0.001f)
            {
                return rounded.ToString("0", CultureInfo.InvariantCulture) + "fr";
            }
        }

        return Seconds(seconds);
    }

    private static string Seconds(float seconds) => seconds.ToString("0.####", CultureInfo.InvariantCulture) + "s";

    // ── 묶음 ────────────────────────────────────────────────────────────────

    private enum Change
    {
        /// <summary>그 줄의 <c>&lt;&lt;…&gt;&gt;</c> 안만 바꾼다 — 들여쓰기 · 줄 끝 주석 · 줄바꿈은 그대로.</summary>
        Replace,

        /// <summary>그 줄 앞에 새 줄을 넣는다.</summary>
        Insert,

        /// <summary>그 줄을 뺀다.</summary>
        Remove,
    }

    /// <summary>묶음의 한 줄에 할 일. 줄 번호는 고치기 전 파일의 것(1부터)이다.</summary>
    /// <param name="TargetLine">고친 뒤 파일에서 대상 커맨드가 있는 줄. 대상이 커맨드가 아니거나 사라지면 null.</param>
    /// <param name="Check">고친 줄을 다시 읽은 것을 받아, 고친 값과 다르면 까닭을 돌려준다.</param>
    private sealed record Plan(Change Change, int FileLine, string? Body, int? TargetLine, Func<LineRead, string?> Check)
    {
        /// <summary>넣는 줄의 끝에 붙일 주석(<c>//</c> 뒤의 글). 없으면 null.</summary>
        public string? Comment { get; init; }

        /// <summary>바꾼 줄의 카드 출처를 걷는가(바꾸기).</summary>
        public bool DropCard { get; init; }
    }

    private readonly record struct AtLine(string Beat, IReadOnlyList<string> Args);

    /// <summary>그 줄의 커맨드 묶음을 물리적인 줄로 편 것 — 줄마다 제 줄바꿈까지 든다.</summary>
    private sealed class Block
    {
        private readonly List<string> _lines = [];
        private readonly int _firstLine;
        private readonly string _indent;
        private readonly string _eol;

        public Block(string text, LineRead line)
        {
            Start = line.Block.Start;
            Length = line.Block.Length;
            _firstLine = line.Block.FirstLine;
            _indent = line.Indent;

            int at = Start;
            int end = Start + Length;

            while (at < end)
            {
                int newline = text.IndexOf('\n', at, end - at);
                int next = newline < 0 ? end : newline + 1;
                _lines.Add(text[at..next]);
                at = next;
            }

            if (_firstLine + _lines.Count != line.Line)
            {
                throw new Refusal("묶음의 줄 수가 줄 읽기와 맞지 않는다 — 고치지 않는다");
            }

            Dialogue = DialogueLine(text, end);
            _eol = EndOfLine(text, end);
        }

        public int Start { get; }

        public int Length { get; }

        /// <summary>대사 줄의 글(줄바꿈 없이).</summary>
        public string Dialogue { get; }

        /// <summary>묶음의 마지막 커맨드 줄 다음 — 빈 줄 · 주석이 대사 줄과의 사이에 있으면 그 앞이다. 묶음이 비었으면 대사 줄.</summary>
        public int AfterLastCommand
        {
            get
            {
                int last = _lines.FindLastIndex(l => l.TrimStart().StartsWith("<<", StringComparison.Ordinal));
                return _firstLine + (last < 0 ? _lines.Count : last + 1);
            }
        }

        /// <summary>그 커맨드의 <c>&lt;&lt;at 표지 이름 인자…&gt;&gt;</c> 줄을 원문에서 다시 읽는다 — 표지는 쓴 그대로 지킨다.</summary>
        public AtLine ReadAt(LineReadCommand command)
        {
            StageCommand parsed = Parse(command.Line);

            if (parsed.Name != LineBeatSchedule.AtCommandName || parsed.Args.Count < 2 || parsed.Args[1] != command.Name)
            {
                throw new Refusal($"{command.Line}행이 <<at … {command.Name} …>>의 꼴이 아니다 — 고치지 않는다");
            }

            return new AtLine(parsed.Args[0], parsed.Args.Skip(2).ToArray());
        }

        public void ReadGap(int fileLine, string anchor)
        {
            StageCommand parsed = Parse(fileLine);

            if (parsed.Name != LineBeatSchedule.GapCommandName || parsed.Args.Count < 1 || parsed.Args[0] != anchor)
            {
                throw new Refusal($"{fileLine}행이 <<gap {anchor} …>>의 꼴이 아니다 — 고치지 않는다");
            }
        }

        public string Apply(Plan plan, out string? removed, out string? added)
        {
            removed = null;
            added = null;

            int index = plan.FileLine - _firstLine;
            List<string> lines = [.. _lines];

            switch (plan.Change)
            {
                case Change.Replace:
                    string replaced = Rewrite(Line(plan.FileLine), plan.Body!);

                    if (plan.DropCard)
                    {
                        replaced = WithoutCard(replaced);
                    }

                    removed = lines[index].Trim();
                    added = replaced.Trim();
                    lines[index] = replaced;
                    break;

                case Change.Insert:
                    if (index < 0 || index > lines.Count)
                    {
                        throw new Refusal($"{plan.FileLine}행은 이 줄의 묶음 밖이다 — 고치지 않는다");
                    }

                    string inserted = $"{_indent}<<{plan.Body}>>{(plan.Comment is null ? string.Empty : $" // {plan.Comment}")}{_eol}";
                    added = inserted.Trim();
                    lines.Insert(index, inserted);
                    break;

                case Change.Remove:
                    removed = Line(plan.FileLine).Trim();
                    lines.RemoveAt(index);
                    break;
            }

            return string.Concat(lines);
        }

        private string Line(int fileLine)
        {
            int index = fileLine - _firstLine;

            if (index < 0 || index >= _lines.Count)
            {
                throw new Refusal($"{fileLine}행은 이 줄의 묶음 밖이다 — 고치지 않는다");
            }

            return _lines[index];
        }

        private StageCommand Parse(int fileLine)
        {
            string raw = Line(fileLine);

            if (!YarnCommandTextExtractor.TryParseCommand(raw[..CodeEnd(raw)], null, out StageCommand parsed))
            {
                throw new Refusal($"{fileLine}행을 커맨드로 읽지 못했다 — 고치지 않는다");
            }

            return parsed;
        }

        private static int CodeEnd(string raw)
        {
            int comment = raw.IndexOf("//", StringComparison.Ordinal);
            return comment < 0 ? raw.Length : comment;
        }

        // 줄 끝의 카드 출처(// card:E04)만 걷는다 — 줄바꿈과 다른 주석은 그대로다.
        private static string WithoutCard(string raw)
        {
            string code = raw.TrimEnd('\r', '\n');
            Match card = CardCommentPattern().Match(code);
            return card.Success ? string.Concat(code.AsSpan(0, card.Index).TrimEnd(), raw.AsSpan(code.Length)) : raw;
        }

        private static string Rewrite(string raw, string body)
        {
            ReadOnlySpan<char> code = raw.AsSpan(0, CodeEnd(raw));
            int open = code.IndexOf("<<");
            int close = code.LastIndexOf(">>");

            if (open < 0 || close < open + 2)
            {
                throw new Refusal("커맨드 줄의 << >>를 찾지 못했다 — 고치지 않는다");
            }

            return string.Concat(raw.AsSpan(0, open + 2), body, raw.AsSpan(close));
        }

        // 새 줄의 줄바꿈 — 대사 줄의 것을 따른다. 대사 줄이 파일의 마지막 줄이라 줄바꿈이 없으면 파일의 첫 줄바꿈.
        private static string EndOfLine(string text, int dialogueStart)
        {
            int newline = text.IndexOf('\n', dialogueStart);

            if (newline < 0)
            {
                newline = text.IndexOf('\n');
            }

            return newline > 0 && text[newline - 1] == '\r' ? "\r\n" : "\n";
        }
    }

    private sealed class Refusal(string message) : Exception(message);
}
