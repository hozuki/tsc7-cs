// ported from: internal/printer/emitcontext.go
//
// 偏离 Go 的地方：
// 1) Go 的 `emitContextPool sync.Pool` → C# 的 `[ThreadStatic]` 单槽（同 singleline writer；见该文件头第 1 条）。
//    `GetEmitContext() (*EmitContext, func())` → 元组 `(EmitContext Context, Action Cleanup)`。
// 2) 未导出类型按 C# 习惯命名并就近内嵌：`environmentFlags` → 私有枚举 `EnvironmentFlags`，`varScope` →
//    私有类 `VarScope`（只在 emitcontext.go 里用），`emitNodeFlags` → 私有枚举 `EmitNodeFlags`，`emitNode` →
//    私有类 `EmitNode`（`core.LinkStore[K,V]` 要求 `V : class, new()`）。
// 3) Go 的 `map[*ast.MemberName]X`（MemberName = Identifier | PrivateIdentifier 的联合）在 C# 里没有联合类型，
//    键类型直接用 `Node`（对应的就是这两种 kind 的节点；docs/ast.md §2 的 "别名 → Node"）。
// 4) Go 的 `map[*ast.Node]X` / `core.LinkStore` 键：Node 没有覆写 Equals/GetHashCode（引用同一性，docs/ast.md §2），
//    所以 `Dictionary<Node, X>` 的默认比较器就是 Go 的指针相等语义。
// 5) `Reset()`：Go 用 `*c = EmitContext{Factory: c.Factory}` 整体清空（含 LinkStore/Stack/OrderedSet 的零值）；
//    C# 逐字段重建（LinkStore 没有 Clear，见 core/linkstore.go 的 C# 移植）。
// 6) Go 的 `slices.Clone`/`append` 在 C# 用 `(T[])x.Clone()` / `List<T>` + `ToArray()`（docs §3.2）；
//    `EmitNode.LeadingComments` 这类"Go 切片字段"保持数组形态（读取点 `for _, c := range …` 逐条对应）。
// 7) Go 的 `TokenSourceMapRange(node, kind) (core.TextRange, bool)` → `TryTokenSourceMapRange(node, kind, out TextRange)`
//    （docs §3.2 的 `(T, bool)` → `TryX`）。
// 8) **Go 的 string 零值是空串，C# 的字段默认是 null**：`AutoGenerateOptions.Prefix/Suffix`、`AutoGenerateInfo.Prefix/
//    Suffix`、`SynthesizedComment.Text` 都显式 `= ""`（否则 `options.Prefix.Length` 这类读取处 NRE）。
//    `AutoGenerateOptions` 另外提供无参构造函数，`new AutoGenerateOptions()` = Go 的 `AutoGenerateOptions{}`；
//    `default(AutoGenerateOptions)` 会绕过字段初始化器，调用点不要用。
using System.Collections.Generic;
using System.Threading;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using EmitFlagsType = TypeScript.Printer.EmitFlags;
using AstNodeFactory = TypeScript.Ast.NodeFactory;

namespace TypeScript.Printer;

/// <summary>
/// Stores side-table information used during transformation that can be read by the printer to customize emit.
///
/// NOTE: EmitContext is not guaranteed to be thread-safe.
/// </summary>
public sealed class EmitContext
{
    /// <summary>Required. The NodeFactory to use to create new nodes.</summary>
    public NodeFactory Factory = null!;

    internal Dictionary<Node, AutoGenerateInfo>? _autoGenerate;
    internal Dictionary<Node, Node>? _textSource;
    private Dictionary<Node, Node>? _original;
    private LinkStore<Node, EmitNode> _emitNodes = new();
    private Dictionary<Node, Node>? _assignedName;
    private Dictionary<Node, Node>? _classThis;
    private Core.Stack<VarScope> _varScopeStack = new();
    private Core.Stack<VarScope> _letScopeStack = new();
    private OrderedSet<EmitHelper> _emitHelpers = new();

    [Flags]
    private enum EnvironmentFlags
    {
        None = 0,
        /// <summary>currently visiting a parameter list</summary>
        InParameters = 1 << 0,
        /// <summary>a temp variable was hoisted while visiting a parameter list</summary>
        VariablesHoistedInParameters = 1 << 1,
    }

    private sealed class VarScope
    {
        public readonly List<Node> Variables = [];
        public readonly List<Node> Functions = [];
        public EnvironmentFlags Flags;
        public readonly List<Node> InitializationStatements = [];
    }

    public EmitContext()
    {
    }

    public void Reset()
    {
        // Go: *c = EmitContext{Factory: c.Factory}
        _autoGenerate = null;
        _textSource = null;
        _original = null;
        _emitNodes = new LinkStore<Node, EmitNode>();
        _assignedName = null;
        _classThis = null;
        _varScopeStack = new Core.Stack<VarScope>();
        _letScopeStack = new Core.Stack<VarScope>();
        _emitHelpers = new OrderedSet<EmitHelper>();
    }

    internal void OnCreate(Node node)
    {
        node.Flags |= NodeFlags.Synthesized;
    }

    internal void OnUpdate(Node updated, Node original)
    {
        SetOriginal(updated, original);
    }

    internal void OnClone(Node updated, Node original)
    {
        SetOriginal(updated, original);
        if (IsIdentifier(updated) || IsPrivateIdentifier(updated))
        {
            if (_autoGenerate is not null && _autoGenerate.TryGetValue(original, out var autoGenerate))
            {
                _autoGenerate[updated] = new AutoGenerateInfo(autoGenerate);
            }
        }
    }

    /// <summary>Creates a new NodeVisitor attached to this EmitContext.</summary>
    public NodeVisitor NewNodeVisitor(NodeVisitorVisit visit)
    {
        return new NodeVisitor(visit, Factory.AsNodeFactory(), new NodeVisitorHooks
        {
            VisitParameters = VisitParameters,
            VisitFunctionBody = VisitFunctionBody,
            VisitIterationBody = VisitIterationBody,
            VisitTopLevelStatements = VisitVariableEnvironment,
            VisitEmbeddedStatement = VisitEmbeddedStatement,
        });
    }

    //
    // Environment tracking
    //

    /// <summary>
    /// Starts a new VariableEnvironment used to track hoisted `var` statements and function declarations.
    ///
    /// see: https://tc39.es/ecma262/#table-additional-state-components-for-ecmascript-code-execution-contexts
    ///
    /// NOTE: This is the equivalent of `transformContext.startLexicalEnvironment` in Strada.
    /// </summary>
    public void StartVariableEnvironment()
    {
        _varScopeStack.Push(new VarScope());
        StartLexicalEnvironment();
    }

    /// <summary>
    /// Ends the current VariableEnvironment, returning a list of statements that should be emitted at the start of
    /// the current scope.
    ///
    /// NOTE: This is the equivalent of `transformContext.endLexicalEnvironment` in Strada.
    /// </summary>
    public Node[] EndVariableEnvironment()
    {
        var scope = _varScopeStack.Pop();
        var statements = new List<Node>();
        if (scope.Functions.Count > 0)
        {
            // Go: slices.Clone(scope.functions)
            statements.AddRange(scope.Functions);
        }
        if (scope.Variables.Count > 0)
        {
            var varDeclList = Factory.NewVariableDeclarationList(Factory.NewNodeList(scope.Variables.ToArray()), NodeFlags.None);
            var varStatement = Factory.NewVariableStatement(null /*modifiers*/, varDeclList);
            SetEmitFlags(varStatement, EmitFlagsType.CustomPrologue);
            statements.Add(varStatement);
        }
        if (scope.InitializationStatements.Count > 0)
        {
            statements.AddRange(scope.InitializationStatements);
        }
        statements.AddRange(EndLexicalEnvironment());
        return statements.ToArray();
    }

    /// <summary>Invokes <c>EndVariableEnvironment()</c> and merges the results into `statements`.</summary>
    public NodeList? EndAndMergeVariableEnvironmentList(NodeList? statements)
    {
        Node[]? nodes = null;
        if (statements is not null)
        {
            nodes = statements.Nodes;
        }

        var (result, changed) = EndAndMergeVariableEnvironmentCore(nodes);
        if (changed)
        {
            var list = Factory.NewNodeList(result);
            list.Loc = statements!.Loc;
            return list;
        }

        return statements;
    }

    /// <summary>Invokes <c>EndVariableEnvironment()</c> and merges the results into `statements`.</summary>
    public Node[]? EndAndMergeVariableEnvironment(Node[]? statements)
    {
        var (result, _) = EndAndMergeVariableEnvironmentCore(statements);
        return result;
    }

    private (Node[]?, bool) EndAndMergeVariableEnvironmentCore(Node[]? statements)
    {
        return MergeEnvironmentCore(statements, EndVariableEnvironment());
    }

    /// <summary>
    /// Adds a `var` declaration to the current VariableEnvironment.
    ///
    /// NOTE: This is the equivalent of `transformContext.hoistVariableDeclaration` in Strada.
    /// </summary>
    public void AddVariableDeclaration(Node name)
    {
        var varDecl = Factory.NewVariableDeclaration(name, null /*exclamationToken*/, null /*typeNode*/, null /*initializer*/);
        SetEmitFlags(varDecl, EmitFlagsType.NoNestedSourceMaps);
        var scope = _varScopeStack.Peek();
        scope.Variables.Add(varDecl);
        if ((scope.Flags & EnvironmentFlags.InParameters) != 0)
        {
            scope.Flags |= EnvironmentFlags.VariablesHoistedInParameters;
        }
    }

    /// <summary>
    /// Adds a hoisted function declaration to the current VariableEnvironment.
    ///
    /// NOTE: This is the equivalent of `transformContext.hoistFunctionDeclaration` in Strada.
    /// </summary>
    public void AddHoistedFunctionDeclaration(Node node)
    {
        SetEmitFlags(node, EmitFlagsType.CustomPrologue);
        var scope = _varScopeStack.Peek();
        scope.Functions.Add(node);
    }

    /// <summary>
    /// Starts a new LexicalEnvironment used to track block-scoped `let`, `const`, and `using` declarations.
    ///
    /// see: https://tc39.es/ecma262/#table-additional-state-components-for-ecmascript-code-execution-contexts
    ///
    /// NOTE: This is the equivalent of `transformContext.startBlockScope` in Strada.
    /// NOTE: This is *not* the same as `startLexicalEnvironment` in Strada as that method is incorrectly named.
    /// </summary>
    public void StartLexicalEnvironment()
    {
        _letScopeStack.Push(new VarScope());
    }

    /// <summary>
    /// Ends the current EndLexicalEnvironment, returning a list of statements that should be emitted at the start of
    /// the current scope.
    ///
    /// NOTE: This is the equivalent of `transformContext.endLexicalEnvironment` in Strada.
    /// NOTE: This is *not* the same as `endLexicalEnvironment` in Strada as that method is incorrectly named.
    /// </summary>
    public Node[] EndLexicalEnvironment()
    {
        var scope = _letScopeStack.Pop();
        var statements = new List<Node>();
        if (scope.Variables.Count > 0)
        {
            var varDeclList = Factory.NewVariableDeclarationList(Factory.NewNodeList(scope.Variables.ToArray()), NodeFlags.Let);
            var varStatement = Factory.NewVariableStatement(null /*modifiers*/, varDeclList);
            SetEmitFlags(varStatement, EmitFlagsType.CustomPrologue);
            statements.Add(varStatement);
        }
        return statements.ToArray();
    }

    /// <summary>Invokes <c>EndLexicalEnvironment()</c> and merges the results into `statements`.</summary>
    public NodeList? EndAndMergeLexicalEnvironmentList(NodeList? statements)
    {
        Node[]? nodes = null;
        if (statements is not null)
        {
            nodes = statements.Nodes;
        }

        var (result, changed) = EndAndMergeLexicalEnvironmentCore(nodes);
        if (changed)
        {
            var list = Factory.NewNodeList(result);
            list.Loc = statements!.Loc;
            return list;
        }

        return statements;
    }

    /// <summary>Invokes <c>EndLexicalEnvironment()</c> and merges the results into `statements`.</summary>
    public Node[]? EndAndMergeLexicalEnvironment(Node[]? statements)
    {
        var (result, _) = EndAndMergeLexicalEnvironmentCore(statements);
        return result;
    }

    /// <summary>Invokes <c>EndLexicalEnvironment()</c> and merges the results into `statements`.</summary>
    private (Node[]?, bool) EndAndMergeLexicalEnvironmentCore(Node[]? statements)
    {
        return MergeEnvironmentCore(statements, EndLexicalEnvironment());
    }

    /// <summary>Adds a `let` declaration to the current LexicalEnvironment.</summary>
    public void AddLexicalDeclaration(Node name)
    {
        var varDecl = Factory.NewVariableDeclaration(name, null /*exclamationToken*/, null /*typeNode*/, null /*initializer*/);
        SetEmitFlags(varDecl, EmitFlagsType.NoNestedSourceMaps);
        var scope = _letScopeStack.Peek();
        scope.Variables.Add(varDecl);
    }

    /// <summary>
    /// Merges declarations produced by <c>EndVariableEnvironment()</c> or <c>EndLexicalEnvironment()</c> into a
    /// statement list.
    /// </summary>
    public NodeList MergeEnvironmentList(NodeList statements, Node[] declarations)
    {
        var (result, changed) = MergeEnvironmentCore(statements.Nodes, declarations);
        if (changed)
        {
            var list = Factory.NewNodeList(result);
            list.Loc = statements.Loc;
            return list;
        }
        return statements;
    }

    /// <summary>
    /// Merges declarations produced by <c>EndVariableEnvironment()</c> or <c>EndLexicalEnvironment()</c> into a
    /// slice of statements.
    /// </summary>
    public Node[]? MergeEnvironment(Node[]? statements, Node[] declarations)
    {
        var (result, _) = MergeEnvironmentCore(statements, declarations);
        return result;
    }

    private (Node[]?, bool) MergeEnvironmentCore(Node[]? statements, Node[] declarations)
    {
        if (declarations.Length == 0)
        {
            return (statements, false);
        }

        // When we merge new lexical statements into an existing statement list, we merge them in the following manner:
        //
        // Given:
        //
        // | Left                               | Right                               |
        // |------------------------------------|-------------------------------------|
        // | [standard prologues (left)]        | [standard prologues (right)]        |
        // | [hoisted functions (left)]         | [hoisted functions (right)]         |
        // | [hoisted variables (left)]         | [hoisted variables (right)]         |
        // | [lexical init statements (left)]   | [lexical init statements (right)]   |
        // | [other statements (left)]          |                                     |
        //
        // The resulting statement list will be:
        //
        // | Result                              |
        // |-------------------------------------|
        // | [standard prologues (right)]        |
        // | [standard prologues (left)]         |
        // | [hoisted functions (right)]         |
        // | [hoisted functions (left)]          |
        // | [hoisted variables (right)]         |
        // | [hoisted variables (left)]          |
        // | [lexical init statements (right)]   |
        // | [lexical init statements (left)]    |
        // | [other statements (left)]           |
        //
        // NOTE: It is expected that new lexical init statements must be evaluated before existing lexical init
        // statements, as the prior transformation may depend on the evaluation of the lexical init statements to be
        // in the correct state.

        var changed = false;

        // find standard prologues on left in the following order: standard directives, hoisted functions, hoisted
        // variables, other custom
        statements ??= [];
        var leftStandardPrologueEnd = Printer.FindSpanEnd(statements, IsPrologueDirective, 0);
        var leftHoistedFunctionsEnd = Printer.FindSpanEndWithEmitContext(this, statements, (c, s) => c.IsHoistedFunction(s), leftStandardPrologueEnd);
        var leftHoistedVariablesEnd = Printer.FindSpanEndWithEmitContext(this, statements, (c, s) => c.IsHoistedVariableStatement(s), leftHoistedFunctionsEnd);

        // find standard prologues on right in the following order: standard directives, hoisted functions, hoisted
        // variables, other custom
        var rightStandardPrologueEnd = Printer.FindSpanEnd(declarations, IsPrologueDirective, 0);
        var rightHoistedFunctionsEnd = Printer.FindSpanEndWithEmitContext(this, declarations, (c, s) => c.IsHoistedFunction(s), rightStandardPrologueEnd);
        var rightHoistedVariablesEnd = Printer.FindSpanEndWithEmitContext(this, declarations, (c, s) => c.IsHoistedVariableStatement(s), rightHoistedFunctionsEnd);
        var rightCustomPrologueEnd = Printer.FindSpanEndWithEmitContext(this, declarations, (c, s) => c.IsCustomPrologue(s), rightHoistedVariablesEnd);
        if (rightCustomPrologueEnd != declarations.Length)
        {
            Debug.Fail("Expected declarations to be valid standard or custom prologues");
        }

        var left = statements;

        // splice other custom prologues from right into left
        if (rightCustomPrologueEnd > rightHoistedVariablesEnd)
        {
            left = Splice(left, leftHoistedVariablesEnd, 0, declarations[rightHoistedVariablesEnd..rightCustomPrologueEnd]);
            changed = true;
        }

        // splice hoisted variables from right into left
        if (rightHoistedVariablesEnd > rightHoistedFunctionsEnd)
        {
            left = Splice(left, leftHoistedFunctionsEnd, 0, declarations[rightHoistedFunctionsEnd..rightHoistedVariablesEnd]);
            changed = true;
        }

        // splice hoisted functions from right into left
        if (rightHoistedFunctionsEnd > rightStandardPrologueEnd)
        {
            left = Splice(left, leftStandardPrologueEnd, 0, declarations[rightStandardPrologueEnd..rightHoistedFunctionsEnd]);
            changed = true;
        }

        // splice standard prologues from right into left (that are not already in left)
        if (rightStandardPrologueEnd > 0)
        {
            if (leftStandardPrologueEnd == 0)
            {
                left = Splice(left, 0, 0, declarations[..rightStandardPrologueEnd]);
                changed = true;
            }
            else
            {
                var leftPrologues = new Set<string>();
                for (var i = 0; i < leftStandardPrologueEnd; i++)
                {
                    var leftPrologue = statements[i];
                    leftPrologues.Add(leftPrologue.Expression()!.Text());
                }
                for (var i = rightStandardPrologueEnd - 1; i >= 0; i--)
                {
                    var rightPrologue = declarations[i];
                    if (!leftPrologues.Has(rightPrologue.Expression()!.Text()))
                    {
                        left = Concatenate([rightPrologue], left);
                        changed = true;
                    }
                }
            }
        }

        return (left, changed);
    }

    internal bool IsCustomPrologue(Node node)
    {
        return (EmitFlags(node) & EmitFlagsType.CustomPrologue) != 0;
    }

    internal bool IsHoistedFunction(Node node)
    {
        return IsCustomPrologue(node) && IsFunctionDeclaration(node);
    }

    internal bool IsHoistedVariableStatement(Node node)
    {
        return IsCustomPrologue(node) &&
            IsVariableStatement(node) &&
            Every(node.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes, n => Printer.IsHoistedVariable(n.AsVariableDeclaration()!));
    }

    //
    // Name Generation
    //

    /// <summary>Gets whether a given name has an associated AutoGenerateInfo entry.</summary>
    public bool HasAutoGenerateInfo(Node? node)
    {
        if (node is not null)
        {
            return _autoGenerate is not null && _autoGenerate.ContainsKey(node);
        }
        return false;
    }

    /// <summary>Gets the associated AutoGenerateInfo entry for a given name.</summary>
    public AutoGenerateInfo? GetAutoGenerateInfo(Node? name)
    {
        if (name is null)
        {
            return null;
        }
        return _autoGenerate is not null && _autoGenerate.TryGetValue(name, out var info) ? info : null;
    }

    /// <summary>
    /// Walks the associated AutoGenerateInfo entries of a name to find the root Node from which the name should be
    /// generated.
    /// </summary>
    public Node GetNodeForGeneratedName(Node name)
    {
        if (_autoGenerate is not null && _autoGenerate.TryGetValue(name, out var autoGenerate) && autoGenerate.Flags.IsNode())
        {
            return GetNodeForGeneratedNameWorker(autoGenerate.Node!, autoGenerate.Id);
        }
        return name;
    }

    internal Node GetNodeForGeneratedNameWorker(Node node, AutoGenerateId autoGenerateId)
    {
        var original = Original(node);
        while (original is not null)
        {
            node = original;
            if (IsMemberName(node))
            {
                // if "node" is a different generated name (having a different "autoGenerateId"), use it and stop
                // traversing.
                var autoGenerate = _autoGenerate is not null && _autoGenerate.TryGetValue(node, out var info) ? info : null;
                if (autoGenerate is null || (autoGenerate.Flags.IsNode() && autoGenerate.Id != autoGenerateId))
                {
                    break;
                }
                if (autoGenerate.Flags.IsNode())
                {
                    original = autoGenerate.Node;
                    continue;
                }
            }
            original = Original(node);
        }
        return node;
    }

    //
    // Original Node Tracking
    //

    /// <summary>
    /// Sets the original node for a given node.
    ///
    /// NOTE: This is the equivalent to `setOriginalNode` in Strada.
    /// </summary>
    public void SetOriginal(Node node, Node? original)
    {
        SetOriginalEx(node, original, false);
    }

    public void UnsetOriginal(Node node)
    {
        _original?.Remove(node);
    }

    public void SetOriginalEx(Node node, Node? original, bool allowOverwrite)
    {
        if (original is null)
        {
            throw new DebugFailureException("Original cannot be nil.");
        }

        _original ??= [];

        if (!_original.TryGetValue(node, out var existing))
        {
            _original[node] = original!;
            var emitNode = _emitNodes.TryGet(original);
            if (emitNode is not null)
            {
                _emitNodes.Get(node).CopyFrom(emitNode);
            }
        }
        else if (!allowOverwrite && !ReferenceEquals(existing, original))
        {
            Debug.Fail("Original node already set.");
        }
        else if (allowOverwrite)
        {
            _original[node] = original!;
        }
    }

    /// <summary>
    /// Gets the original node for a given node.
    ///
    /// NOTE: This is the equivalent to reading `node.original` in Strada.
    /// </summary>
    public Node? Original(Node node)
    {
        if (_original is not null && _original.TryGetValue(node, out var original))
        {
            return original;
        }
        return null;
    }

    /// <summary>
    /// Gets the most original node associated with this node by walking Original pointers.
    ///
    /// NOTE: This method is analogous to `getOriginalNode` in the old compiler, but the name has changed to avoid
    /// accidental conflation with `SetOriginal`/`Original`
    /// </summary>
    public Node? MostOriginal(Node? node)
    {
        if (node is not null)
        {
            var original = Original(node);
            while (original is not null)
            {
                node = original;
                original = Original(node);
            }
        }
        return node;
    }

    /// <summary>
    /// Gets the original parse tree node for a given node.
    ///
    /// NOTE: This is the equivalent of `getParseTreeNode` in Strada.
    /// </summary>
    public Node? ParseNode(Node? node)
    {
        node = MostOriginal(node);
        if (node is not null && IsParseTreeNode(node))
        {
            return node;
        }
        return null;
    }

    public bool IsFileLevelUniqueName(SourceFile sourceFile, string name, Func<string, bool>? hasGlobalName)
    {
        if (hasGlobalName is not null && hasGlobalName(name))
        {
            return false;
        }
        sourceFile = MostOriginal(sourceFile.AsNode())!.AsSourceFile()!;
        return !sourceFile.HasIdentifier(name);
    }

    //
    // Emit-related Data
    //

    [Flags]
    private enum EmitNodeFlags : uint
    {
        HasCommentRange = 1 << 0,
        HasSourceMapRange = 1 << 1,
    }

    /// <summary>Go 的 <c>emitNode</c>（emitcontext.go:551-566）。</summary>
    private sealed class EmitNode
    {
        public EmitNodeFlags Flags;
        public EmitFlags EmitFlags;
        public TextRange CommentRange;
        public TextRange SourceMapRange;
        public Dictionary<Kind, TextRange>? TokenSourceMapRanges;
        public EmitHelper[]? Helpers;
        public Node? ExternalHelpersModuleName;
        public SynthesizedComment[]? LeadingComments;
        public SynthesizedComment[]? TrailingComments;
        public Node? TypeNode;
        public SnippetElement? SnippetElement;

        /// <summary>NOTE: This method is not guaranteed to be thread-safe.</summary>
        public void CopyFrom(EmitNode source)
        {
            Flags = source.Flags;
            EmitFlags = source.EmitFlags;
            CommentRange = source.CommentRange;
            SourceMapRange = source.SourceMapRange;
            TokenSourceMapRanges = source.TokenSourceMapRanges is null ? null : new Dictionary<Kind, TextRange>(source.TokenSourceMapRanges);
            Helpers = source.Helpers is null ? null : (EmitHelper[])source.Helpers.Clone();
            ExternalHelpersModuleName = source.ExternalHelpersModuleName;
            if (source.SnippetElement is not null)
            {
                SnippetElement = new SnippetElement(source.SnippetElement.Value.Kind, source.SnippetElement.Value.Order);
            }
        }
    }

    public EmitFlags EmitFlags(Node node)
    {
        var emitNode = _emitNodes.TryGet(node);
        if (emitNode is not null)
        {
            return emitNode.EmitFlags;
        }
        return EmitFlagsType.None;
    }

    public void SetEmitFlags(Node node, EmitFlags flags)
    {
        _emitNodes.Get(node).EmitFlags = flags;
    }

    public void AddEmitFlags(Node node, EmitFlags flags)
    {
        _emitNodes.Get(node).EmitFlags |= flags;
    }

    public SnippetElement? SnippetElement(Node node)
    {
        var emitNode = _emitNodes.TryGet(node);
        if (emitNode is not null)
        {
            return emitNode.SnippetElement;
        }
        return null;
    }

    public void SetSnippetElement(Node node, SnippetElement snippetElement)
    {
        _emitNodes.Get(node).SnippetElement = snippetElement;
    }

    /// <summary>Gets the range to use for a node when emitting comments.</summary>
    public TextRange CommentRange(Node node)
    {
        var emitNode = _emitNodes.TryGet(node);
        if (emitNode is not null && (emitNode.Flags & EmitNodeFlags.HasCommentRange) != 0)
        {
            return emitNode.CommentRange;
        }
        return node.Loc;
    }

    /// <summary>Sets the range to use for a node when emitting comments.</summary>
    public void SetCommentRange(Node node, TextRange loc)
    {
        var emitNode = _emitNodes.Get(node);
        emitNode.CommentRange = loc;
        emitNode.Flags |= EmitNodeFlags.HasCommentRange;
    }

    /// <summary>Sets the range to use for a node when emitting comments.</summary>
    public void AssignCommentRange(Node to, Node from)
    {
        SetCommentRange(to, CommentRange(from));
    }

    /// <summary>Gets the range to use for a node when emitting source maps.</summary>
    public TextRange SourceMapRange(Node node)
    {
        var emitNode = _emitNodes.TryGet(node);
        if (emitNode is not null && (emitNode.Flags & EmitNodeFlags.HasSourceMapRange) != 0)
        {
            return emitNode.SourceMapRange;
        }
        return node.Loc;
    }

    /// <summary>Sets the range to use for a node when emitting source maps.</summary>
    public void SetSourceMapRange(Node node, TextRange loc)
    {
        var emitNode = _emitNodes.Get(node);
        emitNode.SourceMapRange = loc;
        emitNode.Flags |= EmitNodeFlags.HasSourceMapRange;
    }

    /// <summary>Sets the range to use for a node when emitting source maps.</summary>
    public void AssignSourceMapRange(Node to, Node from)
    {
        SetSourceMapRange(to, SourceMapRange(from));
    }

    /// <summary>Sets the range to use for a node when emitting comments and source maps.</summary>
    public void AssignCommentAndSourceMapRanges(Node to, Node from)
    {
        var emitNode = _emitNodes.Get(to);
        var commentRange = CommentRange(from);
        var sourceMapRange = SourceMapRange(from);
        emitNode.CommentRange = commentRange;
        emitNode.SourceMapRange = sourceMapRange;
        emitNode.Flags |= EmitNodeFlags.HasCommentRange | EmitNodeFlags.HasSourceMapRange;
    }

    /// <summary>Gets the range for a token of a node when emitting source maps.</summary>
    public bool TryTokenSourceMapRange(Node node, Kind kind, out TextRange loc)
    {
        var emitNode = _emitNodes.TryGet(node);
        if (emitNode?.TokenSourceMapRanges is not null && emitNode.TokenSourceMapRanges.TryGetValue(kind, out var value))
        {
            loc = value;
            return true;
        }
        loc = default;
        return false;
    }

    /// <summary>Sets the range for a token of a node when emitting source maps.</summary>
    public void SetTokenSourceMapRange(Node node, Kind kind, TextRange loc)
    {
        var emitNode = _emitNodes.Get(node);
        emitNode.TokenSourceMapRanges ??= [];
        emitNode.TokenSourceMapRanges[kind] = loc;
    }

    public Node? AssignedName(Node node)
    {
        return _assignedName is not null && _assignedName.TryGetValue(node, out var name) ? name : null;
    }

    public Node? TextSource(Node node)
    {
        return _textSource is not null && _textSource.TryGetValue(node, out var source) ? source : null;
    }

    public void SetAssignedName(Node node, Node name)
    {
        _assignedName ??= [];
        _assignedName[node] = name;
    }

    public Node? ClassThis(Node node)
    {
        return _classThis is not null && _classThis.TryGetValue(node, out var classThis) ? classThis : null;
    }

    public void SetClassThis(Node node, Node classThis)
    {
        _classThis ??= [];
        _classThis[node] = classThis;
    }

    public void RequestEmitHelper(EmitHelper helper)
    {
        if (helper.Scoped)
        {
            Debug.Fail("Cannot request a scoped emit helper");
        }
        if (helper.Dependencies is not null)
        {
            foreach (var h in helper.Dependencies)
            {
                RequestEmitHelper(h);
            }
        }
        _emitHelpers.Add(helper);
    }

    public EmitHelper[] ReadEmitHelpers()
    {
        var helpers = new List<EmitHelper>();
        foreach (var helper in _emitHelpers.Values())
        {
            helpers.Add(helper);
        }
        _emitHelpers.Clear();
        return helpers.ToArray();
    }

    public void AddEmitHelper(Node node, params ReadOnlySpan<EmitHelper> helper)
    {
        var emitNode = _emitNodes.Get(node);
        foreach (var h in helper)
        {
            emitNode.Helpers = AppendIfUnique(emitNode.Helpers, h);
        }
    }

    public void MoveEmitHelpers(Node source, Node target, Func<EmitHelper, bool> predicate)
    {
        var sourceEmitNode = _emitNodes.TryGet(source);
        if (sourceEmitNode is null)
        {
            return;
        }
        var sourceEmitHelpers = sourceEmitNode.Helpers;
        if (sourceEmitHelpers is null || sourceEmitHelpers.Length == 0)
        {
            return;
        }

        var targetEmitNode = _emitNodes.Get(target);
        var helpersRemoved = 0;
        for (var i = 0; i < sourceEmitHelpers.Length; i++)
        {
            var helper = sourceEmitHelpers[i];
            if (predicate(helper))
            {
                helpersRemoved++;
                targetEmitNode.Helpers = AppendIfUnique(targetEmitNode.Helpers, helper);
            }
            else if (helpersRemoved > 0)
            {
                sourceEmitHelpers[i - helpersRemoved] = helper;
            }
        }

        if (helpersRemoved > 0)
        {
            // Go: sourceEmitHelpers = sourceEmitHelpers[:len(sourceEmitHelpers)-helpersRemoved]
            var trimmed = new EmitHelper[sourceEmitHelpers.Length - helpersRemoved];
            Array.Copy(sourceEmitHelpers, trimmed, trimmed.Length);
            sourceEmitNode.Helpers = trimmed;
        }
    }

    public EmitHelper[]? GetEmitHelpers(Node node)
    {
        var emitNode = _emitNodes.TryGet(node);
        if (emitNode is not null)
        {
            return emitNode.Helpers;
        }
        return null;
    }

    public Node? GetExternalHelpersModuleName(SourceFile node)
    {
        var parseNode = ParseNode(node.AsNode());
        if (parseNode is not null)
        {
            var emitNode = _emitNodes.TryGet(parseNode);
            if (emitNode is not null)
            {
                return emitNode.ExternalHelpersModuleName;
            }
        }
        return null;
    }

    public void SetExternalHelpersModuleName(SourceFile node, Node? name)
    {
        var parseNode = ParseNode(node.AsNode());
        if (parseNode is null)
        {
            Debug.Fail("Node must be a parse tree node or have an Original pointer to a parse tree node.");
        }

        var emitNode = _emitNodes.Get(parseNode!);
        emitNode.ExternalHelpersModuleName = name;
    }

    public bool HasRecordedExternalHelpers(SourceFile node)
    {
        var parseNode = ParseNode(node.AsNode());
        if (parseNode is not null)
        {
            var emitNode = _emitNodes.TryGet(parseNode);
            return emitNode is not null && (emitNode.ExternalHelpersModuleName is not null || (emitNode.EmitFlags & EmitFlagsType.ExternalHelpers) != 0);
        }
        return false;
    }

    public bool IsCallToHelper(Node firstSegment, string helperName)
    {
        return IsCallExpression(firstSegment) &&
            IsIdentifier(firstSegment.Expression()!) &&
            (EmitFlags(firstSegment.Expression()!) & EmitFlagsType.HelperName) != 0 &&
            firstSegment.Expression()!.Text() == helperName;
    }

    //
    // Visitor Hooks
    //

    public NodeList? VisitVariableEnvironment(NodeList nodes, NodeVisitor visitor)
    {
        StartVariableEnvironment();
        return EndAndMergeVariableEnvironmentList(visitor.VisitNodes(nodes));
    }

    public NodeList? VisitParameters(NodeList nodes, NodeVisitor visitor)
    {
        StartVariableEnvironment();
        var scope = _varScopeStack.Peek();
        var oldFlags = scope.Flags;
        scope.Flags |= EnvironmentFlags.InParameters;
        nodes = visitor.VisitNodes(nodes)!;

        // As of ES2015, any runtime execution of that occurs in for a parameter (such as evaluating an
        // initializer or a binding pattern), occurs in its own lexical scope. As a result, any expression
        // that we might transform that introduces a temporary variable would fail as the temporary variable
        // exists in a different lexical scope. To address this, we move any binding patterns and initializers
        // in a parameter list to the body if we detect a variable being hoisted while visiting a parameter list
        // when the emit target is greater than ES2015. (Which is now all targets.)
        if ((scope.Flags & EnvironmentFlags.VariablesHoistedInParameters) != 0)
        {
            nodes = AddDefaultValueAssignmentsIfNeeded(nodes)!;
        }
        scope.Flags = oldFlags;
        // !!! c.suspendVariableEnvironment()
        return nodes;
    }

    private NodeList? AddDefaultValueAssignmentsIfNeeded(NodeList? nodeList)
    {
        if (nodeList is null)
        {
            return nodeList;
        }
        Node[]? result = null;
        var nodes = nodeList.Nodes ?? [];
        for (var i = 0; i < nodes.Length; i++)
        {
            var parameter = nodes[i];
            var updated = AddDefaultValueAssignmentIfNeeded(parameter.AsParameterDeclaration()!);
            if (!ReferenceEquals(updated, parameter))
            {
                result ??= (Node[])nodes.Clone();
                result[i] = updated;
            }
        }
        if (result is not null)
        {
            var res = Factory.NewNodeList(result);
            res.Loc = nodeList.Loc;
            return res;
        }
        return nodeList;
    }

    private Node AddDefaultValueAssignmentIfNeeded(ParameterDeclaration parameter)
    {
        // A rest parameter cannot have a binding pattern or an initializer,
        // so let's just ignore it.
        if (parameter.DotDotDotToken is not null)
        {
            return parameter.AsNode();
        }
        if (IsBindingPattern(parameter.Name()))
        {
            return AddDefaultValueAssignmentForBindingPattern(parameter);
        }
        if (parameter.Initializer is not null)
        {
            return AddDefaultValueAssignmentForInitializer(parameter, parameter.Name()!, parameter.Initializer);
        }
        return parameter.AsNode();
    }

    private Node AddDefaultValueAssignmentForBindingPattern(ParameterDeclaration parameter)
    {
        Node initNode;
        if (parameter.Initializer is not null)
        {
            initNode = Factory.NewConditionalExpression(
                Factory.NewStrictEqualityExpression(
                    Factory.NewGeneratedNameForNode(parameter.AsNode()),
                    Factory.NewVoidZeroExpression()),
                Factory.NewToken(Kind.QuestionToken),
                parameter.Initializer,
                Factory.NewToken(Kind.ColonToken),
                Factory.NewGeneratedNameForNode(parameter.AsNode()));
        }
        else
        {
            initNode = Factory.NewGeneratedNameForNode(parameter.AsNode());
        }
        AddInitializationStatement(Factory.NewVariableStatement(
            null,
            Factory.NewVariableDeclarationList(Factory.NewNodeList([
                Factory.NewVariableDeclaration(
                    parameter.Name(),
                    null,
                    parameter.Type,
                    initNode),
            ]), NodeFlags.None)));
        return Factory.UpdateParameterDeclaration(
            parameter,
            parameter.Modifiers(),
            parameter.DotDotDotToken,
            Factory.NewGeneratedNameForNode(parameter.AsNode()),
            parameter.QuestionToken,
            parameter.Type,
            null);
    }

    private Node AddDefaultValueAssignmentForInitializer(ParameterDeclaration parameter, Node name, Node initializer)
    {
        AddEmitFlags(initializer, EmitFlagsType.NoSourceMap | EmitFlagsType.NoComments);
        var nameClone = name.Clone(Factory)!;
        AddEmitFlags(nameClone, EmitFlagsType.NoSourceMap);
        var initAssignment = Factory.NewAssignmentExpression(
            nameClone,
            initializer);
        initAssignment.Loc = parameter.Loc;
        AddEmitFlags(initAssignment, EmitFlagsType.NoComments);
        var initBlock = Factory.NewBlock(Factory.NewNodeList([Factory.NewExpressionStatement(initAssignment)]), false);
        initBlock.Loc = parameter.Loc;
        AddEmitFlags(initBlock, EmitFlagsType.SingleLine | EmitFlagsType.NoTrailingSourceMap | EmitFlagsType.NoTokenSourceMaps | EmitFlagsType.NoComments);
        AddInitializationStatement(Factory.NewIfStatement(
            Factory.NewTypeCheck(name.Clone(Factory)!, "undefined"),
            initBlock,
            null));
        return Factory.UpdateParameterDeclaration(
            parameter,
            parameter.Modifiers(),
            parameter.DotDotDotToken,
            parameter.Name(),
            parameter.QuestionToken,
            parameter.Type,
            null);
    }

    public void AddInitializationStatement(Node node)
    {
        var scope = _varScopeStack.Peek();
        if (scope is null)
        {
            Debug.Fail("Tried to add an initialization statement without a surrounding variable scope");
        }
        AddEmitFlags(node, EmitFlagsType.CustomPrologue);
        scope!.InitializationStatements.Add(node);
    }

    public Node ConvertToFunctionBlock(Node node, bool multiLine)
    {
        if (IsBlock(node))
        {
            return node;
        }
        var returnStatement = Factory.NewReturnStatement(node);
        returnStatement.Loc = node.Loc;
        var statements = Factory.NewNodeList([returnStatement]);
        statements.Loc = node.Loc;
        var block = Factory.NewBlock(statements, multiLine);
        block.Loc = node.Loc;
        return block;
    }

    public Node? VisitFunctionBody(Node node, NodeVisitor visitor)
    {
        // !!! c.resumeVariableEnvironment()
        var updated = visitor.VisitNode(node);
        var declarations = EndVariableEnvironment();
        if (declarations.Length == 0)
        {
            return updated;
        }

        if (updated is null)
        {
            return Factory.NewBlock(Factory.NewNodeList(declarations), true /*multiLine*/);
        }

        if (!IsBlock(updated))
        {
            AddEmitFlags(updated, EmitFlagsType.NoComments);
            var block = ConvertToFunctionBlock(updated, false /*multiLine*/);
            return Factory.UpdateBlock(
                block.AsBlock()!,
                MergeEnvironmentList(block.StatementList()!, declarations),
                block.AsBlock()!.MultiLine);
        }

        return Factory.UpdateBlock(
            updated.AsBlock()!,
            MergeEnvironmentList(updated.StatementList()!, declarations),
            updated.AsBlock()!.MultiLine);
    }

    public Node? VisitIterationBody(Node body, NodeVisitor visitor)
    {
        if (body is null)
        {
            return null;
        }

        StartLexicalEnvironment();
        var updated = VisitEmbeddedStatement(body, visitor);
        if (updated is null)
        {
            Debug.Fail("Expected visitor to return a statement.");
        }

        var statements = new List<Node>(EndLexicalEnvironment());
        if (statements.Count > 0)
        {
            if (IsBlock(updated!))
            {
                statements.AddRange(updated!.Statements() ?? []);
                var statementsList = Factory.NewNodeList(statements.ToArray());
                statementsList.Loc = updated.StatementList()!.Loc;
                return Factory.UpdateBlock(updated.AsBlock()!, statementsList, updated.AsBlock()!.MultiLine);
            }
            statements.Add(updated!);
            return Factory.NewBlock(Factory.NewNodeList(statements.ToArray()), true /*multiLine*/);
        }

        return updated;
    }

    public Node? VisitEmbeddedStatement(Node node, NodeVisitor visitor)
    {
        if (node is null)
        {
            return null;
        }
        var embeddedStatement = visitor.VisitEmbeddedStatement(node);
        if (embeddedStatement is null || IsNotEmittedStatement(embeddedStatement))
        {
            var emptyStatement = visitor.Factory.NewEmptyStatement();
            emptyStatement.Loc = node.Loc;
            SetOriginal(emptyStatement, node);
            AssignCommentRange(emptyStatement, node);
            return emptyStatement;
        }
        return embeddedStatement;
    }

    public Node SetSyntheticLeadingComments(Node node, SynthesizedComment[]? comments)
    {
        _emitNodes.Get(node).LeadingComments = comments;
        return node;
    }

    public Node AddSyntheticLeadingComment(Node node, Kind kind, string text, bool hasTrailingNewLine)
    {
        var emitNode = _emitNodes.Get(node);
        emitNode.LeadingComments = AppendComment(emitNode.LeadingComments, new SynthesizedComment
        {
            Kind = kind,
            Loc = NewTextRange(-1, -1),
            HasTrailingNewLine = hasTrailingNewLine,
            Text = text,
        });
        return node;
    }

    public SynthesizedComment[]? GetSyntheticLeadingComments(Node node)
    {
        if (_emitNodes.Has(node))
        {
            return _emitNodes.Get(node).LeadingComments;
        }
        return null;
    }

    public Node SetSyntheticTrailingComments(Node node, SynthesizedComment[]? comments)
    {
        _emitNodes.Get(node).TrailingComments = comments;
        return node;
    }

    public Node AddSyntheticTrailingComment(Node node, Kind kind, string text, bool hasTrailingNewLine)
    {
        var emitNode = _emitNodes.Get(node);
        emitNode.TrailingComments = AppendComment(emitNode.TrailingComments, new SynthesizedComment
        {
            Kind = kind,
            Loc = NewTextRange(-1, -1),
            HasTrailingNewLine = hasTrailingNewLine,
            Text = text,
        });
        return node;
    }

    public SynthesizedComment[]? GetSyntheticTrailingComments(Node node)
    {
        if (_emitNodes.Has(node))
        {
            return _emitNodes.Get(node).TrailingComments;
        }
        return null;
    }

    /// <summary>Go 的 <c>append(comments, comment)</c>（数组形态下的等价写法）。</summary>
    private static SynthesizedComment[] AppendComment(SynthesizedComment[]? comments, SynthesizedComment comment)
    {
        if (comments is null)
        {
            return [comment];
        }
        var result = new SynthesizedComment[comments.Length + 1];
        Array.Copy(comments, result, comments.Length);
        result[^1] = comment;
        return result;
    }

    /// <summary>
    /// SetTypeNode stores the original type node on a name node when the type is erased,
    /// so the emitter can use the type's position for comment preservation.
    /// </summary>
    public void SetTypeNode(Node node, Node? typeNode)
    {
        _emitNodes.Get(node).TypeNode = typeNode;
    }

    /// <summary>GetTypeNode gets the type node stored on a name node by the type eraser.</summary>
    public Node? GetTypeNode(Node node)
    {
        var emitNode = _emitNodes.TryGet(node);
        if (emitNode is not null)
        {
            return emitNode.TypeNode;
        }
        return null;
    }

    public Node NewNotEmittedStatement(Node node)
    {
        var statement = Factory.NewNotEmittedStatement();
        statement.Loc = node.Loc;
        SetOriginal(statement, node);
        AssignCommentRange(statement, node);
        return statement;
    }
}

/// <summary>Go 的 <c>type SnippetKind int</c>（emitcontext.go:536-540）。</summary>
public enum SnippetKind
{
    TabStop = 0,
}

/// <summary>Go 的 <c>type SnippetElement struct</c>：值类型（§3.2 的 <c>*T</c> → <c>T?</c>）。</summary>
public readonly record struct SnippetElement(SnippetKind Kind, int Order);

/// <summary>Go 的 <c>type SynthesizedComment struct</c>（emitcontext.go:542-548）。</summary>
public sealed class SynthesizedComment
{
    public Kind Kind;
    public TextRange Loc;
    public bool HasLeadingNewLine;
    public bool HasTrailingNewLine;
    public string Text = "";
}

/// <summary>Go 的 <c>type AutoGenerateOptions struct</c>：值类型（Go 里按值传递，函数内的 Flags 写入是局部副本）。</summary>
public struct AutoGenerateOptions
{
    public GeneratedIdentifierFlags Flags;

    // Go 的 string 零值是**空串**，C# 的字段默认是 null → 必须显式初始化（读取处 `options.Prefix.Length` 会 NRE）。
    public string Prefix = "";
    public string Suffix = "";

    /// <summary>
    /// Go 的零值 <c>AutoGenerateOptions{}</c> 对应 <c>new AutoGenerateOptions()</c>（不是 <c>default</c>：
    /// <c>default(AutoGenerateOptions)</c> 会绕过字段初始化器，得到 null 字符串）。
    /// 声明无参构造函数是因为带字段初始化器的 struct 必须有显式构造函数（CS8983）。
    /// </summary>
    public AutoGenerateOptions()
    {
        Flags = default;
        Prefix = "";
        Suffix = "";
    }
}

/// <summary>Go 的 <c>type AutoGenerateId uint32</c>。</summary>
public readonly struct AutoGenerateId : IEquatable<AutoGenerateId>
{
    public readonly uint Value;

    public AutoGenerateId(uint value)
    {
        Value = value;
    }

    public static implicit operator uint(AutoGenerateId id) => id.Value;

    public static implicit operator AutoGenerateId(uint value) => new(value);

    public bool Equals(AutoGenerateId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is AutoGenerateId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Go 的 <c>type AutoGenerateInfo struct</c>（emitcontext.go:427-435）。</summary>
public sealed class AutoGenerateInfo
{
    /// <summary>Specifies whether to auto-generate the text for an identifier.</summary>
    public GeneratedIdentifierFlags Flags;

    /// <summary>Ensures unique generated identifiers get unique names, but clones get the same name.</summary>
    public AutoGenerateId Id;

    /// <summary>Optional prefix to apply to the start of the generated name.</summary>
    public string Prefix = "";

    /// <summary>Optional suffix to apply to the end of the generated name.</summary>
    public string Suffix = "";

    /// <summary>For a GeneratedIdentifierFlagsNode, the node from which to generate an identifier.</summary>
    public Node? Node;

    public AutoGenerateInfo()
    {
    }

    /// <summary>Go: <c>autoGenerateCopy := *autoGenerate</c>（emitcontext.go:83）——逐字段浅拷贝。</summary>
    public AutoGenerateInfo(AutoGenerateInfo source)
    {
        Flags = source.Flags;
        Id = source.Id;
        Prefix = source.Prefix;
        Suffix = source.Suffix;
        Node = source.Node;
    }
}

public sealed partial class Printer
{
    [ThreadStatic]
    private static EmitContext? _pooledEmitContext;

    private static uint _nextAutoGenerateId;

    public static EmitContext NewEmitContext()
    {
        var c = new EmitContext();
        c.Factory = new NodeFactory(c);
        return c;
    }

    /// <summary>
    /// Go: <c>func GetEmitContext() (*EmitContext, func())</c>：从池里取一个已重置的上下文，Cleanup 归还
    /// （见文件头第 1 条）。
    /// </summary>
    public static (EmitContext Context, Action Cleanup) GetEmitContext()
    {
        var c = _pooledEmitContext ?? NewEmitContext();
        _pooledEmitContext = null;
        return (c, () =>
        {
            c.Reset();
            _pooledEmitContext = c;
        });
    }

    /// <summary>Go: <c>nextAutoGenerateId.Add(1)</c>（emitcontext.go:419）。</summary>
    internal static AutoGenerateId NextAutoGenerateId() =>
        new((uint)Interlocked.Increment(ref _nextAutoGenerateId));

    /// <summary>Go: <c>func isHoistedVariable(node *ast.VariableDeclarationNode) bool</c>（emitcontext.go:362）。</summary>
    internal static bool IsHoistedVariable(VariableDeclaration node)
    {
        return IsIdentifier(node.Name()!) && node.Initializer is null;
    }
}
