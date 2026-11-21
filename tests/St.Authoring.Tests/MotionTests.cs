using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using St.Authoring.Editing;
using St.Authoring.Motions;

namespace St.Authoring.Tests;

/// <summary>줄의 모션(K6-2) — 풀기 · 키 솎기. 모션 파일도 고친 모션만 다시 쓰고, 대본과 한 칸으로 되돌린다.</summary>
public class MotionTests
{
    // 작업공간의 도구가 내는 꼴 — 들여쓰기 한 칸.
    private const string Shared = """
        {
         "schema": "motions/2",
         "motions": [
          {
           "name": "nod",
           "rig": "character",
           "duration": 0.5,
           "end": "restore",
           "mirror": true,
           "channels": [
            {
             "node": "CharacterPortrait_Track_Move",
             "kind": "position",
             "y": [
              {
               "t": 0.0,
               "v": 0.0,
               "inTangent": 0.0,
               "outTangent": 0.0
              },
              {
               "t": 0.5,
               "v": 0.0,
               "inTangent": 0.0,
               "outTangent": 0.0
              }
             ]
            }
           ]
          }
         ]
        }

        """;

    private const string Script =
        "title: A\n---\n<<at start motion s1 nod>>\n가: 하나 #line:ln_Ab1\n\n<<at start motion s2 nod>>\n나: 둘 #line:t2\n===\n";

    // 한 번 솟았다 내려오는 호를 프레임마다 구운 것.
    private static CurveKey[] Baked(int frames = 15, float height = 60f)
    {
        float Value(int i) => height * MathF.Sin(MathF.PI * i / frames);
        float step = 1f / 30f;

        return Enumerable.Range(0, frames + 1).Select(i =>
        {
            float slope = i == 0 || i == frames ? 0f : (Value(i + 1) - Value(i - 1)) / (2 * step);
            return new CurveKey(i * step, Value(i), slope, slope);
        }).ToArray();
    }

    private static string BakedMotion(string name)
    {
        JsonArray keys = new(Baked().Select(k => (JsonNode)new JsonObject { ["t"] = k.Time, ["v"] = k.Value, ["inTangent"] = k.InTangent, ["outTangent"] = k.OutTangent }).ToArray());
        JsonObject motion = new()
        {
            ["name"] = name, ["rig"] = "character", ["duration"] = 0.5, ["end"] = "restore",
            ["channels"] = new JsonArray(new JsonObject { ["node"] = "N", ["kind"] = "position", ["y"] = keys }),
        };
        return MotionFile.Add(null, motion);
    }

    private static (EditSession Session, string Script, string Dir) Open(string? episode = null, string? shared = Shared)
    {
        string dir = Directory.CreateTempSubdirectory("st-motion-").FullName;
        string script = Path.Combine(dir, "ep01.yarn");
        File.WriteAllText(script, Script);

        if (shared is not null)
        {
            File.WriteAllText(Path.Combine(dir, "motions.json"), shared);
        }

        if (episode is not null)
        {
            File.WriteAllText(Path.Combine(dir, "ep01.motions.json"), episode);
        }

        return (EditSession.Open(dir), script, dir);
    }

    private static CommandAddress Address(string text)
    {
        Assert.True(CommandAddress.TryParse(text, out CommandAddress address, out _));
        return address;
    }

    [Fact]
    public void 구운_키를_솎아도_커브는_한도_안에_머문다()
    {
        CurveKey[] baked = Baked();
        float tolerance = CurveSimplifier.ToleranceFor(MotionChannelKind.Position);

        IReadOnlyList<int> keep = CurveSimplifier.Keep(baked, tolerance);
        CurveKey[] thin = keep.Select(i => baked[i]).ToArray();

        // 열여섯이 몇 개로 준다 — 양 끝은 남는다.
        Assert.InRange(keep.Count, 3, 7);
        Assert.Equal(0, keep[0]);
        Assert.Equal(baked.Length - 1, keep[^1]);
        Assert.All(baked, key => Assert.InRange(Math.Abs(CurveFunctions.Evaluate(thin, key.Time) - key.Value), 0f, tolerance));

        // 곧은 선은 양 끝만 남고, 키가 둘 이하면 그대로다.
        CurveKey[] flat = Enumerable.Range(0, 10).Select(i => new CurveKey(i / 30f, 5f, 0f, 0f)).ToArray();
        Assert.Equal(new[] { 0, 9 }, CurveSimplifier.Keep(flat, tolerance));
        Assert.Equal(new[] { 0, 1 }, CurveSimplifier.Keep(flat[..2], tolerance));
    }

    [Fact]
    public void 모션을_더하고_갈아도_파일의_나머지는_그대로다()
    {
        JsonObject nod = MotionFile.Find(Shared, "nod")!;

        string added = MotionFile.Add(Shared, MotionFile.Renamed(nod, "nod_t1"));

        // 앞의 것은 글자 하나 그대로이고, 새것이 같은 들여쓰기로 뒤에 붙는다. 모르는 칸(mirror)도 따라온다.
        int end = Shared.LastIndexOf("  }", StringComparison.Ordinal) + 3;
        Assert.StartsWith(Shared[..end], added);
        Assert.EndsWith(Shared[end..], added);
        Assert.Contains(",\n  {\n   \"name\": \"nod_t1\",\n   \"rig\": \"character\",", added);
        Assert.Equal(new[] { "nod", "nod_t1" }, MotionFile.Names(added));
        Assert.True(MotionFile.Find(added, "nod_t1")!["mirror"]!.GetValue<bool>());

        // 갈아 끼우면 그 모션만 바뀐다.
        JsonObject longer = MotionFile.Renamed(nod, "nod");
        longer["duration"] = 0.75;
        string replaced = MotionFile.Replace(added, "nod", longer);
        Assert.Contains("\"duration\": 0.75", replaced);
        Assert.Equal(added[added.IndexOf(",\n  {\n   \"name\": \"nod_t1\"", StringComparison.Ordinal)..], replaced[replaced.IndexOf(",\n  {\n   \"name\": \"nod_t1\"", StringComparison.Ordinal)..]);

        // 한 줄로 적힌 파일은 한 줄로, 없던 파일은 새로.
        Assert.Equal("{\"motions\":[{\"name\":\"a\"},{\"name\":\"b\"}]}", MotionFile.Add("{\"motions\":[{\"name\":\"a\"}]}", new JsonObject { ["name"] = "b" }));
        Assert.Equal(new[] { "nod" }, MotionFile.Names(MotionFile.Add(null, nod)));
        Assert.Throws<InvalidDataException>(() => MotionFile.Replace(Shared, "없는 것", nod));
    }

    [Fact]
    public void 풀면_그_줄만_새_이름을_가리키고_되돌리면_두_파일이_함께_돌아온다()
    {
        (EditSession session, string script, string dir) = Open();
        string episode = Path.Combine(dir, "ep01.motions.json");

        EditOutcome outcome = session.UnpackMotion(script, Address("ln_Ab1/start/acting/0"));

        Assert.True(outcome is { Ok: true, Changed: true }, outcome.Error);

        // 그 줄만 바뀐다 — 같은 모션을 쓰는 다른 줄과 모두가 쓰는 파일은 그대로다. 이름은 런타임의 규칙([a-z0-9_]+)을 지킨다.
        Assert.Equal(Script.Replace("motion s1 nod", "motion s1 nod_ln_ab1"), File.ReadAllText(script));
        Assert.Equal(Shared, File.ReadAllText(Path.Combine(dir, "motions.json")));
        Assert.Equal(new[] { "nod_ln_ab1" }, MotionFile.Names(File.ReadAllText(episode)));
        Assert.Matches(MotionRules.NameRule, "nod_ln_ab1");

        // 한 칸이다 — 대본도 모션 파일(새로 만든 것이라 사라진다)도.
        Assert.True(session.Undo());
        Assert.Equal(Script, File.ReadAllText(script));
        Assert.False(File.Exists(episode));
        Assert.False(session.CanUndo);

        Assert.True(session.Redo());
        Assert.Contains("nod_ln_ab1", File.ReadAllText(script));
        Assert.True(File.Exists(episode));

        // 모션 커맨드가 아니면 거절한다.
        Assert.False(session.UnpackMotion(script, Address("t2/start/camera/0")).Ok);
    }

    [Fact]
    public void 솎기는_이_편의_모션만_줄이고_대본은_건드리지_않는다()
    {
        string baked = BakedMotion("nod");
        (EditSession session, string script, string dir) = Open(episode: baked, shared: null);
        string episode = Path.Combine(dir, "ep01.motions.json");

        EditOutcome outcome = session.SimplifyMotion(script, Address("ln_Ab1/start/acting/0"));

        Assert.True(outcome is { Ok: true, Changed: true }, outcome.Error);
        Assert.Equal(Script, File.ReadAllText(script));
        Assert.InRange(MotionFile.Find(File.ReadAllText(episode), "nod")!["channels"]![0]!["y"]!.AsArray().Count, 3, 7);

        // 더 솎을 것이 없으면 바뀌지 않는다 — 되돌리기에 쌓이지 않는다.
        Assert.False(session.SimplifyMotion(script, Address("ln_Ab1/start/acting/0")).Changed);

        Assert.True(session.Undo());
        Assert.Equal(baked, File.ReadAllText(episode));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void 모두가_쓰는_파일의_모션은_솎지_않는다_먼저_푼다()
    {
        (EditSession session, string script, string dir) = Open();

        EditOutcome outcome = session.SimplifyMotion(script, Address("ln_Ab1/start/acting/0"));

        Assert.False(outcome.Ok);
        Assert.Contains("먼저 이 줄의 것으로 푼다", outcome.Error);
        Assert.Equal(Shared, File.ReadAllText(Path.Combine(dir, "motions.json")));
    }

    [Fact]
    public void 모션_파일이_바깥에서_바뀌었으면_되돌리기가_덮어쓰지_않는다()
    {
        string baked = BakedMotion("nod");
        (EditSession session, string script, string dir) = Open(episode: baked, shared: null);
        string episode = Path.Combine(dir, "ep01.motions.json");

        session.SimplifyMotion(script, Address("ln_Ab1/start/acting/0"));
        string outside = File.ReadAllText(episode) + "\n";
        File.WriteAllText(episode, outside);

        Assert.False(session.Undo());
        Assert.Equal(outside, File.ReadAllText(episode));
    }

    // ── 그래프의 편집기 (K6-4) ──────────────────────────────────────────────

    private static readonly Mt.Authoring.Graph.CurveRef Y = new("CharacterPortrait_Track_Move", Mt.Authoring.Model.ChannelKind.Position, Mt.Authoring.Model.Axis.Y);

    // 모두가 쓰는 파일의 꼴 그대로를 이 편의 파일로 — nod(키 둘, mirror가 딸렸다)와 곁의 모션 하나.
    private static string Episode() => MotionFile.Add(Shared, new JsonObject { ["name"] = "other", ["duration"] = 1, ["channels"] = new JsonArray() });

    [Fact]
    public void 그래프의_편집기는_키를_고칠_때마다_그_모션만_파일에_쓴다()
    {
        string before = Episode();
        (EditSession session, string script, string dir) = Open(episode: before, shared: null);
        string episode = Path.Combine(dir, "ep01.motions.json");
        MotionGraphEditor editor = new(session, script);
        int changed = 0;
        editor.GraphChanged += (_, _) => changed++;

        Mt.Authoring.Model.MotionAsset motion = editor.FindMotion("nod")!;
        List<Mt.Authoring.Model.KeyAsset> keys = Y.Resolve(motion)!.Keys;
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.Equal(Mt.Authoring.Model.TangentMode.Free, k.Tangent));

        // 가운데에 키 하나를 더한다.
        editor.SetKeysMany("nod", [(Y, keys.Append(new Mt.Authoring.Model.KeyAsset { T = 0.25f, V = -40f }))]);

        string after = File.ReadAllText(episode);
        Assert.Null(editor.Error);
        Assert.Equal(1, changed);
        Assert.Contains("\"v\": -40", after);

        // 그 모션만 바뀐다 — 곁의 모션과 파일의 머리는 글자 하나 그대로이고, 모르는 칸(mirror)도 남는다.
        Assert.Equal(before[..before.IndexOf("\"channels\"", StringComparison.Ordinal)], after[..after.IndexOf("\"channels\"", StringComparison.Ordinal)]);
        Assert.Equal(before[before.IndexOf("\"name\": \"other\"", StringComparison.Ordinal)..], after[after.IndexOf("\"name\": \"other\"", StringComparison.Ordinal)..]);
        Assert.Equal(Script, File.ReadAllText(script));

        // 쥐고 있는 모션이 곧 파일의 것이다 — 다시 찾아도 같은 것이고 키가 셋이다.
        Assert.Same(motion, editor.FindMotion("nod"));
        Assert.Equal(3, Y.Resolve(motion)!.Keys.Count);

        // 되돌리기 한 칸 — 파일이 처음 그대로 돌아오고, 다시 읽으면 키가 둘이다.
        Assert.True(session.Undo());
        Assert.Equal(before, File.ReadAllText(episode));
        editor.Reload();
        Assert.Equal(2, Y.Resolve(editor.FindMotion("nod")!)!.Keys.Count);
    }

    [Fact]
    public void 탄젠트의_모드는_쥐고_있는_동안_남고_묶은_고침은_한_칸이다()
    {
        (EditSession session, string script, string dir) = Open(episode: BakedMotion("nod"), shared: null);
        string episode = Path.Combine(dir, "ep01.motions.json");
        string before = File.ReadAllText(episode);
        MotionGraphEditor editor = new(session, script);
        Mt.Authoring.Graph.CurveRef curve = new("N", Mt.Authoring.Model.ChannelKind.Position, Mt.Authoring.Model.Axis.Y);
        Mt.Authoring.Graph.KeyRef top = new(curve, 7);

        editor.Batch(() =>
        {
            editor.SetTangentMode("nod", [top], Mt.Authoring.Model.TangentMode.Flat);
            editor.RemoveKeys("nod", [new Mt.Authoring.Graph.KeyRef(curve, 1)]);
        });

        // 평평한 탄젠트가 파일에 적히고(기울기 0), 모드는 이 편집기가 쥔 동안 남는다. 키 하나가 빠졌다.
        Mt.Authoring.Model.MotionAsset motion = editor.FindMotion("nod")!;
        Assert.Equal(15, curve.Resolve(motion)!.Keys.Count);
        Mt.Authoring.Model.KeyAsset flat = curve.Resolve(motion)!.Keys[6];
        Assert.Equal(Mt.Authoring.Model.TangentMode.Flat, flat.Tangent);
        Assert.Equal(0f, flat.InTangent);

        // 묶은 것은 되돌리기 한 칸이다.
        Assert.True(session.Undo());
        Assert.Equal(before, File.ReadAllText(episode));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void 모두가_쓰는_파일의_모션은_그래프에서_고치지_않는다()
    {
        (EditSession session, string script, string dir) = Open();
        MotionGraphEditor editor = new(session, script);

        Assert.Null(editor.FindMotion("nod"));
        Assert.Throws<KeyNotFoundException>(() => editor.SetKeysMany("nod", [(Y, [])]));
        Assert.Equal(Shared, File.ReadAllText(Path.Combine(dir, "motions.json")));
    }
}
