using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ked.Presentation.Core;

namespace St.Authoring.Motions;

/// <summary>
/// 모션 파일(<c>motions/2</c>)의 글을 고친다 — 모션 하나를 더하거나 갈아 끼운다(docs/PLAN.md §4.8).
/// 대본과 같은 약속이다: **고친 모션만 다시 쓰고 파일의 나머지는 바이트 하나 바뀌지 않는다.**
/// 모션은 JSON 객체 그대로 다룬다 — 이 툴이 모르는 칸(<c>mirror</c> …)도 옮겨 적는다.
/// </summary>
public static class MotionFile
{
    private const string Schema = "motions/2";
    private static readonly string[] Axes = ["x", "y", "z"];
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>그 대본의 편 모션 파일 — <c>&lt;대본 이름&gt;.motions.json</c>.</summary>
    public static string EpisodePath(string scriptPath)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? string.Empty, Path.GetFileNameWithoutExtension(scriptPath) + ".motions.json");

    /// <summary>그 대본 곁의 모두가 쓰는 모션 파일 — <c>motions.json</c>.</summary>
    public static string SharedPath(string scriptPath)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? string.Empty, "motions.json");

    /// <summary>파일에 든 모션의 이름들.</summary>
    public static IReadOnlyList<string> Names(string text) => Scan(text).Items.Select(i => i.Name).ToList();

    /// <summary>그 이름의 모션(사본). 없으면 null.</summary>
    public static JsonObject? Find(string text, string name)
    {
        Layout layout = Scan(text);
        Item? item = layout.Items.FirstOrDefault(i => i.Name == name);
        return item is null ? null : JsonNode.Parse(layout.Bytes.AsSpan(item.Start, item.End - item.Start)) as JsonObject;
    }

    /// <summary>이름을 바꾼 사본.</summary>
    public static JsonObject Renamed(JsonObject motion, string name)
    {
        JsonObject copy = (JsonObject)motion.DeepClone();
        copy["name"] = name;
        return copy;
    }

    /// <summary>키를 솎은 사본과, 솎기 전 · 뒤의 키의 수. 남는 키는 원래의 것 그대로다.</summary>
    public static (JsonObject Motion, int Before, int After) Simplified(JsonObject motion)
    {
        JsonObject copy = (JsonObject)motion.DeepClone();
        int before = 0;
        int after = 0;

        foreach (JsonObject channel in (copy["channels"] as JsonArray ?? []).OfType<JsonObject>())
        {
            float tolerance = CurveSimplifier.ToleranceFor(
                Enum.TryParse(channel["kind"]?.GetValue<string>(), ignoreCase: true, out MotionChannelKind kind) ? kind : MotionChannelKind.Position);

            foreach (string axis in Axes)
            {
                if (channel[axis] is not JsonArray keys || keys.Any(k => k is not JsonObject))
                {
                    continue;
                }

                CurveKey[] curve = keys
                    .Select(k => new CurveKey(Number(k!, "t"), Number(k!, "v"), Number(k!, "inTangent"), Number(k!, "outTangent")))
                    .ToArray();
                IReadOnlyList<int> keep = CurveSimplifier.Keep(curve, tolerance);

                before += curve.Length;
                after += keep.Count;

                if (keep.Count < curve.Length)
                {
                    channel[axis] = new JsonArray(keep.Select(i => keys[i]!.DeepClone()).ToArray());
                }
            }
        }

        return (copy, before, after);
    }

    /// <summary>모션 하나를 끝에 더한 글. <paramref name="text"/>가 null이면 새 파일의 글이다.</summary>
    public static string Add(string? text, JsonObject motion)
    {
        ArgumentNullException.ThrowIfNull(motion);

        if (text is null)
        {
            return $"{{\n \"schema\": \"{Schema}\",\n \"motions\": [\n  {Write(motion, " ", "\n", 2)}\n ]\n}}\n";
        }

        Layout layout = Scan(text);

        if (layout.ArrayEnd < 0)
        {
            throw new InvalidDataException("모션 파일에 motions 목록이 없다");
        }

        string block = Write(motion, layout.Unit, layout.NewLine, 2);
        string indent = layout.Unit is null ? string.Empty : layout.NewLine + string.Concat(Enumerable.Repeat(layout.Unit, 2));

        if (layout.Items.Count > 0)
        {
            // 마지막 모션의 바로 뒤에 — 닫는 괄호 앞의 여백은 그대로 둔다.
            return Splice(layout, layout.Items[^1].End, layout.Items[^1].End, "," + indent + block);
        }

        string close = layout.Unit is null ? string.Empty : layout.NewLine + layout.Unit;
        return Splice(layout, layout.ArrayStart + 1, layout.ArrayEnd, indent + block + close);
    }

    /// <summary>그 이름의 모션을 갈아 끼운 글. 없으면 예외.</summary>
    public static string Replace(string text, string name, JsonObject motion)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(motion);

        Layout layout = Scan(text);
        Item item = layout.Items.FirstOrDefault(i => i.Name == name) ?? throw new InvalidDataException($"모션 파일에 '{name}'이 없다");
        return Splice(layout, item.Start, item.End, Write(motion, layout.Unit, layout.NewLine, 2));
    }

    // ── 글의 자리 ───────────────────────────────────────────────────────────

    private sealed record Item(string Name, int Start, int End);

    /// <param name="Unit">들여쓰기 한 칸의 글. 한 줄로 적힌 파일이면 null.</param>
    private sealed record Layout(byte[] Bytes, int Offset, IReadOnlyList<Item> Items, int ArrayStart, int ArrayEnd, string? Unit, string NewLine);

    private static Layout Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        int offset = bytes.AsSpan().StartsWith(Bom) ? Bom.Length : 0;
        Utf8JsonReader reader = new(bytes.AsSpan(offset), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        List<Item> items = [];
        int arrayStart = -1;
        int arrayEnd = -1;

        try
        {
            while (reader.Read())
            {
                if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("motions"))
                {
                    continue;
                }

                reader.Read();

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    break;
                }

                arrayStart = offset + (int)reader.TokenStartIndex;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    int start = offset + (int)reader.TokenStartIndex;
                    reader.Skip();
                    int end = offset + (int)reader.BytesConsumed;

                    // 이름이 없는 것(꼴이 어긋난 항목)도 자리는 센다 — 더할 때 그 뒤에 붙는다.
                    string name = JsonNode.Parse(bytes.AsSpan(start, end - start)) is JsonObject item && item["name"] is JsonValue value && value.TryGetValue(out string? found)
                        ? found
                        : string.Empty;
                    items.Add(new Item(name, start, end));
                }

                arrayEnd = offset + (int)reader.TokenStartIndex;
                break;
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"모션 파일을 읽지 못했다 — {exception.Message}", exception);
        }

        // 들여쓰기 — 첫 모션 앞의 여백이 두 칸어치다.
        string? unit = null;
        string newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        if (items.Count > 0)
        {
            int lineStart = Array.LastIndexOf(bytes, (byte)'\n', items[0].Start) + 1;
            string lead = Encoding.UTF8.GetString(bytes, lineStart, items[0].Start - lineStart);

            if (lineStart > 0 && lead.Length >= 2 && lead.All(char.IsWhiteSpace))
            {
                unit = lead[..(lead.Length / 2)];
            }
        }
        else if (text.Contains('\n'))
        {
            unit = " ";
        }

        return new Layout(bytes, offset, items, arrayStart, arrayEnd, unit, newLine);
    }

    private static string Splice(Layout layout, int from, int to, string insert)
    {
        byte[] middle = Encoding.UTF8.GetBytes(insert);
        byte[] result = new byte[layout.Bytes.Length - (to - from) + middle.Length];

        layout.Bytes.AsSpan(0, from).CopyTo(result);
        middle.CopyTo(result.AsSpan(from));
        layout.Bytes.AsSpan(to).CopyTo(result.AsSpan(from + middle.Length));
        return Encoding.UTF8.GetString(result);
    }

    // 파일의 들여쓰기에 맞춰 적는다 — 첫 줄은 붙일 자리에 그대로, 그 뒤의 줄은 깊이만큼 민다.
    private static string Write(JsonObject motion, string? unit, string newLine, int depth)
    {
        if (unit is null)
        {
            return motion.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        JsonSerializerOptions options = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            IndentCharacter = unit[0],
            IndentSize = unit.Length,
            NewLine = newLine,
        };

        return motion.ToJsonString(options).Replace(newLine, newLine + string.Concat(Enumerable.Repeat(unit, depth)), StringComparison.Ordinal);
    }

    private static float Number(JsonNode key, string name)
        => key[name] is JsonValue value && value.TryGetValue(out float number) ? number : 0f;
}
