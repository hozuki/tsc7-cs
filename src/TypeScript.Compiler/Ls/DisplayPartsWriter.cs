// ported from: internal/ls/displaypartswriter.go:1-217
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 未导出类型 `displayPartsWriter` → `internal sealed class DisplayPartsWriter`（命名空间级；Go 未导出类型在 C# 无
//    "internal 前缀" 之外的对应物）。Go 的包级构造函数 `newDisplayPartsWriter(vsCapability bool)` → **普通构造函数**
//    `new DisplayPartsWriter(bool vsCapability)`（本仓 `SymbolAndEntries` 用 `New*` 工厂是另一批先例；这里 G5/G6 已
//    按 ctor 对齐，两种都只是风格，构造语义完全一致）。
//    Go 未导出的**方法** → `internal` + PascalCase（不加 `_`，IDE1006 在 TreatWarningsAsErrors 下是错误）。
//
// 2) `strings.Builder` → `System.Text.StringBuilder`（`builder.WriteString` → `Append`，`builder.Len()` → `Length`，
//    `builder.Reset()` → `Clear()`）。
//
// 3) `runs []*lsproto.VSClassifiedTextRun` → `List<VSClassifiedTextRun> _runs`（Go 用 append；`Clear()` 里 Go 置 nil，
//    这里 `Clear()`；`GetRuns()` 返回**非空数组**（无内容时为 `[]`），调用点都只做"长度 > 0 / 拼接"，与 Go 的 nil 切片
//    在这些用法下等价（Go 遍历 nil 切片不迭代，C# 遍历空数组同理）。
//
// 4) `var _ printer.EmitTextWriter = &displayPartsWriter{}` 是 Go 的编译期接口断言；C# 的类型系统在声明
//    `: EmitTextWriter` 时已静态校验，无需对应语句。
//
// 5) `HasTrailingWhitespace`：Go 用 `utf8.DecodeLastRuneInString(w.lastWritten)`（空串/非法编码 → RuneError → false）。
//    C# 里 `_lastWritten` 已是 UTF-16 单元序列，取末位 char 即可；沿用**同一语义的既有先例** `Printer/TextWriter.cs:95-107`
//    （在那里就 `_lastWritten[^1]`；char → int 隐式转换喂给 `IsWhiteSpaceLike`）。
//
// 6) `w.AddRun` 的 `string(classification)` → `classification.Value`（LsProto 的 `ClassificationTypeName` 是
//    "命名 string" 的 C# 对应物：`readonly struct` + 隐式 string 入向转换 + `Value` 属性）。常量从 Go 的
//    `lsproto.ClassificationTypeNameText` 改成 C# 的 `ClassificationTypeName.Text`（见 LsProto 生成物文件头约定）。
//
// 7) Go 的 `classificationForSymbol` / `isFirstDeclarationOfSymbolParameter` 是包级函数（不在 `displayPartsWriter` 上），
//    且 `WriteSymbol` 要用 ⇒ 落成 `DisplayPartsWriter` 的 `internal static` 成员（G5/G6 已按此对齐）。
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Printer;
using static TypeScript.StringUtil.StringUtil;
// 同名类型消歧（`using System` / `using TypeScript.LsProto` 带来的同名类型；先例 Ls/Utilities.cs）：
using Kind = TypeScript.Ast.Kind;

namespace TypeScript.Ls;

/// <summary>
/// displayPartsWriter implements EmitTextWriter and captures classified text runs
/// for VS colorized labels, while also building a plain string.
/// When vsCapability is false, only the plain string is built; runs are skipped.
/// </summary>
internal sealed class DisplayPartsWriter : EmitTextWriter
{
    private readonly StringBuilder _builder = new();
    private List<VSClassifiedTextRun> _runs = [];
    private readonly bool _vsCapability;
    private string _lastWritten = "";

    internal DisplayPartsWriter(bool vsCapability)
    {
        _vsCapability = vsCapability;
    }

    internal void AddRun(ClassificationTypeName classification, string text)
    {
        if (text == "")
        {
            return;
        }
        if (_vsCapability)
        {
            _runs.Add(new VSClassifiedTextRun
            {
                ClassificationTypeName = classification.Value,
                Text = text,
            });
        }
        _lastWritten = text;
        _builder.Append(text);
    }

    /// <summary>WriteClassified writes text with an explicit classification type.</summary>
    internal void WriteClassified(string text, ClassificationTypeName classification)
    {
        AddRun(classification, text);
    }

    /// <summary>WriteFrom copies the accumulated content from another displayPartsWriter.</summary>
    internal void WriteFrom(DisplayPartsWriter other)
    {
        _builder.Append(other.String());
        if (_vsCapability)
        {
            _runs.AddRange(other.GetRuns());
        }
        if (other._lastWritten != "")
        {
            _lastWritten = other._lastWritten;
        }
    }

    internal VSClassifiedTextRun[] GetRuns()
    {
        return _runs.ToArray();
    }

    public string String()
    {
        return _builder.ToString();
    }

    public void Clear()
    {
        _lastWritten = "";
        _builder.Clear();
        _runs = [];
    }

    public void DecreaseIndent()
    {
    }

    public UTF16Offset GetColumn()
    {
        return 0;
    }

    public int GetIndent()
    {
        return 0;
    }

    public int GetLine()
    {
        return 0;
    }

    public int GetTextPos()
    {
        return _builder.Length;
    }

    public bool HasTrailingComment()
    {
        return false;
    }

    public bool HasTrailingWhitespace()
    {
        if (_builder.Length == 0)
        {
            return false;
        }
        // Go: utf8.DecodeLastRuneInString(w.lastWritten)；空串/无效编码得到 RuneError → false。
        if (_lastWritten.Length == 0)
        {
            return false;
        }
        return IsWhiteSpaceLike(_lastWritten[^1]);
    }

    public void IncreaseIndent()
    {
    }

    public bool IsAtStartOfLine()
    {
        return false;
    }

    public void RawWrite(string s)
    {
        AddRun(ClassificationTypeName.Text, s);
    }

    public void Write(string s)
    {
        AddRun(ClassificationTypeName.Text, s);
    }

    public void WriteComment(string text)
    {
        // Strada's writeComment uses unknownWrite → SymbolDisplayPartKind.text → "text"
        AddRun(ClassificationTypeName.Text, text);
    }

    public void WriteKeyword(string text)
    {
        AddRun(ClassificationTypeName.Keyword, text);
    }

    public void WriteLine()
    {
        AddRun(ClassificationTypeName.WhiteSpace, " ");
    }

    public void WriteLineForce(bool force)
    {
        AddRun(ClassificationTypeName.WhiteSpace, " ");
    }

    public void WriteLiteral(string s)
    {
        // Strada's writeLiteral → SymbolDisplayPartKind.stringLiteral → "string"
        AddRun(ClassificationTypeName.String, s);
    }

    public void WriteOperator(string text)
    {
        AddRun(ClassificationTypeName.Operator, text);
    }

    public void WriteParameter(string text)
    {
        AddRun(ClassificationTypeName.ParameterName, text);
    }

    public void WriteProperty(string text)
    {
        AddRun(ClassificationTypeName.PropertyName, text);
    }

    public void WritePunctuation(string text)
    {
        AddRun(ClassificationTypeName.Punctuation, text);
    }

    public void WriteSpace(string text)
    {
        AddRun(ClassificationTypeName.WhiteSpace, text);
    }

    public void WriteStringLiteral(string text)
    {
        AddRun(ClassificationTypeName.String, text);
    }

    public void WriteSymbol(string text, Symbol? symbol)
    {
        var classification = ClassificationForSymbol(symbol);
        AddRun(classification, text);
    }

    public void WriteTrailingSemicolon(string text)
    {
        AddRun(ClassificationTypeName.Punctuation, text);
    }

    /// <summary>
    /// classificationForSymbol determines the Roslyn classification type name based on a symbol's flags.
    /// Matches the Strada translation chain: displayPartKind() → GetClassificationName().
    /// </summary>
    internal static ClassificationTypeName ClassificationForSymbol(Symbol? symbol)
    {
        if (symbol is null)
        {
            return ClassificationTypeName.Text;
        }
        var flags = symbol.Flags;
        if ((flags & SymbolFlags.Variable) != 0)
        {
            if (IsFirstDeclarationOfSymbolParameter(symbol))
            {
                return ClassificationTypeName.ParameterName;
            }
            return ClassificationTypeName.LocalName;
        }
        if ((flags & SymbolFlags.Property) != 0)
        {
            return ClassificationTypeName.PropertyName;
        }
        if ((flags & SymbolFlags.GetAccessor) != 0)
        {
            return ClassificationTypeName.PropertyName;
        }
        if ((flags & SymbolFlags.SetAccessor) != 0)
        {
            return ClassificationTypeName.PropertyName;
        }
        if ((flags & SymbolFlags.EnumMember) != 0)
        {
            return ClassificationTypeName.FieldName;
        }
        if ((flags & SymbolFlags.Function) != 0)
        {
            return ClassificationTypeName.MethodName;
        }
        if ((flags & SymbolFlags.Class) != 0)
        {
            return ClassificationTypeName.ClassName;
        }
        if ((flags & SymbolFlags.Interface) != 0)
        {
            return ClassificationTypeName.InterfaceName;
        }
        if ((flags & SymbolFlags.Enum) != 0)
        {
            return ClassificationTypeName.EnumName;
        }
        if ((flags & SymbolFlags.Module) != 0)
        {
            return ClassificationTypeName.ModuleName;
        }
        if ((flags & SymbolFlags.Method) != 0)
        {
            return ClassificationTypeName.MethodName;
        }
        if ((flags & SymbolFlags.TypeParameter) != 0)
        {
            return ClassificationTypeName.TypeParameterName;
        }
        if ((flags & SymbolFlags.TypeAlias) != 0)
        {
            return ClassificationTypeName.Identifier;
        }
        if ((flags & SymbolFlags.Alias) != 0)
        {
            return ClassificationTypeName.Identifier;
        }
        return ClassificationTypeName.Text;
    }

    /// <summary>isFirstDeclarationOfSymbolParameter checks if the symbol's first declaration is a parameter.</summary>
    internal static bool IsFirstDeclarationOfSymbolParameter(Symbol symbol)
    {
        var declarations = symbol.Declarations;
        if (declarations is null || declarations.Length == 0)
        {
            return false;
        }
        return declarations[0].Kind == Kind.Parameter;
    }
}
