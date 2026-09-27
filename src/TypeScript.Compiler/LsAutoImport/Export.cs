// ported from: internal/ls/autoimport/export.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的命名 string 类型 `type ModuleID string` → C# 的 readonly struct（与 `lsproto.DocumentUri`
//    同款：显式转换 + 手工相等性 + `Value` 归一化 null）。**不能**用 record struct：Go 的零值 `ModuleID("")`
//    在 C# 的 `default(ModuleID)` 里会绕过构造函数把字段留在 null，与 `new ModuleID("")` 不相等，
//    而 `Export.Target` 这类字段恰恰靠"零值比较"判断（`e.Target != ExportID{}`）—— 本仓在
//    `Dictionary.TryGetValue(null)` 上踩过同类坑，所以这里显式把 null 归一化成 ""。
// 2) `ExportID` 是 Go 的结构体（比较/拷贝语义），C# 用 readonly struct + IEquatable（同上归一化）。
// 3) `Export` 嵌入 `ExportID`（Go 的匿名字段，因此 `ModuleID`/`ExportName` 是**提升字段**）：
//    C# 没有嵌入，改成两个字段 + 一个只读属性 `ExportID`（保留 Go 的字段名，`e.ExportID` 处照写）。
// 4) Go 未导出字段 `localName`/`through` → C# `_localName`/`_through`（internal：extract.go/view.go 跨文件访问）。
// 5) `checker.IsExternalModuleSymbol`/`ch.GetMergedSymbol`/`ch.SkipAlias` 在 C# 侧分别是静态类 `Checker` 的
//    静态方法与 `Checker` 实例方法（Go 的包级函数在本仓统一放同名静态类，见 docs/architecture.md §2）。
// 6) `ast.X` 包级函数 → `using static TypeScript.Ast.Ast;` 后的裸名；`ast.SymbolFlags` → `SymbolFlags`。
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Core;
using TypeScript.LsUtil;
using Path = TypeScript.TSPath.Path;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.LsAutoImport;

/// <summary>Go: <c>type ModuleID string</c>（export.go:19-21）。<br/>
/// 唯一标识一个模块的多次声明：来自 ambient module 声明时是模块名；来自模块增强时是被解析模块文件的 Path()；
/// 否则是导出源文件的 Path()。
/// </summary>
public readonly struct ModuleID : IEquatable<ModuleID>
{
    private readonly string? _value;

    public ModuleID(string? value) => _value = value;

    /// <summary>Go 的 <c>string(id)</c>。</summary>
    public string Value => _value ?? "";

    public static explicit operator ModuleID(string? value) => new(value);

    public static explicit operator string(ModuleID id) => id.Value;

    public override string ToString() => Value;

    public bool Equals(ModuleID other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ModuleID other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(ModuleID left, ModuleID right) => left.Equals(right);

    public static bool operator !=(ModuleID left, ModuleID right) => !left.Equals(right);
}

/// <summary>Go: <c>type ExportID struct</c>（export.go:23-26）。</summary>
public readonly struct ExportID : IEquatable<ExportID>
{
    private readonly string? _exportName;

    public ExportID(ModuleID moduleID, string? exportName)
    {
        ModuleID = moduleID;
        _exportName = exportName;
    }

    public ModuleID ModuleID { get; }

    public string ExportName => _exportName ?? "";

    public override string ToString() => $"{{ModuleID: {ModuleID}, ExportName: {ExportName}}}";

    public bool Equals(ExportID other) => ModuleID.Equals(other.ModuleID) && string.Equals(ExportName, other.ExportName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ExportID other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ModuleID, ExportName);

    public static bool operator ==(ExportID left, ExportID right) => left.Equals(right);

    public static bool operator !=(ExportID left, ExportID right) => !left.Equals(right);
}

/// <summary>Go: <c>type ExportSyntax int</c>（export.go:28-49）。成员名去类型前缀，数值与 Go 一致（§3.3）。</summary>
public enum ExportSyntax : int
{
    None = 0,
    // export const x = {}
    Modifier = 1,
    // export { x }
    Named = 2,
    // export default function f() {}
    DefaultModifier = 3,
    // export default f
    DefaultDeclaration = 4,
    // export = x
    Equals = 5,
    // export as namespace x
    UMD = 6,
    // export * from "module"
    Star = 7,
    // module.exports = {}
    CommonJSModuleExports = 8,
    // exports.x = {}
    CommonJSExportsProperty = 9,
}

/// <summary>Go: <c>type Export struct</c>（export.go:51-73）。</summary>
public sealed class Export : INamed
{
    // Go 的嵌入字段 ExportID 提升上来的两个字段（见文件头第 3 条）。
    public ModuleID ModuleID;

    public string ExportName = "";

    public string ModuleFileName = "";

    public ExportSyntax Syntax;

    public SymbolFlags Flags;

    /// <summary>Go 的未导出字段 <c>localName</c>。</summary>
    internal string? _localName;

    /// <summary>Go 的未导出字段 <c>through</c>：本导出是在模块符号的那个导出名上找到的，
    /// 取值为 'export='、InternalSymbolNameExportStar 或空串。</summary>
    internal string? _through;

    // Checker-set fields

    public ExportID Target;

    public bool IsTypeOnly;

    public ScriptElementKind ScriptElementKind;

    public ScriptElementKindModifier ScriptElementKindModifiers;

    /// <summary>The file where the export was found.</summary>
    public Path Path;

    public string PackageName = "";

    /// <summary>Go 的嵌入字段 <c>ExportID</c>：保持字段名可读（`e.ExportID` 照写），值由上面两个字段派生。</summary>
    public ExportID ExportID => new(ModuleID, ExportName);

    /// <summary>Go: <c>func (e *Export) Name() string</c>（export.go:75-83）。</summary>
    public string Name()
    {
        if (!string.IsNullOrEmpty(_localName))
        {
            return _localName;
        }
        if (ExportName == InternalSymbolNameExportEquals)
        {
            return Target.ExportName;
        }
        return ExportName;
    }

    /// <summary>Go: <c>func (e *Export) IsRenameable() bool</c>（export.go:85-87）。</summary>
    public bool IsRenameable() => ExportName == InternalSymbolNameExportEquals || ExportName == InternalSymbolNameDefault;

    /// <summary>Go: <c>func (e *Export) AmbientModuleName() string</c>（export.go:89-94）。</summary>
    public string AmbientModuleName()
    {
        if (!IsExternalModuleNameRelative((string)ModuleID))
        {
            return (string)ModuleID;
        }
        return "";
    }

    /// <summary>Go: <c>func (e *Export) IsUnresolvedAlias() bool</c>（export.go:96-98）。</summary>
    public bool IsUnresolvedAlias() => Flags == SymbolFlags.Alias;
}

/// <summary>Go 的 <c>autoimport</c> 包（包级函数/常量放这里，见 docs/architecture.md §2）。</summary>
public static partial class AutoImport
{
    /// <summary>Go: <c>func SymbolToExport(symbol *ast.Symbol, ch *checker.Checker) *Export</c>（export.go:100-127）。</summary>
    public static Export? SymbolToExport(Symbol symbol, TypeScript.Checker.Checker ch)
    {
        if (symbol.Parent is not null && TypeScript.Checker.Checker.IsExternalModuleSymbol(symbol.Parent))
        {
            if (TryGetModuleIDAndFileNameOfModuleSymbol(symbol.Parent, out var moduleID, out var moduleFileName))
            {
                return ExtractFirstExport(symbol, ch, moduleID, moduleFileName, GetSourceFileOfModule(symbol.Parent)!);
            }
            return null;
        }

        var declaration = FirstOrNil(symbol.Declarations);
        if (declaration is null)
        {
            return null;
        }

        var file = GetSourceFileOfNode(declaration);
        if (file is null || file.Symbol is null)
        {
            return null;
        }

        var moduleSymbol = ch.GetMergedSymbol(file.Symbol)!;
        var moduleId = (ModuleID)(string)file.Path();
        var fileName = file.FileName();
        var target = ch.GetMergedSymbol(ch.SkipAlias(symbol)!)!;

        if (TryGetModuleExport(InternalSymbolNameDefault, target, moduleSymbol, ch, moduleId, fileName, file) is { } export)
        {
            return export;
        }
        if (TryGetModuleExport(InternalSymbolNameExportEquals, target, moduleSymbol, ch, moduleId, fileName, file) is { } exportEquals)
        {
            return exportEquals;
        }
        return TryGetModuleExport(symbol.Name, target, moduleSymbol, ch, moduleId, fileName, file);
    }

    /// <summary>Go: <c>func tryGetModuleExport(...)</c>（export.go:129-135）。</summary>
    internal static Export? TryGetModuleExport(string exportName, Symbol target, Symbol moduleSymbol, TypeScript.Checker.Checker ch, ModuleID moduleID, string moduleFileName, SourceFile file)
    {
        var exported = ch.TryGetMemberInModuleExportsAndProperties(exportName, moduleSymbol);
        if (exported is not null && ch.GetMergedSymbol(ch.SkipAlias(exported)!) == target)
        {
            return ExtractFirstExport(exported, ch, moduleID, moduleFileName, file);
        }
        return null;
    }

    /// <summary>Go: <c>func extractFirstExport(...)</c>（export.go:137-141）。</summary>
    internal static Export? ExtractFirstExport(Symbol symbol, TypeScript.Checker.Checker ch, ModuleID moduleID, string moduleFileName, SourceFile file)
    {
        var exports = new List<Export>();
        var extractor = new SymbolExtractor("", ch, null, null);
        extractor.ExtractFromSymbol(symbol.Name, symbol, moduleID, moduleFileName, file, ref exports);
        return FirstOrNil(exports.ToArray());
    }
}
