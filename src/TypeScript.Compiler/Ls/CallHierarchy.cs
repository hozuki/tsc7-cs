// ported from: internal/ls/callhierarchy.go:1-1136
//
// 与 Go 的偏离（逐条说明）：
// 1) Go 未导出类型/函数 → `internal` + PascalCase（`.editorconfig` 的 `members_should_be_pascal_case` 对
//    方法/属性是 **error**；Go 的未导出**字段**才写 `_camelCase`）。
// 2) `type CallHierarchyDeclaration = *ast.Node`（Go 的类型别名）→ C# 无别名，直接用 `Node`。
// 3) Go 的 `any`（`resolveCallHierarchyDeclaration` / `findImplementationOrAllInitialDeclarations` 返回
//    `*ast.Node | []*ast.Node`）→ C# `object?` + 类型模式匹配（与 Go 的 `switch v := x.(type)` 逐一对应）。
// 4) `sync.Once` 缓存（`incomingEntry.sourceFile/documentUri/position`）→ `Lazy<T>`（默认
//    ExecutionAndPublication，与 sync.Once 的"至多一次"一致）；Go 的零值 sync.Once 是即时可用，C# 的 Lazy
//    必须构造时初始化 ⇒ `IncomingEntry` 显式写构造函数。
// 5) `slices.SortFunc`（不稳定）用于 `fromRanges` / 结果数组；C# 用 `Array.Sort`（同样不稳定）或 LINQ
//    `OrderBy`（稳定，用于"结果必须确定"的那两处：`getIncomingCalls` / `getOutgoingCalls` 的顶层排序）。
// 6) Go 的 `[]T` 可 nil → `T[]?`；`len(x) == 0` → `(x?.Length ?? 0) == 0`；`for range nil` → `?? []`。
//    ⚠ **nil 与空切片不同**：`findAllInitialDeclarations` 返回 nil 表示"没有声明"，调用点用 `!= nil` 判定；
//    所以 C# 版本在结果为空时**返回 null**（不是空数组）。
// 7) `scanner.SkipTrivia/SkipTriviaEx` 是 `Scanner` 类的静态成员（`using static`，见 G1 §4.1）；
//    `Printer` / `AstNav` 既是命名空间又是静态类 ⇒ 全限定 `TypeScript.Printer.Printer.X`。
// 8) `lsproto.CompareRanges` / `Ls.LspRangeContains`（G2）/ `Ls.GetSymbolKindFromNode`（G4）为既有成员，直接引用。
// 9) 深递归：`resolveCallHierarchyDeclaration` / `callSiteCollector.collect` 沿 AST 下降，由调用方（lsp 的请求线程）
//    跑在 64 MiB 大栈线程上（本仓既定做法，与 Go 的可增长 goroutine 栈对应）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.Scanner.Scanner;
using CheckerType = TypeScript.Checker.Checker;
using Kind = TypeScript.Ast.Kind;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Ls;

/// <summary>Go: <c>type callSite struct { declaration *ast.Node; textRange core.TextRange; sourceFile *ast.Node }</c>（callhierarchy.go:533-537）。</summary>
internal sealed class CallSite
{
    internal Node _declaration = null!;
    internal TextRange _textRange;
    internal Node _sourceFile = null!;
}

/// <summary>Go 的 <c>lsproto.Location</c> 作 map/set 键（结构体值相等）→ 复用 **G2 落在 `Ls/Definition.cs:68`** 的
/// `LocationKey`（值键，构造点写 <c>new LocationKey(location)</c>）；本文件不重复声明（CS0101）。</summary>
public static partial class Ls
{
    /// <summary>Indicates whether a node is named function or class expression. Go: <c>isNamedExpression</c>（callhierarchy.go:26-35）。</summary>
    internal static bool IsNamedExpression(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        if (!IsFunctionExpression(node) && !IsClassExpression(node))
        {
            return false;
        }

        var name = node.Name();
        return name is not null && IsIdentifier(name);
    }

    /// <summary>Go: <c>isVariableLike</c>（callhierarchy.go:37-43）。</summary>
    internal static bool IsVariableLike(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        return IsPropertyDeclaration(node) || IsVariableDeclaration(node);
    }

    /// <summary>Indicates whether a node is a function, arrow, or class expression assigned to a constant variable or class property. Go: <c>isAssignedExpression</c>（callhierarchy.go:45-73）。</summary>
    internal static bool IsAssignedExpression(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        if (!(IsFunctionExpression(node) || IsArrowFunction(node) || IsClassExpression(node)))
        {
            return false;
        }

        if (node.Name() is not null)
        {
            return false;
        }

        var parent = node.Parent!;
        if (!IsVariableLike(parent))
        {
            return false;
        }

        if (parent.Initializer() != node)
        {
            return false;
        }

        var name = parent.Name();
        if (name is null || !IsIdentifier(name))
        {
            return false;
        }

        return (GetCombinedNodeFlags(parent) & NodeFlags.Const) != 0 || IsPropertyDeclaration(parent);
    }

    /// <summary>Indicates whether a node could possibly be a call hierarchy declaration. Go: <c>isPossibleCallHierarchyDeclaration</c>（callhierarchy.go:75-93）。</summary>
    internal static bool IsPossibleCallHierarchyDeclaration(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        return IsSourceFile(node) ||
            IsModuleDeclaration(node) ||
            IsFunctionDeclaration(node) ||
            IsFunctionExpression(node) ||
            IsClassDeclaration(node) ||
            IsClassExpression(node) ||
            IsClassStaticBlockDeclaration(node) ||
            IsMethodDeclaration(node) ||
            IsMethodSignatureDeclaration(node) ||
            IsGetAccessorDeclaration(node) ||
            IsSetAccessorDeclaration(node);
    }

    /// <summary>Indicates whether a node is a valid call hierarchy declaration. Go: <c>isValidCallHierarchyDeclaration</c>（callhierarchy.go:95-118）。</summary>
    internal static bool IsValidCallHierarchyDeclaration(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        if (IsSourceFile(node))
        {
            return true;
        }

        if (IsModuleDeclaration(node))
        {
            return IsIdentifier(node.Name()!);
        }

        return IsFunctionDeclaration(node) ||
            IsClassDeclaration(node) ||
            IsClassStaticBlockDeclaration(node) ||
            IsMethodDeclaration(node) ||
            IsMethodSignatureDeclaration(node) ||
            IsGetAccessorDeclaration(node) ||
            IsSetAccessorDeclaration(node) ||
            IsNamedExpression(node) ||
            IsAssignedExpression(node);
    }

    /// <summary>Gets the node that can be used as a reference to a call hierarchy declaration. Go: <c>getCallHierarchyDeclarationReferenceNode</c>（callhierarchy.go:120-147）。</summary>
    internal static Node? GetCallHierarchyDeclarationReferenceNode(Node? node)
    {
        if (node is null)
        {
            return null;
        }

        if (IsSourceFile(node))
        {
            return node;
        }

        if (node.Name() is { } name)
        {
            return name;
        }

        if (IsAssignedExpression(node))
        {
            return node.Parent!.Name();
        }

        if (node.Modifiers() is { } modifiers)
        {
            foreach (var mod in modifiers.Nodes ?? [])
            {
                if (mod.Kind == Kind.DefaultKeyword)
                {
                    return mod;
                }
            }
        }

        return null;
    }

    /// <summary>Gets the symbol for a call hierarchy declaration. Go: <c>getSymbolOfCallHierarchyDeclaration</c>（callhierarchy.go:149-159）。</summary>
    internal static Symbol? GetSymbolOfCallHierarchyDeclaration(CheckerType c, Node node)
    {
        if (IsClassStaticBlockDeclaration(node))
        {
            return null;
        }

        var location = GetCallHierarchyDeclarationReferenceNode(node);
        if (location is null)
        {
            return null;
        }

        return c.GetSymbolAtLocation(location);
    }

    /// <summary>Gets the text and range for the name of a call hierarchy declaration. Go: <c>getCallHierarchyItemName</c>（callhierarchy.go:161-219）。</summary>
    internal static (string Text, int Pos, int End) GetCallHierarchyItemName(Program program, Node node)
    {
        if (IsSourceFile(node))
        {
            var sourceFile = node.AsSourceFile()!;
            return (sourceFile.FileName(), 0, 0);
        }

        if ((IsFunctionDeclaration(node) || IsClassDeclaration(node)) && node.Name() is null)
        {
            if (node.Modifiers() is { } modifiers)
            {
                foreach (var mod in modifiers.Nodes ?? [])
                {
                    if (mod.Kind == Kind.DefaultKeyword)
                    {
                        var sourceFile = GetSourceFileOfNode(node)!;
                        var start = SkipTrivia(sourceFile.Text(), mod.Pos());
                        return ("default", start, mod.End());
                    }
                }
            }
        }

        if (IsClassStaticBlockDeclaration(node))
        {
            var sourceFile = GetSourceFileOfNode(node)!;
            var pos = SkipTrivia(sourceFile.Text(), MoveRangePastModifiers(node).Pos());
            var end = pos + 6; // "static".length
            var (c, done) = program.GetTypeCheckerForFile(Context.Background, sourceFile);
            try
            {
                var symbol = c.GetSymbolAtLocation(node.Parent!);
                var prefix = "";
                if (symbol is not null)
                {
                    prefix = c.SymbolToString(symbol) + " ";
                }

                return (prefix + "static {}", pos, end);
            }
            finally
            {
                done();
            }
        }

        Node? declName;
        if (IsAssignedExpression(node))
        {
            declName = node.Parent!.Name();
        }
        else
        {
            declName = GetNameOfDeclaration(node);
        }

        if (declName is null || !NodeIsPresent(declName))
        {
            var sourceFile = GetSourceFileOfNode(node)!;
            if (IsFunctionDeclaration(node) || IsFunctionExpression(node))
            {
                var kwPos = SkipTrivia(sourceFile.Text(), MoveRangePastModifiers(node).Pos());
                return ("(anonymous)", kwPos, kwPos + 8); // "function".length
            }

            if (IsClassDeclaration(node) || IsClassExpression(node))
            {
                var kwPos = SkipTrivia(sourceFile.Text(), MoveRangePastModifiers(node).Pos());
                return ("(anonymous)", kwPos, kwPos + 5); // "class".length
            }

            Debug.Assert(declName is not null, "Expected call hierarchy item to have a name");
        }

        var text = GetTextOfCallHierarchyName(program, node, declName!, node);

        var file = GetSourceFileOfNode(node)!;
        var namePos = SkipTrivia(file.Text(), declName!.Pos());

        return (text, namePos, declName.End());
    }

    /// <summary>Go: <c>getTextOfCallHierarchyName</c>（callhierarchy.go:221-248）。</summary>
    internal static string GetTextOfCallHierarchyName(Program program, Node sourceNode, Node name, Node printNode)
    {
        if (IsIdentifier(name) || IsStringOrNumericLiteralLike(name))
        {
            return name.Text();
        }

        if (IsComputedPropertyName(name))
        {
            var expr = name.Expression()!;
            if (IsStringOrNumericLiteralLike(expr))
            {
                return expr.Text();
            }
        }

        var (c, done) = program.GetTypeCheckerForFile(Context.Background, GetSourceFileOfNode(sourceNode)!);
        try
        {
            var symbol = c.GetSymbolAtLocation(name);
            if (symbol is not null)
            {
                var text = c.SymbolToString(symbol);
                if (text.Length != 0)
                {
                    return text;
                }
            }
        }
        finally
        {
            done();
        }

        var sourceFile = GetSourceFileOfNode(sourceNode)!;
        var (writer, putWriter) = TypeScript.Printer.Printer.GetSingleLineStringWriter();
        try
        {
            var p = TypeScript.Printer.Printer.NewPrinter(
                new TypeScript.Printer.PrinterOptions { NewLine = NewLineKind.LF },
                new TypeScript.Printer.PrintHandlers(),
                null);
            p.Write(printNode, sourceFile, writer, null);
            return writer.String();
        }
        finally
        {
            putWriter();
        }
    }

    /// <summary>Go: <c>getCallHierarchyItemContainerName</c>（callhierarchy.go:250-296）。</summary>
    internal static string GetCallHierarchyItemContainerName(Program program, Node node)
    {
        if (IsAssignedExpression(node))
        {
            var parent = node.Parent!;
            if (IsPropertyDeclaration(parent) && IsClassLike(parent.Parent!))
            {
                if (IsClassExpression(parent.Parent!))
                {
                    if (GetAssignedName(parent.Parent!) is { } assignedName)
                    {
                        return GetTextOfCallHierarchyName(program, node, assignedName, assignedName);
                    }
                }
                else
                {
                    if (parent.Parent!.Name() is { } name)
                    {
                        return GetTextOfCallHierarchyName(program, node, name, name);
                    }
                }
            }

            if (parent.Parent?.Parent?.Parent is not null && IsModuleBlock(parent.Parent.Parent.Parent))
            {
                var modParent = parent.Parent.Parent.Parent.Parent;
                if (modParent is not null && IsModuleDeclaration(modParent))
                {
                    if (modParent.Name() is { } name && IsIdentifier(name))
                    {
                        return name.Text();
                    }
                }
            }

            return "";
        }

        switch (node.Kind)
        {
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.MethodDeclaration:
                if (node.Parent!.Kind == Kind.ObjectLiteralExpression)
                {
                    if (GetAssignedName(node.Parent!) is { } assignedName)
                    {
                        return GetTextOfCallHierarchyName(program, node, assignedName, assignedName);
                    }
                }

                if (GetNameOfDeclaration(node.Parent!) is { } containerName)
                {
                    return GetTextOfCallHierarchyName(program, node, containerName, containerName);
                }

                break;
            case Kind.FunctionDeclaration:
            case Kind.ClassDeclaration:
            case Kind.ModuleDeclaration:
                if (IsModuleBlock(node.Parent!))
                {
                    if (IsModuleDeclaration(node.Parent!.Parent!))
                    {
                        if (node.Parent!.Parent!.Name() is { } name && IsIdentifier(name))
                        {
                            return name.Text();
                        }
                    }
                }

                break;
        }

        return "";
    }

    /// <summary>Go: <c>moveRangePastModifiers</c>（callhierarchy.go:298-305）。</summary>
    internal static TextRange MoveRangePastModifiers(Node node)
    {
        if (node.Modifiers() is { Nodes: { Length: > 0 } nodes })
        {
            var lastMod = nodes[^1];
            return new TextRange(lastMod.End(), node.End());
        }

        return new TextRange(node.Pos(), node.End());
    }

    /// <summary>Finds the implementation of a function-like declaration, if one exists. Go: <c>findImplementation</c>（callhierarchy.go:307-335）。</summary>
    internal static Node? FindImplementation(CheckerType c, Node? node)
    {
        if (node is null)
        {
            return null;
        }

        if (!IsFunctionLikeDeclaration(node))
        {
            return node;
        }

        if (node.Body() is not null)
        {
            return node;
        }

        if (IsConstructorDeclaration(node))
        {
            return GetFirstConstructorWithBody(node.Parent!);
        }

        if (IsFunctionDeclaration(node) || IsMethodDeclaration(node))
        {
            var symbol = GetSymbolOfCallHierarchyDeclaration(c, node);
            if (symbol?.ValueDeclaration is not null)
            {
                if (IsFunctionLikeDeclaration(symbol.ValueDeclaration) && symbol.ValueDeclaration.Body() is not null)
                {
                    return symbol.ValueDeclaration;
                }
            }

            return null;
        }

        return node;
    }

    /// <summary>Go: <c>findAllInitialDeclarations</c>（callhierarchy.go:337-386）。⚠ 空 ⇒ 返回 <c>null</c>（Go 的 nil 切片语义，见文件头第 6 条）。</summary>
    internal static Node[]? FindAllInitialDeclarations(CheckerType c, Node node)
    {
        if (IsClassStaticBlockDeclaration(node))
        {
            return null;
        }

        var symbol = GetSymbolOfCallHierarchyDeclaration(c, node);
        if (symbol?.Declarations is null)
        {
            return null;
        }

        var declarations = symbol.Declarations;
        var indices = new int[declarations.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        var keys = new (string File, int Pos)[declarations.Length];
        for (var i = 0; i < declarations.Length; i++)
        {
            keys[i] = (GetSourceFileOfNode(declarations[i])!.FileName(), declarations[i].Pos());
        }

        Array.Sort(indices, (a, b) =>
        {
            if (keys[a].File != keys[b].File)
            {
                return string.CompareOrdinal(keys[a].File, keys[b].File);
            }

            return keys[a].Pos - keys[b].Pos;
        });

        var result = new List<Node>();
        Node? lastDecl = null;

        foreach (var i in indices)
        {
            var decl = declarations[i];
            if (IsValidCallHierarchyDeclaration(decl))
            {
                if (lastDecl is null || lastDecl.Parent != decl.Parent || lastDecl.End() != decl.Pos())
                {
                    result.Add(decl);
                }

                lastDecl = decl;
            }
        }

        return result.Count == 0 ? null : result.ToArray();
    }

    /// <summary>Find the implementation or the first declaration for a call hierarchy declaration. Go: <c>findImplementationOrAllInitialDeclarations</c>（callhierarchy.go:388-408）。返回值是 Go 的 <c>any</c>（<c>*Node | []*Node</c>）。</summary>
    internal static object? FindImplementationOrAllInitialDeclarations(CheckerType c, Node node)
    {
        if (IsClassStaticBlockDeclaration(node))
        {
            return node;
        }

        if (IsFunctionLikeDeclaration(node))
        {
            if (FindImplementation(c, node) is { } impl)
            {
                return impl;
            }

            if (FindAllInitialDeclarations(c, node) is { } decls)
            {
                return decls;
            }

            return node;
        }

        if (FindAllInitialDeclarations(c, node) is { } initialDecls)
        {
            return initialDecls;
        }

        return node;
    }

    /// <summary>Resolves the call hierarchy declaration for a node. Go: <c>resolveCallHierarchyDeclaration</c>（callhierarchy.go:410-499）。返回值是 Go 的 <c>any</c>（<c>*Node | []*Node</c>）。</summary>
    internal static object? ResolveCallHierarchyDeclaration(Program program, Node? location)
    {
        // A call hierarchy item must refer to either a SourceFile, Module Declaration, Class Static Block, or something intrinsically callable that has a name:
        // - Class Declarations
        // - Class Expressions (with a name)
        // - Function Declarations
        // - Function Expressions (with a name or assigned to a const variable)
        // - Arrow Functions (assigned to a const variable)
        // - Constructors
        // - Class `static {}` initializer blocks
        // - Methods
        // - Accessors
        //
        // If a call is contained in a non-named callable Node (function expression, arrow function, etc.), then
        // its containing `CallHierarchyItem` is a containing function or SourceFile that matches the above list.

        var (c, done) = program.GetTypeChecker(Context.Background);
        try
        {
            var followingSymbol = false;

            while (location is not null)
            {
                if (IsValidCallHierarchyDeclaration(location))
                {
                    return FindImplementationOrAllInitialDeclarations(c, location);
                }

                if (IsPossibleCallHierarchyDeclaration(location))
                {
                    var ancestor = FindAncestor(location, IsValidCallHierarchyDeclaration);
                    if (ancestor is not null)
                    {
                        return FindImplementationOrAllInitialDeclarations(c, ancestor);
                    }
                }

                if (IsDeclarationName(location))
                {
                    if (IsValidCallHierarchyDeclaration(location.Parent!))
                    {
                        return FindImplementationOrAllInitialDeclarations(c, location.Parent!);
                    }

                    if (IsPossibleCallHierarchyDeclaration(location.Parent!))
                    {
                        var ancestor = FindAncestor(location.Parent!, IsValidCallHierarchyDeclaration);
                        if (ancestor is not null)
                        {
                            return FindImplementationOrAllInitialDeclarations(c, ancestor);
                        }
                    }

                    if (IsVariableLike(location.Parent!))
                    {
                        var initializer = location.Parent!.Initializer();
                        if (initializer is not null && IsAssignedExpression(initializer))
                        {
                            return initializer;
                        }
                    }

                    return null;
                }

                if (IsConstructorDeclaration(location))
                {
                    if (IsValidCallHierarchyDeclaration(location.Parent!))
                    {
                        return location.Parent;
                    }

                    return null;
                }

                if (location.Kind == Kind.StaticKeyword && IsClassStaticBlockDeclaration(location.Parent!))
                {
                    location = location.Parent;
                    continue;
                }

                // #39453
                if (IsVariableDeclaration(location))
                {
                    var initializer = location.Initializer();
                    if (initializer is not null && IsAssignedExpression(initializer))
                    {
                        return initializer;
                    }
                }

                if (!followingSymbol)
                {
                    var symbol = c.GetSymbolAtLocation(location);
                    if (symbol is not null)
                    {
                        if ((symbol.Flags & SymbolFlags.Alias) != 0)
                        {
                            symbol = c.GetAliasedSymbol(symbol);
                        }

                        if (symbol.ValueDeclaration is not null)
                        {
                            followingSymbol = true;
                            location = symbol.ValueDeclaration;
                            continue;
                        }
                    }
                }

                return null;
            }

            return null;
        }
        finally
        {
            done();
        }
    }

    /// <summary>Go 的 <c>lsproto.Range</c> 按值比较（`slices.Contains(fromRanges, r)`）；C# 的 `Range` 是 class ⇒ 显式按时值比较。</summary>
    internal static bool RangeEquals(Range a, Range b) =>
        a.Start.Line == b.Start.Line && a.Start.Character == b.Start.Character &&
        a.End.Line == b.End.Line && a.End.Character == b.End.Character;

    /// <summary>Go: <c>func convertEntryToCallSite(entry *ReferenceEntry) *callSite</c>（callhierarchy.go:539-566）。</summary>
    internal static CallSite? ConvertEntryToCallSite(ReferenceEntry entry)
    {
        if (entry._kind != EntryKind.Node)
        {
            return null;
        }

        var node = entry._node!;
        if (!IsCallOrNewExpressionTarget(node, true /*includeElementAccess*/, true /*skipPastOuterExpressions*/) &&
            !IsTaggedTemplateTag(node, true, true) &&
            !IsDecoratorTarget(node, true, true) &&
            !IsJsxOpeningLikeElementTagName(node, true, true) &&
            !IsRightSideOfPropertyAccess(node) &&
            !IsArgumentExpressionOfElementAccess(node))
        {
            return null;
        }

        var sourceFile = GetSourceFileOfNode(node)!;
        var ancestor = FindAncestor(node, IsValidCallHierarchyDeclaration);
        ancestor ??= sourceFile.AsNode();

        var start = SkipTrivia(sourceFile.Text(), node.Pos());
        return new CallSite
        {
            _declaration = ancestor,
            _textRange = new TextRange(start, node.End()),
            _sourceFile = sourceFile.AsNode(),
        };
    }

    /// <summary>Go: <c>func getCallSiteGroupKey(site *callSite) ast.NodeId</c>（callhierarchy.go:568-570）。</summary>
    internal static NodeId GetCallSiteGroupKey(CallSite site)
    {
        return GetNodeId(site._declaration);
    }

    /// <summary>Go: <c>func collectCallSites(program *compiler.Program, c *checker.Checker, node *ast.Node) []*callSite</c>（callhierarchy.go:869-940）。</summary>
    internal static List<CallSite> CollectCallSites(Program program, CheckerType c, Node node)
    {
        var collector = new CallSiteCollector { _program = program, _callSites = [] };

        switch (node.Kind)
        {
            case Kind.SourceFile:
                foreach (var stmt in node.Statements() ?? [])
                {
                    collector.Collect(stmt);
                }

                break;

            case Kind.ModuleDeclaration:
                if (node.Body() is { } body && !HasSyntacticModifier(node, ModifierFlags.Ambient) && IsModuleBlock(body))
                {
                    foreach (var stmt in body.Statements() ?? [])
                    {
                        collector.Collect(stmt);
                    }
                }

                break;

            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                var impl = FindImplementation(c, node);
                if (impl is not null)
                {
                    foreach (var param in impl.Parameters() ?? [])
                    {
                        collector.Collect(param);
                    }

                    collector.Collect(impl.Body()!);
                }

                break;

            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                if (node.Modifiers() is { } modifiers)
                {
                    foreach (var mod in modifiers.Nodes ?? [])
                    {
                        collector.Collect(mod);
                    }
                }

                var heritage = GetClassExtendsHeritageElement(node);
                if (heritage is not null)
                {
                    collector.Collect(heritage.Expression()!);
                }

                foreach (var member in node.Members() ?? [])
                {
                    if (CanHaveModifiers(member) && member.Modifiers() is { } memberModifiers)
                    {
                        foreach (var mod in memberModifiers.Nodes ?? [])
                        {
                            collector.Collect(mod);
                        }
                    }

                    if (IsPropertyDeclaration(member))
                    {
                        collector.Collect(member.Initializer()!);
                    }
                    else if (IsConstructorDeclaration(member))
                    {
                        if (member.Body() is { } memberBody)
                        {
                            foreach (var param in member.Parameters() ?? [])
                            {
                                collector.Collect(param);
                            }

                            collector.Collect(memberBody);
                        }
                    }
                    else if (IsClassStaticBlockDeclaration(member))
                    {
                        collector.Collect(member);
                    }
                }

                break;

            case Kind.ClassStaticBlockDeclaration:
                collector.Collect(node.AsClassStaticBlockDeclaration()!.Body);
                break;

            default:
                Debug.AssertNever(node);
                break;
        }

        return collector._callSites;
    }
}

/// <summary>Go: <c>type callSiteCollector struct { program *compiler.Program; callSites []*callSite }</c>（callhierarchy.go:711-714）。</summary>
internal sealed class CallSiteCollector
{
    internal Program _program = null!;
    internal List<CallSite> _callSites = [];

    /// <summary>Go: <c>func (c *callSiteCollector) recordCallSite(node *ast.Node)</c>（callhierarchy.go:716-767）。</summary>
    internal void RecordCallSite(Node node)
    {
        Node? target = null;

        if (IsTaggedTemplateExpression(node))
        {
            target = node.AsTaggedTemplateExpression()!.Tag;
        }
        else if (IsJsxOpeningElement(node))
        {
            target = node.TagName();
        }
        else if (IsJsxSelfClosingElement(node))
        {
            target = node.TagName();
        }
        else if (IsPropertyAccessExpression(node) || IsElementAccessExpression(node))
        {
            target = node;
        }
        else if (IsClassStaticBlockDeclaration(node))
        {
            target = node;
        }
        else if (IsCallExpression(node))
        {
            target = node.Expression();
        }
        else if (IsNewExpression(node))
        {
            target = node.Expression();
        }
        else if (IsDecorator(node))
        {
            target = node.Expression();
        }

        if (target is null)
        {
            return;
        }

        var declaration = Ls.ResolveCallHierarchyDeclaration(_program, target);
        if (declaration is null)
        {
            return;
        }

        var sourceFile = GetSourceFileOfNode(target)!;
        var start = SkipTrivia(sourceFile.Text(), target.Pos());
        var textRange = new TextRange(start, target.End());

        switch (declaration)
        {
            case Node decl:
                _callSites.Add(new CallSite { _declaration = decl, _textRange = textRange, _sourceFile = sourceFile.AsNode() });
                break;
            case Node[] decls:
                foreach (var d in decls)
                {
                    _callSites.Add(new CallSite { _declaration = d, _textRange = textRange, _sourceFile = sourceFile.AsNode() });
                }

                break;
        }
    }

    /// <summary>Go: <c>func (c *callSiteCollector) collect(node *ast.Node)</c>（callhierarchy.go:769-867）。</summary>
    internal void Collect(Node? node)
    {
        if (node is null)
        {
            return;
        }

        // do not descend into ambient nodes.
        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            return;
        }

        // do not descend into other call site declarations, other than class member names
        if (Ls.IsValidCallHierarchyDeclaration(node))
        {
            if (IsClassLike(node))
            {
                foreach (var member in node.Members() ?? [])
                {
                    if (member.Name() is { } name && IsComputedPropertyName(name))
                    {
                        Collect(name.Expression()!);
                    }
                }
            }

            return;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportDeclaration:
            case Kind.ExportDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
                // do not descend into nodes that cannot contain callable nodes
                return;
            case Kind.ClassStaticBlockDeclaration:
                RecordCallSite(node);
                return;
            case Kind.TypeAssertionExpression:
            case Kind.AsExpression:
                // do not descend into the type side of an assertion
                Collect(node.Expression()!);
                return;
            case Kind.VariableDeclaration:
            case Kind.Parameter:
                // do not descend into the type of a variable or parameter declaration
                Collect(node.Name()!);
                Collect(node.Initializer()!);
                return;
            case Kind.CallExpression:
                // do not descend into the type arguments of a call expression
                RecordCallSite(node);
                Collect(node.Expression()!);
                foreach (var arg in node.Arguments() ?? [])
                {
                    Collect(arg);
                }

                return;
            case Kind.NewExpression:
                // do not descend into the type arguments of a new expression
                RecordCallSite(node);
                Collect(node.Expression()!);
                foreach (var arg in node.Arguments() ?? [])
                {
                    Collect(arg);
                }

                return;
            case Kind.TaggedTemplateExpression:
                // do not descend into the type arguments of a tagged template expression
                RecordCallSite(node);
                var taggedTemplate = node.AsTaggedTemplateExpression()!;
                Collect(taggedTemplate.Tag!);
                Collect(taggedTemplate.Template);
                return;
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
                // do not descend into the type arguments of a JsxOpeningLikeElement
                RecordCallSite(node);
                Collect(node.TagName()!);
                Collect(node.Attributes()!);
                return;
            case Kind.Decorator:
                RecordCallSite(node);
                Collect(node.Expression()!);
                return;
            case Kind.PropertyAccessExpression:
            case Kind.ElementAccessExpression:
                RecordCallSite(node);
                node.ForEachChild(child =>
                {
                    Collect(child);
                    return false;
                });
                return;
            case Kind.SatisfiesExpression:
                // do not descend into the type side of an assertion
                Collect(node.Expression()!);
                return;
        }

        if (IsPartOfTypeNode(node))
        {
            // do not descend into types
            return;
        }

        node.ForEachChild(child =>
        {
            Collect(child);
            return false;
        });
    }
}

/// <summary>Go: <c>type incomingEntry struct</c>（callhierarchy.go:593-607）：`sync.Once` 缓存 → `Lazy&lt;T&gt;`（见文件头第 4 条）。</summary>
internal sealed class IncomingEntry : HasTextDocumentPosition
{
    internal LanguageService _ls = null!;
    internal Node _node = null!;

    internal Lazy<SourceFile> _sourceFile = null!;
    internal Lazy<DocumentUri> _documentUri = null!;
    internal Lazy<Position> _position = null!;

    internal IncomingEntry(LanguageService ls, Node node)
    {
        _ls = ls;
        _node = node;
        _sourceFile = new Lazy<SourceFile>(() => GetSourceFileOfNode(_node)!);
        _documentUri = new Lazy<DocumentUri>(() => FileNameToDocumentURI(GetSourceFile().OriginalFileName()));
        _position = new Lazy<Position>(() =>
        {
            var start = GetTokenPosOfNode(_node, GetSourceFile(), false /*includeJsDoc*/);
            var (position, _) = _ls.CreateLspPosition(start, GetSourceFile());
            return position;
        });
    }

    /// <summary>Go: <c>func (d *incomingEntry) getSourceFile() *ast.SourceFile</c>。</summary>
    internal SourceFile GetSourceFile()
    {
        return _sourceFile.Value;
    }

    /// <summary>Go: <c>func (d *incomingEntry) TextDocumentURI() lsproto.DocumentUri</c>。</summary>
    public DocumentUri TextDocumentURI()
    {
        return _documentUri.Value;
    }

    /// <summary>Go: <c>func (d *incomingEntry) TextDocumentPosition() lsproto.Position</c>。</summary>
    public Position TextDocumentPosition()
    {
        return _position.Value;
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) createCallHierarchyItem(program *compiler.Program, node *ast.Node) *lsproto.CallHierarchyItem</c>（callhierarchy.go:501-531）。</summary>
    internal CallHierarchyItem? CreateCallHierarchyItem(Program program, Node node)
    {
        var sourceFile = GetSourceFileOfNode(node)!;
        var (nameText, namePos, nameEnd) = Ls.GetCallHierarchyItemName(program, node);
        var containerName = Ls.GetCallHierarchyItemContainerName(program, node);

        var kind = Ls.GetSymbolKindFromNode(node);

        var fullStart = SkipTriviaEx(sourceFile.Text(), node.Pos(), new TypeScript.Scanner.SkipTriviaOptions { StopAtComments = true });
        var (span, spanFidelity) = _converters!.ToLSPRangeForFeature(sourceFile, new TextRange(fullStart, node.End()), Feature.CallHierarchy);
        var (selectionSpan, selectionFidelity) = _converters.ToLSPRangeForFeature(sourceFile, new TextRange(namePos, nameEnd), Feature.CallHierarchy);
        if (!selectionFidelity.IsSingleSegment())
        {
            return null;
        }

        if (spanFidelity.IsNone() || (sourceFile.ContentMapper() != "" && !Ls.LspRangeContains(span, selectionSpan)))
        {
            span = selectionSpan;
        }

        var item = new CallHierarchyItem
        {
            Name = nameText,
            Kind = kind,
            Uri = FileNameToDocumentURI(sourceFile.OriginalFileName()),
            Range = span,
            SelectionRange = selectionSpan,
        };

        if (containerName.Length != 0)
        {
            item.Detail = containerName;
        }

        return item;
    }

    /// <summary>Go: <c>func (l *LanguageService) convertCallSiteGroupToIncomingCall(program *compiler.Program, entries []*callSite) *lsproto.CallHierarchyIncomingCall</c>（callhierarchy.go:572-591）。</summary>
    internal CallHierarchyIncomingCall? ConvertCallSiteGroupToIncomingCall(Program program, List<CallSite> entries)
    {
        var fromRanges = new List<Range>();
        foreach (var entry in entries)
        {
            var sourceFile = entry._sourceFile.AsSourceFile()!;
            var (lspRange, fidelity) = _converters!.ToLSPRangeForFeature(sourceFile, entry._textRange, Feature.CallHierarchy);
            if (!fidelity.IsNone())
            {
                fromRanges.Add(lspRange);
            }
        }

        var from = CreateCallHierarchyItem(program, entries[0]._declaration);
        if (from is null || fromRanges.Count == 0)
        {
            return null;
        }

        // Go 的 slices.SortFunc 不稳定；List.Sort 同样不稳定。
        fromRanges.Sort(LsProto.LsProto.CompareRanges);

        return new CallHierarchyIncomingCall
        {
            From = from,
            FromRanges = fromRanges.ToArray(),
        };
    }

    /// <summary>Gets the call sites that call into the provided call hierarchy declaration. Go: <c>getIncomingCalls</c>（callhierarchy.go:632-676）。</summary>
    internal (CallHierarchyIncomingCallsOrNull Response, Exception? Error) GetIncomingCalls(Context ctx, Program program, Node declaration, CrossProjectOrchestrator orchestrator)
    {
        // Source files and modules have no incoming calls.
        if (IsSourceFile(declaration) || IsModuleDeclaration(declaration) || IsClassStaticBlockDeclaration(declaration))
        {
            return (new CallHierarchyIncomingCallsOrNull(), null);
        }

        var location = Ls.GetCallHierarchyDeclarationReferenceNode(declaration);
        if (location is null)
        {
            return (new CallHierarchyIncomingCallsOrNull(), null);
        }

        var locationFile = GetSourceFileOfNode(location)!;
        var locationStart = GetTokenPosOfNode(location, locationFile, false /*includeJsDoc*/);
        if (_converters!.ToLSPPositionForFeature(locationFile, new TextPos(locationStart), Feature.CallHierarchy).Fidelity.IsNone())
        {
            return (new CallHierarchyIncomingCallsOrNull(), null);
        }

        var incomingEntry = new IncomingEntry(this, location);

        var (result, err) = HandleCrossProject(
            ctx,
            incomingEntry,
            orchestrator,
            (ls, c, p, data, options) => ls.SymbolAndEntriesToIncomingCalls(c, p, data, options),
            Ls.CombineIncomingCalls,
            false,
            false,
            new SymbolEntryTransformOptions(),
            null /*defaultProjectData*/,
            (ls, c, uri, pos, rename, impl) => ls.ProvideSymbolsAndEntries(c, uri, pos, rename, impl));
        if (result.CallHierarchyIncomingCalls is not null)
        {
            var sorted = result.CallHierarchyIncomingCalls.Where(call => call is not null)
                .OrderBy(call => call!.From!.Uri.Value, StringComparer.Ordinal)
                .ThenBy(call => call!.FromRanges is { Length: > 0 } ranges ? ranges[0] : null, Comparer<Range?>.Create((a, b) =>
                {
                    if (a is null || b is null)
                    {
                        return 0;
                    }

                    return LsProto.LsProto.CompareRanges(a, b);
                }))
                .ToArray();
            result.CallHierarchyIncomingCalls = sorted;
        }

        return (result, err);
    }

    /// <summary>Go: <c>func (l *LanguageService) symbolAndEntriesToIncomingCalls(ctx, params *incomingEntry, data, options) (lsproto.CallHierarchyIncomingCallsResponse, error)</c>（callhierarchy.go:678-709）。</summary>
    internal (CallHierarchyIncomingCallsOrNull Response, Exception? Error) SymbolAndEntriesToIncomingCalls(Context ctx, IncomingEntry @params, SymbolAndEntriesData data, SymbolEntryTransformOptions options)
    {
        var program = GetProgram()!;
        var refEntries = new List<ReferenceEntry>();
        foreach (var symbolAndEntry in data.SymbolsAndEntries ?? [])
        {
            refEntries.AddRange(symbolAndEntry._references ?? []);
        }

        var callSites = new List<CallSite>();
        foreach (var entry in refEntries)
        {
            if (Ls.ConvertEntryToCallSite(entry) is { } site)
            {
                callSites.Add(site);
            }
        }

        if (callSites.Count == 0)
        {
            return (new CallHierarchyIncomingCallsOrNull(), null);
        }

        var grouped = new Dictionary<NodeId, List<CallSite>>();
        foreach (var site in callSites)
        {
            var key = Ls.GetCallSiteGroupKey(site);
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }

            list.Add(site);
        }

        var result = new List<CallHierarchyIncomingCall>();
        foreach (var sites in grouped.Values)
        {
            if (ConvertCallSiteGroupToIncomingCall(program, sites) is { } incomingCall)
            {
                result.Add(incomingCall);
            }
        }

        return (new CallHierarchyIncomingCallsOrNull { CallHierarchyIncomingCalls = result.ToArray() }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) convertCallSiteGroupToOutgoingCall(program, entries []*callSite) *lsproto.CallHierarchyOutgoingCall</c>（callhierarchy.go:942-962）。</summary>
    internal CallHierarchyOutgoingCall? ConvertCallSiteGroupToOutgoingCall(Program program, List<CallSite> entries)
    {
        var fromRanges = new List<Range>();
        foreach (var entry in entries)
        {
            var sourceFile = entry._sourceFile.AsSourceFile()!;
            var (lspRange, fidelity) = _converters!.ToLSPRangeForFeature(sourceFile, entry._textRange, Feature.CallHierarchy);
            if (!fidelity.IsNone())
            {
                fromRanges.Add(lspRange);
            }
        }

        var to = CreateCallHierarchyItem(program, entries[0]._declaration);
        if (to is null || fromRanges.Count == 0)
        {
            return null;
        }

        fromRanges.Sort(LsProto.LsProto.CompareRanges);

        return new CallHierarchyOutgoingCall
        {
            To = to,
            FromRanges = fromRanges.ToArray(),
        };
    }

    /// <summary>Gets the call sites that call out of the provided call hierarchy declaration. Go: <c>getOutgoingCalls</c>（callhierarchy.go:964-1002）。</summary>
    internal List<CallHierarchyOutgoingCall>? GetOutgoingCalls(Program program, Node declaration)
    {
        if ((declaration.Flags & NodeFlags.Ambient) != 0 || IsMethodSignatureDeclaration(declaration))
        {
            return null;
        }

        var (c, done) = program.GetTypeChecker(Context.Background);
        try
        {
            var callSites = Ls.CollectCallSites(program, c, declaration);

            if (callSites.Count == 0)
            {
                return null;
            }

            var grouped = new Dictionary<NodeId, List<CallSite>>();
            foreach (var site in callSites)
            {
                var key = Ls.GetCallSiteGroupKey(site);
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = [];
                    grouped[key] = list;
                }

                list.Add(site);
            }

            var result = new List<CallHierarchyOutgoingCall>();
            foreach (var sites in grouped.Values)
            {
                if (ConvertCallSiteGroupToOutgoingCall(program, sites) is { } outgoingCall)
                {
                    result.Add(outgoingCall);
                }
            }

            var sorted = result
                .OrderBy(call => call.To!.Uri.Value, StringComparer.Ordinal)
                .ThenBy(call => call.FromRanges is { Length: > 0 } ranges ? ranges[0] : null, Comparer<Range?>.Create((a, b) =>
                {
                    if (a is null || b is null)
                    {
                        return 0;
                    }

                    return LsProto.LsProto.CompareRanges(a, b);
                }))
                .ToList();

            return sorted;
        }
        finally
        {
            done();
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvidePrepareCallHierarchy(ctx, documentURI, position) (lsproto.CallHierarchyPrepareResponse, error)</c>（callhierarchy.go:1004-1026）。</summary>
    public (CallHierarchyItemsOrNull Response, Exception? Error) ProvidePrepareCallHierarchy(Context ctx, DocumentUri documentURI, Position position)
    {
        var (program, file) = GetProgramAndFile(documentURI);
        var declarations = CallHierarchyDeclarations(file, position, program, false);
        var items = new List<CallHierarchyItem>();
        var seen = new HashSet<LocationKey>();
        foreach (var declaration in declarations)
        {
            if (CreateCallHierarchyItem(program, declaration) is { } item)
            {
                var location = new Location { Uri = item.Uri, Range = item.SelectionRange };
                if (seen.Add(new LocationKey(location)))
                {
                    items.Add(item);
                }
            }
        }

        if (items.Count == 0)
        {
            return (new CallHierarchyItemsOrNull(), null);
        }

        return (new CallHierarchyItemsOrNull { CallHierarchyItems = items.ToArray() }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideCallHierarchyIncomingCalls(ctx, item, orchestrator) (lsproto.CallHierarchyIncomingCallsResponse, error)</c>（callhierarchy.go:1028-1068）。</summary>
    public (CallHierarchyIncomingCallsOrNull Response, Exception? Error) ProvideCallHierarchyIncomingCalls(Context ctx, CallHierarchyItem item, CrossProjectOrchestrator orchestrator)
    {
        var program = GetProgram()!;
        var fileName = item.Uri.FileName();
        var file = program.GetSourceFile(fileName);
        if (file is null)
        {
            return (new CallHierarchyIncomingCallsOrNull(), null);
        }

        var declarations = CallHierarchyDeclarations(file, item.SelectionRange.Start, program, true);
        var calls = new List<CallHierarchyIncomingCall>();
        var seen = new Dictionary<LocationKey, CallHierarchyIncomingCall>();
        foreach (var declaration in declarations)
        {
            var (response, err) = GetIncomingCalls(ctx, program, declaration, orchestrator);
            if (err is not null)
            {
                return (new CallHierarchyIncomingCallsOrNull(), err);
            }

            if (response.CallHierarchyIncomingCalls is not null)
            {
                foreach (var call in response.CallHierarchyIncomingCalls)
                {
                    if (call?.From is null)
                    {
                        continue;
                    }

                    var key = new LocationKey(new Location { Uri = call.From.Uri, Range = call.From.SelectionRange });
                    if (seen.TryGetValue(key, out var existing))
                    {
                        foreach (var fromRange in call.FromRanges ?? [])
                        {
                            if (!(existing.FromRanges ?? []).Any(r => Ls.RangeEquals(r, fromRange)))
                            {
                                existing.FromRanges = [.. existing.FromRanges ?? [], fromRange];
                            }
                        }
                    }
                    else
                    {
                        seen[key] = call;
                        calls.Add(call);
                    }
                }
            }
        }

        if (calls.Count == 0)
        {
            return (new CallHierarchyIncomingCallsOrNull(), null);
        }

        return (new CallHierarchyIncomingCallsOrNull { CallHierarchyIncomingCalls = calls.ToArray() }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideCallHierarchyOutgoingCalls(ctx, item) (lsproto.CallHierarchyOutgoingCallsResponse, error)</c>（callhierarchy.go:1070-1103）。</summary>
    public (CallHierarchyOutgoingCallsOrNull Response, Exception? Error) ProvideCallHierarchyOutgoingCalls(Context ctx, CallHierarchyItem item)
    {
        var program = GetProgram()!;
        var fileName = item.Uri.FileName();
        var file = program.GetSourceFile(fileName);
        if (file is null)
        {
            return (new CallHierarchyOutgoingCallsOrNull(), null);
        }

        var declarations = CallHierarchyDeclarations(file, item.SelectionRange.Start, program, true);
        var calls = new List<CallHierarchyOutgoingCall>();
        var seen = new Dictionary<LocationKey, CallHierarchyOutgoingCall>();
        foreach (var declaration in declarations)
        {
            foreach (var call in GetOutgoingCalls(program, declaration) ?? [])
            {
                if (call.To is null)
                {
                    continue;
                }

                var key = new LocationKey(new Location { Uri = call.To.Uri, Range = call.To.SelectionRange });
                if (seen.TryGetValue(key, out var existing))
                {
                    foreach (var fromRange in call.FromRanges ?? [])
                    {
                        if (!(existing.FromRanges ?? []).Any(r => Ls.RangeEquals(r, fromRange)))
                        {
                            existing.FromRanges = [.. existing.FromRanges ?? [], fromRange];
                        }
                    }
                }
                else
                {
                    seen[key] = call;
                    calls.Add(call);
                }
            }
        }

        if (calls.Count == 0)
        {
            return (new CallHierarchyOutgoingCallsOrNull(), null);
        }

        return (new CallHierarchyOutgoingCallsOrNull { CallHierarchyOutgoingCalls = calls.ToArray() }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) callHierarchyDeclarations(file *ast.SourceFile, position lsproto.Position, program *compiler.Program, allowSourceFile bool) []*ast.Node</c>（callhierarchy.go:1105-1136）。</summary>
    internal List<Node> CallHierarchyDeclarations(SourceFile file, Position position, Program program, bool allowSourceFile)
    {
        var positions = _converters!.FromLSPPositionForSourceFile(file, position, Feature.CallHierarchy);
        var declarations = new List<Node>();
        var seen = new Set<Node>();
        foreach (var mapped in positions)
        {
            if (!mapped.Fidelity.IsSingleSegment())
            {
                continue;
            }

            var mappedFile = mapped.Script;
            var pos = (int)mapped.Position;
            var node = mappedFile.AsNode();
            if (pos != 0)
            {
                node = TypeScript.AstNav.AstNav.GetTouchingPropertyName(mappedFile, pos);
            }

            if (node is null || (!allowSourceFile && node.Kind == Kind.SourceFile))
            {
                continue;
            }

            switch (Ls.ResolveCallHierarchyDeclaration(program, node))
            {
                case Node declaration:
                    if (seen.AddIfAbsent(declaration))
                    {
                        declarations.Add(declaration);
                    }

                    break;
                case Node[] nested:
                    foreach (var declaration in nested)
                    {
                        if (seen.AddIfAbsent(declaration))
                        {
                            declarations.Add(declaration);
                        }
                    }

                    break;
            }
        }

        return declarations;
    }
}
