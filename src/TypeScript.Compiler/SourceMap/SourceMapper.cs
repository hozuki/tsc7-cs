// ported from: internal/sourcemap/source_mapper.go
//
// 与 Go 的差异（逐条）：
// - `Host.ReadFile(fileName) (string, bool)` → `bool TryReadFile(string fileName, out string contents)`（§3.2）。
// - `type SourceMappedPosition = MappedPosition` 是 Go 的类型别名 → C# 无别名，统一写 `MappedPosition`（docs/architecture.md §3.2）。
// - `MappedPosition` 的未导出字段 → `internal` 的 `_camelCase`（同包读取）；`isSourceMappedPosition` → `IsSourceMappedPosition`。
// - `GetSourcePosition`/`GetGeneratedPosition` 里的 `if d == nil { return nil }` 是 Go 的 nil 接收者安全分支：
//   C# 实例方法在 null 上不可分派，该分支是死代码直接删（§3.2）；调用点写 `d?.GetSourcePosition(loc)`，
//   且 `GetDocumentPositionMapper` 的返回类型是可空的。
// - `slices.SortFunc` → `List<T>.Sort`；`slices.BinarySearchFunc` → 本文件的 `BinarySearchFunc`（复刻 Go 的
//   "返回下界下标"语义；Go 的两处调用都忽略 found，只取下标）；`core.DeduplicateSorted`/`core.Map`/`core.Some` 直接用。
// - Go 在 `for i, list := range sourceMappings { … sourceMappings[i] = … }` 里**边遍历边改 map**（Go 允许）；
//   C# 的 Dictionary 不允许 → 先取 key 快照再改。
// - `tryParseRawSourceMap` 的 `json.Unmarshal` 失败 → C# 的 `UnmarshalNew<T>` 抛 JsonException，这里 catch 后返回 null
//   （与 Go 的 `err != nil → nil` 一致）。
// - `tryParseBase64Url` 里 Go 的 `url[:len("utf-8;")]` 在剩余串不足 6 字节时会 **panic**；C# 用定长 span 切片
//   （同样抛异常），逐字保留这个行为（不改 Go 的既有 bug）。
// - `base64.StdEncoding.DecodeString` → `Convert.TryFromBase64String`（失败返回 false，等价于 Go 的 err != nil）。
namespace TypeScript.SourceMap;

using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;
using static TypeScript.SourceMap.SourceMap;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

public interface Host
{
    bool UseCaseSensitiveFileNames();

    ECMALineInfo? GetECMALineInfo(string fileName);

    bool TryReadFile(string fileName, out string contents);
}

/// <summary>Similar to `Mapping`, but position-based.</summary>
public sealed class MappedPosition
{
    // Go 的包级常量 missingPosition = -1（只在本方法用到，放在这里更贴近使用点）
    private const int _missingPosition = -1;

    internal int _generatedPosition;
    internal int _sourcePosition;
    internal SourceIndex _sourceIndex;
    internal NameIndex _nameIndex;

    internal bool IsSourceMappedPosition() =>
        _sourceIndex != MissingSource && _sourcePosition != _missingPosition;
}

public sealed class DocumentPosition
{
    public string FileName = "";
    public int Pos;
}

/// <summary>Maps source positions to generated positions and vice versa.</summary>
public sealed class DocumentPositionMapper
{
    private readonly bool _useCaseSensitiveFileNames;
    private readonly string[] _sourceFileAbsolutePaths;
    private readonly Dictionary<string, SourceIndex> _sourceToSourceIndexMap;
    private readonly string _generatedAbsoluteFilePath;
    private readonly MappedPosition[] _generatedMappings;
    private readonly Dictionary<SourceIndex, List<MappedPosition>> _sourceMappings;

    internal DocumentPositionMapper(
        bool useCaseSensitiveFileNames,
        string[] sourceFileAbsolutePaths,
        Dictionary<string, SourceIndex> sourceToSourceIndexMap,
        string generatedAbsoluteFilePath,
        MappedPosition[] generatedMappings,
        Dictionary<SourceIndex, List<MappedPosition>> sourceMappings)
    {
        _useCaseSensitiveFileNames = useCaseSensitiveFileNames;
        _sourceFileAbsolutePaths = sourceFileAbsolutePaths;
        _sourceToSourceIndexMap = sourceToSourceIndexMap;
        _generatedAbsoluteFilePath = generatedAbsoluteFilePath;
        _generatedMappings = generatedMappings;
        _sourceMappings = sourceMappings;
    }

    public DocumentPosition? GetSourcePosition(DocumentPosition loc)
    {
        if (_generatedMappings.Length == 0)
        {
            return null;
        }

        var targetIndex = BinarySearchFunc(_generatedMappings, loc.Pos, (m, pos) => m._generatedPosition - pos);

        if (targetIndex < 0 || targetIndex >= _generatedMappings.Length)
        {
            return null;
        }

        var mapping = _generatedMappings[targetIndex];
        if (!mapping.IsSourceMappedPosition())
        {
            return null;
        }

        // Closest position
        return new DocumentPosition
        {
            FileName = _sourceFileAbsolutePaths[(int)mapping._sourceIndex],
            Pos = mapping._sourcePosition,
        };
    }

    public DocumentPosition? GetGeneratedPosition(DocumentPosition loc)
    {
        if (!_sourceToSourceIndexMap.TryGetValue(GetCanonicalFileName(loc.FileName, _useCaseSensitiveFileNames), out var sourceIndex))
        {
            return null;
        }

        if (sourceIndex < 0 || (int)sourceIndex >= _sourceMappings.Count)
        {
            return null;
        }

        if (!_sourceMappings.TryGetValue(sourceIndex, out var sourceMappings))
        {
            return null;
        }

        var targetIndex = BinarySearchFunc(sourceMappings, loc.Pos, (m, pos) => m._sourcePosition - pos);

        if (targetIndex < 0 || targetIndex >= sourceMappings.Count)
        {
            return null;
        }

        var mapping = sourceMappings[targetIndex];
        if (mapping._sourceIndex != sourceIndex)
        {
            return null;
        }

        // Closest position
        return new DocumentPosition
        {
            FileName = _generatedAbsoluteFilePath,
            Pos = mapping._generatedPosition,
        };
    }

    /// <summary>
    /// Go 的 <c>slices.BinarySearchFunc</c>：要求 x 已按 cmp 排序；返回**下界**下标（找不到时是插入点）。
    /// Go 侧两处调用都只用下标、忽略 found，所以这里不返回 bool。
    /// </summary>
    private static int BinarySearchFunc<T, TTarget>(IReadOnlyList<T> x, TTarget target, Func<T, TTarget, int> cmp)
    {
        var i = 0;
        var j = x.Count;
        while (i < j)
        {
            var h = (int)((uint)(i + j) >> 1);
            if (cmp(x[h], target) < 0)
            {
                i = h + 1;
            }
            else
            {
                j = h;
            }
        }

        return i;
    }

}

public static partial class SourceMap
{
    private static DocumentPositionMapper CreateDocumentPositionMapper(Host host, RawSourceMap sourceMap, string mapPath)
    {
        var mapDirectory = GetDirectoryPath(mapPath);
        string sourceRoot;
        if (sourceMap.SourceRoot.Length != 0)
        {
            sourceRoot = GetNormalizedAbsolutePath(sourceMap.SourceRoot, mapDirectory);
        }
        else
        {
            sourceRoot = mapDirectory;
        }

        var generatedAbsoluteFilePath = GetNormalizedAbsolutePath(sourceMap.File, mapDirectory);
        var sourceFileAbsolutePaths = Map(sourceMap.Sources, source => GetNormalizedAbsolutePath(source, sourceRoot));
        var useCaseSensitiveFileNames = host.UseCaseSensitiveFileNames();
        var sourceToSourceIndexMap = new Dictionary<string, SourceIndex>(sourceFileAbsolutePaths.Length);
        for (var i = 0; i < sourceFileAbsolutePaths.Length; i++)
        {
            sourceToSourceIndexMap[GetCanonicalFileName(sourceFileAbsolutePaths[i], useCaseSensitiveFileNames)] = new SourceIndex(i);
        }

        List<MappedPosition>? decodedMappings = [];
        var sourceMappings = new Dictionary<SourceIndex, List<MappedPosition>>();

        // getDecodedMappings()
        var decoder = DecodeMappings(sourceMap.Mappings);
        foreach (var mapping in decoder.Values())
        {
            // processMapping()
            var generatedPosition = -1;
            var lineInfo = host.GetECMALineInfo(generatedAbsoluteFilePath);
            if (lineInfo is not null)
            {
                generatedPosition = Scanner.Scanner.ComputePositionOfLineAndUTF16Character(
                    lineInfo._lineStarts,
                    mapping.GeneratedLine,
                    mapping.GeneratedCharacter,
                    lineInfo._text,
                    true /*allowEdits*/);
            }

            var sourcePosition = -1;
            if (mapping.IsSourceMapping())
            {
                var sourceLineInfo = host.GetECMALineInfo(sourceFileAbsolutePaths[(int)mapping.SourceIndex]);
                if (sourceLineInfo is not null)
                {
                    sourcePosition = Scanner.Scanner.ComputePositionOfLineAndUTF16Character(
                        sourceLineInfo._lineStarts,
                        mapping.SourceLine,
                        mapping.SourceCharacter,
                        sourceLineInfo._text,
                        true /*allowEdits*/);
                }
            }

            decodedMappings.Add(new MappedPosition
            {
                _generatedPosition = generatedPosition,
                _sourceIndex = mapping.SourceIndex,
                _sourcePosition = sourcePosition,
                _nameIndex = mapping.NameIndex,
            });
        }

        if (decoder.Error() is not null)
        {
            decodedMappings = null;
        }

        // getSourceMappings()
        foreach (var mapping in decodedMappings ?? [])
        {
            if (!mapping.IsSourceMappedPosition())
            {
                continue;
            }

            var sourceIndex = mapping._sourceIndex;
            if (!sourceMappings.TryGetValue(sourceIndex, out var list))
            {
                list = [];
                sourceMappings[sourceIndex] = list;
            }

            list.Add(new MappedPosition
            {
                _generatedPosition = mapping._generatedPosition,
                _sourceIndex = sourceIndex,
                _sourcePosition = mapping._sourcePosition,
                _nameIndex = mapping._nameIndex,
            });
        }

        // Go 在 range map 的同时写 map（合法）；C# 先取 key 快照。
        foreach (var sourceIndex in new List<SourceIndex>(sourceMappings.Keys))
        {
            var list = sourceMappings[sourceIndex];
            list.Sort((a, b) =>
            {
                Debug.Assert(a._sourceIndex == b._sourceIndex, "All source mappings should have the same source index");
                return a._sourcePosition - b._sourcePosition;
            });
            sourceMappings[sourceIndex] =
            [
                .. DeduplicateSorted(list.ToArray(), (a, b) =>
                    a._generatedPosition == b._generatedPosition &&
                    a._sourceIndex == b._sourceIndex &&
                    a._sourcePosition == b._sourcePosition),
            ];
        }

        // getGeneratedMappings()
        MappedPosition[] generatedMappings = decodedMappings is null ? [] : [.. decodedMappings];
        Array.Sort(generatedMappings, (a, b) => a._generatedPosition - b._generatedPosition);
        generatedMappings =
        [
            .. DeduplicateSorted(generatedMappings, (a, b) =>
                a._generatedPosition == b._generatedPosition &&
                a._sourceIndex == b._sourceIndex &&
                a._sourcePosition == b._sourcePosition),
        ];

        return new DocumentPositionMapper(
            useCaseSensitiveFileNames,
            sourceFileAbsolutePaths,
            sourceToSourceIndexMap,
            generatedAbsoluteFilePath,
            generatedMappings,
            sourceMappings);
    }

    private static DocumentPositionMapper? ConvertDocumentToSourceMapper(Host host, string contents, string mapFileName)
    {
        var sourceMap = TryParseRawSourceMap(contents);
        if (sourceMap is null || sourceMap.Sources is null || sourceMap.Sources.Length == 0 ||
            sourceMap.File.Length == 0 || sourceMap.Mappings.Length == 0)
        {
            // invalid map
            return null;
        }

        // Don't support source maps that contain inlined sources
        if (Some(sourceMap.SourcesContent, s => s is not null))
        {
            return null;
        }

        return CreateDocumentPositionMapper(host, sourceMap, mapFileName);
    }

    private static RawSourceMap? TryParseRawSourceMap(string contents)
    {
        RawSourceMap sourceMap;
        try
        {
            sourceMap = UnmarshalNew<RawSourceMap>(Encoding.UTF8.GetBytes(contents));
        }
        catch (JsonException)
        {
            return null;
        }

        if (sourceMap.Version != 3)
        {
            return null;
        }

        return sourceMap;
    }

    /// <summary>Equivalent to /^data:(?:application\/json;(?:charset=[uU][tT][fF]-8;)?base64,([A-Za-z0-9+/=]+)$)?/</summary>
    private static bool TryParseBase64Url(string url, out string parseableUrl, out bool isBase64Url)
    {
        if (!CutPrefix(url, "data:", out url))
        {
            parseableUrl = "";
            isBase64Url = false;
            return false;
        }

        if (!CutPrefix(url, "application/json;", out url))
        {
            parseableUrl = "";
            isBase64Url = true;
            return true;
        }

        if (CutPrefix(url, "charset=", out url))
        {
            // Go: url[:len("utf-8;")]（剩余不足 6 字节时 Go 会 panic，这里同样抛异常，见文件头）
            if (!url.AsSpan(0, "utf-8;".Length).Equals("utf-8;", StringComparison.OrdinalIgnoreCase))
            {
                parseableUrl = "";
                isBase64Url = true;
                return true;
            }

            url = url["utf-8;".Length..];
        }

        if (!CutPrefix(url, "base64,", out url))
        {
            parseableUrl = "";
            isBase64Url = true;
            return true;
        }

        foreach (var r in url)
        {
            if (!(IsASCIILetter(r) || IsDigit(r) || r == '+' || r == '/' || r == '='))
            {
                parseableUrl = "";
                isBase64Url = true;
                return true;
            }
        }

        parseableUrl = url;
        isBase64Url = true;
        return true;
    }

    private static bool CutPrefix(string s, string prefix, out string rest)
    {
        if (s.StartsWith(prefix, StringComparison.Ordinal))
        {
            rest = s[prefix.Length..];
            return true;
        }

        rest = s;
        return false;
    }

    private static bool TryDecodeBase64(string base64Object, out string decoded)
    {
        // Go: base64.StdEncoding.DecodeString
        var buffer = new byte[(base64Object.Length / 4 * 3) + 3];
        if (Convert.TryFromBase64String(base64Object, buffer, out var written))
        {
            decoded = Encoding.UTF8.GetString(buffer, 0, written);
            return true;
        }

        decoded = "";
        return false;
    }

    public static DocumentPositionMapper? GetDocumentPositionMapper(Host host, string generatedFileName)
    {
        var mapFileName = TryGetSourceMappingURL(host, generatedFileName);
        if (mapFileName.Length != 0)
        {
            var matched = TryParseBase64Url(mapFileName, out var base64Object, out _);
            if (matched)
            {
                if (base64Object.Length != 0 && TryDecodeBase64(base64Object, out var decoded))
                {
                    return ConvertDocumentToSourceMapper(host, decoded, generatedFileName);
                }

                // Not a data URL we can parse, skip it
                mapFileName = "";
            }
        }

        var possibleMapLocations = new List<string>();
        if (mapFileName.Length != 0)
        {
            possibleMapLocations.Add(mapFileName);
        }

        possibleMapLocations.Add(generatedFileName + ".map");
        foreach (var location in possibleMapLocations)
        {
            var normalized = GetNormalizedAbsolutePath(location, GetDirectoryPath(generatedFileName));
            if (host.TryReadFile(normalized, out var mapFileContents))
            {
                return ConvertDocumentToSourceMapper(host, mapFileContents, normalized);
            }
        }

        return null;
    }

    /// <summary>Go 的 <c>tryGetSourceMappingURL(host Host, fileName string) string</c>（与 util.go 的同名导出函数构成重载）。</summary>
    private static string TryGetSourceMappingURL(Host host, string fileName)
    {
        var lineInfo = host.GetECMALineInfo(fileName);
        return TryGetSourceMappingURL(lineInfo);
    }
}
