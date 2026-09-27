// ported from: internal/transformers/declarations/transform.go:1-463
//   （ReferencedFilePair / OutputPaths / DeclarationEmitHost / thisPropertyAssignmentKey / DeclarationTransformer 结构体与构造函数、
//     GetDiagnostics、stripInternal 与 @internal 注释扫描、nodebuilder flags、visit、throwDiagnostic、visitSourceFile、
//     collectFileReferences / nodeOrSyntaxListChildren / flattenSyntaxLists / appendCjsExports、transformSourceFile、
//     createEmptyExports、transformAndReplaceLatePaintedStatements）
//
// 与 Go 的偏离（逐条）：
// 1) Go 的包级函数（needsScopeMarker…）落在静态类 Declarations（docs/architecture.md §2）。
// 2) `DeclarationTransformer` 按逻辑拆 4 个 partial 文件（用户批准，docs/architecture.md §2）：本文件为 1-463 行的那一段；
//    字段全部定义在本文件（其它分片只读）。
// 3) Go 的 `type thisPropertyAssignmentKey struct`（map key）→ C# struct + IEquatable/GetHashCode：
//    Go 的 map key 用逐字段 `==`，C# 的 `HashSet<T>` 走 `EqualityComparer<T>.Default`，不实现值相等就会退化成引用比较。
// 4) Go 的 `DeclarationEmitHost` 内嵌 `modulespecifiers.ModuleSpecifierGenerationHost` → C# 接口继承；Go 自己重新声明的
//    `GetCurrentDirectory`/`UseCaseSensitiveFileNames` 用 `new` 显式隐藏（同 ModuleSpecifiers 的既有做法）。
// 5) Go 的 `OutputPaths` 接口在 declarations 包内声明、由 compiler 的 outputPaths 结构体**结构性满足**；C# 无结构性满足，
//    故保留同名接口，`TypeScript.OutputPaths.OutputPaths` 需要显式声明实现它（已列入跨包缺失清单）。
// 6) Go 的 `map[ast.NodeId]*ast.Node` 里 nil 值表示"删除"，与"键不存在"语义不同 → C# `Dictionary<NodeId, Node?>`
//    + `TryGetValue`；`visitDeclarationStatements`（分片 C）里的 `map[id] == nil` 读法对应 `!TryGetValue || value == null`。
// 7) Go 的 `tx.state.lateMarkedStatements` 是 `[]*ast.Node`（`[1:]` 视图 + append）→ C# `Node[]`：`[1..]` + `Core.AppendIfUnique`
//    （docs/architecture.md §3.2：C# 数组无法做视图，切片退化成拷贝，语义一致）。
// 8) `panic(...)` → `throw new DebugFailureException(...)`（docs/ast.md §3.1：让编译器接受控制流）。
// 9) `iter.Seq[ast.CommentRange]` → `IEnumerable<CommentRange>`；Go 返回 nil Seq 的分支在 C# 返回 null，调用点 `?? []`。
// 10) Go 的 `context.FieldName = ...` 复合字面量 `ReferencedFilePair{file: …}` → C# 对象初始化器。
// 11) Go 的 `(*DeclarationTransformer).transformSourceFile` 转 PascalCase 后会与内嵌 `transformers.Transformer`
//     的导出方法 `TransformSourceFile` 撞名（Go 里是两个不同方法）→ 按 docs/checker-plan §5.2 的同名冲突规则加
//     `Worker` 后缀：`TransformSourceFileWorker`；基类的 `TransformSourceFile` 保持原样（Chain 仍走它）。
namespace TypeScript.Transformers.Declarations;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.ModuleSpecifiers;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Scanner.Scanner;
using static TypeScript.Transformers.Declarations.Declarations;

/// <summary>Go: <c>type ReferencedFilePair struct { file *ast.SourceFile; ref *ast.FileReference }</c>。</summary>
public sealed class ReferencedFilePair
{
    internal SourceFile _file = null!;
    internal FileReference _ref = null!;
}

/// <summary>Go: <c>type OutputPaths interface { DeclarationFilePath() string; JsFilePath() string }</c>。</summary>
public interface OutputPaths
{
    string DeclarationFilePath();

    string JsFilePath();
}

// Used to be passed in the TransformationContext, which is now just an EmitContext
/// <summary>Go: <c>type DeclarationEmitHost interface</c>。</summary>
public interface DeclarationEmitHost : ModuleSpecifierGenerationHost
{
    new string GetCurrentDirectory();

    new bool UseCaseSensitiveFileNames();

    SourceFile? GetSourceFileFromReference(SourceFile origin, FileReference @ref);

    OutputPaths GetOutputPathsFor(SourceFile file, bool forceDtsPaths);

    bool SourceFileMayBeEmitted(SourceFile file, bool forceDtsEmit);

    ModifierFlags GetEffectiveDeclarationFlags(Node node, ModifierFlags flags);

    EmitResolver GetEmitResolver();
}

/// <summary>
/// Go: <c>type thisPropertyAssignmentKey struct</c>。作为 <c>Collections.Set</c> 的 key 使用：
/// Go 的 map key 按逐字段 `==` 比较，故这里实现值相等（见文件头第 3 条）。
/// </summary>
internal struct ThisPropertyAssignmentKey : IEquatable<ThisPropertyAssignmentKey>
{
    internal string _name;
    internal Node? _node;
    internal bool _isStatic;
    internal bool _isPrivate;

    public readonly bool Equals(ThisPropertyAssignmentKey other)
    {
        return _name == other._name && ReferenceEquals(_node, other._node) && _isStatic == other._isStatic && _isPrivate == other._isPrivate;
    }

    public readonly override bool Equals(object? obj)
    {
        return obj is ThisPropertyAssignmentKey other && Equals(other);
    }

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(_name, _node, _isStatic, _isPrivate);
    }
}

public static partial class Declarations
{
    internal static ThisPropertyAssignmentKey GetThisPropertyAssignmentKey(Node? name, Node node, bool isStatic)
    {
        var isPrivate = IsPrivateIdentifier(name!);
        if (name != null && !IsDynamicName(name))
        {
            if (TryGetTextOfPropertyName(name, out var nameText))
            {
                return new ThisPropertyAssignmentKey { _name = nameText, _isStatic = isStatic, _isPrivate = isPrivate };
            }
        }
        return new ThisPropertyAssignmentKey { _node = node, _isStatic = isStatic, _isPrivate = isPrivate };
    }
}

public sealed partial class DeclarationTransformer : Transformer
{
    internal DeclarationEmitHost _host = null!;
    internal CompilerOptions _compilerOptions = null!;
    internal SymbolTrackerImpl _tracker = null!;
    internal SymbolTrackerSharedState _state = null!;
    internal EmitResolver _resolver = null!;
    internal string _declarationFilePath = "";
    internal string _declarationMapPath = "";

    internal bool _needsDeclare;
    internal bool _needsScopeFixMarker;
    internal bool _resultHasScopeMarker;
    internal Node? _enclosingDeclaration;
    internal bool _resultHasExternalModuleIndicator;
    internal bool _suppressNewDiagnosticContexts;
    internal Set<string> _witnessedCjsExports = new();
    internal Dictionary<NodeId, Node?> _lateStatementReplacementMap = new();
    internal Dictionary<NodeId, Node?> _expandoHosts = new(); // store the result of transforming expando hosts so they can be inserted later if the host is actually referenced
    internal Dictionary<NodeId, Node[]> _expandoMembers = new(); // store any found expando _members_ after transforming them so *if* the host is referenced, they can be emitted alongside it
    internal Dictionary<NodeId, BinaryExpression[]> _deferredExpandoAssignments = new(); // expando assignments whose host wasn't visible when collected, processed if the host is late-marked visible
    internal Set<ThisPropertyAssignmentKey> _seenProperties = new();
    internal Node[] _thisPropertyAssignmentsCollected = [];
    internal List<ReferencedFilePair> _rawReferencedFiles = [];
    internal FileReference[] _rawTypeReferenceDirectives = [];
    internal FileReference[] _rawLibReferenceDirectives = [];
    internal NodeVisitor _bindingNameVisitor = null!;
    internal NodeVisitor _expressionVisitor = null!;
    internal NodeVisitor _cjsExportAssignmentVisitor = null!;
    internal NodeVisitor _exportStrippingVisitor = null!;
    internal NodeVisitor _thisPropertyVisitor = null!;

    internal Node? _cjsExportAssignment;
    internal Node[] _cjsExportMembers = [];
    internal Node? _cjsExportAssignmentName; // tracks the name node used for `export =` in CJS module.exports assignments
    internal NodeVisitor _declareStrippingVisitor = null!;
    internal bool _inClassExpressionDeclaration; // true when serializing members of a class expression kept as a class declaration
}

public static partial class Declarations
{
    // TODO: Convert to transformers.TransformerFactory signature to allow more automatic composition with other transforms
    public static DeclarationTransformer NewDeclarationTransformer(DeclarationEmitHost host, EmitContext context, CompilerOptions compilerOptions, string declarationFilePath, string declarationMapPath)
    {
        var resolver = host.GetEmitResolver();
        var state = new SymbolTrackerSharedState
        {
            _isolatedDeclarations = compilerOptions.IsolatedDeclarations.IsTrue(),
            _stripInternal = compilerOptions.StripInternal.IsTrue(),
            _resolver = resolver,
        };
        var tracker = NewSymbolTracker(host, resolver, state);
        // TODO: Use new host GetOutputPathsFor method instead of passing in entrypoint paths (which will also better support bundled emit)
        var tx = new DeclarationTransformer
        {
            _host = host,
            _compilerOptions = compilerOptions,
            _tracker = tracker,
            _state = state,
            _resolver = resolver,
            _declarationFilePath = declarationFilePath,
            _declarationMapPath = declarationMapPath,
        };
        tx._state._reportExpandoFunctionErrors = node =>
        {
            if (!tx._state._isolatedDeclarations)
            {
                return;
            }
            var props = resolver.GetPropertiesOfContainerFunction(node);
            foreach (var p in props)
            {
                if (IsExpandoPropertyDeclaration(p.ValueDeclaration))
                {
                    Node errorTarget = p.ValueDeclaration!;
                    if (IsBinaryExpression(errorTarget))
                    {
                        errorTarget = errorTarget.AsBinaryExpression()!.Left!;
                    }
                    tx._state.AddDiagnostic(CreateDiagnosticForNode(errorTarget, Diagnostics.Assigning_properties_to_functions_without_declaring_them_is_not_supported_with_isolatedDeclarations_Add_an_explicit_declaration_for_the_properties_assigned_to_this_function));
                }
            }
        };
        tx.NewTransformer(tx.Visit, context);
        tx._bindingNameVisitor = tx.EmitContext().NewNodeVisitor(tx.VisitBindingName);
        tx._expressionVisitor = tx.EmitContext().NewNodeVisitor(tx.VisitNestedExpression);
        tx._exportStrippingVisitor = tx.EmitContext().NewNodeVisitor(tx.StripExportModifiers);
        tx._thisPropertyVisitor = tx.EmitContext().NewNodeVisitor(tx.VisitThisPropertyAssignments);
        tx._cjsExportAssignmentVisitor = tx.EmitContext().NewNodeVisitor(tx.VisitCJSExportAssignments);
        tx._declareStrippingVisitor = tx.EmitContext().NewNodeVisitor(tx.StripDeclareModifiers);
        return tx;
    }
}

public sealed partial class DeclarationTransformer
{
    public Diagnostic[] GetDiagnostics()
    {
        return [.. _state._diagnostics];
    }

    internal bool ShouldStripInternal(Node? node)
    {
        return _state._stripInternal && node != null && IsInternalDeclaration(node, _state._currentSourceFile!);
    }

    internal bool IsInternalDeclaration(Node? node, SourceFile sourceFile)
    {
        if (node == null)
        {
            return false;
        }
        var parseTreeNode = EmitContext().MostOriginal(node)!;
        if (!IsParseTreeNode(parseTreeNode))
        {
            return false;
        }
        if (parseTreeNode.Kind == Kind.Parameter)
        {
            var parameters = parseTreeNode.Parent!.Parameters()!;
            var paramIdx = System.Array.FindIndex(parameters, p => ReferenceEquals(p, parseTreeNode));
            Node? previousSibling = null;
            if (paramIdx > 0)
            {
                previousSibling = parameters[paramIdx - 1];
            }

            var text = sourceFile.Text();
            var commentRanges = new List<CommentRange>();

            if (previousSibling != null)
            {
                // to handle
                // ... parameters, /** @internal */
                // public param: string
                var trailingPos = SkipTriviaEx(text, previousSibling.End() + 1, new SkipTriviaOptions { StopAtComments = true });
                foreach (var comment in GetTrailingCommentRanges(Factory().AsNodeFactory(), text, trailingPos))
                {
                    commentRanges.Add(comment);
                }
                foreach (var comment in GetLeadingCommentRanges(Factory().AsNodeFactory(), text, node.Pos()))
                {
                    commentRanges.Add(comment);
                }
            }
            else
            {
                var trailingPos = SkipTriviaEx(text, node.Pos(), new SkipTriviaOptions { StopAtComments = true });
                foreach (var comment in GetTrailingCommentRanges(Factory().AsNodeFactory(), text, trailingPos))
                {
                    commentRanges.Add(comment);
                }
            }

            if (commentRanges.Count > 0)
            {
                return HasInternalAnnotation(commentRanges[^1], sourceFile);
            }
            return false;
        }

        foreach (var commentRange in GetLeadingCommentRangesOfNode(parseTreeNode, sourceFile) ?? [])
        {
            if (HasInternalAnnotation(commentRange, sourceFile))
            {
                return true;
            }
        }
        return false;
    }

    internal IEnumerable<CommentRange>? GetLeadingCommentRangesOfNode(Node? node, SourceFile sourceFile)
    {
        if (node == null || node.Kind == Kind.JsxText)
        {
            return null;
        }
        return GetLeadingCommentRanges(Factory().AsNodeFactory(), sourceFile.Text(), node.Pos());
    }
}

public static partial class Declarations
{
    internal static bool HasInternalAnnotation(CommentRange commentRange, SourceFile sourceFile)
    {
        var text = sourceFile.Text();
        var comment = text[commentRange.Range.Pos()..commentRange.Range.End()];
        return comment.Contains("@internal");
    }

    internal const Flags DeclarationEmitNodeBuilderFlags = Flags.MultilineObjectLiterals |
        Flags.WriteClassExpressionAsTypeLiteral |
        Flags.UseTypeOfFunction |
        Flags.UseStructuralFallback |
        Flags.AllowEmptyTuple |
        Flags.GenerateNamesForShadowedTypeParams |
        Flags.NoTruncation;

    internal const InternalFlags DeclarationEmitInternalNodeBuilderFlags = InternalFlags.AllowUnresolvedNames;
}

public sealed partial class DeclarationTransformer
{
    // functions as both `visitDeclarationStatements` and `transformRoot`, utilitzing SyntaxList nodes
    internal Node? Visit(Node? node)
    {
        if (node == null)
        {
            return null;
        }
        switch (node.Kind)
        {
            case Kind.SourceFile:
                return VisitSourceFile(node.AsSourceFile()!);
            // statements we keep but do something to
            case Kind.FunctionDeclaration:
            case Kind.ModuleDeclaration:
            case Kind.ImportEqualsDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.ClassDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.EnumDeclaration:
            case Kind.VariableStatement:
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ExportDeclaration:
            case Kind.ExportAssignment:
                return VisitDeclarationStatements(node);
            // statements we elide
            case Kind.BreakStatement:
            case Kind.ContinueStatement:
            case Kind.DebuggerStatement:
            case Kind.DoStatement:
            case Kind.EmptyStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.ForStatement:
            case Kind.IfStatement:
            case Kind.LabeledStatement:
            case Kind.ReturnStatement:
            case Kind.SwitchStatement:
            case Kind.ThrowStatement:
            case Kind.TryStatement:
            case Kind.WhileStatement:
            case Kind.WithStatement:
            case Kind.NotEmittedStatement:
            case Kind.Block:
            case Kind.MissingDeclaration:
            case Kind.ExpressionStatement:
                return null;
            // parts of things, things we just visit children of
            default:
                return VisitDeclarationSubtree(node);
        }
    }
}

public static partial class Declarations
{
    internal static SymbolAccessibilityDiagnostic? ThrowDiagnostic(SymbolAccessibilityResult result)
    {
        throw new DebugFailureException("Diagnostic emitted without context");
    }

    internal static Node[] NodeOrSyntaxListChildren(Node node)
    {
        if (IsSyntaxList(node))
        {
            return node.AsSyntaxList()!.Children ?? [];
        }
        return [node];
    }

    internal static Node[] FlattenSyntaxLists(Node[]? nodes)
    {
        return FlatMap(nodes, NodeOrSyntaxListChildren);
    }

    internal static Node CreateEmptyExports(TypeScript.Ast.NodeFactory factory)
    {
        return factory.NewExportDeclaration(null /*isTypeOnly*/, false, factory.NewNamedExports(factory.NewNodeList([])), null, null);
    }
}

public sealed partial class DeclarationTransformer
{
    internal Node? VisitSourceFile(SourceFile node)
    {
        _cjsExportAssignmentName = null;
        if (node.IsDeclarationFile)
        {
            return node;
        }

        _needsDeclare = true;
        _needsScopeFixMarker = false;
        _resultHasScopeMarker = false;
        _enclosingDeclaration = node;
        _state._getSymbolAccessibilityDiagnostic = ThrowDiagnostic;
        _resultHasExternalModuleIndicator = false;
        _suppressNewDiagnosticContexts = false;
        _state._lateMarkedStatements = [];
        _lateStatementReplacementMap = [];
        _expandoHosts = [];
        _expandoMembers = [];
        _deferredExpandoAssignments = [];
        _rawReferencedFiles = [];
        _rawTypeReferenceDirectives = [];
        _rawLibReferenceDirectives = [];
        _witnessedCjsExports.Clear();
        _state._currentSourceFile = node;
        CollectFileReferences(node);
        _resolver.PrecalculateDeclarationEmitVisibility(EmitContext().MostOriginal(node)!.AsSourceFile()!);
        var updated = TransformSourceFileWorker(node);
        _state._currentSourceFile = null;
        return updated;
    }

    internal void CollectFileReferences(SourceFile sourceFile)
    {
        _rawReferencedFiles.AddRange(Map(sourceFile.ReferencedFiles, @ref => new ReferencedFilePair { _file = sourceFile, _ref = @ref! }));
        _rawTypeReferenceDirectives = Concatenate(_rawTypeReferenceDirectives, sourceFile.TypeReferenceDirectives ?? []);
        _rawLibReferenceDirectives = Concatenate(_rawLibReferenceDirectives, sourceFile.LibReferenceDirectives ?? []);
    }

    internal NodeList AppendCjsExports(NodeList combinedStatements)
    {
        var result = new List<Node>();
        if (_cjsExportAssignment != null)
        {
            result.Add(_cjsExportAssignment);
        }
        result.AddRange(_cjsExportMembers);
        result.AddRange(combinedStatements.Nodes ?? []);
        var statementNodes = FlattenSyntaxLists(result.ToArray());
        if (statementNodes.Length != (combinedStatements.Nodes?.Length ?? 0))
        {
            combinedStatements = Factory().NewNodeList(statementNodes);
        }
        return combinedStatements;
    }

    internal Node? TransformSourceFileWorker(SourceFile node)
    {
        _cjsExportAssignment = null;
        _cjsExportAssignmentName = null;
        _cjsExportMembers = [];
        try
        {
            _cjsExportAssignmentVisitor.VisitNode(node); // collect nested module.exports= assignments
            _expressionVisitor.VisitNode(node); // collect expando members (requires any export assignment be located in advance)
            var statements = Visitor().VisitNodes(node.Statements)!;
            var combinedStatements = TransformAndReplaceLatePaintedStatements(statements);
            combinedStatements = AppendCjsExports(combinedStatements);
            combinedStatements.Loc = statements.Loc; // setTextRange
            if (IsExternalOrCommonJSModule(node))
            {
                if (IsInJSFile(node))
                {
                    Symbol? exportEquals = null;
                    if (node.Symbol!.Exports != null && node.Symbol!.Exports.TryGetValue(InternalSymbolNameExportEquals, out var exportedValue))
                    {
                        exportEquals = exportedValue;
                    }
                    if (exportEquals != null && (exportEquals.Declarations?.Length ?? 0) > 1)
                    {
                        foreach (var declaration in exportEquals.Declarations ?? [])
                        {
                            _state.AddDiagnostic(CreateDiagnosticForNode(declaration, Diagnostics.Multiple_module_exports_assignments_cannot_be_serialized_for_declaration_emit));
                        }
                    }
                }
                if (!_resultHasExternalModuleIndicator || (_needsScopeFixMarker && !_resultHasScopeMarker))
                {
                    var marker = CreateEmptyExports(Factory());
                    var newList = Concatenate(combinedStatements.Nodes, [marker]);
                    var withMarker = Factory().NewNodeList(newList);
                    withMarker.Loc = combinedStatements.Loc;
                    combinedStatements = withMarker;
                }
            }
            var outputFilePath = GetDirectoryPath(NormalizeSlashes(_declarationFilePath));
            var result = Factory().UpdateSourceFile(node, combinedStatements, node.EndOfFileToken);
            result.AsSourceFile()!.LibReferenceDirectives = GetLibReferences();
            result.AsSourceFile()!.TypeReferenceDirectives = GetTypeReferences();
            result.AsSourceFile()!.IsDeclarationFile = true;
            result.AsSourceFile()!.ReferencedFiles = GetReferencedFiles(outputFilePath);
            return result;
        }
        finally
        {
            _cjsExportAssignment = null;
            _cjsExportAssignmentName = null;
            _cjsExportMembers = [];
        }
    }

    internal NodeList TransformAndReplaceLatePaintedStatements(NodeList statements)
    {
        // This is a `while` loop because `handleSymbolAccessibilityError` can see additional import aliases marked as visible during
        // error handling which must now be included in the output and themselves checked for errors.
        // For example:
        // ```
        // module A {
        //   export module Q {}
        //   import B = Q;
        //   import C = B;
        //   export import D = C;
        // }
        // ```
        // In such a scenario, only Q and D are initially visible, but we don't consider imports as private names - instead we say they if they are referenced they must
        // be recorded. So while checking D's visibility we mark C as visible, then we must check C which in turn marks B, completing the chain of
        // dependent imports and allowing a valid declaration file output. Today, this dependent alias marking only happens for internal import aliases.
        while (true)
        {
            if (_state._lateMarkedStatements.Length == 0)
            {
                break;
            }

            var next = _state._lateMarkedStatements[0];
            _state._lateMarkedStatements = _state._lateMarkedStatements[1..];

            var saveNeedsDeclare = _needsDeclare;
            _needsDeclare = next.Parent != null && IsSourceFile(next.Parent!);

            var result = TransformTopLevelDeclaration(next);

            _needsDeclare = saveNeedsDeclare;
            var original = EmitContext().MostOriginal(next)!;
            var id = GetNodeId(original);
            _lateStatementReplacementMap[id] = result;
        }

        // And lastly, we need to get the final form of all those indetermine import declarations from before and add them to the output list
        // (and remove them from the set to examine for outter declarations)
        var results = new List<Node>(statements.Nodes?.Length ?? 0);
        foreach (var statement in statements.Nodes ?? [])
        {
            if (!IsLateVisibilityPaintedStatement(statement))
            {
                results.Add(statement);
                continue;
            }
            var original = EmitContext().MostOriginal(statement)!;
            var id = GetNodeId(original);
            if (!_lateStatementReplacementMap.TryGetValue(id, out var replacement))
            {
                results.Add(statement);
                continue; // not replaced
            }
            if (replacement == null)
            {
                continue; // deleted
            }
            if (replacement.Kind == Kind.SyntaxList)
            {
                if (!_needsScopeFixMarker || !_resultHasExternalModuleIndicator)
                {
                    foreach (var elem in replacement.AsSyntaxList()!.Children ?? [])
                    {
                        if (NeedsScopeMarker(elem))
                        {
                            _needsScopeFixMarker = true;
                        }
                        if (IsSourceFile(statement.Parent!) && IsExternalModuleIndicator(elem))
                        {
                            _resultHasExternalModuleIndicator = true;
                        }
                    }
                }
                results.AddRange(replacement.AsSyntaxList()!.Children ?? []);
            }
            else
            {
                if (NeedsScopeMarker(replacement))
                {
                    _needsScopeFixMarker = true;
                }
                if (IsSourceFile(statement.Parent!) && IsExternalModuleIndicator(replacement))
                {
                    _resultHasExternalModuleIndicator = true;
                }
                results.Add(replacement);
            }
        }

        return Factory().NewNodeList(results.ToArray());
    }

}
