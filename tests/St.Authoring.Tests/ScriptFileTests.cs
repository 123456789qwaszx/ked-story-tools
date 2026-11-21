using System.Text;
using Ked.Presentation.Core;
using St.Authoring.Editing;
using St.Authoring.Scripts;

namespace St.Authoring.Tests;

public class ScriptFileTests
{
    private static string Temp(string name, byte[] bytes)
    {
        string dir = Directory.CreateTempSubdirectory("st-tests-").FullName;
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void 읽은_글을_그대로_쓰면_바이트가_같다(bool bom, bool crlf)
    {
        string text = crlf ? Scripts.Crlf(Scripts.Sample) : Scripts.Sample;
        byte[] body = Encoding.UTF8.GetBytes(text);
        byte[] bytes = bom ? [0xEF, 0xBB, 0xBF, .. body] : body;

        ScriptFile file = ScriptFile.Decode("a.yarn", bytes);

        Assert.Equal(bom, file.HasBom);
        Assert.Equal(text, file.Text);
        Assert.Equal(bytes, file.Encode(file.Text));
    }

    [Fact]
    public void 고친_파일은_묶음_밖의_바이트가_같다()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Scripts.Crlf(Scripts.Sample))];
        string path = Temp("a.yarn", original);

        ScriptFile file = ScriptFile.Load(path);
        EditOutcome outcome = LineEditor.Apply(file.Text, new SetValue(new CommandAddress("line:t1", "a", CommandKind.Acting, 0), "face", "07"));
        file.Save(outcome.Text);

        byte[] saved = File.ReadAllBytes(path);
        Assert.Equal(original.Length, saved.Length);

        // 03 → 07의 한 글자만 다르다.
        int differing = original.Zip(saved).Count(pair => pair.First != pair.Second);
        Assert.Equal(1, differing);
    }

    [Fact]
    public void UTF8이_아닌_파일은_읽지_않는다()
        => Assert.Throws<InvalidDataException>(() => ScriptFile.Decode("a.yarn", [0x74, 0xFF, 0xFE, 0x41]));

    [Fact]
    public void 폴더를_열면_LineId로_파일을_찾는다()
    {
        string first = Temp("a.yarn", Encoding.UTF8.GetBytes(Scripts.Sample));
        string dir = Path.GetDirectoryName(first)!;
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        string second = Path.Combine(dir, "sub", "b.yarn");
        File.WriteAllText(second, "title: U\n---\n마: 다른 파일 #line:u1\n===\n");

        ScriptSet set = ScriptSet.Open(dir);

        Assert.Equal(new[] { first, second }, set.Paths);
        Assert.Equal(second, set.Find("u1").Path);
        Assert.Equal(first, set.Find("line:t1").Path);
        Assert.Throws<InvalidDataException>(() => set.Find("없는줄"));

        File.WriteAllText(Path.Combine(dir, "c.yarn"), "title: V\n---\n바: 겹친 줄 #line:u1\n===\n");
        Assert.Throws<InvalidDataException>(() => ScriptSet.Open(dir).Find("u1"));
    }

    [Fact]
    public void 명세_표를_내보낸_JSON으로_다시_읽는다()
    {
        List<string> problems = [];
        StageCommandSpecTable table = SpecTables.Parse(SpecTables.ToJson(StageCommandSpecTable.Default), problems);

        Assert.Empty(problems);
        Assert.Equal(StageCommandSpecTable.Default.All.Select(s => s.ToString()), table.All.Select(s => s.ToString()));
        Assert.Throws<InvalidDataException>(() => SpecTables.Parse("{\"schema\":\"command-specs/0\"}", problems));
    }
}
