using System.Text;
using System.Text.RegularExpressions;
using Ked.Presentation.Core;

namespace St.Authoring.Composing;

/// <summary>
/// 합치기 — 대사 층(디렉터의 <c>exported/yarn</c>)과 연출 층(이 툴이 고치는 yarn)을 줄 ID로 맞물려 게임 yarn을 낸다(docs/PLAN.md §2.5).
/// 뼈대(파일 · 노드 · 줄의 차례 · 화자와 글)는 대사 층을 따르고, 줄 앞의 묶음 · 박자 표시 · 태그는 연출 층에서 가져온다.
/// 연출 쪽 글은 바이트 그대로 옮긴다 — 대사가 같으면 게임 yarn과 새 연출 층은 연출 층과 한 글자도 다르지 않다(왕복).
/// 순수 함수다 — 글을 받아 글을 낸다.
/// </summary>
public static partial class LayerComposer
{
    /// <summary>보관소 파일의 꼬리 — <c>&lt;챕터&gt;_&lt;장면&gt;.retired.yarn</c>(C3).</summary>
    public const string RetiredSuffix = ".retired.yarn";

    // 문제 글 머리의 "파일:줄 " — 줄이 옮겨 가도 같은 문제로 본다.
    [GeneratedRegex(@"^\S+:\d+\s+")]
    private static partial Regex ProblemSourcePattern();

    /// <summary>그 연출 층 파일의 보관소 이름.</summary>
    public static string RetiredNameOf(string stagingName)
    {
        ArgumentNullException.ThrowIfNull(stagingName);
        return Path.GetFileNameWithoutExtension(stagingName) + RetiredSuffix;
    }

    /// <summary>보관소 파일의 이름인가 — 폴더를 열 때 대본 목록에서 뺀다.</summary>
    public static bool IsRetiredName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.EndsWith(RetiredSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 합친다. 세 묶음은 파일 이름(<c>ch01_scene01.yarn</c>) → 글이다. 줄은 줄 ID로만 맞춘다 — 다른 파일 · 다른 노드로 간 줄도 연출이 따라간다.
    /// </summary>
    public static ComposeResult Compose(
        IReadOnlyDictionary<string, string> dialogue,
        IReadOnlyDictionary<string, string> staging,
        IReadOnlyDictionary<string, string>? retired = null,
        StageCommandSpecTable? specs = null)
    {
        ArgumentNullException.ThrowIfNull(dialogue);
        ArgumentNullException.ThrowIfNull(staging);
        retired ??= new Dictionary<string, string>();

        List<(string Name, LayerScript Script)> dialogueFiles = ParseAll(dialogue);
        List<(string Name, LayerScript Script)> stagingFiles = ParseAll(staging);
        List<(string Name, LayerScript Script)> retiredFiles = ParseAll(retired);

        List<string> errors = [];
        Index(dialogueFiles, "대사 층", errors);
        Dictionary<string, Held> staged = Index(stagingFiles, "연출 층", errors);
        Dictionary<string, Held> shelved = Index(retiredFiles, "보관소", []);

        if (errors.Count > 0)
        {
            return ComposeResult.Rejected(errors);
        }

        // 노드의 꼬리와 뼈대 줄 앞의 것은 같은 title의 연출 층 노드에서 찾는다.
        Dictionary<string, (string File, LayerNode Node)> stagedNodes = new(StringComparer.Ordinal);

        foreach ((string name, LayerScript script) in stagingFiles)
        {
            foreach (LayerNode node in script.Nodes)
            {
                stagedNodes.TryAdd(node.Title, (name, node));
            }
        }

        Dictionary<string, string?> game = new(StringComparer.Ordinal);
        Dictionary<string, string?> newStaging = new(StringComparer.Ordinal);
        List<LineNote> notes = [];
        HashSet<LayerItem> used = [];
        HashSet<LayerNode> usedTails = [];

        foreach ((string name, LayerScript script) in dialogueFiles)
        {
            StringBuilder gameText = new();
            StringBuilder stagingText = new();

            foreach (LayerNode node in script.Nodes)
            {
                gameText.Append(node.Header);
                stagingText.Append(node.Header);

                LayerNode? stagedNode = stagedNodes.TryGetValue(node.Title, out (string File, LayerNode Node) found) ? found.Node : null;
                List<LayerItem> stagedBones = stagedNode?.Items.Where(i => i.Line is null).ToList() ?? [];
                int bone = 0;

                foreach (LayerItem item in node.Items)
                {
                    if (item.Line is not { LineId: { } lineId } line)
                    {
                        // 뼈대 줄 — 같은 노드의 같은 차례 · 같은 글이면 그 앞의 연출 쪽 줄을 가져온다.
                        LayerItem? match = bone < stagedBones.Count && stagedBones[bone].Key == item.Key ? stagedBones[bone] : null;
                        bone++;

                        string lead = match?.Lead ?? item.Lead;
                        gameText.Append(lead).Append(item.Raw);
                        stagingText.Append(lead).Append(item.Raw);

                        if (match is not null)
                        {
                            used.Add(match);
                        }

                        continue;
                    }

                    Held? held = staged.GetValueOrDefault(lineId);
                    bool restored = false;

                    if (held is null && shelved.TryGetValue(lineId, out Held? kept))
                    {
                        held = kept;
                        restored = true;
                        kept.Node.Items.Remove(kept.Item);
                    }

                    if (held is null)
                    {
                        gameText.Append(item.Lead).Append(item.Raw);
                        stagingText.Append(item.Lead).Append(item.Raw);
                        notes.Add(new LineNote(LineNoteKind.NoStaging, lineId, name, node.Title));
                        continue;
                    }

                    used.Add(held.Item);
                    LineText basis = held.Item.Line!;
                    string carried = line.Indent + held.Item.Raw[basis.Indent.Length..];
                    string? rebuilt = Carry(line, basis, out bool beatsMoved);

                    gameText.Append(held.Item.Lead).Append(rebuilt ?? carried);
                    stagingText.Append(held.Item.Lead).Append(carried);

                    if (restored)
                    {
                        notes.Add(new LineNote(LineNoteKind.Restored, lineId, name, node.Title));
                    }
                    else if (held.Node.Title != node.Title || held.File != name)
                    {
                        notes.Add(new LineNote(LineNoteKind.Moved, lineId, name, node.Title));
                    }

                    if (rebuilt is not null)
                    {
                        notes.Add(new LineNote(LineNoteKind.Review, lineId, name, node.Title, basis.Plain, line.Plain, beatsMoved));
                    }
                }

                string tail = stagedNode is null ? node.Tail : stagedNode.Tail;
                gameText.Append(tail).Append(node.End);
                stagingText.Append(tail).Append(node.End);

                if (stagedNode is not null)
                {
                    usedTails.Add(stagedNode);
                }
            }

            gameText.Append(script.Trailer);
            stagingText.Append(script.Trailer);
            game[name] = gameText.ToString();
            newStaging[name] = stagingText.ToString();
        }

        // 대사 층에 없는 장면의 파일은 없어진다.
        foreach ((string name, _) in stagingFiles)
        {
            if (!game.ContainsKey(name))
            {
                game[name] = null;
                newStaging[name] = null;
            }
        }

        // 대사 층에서 사라진 줄의 연출은 보관소로 — 있던 파일 · 있던 노드의 이름 아래에.
        Dictionary<string, LayerScript> shelves = retiredFiles.ToDictionary(f => f.Name, f => f.Script, StringComparer.Ordinal);

        foreach ((string name, LayerScript script) in stagingFiles)
        {
            foreach (LayerNode node in script.Nodes)
            {
                foreach (LayerItem item in node.Items.Where(i => !used.Contains(i)))
                {
                    if (item.LineId is { } lineId)
                    {
                        ShelfNode(shelves, name, node.Title).Items.Add(new LayerItem
                        {
                            Lead = item.Lead,
                            LeadHasCommand = item.LeadHasCommand,
                            Raw = Terminated(item.Raw),
                            Line = item.Line,
                            Key = item.Key,
                        });

                        notes.Add(new LineNote(LineNoteKind.Retired, lineId, name, node.Title));
                    }
                    else if (item.LeadHasCommand)
                    {
                        // 짝 잃은 뼈대 줄 앞의 명령 — 줄은 대사 층의 것이라 버리고 명령만 남긴다.
                        LayerNode shelf = ShelfNode(shelves, name, node.Title);
                        shelf.Tail += Terminated(item.Lead);
                        shelf.TailHasCommand = true;
                    }
                }

                if (!usedTails.Contains(node) && node.TailHasCommand)
                {
                    LayerNode shelf = ShelfNode(shelves, name, node.Title);
                    shelf.Tail += Terminated(node.Tail);
                    shelf.TailHasCommand = true;
                }
            }
        }

        Dictionary<string, string?> newRetired = new(StringComparer.Ordinal);

        foreach ((string name, LayerScript script) in shelves.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            Prune(script);
            newRetired[name] = script.Nodes.Count == 0 && script.Trailer.Trim().Length == 0 ? null : script.ToText();
        }

        // 낸 게임 yarn을 줄 읽기로 다시 읽는다 — 받은 것에 없던 문제가 하나라도 있으면 아무것도 내지 않는다.
        Dictionary<string, int> known = new(StringComparer.Ordinal);

        foreach ((string name, string text) in dialogue.Concat(staging))
        {
            foreach (string problem in ProblemsOf(text, name, specs))
            {
                known[problem] = known.GetValueOrDefault(problem) + 1;
            }
        }

        foreach ((string name, string? text) in game.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (text is null)
            {
                continue;
            }

            foreach (string problem in ProblemsOf(text, name, specs))
            {
                int left = known.GetValueOrDefault(problem);

                if (left > 0)
                {
                    known[problem] = left - 1;
                }
                else
                {
                    errors.Add($"{name} — 합친 대본에 없던 문제가 생긴다: {problem}");
                }
            }
        }

        return errors.Count > 0 ? ComposeResult.Rejected(errors) : new ComposeResult(game, newStaging, newRetired, notes, []);
    }

    /// <summary>
    /// 연출 층의 대사 줄(물리 줄 하나)을 대사 층의 그 줄과 견준다 — 합치기가 그 줄을 어떻게 낼지와 같은 셈이다.
    /// </summary>
    public static LineCompare CompareLine(string stagingLine, string dialogueLine)
    {
        ArgumentNullException.ThrowIfNull(stagingLine);
        ArgumentNullException.ThrowIfNull(dialogueLine);

        LineText basis = LineText.Parse(stagingLine);
        LineText line = LineText.Parse(dialogueLine);
        string? rebuilt = Carry(line, basis, out bool beatsMoved);

        return new LineCompare(rebuilt is null, beatsMoved, basis.Plain, line.Plain, rebuilt?[..^line.Eol.Length]);
    }

    /// <summary>줄 하나가 어느 파일 · 어느 노드에 있는가.</summary>
    private sealed record Held(string File, LayerNode Node, LayerItem Item);

    private static List<(string Name, LayerScript Script)> ParseAll(IReadOnlyDictionary<string, string> files)
        => files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => (f.Key, LayerScript.Parse(f.Value))).ToList();

    // 줄 ID → 있는 곳. 한 층의 두 곳에 같은 줄 ID가 있으면 맞출 수 없다.
    private static Dictionary<string, Held> Index(List<(string Name, LayerScript Script)> files, string layer, List<string> errors)
    {
        Dictionary<string, Held> index = new(StringComparer.Ordinal);

        foreach ((string name, LayerScript script) in files)
        {
            foreach (LayerNode node in script.Nodes)
            {
                foreach (LayerItem item in node.Items)
                {
                    if (item.LineId is not { } lineId)
                    {
                        continue;
                    }

                    if (index.TryGetValue(lineId, out Held? first))
                    {
                        errors.Add($"{layer}에 줄 ID #{lineId}가 두 번 있다 — {first.File}({first.Node.Title}) · {name}({node.Title})");
                    }
                    else
                    {
                        index.Add(lineId, new Held(name, node, item));
                    }
                }
            }
        }

        return index;
    }

    /// <summary>
    /// 연출 층의 줄(기준)에 붙은 박자 표시와 태그를 대사 층의 새 글에 다시 얹는다. 화자와 글이 같으면 null — 연출 층의 줄을 그대로 쓴다.
    /// </summary>
    private static string? Carry(LineText dialogue, LineText staging, out bool beatsMoved)
    {
        beatsMoved = false;
        bool sameBody = dialogue.Body == staging.Body;

        if (sameBody && dialogue.Speaker == staging.Speaker)
        {
            return null;
        }

        if (sameBody)
        {
            // 화자만 바뀌었다 — 글과 박자의 자리는 그대로다.
            return dialogue.Indent + dialogue.SpeakerPrefix + staging.RawBody + staging.Tail + dialogue.Eol;
        }

        // 글이 바뀌었다 — 말 도중의 박자는 자리를 잃는다. 줄 끝으로 옮기고 사람이 다시 놓는다(L5).
        List<Beat> mid = staging.MidBeats.ToList();
        beatsMoved = mid.Count > 0;

        return dialogue.Indent
            + dialogue.SpeakerPrefix
            + string.Concat(staging.HeadBeats.Select(b => b.Raw))
            + dialogue.Body
            + string.Concat(mid.Select(b => b.Raw))
            + string.Concat(staging.EndBeats.Select(b => b.Raw))
            + staging.Tail
            + dialogue.Eol;
    }

    // 보관소에서 그 파일 · 그 노드의 자리 — 없으면 만든다.
    private static LayerNode ShelfNode(Dictionary<string, LayerScript> shelves, string stagingName, string title)
    {
        string name = RetiredNameOf(stagingName);

        if (!shelves.TryGetValue(name, out LayerScript? shelf))
        {
            shelves.Add(name, shelf = new LayerScript());
        }

        if (shelf.Nodes.FirstOrDefault(n => n.Title == title) is { } node)
        {
            return node;
        }

        node = new LayerNode
        {
            Header = (shelf.Nodes.Count == 0 ? string.Empty : "\n") + $"title: {title}\n---\n",
            Title = title,
            End = "===\n",
        };

        shelf.Nodes.Add(node);
        return node;
    }

    // 되살려서 비게 된 보관소의 노드를 걷는다.
    private static void Prune(LayerScript shelf)
    {
        shelf.Nodes.RemoveAll(n => n.Items.Count == 0 && !n.TailHasCommand);

        if (shelf.Nodes.Count > 0 && shelf.Nodes[0].Header.StartsWith('\n'))
        {
            shelf.Nodes[0].Header = shelf.Nodes[0].Header[1..];
        }
    }

    // 줄 끝 문자가 없는 글(파일의 마지막 줄)은 붙여 준다 — 뒤에 다른 줄이 온다.
    private static string Terminated(string text) => text.Length == 0 || text.EndsWith('\n') ? text : text + "\n";

    private static IEnumerable<string> ProblemsOf(string text, string name, StageCommandSpecTable? specs)
    {
        YarnFileRead read = YarnLineReader.ReadFile(text, name, specs);
        return read.Problems.Concat(read.Lines.SelectMany(l => l.Problems)).Select(p => ProblemSourcePattern().Replace(p, string.Empty));
    }
}
