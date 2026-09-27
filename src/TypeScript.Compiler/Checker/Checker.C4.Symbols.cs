// ported from: internal/checker/checker.go:14302-14568
//   （newSymbol / newSymbolEx / newParameter / newProperty / combineSymbolTables / mergeSymbolTable / mergeSymbol /
//    reportMergeSymbolError / addDuplicateDeclarationErrorsForSymbols / addDuplicateDeclarationError /
//    createDiagnosticForNode / getAdjustedNodeForError / lookupOrIssueError / getFirstDeclaration /
//    getExcludedSymbolFlags）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `c.symbolArena.New()` → `new Symbol()`：Symbol 在 C# 里是 class（引用类型），Go 的 arena 只为"结构体紧凑
//    存放 + 稳定指针"（docs/architecture.md §3.2、Binder.cs 文件头 2 同款处理）。
// 2) 包级函数（无接收者）→ 同一个类的 static 成员：`createDiagnosticForNode` → `CreateDiagnosticForNode`、
//    `getAdjustedNodeForError` → `GetAdjustedNodeForError`、`getFirstDeclaration` → `GetFirstDeclaration`、
//    `getExcludedSymbolFlags` → `GetExcludedSymbolFlags`。
// 3) `ast.SymbolTable` 是 `Dictionary<string, Symbol>`，Go 的 `target[id]` 读（零值 nil）→ `TryGetValue`；
//    `len(table)` → `table.Count`；`make(ast.SymbolTable)` → `new SymbolTable()`。
// 4) `ast.GetSymbolTable(&target.Members)` 接收 `*SymbolTable`（Go 的"取字段地址以便写到字段"）→ C# 的
//    `GetSymbolTable(ref target.Members)`（`Utilities.GetSymbolTable(ref SymbolTable?)`，同义：null 时建表并写回）。
// 5) `append(target.Declarations, source.Declarations...)` → `Concatenate(target.Declarations, source.Declarations)`
//    （Core 的拼接助手，含 nil 切片语义，§3.2）。
// 6) `binder.SetValueDeclaration(...)`（`internal/binder` 包级导出函数，C# 是 `Binder` 类的 `public static`）→
//    用别名 `CheckerBinder` 调用：本命名空间里裸写 `Binder` 会先解析到命名空间 `TypeScript.Binder`
//    （docs/architecture.md §2 的命名空间/类型同名坑，同 `Ast`/`Diagnostics`）。
// 7) `getExcludedSymbolFlags` 里 Go 的 `result &^= ast.SymbolFlagsMethod` → `result &= ~SymbolFlags.Method`。
// 8) 未导出成员命中 `artifacts/checker-method-collisions.md` 的改名照写：`getMergedSymbol` →
//    `GetMergedSymbolWorker`（本片定义）、`symbolToString` → `SymbolToStringWorker`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using CheckerBinder = TypeScript.Binder.Binder;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) newSymbol(flags ast.SymbolFlags, name string) *ast.Symbol</c>。</summary>
    internal Symbol NewSymbol(SymbolFlags flags, string name)
    {
        SymbolCount++;
        var result = new Symbol(); // Go: c.symbolArena.New()
        result.Flags = flags | SymbolFlags.Transient;
        result.Name = name;
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) newSymbolEx(flags ast.SymbolFlags, name string, checkFlags ast.CheckFlags) *ast.Symbol</c>。</summary>
    internal Symbol NewSymbolEx(SymbolFlags flags, string name, CheckFlags checkFlags)
    {
        var result = NewSymbol(flags, name);
        result.CheckFlags = checkFlags;
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) newParameter(name string, t *Type) *ast.Symbol</c>。</summary>
    internal Symbol NewParameter(string name, Type t)
    {
        var symbol = NewSymbol(SymbolFlags.FunctionScopedVariable, name);
        _valueSymbolLinks.Get(symbol)._resolvedType = t;
        return symbol;
    }

    /// <summary>Go: <c>func (c *Checker) newProperty(name string, t *Type) *ast.Symbol</c>。</summary>
    internal Symbol NewProperty(string name, Type t)
    {
        var symbol = NewSymbol(SymbolFlags.Property, name);
        _valueSymbolLinks.Get(symbol)._resolvedType = t;
        return symbol;
    }

    /// <summary>
    /// Go: <c>maps.Clone(m)</c>。`SymbolTable : Dictionary&lt;string, Symbol&gt;` 没有继承 `Dictionary` 的拷贝构造器
    /// （C# 的构造器不继承），所以这里显式复制条目；Go 的 `maps.Clone(nil)` → nil 由调用点的 `is null ? null :` 保留。
    /// </summary>
    internal static SymbolTable CloneSymbolTable(SymbolTable source)
    {
        var result = new SymbolTable();
        foreach (var (name, symbol) in source)
        {
            result[name] = symbol;
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func (c *Checker) combineSymbolTables(first, second ast.SymbolTable) ast.SymbolTable</c>。
    /// Go 的 map 允许 nil（nil 与空表在所有映射操作上等价），C# 的 <see cref="SymbolTable"/> 是 class →
    /// 结果会为 nil 时返回空表（调用点只把它当映射读/遍历；见 Checker.C4.EntityName.cs 的说明）。
    /// </summary>
    internal SymbolTable CombineSymbolTables(SymbolTable? first, SymbolTable? second)
    {
        if (first is null || first.Count == 0)
        {
            return second ?? new SymbolTable();
        }

        if (second is null || second.Count == 0)
        {
            return first;
        }

        var combined = new SymbolTable();
        MergeSymbolTable(combined, first, false, null);
        MergeSymbolTable(combined, second, false, null);
        return combined;
    }

    /// <summary>Go: <c>func (c *Checker) mergeSymbolTable(target, source ast.SymbolTable, unidirectional bool, mergedParent *ast.Symbol)</c>。</summary>
    internal void MergeSymbolTable(SymbolTable target, SymbolTable source, bool unidirectional, Symbol? mergedParent)
    {
        foreach (var (id, sourceSymbol) in source)
        {
            target.TryGetValue(id, out var targetSymbol);
            Symbol merged;
            if (targetSymbol is not null)
            {
                merged = MergeSymbol(targetSymbol, sourceSymbol, unidirectional);
            }
            else
            {
                merged = GetMergedSymbolWorker(sourceSymbol)!;
            }

            if (mergedParent is not null && targetSymbol is not null)
            {
                // If a merge was performed on the target symbol, set its parent to the merged parent that initiated the merge
                // of its exports. Otherwise, `merged` came only from `sourceSymbol` and can keep its parent:
                //
                // // a.ts
                // export interface A { x: number; }
                //
                // // b.ts
                // declare module "./a" {
                //   interface A { y: number; }
                //   interface B {}
                // }
                //
                // When merging the module augmentation into a.ts, the symbol for `A` will itself be merged, so its parent
                // should be the merged module symbol. But the symbol for `B` has only one declaration, so its parent should
                // be the module augmentation symbol, which contains its only declaration.
                if ((merged.Flags & SymbolFlags.Transient) != 0)
                {
                    merged.Parent = mergedParent;
                }
            }

            target[id] = merged;
        }
    }

    /// <summary>
    /// Note: if target is transient, then it is mutable, and mergeSymbol with both mutate and return it.
    /// If target is not transient, mergeSymbol will produce a transient clone, mutate that and return it.
    /// </summary>
    internal Symbol MergeSymbol(Symbol target, Symbol source, bool unidirectional)
    {
        if ((target.Flags & GetExcludedSymbolFlags(source.Flags)) == 0
            || ((source.Flags | target.Flags) & SymbolFlags.Assignment) != 0)
        {
            if (source == target)
            {
                // This can happen when an export assigned namespace exports something also erroneously exported at the top level
                // See `declarationFileNoCrashOnExtraExportModifier` for an example
                return target;
            }

            if ((target.Flags & SymbolFlags.Transient) == 0)
            {
                var resolvedTarget = ResolveSymbol(target);
                if (resolvedTarget == _unknownSymbol)
                {
                    return source;
                }

                if ((resolvedTarget!.Flags & GetExcludedSymbolFlags(source.Flags)) == 0
                    || ((source.Flags | resolvedTarget.Flags) & SymbolFlags.Assignment) != 0)
                {
                    target = CloneSymbol(resolvedTarget);
                }
                else
                {
                    ReportMergeSymbolError(target, source);
                    return source;
                }
            }

            // Javascript static-property-assignment declarations always merge, even though they are also values
            if ((source.Flags & SymbolFlags.ValueModule) != 0
                && (target.Flags & SymbolFlags.ValueModule) != 0
                && (target.Flags & SymbolFlags.ConstEnumOnlyModule) != 0
                && (source.Flags & SymbolFlags.ConstEnumOnlyModule) == 0)
            {
                // reset flag when merging instantiated module into value module that has only const enums
                target.Flags &= ~SymbolFlags.ConstEnumOnlyModule;
            }

            var sourceFlags = source.Flags;
            if ((target.Flags & SymbolFlags.ConstEnumOnlyModule) == 0)
            {
                sourceFlags &= ~SymbolFlags.ConstEnumOnlyModule;
            }

            target.Flags |= sourceFlags;
            if (source.ValueDeclaration is not null)
            {
                CheckerBinder.SetValueDeclaration(target, source.ValueDeclaration);
            }

            target.Declarations = Concatenate(target.Declarations, source.Declarations);
            if (source.Members is not null)
            {
                MergeSymbolTable(GetSymbolTable(ref target.Members), source.Members, unidirectional, null);
            }

            if (source.Exports is not null)
            {
                MergeSymbolTable(GetSymbolTable(ref target.Exports), source.Exports, unidirectional, target);
            }

            if (!unidirectional)
            {
                RecordMergedSymbol(target, source);
            }
        }
        else if ((target.Flags & SymbolFlags.NamespaceModule) != 0)
        {
            // Do not report an error when merging `var globalThis` with the built-in `globalThis`,
            // as we will already report a "Declaration name conflicts..." error, and this error
            // won't make much sense.
            if (target != _globalThisSymbol)
            {
                Error(GetNameOfDeclaration(GetFirstDeclaration(source)), Cannot_augment_module_0_with_value_exports_because_it_resolves_to_a_non_module_entity, SymbolToStringWorker(target));
            }
        }
        else
        {
            ReportMergeSymbolError(target, source);
        }

        return target;
    }

    /// <summary>Go: <c>func (c *Checker) reportMergeSymbolError(target, source *ast.Symbol)</c>。</summary>
    internal void ReportMergeSymbolError(Symbol target, Symbol source)
    {
        var isEitherEnum = (target.Flags & SymbolFlags.Enum) != 0 || (source.Flags & SymbolFlags.Enum) != 0;
        var isEitherBlockScoped = (target.Flags & SymbolFlags.BlockScopedVariable) != 0 || (source.Flags & SymbolFlags.BlockScopedVariable) != 0;
        Message message;
        if (isEitherEnum)
        {
            message = Enum_declarations_can_only_merge_with_namespace_or_other_enum_declarations;
        }
        else if (isEitherBlockScoped)
        {
            message = Cannot_redeclare_block_scoped_variable_0;
        }
        else
        {
            message = Duplicate_identifier_0;
        }

        var sourceSymbolFile = GetSourceFileOfNode(GetFirstDeclaration(source));
        var targetSymbolFile = GetSourceFileOfNode(GetFirstDeclaration(target));
        var isSourcePlainJS = IsPlainJSFile(sourceSymbolFile, _compilerOptions.CheckJs);
        var isTargetPlainJS = IsPlainJSFile(targetSymbolFile, _compilerOptions.CheckJs);
        var symbolName = SymbolToStringWorker(source);
        if (!isSourcePlainJS)
        {
            AddDuplicateDeclarationErrorsForSymbols(source, message, symbolName, target);
        }

        if (!isTargetPlainJS)
        {
            AddDuplicateDeclarationErrorsForSymbols(target, message, symbolName, source);
        }
    }

    /// <summary>Go: <c>func (c *Checker) addDuplicateDeclarationErrorsForSymbols(target *ast.Symbol, message *diagnostics.Message, symbolName string, source *ast.Symbol)</c>。</summary>
    internal void AddDuplicateDeclarationErrorsForSymbols(Symbol target, Message message, string symbolName, Symbol source)
    {
        // Go: len(nil) == 0，range nil 不迭代；C# 的 Node[]? 必须判空。
        foreach (var node in target.Declarations ?? [])
        {
            AddDuplicateDeclarationError(node, message, symbolName, source.Declarations);
        }
    }

    /// <summary>Go: <c>func (c *Checker) addDuplicateDeclarationError(node *ast.Node, message *diagnostics.Message, symbolName string, relatedNodes []*ast.Node)</c>。</summary>
    internal void AddDuplicateDeclarationError(Node node, Message message, string symbolName, Node[]? relatedNodes)
    {
        var errorNode = GetAdjustedNodeForError(node);
        errorNode ??= node;
        var err = LookupOrIssueError(errorNode, message, symbolName);
        // Go: range nil 切片不迭代。
        foreach (var relatedNode in relatedNodes ?? [])
        {
            var adjustedNode = GetAdjustedNodeForError(relatedNode);
            if (adjustedNode == errorNode)
            {
                continue;
            }

            var leadingMessage = CreateDiagnosticForNode(adjustedNode!, X_0_was_also_declared_here, symbolName);
            var followOnMessage = CreateDiagnosticForNode(adjustedNode!, X_and_here);
            // Go: RelatedInformation() 可为 nil 切片，len(nil) == 0、core.Some(nil, …) == false。
            var relatedInformation = err.RelatedInformation();
            var relatedInformationLength = relatedInformation?.Length ?? 0;
            if (relatedInformationLength >= 5 || Some(relatedInformation, d =>
                CompareDiagnostics(d, followOnMessage) == 0 || CompareDiagnostics(d, leadingMessage) == 0))
            {
                continue;
            }

            if (relatedInformationLength == 0)
            {
                err.AddRelatedInfo(leadingMessage);
            }
            else
            {
                err.AddRelatedInfo(followOnMessage);
            }
        }
    }

    /// <summary>Go（包级）: <c>func createDiagnosticForNode(node *ast.Node, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>。</summary>
    internal static Diagnostic CreateDiagnosticForNode(Node node, Message message, params object?[] args)
    {
        return NewDiagnosticForNode(node, message, args);
    }

    /// <summary>Go（包级）: <c>func getAdjustedNodeForError(node *ast.Node) *ast.Node</c>。</summary>
    internal static Node? GetAdjustedNodeForError(Node node)
    {
        var name = GetNameOfDeclaration(node);
        if (name is not null)
        {
            return name;
        }

        return node;
    }

    /// <summary>Go: <c>func (c *Checker) lookupOrIssueError(location *ast.Node, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>。</summary>
    internal Diagnostic LookupOrIssueError(Node location, Message message, params object?[] args)
    {
        return AddDiagnostic(NewDiagnosticForNode(location, message, args));
    }

    /// <summary>Go（包级）: <c>func getFirstDeclaration(symbol *ast.Symbol) *ast.Node</c>。</summary>
    internal static Node? GetFirstDeclaration(Symbol symbol)
    {
        // Go: `if len(symbol.Declarations) > 0 { return symbol.Declarations[0] }`（nil 切片长度为 0）。
        var declarations = symbol.Declarations;
        if (declarations is { Length: > 0 })
        {
            return declarations[0];
        }

        return null;
    }

    /// <summary>Go（包级）: <c>func getExcludedSymbolFlags(flags ast.SymbolFlags) ast.SymbolFlags</c>。</summary>
    internal static SymbolFlags GetExcludedSymbolFlags(SymbolFlags flags)
    {
        var result = SymbolFlags.None;
        if ((flags & SymbolFlags.BlockScopedVariable) != 0)
        {
            result |= SymbolFlags.BlockScopedVariableExcludes;
        }

        if ((flags & SymbolFlags.FunctionScopedVariable) != 0)
        {
            result |= SymbolFlags.FunctionScopedVariableExcludes;
        }

        if ((flags & SymbolFlags.Property) != 0)
        {
            result |= SymbolFlags.PropertyExcludes;
        }

        if ((flags & SymbolFlags.EnumMember) != 0)
        {
            result |= SymbolFlags.EnumMemberExcludes;
        }

        if ((flags & SymbolFlags.Function) != 0)
        {
            result |= SymbolFlags.FunctionExcludes;
        }

        if ((flags & SymbolFlags.Class) != 0)
        {
            result |= SymbolFlags.ClassExcludes;
        }

        if ((flags & SymbolFlags.Interface) != 0)
        {
            result |= SymbolFlags.InterfaceExcludes;
        }

        if ((flags & SymbolFlags.RegularEnum) != 0)
        {
            result |= SymbolFlags.RegularEnumExcludes;
        }

        if ((flags & SymbolFlags.ConstEnum) != 0)
        {
            result |= SymbolFlags.ConstEnumExcludes;
        }

        if ((flags & SymbolFlags.ValueModule) != 0)
        {
            result |= SymbolFlags.ValueModuleExcludes;
        }

        if ((flags & SymbolFlags.Method) != 0)
        {
            result |= SymbolFlags.MethodExcludes;
        }

        if ((flags & SymbolFlags.GetAccessor) != 0)
        {
            result |= SymbolFlags.GetAccessorExcludes;
        }

        if ((flags & SymbolFlags.SetAccessor) != 0)
        {
            result |= SymbolFlags.SetAccessorExcludes;
        }

        if ((flags & SymbolFlags.TypeParameter) != 0)
        {
            result |= SymbolFlags.TypeParameterExcludes;
        }

        if ((flags & SymbolFlags.TypeAlias) != 0)
        {
            result |= SymbolFlags.TypeAliasExcludes;
        }

        if ((flags & SymbolFlags.Alias) != 0)
        {
            result |= SymbolFlags.AliasExcludes;
        }

        if ((flags & SymbolFlags.ReplaceableByMethod) != 0)
        {
            result &= ~SymbolFlags.Method;
        }

        return result;
    }
}
