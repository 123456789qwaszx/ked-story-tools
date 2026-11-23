using System.Text.Json;
using St.Cli;

namespace St.Authoring.Tests;

public class StCliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int code = StCli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string Script()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("st-cli-").FullName, "a.yarn");
        File.WriteAllText(path, Scripts.Sample);
        return path;
    }

    [Fact]
    public void 줄을_읽으면_커맨드마다_주소가_붙는다()
    {
        (int code, string output, _) = Run("read", Script(), "--line", "t1");

        Assert.Equal(0, code);
        Assert.Contains("a/expression/0", output);
        Assert.Contains("face_swap  slot=s2 face=03 duration=13fr", output);
        Assert.Contains("in/time/0", output);
        Assert.Contains("간격 1s", output);
    }

    [Fact]
    public void 종류로_거르면_그_종류만_남고_지연은_남는다()
    {
        (int code, string output, _) = Run("read", Script(), "--line", "t1", "--kind", "expression");

        Assert.Equal(0, code);
        Assert.DoesNotContain("fade_out", output);
        Assert.DoesNotContain("pause", output);
        Assert.Contains("in/expression/0", output);
        Assert.Contains("+0.5s", output);
    }

    [Fact]
    public void 목록은_종류가_있는_줄만_거른다()
    {
        string path = Script();

        Assert.Contains("t4", Run("read", path).Out);

        string placement = Run("read", path, "--kind", "placement").Out;
        Assert.Contains("t1", placement);
        Assert.Contains("t2", placement);
        Assert.DoesNotContain("t3", placement);
    }

    [Fact]
    public void JSON으로_읽는다()
    {
        (int code, string output, _) = Run("read", Script(), "--line", "t1", "--json");

        Assert.Equal(0, code);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement line = json.RootElement.GetProperty("line");
        Assert.Equal("t1", line.GetProperty("lineId").GetString());
        Assert.True(line.GetProperty("editable").GetBoolean());
        Assert.Equal("t1/in/placement/0", line.GetProperty("steps")[0].GetProperty("commands")[0].GetProperty("address").GetString());
    }

    [Fact]
    public void 조작_다섯이_파일을_고친다()
    {
        string path = Script();

        Assert.Equal(0, Run("set", path, "t1/a/expression/0", "face", "07").Code);
        Assert.Equal(0, Run("add", path, "t1", "end", "shot_pan", "-2u", "0u", "--index", "0").Code);
        Assert.Equal(0, Run("delay", path, "t1/a/expression/0", "12fr").Code);
        Assert.Equal(0, Run("gap", path, "t1", "a", "1s").Code);
        Assert.Equal(0, Run("remove", path, "t1/in/expression/0").Code);

        string text = File.ReadAllText(path);
        Assert.Contains("<<at a pause 12fr>>\n<<at a face_swap s2 07 13fr>> // 줄 끝 주석\n<<gap a 1s>>\n", text);
        Assert.Contains("<<at end shot_pan -2u 0u>>\n", text);
        Assert.DoesNotContain("<<at in face s2 05>>", text);

        Assert.Equal(0, Run("set", path, "t1/a/expression/0", "duration", "--clear").Code);
        Assert.Equal(0, Run("gap", path, "t1", "a", "--clear").Code);
        Assert.Contains("<<at a face_swap s2 07>> // 줄 끝 주석\n<<at end", File.ReadAllText(path));
    }

    [Fact]
    public void 폴더를_주면_LineId로_파일을_찾아_고친다()
    {
        string path = Script();

        Assert.Equal(0, Run("set", Path.GetDirectoryName(path)!, "t1/a/expression/0", "face", "07").Code);
        Assert.Contains("face_swap s2 07", File.ReadAllText(path));
    }

    [Fact]
    public void dry_run은_쓰지_않는다()
    {
        string path = Script();
        (int code, string output, _) = Run("set", path, "t1/a/expression/0", "face", "07", "--dry-run");

        Assert.Equal(0, code);
        Assert.Contains("+ <<at a face_swap s2 07 13fr>>", output);
        Assert.Equal(Scripts.Sample, File.ReadAllText(path));
    }

    [Fact]
    public void 거절은_1이고_파일은_그대로다()
    {
        string path = Script();

        (int code, _, string error) = Run("add", path, "t2", "start", "face", "s1", "3");
        Assert.Equal(1, code);
        Assert.Contains("at 없는 명령", error);

        Assert.Equal(1, Run("set", path, "t1/a/expression/0", "duration", "빠르게").Code);
        Assert.Equal(1, Run("read", path + ".없다").Code);
        Assert.Equal(Scripts.Sample, File.ReadAllText(path));
    }

    [Fact]
    public void 쓰는_법이_틀리면_2다()
    {
        string path = Script();

        Assert.Equal(2, Run().Code);
        Assert.Equal(2, Run("모르는명령", path).Code);
        Assert.Equal(2, Run("set", path, "t1/a/expression", "face", "07").Code);
        Assert.Equal(2, Run("read", path, "--kind", "연기").Code);
        Assert.Equal(2, Run("remove", path).Code);
        Assert.Equal(0, Run("help").Code);
    }

    [Fact]
    public void 어휘를_종류로_거른다()
    {
        (int code, string output, _) = Run("vocab", "--kind", "camera");

        Assert.Equal(0, code);
        Assert.Contains("shot_zoom <zoom> [duration=0.45s] [ease]", output);
        Assert.DoesNotContain("face_swap", output);
    }

    // ── 카드 층(K7-6) ───────────────────────────────────────────────────────

    private static string CardScript(string? text = null)
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("st-cli-cards-").FullName, "c.yarn");
        File.WriteAllText(path, text ?? CatalogTests.CardScript);
        return path;
    }

    [Fact]
    public void cards는_줄의_카드와_커맨드의_주소를_보이고_카탈로그로_이름과_일부를_안다()
    {
        string path = CardScript();

        (int code, string output, _) = Run("cards", path, "--line", "c1", "--catalog", CatalogTests.CatalogFolder());
        Assert.Equal(0, code);
        Assert.Contains("카드 3장", output);
        Assert.Contains("E04      옆에서 들어옴  — 커맨드 2: start/placement/0 start/placement/1", output);
        Assert.Contains("G01      뛰기  — 커맨드 2: start/acting/0 start/expression/0  [다음 줄에 이 카드의 다른 묶음]", output);
        Assert.Contains("E04#2    옆에서 들어옴 · 일부  — 커맨드 1: a/placement/0", output);

        // 카탈로그 없이는 묶기만 — 이름도 일부도 모른다. 카드의 글은 어느 쪽이든 내지 않는다.
        (_, string bare, _) = Run("cards", path, "--line", "c1");
        Assert.DoesNotContain("옆에서 들어옴", bare);
        Assert.Contains("E04#2    — 커맨드 1: a/placement/0", bare);

        (_, string none, _) = Run("cards", path, "--line", "c3");
        Assert.Contains("카드 없음", none);
    }

    [Fact]
    public void cards는_줄을_주지_않으면_카드가_있는_줄의_목록이고_json도_낸다()
    {
        string path = CardScript();

        (int code, string output, _) = Run("cards", path, "--catalog", CatalogTests.CatalogFolder());
        Assert.Equal(0, code);
        Assert.Contains("카드가 있는 줄 2개(대사 줄 3개 가운데)", output);
        Assert.Contains("E04 G01 E04#2(일부)", output);
        Assert.DoesNotContain("  c3 ", output);

        (_, string json, _) = Run("cards", path, "--line", "c1", "--json");
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement first = document.RootElement.GetProperty("cards")[0];
        Assert.Equal("E04", first.GetProperty("tag").GetString());
        Assert.Equal("c1/start/placement/0", first.GetProperty("commands")[0].GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("whole").ValueKind);
    }

    [Fact]
    public void remove_card는_카드의_커맨드를_한_번에_빼고_간격과_지연은_두고_알린다()
    {
        const string text =
            "title: D\n---\n" +
            "<<gap start 0.5s>>\n<<at start pause 5fr>>\n" +
            "<<at start left s1 3.27u 0fr>> // card:E04\n<<at start fade_in s1 0.6s>> // card:E04\n<<at start face s1 05>>\n" +
            "가: 하나 #line:d1\n===\n";
        string path = CardScript(text);

        (int dry, _, _) = Run("remove", path, "d1", "--card", "E04", "--dry-run");
        Assert.Equal(0, dry);
        Assert.Equal(text, File.ReadAllText(path));

        (int code, string output, _) = Run("remove", path, "d1", "--card", "E04");
        Assert.Equal(0, code);
        Assert.Contains("  - <<at start left s1 3.27u 0fr>> // card:E04", output);
        Assert.Contains("  - <<at start fade_in s1 0.6s>> // card:E04", output);
        Assert.Contains("간격 · 지연은 건드리지 않았다", output);
        Assert.Equal(
            "title: D\n---\n<<gap start 0.5s>>\n<<at start pause 5fr>>\n<<at start face s1 05>>\n가: 하나 #line:d1\n===\n",
            File.ReadAllText(path));
    }

    [Fact]
    public void remove_card는_없는_카드를_거절하고_있는_카드를_알린다()
    {
        string path = CardScript();

        (int code, _, string error) = Run("remove", path, "c1", "--card", "Z99");
        Assert.Equal(1, code);
        Assert.Contains("있는 카드: E04 · G01 · E04#2", error);
        Assert.Equal(CatalogTests.CardScript, File.ReadAllText(path));

        // 한 줄 안의 카드만 뺀다 — 다음 줄의 G01은 그대로다.
        Assert.Equal(0, Run("remove", path, "c1", "--card", "G01").Code);
        string after = File.ReadAllText(path);
        Assert.DoesNotContain("<<at start motion s2 g01_hop>> // card:G01", after);
        Assert.Contains("<<at a motion s2 g01_hop>> // card:G01\n나:", after);
    }

    [Fact]
    public void replace는_제자리에서_바꾸고_인자가_없으면_슬롯_길이_이징을_옮겨_적는다()
    {
        string path = CardScript();

        // fade_in s1 0.6s(카드 E04) → fade_out: 슬롯과 길이를 옮겨 적고 카드의 출처는 걷힌다.
        (int code, string output, _) = Run("replace", path, "c1/start/placement/1", "fade_out");
        Assert.Equal(0, code);
        Assert.Contains("옛 커맨드에서 옮겨 적었다: s1 0.6s", output);
        Assert.Contains("<<at start left s1 3.27u 0fr>> // card:E04\n<<at start fade_out s1 0.6s>>\n<<at start motion", File.ReadAllText(path));

        // 인자를 적으면 그대로.
        Assert.Equal(0, Run("replace", path, "c1/start/expression/1", "shot_zoom", "1.2").Code);
        Assert.Contains("<<at start shot_zoom 1.2>>\n가:", File.ReadAllText(path));

        // 꼭 적는 칸을 옮겨 적을 수 없으면 거절한다.
        string before = File.ReadAllText(path);
        (int refused, _, string error) = Run("replace", path, "c1/a/placement/0", "shot_zoom");
        Assert.Equal(1, refused);
        Assert.Contains("'줌'(zoom)", error);
        Assert.Equal(before, File.ReadAllText(path));
    }
}
