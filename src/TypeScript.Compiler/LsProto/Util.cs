// ported from: internal/lsp/lsproto/util.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的包级函数（ComparePositions/CompareRanges/CompareDiagnostics）→ 静态类 `LsProto` 的 public static
//    成员（docs/architecture.md §2：包级函数落在与包同名的静态类 LsProto 里）。
// 2) Go 里写在 StringOrMarkupContent/IntegerOrString/Diagnostic 上的方法（AsString/CodeAsString）→ C# 的
//    **扩展方法**：这些类型由生成器产出（Lsp.Generated.Unions.cs / Structures.cs），不是 partial，无法加实例
//    方法；先例是 Lsp.Generated.Enums.cs 里 Go 的 stringer（`String(this ErrorCode)`）。调用点写法不变
//    （`diag.AsString()`），与 Go 逐字对应。
// 3) Go 的未导出函数（diagnosticExistsInSlice/diagnosticsEqual/diagnosticCodesEqual/diagnosticMessagesEqual）
//    → `internal static` + PascalCase（同 LsUtil 的 parseIndentStyle → ParseIndentStyle 先例）。
// 4) `[]*Diagnostic` → `Diagnostic[]?`（nil 切片 = null，消费处 `?? []`）；Go 返回的两个切片本来就是非 nil 的
//    空切片（`[]*Diagnostic{}`），C# 返回空数组。
// 5) Go 的 `cmp.Compare`（有序类型的三路比较：<0 / 0 / >0）→ C# 的 `T.CompareTo`；uint 的 CompareTo 与
//    cmp.Compare 一样返回规范化后的 -1/0/1。
// 6) `strconv.Itoa(int(*m.Integer))` 与 `fmt.Sprintf` 的 `%v` 对整数一律十进制、与 culture 无关 →
//    C# 侧显式 `CultureInfo.InvariantCulture`。
using System.Globalization;

namespace TypeScript.LsProto;

public static partial class LsProto
{
    /// <summary>Go: <c>func ComparePositions(pos, other Position) int</c>（= <c>cmp.Compare(pos, other)</c>）。</summary>
    public static int ComparePositions(Position pos, Position other)
    {
        var lineComp = pos.Line.CompareTo(other.Line);
        if (lineComp != 0)
        {
            return lineComp;
        }

        return pos.Character.CompareTo(other.Character);
    }

    /// <summary>
    /// Go: <c>func CompareRanges(lsRange, other Range) int</c>（= <c>cmp.Compare(lsRange, other)</c>）。
    /// Range.Start 先于 Range.End 比较。
    /// </summary>
    public static int CompareRanges(Range lsRange, Range other)
    {
        var startComp = ComparePositions(lsRange.Start, other.Start);
        if (startComp != 0)
        {
            return startComp;
        }

        return ComparePositions(lsRange.End, other.End);
    }

    /// <summary>
    /// Go: <c>func (m StringOrMarkupContent) AsString() string</c>：不是普通字符串时读 MarkupContent.Value。
    /// </summary>
    public static string AsString(this StringOrMarkupContent m)
    {
        if (m.String is not null)
        {
            return m.String;
        }

        if (m.MarkupContent is not null)
        {
            return m.MarkupContent.Value;
        }

        return "";
    }

    /// <summary>Go: <c>func (m IntegerOrString) AsString() string</c>（两者都没有时返回 <c>"-1"</c>）。</summary>
    public static string AsString(this IntegerOrString m)
    {
        string codeStr;
        if (m.String is not null)
        {
            codeStr = m.String;
        }
        else if (m.Integer is not null)
        {
            codeStr = m.Integer.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            codeStr = "-1";
        }

        return codeStr;
    }

    /// <summary>Go: <c>func diagnosticExistsInSlice(elem *Diagnostic, diags []*Diagnostic) bool</c>。</summary>
    internal static bool DiagnosticExistsInSlice(Diagnostic elem, Diagnostic[] diags)
    {
        foreach (var diag in diags)
        {
            if (DiagnosticsEqual(elem, diag))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func diagnosticsEqual(diag1 *Diagnostic, diag2 *Diagnostic) bool</c>。</summary>
    internal static bool DiagnosticsEqual(Diagnostic diag1, Diagnostic diag2)
    {
        // Go 的 `diag1.Code` 是 `*IntegerOrString`：这里用 `!` 表达 Go 的隐式解引用（nil 同样是运行期失败）。
        if (DiagnosticCodesEqual(diag1.Code!, diag2.Code!) && DiagnosticMessagesEqual(diag1.Message, diag2.Message) && CompareRanges(diag1.Range, diag2.Range) == 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Go: <c>func diagnosticCodesEqual(code1 *IntegerOrString, code2 *IntegerOrString) bool</c>
    /// （Go 侧对 nil 指针是解引用 panic，C# 侧由调用点的 `!` 承担同一语义）。
    /// </summary>
    internal static bool DiagnosticCodesEqual(IntegerOrString code1, IntegerOrString code2)
    {
        if (code1.String is not null && code2.String is not null)
        {
            return code1.String == code2.String;
        }

        if (code1.Integer is not null && code2.Integer is not null)
        {
            return code1.Integer == code2.Integer;
        }

        return false;
    }

    /// <summary>Go: <c>func diagnosticMessagesEqual(message1, message2 StringOrMarkupContent) bool</c>。</summary>
    internal static bool DiagnosticMessagesEqual(StringOrMarkupContent message1, StringOrMarkupContent message2)
    {
        if (message1.String is not null && message2.String is not null)
        {
            return message1.String == message2.String;
        }

        if (message1.MarkupContent is not null && message2.MarkupContent is not null)
        {
            return message1.MarkupContent.Kind == message2.MarkupContent.Kind && message1.MarkupContent.Value == message2.MarkupContent.Value;
        }

        return false;
    }

    /// <summary>
    /// Go: <c>func CompareDiagnostics(list1, list2 []*Diagnostic) ([]*Diagnostic, []*Diagnostic)</c>：
    /// 返回「list1 少掉的」「list2 少掉的」（注意 Go 的命名与 append 目标相反，这里照抄）。
    /// </summary>
    public static (Diagnostic[] MissingFromList1, Diagnostic[] MissingFromList2) CompareDiagnostics(Diagnostic[]? list1, Diagnostic[]? list2)
    {
        var missingFromList1 = new List<Diagnostic>();
        var missingFromList2 = new List<Diagnostic>();
        foreach (var elem in list1 ?? [])
        {
            if (!DiagnosticExistsInSlice(elem, list2 ?? []))
            {
                missingFromList2.Add(elem);
            }
        }

        foreach (var elem in list2 ?? [])
        {
            if (!DiagnosticExistsInSlice(elem, list1 ?? []))
            {
                missingFromList1.Add(elem);
            }
        }

        return (missingFromList1.ToArray(), missingFromList2.ToArray());
    }

    /// <summary>Go: <c>func (elem *Diagnostic) AsString() string</c>。</summary>
    public static string AsString(this Diagnostic elem)
    {
        return $"{elem.Code!.AsString()} ({elem.Range.Start.Line.ToString(CultureInfo.InvariantCulture)}:{elem.Range.Start.Character.ToString(CultureInfo.InvariantCulture)}-{elem.Range.End.Line.ToString(CultureInfo.InvariantCulture)}:{elem.Range.End.Character.ToString(CultureInfo.InvariantCulture)}): {elem.Message.AsString()}";
    }

    /// <summary>Go: <c>func (elem *Diagnostic) CodeAsString() string</c>。</summary>
    public static string CodeAsString(this Diagnostic elem)
    {
        return $"Code({elem.Code!.AsString()})";
    }
}
