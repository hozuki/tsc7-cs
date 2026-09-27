// ported from: internal/sourcemap/generator_test.go
//
// 与 Go 测试的差异（逐条）：
// - Go 的 `assert.DeepEqual(t, sourceMap, &RawSourceMap{...})` → 逐字段 `Assert.Equal`（C# 的数组比较用
//   xunit 的 `Assert.Equal(IEnumerable, IEnumerable)`；可空数组 `SourcesContent` 比较 null/元素）。
// - Go 的 `(T, error)` → C# 抛异常：`assert.NilError` 变成裸调用（成功），`assert.Error(t, err, "msg")` 变成
//   `Assert.Equal("msg", Assert.Throws<ArgumentException>(...).Message)`。
// - `gen.String()`（stringer）→ C# 的 `gen.ToString()`（另有 `SourceMap.String(gen)` 扩展方法保住 Go 写法）。
// - `tspath.ComparePathsOptions{}` → `new ComparePathsOptions()`。
using TypeScript.SourceMap;
using static TypeScript.SourceMap.SourceMap;
using TypeScript.TSPath;
using Xunit;

namespace TypeScript.Tests;

public class SourceMapGeneratorTests
{
    private static Generator NewTestGenerator() =>
        NewGenerator("main.js", "/", "/", new ComparePathsOptions());

    private static void AssertMap(
        RawSourceMap map,
        string file,
        string sourceRoot,
        string[] sources,
        string mappings,
        string[] names,
        string?[]? sourcesContent = null)
    {
        Assert.Equal(3, map.Version);
        Assert.Equal(file, map.File);
        Assert.Equal(sourceRoot, map.SourceRoot);
        Assert.Equal(sources, map.Sources);
        Assert.Equal(mappings, map.Mappings);
        Assert.Equal(names, map.Names);
        if (sourcesContent is null)
        {
            Assert.Null(map.SourcesContent);
        }
        else
        {
            Assert.Equal(sourcesContent, map.SourcesContent);
        }
    }

    [Fact]
    public void Generator_Empty()
    {
        var gen = NewTestGenerator();
        AssertMap(gen.RawSourceMap(), "main.js", "/", [], "", []);
    }

    [Fact]
    public void Generator_Empty_Serialized()
    {
        var gen = NewTestGenerator();
        Assert.Equal("""{"version":3,"file":"main.js","sourceRoot":"/","sources":[],"names":[],"mappings":""}""", gen.ToString());
    }

    [Fact]
    public void Generator_AddSource()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        Assert.Equal(0, (int)sourceIndex);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "", []);
    }

    [Fact]
    public void Generator_SetSourceContent()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        const string sourceContent = "foo";
        gen.SetSourceContent(sourceIndex, sourceContent);
        Assert.Equal(0, (int)sourceIndex);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "", [], [sourceContent]);
    }

    [Fact]
    public void Generator_SetSourceContent_ForSecondSourceOnly()
    {
        var gen = NewTestGenerator();
        gen.AddSource("/skipped.ts");
        var sourceIndex = gen.AddSource("/main.ts");
        const string sourceContent = "foo";
        gen.SetSourceContent(sourceIndex, sourceContent);
        Assert.Equal(1, (int)sourceIndex);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["skipped.ts", "main.ts"], "", [], [null, sourceContent]);
    }

    [Fact]
    public void Generator_SetSourceContent_SourceIndexOutOfRange()
    {
        var gen = NewTestGenerator();
        Assert.Equal("sourceIndex is out of range", Assert.Throws<ArgumentException>(() => gen.SetSourceContent(-1, "")).Message);
        Assert.Equal("sourceIndex is out of range", Assert.Throws<ArgumentException>(() => gen.SetSourceContent(0, "")).Message);
    }

    [Fact]
    public void Generator_SetSourceContent_ForSecondSourceOnly_Serialized()
    {
        var gen = NewTestGenerator();
        gen.AddSource("/skipped.ts");
        var sourceIndex = gen.AddSource("/main.ts");
        gen.SetSourceContent(sourceIndex, "foo");
        Assert.Equal(
            """{"version":3,"file":"main.js","sourceRoot":"/","sources":["skipped.ts","main.ts"],"names":[],"mappings":"","sourcesContent":[null,"foo"]}""",
            gen.ToString());
    }

    [Fact]
    public void Generator_AddName()
    {
        var gen = NewTestGenerator();
        var nameIndex = gen.AddName("foo");
        Assert.Equal(0, (int)nameIndex);
        AssertMap(gen.RawSourceMap(), "main.js", "/", [], "", ["foo"]);
    }

    [Fact]
    public void Generator_AddGeneratedMapping()
    {
        var gen = NewTestGenerator();
        gen.AddGeneratedMapping(0, 0);
        AssertMap(gen.RawSourceMap(), "main.js", "/", [], "A", []);
    }

    [Fact]
    public void Generator_AddGeneratedMapping_ReplacesPendingSourceMapping()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddSourceMapping(0, 0, sourceIndex, 0, 0);
        gen.AddGeneratedMapping(0, 0);
        Assert.Equal("A", gen.RawSourceMap().Mappings);
    }

    [Fact]
    public void Generator_AddGeneratedMapping_IsNotReplacedBySourceMapping()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddGeneratedMapping(0, 0);
        gen.AddSourceMapping(0, 0, sourceIndex, 0, 0);
        Assert.Equal("A", gen.RawSourceMap().Mappings);
    }

    [Fact]
    public void Generator_AddGeneratedMapping_OnSecondLineOnly()
    {
        var gen = NewTestGenerator();
        gen.AddGeneratedMapping(1, 0);
        AssertMap(gen.RawSourceMap(), "main.js", "/", [], ";A", []);
    }

    [Fact]
    public void Generator_AddSourceMapping()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddSourceMapping(0, 0, sourceIndex, 0, 0);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "AAAA", []);
    }

    [Fact]
    public void Generator_AddSourceMapping_NextGeneratedCharacter()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddSourceMapping(0, 0, sourceIndex, 0, 0);
        gen.AddSourceMapping(0, 1, sourceIndex, 0, 0);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "AAAA,CAAA", []);
    }

    [Fact]
    public void Generator_AddSourceMapping_NextGeneratedAndSourceCharacter()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddSourceMapping(0, 0, sourceIndex, 0, 0);
        gen.AddSourceMapping(0, 1, sourceIndex, 0, 1);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "AAAA,CAAC", []);
    }

    [Fact]
    public void Generator_AddSourceMapping_NextGeneratedLine()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddSourceMapping(0, 0, sourceIndex, 0, 0);
        gen.AddSourceMapping(1, 0, sourceIndex, 0, 0);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "AAAA;AAAA", []);
    }

    [Fact]
    public void Generator_AddSourceMapping_PreviousSourceCharacter()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddSourceMapping(0, 0, sourceIndex, 0, 1);
        gen.AddSourceMapping(0, 1, sourceIndex, 0, 0);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "AAAC,CAAD", []);
    }

    [Fact]
    public void Generator_AddNamedSourceMapping()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        var nameIndex = gen.AddName("foo");
        gen.AddNamedSourceMapping(0, 0, sourceIndex, 0, 0, nameIndex);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "AAAAA", ["foo"]);
    }

    [Fact]
    public void Generator_AddNamedSourceMapping_WithPreviousName()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        var nameIndex1 = gen.AddName("foo");
        var nameIndex2 = gen.AddName("bar");
        gen.AddNamedSourceMapping(0, 0, sourceIndex, 0, 0, nameIndex2);
        gen.AddNamedSourceMapping(0, 1, sourceIndex, 0, 0, nameIndex1);
        AssertMap(gen.RawSourceMap(), "main.js", "/", ["main.ts"], "AAAAC,CAAAD", ["foo", "bar"]);
    }

    [Fact]
    public void Generator_AddGeneratedMapping_GeneratedLineCannotBacktrack()
    {
        var gen = NewTestGenerator();
        gen.AddGeneratedMapping(1, 0);
        Assert.Equal("generatedLine cannot backtrack", Assert.Throws<ArgumentException>(() => gen.AddGeneratedMapping(0, 0)).Message);
    }

    [Fact]
    public void Generator_AddGeneratedMapping_GeneratedCharacterCannotBeNegative()
    {
        var gen = NewTestGenerator();
        gen.AddGeneratedMapping(0, 0);
        Assert.Equal("generatedCharacter cannot be negative", Assert.Throws<ArgumentException>(() => gen.AddGeneratedMapping(0, -1)).Message);
    }

    [Fact]
    public void Generator_AddSourceMapping_GeneratedLineCannotBacktrack()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddSourceMapping(1, 0, sourceIndex, 0, 0);
        Assert.Equal("generatedLine cannot backtrack", Assert.Throws<ArgumentException>(() => gen.AddSourceMapping(0, 0, sourceIndex, 0, 0)).Message);
    }

    [Fact]
    public void Generator_AddSourceMapping_GeneratedCharacterCannotBeNegative()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        gen.AddSourceMapping(0, 0, sourceIndex, 0, 0);
        Assert.Equal("generatedCharacter cannot be negative", Assert.Throws<ArgumentException>(() => gen.AddSourceMapping(0, -1, sourceIndex, 0, 0)).Message);
    }

    [Fact]
    public void Generator_AddSourceMapping_SourceIndexIsOutOfRange()
    {
        var gen = NewTestGenerator();
        Assert.Equal("sourceIndex is out of range", Assert.Throws<ArgumentException>(() => gen.AddSourceMapping(0, 0, -1, 0, 0)).Message);
        Assert.Equal("sourceIndex is out of range", Assert.Throws<ArgumentException>(() => gen.AddSourceMapping(0, 0, 0, 0, 0)).Message);
    }

    [Fact]
    public void Generator_AddSourceMapping_SourceLineCannotBeNegative()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        Assert.Equal("sourceLine cannot be negative", Assert.Throws<ArgumentException>(() => gen.AddSourceMapping(0, 0, sourceIndex, -1, 0)).Message);
    }

    [Fact]
    public void Generator_AddSourceMapping_SourceCharacterCannotBeNegative()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        Assert.Equal("sourceCharacter cannot be negative", Assert.Throws<ArgumentException>(() => gen.AddSourceMapping(0, 0, sourceIndex, 0, -1)).Message);
    }

    [Fact]
    public void Generator_AddNamedSourceMapping_GeneratedLineCannotBacktrack()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        var nameIndex = gen.AddName("foo");
        gen.AddNamedSourceMapping(1, 0, sourceIndex, 0, 0, nameIndex);
        Assert.Equal("generatedLine cannot backtrack", Assert.Throws<ArgumentException>(() => gen.AddNamedSourceMapping(0, 0, sourceIndex, 0, 0, nameIndex)).Message);
    }

    [Fact]
    public void Generator_AddNamedSourceMapping_GeneratedCharacterCannotBeNegative()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        var nameIndex = gen.AddName("foo");
        gen.AddNamedSourceMapping(0, 0, sourceIndex, 0, 0, nameIndex);
        Assert.Equal("generatedCharacter cannot be negative", Assert.Throws<ArgumentException>(() => gen.AddNamedSourceMapping(0, -1, sourceIndex, 0, 0, nameIndex)).Message);
    }

    [Fact]
    public void Generator_AddNamedSourceMapping_SourceIndexIsOutOfRange()
    {
        var gen = NewTestGenerator();
        var nameIndex = gen.AddName("foo");
        Assert.Equal("sourceIndex is out of range", Assert.Throws<ArgumentException>(() => gen.AddNamedSourceMapping(0, 0, -1, 0, 0, nameIndex)).Message);
        Assert.Equal("sourceIndex is out of range", Assert.Throws<ArgumentException>(() => gen.AddNamedSourceMapping(0, 0, 0, 0, 0, nameIndex)).Message);
    }

    [Fact]
    public void Generator_AddNamedSourceMapping_SourceLineCannotBeNegative()
    {
        var gen = NewTestGenerator();
        var nameIndex = gen.AddName("foo");
        var sourceIndex = gen.AddSource("/main.ts");
        Assert.Equal("sourceLine cannot be negative", Assert.Throws<ArgumentException>(() => gen.AddNamedSourceMapping(0, 0, sourceIndex, -1, 0, nameIndex)).Message);
    }

    [Fact]
    public void Generator_AddNamedSourceMapping_SourceCharacterCannotBeNegative()
    {
        var gen = NewTestGenerator();
        var nameIndex = gen.AddName("foo");
        var sourceIndex = gen.AddSource("/main.ts");
        Assert.Equal("sourceCharacter cannot be negative", Assert.Throws<ArgumentException>(() => gen.AddNamedSourceMapping(0, 0, sourceIndex, 0, -1, nameIndex)).Message);
    }

    [Fact]
    public void Generator_AddNamedSourceMapping_NameIndexIsOutOfRange()
    {
        var gen = NewTestGenerator();
        var sourceIndex = gen.AddSource("/main.ts");
        Assert.Equal("nameIndex is out of range", Assert.Throws<ArgumentException>(() => gen.AddNamedSourceMapping(0, 0, sourceIndex, 0, 0, -1)).Message);
        Assert.Equal("nameIndex is out of range", Assert.Throws<ArgumentException>(() => gen.AddNamedSourceMapping(0, 0, sourceIndex, 0, 0, 0)).Message);
    }
}
