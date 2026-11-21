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
        Assert.Contains("a/acting/0", output);
        Assert.Contains("face_swap  slot=s2 face=03 duration=13fr", output);
        Assert.Contains("in/time/0", output);
        Assert.Contains("간격 1s", output);
    }

    [Fact]
    public void 종류로_거르면_그_종류만_남고_지연은_남는다()
    {
        (int code, string output, _) = Run("read", Script(), "--line", "t1", "--kind", "acting");

        Assert.Equal(0, code);
        Assert.DoesNotContain("fade_out", output);
        Assert.DoesNotContain("pause", output);
        Assert.Contains("in/acting/0", output);
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

        Assert.Equal(0, Run("set", path, "t1/a/acting/0", "face", "07").Code);
        Assert.Equal(0, Run("add", path, "t1", "end", "shot_pan", "-2u", "0u", "--index", "0").Code);
        Assert.Equal(0, Run("delay", path, "t1/a/acting/0", "12fr").Code);
        Assert.Equal(0, Run("gap", path, "t1", "a", "1s").Code);
        Assert.Equal(0, Run("remove", path, "t1/in/acting/0").Code);

        string text = File.ReadAllText(path);
        Assert.Contains("<<at a pause 12fr>>\n<<at a face_swap s2 07 13fr>> // 줄 끝 주석\n<<gap a 1s>>\n", text);
        Assert.Contains("<<at end shot_pan -2u 0u>>\n", text);
        Assert.DoesNotContain("<<at in face s2 05>>", text);

        Assert.Equal(0, Run("set", path, "t1/a/acting/0", "duration", "--clear").Code);
        Assert.Equal(0, Run("gap", path, "t1", "a", "--clear").Code);
        Assert.Contains("<<at a face_swap s2 07>> // 줄 끝 주석\n<<at end", File.ReadAllText(path));
    }

    [Fact]
    public void 폴더를_주면_LineId로_파일을_찾아_고친다()
    {
        string path = Script();

        Assert.Equal(0, Run("set", Path.GetDirectoryName(path)!, "t1/a/acting/0", "face", "07").Code);
        Assert.Contains("face_swap s2 07", File.ReadAllText(path));
    }

    [Fact]
    public void dry_run은_쓰지_않는다()
    {
        string path = Script();
        (int code, string output, _) = Run("set", path, "t1/a/acting/0", "face", "07", "--dry-run");

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
        Assert.Contains("옛 표기", error);

        Assert.Equal(1, Run("set", path, "t1/a/acting/0", "duration", "빠르게").Code);
        Assert.Equal(1, Run("read", path + ".없다").Code);
        Assert.Equal(Scripts.Sample, File.ReadAllText(path));
    }

    [Fact]
    public void 쓰는_법이_틀리면_2다()
    {
        string path = Script();

        Assert.Equal(2, Run().Code);
        Assert.Equal(2, Run("모르는명령", path).Code);
        Assert.Equal(2, Run("set", path, "t1/a/acting", "face", "07").Code);
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
}
