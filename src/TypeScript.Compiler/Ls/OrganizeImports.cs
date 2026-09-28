// ported from: internal/ls/organizeimports.go:1-957
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级类型 `organizeImportsComparerSettings` / `categorizedImports` / `importGroup` / `categorizedExports` →
//    本文件的 `internal sealed class OrganizeImportsComparerSettings` / `CategorizedImports` / `ImportGroup` /
//    `CategorizedExports`（Go 未导出字段 → `_camelCase`）。Go 的 `importGroup` 是值类型但会被 `&regularImports`
//    取址后原地追加 ⇒ C# 用 class（引用语义），`getCategorizedImports` 里直接改字段即等价。
//    `importGroup.isEmpty()` → `ImportGroup.IsEmpty()`（C# 方法必须 PascalCase，IDE1006 是错误级）。
//
// 2) 包级函数 → 静态类 `Ls` 的 `internal static` 成员（PascalCase、不加 `_`）：`OrganizeImportsWorker` /
//    `GroupByModuleSpecifier` / `RemoveUnusedImports` / `FilterUsedImportSpecifiers` /
//    `HasModuleDeclarationMatchingSpecifier` / `GetImportAttributesKey` / `GroupByNewlineContiguous` / `IsNewGroup` /
//    `CoalesceImportsWorker` / `GetCategorizedImports` / `GetNewImportSpecifiers` / `TryGetNamedBindingElements` /
//    `GetTopLevelExportGroups` / `OrganizeExportsWorker` / `CoalesceExportsWorker` / `GetCategorizedExports`。
//    本文件**不** `using static TypeScript.Ls.Ls;`（避免与 `Ast` 的 8 个同名静态成员二义，见
//    artifacts/ls-g1-helpers.md §4.10）⇒ 调用点显式写 `Ls.Xxx(...)`；`Ast` 侧的同名成员写 `Ast.Xxx(...)`。
//
// 3) Go 的切片（`[]*ast.Statement`）→ C# `List<Node>` / `Node[]`：只读遍历用 `Node[]`，会被 `append`/`SortFunc`
//    改动的用 `List<Node>`。Go 遍历 nil 切片不迭代 ⇒ `?? []`（`NodeList.Nodes` 是 `Node[]?`）。
//    `slices.SortStableFunc` → LINQ `OrderBy`（稳定）；`slices.SortFunc` → `List<T>.Sort`（都不稳定，与 Go 一致）。
//
// 4) `change.NewTracker(...)` → `Tracker.NewTracker(...)`（`TypeScript.LsChange` 既是命名空间又有同名静态类，
//    写 `LsChange.NewTracker` 会先绑到命名空间 ⇒ 用 `Tracker` 类名，先例 `LsAutoImport/Fix.cs:108`）。
//    `changeTracker.SetEmitFlags/AddEmitFlags` 是 Go 里 `*printer.EmitContext` 的提升方法 ⇒ C# 显式
//    `changeTracker.EmitContext.SetEmitFlags(node, EmitFlags.X)`（`Tracker.EmitContext` 是 public 字段）。
//
// 5) `printer.EFNoLeadingComments` → `EmitFlags.NoLeadingComments`；`printer.EFMultiLine` → `EmitFlags.MultiLine`
//    （`Printer/EmitFlags.cs` 的映射：成员名 = Go 常量名去掉 `EF`）。
//
// 6) `ast.NewNodeFactory(ast.NodeFactoryHooks{})` → `new NodeFactory()`（先例 `Ls/SelectionRanges.cs:249`）。
//    `factory.UpdateXxx(...)` 在 C# 生成物里**已经返回 `Node`** （Go 返回具体类型再 `.AsNode()`）⇒ 少一步。
//
// 7) `(*ast.Statement).AsNode()` 是恒等（Go 里 `Statement = Node` 类型别名）⇒ C# 直接用节点本身，
//    `core.Map(newImportDecls, s => s.AsNode())` 也就等价于直接取列表。
//
// 8) `clause.IsTypeOnly()` 是 `Node` 上的方法（`Clause.PhaseModifier == KindTypeKeyword`）；
//    `export.IsTypeOnly` 是 `ExportDeclaration` 的**字段**（`Ast/SubtreeFactsOverrides.cs` 的说明）。
//
// 9) `checker.IsDeclarationUsed(sourceFile, identifier, …)` 的第二个形参在 C# 是 `Identifier`（非 `Node`）⇒
//    `name.AsIdentifier()!`（Go 的 `name.AsIdentifier()` 在类型不符时 panic）。
//
// 10) `strings.Builder` → `StringBuilder`；`stringutil.CompareStringsCaseSensitive` → `CompareStringsCaseSensitive`
//    （`using static TypeScript.StringUtil.StringUtil;`）。
//
// 11) `ast.KindImportDeclaration` 等 → `Kind.ImportDeclaration`；`ast.NewLineTrivia` → `Kind.NewLineTrivia`；
//    `scanner.NewScanner()` → `NewScanner()`（`using static TypeScript.Scanner.Scanner;`）；
//    `s.TokenStart()` 在 C# 是**方法**（`Scanner.TokenStart()`）；`s.SetText(string)`。
//
// 12) `scanner.GetTokenPosOfNode(element, configFile, false)` 不在本文件（见 `Ls/FileRename.cs`）。
//
// 13) 深递归：本文件不递归（只有列表迭代）⇒ 无需大栈线程。
using System.Text;
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsChange;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Printer;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using Kind = TypeScript.Ast.Kind;
using LsUtilClass = TypeScript.LsUtil.LsUtil;
using NodeFactory = TypeScript.Ast.NodeFactory;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Tracker = TypeScript.LsChange.Tracker;

namespace TypeScript.Ls;

/// <summary>Go: <c>type organizeImportsComparerSettings struct</c>（organizeimports.go:166-170）。</summary>
internal sealed class OrganizeImportsComparerSettings
{
    internal Func<string, string, int> _moduleSpecifierComparer = null!;
    internal Func<string, string, int> _namedImportComparer = null!;
    internal OrganizeImportsTypeOrder _typeOrder;
}

/// <summary>Go: <c>type categorizedImports struct</c>（organizeimports.go:650-654）。</summary>
internal sealed class CategorizedImports
{
    internal Node? _importWithoutClause;
    internal ImportGroup _typeOnlyImports = new();
    internal ImportGroup _regularImports = new();
}

/// <summary>Go: <c>type importGroup struct</c>（organizeimports.go:656-660）。</summary>
internal sealed class ImportGroup
{
    internal List<Node> _defaultImports = [];
    internal List<Node> _namespaceImports = [];
    internal List<Node> _namedImports = [];

    /// <summary>Go: <c>func (g importGroup) isEmpty() bool</c>（organizeimports.go:662-664）。</summary>
    internal bool IsEmpty()
    {
        return _defaultImports.Count == 0 && _namespaceImports.Count == 0 && _namedImports.Count == 0;
    }
}

/// <summary>Go: <c>type categorizedExports struct</c>（organizeimports.go:915-919）。</summary>
internal sealed class CategorizedExports
{
    internal Node? _exportWithoutClause;
    internal List<Node> _namedExports = [];
    internal List<Node> _typeOnlyExports = [];
}

public static partial class Ls
{
    /// <summary>Go: <c>func organizeImportsWorker(...)</c>（organizeimports.go:172-263）。</summary>
    internal static void OrganizeImportsWorker(
        List<Node> oldImportDecls,
        OrganizeImportsComparerSettings comparer,
        bool shouldSort,
        bool shouldCombine,
        bool shouldRemove,
        SourceFile sourceFile,
        Program program,
        Tracker changeTracker,
        Context ctx)
    {
        if (oldImportDecls.Count == 0)
        {
            return;
        }

        // Header comment preservation is handled via LeadingTriviaOptionExclude in the change tracker below

        var processedImports = new List<Node>(oldImportDecls);
        if (shouldRemove)
        {
            var (typeChecker, done) = program.GetTypeCheckerForFile(ctx, sourceFile);
            try
            {
                processedImports = RemoveUnusedImports(processedImports, sourceFile, typeChecker, program, changeTracker);
            }
            finally
            {
                done();
            }
        }

        List<Node> newImportDecls;
        if (shouldCombine)
        {
            var grouped = GroupByModuleSpecifier(processedImports);
            if (shouldSort)
            {
                grouped.Sort((a, b) =>
                {
                    if (a.Count == 0 || b.Count == 0)
                    {
                        return 0;
                    }

                    return CompareModuleSpecifiers(
                        a[0].ModuleSpecifier(),
                        b[0].ModuleSpecifier(),
                        comparer._moduleSpecifierComparer);
                });
            }

            var specifierComparer = GetNamedImportSpecifierComparer(
                new UserPreferences { OrganizeImportsTypeOrder = comparer._typeOrder },
                comparer._namedImportComparer);

            newImportDecls = [];
            foreach (var importGroup in grouped)
            {
                var coalesced = CoalesceImportsWorker(importGroup, comparer._moduleSpecifierComparer, specifierComparer, sourceFile, changeTracker);
                if (shouldSort)
                {
                    coalesced.Sort((a, b) => CompareImportsOrRequireStatements(a, b, comparer._moduleSpecifierComparer));
                }

                newImportDecls.AddRange(coalesced);
            }
        }
        else
        {
            newImportDecls = processedImports;
        }

        if (shouldSort && !shouldCombine)
        {
            newImportDecls.Sort((a, b) => CompareImportsOrRequireStatements(a, b, comparer._moduleSpecifierComparer));
        }

        if (newImportDecls.Count == 0)
        {
            changeTracker.DeleteNodeRange(
                sourceFile,
                oldImportDecls[0],
                oldImportDecls[^1],
                LeadingTriviaOption.Exclude, // Preserve header comment
                TrailingTriviaOption.Include);
        }
        else
        {
            foreach (var imp in newImportDecls)
            {
                changeTracker.EmitContext.SetEmitFlags(imp, EmitFlags.NoLeadingComments);
            }

            var options = new NodeOptions
            {
                LeadingTriviaOption = TypeScript.LsChange.LeadingTriviaOption.Exclude, // Preserve header comment
                TrailingTriviaOption = TypeScript.LsChange.TrailingTriviaOption.Include,
                Suffix = "\n",
            };

            changeTracker.ReplaceNodeWithNodes(sourceFile, oldImportDecls[0], newImportDecls.ToArray(), options);

            if (oldImportDecls.Count > 1)
            {
                for (var i = 1; i < oldImportDecls.Count; i++)
                {
                    changeTracker.Delete(sourceFile, oldImportDecls[i]);
                }
            }
        }
    }

    /// <summary>Go: <c>func groupByModuleSpecifier(imports []*ast.Statement) [][]*ast.Statement</c>（organizeimports.go:265-283）。</summary>
    internal static List<List<Node>> GroupByModuleSpecifier(List<Node> imports)
    {
        var groups = new Dictionary<string, List<Node>>();
        var order = new List<string>();

        foreach (var imp in imports)
        {
            var specifier = LsUtilClass.GetExternalModuleName(imp.ModuleSpecifier());
            if (!groups.ContainsKey(specifier))
            {
                order.Add(specifier);
            }

            if (!groups.TryGetValue(specifier, out var list))
            {
                groups[specifier] = list = [];
            }

            list.Add(imp);
        }

        var result = new List<List<Node>>(order.Count);
        foreach (var key in order)
        {
            result.Add(groups[key]);
        }

        return result;
    }

    /// <summary>Go: <c>func removeUnusedImports(...)</c>（organizeimports.go:285-375）。</summary>
    internal static List<Node> RemoveUnusedImports(
        List<Node> oldImports,
        SourceFile sourceFile,
        TypeScript.Checker.Checker typeChecker,
        Program program,
        Tracker changeTracker)
    {
        var compilerOptions = program.Options();
        var jsxElementsPresent = (sourceFile.SubtreeFacts & SubtreeFacts.ContainsJsx) != 0;
        var jsxModeNeedsExplicitImport = compilerOptions.Jsx == JsxEmit.React || compilerOptions.Jsx == JsxEmit.ReactNative;

        var factory = new NodeFactory();
        var usedImports = new List<Node>(oldImports.Count);

        foreach (var importDecl in oldImports)
        {
            var importClause = importDecl.AsImportDeclaration()!.ImportClause;
            if (importClause is null)
            {
                usedImports.Add(importDecl);
                continue;
            }

            var clause = importClause.AsImportClause()!;
            var name = clause.Name();
            var namedBindings = clause.NamedBindings;

            if (name is not null && !typeChecker.IsDeclarationUsed(sourceFile, name.AsIdentifier()!, jsxElementsPresent, jsxModeNeedsExplicitImport))
            {
                name = null;
            }

            if (namedBindings is not null)
            {
                switch (namedBindings.Kind)
                {
                    case Kind.NamespaceImport:
                        var nsImport = namedBindings.AsNamespaceImport()!;
                        if (!typeChecker.IsDeclarationUsed(sourceFile, nsImport.Name()!.AsIdentifier()!, jsxElementsPresent, jsxModeNeedsExplicitImport))
                        {
                            namedBindings = null;
                        }

                        break;
                    case Kind.NamedImports:
                        var namedImports = namedBindings.AsNamedImports()!;
                        var originalBindings = namedBindings;
                        var newElements = FilterUsedImportSpecifiers(namedImports.Elements!.Nodes ?? [], typeChecker, sourceFile, jsxElementsPresent, jsxModeNeedsExplicitImport);
                        if (newElements.Count == 0)
                        {
                            namedBindings = null;
                        }
                        else if (newElements.Count < (namedImports.Elements!.Nodes ?? []).Length)
                        {
                            var newList = factory.NewNodeList(newElements.ToArray());
                            var updatedNamedImports = factory.UpdateNamedImports(namedImports, newList);
                            namedBindings = updatedNamedImports;
                        }

                        if (namedBindings is not null && !NodeIsSynthesized(originalBindings) && !RangeIsOnSingleLine(originalBindings.Loc, sourceFile))
                        {
                            changeTracker.EmitContext.SetEmitFlags(namedBindings, EmitFlags.MultiLine);
                        }

                        break;
                }
            }

            if (name is not null || namedBindings is not null)
            {
                var importDeclNode = importDecl.AsImportDeclaration()!;
                var newClause = factory.UpdateImportClause(clause, clause.PhaseModifier, name, namedBindings);
                var newImportDecl = factory.UpdateImportDeclaration(
                    importDeclNode,
                    importDeclNode.Modifiers(),
                    newClause,
                    importDeclNode.ModuleSpecifier,
                    importDeclNode.Attributes);
                usedImports.Add(newImportDecl);
            }
            else
            {
                var moduleSpecifier = importDecl.ModuleSpecifier();
                if (HasModuleDeclarationMatchingSpecifier(sourceFile, moduleSpecifier))
                {
                    if (sourceFile.IsDeclarationFile)
                    {
                        var importDeclNode = importDecl.AsImportDeclaration()!;
                        var newImportDecl = factory.UpdateImportDeclaration(
                            importDeclNode,
                            importDeclNode.Modifiers(),
                            null, // no import clause
                            importDeclNode.ModuleSpecifier,
                            importDeclNode.Attributes);
                        usedImports.Add(newImportDecl);
                    }
                    else
                    {
                        usedImports.Add(importDecl);
                    }
                }
            }
        }

        return usedImports;
    }

    /// <summary>Go: <c>func filterUsedImportSpecifiers(...)</c>（organizeimports.go:377-392）。</summary>
    internal static List<Node> FilterUsedImportSpecifiers(
        Node[] elements,
        TypeScript.Checker.Checker typeChecker,
        SourceFile sourceFile,
        bool jsxElementsPresent,
        bool jsxModeNeedsExplicitImport)
    {
        var result = new List<Node>();
        foreach (var elem in elements)
        {
            var spec = elem.AsImportSpecifier()!;
            if (typeChecker.IsDeclarationUsed(sourceFile, spec.Name()!.AsIdentifier()!, jsxElementsPresent, jsxModeNeedsExplicitImport))
            {
                result.Add(elem);
            }
        }

        return result;
    }

    /// <summary>Go: <c>func hasModuleDeclarationMatchingSpecifier(sourceFile *ast.SourceFile, moduleSpecifier *ast.Expression) bool</c>（organizeimports.go:394-408）。</summary>
    internal static bool HasModuleDeclarationMatchingSpecifier(SourceFile sourceFile, Node? moduleSpecifier)
    {
        if (moduleSpecifier is null || !IsStringLiteral(moduleSpecifier))
        {
            return false;
        }

        var moduleSpecifierText = moduleSpecifier.Text();

        foreach (var moduleName in sourceFile.ModuleAugmentations ?? [])
        {
            if (IsStringLiteral(moduleName) && moduleName.Text() == moduleSpecifierText)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func getImportAttributesKey(attributes *ast.ImportAttributesNode) string</c>（organizeimports.go:410-442）：returns a key for grouping imports by their attributes.</summary>
    internal static string GetImportAttributesKey(Node? attributes)
    {
        if (attributes is null)
        {
            return "";
        }

        var importAttrs = attributes.AsImportAttributes()!;
        var key = new StringBuilder();
        key.Append(importAttrs.Token.String());
        key.Append(' ');

        var attrNodes = new Node[(importAttrs.Attributes!.Nodes ?? []).Length];
        Array.Copy(importAttrs.Attributes!.Nodes ?? [], attrNodes, attrNodes.Length);
        Array.Sort(attrNodes, (a, b) =>
        {
            var aName = a.AsImportAttribute()!.Name()!.Text();
            var bName = b.AsImportAttribute()!.Name()!.Text();
            return CompareStringsCaseSensitive(aName, bName);
        });

        foreach (var attrNode in attrNodes)
        {
            var attr = attrNode.AsImportAttribute()!;
            key.Append(attr.Name()!.Text());
            key.Append(':');
            if (IsStringLiteralLike(attr.Value))
            {
                key.Append('"');
                key.Append(attr.Value!.Text());
                key.Append('"');
            }
            else
            {
                key.Append(attr.Value!.Text());
            }

            key.Append(' ');
        }

        return key.ToString();
    }

    /// <summary>Go: <c>func groupByNewlineContiguous(sourceFile *ast.SourceFile, decls []*ast.Statement) [][]*ast.Statement</c>（organizeimports.go:444-465）：groups declarations by blank lines between them.</summary>
    internal static List<List<Node>> GroupByNewlineContiguous(SourceFile sourceFile, Node[] decls)
    {
        var s = NewScanner();
        s.SetSkipTrivia(false); // Must not skip trivia to detect newlines
        var groups = new List<List<Node>>();
        List<Node>? currentGroup = null;

        foreach (var decl in decls)
        {
            if (currentGroup is { Count: > 0 } && IsNewGroup(sourceFile, decl, s))
            {
                groups.Add(currentGroup);
                currentGroup = null;
            }

            (currentGroup ??= []).Add(decl);
        }

        if (currentGroup is { Count: > 0 })
        {
            groups.Add(currentGroup);
        }

        return groups;
    }

    /// <summary>Go: <c>func isNewGroup(sourceFile *ast.SourceFile, decl *ast.Statement, s *scanner.Scanner) bool</c>（organizeimports.go:467-497）。</summary>
    private static bool IsNewGroup(SourceFile sourceFile, Node decl, Scanner.Scanner s)
    {
        var fullStart = decl.Pos();
        if (fullStart < 0)
        {
            return false;
        }

        var text = sourceFile.Text();
        var textLen = text.Length;

        if (fullStart >= textLen)
        {
            return false;
        }

        var startPos = SkipTrivia(text, fullStart);
        if (startPos <= fullStart)
        {
            return false;
        }

        var triviaLen = startPos - fullStart;
        s.SetText(text.Substring(fullStart, startPos - fullStart));

        var numberOfNewLines = 0;
        while (s.TokenStart() < triviaLen)
        {
            var tokenKind = s.Scan();
            if (tokenKind == Kind.NewLineTrivia)
            {
                numberOfNewLines++;
                if (numberOfNewLines >= 2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Go: <c>func coalesceImportsWorker(...)</c>（organizeimports.go:499-648）。</summary>
    internal static List<Node> CoalesceImportsWorker(
        List<Node> importDecls,
        Func<string, string, int> comparer,
        Func<Node, Node, int> specifierComparer,
        SourceFile? sourceFile,
        Tracker changeTracker)
    {
        if (importDecls.Count == 0)
        {
            return importDecls;
        }

        var importGroupsByAttributes = new Dictionary<string, List<Node>>();
        var attributeKeys = new List<string>();

        foreach (var importDecl in importDecls)
        {
            var key = GetImportAttributesKey(importDecl.AsImportDeclaration()!.Attributes);
            if (!importGroupsByAttributes.ContainsKey(key))
            {
                attributeKeys.Add(key);
            }

            if (!importGroupsByAttributes.TryGetValue(key, out var list))
            {
                importGroupsByAttributes[key] = list = [];
            }

            list.Add(importDecl);
        }

        var coalescedImports = new List<Node>();

        foreach (var attributeKey in attributeKeys)
        {
            var importGroupSameAttrs = importGroupsByAttributes[attributeKey];
            var categorized = GetCategorizedImports(importGroupSameAttrs);

            if (categorized._importWithoutClause is not null)
            {
                coalescedImports.Add(categorized._importWithoutClause);
            }

            var factory = new NodeFactory();

            ImportGroup[] groups = [categorized._regularImports, categorized._typeOnlyImports];
            for (var i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (group.IsEmpty())
                {
                    continue;
                }

                var isTypeOnly = i == 1;

                if (!isTypeOnly && group._defaultImports.Count == 1 && group._namespaceImports.Count == 1 && group._namedImports.Count == 0)
                {
                    var defaultImport = group._defaultImports[0];
                    var namespaceImport = group._namespaceImports[0];

                    var defaultClause = defaultImport.AsImportDeclaration()!.ImportClause!.AsImportClause()!;
                    var namespaceBindings = namespaceImport.AsImportDeclaration()!.ImportClause!.AsImportClause()!.NamedBindings;

                    var newClause = factory.UpdateImportClause(defaultClause, defaultClause.PhaseModifier, defaultClause.Name(), namespaceBindings);
                    var defaultDeclNode = defaultImport.AsImportDeclaration()!;
                    var newImportDecl = factory.UpdateImportDeclaration(
                        defaultDeclNode,
                        defaultDeclNode.Modifiers(),
                        newClause,
                        defaultDeclNode.ModuleSpecifier,
                        defaultDeclNode.Attributes);
                    coalescedImports.Add(newImportDecl);
                    continue;
                }

                group._namespaceImports.Sort((a, b) =>
                {
                    var n1 = a.AsImportDeclaration()!.ImportClause!.AsImportClause()!.NamedBindings!.AsNamespaceImport()!.Name()!;
                    var n2 = b.AsImportDeclaration()!.ImportClause!.AsImportClause()!.NamedBindings!.AsNamespaceImport()!.Name()!;
                    return comparer(n1.Text(), n2.Text());
                });

                foreach (var nsImport in group._namespaceImports)
                {
                    var nsImportDecl = nsImport.AsImportDeclaration()!;
                    var clause = nsImportDecl.ImportClause!.AsImportClause()!;
                    var newClause = factory.UpdateImportClause(clause, clause.PhaseModifier, null, clause.NamedBindings);
                    var newImportDecl = factory.UpdateImportDeclaration(
                        nsImportDecl,
                        nsImportDecl.Modifiers(),
                        newClause,
                        nsImportDecl.ModuleSpecifier,
                        nsImportDecl.Attributes);
                    coalescedImports.Add(newImportDecl);
                }

                Node? firstDefaultImport = null;
                Node? firstNamedImport = null;

                if (group._defaultImports.Count > 0)
                {
                    firstDefaultImport = group._defaultImports[0];
                }

                if (group._namedImports.Count > 0)
                {
                    firstNamedImport = group._namedImports[0];
                }

                var importDecl = firstDefaultImport;
                importDecl ??= firstNamedImport;
                if (importDecl is null)
                {
                    continue;
                }

                Node? newDefaultImport = null;
                List<Node> newImportSpecifiers = [];

                if (group._defaultImports.Count == 1)
                {
                    newDefaultImport = group._defaultImports[0].AsImportDeclaration()!.ImportClause!.AsImportClause()!.Name();
                }
                else
                {
                    foreach (var defaultImport in group._defaultImports)
                    {
                        var defaultClause = defaultImport.AsImportDeclaration()!.ImportClause!.AsImportClause()!;
                        var defaultName = defaultClause.Name();
                        var propertyName = factory.NewIdentifier("default");
                        var importSpec = factory.NewImportSpecifier(false, propertyName, defaultName);
                        newImportSpecifiers.Add(importSpec);
                    }
                }

                newImportSpecifiers.AddRange(GetNewImportSpecifiers(group._namedImports, factory));
                newImportSpecifiers = [.. newImportSpecifiers.OrderBy(x => x, Comparer<Node>.Create((a, b) => specifierComparer(a, b)))];

                Node? newNamedImports;
                if (newImportSpecifiers.Count == 0)
                {
                    if (newDefaultImport is not null)
                    {
                        newNamedImports = null;
                    }
                    else
                    {
                        newNamedImports = factory.NewNamedImports(factory.NewNodeList(null));
                    }
                }
                else
                {
                    var sortedList = factory.NewNodeList(newImportSpecifiers.ToArray());
                    if (firstNamedImport is not null)
                    {
                        var firstNamedBindings = firstNamedImport.AsImportDeclaration()!.ImportClause!.AsImportClause()!.NamedBindings!.AsNamedImports()!;
                        var originalElements = firstNamedBindings.Elements!;
                        if (originalElements.HasTrailingComma())
                        {
                            sortedList.Loc = originalElements.Loc;
                        }

                        newNamedImports = factory.UpdateNamedImports(firstNamedBindings, sortedList);
                    }
                    else
                    {
                        newNamedImports = factory.NewNamedImports(sortedList);
                    }
                }

                if (sourceFile is not null && newNamedImports is not null && firstNamedImport is not null)
                {
                    var firstNamedBindings = firstNamedImport.AsImportDeclaration()!.ImportClause!.AsImportClause()!.NamedBindings!;
                    if (!NodeIsSynthesized(firstNamedBindings) && !RangeIsOnSingleLine(firstNamedBindings.Loc, sourceFile))
                    {
                        changeTracker.EmitContext.SetEmitFlags(newNamedImports, EmitFlags.MultiLine);
                    }
                }

                if (isTypeOnly && newDefaultImport is not null && newNamedImports is not null)
                {
                    var importDeclNode = importDecl.AsImportDeclaration()!;

                    var defaultClause = factory.NewImportClause(importDeclNode.ImportClause!.AsImportClause()!.PhaseModifier, newDefaultImport, null);
                    var defaultImportDecl = factory.UpdateImportDeclaration(
                        importDeclNode,
                        importDeclNode.Modifiers(),
                        defaultClause,
                        importDeclNode.ModuleSpecifier,
                        importDeclNode.Attributes);
                    coalescedImports.Add(defaultImportDecl);

                    var namedDeclNode = firstNamedImport;
                    namedDeclNode ??= importDecl;
                    var namedImportDeclNode = namedDeclNode.AsImportDeclaration()!;
                    var namedClause = factory.NewImportClause(namedImportDeclNode.ImportClause!.AsImportClause()!.PhaseModifier, null, newNamedImports);
                    var namedImportDecl = factory.UpdateImportDeclaration(
                        namedImportDeclNode,
                        namedImportDeclNode.Modifiers(),
                        namedClause,
                        namedImportDeclNode.ModuleSpecifier,
                        namedImportDeclNode.Attributes);
                    coalescedImports.Add(namedImportDecl);
                }
                else
                {
                    var importDeclNode = importDecl.AsImportDeclaration()!;
                    var clauseNode = importDeclNode.ImportClause!.AsImportClause()!;
                    var newClause = factory.UpdateImportClause(clauseNode, clauseNode.PhaseModifier, newDefaultImport, newNamedImports);
                    var newImportDecl = factory.UpdateImportDeclaration(
                        importDeclNode,
                        importDeclNode.Modifiers(),
                        newClause,
                        importDeclNode.ModuleSpecifier,
                        importDeclNode.Attributes);
                    coalescedImports.Add(newImportDecl);
                }
            }
        }

        return coalescedImports;
    }

    /// <summary>Go: <c>func getCategorizedImports(importDecls []*ast.Statement) categorizedImports</c>（organizeimports.go:666-700）。</summary>
    internal static CategorizedImports GetCategorizedImports(List<Node> importDecls)
    {
        Node? importWithoutClause = null;
        var typeOnlyImports = new ImportGroup();
        var regularImports = new ImportGroup();

        foreach (var importDecl in importDecls)
        {
            if (importDecl.AsImportDeclaration()!.ImportClause is null)
            {
                importWithoutClause ??= importDecl;
                continue;
            }

            var clause = importDecl.AsImportDeclaration()!.ImportClause!.AsImportClause()!;
            var group = clause.IsTypeOnly() ? typeOnlyImports : regularImports;

            var name = clause.Name();
            var namedBindings = clause.NamedBindings;

            if (name is not null)
            {
                group._defaultImports.Add(importDecl);
            }

            if (namedBindings is not null)
            {
                switch (namedBindings.Kind)
                {
                    case Kind.NamespaceImport:
                        group._namespaceImports.Add(importDecl);
                        break;
                    case Kind.NamedImports:
                        group._namedImports.Add(importDecl);
                        break;
                }
            }
        }

        return new CategorizedImports
        {
            _importWithoutClause = importWithoutClause,
            _typeOnlyImports = typeOnlyImports,
            _regularImports = regularImports,
        };
    }

    /// <summary>Go: <c>func getNewImportSpecifiers(namedImports []*ast.Statement, factory *ast.NodeFactory) []*ast.Node</c>（organizeimports.go:702-731）。</summary>
    internal static List<Node> GetNewImportSpecifiers(List<Node> namedImports, NodeFactory factory)
    {
        var result = new List<Node>();

        foreach (var namedImport in namedImports)
        {
            var elements = TryGetNamedBindingElements(namedImport);
            if (elements is null)
            {
                continue;
            }

            foreach (var elem in elements)
            {
                var spec = elem.AsImportSpecifier()!;

                if (spec.PropertyName is not null && spec.Name() is not null)
                {
                    var propertyText = spec.PropertyName.Text();
                    var nameText = spec.Name()!.Text();

                    if (propertyText == nameText)
                    {
                        var normalized = factory.UpdateImportSpecifier(spec, spec.IsTypeOnly, null, spec.Name());
                        result.Add(normalized);
                        continue;
                    }
                }

                result.Add(elem);
            }
        }

        return result;
    }

    /// <summary>Go: <c>func tryGetNamedBindingElements(namedImport *ast.Statement) []*ast.Statement</c>（organizeimports.go:733-751）。</summary>
    internal static Node[]? TryGetNamedBindingElements(Node namedImport)
    {
        if (namedImport.Kind != Kind.ImportDeclaration)
        {
            return null;
        }

        var importDecl = namedImport.AsImportDeclaration()!;
        if (importDecl.ImportClause is null)
        {
            return null;
        }

        var clause = importDecl.ImportClause.AsImportClause()!;
        var namedBindings = clause.NamedBindings;

        if (namedBindings is not null && namedBindings.Kind == Kind.NamedImports)
        {
            var namedImportsNode = namedBindings.AsNamedImports()!;
            return namedImportsNode.Elements!.Nodes;
        }

        return null;
    }

    /// <summary>Go: <c>func getTopLevelExportGroups(sourceFile *ast.SourceFile) [][]*ast.Statement</c>（organizeimports.go:753-791）。</summary>
    internal static List<List<Node>> GetTopLevelExportGroups(SourceFile sourceFile)
    {
        var topLevelExportGroups = new List<List<Node>>();
        var statements = sourceFile.Statements!.Nodes ?? [];
        var statementsLen = statements.Length;

        var i = 0;
        var groupIndex = 0;
        while (i < statementsLen)
        {
            if (statements[i].Kind == Kind.ExportDeclaration)
            {
                if (groupIndex >= topLevelExportGroups.Count)
                {
                    topLevelExportGroups.Add([]);
                }

                var exportDecl = statements[i].AsExportDeclaration()!;
                if (exportDecl.ModuleSpecifier is not null)
                {
                    topLevelExportGroups[groupIndex].Add(statements[i]);
                    i++;
                }
                else
                {
                    while (i < statementsLen && statements[i].Kind == Kind.ExportDeclaration)
                    {
                        topLevelExportGroups[groupIndex].Add(statements[i]);
                        i++;
                    }

                    groupIndex++;
                }
            }
            else
            {
                i++;
                if (groupIndex < topLevelExportGroups.Count && topLevelExportGroups[groupIndex].Count > 0)
                {
                    groupIndex++;
                }
            }
        }

        var result = new List<List<Node>>();
        foreach (var exportGroup in topLevelExportGroups)
        {
            result.AddRange(GroupByNewlineContiguous(sourceFile, exportGroup.ToArray()));
        }

        return result;
    }

    /// <summary>Go: <c>func organizeExportsWorker(...)</c>（organizeimports.go:793-840）。</summary>
    internal static void OrganizeExportsWorker(
        List<Node> oldExportDecls,
        OrganizeImportsComparerSettings comparer,
        SourceFile sourceFile,
        Tracker changeTracker)
    {
        if (oldExportDecls.Count == 0)
        {
            return;
        }

        var specifierComparerFunc = GetNamedImportSpecifierComparer(
            new UserPreferences { OrganizeImportsTypeOrder = comparer._typeOrder },
            comparer._namedImportComparer);

        var newExportDecls = CoalesceExportsWorker(oldExportDecls, specifierComparerFunc, comparer._moduleSpecifierComparer, sourceFile, changeTracker);

        if (oldExportDecls.Count > 0)
        {
            if (newExportDecls.Count == 0)
            {
                changeTracker.DeleteNodeRange(
                    sourceFile,
                    oldExportDecls[0],
                    oldExportDecls[^1],
                    LeadingTriviaOption.Exclude,
                    TrailingTriviaOption.Include);
            }
            else
            {
                foreach (var exp in newExportDecls)
                {
                    changeTracker.EmitContext.AddEmitFlags(exp, EmitFlags.NoLeadingComments);
                }

                var options = new NodeOptions
                {
                    LeadingTriviaOption = TypeScript.LsChange.LeadingTriviaOption.Exclude,
                    TrailingTriviaOption = TypeScript.LsChange.TrailingTriviaOption.Include,
                    Suffix = "\n",
                };

                changeTracker.ReplaceNodeWithNodes(sourceFile, oldExportDecls[0], newExportDecls.ToArray(), options);

                if (oldExportDecls.Count > 1)
                {
                    for (var i = 1; i < oldExportDecls.Count; i++)
                    {
                        changeTracker.Delete(sourceFile, oldExportDecls[i]);
                    }
                }
            }
        }
    }

    /// <summary>Go: <c>func coalesceExportsWorker(...)</c>（organizeimports.go:842-913）。</summary>
    internal static List<Node> CoalesceExportsWorker(
        List<Node> exportGroup,
        Func<Node, Node, int> specifierComparer,
        Func<string, string, int> moduleSpecifierComparer,
        SourceFile sourceFile,
        Tracker changeTracker)
    {
        if (exportGroup.Count == 0)
        {
            return exportGroup;
        }

        var exportsByModuleSpecifier = new Dictionary<string, List<Node>>();
        var moduleSpecifierOrder = new List<string>();

        foreach (var exportDecl in exportGroup)
        {
            var export = exportDecl.AsExportDeclaration()!;
            var moduleSpecifier = "";
            if (export.ModuleSpecifier is not null)
            {
                moduleSpecifier = export.ModuleSpecifier.Text();
            }

            if (!exportsByModuleSpecifier.ContainsKey(moduleSpecifier))
            {
                moduleSpecifierOrder.Add(moduleSpecifier);
            }

            if (!exportsByModuleSpecifier.TryGetValue(moduleSpecifier, out var list))
            {
                exportsByModuleSpecifier[moduleSpecifier] = list = [];
            }

            list.Add(exportDecl);
        }

        moduleSpecifierOrder = [.. moduleSpecifierOrder.OrderBy(x => x, Comparer<string>.Create((a, b) =>
        {
            if (a == "" && b != "")
            {
                return 1;
            }

            if (a != "" && b == "")
            {
                return -1;
            }

            return moduleSpecifierComparer(a, b);
        }))];

        var coalescedExports = new List<Node>();
        var factory = new NodeFactory();

        foreach (var moduleSpecifier in moduleSpecifierOrder)
        {
            var group = exportsByModuleSpecifier[moduleSpecifier];

            var categorized = GetCategorizedExports(group);

            if (categorized._exportWithoutClause is not null)
            {
                coalescedExports.Add(categorized._exportWithoutClause);
            }

            List<Node>[] subGroups = [categorized._namedExports, categorized._typeOnlyExports];
            foreach (var subGroup in subGroups)
            {
                if (subGroup.Count == 0)
                {
                    continue;
                }

                List<Node> newExportSpecifiers = [];
                foreach (var exportDecl in subGroup)
                {
                    var exportClause = exportDecl.AsExportDeclaration()!.ExportClause;
                    if (exportClause is not null && exportClause.Kind == Kind.NamedExports)
                    {
                        var namedExports = exportClause.AsNamedExports()!;
                        newExportSpecifiers.AddRange(namedExports.Elements!.Nodes ?? []);
                    }
                }

                newExportSpecifiers = [.. newExportSpecifiers.OrderBy(x => x, Comparer<Node>.Create((a, b) => specifierComparer(a, b)))];

                var exportDecl0 = subGroup[0].AsExportDeclaration()!;

                Node? updatedExportClause = null;
                if (exportDecl0.ExportClause is not null)
                {
                    if (exportDecl0.ExportClause.Kind == Kind.NamedExports)
                    {
                        var namedExports = exportDecl0.ExportClause.AsNamedExports()!;
                        var sortedList = factory.NewNodeList(newExportSpecifiers.ToArray());
                        updatedExportClause = factory.UpdateNamedExports(namedExports, sortedList);

                        if (sourceFile is not null && !NodeIsSynthesized(namedExports) && !RangeIsOnSingleLine(namedExports.Loc, sourceFile))
                        {
                            changeTracker.EmitContext.SetEmitFlags(updatedExportClause, EmitFlags.MultiLine);
                        }
                    }
                    else
                    {
                        updatedExportClause = exportDecl0.ExportClause;
                    }
                }

                var newExportDecl = factory.UpdateExportDeclaration(
                    exportDecl0,
                    exportDecl0.Modifiers(),
                    exportDecl0.IsTypeOnly,
                    updatedExportClause,
                    exportDecl0.ModuleSpecifier,
                    exportDecl0.Attributes);
                coalescedExports.Add(newExportDecl);
            }
        }

        return coalescedExports;
    }

    /// <summary>Go: <c>func getCategorizedExports(exportGroup []*ast.Statement) categorizedExports</c>（organizeimports.go:921-943）。</summary>
    internal static CategorizedExports GetCategorizedExports(List<Node> exportGroup)
    {
        Node? exportWithoutClause = null;
        List<Node> namedExports = [];
        List<Node> typeOnlyExports = [];

        foreach (var exportDecl in exportGroup)
        {
            var export = exportDecl.AsExportDeclaration()!;
            if (export.ExportClause is null)
            {
                exportWithoutClause ??= exportDecl;
            }
            else if (export.IsTypeOnly)
            {
                typeOnlyExports.Add(exportDecl);
            }
            else
            {
                namedExports.Add(exportDecl);
            }
        }

        return new CategorizedExports
        {
            _exportWithoutClause = exportWithoutClause,
            _namedExports = namedExports,
            _typeOnlyExports = typeOnlyExports,
        };
    }
}

public sealed partial class LanguageService
{
    /// <summary>
    /// Go: <c>func (l *LanguageService) OrganizeImports(ctx context.Context, sourceFile *ast.SourceFile, program *compiler.Program, kind lsproto.CodeActionKind) map[string][]*lsproto.TextEdit</c>
    /// （organizeimports.go:15-129）。OrganizeImports organizes imports by:
    ///  1. Removing unused imports
    ///  2. Coalescing imports from the same module
    ///  3. Sorting imports
    /// </summary>
    public Dictionary<string, TextEdit[]> OrganizeImports(
        Context ctx,
        SourceFile sourceFile,
        Program program,
        CodeActionKind kind)
    {
        var changeTracker = Tracker.NewTracker(ctx, program.Options(), FormatOptions(), _converters!);
        var shouldSort = kind == CodeActionKind.SourceSortImportsTs || kind == CodeActionKind.SourceOrganizeImportsTs;
        var shouldCombine = shouldSort;
        var shouldRemove = kind == CodeActionKind.SourceRemoveUnusedImportsTs || kind == CodeActionKind.SourceOrganizeImportsTs;
        var topLevelImportDecls = FilterImportDeclarations(sourceFile.Statements!.Nodes ?? []);
        var topLevelImportGroupDecls = Ls.GroupByNewlineContiguous(sourceFile, topLevelImportDecls);

        var preferences = UserPreferences();
        var (comparersToTest, typeOrdersToTest) = GetDetectionLists(preferences);
        var defaultComparer = comparersToTest[0];
        var sort = ResolveOrganizeImportsSort(preferences);

        Func<string, string, int>? moduleSpecifierComparer;
        Func<string, string, int>? namedImportComparer;
        if (sort != OrganizeImportsSort.Auto)
        {
            moduleSpecifierComparer = defaultComparer;
            namedImportComparer = defaultComparer;
        }
        else
        {
            moduleSpecifierComparer = null;
            namedImportComparer = null;
        }

        var typeOrder = preferences.OrganizeImportsTypeOrder;

        if (sort == OrganizeImportsSort.Auto)
        {
            TryDetectModuleSpecifierCaseBySort(topLevelImportGroupDecls.Select(g => g.ToArray()).ToArray(), comparersToTest, out var result, out _);
            moduleSpecifierComparer = result;
        }

        if (typeOrder == OrganizeImportsTypeOrder.Auto || sort == OrganizeImportsSort.Auto)
        {
            var found = TryDetectNamedImportOrganizationBySort(topLevelImportDecls, comparersToTest, typeOrdersToTest, out var namedImportComparer2, out var typeOrder2);
            if (found)
            {
                if (namedImportComparer is null || sort == OrganizeImportsSort.Auto)
                {
                    namedImportComparer = namedImportComparer2;
                }

                if (typeOrder == OrganizeImportsTypeOrder.Auto)
                {
                    typeOrder = typeOrder2;
                }
            }
        }

        var comparer = new OrganizeImportsComparerSettings
        {
            _moduleSpecifierComparer = moduleSpecifierComparer!,
            _namedImportComparer = namedImportComparer!,
            _typeOrder = typeOrder,
        };

        foreach (var importGroupDecl in topLevelImportGroupDecls)
        {
            Ls.OrganizeImportsWorker(importGroupDecl, comparer, shouldSort, shouldCombine, shouldRemove, sourceFile, program, changeTracker, ctx);
        }

        if (kind != CodeActionKind.SourceRemoveUnusedImportsTs)
        {
            var topLevelExportGroupDecls = Ls.GetTopLevelExportGroups(sourceFile);
            foreach (var exportGroupDecl in topLevelExportGroupDecls)
            {
                Ls.OrganizeExportsWorker(exportGroupDecl, comparer, sourceFile, changeTracker);
            }
        }

        foreach (var stmt in sourceFile.Statements!.Nodes ?? [])
        {
            if (!IsAmbientModule(stmt))
            {
                continue;
            }

            var ambientModule = stmt.AsModuleDeclaration()!;
            if (ambientModule.Body is null)
            {
                continue;
            }

            var moduleBody = ambientModule.Body.AsModuleBlock()!;

            var ambientModuleImportDecls = FilterImportDeclarations(moduleBody.Statements!.Nodes ?? []);
            var ambientModuleImportGroupDecls = Ls.GroupByNewlineContiguous(sourceFile, ambientModuleImportDecls);

            foreach (var importGroupDecl in ambientModuleImportGroupDecls)
            {
                Ls.OrganizeImportsWorker(importGroupDecl, comparer, shouldSort, shouldCombine, shouldRemove, sourceFile, program, changeTracker, ctx);
            }

            if (kind != CodeActionKind.SourceRemoveUnusedImportsTs)
            {
                List<Node> ambientModuleExportDecls = [];
                foreach (var s in moduleBody.Statements!.Nodes ?? [])
                {
                    if (s.Kind == Kind.ExportDeclaration)
                    {
                        ambientModuleExportDecls.Add(s);
                    }
                }

                Ls.OrganizeExportsWorker(ambientModuleExportDecls, comparer, sourceFile, changeTracker);
            }
        }

        // Unmappable files are dropped by GetChanges, so a content-mapped file whose imports cannot be
        // faithfully rewritten yields no edits rather than a corrupting one.
        var (changes, _) = changeTracker.GetChanges();
        return changes;
    }
}
