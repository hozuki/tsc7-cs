// ported from: internal/fourslash/test_parser.go:1-522
//
// 偏离清单（相对 Go 原文逐条）：
// 1) **字节 vs UTF-16 代码单元**：Go 的 `content[i]`、`utf8.DecodeRuneInString`、`len(content)` 都在 UTF-8
//    字节上工作；本仓的内部坐标是 **UTF-16 代码单元**（Core/Text.cs 文件头），所以下标、长度、切片一律按
//    UTF-16 重写：`size` 就是消耗的代码单元数（与 LsConv/Linemap.cs 文件头第 4 条同一套约定）。
//    对 ASCII 定界符（`/`、`*`、`|`、`[`、`]`、`{`、`}`）的前瞻（`content[i+1]`）两种编码下判定一致。
// 2) `utf8.DecodeRuneInString` → 仓库既有的 `Scanner.DecodeRuneAt`（`using static TypeScript.Scanner.Scanner;`）。
//    它是编译器程序集的 internal，靠 `InternalsVisibleTo("TypeScript.Harness")` 可见（M8 为 fourslash harness
//    统一开的内部面，见 src/TypeScript.Compiler/Properties/AssemblyInfo.cs）。
// 3) 包级函数 → `FourslashTest` partial 上的 `static` 成员（Go 未导出 → `internal static`；`ParseTestData`
//    是导出函数 → `public static`）。按 FsCore 的接口冻结表，宿主是 `FourslashTest` 而不是单独的 `Fourslash`
//    静态类（先例：FourslashTest.Capabilities.cs 的 `GetCapabilitiesWithDefaults`）。
// 4) `fourslashError`（未导出错误类型）→ `FourslashErrorException`（继承 Exception）。`(T, error)` → 抛异常：
//    `reportError` 返回异常对象、调用点 `throw ReportError(...)`。`ParseTestData` 里 Go 把 testrunner 回传的
//    error 变成 `t.Fatalf("Error parsing fourslash data: %s", e.Error())`，C# 侧回调直接抛出 → 在此 catch
//    后 `t.Fatal`，文本与 Go 逐字节相同。C# 侧的形状是"先声明并给空集合 + try 里解构赋值 + catch 里
//    `t.Fatal`"：编译器不把接口成员（`IHarnessTest.Fatal`）上的 [DoesNotReturn] 计入**确定赋值**分析，
//    不先给初值会报 CS0165（实测报错后再改的）。
// 5) `t.Helper()` 删除；`t.Fatalf(fmt, args...)` → `t.Fatal($"...")`。
// 6) `strings.TrimSpace` → `TsOptions.TrimSpace`（Go 的 unicode.IsSpace 集合，TsOptions/ParsingHelpers.cs；
//    同样是靠 InternalsVisibleTo("TypeScript.Harness") 可见的 internal）。
// 7) `strings.ToLower` → `ToLowerJS`（§3.4 的 JS 小写映射，`StringUtil` 既有搬运方式）。
// 8) `strings.Split(content, "\n")` → `content.Split('\n')`；`strings.Join(result, "\n")` → `string.Join('\n', result)`。
// 9) `slices.SortStableFunc(rangeMarkers, cmp)` → `OrderBy(Pos).ThenByDescending(End)`：与 Go 的比较函数给出
//    同一个全序，且 LINQ 的 OrderBy/ThenBy 是**稳定**排序 → 比较函数返回 0 的元素保持原顺序，与 Go 同。
// 10) `testFileWithMarkers` → `TestFileWithMarkers`（Go 未导出类型，只在包内用 → internal）。
// 11) `locationInformation`/`rangeLocationInformation` → internal 类；Go 对前者的**嵌入**（字段提升）在 C# 里
//     用组合表达（`_locationInformation` 字段），于是 Go 的 `openRange.sourceLine` 写作
//     `openRange._locationInformation._sourceLine`。`rangeLocationInformation` 必须是引用类型：Go 的
//     `openRanges[len-1].marker = marker` 要能改到容器元素本身。
// 12) `parserState` 的三个常量（Go 的 iota）→ `ParserState.None`/`InSlashStarMarker`/`InObjectMarker`；
//     `emitThisFileOption`/`noOpenFileOption` → private const（仅本文件使用，按 §2 的私有字段命名规则加 `_`）。
// 13) `map[string]string` 的"缺键得零值"（`fileOptions[emitThisFileOption] == "true"`）→ 显式 `TryGetValue`
//     （缺键得 ""，判定结果一致）；`isStateBaseliningEnabled`/`hasUnsupportedGlobalOptionsWithConfig` 收到的
//     map 在 Go 里可以是 nil（读 nil map 得零值、遍历 nil map 不迭代）→ 参数写成 `Dictionary<string,string>?`。
// 14) `map[string]any` → `Dictionary<string, object?>`；`json.Unmarshal([]byte(...), &v)` 的 `any` 目标
//     → `Json.Unmarshal<object>(...)`（Json/Decode.cs 文件头：typeof(object) 按 JSON 形状解码为
//     Dictionary/List/double/string/bool/null；解析失败在 C# 侧抛 `JsonException`，故用 try/catch 落回
//     Go 的 `e != nil` 分支）。
// 15) `Marker.MakerWithSymlink` 保留 Go 原文拼写（Go 侧就是 `MakerWithSymlink`，不是 `MarkerWithSymlink`）。
// 16) Go 的字段/类型名照抄可见性：导出（`Range`/`LSRange`/`Marker`/`Position`/`LSPosition`/`Name`/`Data`/
//     `Content`）→ public；未导出（`fileName`/`emit`/`open`/`marker`/`position`/`sourcePosition`/`sourceLine`/
//     `sourceColumn`/`file`/`markers`/`ranges`）→ internal + `_camelCase`。
// 17) C# 侧新增（FsCore 接口冻结表要求）：`TestData` 的切片/map 字段声明为可空（Go 的 nil 切片/nil map 语义，
//     调用点 `?? []`）；`RangeMarker` 增加 `(string fileName, TextRange range, Range lsRange, Marker? marker)`
//     主构造函数（Go 用复合字面量，C# 侧其它切片要按参数构造）。
// 18) C# 侧新增（FsBaseline 的 `MarkerAndItem<T>` 需要）：`Marker` 实现 `TypeScript.Json.IJsonMarshaler`，
//     按 Go 导出字段的**声明顺序**写 `{"Position":…,"LSPosition":{line,character},"Name":…,"Data":…}`
//     （`fileName` 未导出不入 JSON；Go 侧是 `core.StringifyJson(markersAndItems, "", "  ")` 的反射结果，
//     C# 无反射序列化 → 手写）。**nil `Data` 写成 `{}` 而不是 `null`**：Go 的 json/v2 默认
//     `FormatNilMapAsNull=false`（nil map → `{}`），tsgo 已提交的 1731 处 `"Data"` 全是 `{}`
//     （见 testdata/baselines/reference/fourslash/quickInfo/*.baseline），照 `null` 写会全量不匹配。
//     `Name` 是 `*string`（指针）→ nil 写 `null` ✓。多键 `Data` 的键序不锁：Go json/v2 默认
//     `Deterministic=false`，Go 侧本身就不确定（语料里没有多键 Data 的样例）。

using System.Text;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.TestRunner;
using TypeScript.TestUtil;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;
using static TypeScript.LsConv.LsConv;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TestRunner.TestRunner;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using JsonEncoder = TypeScript.Json.Encoder;
using Position = TypeScript.LsProto.Position;
using Range = TypeScript.LsProto.Range;
using SpanMapType = TypeScript.SpanMap.SpanMap;

namespace TypeScript.Harness.Fourslash;

/// <summary>
/// Go: <c>type MarkerOrRange interface</c>（test_parser.go:81-85）。标记（<see cref="Marker"/>）与区间标记
/// （<see cref="RangeMarker"/>）的共同视图：都定位到某个文件里的一个 LSP 位置。
/// </summary>
public interface MarkerOrRange
{
    Position LSPos();

    string FileName();

    string? GetName();
}

/// <summary>
/// Go: <c>type RangeMarker struct</c>（test_parser.go:23-27）。源码里用 <c>[|</c> … <c>|]</c> 围起来的区间，例如
/// <c>[|text in range|]</c> 就是一段被"选中"的 <c>text in range</c>。
/// </summary>
public sealed class RangeMarker(string fileName, TextRange range, Range lsRange, Marker? marker) : MarkerOrRange
{
    internal string _fileName = fileName;

    public TextRange Range = range;

    public Range LSRange = lsRange;

    public Marker? Marker = marker;

    public Position LSPos()
    {
        return LSRange.Start;
    }

    public string FileName()
    {
        return _fileName;
    }

    public string? GetName()
    {
        if (Marker is null)
        {
            return null;
        }

        return Marker.Name;
    }

    public Location LSLocation()
    {
        return new Location
        {
            Uri = FileNameToDocumentURI(_fileName),
            Range = LSRange,
        };
    }
}

/// <summary>Go: <c>type Marker struct</c>（test_parser.go:51-57）。</summary>
public sealed class Marker : MarkerOrRange, TypeScript.Json.IJsonMarshaler
{
    internal string _fileName = "";

    public int Position;

    public Position LSPosition = new();

    /// <summary>`nil` for anonymous markers such as `{| "foo": "bar" |}`。</summary>
    public string? Name;

    public Dictionary<string, object?>? Data;

    public Position LSPos()
    {
        return LSPosition;
    }

    public string FileName()
    {
        return _fileName;
    }

    public string? GetName()
    {
        return Name;
    }

    /// <summary>
    /// Go: `type Marker struct` 的**导出**字段（声明顺序 = JSON 键顺序；未导出的 `fileName` 不入 JSON）经
    /// encoding/json 反射写出的线格式 —— fourslash 的 quickinfo/completions 基线把
    /// `markerAndItem[*Marker]` 整个 `core.StringifyJson(…, "", "  ")`（fourslash.go:2978 等），其中 `"marker"`
    /// 就是这个形状。C# 无反射序列化（docs/json.md §2）→ 手写（Go 的 `LSPosition` 走它自己的
    /// `MarshalJSONTo`；`Data` 为 nil 时写 `null`，键顺序由 Json 库按 Go 的规则排序）。
    /// </summary>
    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        MarshalEncode(encoder, "Position");
        MarshalEncode(encoder, Position);
        MarshalEncode(encoder, "LSPosition");
        MarshalEncode(encoder, LSPosition);
        MarshalEncode(encoder, "Name");
        MarshalEncode(encoder, Name);
        MarshalEncode(encoder, "Data");
        if (Data is null)
        {
            // Go 的 json/v2 把 **nil map** 写成 `{}` 而不是 `null`（tsgo fourslash 基线里所有普通
            // `/*1*/` 标记的 `"Data"` 都是 `{}`，见 reference/fourslash/quickInfo/*.baseline）；
            // C# 侧传 object? 会丢静态类型信息、只能写 null，故这里显式写空对象。
            encoder.WriteToken(BeginObject);
            encoder.WriteToken(EndObject);
        }
        else
        {
            MarshalEncode(encoder, Data);
        }

        encoder.WriteToken(EndObject);
    }

    // Go 原文的方法名就是 MakerWithSymlink（拼写偏离，照抄）。
    public Marker MakerWithSymlink(string fileName)
    {
        return new Marker
        {
            _fileName = fileName,
            Position = Position,
            LSPosition = LSPosition,
            Name = Name,
            Data = Data,
        };
    }
}

/// <summary>Go: <c>type TestData struct</c>（test_parser.go:87-94）：一个 fourslash 用例解析出来的全部数据。</summary>
public sealed class TestData
{
    public TestFileInfo[]? Files;

    public Dictionary<string, Marker> MarkerPositions = [];

    public Marker[]? Markers;

    public Dictionary<string, string>? Symlinks;

    public Dictionary<string, string>? GlobalOptions;

    public RangeMarker[]? Ranges;

    public bool IsStateBaseliningEnabled()
    {
        return FourslashTest.IsStateBaseliningEnabled(GlobalOptions);
    }
}

/// <summary>Go: <c>type testFileWithMarkers struct</c>（test_parser.go:100-104）。</summary>
internal sealed class TestFileWithMarkers
{
    internal TestFileInfo _file = new();
    internal Marker[] _markers = [];
    internal RangeMarker[] _ranges = [];
}

/// <summary>Go: <c>type locationInformation struct</c>（test_parser.go:190-196）：解析中的位置（0 基位置 + 1 基行列）。</summary>
internal sealed class LocationInformation
{
    internal int _position;
    internal int _sourcePosition;
    internal int _sourceLine;
    internal int _sourceColumn;
}

/// <summary>
/// Go: <c>type rangeLocationInformation struct { locationInformation; marker *Marker }</c>
/// （test_parser.go:198-201）。Go 的嵌入（字段提升）在 C# 里用组合表达；用引用类型承载，才能像 Go 的
/// <c>openRanges[len-1].marker = marker</c> 那样改到容器元素本身。
/// </summary>
internal sealed class RangeLocationInformation
{
    internal LocationInformation _locationInformation = new();
    internal Marker? _marker;
}

/// <summary>
/// Go: <c>type TestFileInfo struct</c>（test_parser.go:203-208）。实现 <see cref="Script"/>，供
/// <see cref="Converters"/> 把内部坐标换算成 LSP 行列。
/// </summary>
public sealed class TestFileInfo : Script
{
    internal string _fileName = "";

    /// <summary>The contents of the file (with markers, etc stripped out)。</summary>
    public string Content = "";

    internal bool _emit;
    internal bool _open;

    /// <summary>FileName implements lsconv.Script。</summary>
    public string FileName()
    {
        return _fileName;
    }

    /// <summary>OriginalFileName implements lsconv.Script。</summary>
    public string OriginalFileName()
    {
        return _fileName;
    }

    /// <summary>Text implements lsconv.Script。</summary>
    public string Text()
    {
        return Content;
    }

    /// <summary>OriginalText implements lsconv.Script。</summary>
    public string OriginalText()
    {
        return Content;
    }

    /// <summary>SpanMap implements lsconv.Script。</summary>
    public SpanMapType? SpanMap()
    {
        return null;
    }
}

/// <summary>Go: <c>type parserState int</c>（test_parser.go:235-241）。</summary>
internal enum ParserState
{
    None = 0,
    InSlashStarMarker,
    InObjectMarker,
}

/// <summary>Go: <c>type fourslashError struct</c>（test_parser.go:511-517）：fourslash 解析错误。</summary>
internal sealed class FourslashErrorException : Exception
{
    public FourslashErrorException(string message)
        : base(message)
    {
    }
}

/// <summary>Go: <c>package fourslash</c> 的包级函数（test_parser.go）。</summary>
public sealed partial class FourslashTest
{
    private const string _emitThisFileOption = "emitthisfile";
    private const string _noOpenFileOption = "noopen";

    /// <summary>Go: <c>utf8.RuneError</c>（非法 UTF-8 / EOF 的返回值）。</summary>
    private const int _runeError = 0xFFFD;

    /// <summary>Go: <c>func ParseTestData(t *testing.T, contents string, fileName string) TestData</c>（test_parser.go:110-170）。</summary>
    public static TestData ParseTestData(IHarnessTest t, string contents, string fileName)
    {
        // List of all the subfiles we've parsed out
        var files = new List<TestFileInfo>();

        var markerPositions = new Dictionary<string, Marker>();
        var markers = new List<Marker>();
        var ranges = new List<RangeMarker>();

        // 见文件头第 4 条：Go 的 `a, b, c, d, e := f()` 在 C# 里落成"先声明 + try 里解构赋值 + catch 里 t.Fatal"；
        // 编译器不把接口成员上的 [DoesNotReturn] 计入确定赋值分析，故先给空集合（随后必然被覆盖）。
        TestFileWithMarkers[] filesWithMarker = [];
        Dictionary<string, string> symlinks = [];
        Dictionary<string, string> globalOptions = [];
        try
        {
            (filesWithMarker, symlinks, _, globalOptions) = ParseTestFilesAndSymlinksWithOptions(
                contents,
                fileName,
                ParseFileContent,
                new ParseTestFilesOptions
                {
                    AllowImplicitFirstFile = true,
                });
        }
        catch (FourslashErrorException e)
        {
            t.Fatal($"Error parsing fourslash data: {e.Message}");
        }

        var hasTSConfig = false;
        foreach (var file in filesWithMarker)
        {
            files.Add(file._file);
            hasTSConfig = hasTSConfig || IsConfigFile(file._file._fileName);

            markers.AddRange(file._markers);
            ranges.AddRange(file._ranges);
            foreach (var marker in file._markers)
            {
                if (marker.Name is null)
                {
                    if (marker.Data is not null)
                    {
                        // The marker is an anonymous object marker, which does not need a name. Markers are only set into markerPositions if they have a name
                        continue;
                    }

                    t.Fatal($"Marker at position {marker.Position} is unnamed");
                }

                if (markerPositions.TryGetValue(marker.Name, out var existing))
                {
                    t.Fatal($"Duplicate marker name: \"{marker.Name}\" at {marker.Position} and {existing.Position}");
                }

                markerPositions[marker.Name] = marker;
            }
        }

        if (hasTSConfig && HasUnsupportedGlobalOptionsWithConfig(globalOptions) && !IsStateBaseliningEnabled(globalOptions))
        {
            t.Fatal("It is not allowed to use global options along with config files.");
        }

        return new TestData
        {
            Files = [.. files],
            MarkerPositions = markerPositions,
            Markers = [.. markers],
            Symlinks = symlinks,
            GlobalOptions = globalOptions,
            Ranges = [.. ranges],
        };
    }

    /// <summary>Go: <c>func isStateBaseliningEnabled(globalOptions map[string]string) bool</c>（test_parser.go:106-108）。</summary>
    internal static bool IsStateBaseliningEnabled(Dictionary<string, string>? globalOptions)
    {
        return globalOptions is not null && globalOptions.TryGetValue("statebaseline", out var value) && value == "true";
    }

    /// <summary>Go: <c>func hasUnsupportedGlobalOptionsWithConfig(globalOptions map[string]string) bool</c>（test_parser.go:172-183）。</summary>
    internal static bool HasUnsupportedGlobalOptionsWithConfig(Dictionary<string, string>? globalOptions)
    {
        if (globalOptions is null)
        {
            return false;
        }

        foreach (var option in globalOptions.Keys)
        {
            switch (ToLowerJS(option))
            {
                case "symlink":
                case "link":
                case "usecasesensitivefilenames":
                    continue;
                default:
                    return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func isConfigFile(fileName string) bool</c>（test_parser.go:185-188）。</summary>
    internal static bool IsConfigFile(string fileName)
    {
        fileName = ToLowerJS(fileName);
        return fileName.EndsWith("tsconfig.json", StringComparison.Ordinal) || fileName.EndsWith("jsconfig.json", StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func reportError(fileName string, line int, col int, message string) error</c>（test_parser.go:492-494）。</summary>
    internal static FourslashErrorException ReportError(string fileName, int line, int col, string message)
    {
        return new FourslashErrorException($"{fileName} ({line},{col}): {message}");
    }

    /// <summary>Go: <c>func chompLeadingSpace(content string) string</c>（test_parser.go:496-509）。</summary>
    internal static string ChompLeadingSpace(string content)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            if (line.Length > 0 && line[0] != ' ')
            {
                return content;
            }
        }

        var result = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            result[i] = lines[i].Length > 0 ? lines[i][1..] : "";
        }

        return string.Join('\n', result);
    }

    /// <summary>Go: <c>func parseFileContent(fileName string, content string, fileOptions map[string]string) (*testFileWithMarkers, error)</c>（test_parser.go:253-460）。</summary>
    internal static TestFileWithMarkers ParseFileContent(string fileName, string content, Dictionary<string, string> fileOptions)
    {
        fileName = GetNormalizedAbsolutePath(fileName, "/");
        content = ChompLeadingSpace(content);

        // The file content (minus metacharacters) so far
        var output = new StringBuilder();

        var markers = new List<Marker>();

        // A stack of the open range markers that are still unclosed
        var openRanges = new List<RangeLocationInformation>();

        // A list of closed ranges we've collected so far
        var rangeMarkers = new List<RangeMarker>();

        // The total number of metacharacters removed from the file (so far)
        var difference = 0;

        // One-based current position data
        var line = 1;
        var column = 1;

        // The current marker (or maybe multi-line comment?) we're parsing, possibly
        LocationInformation? openMarker = null;

        // The latest position of the start of an unflushed plain text area
        var lastNormalCharPosition = 0;

        void Flush(int lastSafeCharIndex)
        {
            if (lastSafeCharIndex != -1)
            {
                output.Append(content, lastNormalCharPosition, lastSafeCharIndex - lastNormalCharPosition);
            }
            else
            {
                output.Append(content, lastNormalCharPosition, content.Length - lastNormalCharPosition);
            }
        }

        var state = ParserState.None;
        // Go: previousCharacter, i := utf8.DecodeRuneInString(content)；C# 的 for 后置语句就是 Go 的 i = i + size
        // （continue 同样会走到后置语句，与 Go 一致）。
        var previousCharacter = DecodeRuneAt(content, 0, out var size);
        for (var i = size; i < content.Length; i += size)
        {
            var currentCharacter = DecodeRuneAt(content, i, out size);
            switch (state)
            {
                case ParserState.None:
                    if (previousCharacter == '[' && currentCharacter == '|')
                    {
                        // found a range start
                        openRanges.Add(new RangeLocationInformation
                        {
                            _locationInformation = new LocationInformation
                            {
                                _position = (i - 1) - difference,
                                _sourcePosition = i - 1,
                                _sourceLine = line,
                                _sourceColumn = column,
                            },
                        });
                        // copy all text up to marker position
                        Flush(i - 1);
                        lastNormalCharPosition = i + 1;
                        difference += 2;
                    }
                    else if (previousCharacter == '|' && currentCharacter == ']')
                    {
                        // found a range end
                        if (openRanges.Count == 0)
                        {
                            throw ReportError(fileName, line, column, "Found range end with no matching start.");
                        }

                        var rangeStart = openRanges[^1];
                        openRanges.RemoveAt(openRanges.Count - 1);

                        rangeMarkers.Add(new RangeMarker(
                            fileName,
                            NewTextRange(rangeStart._locationInformation._position, (i - 1) - difference),
                            new Range(),
                            rangeStart._marker));

                        // copy all text up to range marker position
                        Flush(i - 1);
                        lastNormalCharPosition = i + 1;
                        difference += 2;
                    }
                    else if (previousCharacter == '/' && currentCharacter == '*' && (i + 1 >= content.Length || content[i + 1] != '/'))
                    {
                        // found a possible marker start
                        state = ParserState.InSlashStarMarker;
                        openMarker = new LocationInformation
                        {
                            _position = (i - 1) - difference,
                            _sourcePosition = i - 1,
                            _sourceLine = line,
                            _sourceColumn = column - 1,
                        };
                    }
                    else if (previousCharacter == '{' && currentCharacter == '|')
                    {
                        // found an object marker start
                        state = ParserState.InObjectMarker;
                        openMarker = new LocationInformation
                        {
                            _position = (i - 1) - difference,
                            _sourcePosition = i - 1,
                            _sourceLine = line,
                            _sourceColumn = column,
                        };
                        Flush(i - 1);
                    }
                    break;
                case ParserState.InObjectMarker:
                    // Object markers are only ever terminated by |} and have no content restrictions
                    if (previousCharacter == '|' && currentCharacter == '}')
                    {
                        var start = openMarker!;
                        var objectMarkerData = TrimSpace(content[(start._sourcePosition + 2)..(i - 1)]);
                        var marker = GetObjectMarker(fileName, start, objectMarkerData);

                        if (openRanges.Count > 0)
                        {
                            openRanges[^1]._marker = marker;
                        }

                        markers.Add(marker);

                        // Set the current start to point to the end of the current marker to ignore its text
                        lastNormalCharPosition = i + 1;
                        difference += i + 1 - start._sourcePosition;

                        // Reset the state
                        openMarker = null;
                        state = ParserState.None;
                    }
                    break;
                case ParserState.InSlashStarMarker:
                    if (previousCharacter == '*' && currentCharacter == '/')
                    {
                        // Record the marker
                        // start + 2 to ignore the /*, -1 on the end to ignore the * (/ is next)
                        var start = openMarker!;
                        var markerNameText = TrimSpace(content[(start._sourcePosition + 2)..(i - 1)]);
                        var marker = new Marker
                        {
                            _fileName = fileName,
                            Position = start._position,
                            Name = markerNameText,
                        };
                        if (openRanges.Count > 0)
                        {
                            openRanges[^1]._marker = marker;
                        }

                        markers.Add(marker);

                        // Set the current start to point to the end of the current marker to ignore its text
                        Flush(start._sourcePosition);
                        lastNormalCharPosition = i + 1;
                        difference += i + 1 - start._sourcePosition;

                        // Reset the state
                        openMarker = null;
                        state = ParserState.None;
                    }
                    else if (!(IsDigit(currentCharacter)
                        || IsASCIILetter(currentCharacter)
                        || currentCharacter == '$'
                        || currentCharacter == '_'))
                    {
                        // We've hit a non-valid marker character, so we were actually in a block comment.
                        // (The only exception is a marker that is about to be closed: `*/` — ignore the 'invalid' char.)
                        if (!(currentCharacter == '*' && i < content.Length - 1 && content[i + 1] == '/'))
                        {
                            // Bail out the text we've gathered so far back into the output
                            Flush(i);
                            lastNormalCharPosition = i;
                            openMarker = null;
                            state = ParserState.None;
                        }
                    }
                    break;
                default:
                    break;
            }

            if (currentCharacter == '\n' && previousCharacter == '\r')
            {
                // Ignore trailing \n after \r
                continue;
            }
            else if (currentCharacter == '\n' || currentCharacter == '\r')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
            if (i >= lastNormalCharPosition)
            {
                previousCharacter = currentCharacter;
            }
            else
            {
                // reset to avoid accidentally reusing marker delimiters as part of other markers
                previousCharacter = _runeError;
            }
        }

        // Add the remaining text
        Flush(-1);

        if (openRanges.Count > 0)
        {
            var openRange = openRanges[0];
            throw ReportError(
                fileName,
                openRange._locationInformation._sourceLine,
                openRange._locationInformation._sourceColumn,
                "Unterminated range.");
        }

        if (openMarker is not null)
        {
            throw ReportError(fileName, openMarker._sourceLine, openMarker._sourceColumn, "Unterminated marker.");
        }

        var outputString = output.ToString();
        // Set LS positions for markers
        var lineMap = ComputeLSPLineStarts(outputString);
        var converters = new TestConverters(NewConverters(PositionEncodingKind.UTF8, _ => lineMap));

        var emit = fileOptions.TryGetValue(_emitThisFileOption, out var emitOptionValue) && emitOptionValue == "true";

        var testFileInfo = new TestFileInfo
        {
            _fileName = fileName,
            Content = outputString,
            _emit = emit,
            _open = !(fileOptions.TryGetValue(_noOpenFileOption, out var noOpenOptionValue) && noOpenOptionValue == "true"),
        };

        rangeMarkers = [.. rangeMarkers.OrderBy(static r => r.Range.Pos()).ThenByDescending(static r => r.Range.End())];

        foreach (var marker in markers)
        {
            marker.LSPosition = converters.PositionToLineAndCharacter(testFileInfo, new TextPos(marker.Position));
        }

        foreach (var rangeMarker in rangeMarkers)
        {
            rangeMarker.LSRange = new Range
            {
                Start = converters.PositionToLineAndCharacter(testFileInfo, new TextPos(rangeMarker.Range.Pos())),
                End = converters.PositionToLineAndCharacter(testFileInfo, new TextPos(rangeMarker.Range.End())),
            };
        }

        return new TestFileWithMarkers
        {
            _file = testFileInfo,
            _markers = [.. markers],
            _ranges = [.. rangeMarkers],
        };
    }

    /// <summary>Go: <c>func getObjectMarker(fileName string, location *locationInformation, text string) (*Marker, error)</c>（test_parser.go:462-490）。</summary>
    internal static Marker GetObjectMarker(string fileName, LocationInformation location, string text)
    {
        // Attempt to parse the marker value as JSON
        object? value;
        try
        {
            value = TypeScript.Json.Json.Unmarshal<object>(Encoding.UTF8.GetBytes("{ " + text + " }"));
        }
        catch (TypeScript.Json.JsonException)
        {
            throw ReportError(fileName, location._sourceLine, location._sourceColumn, "Unable to parse marker text " + text);
        }

        if (value is not Dictionary<string, object?> markerValue || markerValue.Count == 0)
        {
            throw ReportError(fileName, location._sourceLine, location._sourceColumn, "Object markers can not be empty");
        }

        var marker = new Marker
        {
            _fileName = fileName,
            Position = location._position,
            Data = markerValue,
        };

        // Object markers can be anonymous
        if (markerValue.TryGetValue("name", out var nameValue) && nameValue is string name && name != "")
        {
            marker.Name = name;
        }

        return marker;
    }
}
