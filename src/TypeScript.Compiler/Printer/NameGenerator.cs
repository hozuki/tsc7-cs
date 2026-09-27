// ported from: internal/printer/namegenerator.go
//
// 与 Go 的偏离（逐条说明；除标注外都是语义等价，只为对齐 C# 的语言特性）：
//
// 1) Go 的 `type tempFlags int` + 常量 tempFlagsAuto/tempFlagsCountMask/tempFlags_i → C#
//    `[Flags] internal enum TempFlags`（docs §3.3：成员名 = Go 常量名去掉 `tempFlags` 前缀，数值逐条一致）。
//    其中 `tempFlags_i` 落成 `I`：C# 枚举成员名以下划线起头虽然合法但不可读，而 Go 里它就是"_i 的偏好位"
//    这个专用名本身，故按契约重命名为 I（调用点 `tempFlags_i` ↔ `TempFlags.I`）。
//    **注意**：这三个常量在 Go 里是**位打包的计数器**——低 28 位是计数（`flags & tempFlagsCountMask`），
//    第 28 位是"偏好 '_i'"标志。因此本文件里的算术/位运算逐条保留，绝不能改写成普通的 flags 判定。
//    C# 的 enum 不支持 `++`/`|=`/`+`，故：
//      `tempFlags++`  → `tempFlags = (TempFlags)((int)tempFlags + 1)`（低位照 Go 的方式向第 28 位进位）
//      `tempFlags |= flags` → `tempFlags = tempFlags | flags`
//    Go 里 `count != 8`/`count != 13`/`count < 26`/`count - 26` 依赖未命名常量隐式转成 tempFlags，
//    C# 的 enum 只接受常量 0，故这些地方显式 `(int)count` 后再比较/相减；数值语义与 Go 完全一致。
//    `flags != 0`、`(tempFlags & flags) == 0` 是常量 0 的比较，C# 允许，照 Go 原样保留。
//
// 2) 集合：Go 的 `collections.Set[string]` → C# 的 `TypeScript.Collections.Set<string>`
//    （src/TypeScript.Compiler/Collections/Set.cs 已有同名等价类型，按 hard rule 优先复用现成类型，
//    不退回 `HashSet<string>`）。Go 零值 Set 可直接用（首个 Add 时建 map），C# 侧字段用 `= new()` 初始化。
//
// 3) 指向字段的指针：Go 的 `func (g *NameGenerator) getScope(privateName bool) **nameGenerationScope`
//    返回"字段的地址"，调用点用 `*scope` 读/写、`scope = &(*scope).next` 沿链走。C# 没有指向字段的指针，
//    改用 ref 返回的 `private ref NameGenerationScope? GetScope(bool privateName)`，调用点写 ref 局部变量：
//    Go 的 `*scope` 读/写 ↔ C# ref 局部变量的读/写（`scope._tempFlags = ...` 直接写回字段），
//    `scope = &(*scope).next` ↔ `scope = ref scope._next`。语义一致（含"字段为 nil 时 *scope == nil"）。
//    同样地 `core.IfElse(privateName, &g.nodeIdToGeneratedPrivateName, &g.nodeIdToGeneratedName)` ↔
//    条件 ref 表达式 `privateName ? ref _nodeIdToGeneratedPrivateName : ref _nodeIdToGeneratedName`。
//
// 4) `core.IfElse` 只在 `makeName` 里择一取回调（checkFn）时用到，按 Ast/SubtreeFacts.cs 的先例就地写 C#
//    条件表达式，不为此引入 Core 静态类。
//
// 5) `panic(...)` → `Debug.Fail(...)`（静态类 `TypeScript.Debug`，本文件命名空间嵌套在 `TypeScript` 下，
//    可直接写 `Debug`）。Go 里隐含的 nil 解引用（`ast.IsStringLiteral(expr)`、`node.Name()`、
//    `g.GetTextOfNode(...)` 为 nil 时 panic）在 C# 里用 `!` / 显式 `!` 表达：契约上这些值必须非 nil，
//    否则运行期失败——与 Go 一致；见各调用点的注释。
//
// 6) 访问性：Go 的 `reserveName`/`isUniqueName`/`makeUniqueName` 未导出，但 main 分片（printer.go）的绑定
//    契约要求公开 `ReserveName`/`IsUniqueName`/`MakeUniqueName`，故按契约公开（名字 = Go 名 PascalCase）。
//    同一契约另加了 2 参重载 `MakeUniqueName(string name, bool privateName)`（Go 里没有；等价于
//    checkFn=nil、optimistic=false、scoped=false、prefix=""、suffix="" 的那次调用）——7 参版本才是权威实现。
//    其余未导出成员只在本类内使用 → private（docs §2）。
//
// 7) 未导出类型 `nameGenerationScope` → `internal sealed class NameGenerationScope`；其未导出字段按 docs §2
//    落成 `_next`/`_tempFlags`/`_formattedNameTempFlags`/`_reservedNames`（.editorconfig：非公开字段 `_camelCase`）。
//
// 8) Go 的多值 `case A, B:` → C# 的 `case A or B:`（同 printer.go 的落地约定）。
//
// 9) 两个包级函数 `nextContainer`/`isUniqueLocalName` → `Printer` 的 `internal static` 成员（docs §2：Go 包级
//    函数 = `Printer` 的 static 成员），故文件末尾有一段 `partial class Printer`；它们在 Go 文件里的位置
//    （checkUniqueName 与 isUniqueName 之间）在 C# 里挪到文件末尾，其余成员保持 Go 的顺序。
//
// 10) Strada 相关的注释全部逐条保留（`generatedNames` 未按 scope 生效、`_i`/`_n` 的跳过规则等）：它们解释的是
//     当前**故意**与 Strada 一致、但已知不正确的行为，不是可以删掉的死注释。
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Collections;
using static TypeScript.Ast.Ast;

namespace TypeScript.Printer;

/// <summary>
/// Flags enum to track count of temp variables and a few dedicated names.
/// Go: <c>type tempFlags int</c> + <c>tempFlagsAuto</c>/<c>tempFlagsCountMask</c>/<c>tempFlags_i</c>。
/// 见文件头第 1 条：这是**位打包的计数器**（不是普通位标志集合），用法必须与 Go 一致。
/// </summary>
[Flags]
internal enum TempFlags
{
    /// <summary>Go: <c>tempFlagsAuto</c> —— No preferred name。</summary>
    Auto = 0x00000000,
    /// <summary>Go: <c>tempFlagsCountMask</c> —— Temp variable counter。</summary>
    CountMask = 0x0FFFFFFF,
    /// <summary>Go: <c>tempFlags_i</c> —— Use/preference flag for '_i'。</summary>
    I = 0x10000000,
}

/// <summary>Go: <c>type nameGenerationScope struct</c>：一层名字生成作用域。</summary>
internal sealed class NameGenerationScope
{
    /// <summary>Go: <c>next *nameGenerationScope</c> —— The next nameGenerationScope in the stack。</summary>
    internal NameGenerationScope? _next;

    /// <summary>Go: <c>tempFlags tempFlags</c> —— TempFlags for the current name generation scope。</summary>
    internal TempFlags _tempFlags;

    /// <summary>Go: <c>formattedNameTempFlags map[string]tempFlags</c> —— TempFlags for the current name generation scope（按生成名的固定部分为键，懒建）。</summary>
    internal Dictionary<string, TempFlags>? _formattedNameTempFlags;

    /// <summary>Go: <c>reservedNames collections.Set[string]</c> —— Names reserved in nested name generation scopes。</summary>
    internal readonly Set<string> _reservedNames = new();

    // Go: generatedNames collections.Set[string] // NOTE: generated names should be scoped after Strada port is complete.
}

/// <summary>Go: <c>type NameGenerator struct</c>：生成/缓存 emit 阶段的标识符名（生成名、私有名、临时变量名）。</summary>
public sealed class NameGenerator
{
    public EmitContext? Context;
    /// <summary>callback for Printer.isFileLevelUniqueNameInCurrentFile</summary>
    public Func<string, bool, bool>? IsFileLevelUniqueNameInCurrentFile;
    /// <summary>callback for Printer.getTextOfNode</summary>
    public Func<Node, string>? GetTextOfNode;
    /// <summary>Map of generated names for specific nodes（Go: <c>map[ast.NodeId]string</c>）</summary>
    private Dictionary<NodeId, string>? _nodeIdToGeneratedName;
    /// <summary>Map of generated private names for specific nodes（Go: <c>map[ast.NodeId]string</c>）</summary>
    private Dictionary<NodeId, string>? _nodeIdToGeneratedPrivateName;
    /// <summary>Map of generated names for temp and loop variables（Go: <c>map[AutoGenerateId]string</c>）</summary>
    private Dictionary<AutoGenerateId, string>? _autoGeneratedIdToGeneratedName;
    /// <summary>Go: <c>nameGenerationScope *nameGenerationScope</c></summary>
    private NameGenerationScope? _nameGenerationScope;
    /// <summary>Go: <c>privateNameGenerationScope *nameGenerationScope</c></summary>
    private NameGenerationScope? _privateNameGenerationScope;
    /// <summary>NOTE: Used to match Strada, but should be moved to nameGenerationScope after port is complete.</summary>
    private readonly Set<string> _generatedNames = new();

    public void PushScope(bool reuseTempVariableScope)
    {
        _privateNameGenerationScope = new NameGenerationScope { _next = _privateNameGenerationScope };
        if (!reuseTempVariableScope)
        {
            _nameGenerationScope = new NameGenerationScope { _next = _nameGenerationScope };
        }
    }

    public void PopScope(bool reuseTempVariableScope)
    {
        if (_privateNameGenerationScope is not null)
        {
            _privateNameGenerationScope = _privateNameGenerationScope._next;
        }
        if (!reuseTempVariableScope && _nameGenerationScope is not null)
        {
            _nameGenerationScope = _nameGenerationScope._next;
        }
    }

    // Go: `func (g *NameGenerator) getScope(privateName bool) **nameGenerationScope` —— 返回**字段本身**的引用
    // （见文件头第 3 条）：Go 的 `*scope` / `*scope = ...` ↔ C# 的 `scope` / `scope = ...`。
    private ref NameGenerationScope? GetScope(bool privateName)
    {
        return ref (privateName ? ref _privateNameGenerationScope : ref _nameGenerationScope);
    }

    private TempFlags GetTempFlags(bool privateName)
    {
        ref var scope = ref GetScope(privateName);
        if (scope is not null)
        {
            return scope._tempFlags;
        }
        return TempFlags.Auto;
    }

    private void SetTempFlags(bool privateName, TempFlags flags)
    {
        ref var scope = ref GetScope(privateName);
        if (scope is null)
        {
            scope = new NameGenerationScope();
        }
        scope._tempFlags = flags;
    }

    // Gets the TempFlags to use in the current nameGenerationScope for the given key
    private TempFlags GetTempFlagsForFormattedName(bool privateName, string formattedNameKey)
    {
        ref var scope = ref GetScope(privateName);
        if (scope is not null)
        {
            // Go 对 nil map 的读返回 (零值, false)，故这里要连带判 Dictionary 是否已懒建。
            if (scope._formattedNameTempFlags is not null
                && scope._formattedNameTempFlags.TryGetValue(formattedNameKey, out var flags))
            {
                return flags;
            }
        }
        return TempFlags.Auto;
    }

    // Sets the TempFlags to use in the current nameGenerationScope for the given key
    private void SetTempFlagsForFormattedName(bool privateName, string formattedNameKey, TempFlags flags)
    {
        ref var scope = ref GetScope(privateName);
        if (scope is null)
        {
            scope = new NameGenerationScope();
        }
        scope._formattedNameTempFlags ??= new Dictionary<string, TempFlags>();
        scope._formattedNameTempFlags[formattedNameKey] = flags;
    }

    public void ReserveName(string name, bool privateName, bool scoped, bool temp)
    {
        ref var scope = ref GetScope(privateName);
        if (scope is null)
        {
            scope = new NameGenerationScope();
        }
        if (privateName || scoped)
        {
            scope._reservedNames.Add(name);
        }
        else if (!temp)
        {
            _generatedNames.Add(name); // NOTE: Matches Strada, but is incorrect.
            // Go: (*scope).generatedNames.Add(name) // TODO: generated names should be scoped after Strada port is complete.
        }
    }

    // Generate the text for a generated identifier or private identifier
    public string GenerateName(Node name)
    {
        if (Context is not null)
        {
            var autoGenerate = Context.GetAutoGenerateInfo(name);
            if (autoGenerate is not null)
            {
                if (autoGenerate.Flags.IsNode())
                {
                    // Node names generate unique names based on their original node
                    // and are cached based on that node's id.
                    return GenerateNameForNodeCached(Context.GetNodeForGeneratedName(name), IsPrivateIdentifier(name), autoGenerate.Flags, autoGenerate.Prefix, autoGenerate.Suffix);
                }
                else
                {
                    // Auto, Loop, and Unique names are cached based on their unique autoGenerateId.
                    if (_autoGeneratedIdToGeneratedName is not null
                        && _autoGeneratedIdToGeneratedName.TryGetValue(autoGenerate.Id, out var autoGeneratedName))
                    {
                        return autoGeneratedName;
                    }
                    _autoGeneratedIdToGeneratedName ??= new Dictionary<AutoGenerateId, string>();
                    var generatedName = MakeName(name);
                    _autoGeneratedIdToGeneratedName[autoGenerate.Id] = generatedName;
                    return generatedName;
                }
            }
        }
        return GetTextOfNode!(name); // Go: g.GetTextOfNode(name)（回调为 nil 时 Go panic）
    }

    private string GenerateNameForNodeCached(Node node, bool privateName, GeneratedIdentifierFlags flags, string prefix, string suffix)
    {
        var nodeId = GetNodeId(node);
        // Go: core.IfElse(privateName, &g.nodeIdToGeneratedPrivateName, &g.nodeIdToGeneratedName)（文件头第 3 条）
        ref var cache = ref (privateName ? ref _nodeIdToGeneratedPrivateName : ref _nodeIdToGeneratedName);
        cache ??= new Dictionary<NodeId, string>();

        if (cache.TryGetValue(nodeId, out var name))
        {
            return name;
        }

        name = GenerateNameForNode(node, privateName, flags, prefix, suffix);
        cache[nodeId] = name;
        return name;
    }

    private string GenerateNameForNode(Node node, bool privateName, GeneratedIdentifierFlags flags, string prefix, string suffix)
    {
        switch (node.Kind)
        {
            case Kind.Identifier or Kind.PrivateIdentifier:
                return MakeUniqueName(GetTextOfNode!(node), null /*checkFn*/, flags.IsOptimistic(), flags.IsReservedInNestedScopes(), privateName, prefix, suffix);
            case Kind.ModuleDeclaration or Kind.EnumDeclaration:
                if (privateName || prefix.Length > 0 || suffix.Length > 0)
                {
                    Debug.Fail("Generated name for a module or enum cannot be private and may have neither a prefix nor suffix");
                }
                return GenerateNameForModuleOrEnum(node);
            case Kind.ImportDeclaration or Kind.JSImportDeclaration or Kind.ExportDeclaration:
                if (privateName || prefix.Length > 0 || suffix.Length > 0)
                {
                    Debug.Fail("Generated name for an import or export cannot be private and may have neither a prefix nor suffix");
                }
                return GenerateNameForImportOrExportDeclaration(node);
            case Kind.FunctionDeclaration or Kind.ClassDeclaration:
                if (privateName || prefix.Length > 0 || suffix.Length > 0)
                {
                    Debug.Fail("Generated name for a class or function declaration cannot be private and may have neither a prefix nor suffix");
                }
                var name = node.Name();
                // Go: `!(g.Context == nil && g.Context.HasAutoGenerateInfo(name))` —— 逐字保留 Go 的 `&&`
                // （第二操作数在 g.Context == nil 时也会被求值 → nil 解引用 panic；Go 这里应是 `||`），
                // 故 C# 用 `Context!` 表达同样的"运行期失败"语义。
                if (name is not null && !(Context is null && Context!.HasAutoGenerateInfo(name)))
                {
                    return GenerateNameForNode(name, false /*privateName*/, flags, "" /*prefix*/, "" /*suffix*/);
                }
                return GenerateNameForExportDefault();
            case Kind.ExportAssignment:
                if (privateName || prefix.Length > 0 || suffix.Length > 0)
                {
                    Debug.Fail("Generated name for an export assignment cannot be private and may have neither a prefix nor suffix");
                }
                return GenerateNameForExportDefault();
            case Kind.ClassExpression:
                if (privateName || prefix.Length > 0 || suffix.Length > 0)
                {
                    Debug.Fail("Generated name for a class expression cannot be private and may have neither a prefix nor suffix");
                }
                return GenerateNameForClassExpression();
            case Kind.MethodDeclaration or Kind.GetAccessor or Kind.SetAccessor:
                return GenerateNameForMethodOrAccessor(node, privateName, prefix, suffix);
            case Kind.ComputedPropertyName:
                return MakeTempVariableName(TempFlags.Auto, true /*reservedInNestedScopes*/, privateName, prefix, suffix);
            default:
                return MakeTempVariableName(TempFlags.Auto, false /*reservedInNestedScopes*/, privateName, prefix, suffix);
        }
    }

    // Go: node /* ModuleDeclaration | EnumDeclaration */
    private string GenerateNameForModuleOrEnum(Node node)
    {
        var name = GetTextOfNode!(node.Name()!); // Go: g.GetTextOfNode(node.Name())（两者为 nil 时 Go panic）
        // Use module/enum name itself if it is unique, otherwise make a unique variation
        if (Printer.IsUniqueLocalName(name, node)) // Go: isUniqueLocalName(name, node)（包级函数，见文件头第 9 条）
        {
            return name;
        }
        else
        {
            return MakeUniqueName(name, null /*checkFn*/, false /*optimistic*/, false /*scoped*/, false /*privateName*/, "" /*prefix*/, "" /*suffix*/);
        }
    }

    // Go: node /* ImportDeclaration | ExportDeclaration */
    private string GenerateNameForImportOrExportDeclaration(Node node)
    {
        // Go 这里没有 Strada 的 `expr &&` 守卫：expr 为 nil 时 ast.IsStringLiteral 会 panic，故 C# 用 `!`。
        var expr = GetExternalModuleName(node)!;
        var baseName = "module";
        if (IsStringLiteral(expr))
        {
            baseName = Printer.MakeIdentifierFromModuleName(expr.Text());
        }
        return MakeUniqueName(baseName, null /*checkFn*/, false /*optimistic*/, false /*scoped*/, false /*privateName*/, "" /*prefix*/, "" /*suffix*/);
    }

    private string GenerateNameForExportDefault()
    {
        return MakeUniqueName("default", null /*checkFn*/, false /*optimistic*/, false /*scoped*/, false /*privateName*/, "" /*prefix*/, "" /*suffix*/);
    }

    private string GenerateNameForClassExpression()
    {
        return MakeUniqueName("class", null /*checkFn*/, false /*optimistic*/, false /*scoped*/, false /*privateName*/, "" /*prefix*/, "" /*suffix*/);
    }

    // Go: node /* MethodDeclaration | AccessorDeclaration */
    private string GenerateNameForMethodOrAccessor(Node node, bool privateName, string prefix, string suffix)
    {
        if (IsIdentifier(node.Name()!)) // Go: ast.IsIdentifier(node.Name())（nil 时 Go panic）
        {
            return GenerateNameForNodeCached(node.Name()!, privateName, GeneratedIdentifierFlags.None, prefix, suffix);
        }
        return MakeTempVariableName(TempFlags.Auto, false /*reservedInNestedScopes*/, privateName, prefix, suffix);
    }

    private string MakeName(Node name)
    {
        if (Context is not null)
        {
            var autoGenerate = Context.GetAutoGenerateInfo(name);
            if (autoGenerate is not null)
            {
                switch (autoGenerate.Flags.Kind())
                {
                    case GeneratedIdentifierFlags.Auto:
                        return MakeTempVariableName(TempFlags.Auto, autoGenerate.Flags.IsReservedInNestedScopes(), IsPrivateIdentifier(name), autoGenerate.Prefix, autoGenerate.Suffix);
                    case GeneratedIdentifierFlags.Loop:
                        Debug.Assert(IsIdentifier(name));
                        return MakeTempVariableName(TempFlags.I, autoGenerate.Flags.IsReservedInNestedScopes(), false /*privateName*/, autoGenerate.Prefix, autoGenerate.Suffix);
                    case GeneratedIdentifierFlags.Unique:
                        // Go: core.IfElse(autoGenerate.Flags.IsFileLevel(), g.IsFileLevelUniqueNameInCurrentFile, nil)（文件头第 4 条）
                        return MakeUniqueName(
                            name.Text(),
                            autoGenerate.Flags.IsFileLevel() ? IsFileLevelUniqueNameInCurrentFile : null,
                            autoGenerate.Flags.IsOptimistic(),
                            autoGenerate.Flags.IsReservedInNestedScopes(),
                            IsPrivateIdentifier(name),
                            autoGenerate.Prefix,
                            autoGenerate.Suffix);
                }
            }
        }
        return GetTextOfNode!(name); // Go: g.GetTextOfNode(name)（回调为 nil 时 Go panic）
    }

    // Return the next available name in the pattern _a ... _z, _0, _1, ...
    // TempFlags._i may be used to express a preference for that dedicated name.
    // Note that names generated by makeTempVariableName and makeUniqueName will never conflict.
    private string MakeTempVariableName(TempFlags flags, bool reservedInNestedScopes, bool privateName, string prefix, string suffix)
    {
        TempFlags tempFlags;
        var key = ""; // Go 的 string 零值；simple 路径下不会被读。
        var simple = prefix.Length == 0 && suffix.Length == 0;
        if (simple)
        {
            tempFlags = GetTempFlags(privateName);
        }
        else
        {
            // Generate a key to use to acquire a TempFlags counter based on the fixed portions of the generated name.
            key = Printer.FormatGeneratedName(privateName, prefix, "" /*base*/, suffix);
            if (privateName)
            {
                key = Printer.EnsureLeadingHash(key);
            }
            tempFlags = GetTempFlagsForFormattedName(privateName, key);
        }

        if (flags != 0 && (tempFlags & flags) == 0)
        {
            var fullName = Printer.FormatGeneratedName(privateName, prefix, "_i", suffix);
            if (IsUniqueName(fullName, privateName))
            {
                tempFlags = tempFlags | flags;
                ReserveName(fullName, privateName, reservedInNestedScopes, true /*temp*/);
                if (simple)
                {
                    SetTempFlags(privateName, tempFlags);
                }
                else
                {
                    SetTempFlagsForFormattedName(privateName, key, tempFlags);
                }
                return fullName;
            }
        }

        while (true)
        {
            var count = tempFlags & TempFlags.CountMask;
            tempFlags = (TempFlags)((int)tempFlags + 1);
            // Skip over 'i' and 'n'
            if ((int)count != 8 && (int)count != 13)
            {
                string name;
                if ((int)count < 26)
                {
                    name = $"_{(char)('a' + (int)count)}";
                }
                else
                {
                    name = $"_{((int)count - 26).ToString(CultureInfo.InvariantCulture)}";
                }
                var fullName = Printer.FormatGeneratedName(privateName, prefix, name, suffix);
                if (IsUniqueName(fullName, privateName))
                {
                    ReserveName(fullName, privateName, reservedInNestedScopes, true /*temp*/);
                    if (simple)
                    {
                        SetTempFlags(privateName, tempFlags);
                    }
                    else
                    {
                        SetTempFlagsForFormattedName(privateName, key, tempFlags);
                    }
                    return fullName;
                }
            }
        }
    }

    // Generate a name that is unique within the current file and doesn't conflict with any names
    // in global scope. The name is formed by adding an '_n' suffix to the specified base name,
    // where n is a positive integer. Note that names generated by makeTempVariableName and
    // makeUniqueName are guaranteed to never conflict.
    // If `optimistic` is set, the first instance will use 'baseName' verbatim instead of 'baseName_1'
    public string MakeUniqueName(string baseName, Func<string, bool, bool>? checkFn, bool optimistic, bool scoped, bool privateName, string prefix, string suffix)
    {
        baseName = Printer.RemoveLeadingHash(baseName);
        if (optimistic)
        {
            var fullName = Printer.FormatGeneratedName(privateName, prefix, baseName, suffix);
            if (CheckUniqueName(fullName, privateName, checkFn))
            {
                ReserveName(fullName, privateName, scoped, false /*temp*/);
                return fullName;
            }
        }

        // Find the first unique 'name_n', where n is a positive integer
        if (baseName.Length > 0 && baseName[baseName.Length - 1] != '_')
        {
            baseName += "_";
        }

        var i = 1;
        while (true)
        {
            var fullName = Printer.FormatGeneratedName(privateName, prefix, baseName + i.ToString(CultureInfo.InvariantCulture), suffix);
            if (CheckUniqueName(fullName, privateName, checkFn))
            {
                ReserveName(fullName, privateName, scoped, false /*temp*/);
                return fullName;
            }
            i++;
        }
    }

    /// <summary>
    /// 2 参重载（Go 里没有；main 分片绑定契约要求）：等价于
    /// <c>makeUniqueName(name, nil /*checkFn*/, false /*optimistic*/, false /*scoped*/, privateName, "", "")</c>。
    /// 权威实现是上面的 7 参版本（见文件头第 6 条）。
    /// </summary>
    public string MakeUniqueName(string name, bool privateName)
    {
        return MakeUniqueName(name, null /*checkFn*/, false /*optimistic*/, false /*scoped*/, privateName, "" /*prefix*/, "" /*suffix*/);
    }

    public string MakeFileLevelOptimisticUniqueName(string name)
    {
        return MakeUniqueName(name, IsFileLevelUniqueNameInCurrentFile, true /*optimistic*/, false /*scoped*/, false /*privateName*/, "" /*prefix*/, "" /*suffix*/);
    }

    private bool CheckUniqueName(string name, bool privateName, Func<string, bool, bool>? checkFn)
    {
        if (checkFn is not null)
        {
            return checkFn(name, privateName);
        }
        else
        {
            return IsUniqueName(name, privateName);
        }
    }

    public bool IsUniqueName(string name, bool privateName)
    {
        return (IsFileLevelUniqueNameInCurrentFile is null || IsFileLevelUniqueNameInCurrentFile(name, privateName)) &&
            !IsReservedName(name, privateName);
    }

    private bool IsReservedName(string name, bool privateName)
    {
        ref var scope = ref GetScope(privateName);

        // NOTE: The following matches Strada, but is incorrect.
        if (_generatedNames.Has(name))
        {
            return true;
        }

        // TODO: generated names should be scoped after Strada port is complete.
        ////if (*scope != nil) {
        ////    if (*scope).generatedNames.Has(name) {
        ////        return true
        ////    }
        ////}

        while (scope is not null)
        {
            if (scope._reservedNames.Has(name))
            {
                return true;
            }
            scope = ref scope._next;
        }
        return false;
    }
}

// 见文件头第 9 条：Go 的包级函数 `nextContainer`/`isUniqueLocalName`（namegenerator.go）落到 `Printer` 的
// static 成员（Go 包级函数 = 包级 `Printer` 的 static 成员，docs §2）。本段在 Go 文件里的位置是
// checkUniqueName 与 isUniqueName 之间；C# 里集中到文件末尾。
public sealed partial class Printer
{
    internal static Node? NextContainer(Node node)
    {
        var data = node.LocalsContainerData();
        if (data is not null)
        {
            return data.NextContainer;
        }
        return null;
    }

    internal static bool IsUniqueLocalName(string name, Node container)
    {
        var node = container;
        while (node is not null && IsNodeDescendantOf(node, container) && node.LocalsContainerData() is not null)
        {
            var locals = node.Locals();
            if (locals is not null)
            {
                // We conservatively include alias symbols to cover cases where they're emitted as locals
                if (locals.TryGetValue(name, out var local)
                    && (local.Flags & (SymbolFlags.Value | SymbolFlags.ExportValue | SymbolFlags.Alias)) != 0)
                {
                    return false;
                }
            }
            node = NextContainer(node);
        }
        return true;
    }
}
