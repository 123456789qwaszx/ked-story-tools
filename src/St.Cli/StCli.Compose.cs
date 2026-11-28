using System.Text.Json;
using Ked.Presentation.Core;
using St.Authoring.Composing;
using St.Authoring.Editing;

namespace St.Cli;

public static partial class StCli
{
    // ── 합치기 (K8 I2) ──────────────────────────────────────────────────────

    private const string ComposeUsage = "st compose <연출 층 폴더> [--dialogue <대사 층>] [--out <게임 폴더>] [--force] [--dry-run]";

    private static int Compose(Arguments parsed, StageCommandSpecTable specs, TextWriter stdout, TextWriter stderr)
    {
        parsed.Expect(1, ComposeUsage);
        string folder = Path.GetFullPath(parsed.Positional[0]);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"연출 층 폴더가 없다 — {folder}");
        }

        ComposeSettings? saved = ComposeSettings.Load(folder);

        if (saved is null && parsed.Out is null)
        {
            throw new UsageException($"{ComposeSettings.FileName}이 없다 — 처음에는 --out <게임 폴더>를 준다(--dialogue를 적지 않으면 {ComposeSettings.DefaultDialogue})");
        }

        // 선택지로 준 짝은 이번에 쓰고 설정에 적는다 — 상대 경로로.
        ComposeSettings settings = new(
            parsed.Dialogue is null ? saved?.Dialogue ?? ComposeSettings.DefaultDialogue : ComposeSettings.Relative(folder, parsed.Dialogue),
            parsed.Out is null ? saved!.Out : ComposeSettings.Relative(folder, parsed.Out));

        ComposeOutcome outcome = ComposeFolder.Run(folder, settings, parsed.Force, parsed.DryRun, specs);
        ComposeResult result = outcome.Result;

        if (result.Ok && !parsed.DryRun && settings != saved)
        {
            settings.Save(folder);
        }

        if (parsed.Json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(
                new
                {
                    staging = folder,
                    dialogue = settings.DialogueFolder(folder),
                    @out = settings.GameFolder(folder),
                    scenes = outcome.Scenes,
                    lines = outcome.Lines,
                    dryRun = outcome.DryRun,
                    unchanged = outcome.Unchanged,
                    notes = result.Notes.Select(n => new
                    {
                        kind = NoteKey(n.Kind),
                        line = CommandAddress.ShortLineId(n.LineId),
                        file = n.File,
                        node = n.Node,
                        old = n.OldText,
                        @new = n.NewText,
                        beatsMoved = n.BeatsMoved,
                    }),
                    files = outcome.Files.Select(f => new { layer = f.Layer, name = f.Name, change = ChangeKey(f.Change), reason = f.Reason }),
                    errors = result.Errors,
                },
                Output));
        }
        else if (result.Ok)
        {
            stdout.WriteLine($"# 합치기 — {folder}{(outcome.DryRun ? " (dry-run — 쓰지 않았다)" : string.Empty)}");
            stdout.WriteLine($"대사 층  {settings.DialogueFolder(folder)} — 장면 {outcome.Scenes} · 줄 {outcome.Lines}");
            stdout.WriteLine($"게임     {settings.GameFolder(folder)}");
            stdout.WriteLine(string.Join(" · ", Enum.GetValues<LineNoteKind>().Select(k => $"{NoteName(k)} {result.Notes.Count(n => n.Kind == k)}")));

            foreach (LineNote note in result.Notes)
            {
                string beats = note.BeatsMoved ? " — 박자 다시 놓을 것" : string.Empty;
                stdout.WriteLine($"  {NoteName(note.Kind)}  {CommandAddress.ShortLineId(note.LineId)}  {note.File} ({note.Node}){beats}");

                if (note.Kind == LineNoteKind.Review)
                {
                    stdout.WriteLine($"      옛: {note.OldText}");
                    stdout.WriteLine($"      새: {note.NewText}");
                }
            }

            string verb = outcome.DryRun ? "쓸 것" : "쓴 것";
            stdout.WriteLine(
                $"{verb} {outcome.Files.Count(f => f.Change == FileChange.Written)} · 걷{(outcome.DryRun ? "을" : "은")} 것 {outcome.Files.Count(f => f.Change == FileChange.Removed)}"
                + $" · 멈춘 것 {outcome.Files.Count(f => f.Change == FileChange.Held)} · 그대로 {outcome.Unchanged}");

            foreach (ComposeFileNote file in outcome.Files)
            {
                stdout.WriteLine($"  {file.Layer}  {ChangeName(file.Change)}  {file.Name}{(file.Reason is null ? string.Empty : " — " + file.Reason)}");
            }
        }

        foreach (string error in result.Errors)
        {
            stderr.WriteLine($"거절: {error}");
        }

        foreach (ComposeFileNote held in outcome.Files.Where(f => f.Change == FileChange.Held))
        {
            stderr.WriteLine($"멈춤: {held.Name} — {held.Reason}. 대사는 디렉터에서, 연출은 이 툴에서 고친다. 덮으려면 --force");
        }

        return result.Ok && !outcome.Held ? 0 : 1;
    }

    private static string NoteName(LineNoteKind kind) => kind switch
    {
        LineNoteKind.Review => "다시 볼 것",
        LineNoteKind.NoStaging => "연출 없음",
        LineNoteKind.Retired => "보관",
        LineNoteKind.Restored => "복원",
        _ => "옮김",
    };

    private static string NoteKey(LineNoteKind kind) => kind switch
    {
        LineNoteKind.Review => "review",
        LineNoteKind.NoStaging => "noStaging",
        LineNoteKind.Retired => "retired",
        LineNoteKind.Restored => "restored",
        _ => "moved",
    };

    private static string ChangeName(FileChange change) => change switch
    {
        FileChange.Written => "쓰기",
        FileChange.Removed => "걷기",
        _ => "멈춤",
    };

    private static string ChangeKey(FileChange change) => change switch
    {
        FileChange.Written => "written",
        FileChange.Removed => "removed",
        _ => "held",
    };
}
