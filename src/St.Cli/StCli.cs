using System.Text.Encodings.Web;
using System.Text.Json;
using Ked.Presentation.Core;
using St.Authoring.Editing;
using St.Authoring.Scripts;

namespace St.Cli;

/// <summary>
/// <c>st</c> — 연출 고치기의 명령줄 입구. 창 없이 yarn을 <c>LineId / 앵커 / 종류 / 순번</c>으로 읽고 조작 다섯으로 고친다.
///
/// 결과는 표준 출력에 — 기본은 사람과 AI가 함께 읽는 글이고 <c>--json</c>이면 JSON 하나다. 거절 · 오류의 까닭은 표준 오류에.
/// 종료 코드는 0 = 됨 · 1 = 거절(쓰지 않았다) · 2 = 쓰는 법이 틀렸다.
/// 화면과 같은 함수를 지난다(<see cref="LineEditor"/>) — 여기서 거절되는 것은 화면에서도 거절된다.
/// </summary>
public static partial class StCli
{
    public const string Usage = """
        st — ked-story-tools 명령줄 (종료 코드 0 됨 · 1 거절 — 쓰지 않았다 · 2 쓰는 법)

        읽기
          st read <경로>                               대사 줄의 목록 — LineId · 화자 · 종류별 커맨드 수
          st read <경로> --kind <종류>[,<종류>]         그 종류의 커맨드가 있는 줄만
          st read <경로> --line <LineId> [--kind …]    그 줄 — 앵커(스텝)마다 커맨드의 주소 · 지연 · 인자 칸
          st vocab [--kind <종류>]                     명세 표의 어휘 — 커맨드와 인자 칸

        고치기 — 그 줄의 커맨드 묶음만 다시 쓴다. 깨진 yarn이 되면 거절하고 쓰지 않는다
          st set    <경로> <주소> <칸> <값>             값 바꾸기 (값 대신 --clear면 그 칸을 비운다)
          st add    <경로> <LineId> <앵커> <커맨드> [인자…] [--index <순번>] [--card <카드 ID>]
                                                       커맨드 더하기 — 앵커의 맨 뒤에, --index면 그 종류의 그 순번에.
                                                       --card면 줄 끝에 출처를 남긴다(// card:E04)
          st remove <경로> <주소>                      커맨드 빼기
          st delay  <경로> <주소> <길이>                지연 바꾸기 — 앵커가 열린 뒤 그 커맨드가 시작하기까지
          st gap    <경로> <LineId> <앵커> <길이>       간격 바꾸기 — 그 앵커 뒤 다음 앵커까지의 최소 시간 (--clear면 걷는다)

          <경로>   yarn 파일 하나, 또는 대본 폴더(그 아래의 *.yarn — LineId로 파일을 찾는다)
          <주소>   LineId/앵커/종류/순번 — st read --line 이 커맨드마다 보여 준다 (예: ln_abc/start/acting/0)
          <종류>   camera · placement · acting · screen · setup · sound · time
          <길이>   시간 토큰 — 15fr · 0.5s
          --json            결과를 JSON 하나로
          --dry-run         고친 결과만 보이고 파일에 쓰지 않는다
          --specs <파일>    명세 표를 런타임이 낸 command-specs.json으로 (포크의 어휘)
        """;

    private static readonly JsonSerializerOptions Output = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            stdout.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            Arguments parsed = Arguments.Parse(args.AsSpan(1));
            StageCommandSpecTable specs = LoadSpecs(parsed, stderr);

            return args[0] switch
            {
                "read" => Read(parsed, specs, stdout),
                "vocab" => Vocab(parsed, specs, stdout),
                "set" => Edit(parsed, specs, stdout, stderr, SetValueOf),
                "add" => Edit(parsed, specs, stdout, stderr, AddOf),
                "remove" => Edit(parsed, specs, stdout, stderr, RemoveOf),
                "delay" => Edit(parsed, specs, stdout, stderr, DelayOf),
                "gap" => Edit(parsed, specs, stdout, stderr, GapOf),
                _ => throw new UsageException($"모르는 명령 '{args[0]}'"),
            };
        }
        catch (UsageException usage)
        {
            stderr.WriteLine($"쓰는 법: {usage.Message} — st help");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"거절: {exception.Message}");
            return 1;
        }
    }

    private static StageCommandSpecTable LoadSpecs(Arguments parsed, TextWriter stderr)
    {
        if (parsed.Specs is null)
        {
            return StageCommandSpecTable.Default;
        }

        List<string> problems = [];
        StageCommandSpecTable table = SpecTables.Load(parsed.Specs, problems);

        foreach (string problem in problems)
        {
            stderr.WriteLine($"명세 표: {problem}");
        }

        return table;
    }

    // ── 읽기 ────────────────────────────────────────────────────────────────

    private static int Read(Arguments parsed, StageCommandSpecTable specs, TextWriter stdout)
    {
        parsed.Expect(1, "st read <경로> [--line <LineId>] [--kind <종류>]");
        ScriptSet set = ScriptSet.Open(parsed.Positional[0]);
        IReadOnlySet<CommandKind>? kinds = parsed.Kinds();

        if (parsed.Line is not null)
        {
            ScriptFile file = set.Find(parsed.Line, specs);
            YarnLineReader.ReadFile(file.Text, file.Path, specs).TryGet(parsed.Line, out LineRead line);

            if (parsed.Json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { path = file.Path, line = LineView.Json(file.Text, line, kinds) }, Output));
            }
            else
            {
                stdout.WriteLine($"# {file.Path}");
                LineView.Write(stdout, file.Text, line, kinds);
            }

            return 0;
        }

        List<object> files = [];

        foreach (string path in set.Paths)
        {
            ScriptFile file = ScriptFile.Load(path);
            YarnFileRead read = YarnLineReader.ReadFile(file.Text, path, specs);
            List<LineRead> lines = read.Lines.Where(l => kinds is null || LineView.Has(l, kinds)).ToList();

            if (parsed.Json)
            {
                files.Add(new { path, problems = read.Problems, lines = lines.Select(LineView.SummaryJson).ToList() });
                continue;
            }

            stdout.WriteLine($"# {path} — 대사 줄 {lines.Count}개{(kinds is null ? string.Empty : $"(전체 {read.Lines.Count}개 가운데)")}");

            foreach (LineRead line in lines)
            {
                stdout.WriteLine(LineView.Summary(line));
            }

            foreach (string problem in read.Problems)
            {
                stdout.WriteLine($"  문제: {problem}");
            }
        }

        if (parsed.Json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { files }, Output));
        }

        return 0;
    }

    private static int Vocab(Arguments parsed, StageCommandSpecTable specs, TextWriter stdout)
    {
        parsed.Expect(0, "st vocab [--kind <종류>]");
        IReadOnlySet<CommandKind>? kinds = parsed.Kinds();
        List<StageCommandSpec> all = specs.All.Where(s => kinds is null || kinds.Contains(s.Kind)).ToList();

        if (parsed.Json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { commands = all.Select(LineView.SpecJson).ToList() }, Output));
            return 0;
        }

        foreach (IGrouping<CommandKind, StageCommandSpec> group in all.GroupBy(s => s.Kind))
        {
            stdout.WriteLine($"{CommandKinds.Key(group.Key)} — {CommandKinds.Label(group.Key)}");

            foreach (StageCommandSpec spec in group)
            {
                string marks = (spec.Waits ? " [기다린다]" : string.Empty) + (spec.Immediate ? " [바로 실행 — at 없이]" : string.Empty);
                stdout.WriteLine($"  {spec.Name} {string.Join(' ', spec.Args.Select(LineView.ArgSignature))}".TrimEnd() + $"  — {spec.Summary}{marks}");
            }
        }

        return 0;
    }

    // ── 고치기 ──────────────────────────────────────────────────────────────

    private static int Edit(
        Arguments parsed, StageCommandSpecTable specs, TextWriter stdout, TextWriter stderr, Func<Arguments, LineEdit> build)
    {
        LineEdit edit = build(parsed);
        ScriptFile file = ScriptSet.Open(parsed.Positional[0]).Find(edit.LineId, specs);
        EditOutcome outcome = LineEditor.Apply(file.Text, edit, specs);

        if (!outcome.Ok)
        {
            if (parsed.Json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { ok = false, path = file.Path, error = outcome.Error }, Output));
            }

            stderr.WriteLine($"거절: {outcome.Error}");
            return 1;
        }

        bool written = outcome.Changed && !parsed.DryRun;

        if (written)
        {
            file.Save(outcome.Text);
        }

        if (parsed.Json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(
                new
                {
                    ok = true,
                    changed = outcome.Changed,
                    written,
                    path = file.Path,
                    target = outcome.Target?.ToString(),
                    removed = outcome.Removed,
                    added = outcome.Added,
                    warnings = outcome.Warnings,
                    line = LineView.Json(outcome.Text, outcome.Line!, null),
                },
                Output));
            return 0;
        }

        if (!outcome.Changed)
        {
            stdout.WriteLine("바뀐 것이 없다 — 이미 그 값이다");
            return 0;
        }

        stdout.WriteLine(written ? $"고쳤다 — {file.Path}" : $"쓰지 않았다(--dry-run) — {file.Path}");

        if (outcome.Removed is not null)
        {
            stdout.WriteLine($"  - {outcome.Removed}");
        }

        if (outcome.Added is not null)
        {
            stdout.WriteLine($"  + {outcome.Added}");
        }

        if (outcome.Target is { } target)
        {
            stdout.WriteLine($"  주소: {target}");
        }

        foreach (string warning in outcome.Warnings)
        {
            stdout.WriteLine($"  알림: {warning}");
        }

        LineView.Write(stdout, outcome.Text, outcome.Line!, null);
        return 0;
    }

    private static LineEdit SetValueOf(Arguments parsed)
    {
        const string usage = "st set <경로> <주소> <칸> <값> | st set <경로> <주소> <칸> --clear";

        if (parsed.Clear)
        {
            parsed.Expect(3, usage);
            return new SetValue(parsed.Address(1), parsed.Positional[2], null);
        }

        parsed.Expect(4, usage);
        return new SetValue(parsed.Address(1), parsed.Positional[2], parsed.Positional[3]);
    }

    private static LineEdit AddOf(Arguments parsed)
    {
        parsed.ExpectAtLeast(4, "st add <경로> <LineId> <앵커> <커맨드> [인자…] [--index <순번>] [--card <카드 ID>]");
        return new AddCommand(
            parsed.Positional[1], parsed.Positional[2], parsed.Positional[3], parsed.Positional.Skip(4).ToArray(), parsed.Index, parsed.Card);
    }

    private static LineEdit RemoveOf(Arguments parsed)
    {
        parsed.Expect(2, "st remove <경로> <주소>");
        return new RemoveCommand(parsed.Address(1));
    }

    private static LineEdit DelayOf(Arguments parsed)
    {
        parsed.Expect(3, "st delay <경로> <주소> <길이>");
        return new SetDelay(parsed.Address(1), parsed.Positional[2]);
    }

    private static LineEdit GapOf(Arguments parsed)
    {
        const string usage = "st gap <경로> <LineId> <앵커> <길이> | st gap <경로> <LineId> <앵커> --clear";

        if (parsed.Clear)
        {
            parsed.Expect(3, usage);
            return new SetGap(parsed.Positional[1], parsed.Positional[2], null);
        }

        parsed.Expect(4, usage);
        return new SetGap(parsed.Positional[1], parsed.Positional[2], parsed.Positional[3]);
    }

    private sealed class UsageException(string message) : Exception(message);
}
