using System.Globalization;
using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.Cli;

/// <summary>줄 읽기의 결과를 글과 JSON으로 편다 — 커맨드마다 주소(<c>LineId/앵커/종류/순번</c>)를 붙인다.</summary>
internal static class LineView
{
    private const int ExcerptLength = 28;

    public static bool Has(LineRead line, IReadOnlySet<CommandKind> kinds)
        => line.Steps.Any(s => s.Commands.Any(c => c.Spec is not null && kinds.Contains(c.Spec.Kind)));

    // ── 목록의 한 줄 ────────────────────────────────────────────────────────

    public static string Summary(LineRead line)
    {
        string id = line.LineId is null ? "(LineId 없음)" : CommandAddress.ShortLineId(line.LineId);
        string speaker = line.Speaker.Length > 0 ? $"{line.Speaker}: " : string.Empty;
        string counts = string.Join(" · ", Counts(line).Where(c => c.Value > 0).Select(c => $"{CommandKinds.Label(c.Key)} {c.Value}"));
        string readOnly = LineEditor.IsEditable(line, out _) ? string.Empty : "  [읽기만]";

        return $"  {id,-16} {line.Line,5}행  {speaker}{Excerpt(line.Body)}  ({(counts.Length > 0 ? counts : "커맨드 없음")}){readOnly}";
    }

    public static object SummaryJson(LineRead line)
    {
        bool editable = LineEditor.IsEditable(line, out string reason);

        return new
        {
            lineId = line.LineId is null ? null : CommandAddress.ShortLineId(line.LineId),
            line = line.Line,
            node = line.NodeName,
            speaker = line.Speaker,
            text = line.Body,
            editable,
            readOnlyReason = editable ? null : reason,
            counts = Counts(line).ToDictionary(c => CommandKinds.Key(c.Key), c => c.Value),
        };
    }

    // ── 줄 하나 ─────────────────────────────────────────────────────────────

    public static void Write(TextWriter output, string text, LineRead line, IReadOnlySet<CommandKind>? kinds)
    {
        bool editable = LineEditor.IsEditable(line, out string reason);
        string id = line.LineId is null ? "(LineId 없음)" : CommandAddress.ShortLineId(line.LineId);
        string speaker = line.Speaker.Length > 0 ? $" · {line.Speaker}" : string.Empty;

        output.WriteLine($"{id}{speaker} · {line.NodeName} · {line.Line}행{(editable ? string.Empty : $"  [읽기만 — {reason}]")}");

        if (kinds is not null)
        {
            output.WriteLine($"  종류: {string.Join(" · ", kinds.Select(CommandKinds.Label))}");
        }

        foreach (string anchor in LineEditor.Anchors(text, line).Concat(line.Steps.Select(s => s.Name)).Distinct())
        {
            LineReadStep? step = line.Step(anchor);
            string segment = step is { Text.Length: > 0 } ? $"  \"{step.Text}\"" : string.Empty;
            string gap = step is { HasGap: true } ? $"  간격 {Seconds(step.Gap)}" : string.Empty;

            output.WriteLine($"  {anchor,-6}{segment}{gap}".TrimEnd());

            foreach (LineReadCommand command in step?.Commands ?? [])
            {
                if (command.Spec is null)
                {
                    if (kinds is null)
                    {
                        output.WriteLine($"      {"(주소 없음)",-22} +{Seconds(command.Delay),-7} {command.Name} {string.Join(' ', command.Args)}  — 명세 표에 없다");
                    }

                    continue;
                }

                if (kinds is not null && !kinds.Contains(command.Spec.Kind))
                {
                    continue;
                }

                string address = $"{anchor}/{CommandKinds.Key(command.Spec.Kind)}/{command.KindIndex}";
                string args = string.Join(' ', command.Bound.Select(ArgText).Where(a => a.Length > 0));
                string waits = command.Waits ? $"  [기다린다 {Seconds(command.Hold)}]" : string.Empty;

                output.WriteLine($"      {address,-22} +{Seconds(command.Delay),-7} {command.Name}  {args}{waits}".TrimEnd());
            }
        }

        if (kinds is null && line.Immediate.Count > 0)
        {
            output.WriteLine($"  앵커 밖(바로 실행): {string.Join(" · ", line.Immediate.Select(c => $"{c.Name} {string.Join(' ', c.Args)}".TrimEnd()))}");
        }

        foreach (string problem in line.Problems)
        {
            output.WriteLine($"  문제: {problem}");
        }
    }

    public static object Json(string text, LineRead line, IReadOnlySet<CommandKind>? kinds)
    {
        bool editable = LineEditor.IsEditable(line, out string reason);
        string? id = line.LineId is null ? null : CommandAddress.ShortLineId(line.LineId);

        return new
        {
            lineId = id,
            line = line.Line,
            node = line.NodeName,
            speaker = line.Speaker,
            text = line.Body,
            editable,
            readOnlyReason = editable ? null : reason,
            anchors = LineEditor.Anchors(text, line),
            steps = line.Steps.Select(step => new
            {
                anchor = step.Name,
                kind = step.Kind.ToString().ToLowerInvariant(),
                text = step.Text,
                gap = step.HasGap ? step.Gap : (float?)null,
                commands = step.Commands
                    .Where(c => kinds is null || (c.Spec is not null && kinds.Contains(c.Spec.Kind)))
                    .Select(c => CommandJson(line, c))
                    .ToList(),
            }).ToList(),
            immediate = line.Immediate.Select(c => new { name = c.Name, args = c.Args }).ToList(),
            problems = line.Problems,
        };
    }

    private static object CommandJson(LineRead line, LineReadCommand command) => new
    {
        address = CommandAddress.Of(line, command)?.ToString(),
        name = command.Name,
        kind = command.Spec is null ? null : CommandKinds.Key(command.Spec.Kind),
        index = command.KindIndex,
        delay = command.Delay,
        duration = command.Duration,
        waits = command.Waits,
        line = command.Line,
        args = command.Spec is null
            ? command.Args.Select(a => (object)new { value = a }).ToList()
            : command.Bound.Select(b => (object)new
            {
                name = b.Spec.Name,
                label = b.Spec.Label,
                kind = ArgKinds.Key(b.Spec.Kind),
                value = b.Value,
                written = b.Token is not null,
            }).ToList(),
    };

    // ── 어휘 ────────────────────────────────────────────────────────────────

    public static string ArgSignature(CommandArgSpec arg)
        => arg.Default is { Length: > 0 } ? $"[{arg.Name}={arg.Default}]" : arg.Optional ? $"[{arg.Name}]" : $"<{arg.Name}>";

    public static object SpecJson(StageCommandSpec spec) => new
    {
        name = spec.Name,
        kind = CommandKinds.Key(spec.Kind),
        summary = spec.Summary,
        waits = spec.Waits,
        immediate = spec.Immediate,
        args = spec.Args.Select(a => new
        {
            name = a.Name,
            label = a.Label,
            kind = ArgKinds.Key(a.Kind),
            @default = a.Default,
            optional = a.Optional,
            flag = a.Flag,
            catalog = a.Catalog,
            choices = a.Choices,
            note = a.Note,
        }).ToList(),
    };

    // ── 글 ──────────────────────────────────────────────────────────────────

    // 적은 칸은 칸=값, 적지 않았지만 생략값이 있는 칸은 칸=(값). 적지 않았고 생략값도 없는 칸은 보이지 않는다.
    private static string ArgText(BoundArg arg)
        => arg.Token is not null ? $"{arg.Spec.Name}={arg.Token}"
            : arg.Spec.Default is { Length: > 0 } fallback ? $"{arg.Spec.Name}=({fallback})"
            : string.Empty;

    private static IEnumerable<KeyValuePair<CommandKind, int>> Counts(LineRead line)
        => CommandKinds.All.Select(kind => KeyValuePair.Create(
            kind, line.Steps.Sum(s => s.Commands.Count(c => c.Spec?.Kind == kind))));

    private static string Excerpt(string body)
        => body.Length <= ExcerptLength ? body : string.Concat(body.AsSpan(0, ExcerptLength), "…");

    private static string Seconds(float seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";
}
