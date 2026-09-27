// ported from: internal/transformers/estransforms/classfields.go:1-377（类型定义 / 构造函数 / 主访问器分发）
//
// Go→C# 语义改写（本分片）：
// 1) Go 未导出类型按 C# 习惯命名（docs/architecture.md §2）：classFacts → ClassFacts、
//    privateIdentifierInfo → PrivateIdentifierInfo、privateEnvironmentData → PrivateEnvironmentData、
//    privateEnvironment → PrivateEnvironment、classLexicalEnvironment → ClassLexicalEnvironment、
//    classLexicalEnv → ClassLexicalEnv、classFieldsTransformer → ClassFieldsTransformer。
// 2) `type classFacts int` + 位常量 → `[Flags] enum ClassFacts`（数值与 Go 一致，成员名 = Go 常量名去 `classFacts` 前缀）。
// 3) Go 的 `map[*ast.Node]X` → `Dictionary<Node, X>`：Node 不覆写 Equals/GetHashCode（docs/ast.md §2），
//    默认比较器就是 Go 的指针相等语义。
// 4) Go 的 `[]*ast.Expression` / `[]*ast.Statement` 字段 → `List<Node>`（Go 用 append 就地扩容，C# 数组没有 len<cap 视图，
//    见 docs/architecture.md §3.2）；读取处 `InlineExpressions(x)` 需要 `Node[]`，故调用点 `.ToArray()`。
// 5) Go 的 `func(tx *classFieldsTransformer, node *ast.Node) *ast.Node` 具名回调参数 → `Func<Node, Node?>`（绑定的实例方法组）。
// 6) Go 的 `func(*anonymousFunctionDefinition) bool` 字段 → `Func<Node, bool>?`（Go 的 anonymousFunctionDefinition 就是 *Node）。
// 7) `defer tx.popNode(...)` → `try/finally`。
// 8) Go 的 nil map 读取（len == 0）→ `_classAliases is { Count: > 0 }`。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Printer;
using TypeScript.Collections;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
// Go 的 printer.PrivateIdentifierKind 在 C# 侧是 `TypeScript.Printer` 的命名空间级类型（Printer/NodeFactory.cs 末尾），
// 别名让调用点与 Go 逐字一致。
using PrivateIdentifierKind = TypeScript.Printer.PrivateIdentifierKind;

namespace TypeScript.Transformers.EsTransforms;

/// <summary>Go: <c>type classFacts int</c>（classfields.go:18-27）。</summary>
[Flags]
internal enum ClassFacts
{
    /// <summary>Go: <c>classFactsNone</c>。</summary>
    None = 0,
    /// <summary>Go: <c>classFactsClassWasDecorated</c>。</summary>
    ClassWasDecorated = 1 << 0,
    /// <summary>Go: <c>classFactsNeedsClassConstructorReference</c>。</summary>
    NeedsClassConstructorReference = 1 << 1,
    /// <summary>Go: <c>classFactsNeedsClassSuperReference</c>。</summary>
    NeedsClassSuperReference = 1 << 2,
    /// <summary>Go: <c>classFactsNeedsSubstitutionForThisInClassStaticField</c>。</summary>
    NeedsSubstitutionForThisInClassStaticField = 1 << 3,
    /// <summary>Go: <c>classFactsWillHoistInitializersToConstructor</c>。</summary>
    WillHoistInitializersToConstructor = 1 << 4,
}

/// <summary>Go: <c>type privateIdentifierInfo struct</c>（classfields.go:31-52）。</summary>
internal sealed class PrivateIdentifierInfo
{
    /// <summary>Go: <c>kind</c>。</summary>
    internal PrivateIdentifierKind _kind;
    /// <summary>
    /// Go: <c>brandCheckIdentifier</c>。可以包含：
    /// <list type="bullet">
    /// <item>实例字段：将作为该字段存储的 WeakMap。</item>
    /// <item>实例方法或访问器：将用于 brand check 的 WeakSet。</item>
    /// <item>静态成员：将用于 brand check 的构造函数。</item>
    /// </list>
    /// </summary>
    internal Node? _brandCheckIdentifier;
    /// <summary>Go: <c>isStatic</c>——标识符是否为静态。</summary>
    internal bool _isStatic;
    /// <summary>Go: <c>isValid</c>——标识符声明是否有效。保留名（如 <c>#constructor</c>）或重复标识符视为无效。</summary>
    internal bool _isValid;
    /// <summary>Go: <c>variableName</c>——将作为静态字段存储的变量。</summary>
    internal Node? _variableName;
    /// <summary>Go: <c>methodName</c>——将包含私有方法实现的变量的标识符。</summary>
    internal Node? _methodName;
    /// <summary>Go: <c>getterName</c>——将包含私有 get 访问器实现的变量的标识符（若有）。</summary>
    internal Node? _getterName;
    /// <summary>Go: <c>setterName</c>——将包含私有 set 访问器实现的变量的标识符（若有）。</summary>
    internal Node? _setterName;
}

/// <summary>Go: <c>type privateEnvironmentData struct</c>（classfields.go:54-61）。</summary>
internal sealed class PrivateEnvironmentData
{
    /// <summary>Go: <c>className</c>——用于给生成的变量名加前缀。</summary>
    internal Node? _className;
    /// <summary>Go: <c>weakSetName</c>——用于私有方法 brand check。</summary>
    internal Node? _weakSetName;
}

/// <summary>
/// Go: <c>type privateEnvironment struct</c>（classfields.go:65-69）。与 Strada 一样使用两张表：非生成标识符按文本索引，
/// 生成的标识符按原始 AST 节点索引。这样不同 auto-accessor 产生的同名 backing field 文本不会互相冲突。
/// </summary>
internal sealed class PrivateEnvironment
{
    /// <summary>Go: <c>data</c>（值类型字段；C# 侧用非空实例保持"可寻址"语义）。</summary>
    internal PrivateEnvironmentData _data = new();
    /// <summary>Go: <c>members map[string]*privateIdentifierInfo</c>。</summary>
    internal Dictionary<string, PrivateIdentifierInfo> _members = new();
    /// <summary>Go: <c>generatedIdentifiers map[*ast.Node]*privateIdentifierInfo</c>（Go 里懒初始化的 nil map）。</summary>
    internal Dictionary<Node, PrivateIdentifierInfo>? _generatedIdentifiers;
}

/// <summary>Go: <c>type classLexicalEnvironment struct</c>（classfields.go:72-79）。</summary>
internal sealed class ClassLexicalEnvironment
{
    /// <summary>Go: <c>facts</c>。</summary>
    internal ClassFacts _facts;
    /// <summary>Go: <c>classConstructor</c>——用于静态成员的 brand check，以及静态初始化器中的 `this` 引用。</summary>
    internal Node? _classConstructor;
    /// <summary>Go: <c>classThis</c>。</summary>
    internal Node? _classThis;
    /// <summary>Go: <c>superClassReference</c>——用于静态初始化器中的 `super` 引用。</summary>
    internal Node? _superClassReference;
}

/// <summary>Go: <c>type classLexicalEnv struct</c>（classfields.go:82-86）：类词法环境的链表节点。</summary>
internal sealed class ClassLexicalEnv
{
    /// <summary>Go: <c>previous</c>。</summary>
    internal ClassLexicalEnv? _previous;
    /// <summary>Go: <c>data</c>（懒创建）。</summary>
    internal ClassLexicalEnvironment? _data;
    /// <summary>Go: <c>privateEnv</c>（懒创建）。</summary>
    internal PrivateEnvironment? _privateEnv;
}

/// <summary>Go: <c>type classFieldsTransformer struct</c>（classfields.go:88-138）。</summary>
public sealed partial class ClassFieldsTransformer : Transformer
{
    /// <summary>Go: <c>compilerOptions</c>。</summary>
    private readonly CompilerOptions _compilerOptions;
    /// <summary>Go: <c>resolver</c>。</summary>
    private readonly ReferenceResolver _resolver;

    // Computed configuration flags（计算出的配置标志）
    /// <summary>Go: <c>shouldTransformInitializersUsingSet</c>。</summary>
    private bool _shouldTransformInitializersUsingSet;
    /// <summary>Go: <c>shouldTransformInitializersUsingDefine</c>。</summary>
    private bool _shouldTransformInitializersUsingDefine;
    /// <summary>Go: <c>shouldTransformInitializers</c>。</summary>
    private bool _shouldTransformInitializers;
    /// <summary>Go: <c>shouldTransformPrivateElementsOrClassStaticBlocks</c>。</summary>
    private bool _shouldTransformPrivateElementsOrClassStaticBlocks;
    /// <summary>Go: <c>shouldTransformAutoAccessors</c>。</summary>
    private bool _shouldTransformAutoAccessors;
    /// <summary>Go: <c>shouldTransformThisInStaticInitializers</c>。</summary>
    private bool _shouldTransformThisInStaticInitializers;
    /// <summary>Go: <c>shouldTransformSuperInStaticInitializers</c>。</summary>
    private bool _shouldTransformSuperInStaticInitializers;
    /// <summary>Go: <c>shouldTransformPrivateStaticElementsInFile</c>。</summary>
    private bool _shouldTransformPrivateStaticElementsInFile;
    /// <summary>Go: <c>legacyDecorators</c>。</summary>
    private readonly bool _legacyDecorators;

    /// <summary>Go: <c>pendingExpressions</c>——来自被省略名字的计算属性名表达式，必须按文档顺序内联到下一个执行点。</summary>
    private List<Node> _pendingExpressions = [];
    /// <summary>Go: <c>pendingStatements</c>——计算属性名表达式语句与静态属性初始化器，必须按文档顺序在下一个执行点发出（装饰类）。</summary>
    private List<Node> _pendingStatements = [];
    /// <summary>Go: <c>lexicalEnvironment</c>。</summary>
    private ClassLexicalEnv? _lexicalEnvironment;
    /// <summary>Go: <c>currentClassContainer</c>。</summary>
    private Node? _currentClassContainer;
    /// <summary>Go: <c>currentClassElement</c>。</summary>
    private Node? _currentClassElement;
    /// <summary>
    /// Go: <c>classAliases</c>——把类声明映射到别名标识符，用于替换静态初始化器中的类名引用。
    /// 取代 Strada 的 onSubstituteNode/trySubstituteClassAlias。
    /// </summary>
    private Dictionary<Node, Node>? _classAliases;
    /// <summary>Go: <c>enclosingClassDeclarations</c>。</summary>
    private readonly Set<Node> _enclosingClassDeclarations = new();
    /// <summary>Go: <c>inIterationStatement</c>。</summary>
    private bool _inIterationStatement;
    /// <summary>
    /// Go: <c>insideComputedPropertyName</c>——取代 Strada 针对 ComputedPropertyName 的 onEmitNode（切换到外层词法环境）。
    /// visitThisExpression() 用它在外层环境下做替换，而不要求 currentClassElement 是静态的。
    /// </summary>
    private bool _insideComputedPropertyName;
    /// <summary>Go: <c>parentNode</c>。</summary>
    private Node? _parentNode;
    /// <summary>Go: <c>currentNode</c>。</summary>
    private Node? _currentNode;

    // Visitors（访问器）
    /// <summary>Go: <c>modifierVisitor</c>。</summary>
    internal NodeVisitor _modifierVisitor = null!;
    /// <summary>Go: <c>discardedValueVisitor</c>。</summary>
    internal NodeVisitor _discardedValueVisitor = null!;
    /// <summary>Go: <c>heritageClauseVisitor</c>。</summary>
    internal NodeVisitor _heritageClauseVisitor = null!;
    /// <summary>Go: <c>assignmentTargetVisitor</c>。</summary>
    internal NodeVisitor _assignmentTargetVisitor = null!;
    /// <summary>Go: <c>classElementVisitor</c>。</summary>
    internal NodeVisitor _classElementVisitor = null!;
    /// <summary>Go: <c>accessorFieldResultVisitor</c>。</summary>
    internal NodeVisitor _accessorFieldResultVisitor = null!;
    /// <summary>Go: <c>arrayAssignmentElementVisitor</c>。</summary>
    internal NodeVisitor _arrayAssignmentElementVisitor = null!;
    /// <summary>Go: <c>objectAssignmentElementVisitor</c>。</summary>
    internal NodeVisitor _objectAssignmentElementVisitor = null!;
    /// <summary>Go: <c>substitutionVisitor</c>。</summary>
    internal NodeVisitor _substitutionVisitor = null!;

    /// <summary>Go: <c>isAnonymousClassNeedingAssignedName</c>（预先绑定的回调，避免重复分配闭包）。</summary>
    internal Func<Node, bool>? _isAnonymousClassNeedingAssignedName;

    /// <summary>
    /// C# 侧桥接方法：Go 的工厂函数与 classFieldsTransformer 处在**同一个包**里，可以直接读它的未导出方法/字段；
    /// C# 的 private 是类型级，`EsTransforms.NewClassFieldsTransformer`（另一个类型）拿不到，所以把这 11 行搬回类型内部。
    /// 语句顺序与 Go 的 newClassFieldsTransformer 完全一致（NewTransformer 必须先于 EmitContext() 的使用）。
    /// </summary>
    internal void InitTransform(TransformOptions opts)
    {
        NewTransformer(Visit, opts.Context);
        _modifierVisitor = EmitContext().NewNodeVisitor(VisitModifier);
        _discardedValueVisitor = EmitContext().NewNodeVisitor(VisitDiscardedValue);
        _heritageClauseVisitor = EmitContext().NewNodeVisitor(VisitHeritageClause);
        _assignmentTargetVisitor = EmitContext().NewNodeVisitor(VisitAssignmentTarget);
        _classElementVisitor = EmitContext().NewNodeVisitor(VisitClassElement);
        _accessorFieldResultVisitor = EmitContext().NewNodeVisitor(VisitAccessorFieldResult);
        _arrayAssignmentElementVisitor = EmitContext().NewNodeVisitor(VisitArrayAssignmentElement);
        _objectAssignmentElementVisitor = EmitContext().NewNodeVisitor(VisitObjectAssignmentElement);
        _substitutionVisitor = EmitContext().NewNodeVisitor(VisitForSubstitution);
        _isAnonymousClassNeedingAssignedName = IsAnonymousClassNeedingAssignedNameWorker;
    }

    internal ClassFieldsTransformer(TransformOptions opts)
    {
        _compilerOptions = opts.CompilerOptions!;
        _resolver = opts.Resolver!;
        _legacyDecorators = opts.CompilerOptions!.ExperimentalDecorators.IsTrue();
        // Always transform field initializers using Set semantics when `useDefineForClassFields: false`.
        _shouldTransformInitializersUsingSet = !_compilerOptions.GetUseDefineForClassFields();

        // Transform field initializers using Define semantics when `useDefineForClassFields: true` and target < ES2022.
        _shouldTransformInitializersUsingDefine =
            _compilerOptions.GetUseDefineForClassFields() && _compilerOptions.GetEmitScriptTarget() < ScriptTarget.ES2022;

        _shouldTransformInitializers = _shouldTransformInitializersUsingSet || _shouldTransformInitializersUsingDefine;

        // We need to transform private members and class static blocks when target < ES2022.
        _shouldTransformPrivateElementsOrClassStaticBlocks = _compilerOptions.GetEmitScriptTarget() < ScriptTarget.ES2022;

        // We need to transform `accessor` fields when target < ESNext.
        // We may need to transform `accessor` fields when `useDefineForClassFields: false`
        _shouldTransformAutoAccessors = _compilerOptions.GetEmitScriptTarget() < ScriptTarget.ESNext;

        // We need to transform `this` in a static initializer into a reference to the class
        // when target < ES2022 since the assignment will be moved outside of the class body.
        _shouldTransformThisInStaticInitializers = _compilerOptions.GetEmitScriptTarget() < ScriptTarget.ES2022;

        // Since target is always >= ES2015, this is always the same as
        // shouldTransformThisInStaticInitializers.
        _shouldTransformSuperInStaticInitializers = _shouldTransformThisInStaticInitializers;
    }

    /// <summary>
    /// Go: <c>requiresBlockScopedVar</c>（classfields.go:199-201）。当私有字段临时变量应声明为块作用域（let）而非函数作用域（var）
    /// 时返回 true。这发生在类表达式直接位于循环体内时。取代 Strada 的
    /// <c>resolver.hasNodeCheckFlag(node, NodeCheckFlags.BlockScopedBindingInLoop)</c>。
    /// </summary>
    private bool RequiresBlockScopedVar()
    {
        return _inIterationStatement && _currentClassContainer != null && IsClassExpression(_currentClassContainer);
    }

    /// <summary>
    /// Go 的 <c>info.kind == printer.PrivateIdentifierKindField</c> 之类的比较。C# 的 <c>PrivateIdentifierKind</c> 是包着
    /// string 的 struct（没有定义 <c>==</c>），且 <c>static readonly</c> 字段不能作为 <c>case</c> 常量模式，
    /// 所以统一用本助手做 Go 的字符串相等比较（也用于替代 Go 的 switch on kind）。
    /// </summary>
    private static bool IsPrivateKind(PrivateIdentifierKind kind, PrivateIdentifierKind expected) => kind.Value == expected.Value;

    /// <summary>
    /// Go: <c>classExpressionNeedsBlockScopedTemp</c>（classfields.go:207-218）。比 RequiresBlockScopedVar 更具体：
    /// 只有当类表达式带有非静态的、计算属性名的属性且位于循环内时，类的临时变量才需要块作用域
    /// （与 checker 在类节点上的 BlockScopedBindingInLoop 一致）。
    /// </summary>
    private bool ClassExpressionNeedsBlockScopedTemp()
    {
        if (!RequiresBlockScopedVar())
        {
            return false;
        }
        foreach (var member in _currentClassContainer!.Members() ?? [])
        {
            if (IsPropertyDeclaration(member) && !HasStaticModifier(member) &&
                member.Name() != null && IsComputedPropertyName(member.Name()!))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>visitSourceFile</c>（classfields.go:220-233）。</summary>
    private Node VisitSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node.AsNode();
        }
        _lexicalEnvironment = null;
        _shouldTransformPrivateStaticElementsInFile =
            (EmitContext().EmitFlags(node.AsNode()) & EmitFlags.TransformPrivateStaticElements) != 0;
        _classAliases = new Dictionary<Node, Node>();
        _enclosingClassDeclarations.Clear();
        var visited = Visitor().VisitEachChild(node.AsNode())!;
        EmitContext().AddEmitHelper(visited, EmitContext().ReadEmitHelpers());
        _classAliases = null;
        _enclosingClassDeclarations.Clear();
        return visited;
    }

    /// <summary>Go: <c>visitModifier</c>（classfields.go:235-246）。</summary>
    private Node? VisitModifier(Node node)
    {
        if (node.Kind == Kind.AccessorKeyword)
        {
            if (ShouldTransformAutoAccessorsInCurrentClass())
            {
                return null;
            }
            return node;
        }
        if (IsModifier(node))
        {
            return node;
        }
        return null;
    }

    /// <summary>Go: <c>pushNode</c>（classfields.go:248-253）。</summary>
    private Node? PushNode(Node node)
    {
        var grandparentNode = _parentNode;
        _parentNode = _currentNode;
        _currentNode = node;
        return grandparentNode;
    }

    /// <summary>Go: <c>popNode</c>（classfields.go:255-258）。</summary>
    private void PopNode(Node? grandparentNode)
    {
        _currentNode = _parentNode;
        _parentNode = grandparentNode;
    }

    /// <summary>
    /// Go: <c>visitForSubstitution</c>（classfields.go:265-273）。只为类别名替换而访问：在不含类字段或词法 this/super
    /// 变换的子树里，把引用类声明的标识符替换成其别名，同时跳过 PropertyAccessExpression 的 .Name()——
    /// 因为 Strada 的 onSubstituteNode 只对 EmitHint.Expression 触发，属性访问名不属于其中。
    /// </summary>
    private Node? VisitForSubstitution(Node node)
    {
        if (node.Kind == Kind.Identifier)
        {
            return VisitIdentifier(node.AsIdentifier()!);
        }
        if (node.Kind == Kind.PropertyAccessExpression && IsIdentifier(node.AsPropertyAccessExpression()!.Name()!))
        {
            return VisitPropertyAccessExpressionForSubstitution(node.AsPropertyAccessExpression()!);
        }
        return _substitutionVisitor.VisitEachChild(node);
    }

    /// <summary>Go: <c>visit</c>（classfields.go:276-341）——主访问器。</summary>
    private Node? Visit(Node node)
    {
        var grandparentNode = PushNode(node);
        try
        {
            if ((node.SubtreeFacts & (SubtreeFacts.ContainsClassFields | SubtreeFacts.ContainsLexicalThisOrSuper)) == 0)
            {
                if (_currentClassContainer != null && _classAliases is { Count: > 0 })
                {
                    // Continue visiting for alias substitution even in non-class-field subtrees.
                    return VisitForSubstitution(node);
                }
                return node;
            }

            switch (node.Kind)
            {
                case Kind.SourceFile:
                    return VisitSourceFile(node.AsSourceFile()!);
                case Kind.ClassDeclaration:
                    return VisitClassDeclaration(node.AsClassDeclaration()!);
                case Kind.ClassExpression:
                    return VisitClassExpression(node.AsClassExpression()!);
                case Kind.ClassStaticBlockDeclaration:
                case Kind.PropertyDeclaration:
                    Debug.Fail("Use `classElementVisitor` instead.");
                    return null;
                case Kind.PropertyAssignment:
                    return VisitPropertyAssignment(node.AsPropertyAssignment()!);
                case Kind.VariableStatement:
                    return VisitVariableStatement(node.AsVariableStatement()!);
                case Kind.VariableDeclaration:
                    return VisitVariableDeclaration(node.AsVariableDeclaration()!);
                case Kind.Parameter:
                    return VisitParameterDeclaration(node.AsParameterDeclaration()!);
                case Kind.BindingElement:
                    return VisitBindingElement(node.AsBindingElement()!);
                case Kind.ExportAssignment:
                    return VisitExportAssignment(node.AsExportAssignment()!);
                case Kind.PrivateIdentifier:
                    return VisitPrivateIdentifier(node);
                case Kind.PropertyAccessExpression:
                    return VisitPropertyAccessExpression(node.AsPropertyAccessExpression()!);
                case Kind.ElementAccessExpression:
                    return VisitElementAccessExpression(node.AsElementAccessExpression()!);
                case Kind.PrefixUnaryExpression:
                case Kind.PostfixUnaryExpression:
                    return VisitPreOrPostfixUnaryExpression(node, false /*discarded*/);
                case Kind.BinaryExpression:
                    return VisitBinaryExpression(node.AsBinaryExpression()!, false /*discarded*/);
                case Kind.ParenthesizedExpression:
                    return VisitParenthesizedExpression(node.AsParenthesizedExpression()!, false /*discarded*/);
                case Kind.CallExpression:
                    return VisitCallExpression(node.AsCallExpression()!);
                case Kind.ExpressionStatement:
                    return VisitExpressionStatement(node.AsExpressionStatement()!);
                case Kind.TaggedTemplateExpression:
                    return VisitTaggedTemplateExpression(node.AsTaggedTemplateExpression()!);
                case Kind.ForStatement:
                    return VisitForStatement(node.AsForStatement()!);
                case Kind.ForInStatement:
                case Kind.ForOfStatement:
                case Kind.DoStatement:
                case Kind.WhileStatement:
                    return SetInIterationStatementAnd(true, VisitEachChildOfNode, node);
                case Kind.ThisKeyword:
                    return VisitThisExpression(node);
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                    return SetInIterationStatementAnd(false, VisitFunctionExpressionOrDeclaration, node);
                case Kind.Constructor:
                case Kind.MethodDeclaration:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                    return SetInIterationStatementAnd(false, SetClassElementAndVisitEachChild, node);
                default:
                    return Visitor().VisitEachChild(node);
            }
        }
        finally
        {
            PopNode(grandparentNode);
        }
    }

    /// <summary>Go: <c>visitDiscardedValue</c>（classfields.go:343-355）——访问结果被丢弃的表达式中的节点。</summary>
    private Node? VisitDiscardedValue(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PrefixUnaryExpression:
            case Kind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, true /*discarded*/);
            case Kind.BinaryExpression:
                return VisitBinaryExpression(node.AsBinaryExpression()!, true /*discarded*/);
            case Kind.ParenthesizedExpression:
                return VisitParenthesizedExpression(node.AsParenthesizedExpression()!, true /*discarded*/);
            default:
                return Visit(node);
        }
    }

    /// <summary>Go: <c>visitHeritageClause</c>（classfields.go:357-367）——访问 HeritageClause 中的节点。</summary>
    private Node? VisitHeritageClause(Node node)
    {
        switch (node.Kind)
        {
            case Kind.HeritageClause:
                return _heritageClauseVisitor.VisitEachChild(node);
            case Kind.ExpressionWithTypeArguments:
                return VisitExpressionWithTypeArgumentsInHeritageClause(node.AsExpressionWithTypeArguments()!);
            default:
                return Visit(node);
        }
    }

    /// <summary>Go: <c>visitAssignmentTarget</c>（classfields.go:369-376）——访问解构赋值的赋值目标。</summary>
    private Node? VisitAssignmentTarget(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ObjectLiteralExpression:
            case Kind.ArrayLiteralExpression:
                return VisitAssignmentPattern(node);
            default:
                return Visit(node);
        }
    }
}

/// <summary>
/// Go 包级函数（package estransforms）→ 与包同名的静态类（docs/architecture.md §2）。
/// 本分片只放 classfields.go:140-196 的工厂函数；estransforms 其它文件的包级函数由各自分片 partial 到本类。
/// </summary>
public static partial class EsTransforms
{
    /// <summary>Go: <c>func newClassFieldsTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>（classfields.go:140-196）。</summary>
    internal static Transformer? NewClassFieldsTransformer(TransformOptions opts)
    {
        var languageVersion = opts.CompilerOptions!.GetEmitScriptTarget();
        var useDefineForClassFields = opts.CompilerOptions!.GetUseDefineForClassFields();

        // When targeting ESNext+ with useDefineForClassFields (the default), there are no class
        // field transformations to perform and no prior transform sets EFTransformPrivateStaticElements,
        // so every node would be returned unchanged. Skip entirely.
        if (languageVersion >= ScriptTarget.ESNext && useDefineForClassFields)
        {
            return null;
        }

        var tx = new ClassFieldsTransformer(opts);
        // Go 在 NewTransformer 之后创建各 visitor（C# 里 EmitContext() 的字段由 NewTransformer 赋值）——
        // 见 ClassFieldsTransformer.InitTransform（C# 的 private 是类型级，工厂在另一个类型里）。
        tx.InitTransform(opts);
        return tx;
    }
}
