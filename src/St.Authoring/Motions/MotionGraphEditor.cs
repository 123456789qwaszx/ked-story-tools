using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using St.Authoring.Editing;

namespace St.Authoring.Motions;

/// <summary>
/// 그래프 에디터가 이 툴에서 고치는 길(docs/PLAN.md §4.8) — 그 편의 모션 파일에 든 모션 하나를 그래프의 모델로 읽고,
/// 키를 고칠 때마다 그 모션만 파일에 다시 쓴다(<see cref="EditSession.WriteMotion"/> — 되돌리기 한 칸).
/// 키를 고치는 속은 characterRig와 같은 것이다(<see cref="MotionKeyEdits"/>).
///
/// <c>motions/2</c>에는 탄젠트의 모드(auto · flat · linear)와 끊김이 없다 — 읽을 때는 모두 자유 탄젠트이고,
/// 양쪽 기울기가 다르면 끊긴 것으로 본다. 이 편집기가 쥔 동안에는 고른 모드가 남는다(파일을 다시 읽으면 사라진다).
/// 모두가 쓰는 파일(<c>motions.json</c>)의 모션은 고치지 않는다 — 먼저 이 줄의 것으로 푼다.
/// </summary>
public sealed class MotionGraphEditor : IGraphEditor
{
    private static readonly string[] AxisNames = ["x", "y", "z"];

    private readonly EditSession _session;
    private readonly string _scriptPath;

    private string? _name;
    private string? _text;
    private MotionAsset? _motion;
    private JsonObject? _source;
    private int _batch;
    private bool _dirty;

    public MotionGraphEditor(EditSession session, string scriptPath)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scriptPath);
        _session = session;
        _scriptPath = scriptPath;
    }

    public event EventHandler? GraphChanged;

    /// <summary>마지막 고침이 파일에 쓰이지 못한 까닭. 쓰였으면 null.</summary>
    public string? Error { get; private set; }

    /// <summary>그 편의 모션 파일에 든 모션. 없으면(모두가 쓰는 파일의 것이거나 모르는 이름) null.</summary>
    public MotionAsset? FindMotion(string name)
    {
        string path = MotionFile.EpisodePath(_scriptPath);

        if (!File.Exists(path))
        {
            return null;
        }

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // 파일이 우리가 아는 그대로면 쥐고 있던 것을 준다 — 탄젠트의 모드가 남는다.
        if (name == _name && text == _text && _motion is not null)
        {
            return _motion;
        }

        try
        {
            if (MotionFile.Find(text, name) is not { } source)
            {
                return null;
            }

            (_name, _text, _source, _motion) = (name, text, source, Read(source));
            return _motion;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>파일이 바깥에서(또는 되돌리기로) 바뀌었다 — 쥐고 있던 것을 놓고 그래프가 다시 읽게 한다.</summary>
    public void Reload()
    {
        (_text, _motion, _source) = (null, null, null);
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetKeysMany(string name, IEnumerable<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> edits)
        => Apply(name, motion => MotionKeyEdits.SetKeysMany(motion, edits));

    public void SetTangentMode(string name, IEnumerable<KeyRef> keys, TangentMode mode)
        => Apply(name, motion => MotionKeyEdits.SetTangentMode(motion, keys, mode));

    public void SetBroken(string name, IEnumerable<KeyRef> keys, bool broken)
        => Apply(name, motion => MotionKeyEdits.SetBroken(motion, keys, broken));

    public void SetTangents(string name, KeyRef key, float inTangent, float outTangent)
        => Apply(name, motion => MotionKeyEdits.SetTangents(motion, key, inTangent, outTangent));

    public List<KeyRef> PasteKeys(string name, KeyClipboard clipboard, float atTime, Func<float, float>? snap = null, IReadOnlyList<CurveRef>? targets = null)
    {
        List<KeyRef> pasted = [];
        Apply(name, motion => MotionKeyEdits.PasteKeys(motion, clipboard, atTime, snap, targets, pasted));
        return pasted;
    }

    public void RemoveKeys(string name, IEnumerable<KeyRef> keys)
        => Apply(name, motion => MotionKeyEdits.WithoutKeys(motion, keys) is { Count: > 0 } edits ? MotionKeyEdits.SetKeysMany(motion, edits) : null);

    public void Batch(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _batch++;

        try
        {
            change();
        }
        finally
        {
            _batch--;
        }

        if (_batch == 0 && _dirty)
        {
            Commit();
        }
    }

    private void Apply(string name, Func<MotionAsset, Action?> plan)
    {
        MotionAsset motion = FindMotion(name) ?? throw new KeyNotFoundException($"모션 '{name}'은 이 편의 모션 파일에 없다.");

        if (plan(motion) is not { } change)
        {
            return;
        }

        change();
        _dirty = true;

        if (_batch == 0)
        {
            Commit();
        }
    }

    // 쥐고 있는 모션을 파일에 쓴다 — 한 칸. 쓰지 못했으면 쥔 것을 놓아 파일의 것으로 돌아간다.
    private void Commit()
    {
        _dirty = false;

        if (_name is null || _motion is null || _source is null)
        {
            return;
        }

        JsonObject written = Write(_source, _motion);
        Error = _session.WriteMotion(_scriptPath, _name, written);

        if (Error is null)
        {
            _source = written;
            _text = File.ReadAllText(MotionFile.EpisodePath(_scriptPath));
        }
        else
        {
            (_text, _motion, _source) = (null, null, null);
        }

        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── motions/2 ↔ 그래프의 모델 ───────────────────────────────────────────

    private static MotionAsset Read(JsonObject source)
    {
        MotionAsset motion = new()
        {
            Name = Text(source, "name") ?? string.Empty,
            Rig = Text(source, "rig") ?? string.Empty,
            Duration = Number(source, "duration"),
            End = Enum.TryParse(Text(source, "end"), ignoreCase: true, out MotionEnd end) ? end : MotionEnd.Restore,
        };

        foreach (JsonObject channel in (source["channels"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Text(channel, "node") is not { Length: > 0 } node || !Enum.TryParse(Text(channel, "kind"), ignoreCase: true, out MotionChannelKind kind))
            {
                continue;
            }

            ChannelAsset asset = new() { Node = node, Kind = ChannelKinds.FromCore(kind) };

            for (int i = 0; i < AxisNames.Length; i++)
            {
                if (channel[AxisNames[i]] is JsonArray keys && keys.Count > 0)
                {
                    asset.Set((Axis)i, new ComponentCurve
                    {
                        Keys = keys.OfType<JsonObject>().Select(k =>
                        {
                            float into = Number(k, "inTangent");
                            float outOf = Number(k, "outTangent");
                            return new KeyAsset { T = Number(k, "t"), V = Number(k, "v"), InTangent = into, OutTangent = outOf, Tangent = TangentMode.Free, Broken = into != outOf };
                        }).OrderBy(k => k.T).ToList(),
                    });
                }
            }

            motion.Channels.Add(asset);
        }

        return motion;
    }

    // 원래의 객체에서 채널만 갈아 끼운다 — 이 툴이 모르는 칸(mirror …)은 그대로 남는다.
    private static JsonObject Write(JsonObject source, MotionAsset motion)
    {
        JsonObject copy = (JsonObject)source.DeepClone();
        JsonArray channels = [];

        foreach (ChannelAsset channel in motion.Channels)
        {
            if (!ChannelKinds.TryToCore(channel.Kind, out MotionChannelKind kind))
            {
                continue;
            }

            JsonObject written = new() { ["node"] = channel.Node, ["kind"] = kind.ToString().ToLowerInvariant() };

            for (int i = 0; i < AxisNames.Length; i++)
            {
                if (channel.Get((Axis)i) is { Keys.Count: > 0 } curve)
                {
                    written[AxisNames[i]] = new JsonArray(curve.Keys.Select(k => (JsonNode)new JsonObject
                    {
                        ["t"] = k.T, ["v"] = k.V, ["inTangent"] = k.InTangent, ["outTangent"] = k.OutTangent,
                    }).ToArray());
                }
            }

            channels.Add(written);
        }

        copy["channels"] = channels;
        return copy;
    }

    private static string? Text(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static float Number(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue(out float number) ? number : 0f;
}
