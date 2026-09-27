// ported from: internal/sourcemap/generator.go
//
// 与 Go 的差异（逐条）：
// - `type SourceIndex int` / `type NameIndex int` → 只读结构体包装 + 双向隐式转换（见 Decoder.cs 文件头）。
// - 未导出字段 → `private`/`internal` 的 `_camelCase`；`rawSources`/`sources`/`names`/`sourcesContent` 在 Go 里是切片
//   （append 语义）→ C# 用 `List<T>`；`Sources()` 因此返回**快照数组**（Go 返回的是切片别名，调用点只读，差异可忽略）。
// - `strings.Builder` → `StringBuilder`；`appendMappingCharCode(rune)` 的参数是 base64 字符（ASCII）→ `(char)`。
// - Go 的 `(T, error)` → C# 抛异常：`SetSourceContent`/`AddGeneratedMapping`/`AddSourceMapping`/`AddNamedSourceMapping`
//   的校验失败抛 `ArgumentException`（消息与 Go 的错误文本逐字一致，测试按消息断言）。
// - Go 的 stringer `func (g *Generator) String() string` → `ToString()` 覆写（§3.2），另在静态类 `SourceMap` 上给
//   `String(this Generator)` 扩展方法，保住 Go 调用点写法（`sourceMapGenerator.String()`，compiler/emitter.go:361）。
// - `bytes()` 未导出 → `Bytes()`（private）；`json.Marshal` → C# 的 `Marshal`（byte[]）。
// - 结构体 JSON（Go 靠 tag）→ 显式 `IJsonMarshaler`（docs/json.md §2）。字段顺序 = Go 的声明顺序；
//   `sourcesContent` 带 omitzero（nil 不写，但元素可以是 null）。
namespace TypeScript.SourceMap;

using System.Globalization;
using System.Text;
using TypeScript.Core;
using JsonDecoder = TypeScript.Json.Decoder;
using JsonEncoder = TypeScript.Json.Encoder;
using TypeScript.Json;
using TypeScript.TSPath;
using static TypeScript.Json.Json;
using static TypeScript.TSPath.TSPath;

/// <summary>Go 的 <c>type SourceIndex int</c>。</summary>
public readonly struct SourceIndex : IEquatable<SourceIndex>
{
    public readonly int Value;

    public SourceIndex(int value)
    {
        Value = value;
    }

    public static implicit operator int(SourceIndex index) => index.Value;

    public static implicit operator SourceIndex(int value) => new(value);

    public bool Equals(SourceIndex other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SourceIndex other && Equals(other);

    public override int GetHashCode() => Value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Go 的 <c>type NameIndex int</c>。</summary>
public readonly struct NameIndex : IEquatable<NameIndex>
{
    public readonly int Value;

    public NameIndex(int value)
    {
        Value = value;
    }

    public static implicit operator int(NameIndex index) => index.Value;

    public static implicit operator NameIndex(int value) => new(value);

    public bool Equals(NameIndex other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is NameIndex other && Equals(other);

    public override int GetHashCode() => Value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed class RawSourceMap : IJsonMarshaler, IJsonUnmarshaler
{
    public int Version;
    public string File = "";
    public string SourceRoot = "";
    public string[]? Sources;
    public string[]? Names;
    public string Mappings = "";
    public string?[]? SourcesContent;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        WriteName(encoder, "version");
        MarshalEncode(encoder, Version);
        WriteName(encoder, "file");
        MarshalEncode(encoder, File);
        WriteName(encoder, "sourceRoot");
        MarshalEncode(encoder, SourceRoot);
        WriteName(encoder, "sources");
        MarshalEncode(encoder, Sources);
        WriteName(encoder, "names");
        MarshalEncode(encoder, Names);
        WriteName(encoder, "mappings");
        MarshalEncode(encoder, Mappings);
        if (SourcesContent is not null)
        {
            WriteName(encoder, "sourcesContent");
            MarshalEncode(encoder, SourcesContent);
        }

        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("expected object");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadToken().StringValue;
            switch (name)
            {
                case "version":
                    Version = decoder.Decode<int>();
                    break;
                case "file":
                    File = decoder.Decode<string>() ?? "";
                    break;
                case "sourceRoot":
                    SourceRoot = decoder.Decode<string>() ?? "";
                    break;
                case "sources":
                    Sources = decoder.DecodeNew<List<string>>().ToArray();
                    break;
                case "names":
                    Names = decoder.DecodeNew<List<string>>().ToArray();
                    break;
                case "mappings":
                    Mappings = decoder.Decode<string>() ?? "";
                    break;
                case "sourcesContent":
                    SourcesContent = decoder.DecodeNew<List<string?>>()?.ToArray();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken(); // }
    }

    private static void WriteName(JsonEncoder encoder, string name) => encoder.WriteToken(JsonToken.String(name));
}

public sealed class Generator
{
    private readonly ComparePathsOptions _pathOptions;
    private readonly string _file;
    private readonly string _sourceRoot;
    private readonly string _sourcesDirectoryPath;
    private readonly List<string> _rawSources = [];
    private readonly List<string> _sources = [];
    private readonly Dictionary<string, SourceIndex> _sourceToSourceIndexMap = [];
    private readonly List<string?> _sourcesContent = [];
    private readonly List<string> _names = [];
    private readonly Dictionary<string, NameIndex> _nameToNameIndexMap = [];
    private readonly StringBuilder _mappings = new();
    private int _lastGeneratedLine;
    private UTF16Offset _lastGeneratedCharacter;
    private SourceIndex _lastSourceIndex;
    private int _lastSourceLine;
    private UTF16Offset _lastSourceCharacter;
    private NameIndex _lastNameIndex;
    private bool _hasLast;
    private int _pendingGeneratedLine;
    private UTF16Offset _pendingGeneratedCharacter;
    private SourceIndex _pendingSourceIndex;
    private int _pendingSourceLine;
    private UTF16Offset _pendingSourceCharacter;
    private NameIndex _pendingNameIndex;
    private bool _hasPending;
    private bool _hasPendingSource;
    private bool _hasPendingName;

    private const int _notSet = -1;
    private static readonly UTF16Offset _notSetUTF16 = -1;
    private static readonly SourceIndex _sourceIndexNotSet = -1;
    private static readonly NameIndex _nameIndexNotSet = -1;

    internal Generator(string file, string sourceRoot, string sourcesDirectoryPath, ComparePathsOptions options)
    {
        _file = file;
        _sourceRoot = sourceRoot;
        _sourcesDirectoryPath = sourcesDirectoryPath;
        _pathOptions = options;
    }

    public string[] Sources() => [.. _rawSources];

    /// <summary>Adds a source to the source map.</summary>
    public SourceIndex AddSource(string fileName)
    {
        var source = GetRelativePathToDirectoryOrUrl(
            _sourcesDirectoryPath,
            fileName,
            true, /*isAbsolutePathAnUrl*/
            _pathOptions);

        if (!_sourceToSourceIndexMap.TryGetValue(source, out var sourceIndex))
        {
            sourceIndex = new SourceIndex(_sources.Count);
            _sources.Add(source);
            _rawSources.Add(fileName);
            _sourceToSourceIndexMap[source] = sourceIndex;
        }

        return sourceIndex;
    }

    /// <summary>Sets the content for a source.</summary>
    public void SetSourceContent(SourceIndex sourceIndex, string content)
    {
        if (sourceIndex < 0 || (int)sourceIndex >= _sources.Count)
        {
            throw new ArgumentException("sourceIndex is out of range");
        }

        while (_sourcesContent.Count <= (int)sourceIndex)
        {
            _sourcesContent.Add(null);
        }

        _sourcesContent[(int)sourceIndex] = content;
    }

    /// <summary>Declares a name in the source map, returning the index of the name.</summary>
    public NameIndex AddName(string name)
    {
        if (!_nameToNameIndexMap.TryGetValue(name, out var nameIndex))
        {
            nameIndex = new NameIndex(_names.Count);
            _names.Add(name);
            _nameToNameIndexMap[name] = nameIndex;
        }

        return nameIndex;
    }

    private bool IsNewGeneratedPosition(int generatedLine, UTF16Offset generatedCharacter) =>
        !_hasPending ||
        _pendingGeneratedLine != generatedLine ||
        _pendingGeneratedCharacter != generatedCharacter;

    private bool IsBacktrackingSourcePosition(SourceIndex sourceIndex, int sourceLine, UTF16Offset sourceCharacter) =>
        sourceIndex != _sourceIndexNotSet &&
        sourceLine != _notSet &&
        sourceCharacter != _notSetUTF16 &&
        _pendingSourceIndex == sourceIndex &&
        (_pendingSourceLine > sourceLine ||
            (_pendingSourceLine == sourceLine && _pendingSourceCharacter > sourceCharacter));

    private bool ShouldCommitMapping() =>
        _hasPending && (!_hasLast ||
            _lastGeneratedLine != _pendingGeneratedLine ||
            _lastGeneratedCharacter != _pendingGeneratedCharacter ||
            _lastSourceIndex != _pendingSourceIndex ||
            _lastSourceLine != _pendingSourceLine ||
            _lastSourceCharacter != _pendingSourceCharacter ||
            _lastNameIndex != _pendingNameIndex);

    private void AppendMappingCharCode(char charCode) => _mappings.Append(charCode);

    private void AppendBase64VLQ(int inValue)
    {
        // Add a new least significant bit that has the sign of the value.
        // if negative number the least significant bit that gets added to the number has value 1
        // else least significant bit value that gets added is 0
        // eg. -1 changes to binary : 01 [1] => 3
        //     +1 changes to binary : 01 [0] => 2
        if (inValue < 0)
        {
            inValue = ((-inValue) << 1) + 1;
        }
        else
        {
            inValue <<= 1;
        }

        // Encode 5 bits at a time starting from least significant bits
        while (true)
        {
            var currentDigit = inValue & 31; // 11111
            inValue >>= 5;
            if (inValue > 0)
            {
                // There are still more digits to decode, set the msb (6th bit)
                currentDigit |= 32;
            }

            AppendMappingCharCode(Base64FormatEncode(currentDigit));
            if (inValue <= 0)
            {
                break;
            }
        }
    }

    private void CommitPendingMapping()
    {
        if (!ShouldCommitMapping())
        {
            return;
        }

        // Line/Comma delimiters
        if (_lastGeneratedLine < _pendingGeneratedLine)
        {
            // Emit line delimiters
            while (true)
            {
                AppendMappingCharCode(';');
                _lastGeneratedLine++;
                if (_lastGeneratedLine >= _pendingGeneratedLine)
                {
                    break;
                }
            }

            // Only need to set this once
            _lastGeneratedCharacter = 0;
        }
        else
        {
            if (_lastGeneratedLine != _pendingGeneratedLine)
            {
                // panic rather than error as an invariant has been violated
                throw new DebugFailureException("generatedLine cannot backtrack");
            }

            // Emit comma to separate the entry
            if (_hasLast)
            {
                AppendMappingCharCode(',');
            }
        }

        // 1. Relative generated character
        AppendBase64VLQ((int)_pendingGeneratedCharacter - (int)_lastGeneratedCharacter);
        _lastGeneratedCharacter = _pendingGeneratedCharacter;

        if (_hasPendingSource)
        {
            // 2. Relative sourceIndex
            AppendBase64VLQ((int)_pendingSourceIndex - (int)_lastSourceIndex);
            _lastSourceIndex = _pendingSourceIndex;

            // 3. Relative source line
            AppendBase64VLQ(_pendingSourceLine - _lastSourceLine);
            _lastSourceLine = _pendingSourceLine;

            // 4. Relative source character
            AppendBase64VLQ((int)_pendingSourceCharacter - (int)_lastSourceCharacter);
            _lastSourceCharacter = _pendingSourceCharacter;

            if (_hasPendingName)
            {
                // 5. Relative nameIndex
                AppendBase64VLQ((int)_pendingNameIndex - (int)_lastNameIndex);
                _lastNameIndex = _pendingNameIndex;
            }
        }

        _hasLast = true;
    }

    private void AddMapping(int generatedLine, UTF16Offset generatedCharacter, SourceIndex sourceIndex, int sourceLine, UTF16Offset sourceCharacter, NameIndex nameIndex)
    {
        if (IsNewGeneratedPosition(generatedLine, generatedCharacter) ||
            IsBacktrackingSourcePosition(sourceIndex, sourceLine, sourceCharacter))
        {
            CommitPendingMapping();
            _pendingGeneratedLine = generatedLine;
            _pendingGeneratedCharacter = generatedCharacter;
            _hasPendingSource = false;
            _hasPendingName = false;
            _hasPending = true;
        }

        if (sourceIndex != _sourceIndexNotSet && sourceLine != _notSet && sourceCharacter != _notSetUTF16)
        {
            _pendingSourceIndex = sourceIndex;
            _pendingSourceLine = sourceLine;
            _pendingSourceCharacter = sourceCharacter;
            _hasPendingSource = true;
            if (nameIndex != _nameIndexNotSet)
            {
                _pendingNameIndex = nameIndex;
                _hasPendingName = true;
            }
        }
    }

    /// <summary>Adds a mapping without source information.</summary>
    public void AddGeneratedMapping(int generatedLine, UTF16Offset generatedCharacter)
    {
        if (generatedLine < _pendingGeneratedLine)
        {
            throw new ArgumentException("generatedLine cannot backtrack");
        }

        if (generatedCharacter < 0)
        {
            throw new ArgumentException("generatedCharacter cannot be negative");
        }

        AddMapping(generatedLine, generatedCharacter, _sourceIndexNotSet, _notSet /*sourceLine*/, _notSetUTF16 /*sourceCharacter*/, _nameIndexNotSet);
        _hasPendingSource = false;
        _hasPendingName = false;
    }

    /// <summary>Adds a mapping with source information.</summary>
    public void AddSourceMapping(int generatedLine, UTF16Offset generatedCharacter, SourceIndex sourceIndex, int sourceLine, UTF16Offset sourceCharacter)
    {
        if (generatedLine < _pendingGeneratedLine)
        {
            throw new ArgumentException("generatedLine cannot backtrack");
        }

        if (generatedCharacter < 0)
        {
            throw new ArgumentException("generatedCharacter cannot be negative");
        }

        if (sourceIndex < 0 || (int)sourceIndex >= _sources.Count)
        {
            throw new ArgumentException("sourceIndex is out of range");
        }

        if (sourceLine < 0)
        {
            throw new ArgumentException("sourceLine cannot be negative");
        }

        if (sourceCharacter < 0)
        {
            throw new ArgumentException("sourceCharacter cannot be negative");
        }

        if (_hasPending && !IsNewGeneratedPosition(generatedLine, generatedCharacter) && !_hasPendingSource)
        {
            return;
        }

        AddMapping(generatedLine, generatedCharacter, sourceIndex, sourceLine, sourceCharacter, _nameIndexNotSet);
    }

    /// <summary>Adds a mapping with source and name information.</summary>
    public void AddNamedSourceMapping(int generatedLine, UTF16Offset generatedCharacter, SourceIndex sourceIndex, int sourceLine, UTF16Offset sourceCharacter, NameIndex nameIndex)
    {
        if (generatedLine < _pendingGeneratedLine)
        {
            throw new ArgumentException("generatedLine cannot backtrack");
        }

        if (generatedCharacter < 0)
        {
            throw new ArgumentException("generatedCharacter cannot be negative");
        }

        if (sourceIndex < 0 || (int)sourceIndex >= _sources.Count)
        {
            throw new ArgumentException("sourceIndex is out of range");
        }

        if (sourceLine < 0)
        {
            throw new ArgumentException("sourceLine cannot be negative");
        }

        if (sourceCharacter < 0)
        {
            throw new ArgumentException("sourceCharacter cannot be negative");
        }

        if (nameIndex < 0 || (int)nameIndex >= _names.Count)
        {
            throw new ArgumentException("nameIndex is out of range");
        }

        if (_hasPending && !IsNewGeneratedPosition(generatedLine, generatedCharacter) && !_hasPendingSource)
        {
            return;
        }

        AddMapping(generatedLine, generatedCharacter, sourceIndex, sourceLine, sourceCharacter, nameIndex);
    }

    /// <summary>Gets the source map as a `RawSourceMap` object.</summary>
    public RawSourceMap RawSourceMap()
    {
        CommitPendingMapping();
        return new RawSourceMap
        {
            Version = 3,
            File = _file,
            SourceRoot = _sourceRoot,
            Sources = [.. _sources],
            Names = [.. _names],
            Mappings = _mappings.ToString(),
            SourcesContent = _sourcesContent.Count == 0 ? null : [.. _sourcesContent],
        };
    }

    private byte[] Bytes()
    {
        // Go: buf, err := json.Marshal(gen.RawSourceMap()); if err != nil { panic }
        return Marshal(RawSourceMap());
    }

    /// <summary>Gets the string representation of the source map（Go 的 stringer <c>String()</c>，见文件头）。</summary>
    public override string ToString() => Encoding.UTF8.GetString(Bytes());

    public string Base64DataURL()
    {
        const string prefix = "data:application/json;base64,";
        // Go: base64.StdEncoding（C# 的 Convert.ToBase64String 输出相同的标准 base64 字符集，无换行）
        return prefix + Convert.ToBase64String(Bytes());
    }

    private static char Base64FormatEncode(int value)
    {
        switch (value)
        {
            case >= 0 and < 26:
                return (char)('A' + value);
            case >= 26 and < 52:
                return (char)('a' + value - 26);
            case >= 52 and < 62:
                return (char)('0' + value - 52);
            case 62:
                return '+';
            case 63:
                return '/';
            default:
                throw new DebugFailureException("not a base64 value");
        }
    }
}

public static partial class SourceMap
{
    public static Generator NewGenerator(string file, string sourceRoot, string sourcesDirectoryPath, ComparePathsOptions options) =>
        new(file, sourceRoot, sourcesDirectoryPath, options);

    /// <summary>Go 的 stringer 调用点（<c>gen.String()</c>）在 C# 里的等价写法。</summary>
    public static string String(this Generator generator) => generator.ToString();
}
