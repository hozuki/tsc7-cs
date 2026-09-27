// ported from: internal/sourcemap/decoder.go
//
// 与 Go 的差异（逐条）：
// - `type SourceIndex int` / `type NameIndex int` 是 Go 的整型别名类型 → C# 的只读结构体包装（与 Core 的 TextPos/
//   UTF16Offset 同款：双向隐式转换到 int，于是 Go 的 `int(x)`/`SourceIndex(n)` 两种写法都能编译）。
// - 包级常量 `MissingSource`/`MissingName`/`MissingLineOrColumn`/`MissingUTF16Column` → 静态类 `SourceMap` 上的成员
//   （§3.3 的"包级常量放静态类"）。注意 `SourceMap` 同时是命名空间名：包内可写裸名，包外要写全名
//   `TypeScript.SourceMap.SourceMap.MissingSource` 或 `using static`（docs/architecture.md §2 的同名陷阱）。
// - `MappingsDecoder.Error() error` 的 error 在 C# 里是异常对象：错误文本（Go 的 `errors.New(err)`）落成
//   `FormatException` 实例（解码错误是输入格式问题），只在 `Error()` 里被检查、不抛出。
// - `Values() iter.Seq[*Mapping]` → `IEnumerable<Mapping>`（C# 的迭代器等价物，惰性）。
// - `Next() (*Mapping, bool)` 是 Go 的 "(值, 结束)" 形状：C# 落成 `bool TryNext(out Mapping? value)`
//   （docs/architecture.md §3.2 的 `(T,bool)` → `TryX`；返回 false 表示迭代结束，等价于 Go 的 done）。
// - `mappingArena core.Arena[Mapping]` 去掉（docs/architecture.md §3.2：Arena 只对 struct 有意义），每次 `new Mapping()`。
// - `base64VLQFormatDecode` 是未导出的包级函数 → `MappingsDecoder` 上的 `private static`（Go 的参数是 byte，
//   C# 是 string 下标取出的 char；两者对 ASCII 全等价）。
namespace TypeScript.SourceMap;

using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.SourceMap.SourceMap;

public sealed class Mapping : IEquatable<Mapping>
{
    public int GeneratedLine;
    public UTF16Offset GeneratedCharacter;
    public SourceIndex SourceIndex;
    public int SourceLine;
    public UTF16Offset SourceCharacter;
    public NameIndex NameIndex;

    public bool Equals(Mapping? other)
    {
        return other is not null && (ReferenceEquals(this, other) ||
            GeneratedLine == other.GeneratedLine &&
            GeneratedCharacter == other.GeneratedCharacter &&
            SourceIndex == other.SourceIndex &&
            SourceLine == other.SourceLine &&
            SourceCharacter == other.SourceCharacter &&
            NameIndex == other.NameIndex);
    }

    public override bool Equals(object? obj) => obj is Mapping other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GeneratedLine, (int)GeneratedCharacter, (int)SourceIndex, SourceLine, (int)SourceCharacter, (int)NameIndex);

    public bool IsSourceMapping() =>
        SourceIndex != MissingSource &&
        SourceLine != MissingLineOrColumn &&
        SourceCharacter != MissingUTF16Column;
}

public sealed class MappingsDecoder
{
    private readonly string _mappings;
    private bool _done;
    private int _pos;
    private int _generatedLine;
    private UTF16Offset _generatedCharacter;
    private SourceIndex _sourceIndex;
    private int _sourceLine;
    private UTF16Offset _sourceCharacter;
    private NameIndex _nameIndex;
    private FormatException? _error;

    internal MappingsDecoder(string mappings)
    {
        _mappings = mappings;
    }

    public string MappingsString() => _mappings;

    public int Pos() => _pos;

    public FormatException? Error() => _error;

    public Mapping State() => CaptureMapping(true /*hasSource*/, true /*hasName*/);

    public IEnumerable<Mapping> Values()
    {
        while (TryNext(out var value))
        {
            yield return value!;
        }
    }

    public bool TryNext(out Mapping? value)
    {
        while (!_done && _pos < _mappings.Length)
        {
            var ch = _mappings[_pos];
            if (ch == ';')
            {
                // new line
                _generatedLine++;
                _generatedCharacter = 0;
                _pos++;
                continue;
            }

            if (ch == ',')
            {
                // Next entry is on same line - no action needed
                _pos++;
                continue;
            }

            var hasSource = false;
            var hasName = false;
            _generatedCharacter += Base64VLQFormatDecode();
            if (HasReportedError())
            {
                return StopIterating(out value);
            }

            if (_generatedCharacter < 0)
            {
                return SetErrorAndStopIterating("Invalid generatedCharacter found", out value);
            }

            if (!IsSourceMappingSegmentEnd())
            {
                hasSource = true;

                _sourceIndex += Base64VLQFormatDecode();
                if (HasReportedError())
                {
                    return StopIterating(out value);
                }

                if (_sourceIndex < 0)
                {
                    return SetErrorAndStopIterating("Invalid sourceIndex found", out value);
                }

                if (IsSourceMappingSegmentEnd())
                {
                    return SetErrorAndStopIterating("Unsupported Format: No entries after sourceIndex", out value);
                }

                _sourceLine += Base64VLQFormatDecode();
                if (HasReportedError())
                {
                    return StopIterating(out value);
                }

                if (_sourceLine < 0)
                {
                    return SetErrorAndStopIterating("Invalid sourceLine found", out value);
                }

                if (IsSourceMappingSegmentEnd())
                {
                    return SetErrorAndStopIterating("Unsupported Format: No entries after sourceLine", out value);
                }

                _sourceCharacter += Base64VLQFormatDecode();
                if (HasReportedError())
                {
                    return StopIterating(out value);
                }

                if (_sourceCharacter < 0)
                {
                    return SetErrorAndStopIterating("Invalid sourceCharacter found", out value);
                }

                if (!IsSourceMappingSegmentEnd())
                {
                    hasName = true;
                    _nameIndex += Base64VLQFormatDecode();
                    if (HasReportedError())
                    {
                        return StopIterating(out value);
                    }

                    if (_nameIndex < 0)
                    {
                        return SetErrorAndStopIterating("Invalid nameIndex found", out value);
                    }

                    if (!IsSourceMappingSegmentEnd())
                    {
                        return SetErrorAndStopIterating("Unsupported Error Format: Entries after nameIndex", out value);
                    }
                }
            }

            value = CaptureMapping(hasSource, hasName);
            return true;
        }

        return StopIterating(out value);
    }

    private Mapping CaptureMapping(bool hasSource, bool hasName)
    {
        var mapping = new Mapping();
        mapping.GeneratedLine = _generatedLine;
        mapping.GeneratedCharacter = _generatedCharacter;
        mapping.SourceIndex = IfElse(hasSource, _sourceIndex, MissingSource);
        mapping.SourceLine = IfElse(hasSource, _sourceLine, MissingLineOrColumn);
        mapping.SourceCharacter = IfElse(hasSource, _sourceCharacter, MissingUTF16Column);
        mapping.NameIndex = IfElse(hasName, _nameIndex, MissingName);
        return mapping;
    }

    private bool StopIterating(out Mapping? value)
    {
        _done = true;
        value = null;
        return false;
    }

    private void SetError(string err) => _error = new FormatException(err);

    private bool SetErrorAndStopIterating(string err, out Mapping? value)
    {
        SetError(err);
        return StopIterating(out value);
    }

    private bool HasReportedError() => _error is not null;

    private bool IsSourceMappingSegmentEnd() =>
        _pos == _mappings.Length || _mappings[_pos] == ',' || _mappings[_pos] == ';';

    private int Base64VLQFormatDecode()
    {
        var moreDigits = true;
        var shiftCount = 0;
        var value = 0;
        for (; moreDigits; _pos++)
        {
            if (_pos >= _mappings.Length)
            {
                SetError("Error in decoding base64VLQFormatDecode, past the mapping string");
                return -1;
            }

            // 6 digit number
            var currentByte = Base64FormatDecode(_mappings[_pos]);
            if (currentByte == -1)
            {
                SetError("Invalid character in VLQ");
                return -1;
            }

            // If msb is set, we still have more bits to continue
            moreDigits = (currentByte & 32) != 0;

            // least significant 5 bits are the next msbs in the final value.
            value |= (currentByte & 31) << shiftCount;
            shiftCount += 5;
        }

        // Least significant bit if 1 represents negative and rest of the msb is actual absolute value
        if ((value & 1) == 0)
        {
            // + number
            value >>= 1;
        }
        else
        {
            // - number
            value >>= 1;
            value = -value;
        }

        return value;
    }

    private static int Base64FormatDecode(char ch)
    {
        switch (ch)
        {
            case >= 'A' and <= 'Z':
                return ch - 'A';
            case >= 'a' and <= 'z':
                return ch - 'a' + 26;
            case >= '0' and <= '9':
                return ch - '0' + 52;
            case '+':
                return 62;
            case '/':
                return 63;
            default:
                return -1;
        }
    }
}

public static partial class SourceMap
{
    // Go: type SourceIndex int / type NameIndex int（见文件头）
    public static readonly SourceIndex MissingSource = -1;
    public static readonly NameIndex MissingName = -1;
    public const int MissingLineOrColumn = -1;
    public static readonly UTF16Offset MissingUTF16Column = -1;

    public static MappingsDecoder DecodeMappings(string mappings) => new(mappings);
}
