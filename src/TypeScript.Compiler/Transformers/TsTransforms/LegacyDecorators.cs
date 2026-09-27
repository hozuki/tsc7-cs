// ported from: internal/transformers/tstransforms/legacydecorators.go:1-1038
//
// 与 Go 的差异（语言特性；其余见 docs/architecture.md §2/§3）：
// 1) Go 的嵌入 `transformers.Transformer` → 基类继承；`tx.visit` 方法组传给 `NewTransformer`。
// 2) `defer tx.popEnclosingClass()` / `defer tx.popNode()` → `try/finally`（C# 没有 defer）。
// 3) Go 未导出类型 `allDecorators` → C# `AllDecorators`（按 C# 习惯命名），字段 `decorators`/`parameters`
//    → `_decorators`/`_parameters`（未导出字段加 `_`，跨类访问用 internal）。
// 4) Go 的 `map[*ast.Node]*ast.Node` → `Dictionary<Node, Node>?`（nil = 未初始化；Go 的 map 读缺失键
//    返回零值 → C# 用 `TryGetValue`）。
// 5) Go 的 `[]*ast.Node` 具名返回/`append` → C# 的 `Node[]` / `List<Node>`（docs §3.2）。
//    `transformDecorators` 的形参由 Go 的切片改成 `IEnumerable<Node>`：调用点既传数组也传
//    `MultiMap.Get` 的结果（`List<V>`），这样零拷贝。
// 6) `printer.EFxxx` → `EmitFlags.Xxx`；`printer.NameOptions{...}` / `printer.AssignedNameOptions{...}`
//    → `NodeFactory.NameOptions` / `NodeFactory.AssignedNameOptions`（C# 侧是 Printer.NodeFactory 的嵌套类型）。
// 7) `collections.GroupBy` → `Collections.GroupBy`（`TypeScript.Collections.Collections` 静态类）；
//    `mm.Get(k)` 在 C# 返回 `List<V>?`（Go 是 nil slice）→ `?? []`。
// 8) Go 的递归闭包 `var isOrContainsStaticSelfReference func(*ast.Node) bool` → C# 的**局部函数**
//    （直接做方法组传给 `ForEachChild`，零分配）。
// 9) `NewLegacyDecoratorsTransformer` 是 Go 的包级函数 → 同类上的 `public static` 工厂（先例 Printer.NewPrinter）。
// 10) 包级函数（`elideNodes`/`getAllDecoratorsOf*`/`getDecoratedClassElements`/…）落在静态类 `TsTransforms`
//     （docs §2；都是 Go 未导出 → `internal static`）。
// 11) Go 的 `getConstructorDecorationStatement` 可能返回 nil 但调用点无条件 `append`（Go 会插入 nil 元素）；
//     C# 逐字保留（`Add(…!)`），因为该分支在 Go 里也不可达（见函数注释）。
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Transformers;
using static TypeScript.Transformers.TsTransforms.TsTransforms;
// Go: printer.NodeFactory（与 ast.NodeFactory 同名不同包 → 显式限定，避免 CS0104）。
using NodeFactory = TypeScript.Printer.NodeFactory;

namespace TypeScript.Transformers.TsTransforms;

/// <summary>Go: <c>type allDecorators struct</c>。</summary>
internal sealed class AllDecorators
{
    internal Node[] _decorators = [];
    internal Node[][] _parameters = [];
}

/// <summary>Go: <c>type LegacyDecoratorsTransformer struct</c>。</summary>
public sealed class LegacyDecoratorsTransformer : Transformer
{
    private readonly ScriptTarget _languageVersion;
    private readonly ReferenceResolver? _referenceResolver;

    /// <summary>
    /// A map that keeps track of aliases created for classes with decorators to avoid issues
    /// with the double-binding behavior of classes.
    /// </summary>
    private Dictionary<Node, Node>? _classAliases;

    private List<ClassDeclaration> _enclosingClasses = [];

    private LegacyDecoratorsTransformer(ScriptTarget languageVersion, ReferenceResolver? referenceResolver)
    {
        _languageVersion = languageVersion;
        _referenceResolver = referenceResolver;
    }

    /// <summary>Go: <c>func NewLegacyDecoratorsTransformer(opt *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewLegacyDecoratorsTransformer(TransformOptions opt)
    {
        var compilerOptions = opt.CompilerOptions!;
        var tx = new LegacyDecoratorsTransformer(compilerOptions.GetEmitScriptTarget(), opt.Resolver);
        return tx.NewTransformer(tx.Visit, opt.Context);
    }

    private Node? Visit(Node node)
    {
        // we have to visit all identifiers in classes, just in case they require substitution
        if ((node.SubtreeFacts & SubtreeFacts.ContainsDecorators) == 0 && _enclosingClasses.Count == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
                return VisitIdentifier(node.AsIdentifier()!);
            case Kind.PropertyAccessExpression:
                return VisitPropertyAccessExpression(node.AsPropertyAccessExpression()!);
            case Kind.Decorator:
                // Decorators are elided. They will be emitted as part of `visitClassDeclaration`.
                return null;
            case Kind.ClassDeclaration:
                return VisitClassDeclaration(node.AsClassDeclaration()!);
            case Kind.ClassExpression:
                return VisitClassExpression(node.AsClassExpression()!);
            case Kind.Constructor:
                return VisitConstructorDeclaration(node.AsConstructorDeclaration()!);
            case Kind.MethodDeclaration:
                return VisitMethodDeclaration(node.AsMethodDeclaration()!);
            case Kind.SetAccessor:
                return VisitSetAccessorDeclaration(node.AsSetAccessorDeclaration()!);
            case Kind.GetAccessor:
                return VisitGetAccessorDeclaration(node.AsGetAccessorDeclaration()!);
            case Kind.PropertyDeclaration:
                return VisitPropertyDeclaration(node.AsPropertyDeclaration()!);
            case Kind.Parameter:
                return VisitParamerDeclaration(node.AsParameterDeclaration()!);
            case Kind.SourceFile:
                _classAliases = [];
                _enclosingClasses = [];
                var result = Visitor().VisitEachChild(node)!;
                EmitContext().AddEmitHelper(result, EmitContext().ReadEmitHelpers());
                _classAliases = null;
                _enclosingClasses = [];
                return result;
            default:
                return Visitor().VisitEachChild(node);
        }
    }

    private Node? VisitIdentifier(Identifier node)
    {
        // takes the place of `substituteIdentifier` in the strada transform
        foreach (var d in _enclosingClasses)
        {
            if (_classAliases is not null && _classAliases.TryGetValue(d, out var classAlias) && _referenceResolver!.GetReferencedValueDeclaration(EmitContext().MostOriginal(node)!) == EmitContext().MostOriginal(d))
            {
                return classAlias;
            }
        }

        return node;
    }

    private Node? VisitPropertyAccessExpression(PropertyAccessExpression node)
    {
        // Visit the expression but not the name, since property access names should not be substituted.
        // Strada's onSubstituteNode only fires for EmitHint.Expression, which excludes the
        // .name of PropertyAccessExpression.
        var expression = Visitor().VisitNode(node.Expression);
        if (expression != node.Expression)
        {
            return Factory().UpdatePropertyAccessExpression(node, expression, node.QuestionDotToken, node.Name(), node.Flags);
        }

        return node;
    }

    private Node? FinishClassElement(Node? updated, Node original)
    {
        if (updated != original)
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitContext().SetCommentRange(updated!, original.Loc);
            EmitContext().SetSourceMapRange(updated!, MoveRangePastModifiers(original));
        }

        return updated;
    }

    private Node? VisitParamerDeclaration(ParameterDeclaration node)
    {
        var updated = Factory().UpdateParameterDeclaration(
            node,
            ElideModifiers(Factory(), node.Modifiers()),
            node.DotDotDotToken,
            Visitor().VisitNode(node.Name()),
            null,
            null,
            Visitor().VisitNode(node.Initializer));
        if (updated != node)
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitContext().SetCommentRange(updated, node.Loc);
            var newLoc = MoveRangePastModifiers(node);
            updated.Loc = newLoc;
            EmitContext().SetSourceMapRange(updated, newLoc);
            EmitContext().SetEmitFlags(updated.Name()!, EmitFlags.NoTrailingSourceMap);
        }

        return updated;
    }

    // visitPropertyNameOfClassElement visits the property name of a class element,
    // for use when emitting property initializers. For a computed property on a node
    // with decorators, a temporary value is stored for later use.
    private Node? VisitPropertyNameOfClassElement(Node member)
    {
        var name = member.Name();
        if (IsComputedPropertyName(name!) && HasDecorators(member))
        {
            var expression = Visitor().VisitNode(name!.AsComputedPropertyName()!.Expression);
            var innerExpression = SkipPartiallyEmittedExpressions(expression);
            if (!IsSimpleInlineableExpression(innerExpression!))
            {
                var generatedName = Factory().NewGeneratedNameForNode(name);
                EmitContext().AddVariableDeclaration(generatedName);
                return Factory().UpdateComputedPropertyName(name.AsComputedPropertyName()!, Factory().NewAssignmentExpression(generatedName, expression!));
            }
        }

        return Visitor().VisitNode(name);
    }

    private Node? VisitPropertyDeclaration(PropertyDeclaration node)
    {
        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            return null;
        }

        if (HasSyntacticModifier(node, ModifierFlags.Ambient | ModifierFlags.Abstract))
        {
            return null;
        }

        return FinishClassElement(
            Factory().UpdatePropertyDeclaration(
                node,
                Visitor().VisitModifiers(node.Modifiers()),
                VisitPropertyNameOfClassElement(node),
                null,
                null,
                Visitor().VisitNode(node.Initializer)),
            node);
    }

    private Node? VisitGetAccessorDeclaration(GetAccessorDeclaration node)
    {
        return FinishClassElement(
            Factory().UpdateGetAccessorDeclaration(
                node,
                Visitor().VisitModifiers(node.Modifiers()),
                VisitPropertyNameOfClassElement(node),
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                Visitor().VisitNode(node.Body)),
            node);
    }

    private Node? VisitSetAccessorDeclaration(SetAccessorDeclaration node)
    {
        return FinishClassElement(
            Factory().UpdateSetAccessorDeclaration(
                node,
                Visitor().VisitModifiers(node.Modifiers()),
                VisitPropertyNameOfClassElement(node),
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                Visitor().VisitNode(node.Body)),
            node);
    }

    private Node? VisitMethodDeclaration(MethodDeclaration node)
    {
        return FinishClassElement(
            Factory().UpdateMethodDeclaration(
                node,
                Visitor().VisitModifiers(node.Modifiers()),
                node.AsteriskToken,
                VisitPropertyNameOfClassElement(node),
                null,
                null,
                Visitor().VisitNodes(node.Parameters),
                null,
                null,
                Visitor().VisitNode(node.Body)),
            node);
    }

    private Node? VisitConstructorDeclaration(ConstructorDeclaration node)
    {
        return Factory().UpdateConstructorDeclaration(
            node,
            Visitor().VisitModifiers(node.Modifiers()),
            null,
            Visitor().VisitNodes(node.Parameters),
            null,
            null,
            Visitor().VisitNode(node.Body));
    }

    private Node? VisitClassExpression(ClassExpression node)
    {
        // Legacy decorators were not supported on class expressions
        return Factory().UpdateClassExpression(
            node,
            Visitor().VisitModifiers(node.Modifiers()),
            node.Name(),
            null,
            Visitor().VisitNodes(node.HeritageClauses),
            Visitor().VisitNodes(node.Members));
    }

    private Node? VisitClassDeclaration(ClassDeclaration node)
    {
        var decorated = ClassOrConstructorParameterIsDecorated(true, node);
        if (!(decorated || ChildIsDecorated(true, node, null)))
        {
            return Visitor().VisitEachChild(node);
        }

        if (decorated)
        {
            return TransformClassDeclarationWithClassDecorators(node, node.Name());
        }

        return TransformClassDeclarationWithoutClassDecorators(node, node.Name());
    }

    /// <summary>
    /// Transforms a non-decorated class declaration.
    /// </summary>
    private Node? TransformClassDeclarationWithoutClassDecorators(ClassDeclaration node, Node? name)
    {
        //  ${modifiers} class ${name} ${heritageClauses} {
        //      ${members}
        //  }
        var modifiers = Visitor().VisitModifiers(node.Modifiers());
        var heritageClauses = Visitor().VisitNodes(node.HeritageClauses);
        var initialMembers = Visitor().VisitNodes(node.Members);
        var (members, decorationStatements) = TransformDecoratorsOfClassElements(node, initialMembers);

        if (name is null && decorationStatements.Length > 0)
        {
            name = Factory().NewGeneratedNameForNode(node);
        }

        var updated = Factory().UpdateClassDeclaration(
            node,
            modifiers,
            name,
            null,
            heritageClauses,
            members);

        if (decorationStatements.Length == 0)
        {
            return updated;
        }

        var list = new Node[decorationStatements.Length + 1];
        list[0] = updated;
        Array.Copy(decorationStatements, 0, list, 1, decorationStatements.Length);
        return Factory().NewSyntaxList(list);
    }

    private void PopEnclosingClass()
    {
        _enclosingClasses.RemoveAt(_enclosingClasses.Count - 1);
    }

    private void PushEnclosingClass(ClassDeclaration cls)
    {
        _enclosingClasses.Add(cls);
    }

    /// <summary>
    /// Transforms a decorated class declaration and appends the resulting statements. If
    /// the class requires an alias to avoid issues with double-binding, the alias is returned.
    /// </summary>
    private Node? TransformClassDeclarationWithClassDecorators(ClassDeclaration node, Node? name)
    {
        // When we emit an ES6 class that has a class decorator, we must tailor the
        // emit to certain specific cases.
        //
        // In the simplest case, we emit the class declaration as a let declaration, and
        // evaluate decorators after the close of the class body:
        //
        //  [Example 1]
        //  ---------------------------------------------------------------------
        //  TypeScript                      | Javascript
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  class C {                       | }
        //  }                               | C = __decorate([dec], C);
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  export class C {                | }
        //  }                               | C = __decorate([dec], C);
        //                                  | export { C };
        //  ---------------------------------------------------------------------
        //
        // If a class declaration contains a reference to itself *inside* of the class body,
        // this introduces two bindings to the class: One outside of the class body, and one
        // inside of the class body. If we apply decorators as in [Example 1] above, there
        // is the possibility that the decorator `dec` will return a new value for the
        // constructor, which would result in the binding inside of the class no longer
        // pointing to the same reference as the binding outside of the class.
        //
        // As a result, we must instead rewrite all references to the class *inside* of the
        // class body to instead point to a local temporary alias for the class:
        //
        //  [Example 2]
        //  ---------------------------------------------------------------------
        //  TypeScript                      | Javascript
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = C_1 = class C {
        //  class C {                       |   static x() { return C_1.y; }
        //    static x() { return C.y; }    | }
        //    static y = 1;                 | C.y = 1;
        //  }                               | C = C_1 = __decorate([dec], C);
        //                                  | var C_1;
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  export class C {                |   static x() { return C_1.y; }
        //    static x() { return C.y; }    | }
        //    static y = 1;                 | C.y = 1;
        //  }                               | C = C_1 = __decorate([dec], C);
        //                                  | export { C };
        //                                  | var C_1;
        //  ---------------------------------------------------------------------
        //
        // If a class declaration is the default export of a module, we instead emit
        // the export after the decorated declaration:
        //
        //  [Example 3]
        //  ---------------------------------------------------------------------
        //  TypeScript                      | Javascript
        //  ---------------------------------------------------------------------
        //  @dec                            | let default_1 = class {
        //  export default class {          | }
        //  }                               | default_1 = __decorate([dec], default_1);
        //                                  | export default default_1;
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  export default class C {        | }
        //  }                               | C = __decorate([dec], C);
        //                                  | export default C;
        //  ---------------------------------------------------------------------
        //
        // If the class declaration is the default export and a reference to itself
        // inside of the class body, we must emit both an alias for the class *and*
        // move the export after the declaration:
        //
        //  [Example 4]
        //  ---------------------------------------------------------------------
        //  TypeScript                      | Javascript
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  export default class C {        |   static x() { return C_1.y; }
        //    static x() { return C.y; }    | }
        //    static y = 1;                 | C.y = 1;
        //  }                               | C = C_1 = __decorate([dec], C);
        //                                  | export default C;
        //                                  | var C_1;
        //  ---------------------------------------------------------------------
        //

        var isExport = HasSyntacticModifier(node, ModifierFlags.Export);
        var isDefault = HasSyntacticModifier(node, ModifierFlags.Default);
        ModifierList? modifiers = null;
        if (node.Modifiers() is not null && node.Modifiers()!.Nodes is { Length: > 0 })
        {
            var modifierNodes = Filter(node.Modifiers()!.Nodes!, IsNotExportOrDefaultOrDecorator);
            if (modifierNodes.Length != node.Modifiers()!.Nodes!.Length)
            {
                modifiers = Factory().NewModifierList(modifierNodes);
                modifiers.Loc = node.Modifiers()!.Loc;
            }
            else
            {
                modifiers = node.Modifiers();
            }
        }

        var location = MoveRangePastModifiers(node);
        var classAlias = GetClassAliasIfNeeded(node);
        if (classAlias is not null)
        {
            PushEnclosingClass(node);
        }

        try
        {
            // When we used to transform to ES5/3 this would be moved inside an IIFE and should reference the name
            // without any block-scoped variable collision handling - but we don't support that anymore, so we always
            // use the local name for the class
            var declName = Factory().GetLocalNameEx(node, new NodeFactory.AssignedNameOptions { AllowComments = false, AllowSourceMaps = true });

            //  ... = class ${name} ${heritageClauses} {
            //      ${members}
            //  }
            var heritageClauses = Visitor().VisitNodes(node.HeritageClauses);
            var members = Visitor().VisitNodes(node.Members);

            (members, var decorationStatements) = TransformDecoratorsOfClassElements(node, members);

            // If we're emitting to ES2022 or later then we need to reassign the class alias before
            // static initializers are evaluated.
            var assignClassAliasInStaticBlock = _languageVersion >= ScriptTarget.ES2022 && classAlias is not null && members is not null && members.Nodes is { Length: > 0 } && Some(members.Nodes, IsClassStaticBlockDeclarationOrStaticProperty);
            if (assignClassAliasInStaticBlock)
            {
                var memberList = new List<Node>
                {
                    Factory().NewClassStaticBlockDeclaration(null, Factory().NewBlock(
                        Factory().NewNodeList([
                            Factory().NewExpressionStatement(
                                Factory().NewAssignmentExpression(classAlias!, Factory().NewKeywordExpression(Kind.ThisKeyword)))]),
                        false)),
                };
                memberList.AddRange(members!.Nodes ?? []);
                var newList = Factory().NewNodeList(memberList.ToArray());
                newList.Loc = members.Loc;
                members = newList;
            }

            var exprName = name;
            if (name is not null && IsGeneratedIdentifier(EmitContext(), name))
            {
                exprName = null;
            }

            var classExpression = Factory().NewClassExpression(
                modifiers,
                exprName,
                null,
                heritageClauses,
                members);

            EmitContext().SetOriginal(classExpression, node);
            classExpression.Loc = location;

            //  let ${name} = ${classExpression} where name is either declaredName if the class doesn't contain self-reference
            //                                         or decoratedClassAlias if the class contain self-reference.
            var varInitializer = classExpression;
            if (classAlias is not null && !assignClassAliasInStaticBlock)
            {
                varInitializer = Factory().NewAssignmentExpression(classAlias, classExpression);
            }

            var varDecl = Factory().NewVariableDeclaration(
                declName,
                null,
                null,
                varInitializer);
            EmitContext().SetOriginal(varDecl, node);

            var varDeclList = Factory().NewVariableDeclarationList(Factory().NewNodeList([varDecl]), NodeFlags.Let);
            var varStatement = Factory().NewVariableStatement(null, varDeclList);
            EmitContext().SetOriginal(varStatement, node);
            varStatement.Loc = location;
            EmitContext().SetCommentRange(varStatement, node.Loc);

            var statements = new List<Node> { varStatement };
            statements.AddRange(decorationStatements);
            // Go 无条件 append（即使返回 nil）；该分支在 Go 里也不可达：transformClassDeclarationWithClassDecorators
            // 只在 ClassOrConstructorParameterIsDecorated 为真时调用，此时 __decorate 表达式必非空。
            statements.Add(GetConstructorDecorationStatement(node)!);

            if (isExport)
            {
                var exportStatement = isDefault
                    ? Factory().NewExportDefault(declName)
                    : Factory().NewExternalModuleExport(Factory().GetDeclarationName(node));
                statements.Add(exportStatement);
            }

            if (statements.Count == 1)
            {
                return statements[0];
            }

            return Factory().NewSyntaxList(statements.ToArray());
        }
        finally
        {
            if (classAlias is not null)
            {
                PopEnclosingClass();
            }
        }
    }

    private bool HasInternalStaticReference(ClassDeclaration node)
    {
        var classNode = EmitContext().MostOriginal(node);

        bool IsOrContainsStaticSelfReference(Node n)
        {
            if (IsIdentifier(n) && _referenceResolver!.GetReferencedValueDeclaration(EmitContext().MostOriginal(n)!) == classNode)
            {
                return true;
            }

            // For PropertyAccessExpression, only check the expression, not the name.
            // The .Name() is a property access name, not a value reference to the class.
            if (IsPropertyAccessExpression(n))
            {
                return IsOrContainsStaticSelfReference(n.Expression()!);
            }

            return n.ForEachChild(IsOrContainsStaticSelfReference);
        }

        foreach (var member in node.Members!.Nodes ?? [])
        {
            if (member.ForEachChild(IsOrContainsStaticSelfReference))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a local alias for a class declaration if it is a decorated class with an internal
    /// reference to the static side of the class. This is necessary to avoid issues with
    /// double-binding semantics for the class name.
    /// </summary>
    private Node? GetClassAliasIfNeeded(ClassDeclaration node)
    {
        if (!HasInternalStaticReference(node))
        {
            return null;
        }

        var nameText = "default";
        if (node.Name() is not null && !IsGeneratedIdentifier(EmitContext(), node.Name()!))
        {
            nameText = node.Name()!.Text();
        }

        var classAlias = Factory().NewUniqueName(nameText);
        EmitContext().AddVariableDeclaration(classAlias);
        _classAliases![node] = classAlias;

        return classAlias;
    }

    /// <summary>Generates a <c>__decorate</c> helper call for a class constructor.</summary>
    private Node? GetConstructorDecorationStatement(ClassDeclaration node)
    {
        var expression = GenerateConstructorDecorationExpression(node);
        if (expression is not null)
        {
            var result = Factory().NewExpressionStatement(expression);
            EmitContext().SetOriginal(result, node);
            return result;
        }

        return null;
    }

    /// <summary>Generates a <c>__decorate</c> helper call for a class constructor.</summary>
    private Node? GenerateConstructorDecorationExpression(ClassDeclaration node)
    {
        var allDecorators = GetAllDecoratorsOfClass(node, true);
        // Decorator expressions are evaluated outside the class body, so references to the
        // class name should use the original binding, not the class alias. In Strada, this is
        // handled by NodeCheckFlags.ConstructorReference which is only set for identifiers
        // inside the class body. Since Corsa lacks per-node flags, we temporarily pop the
        // enclosing class to prevent alias substitution during decorator expression visiting.
        var hasAlias = _enclosingClasses.Count > 0 && _enclosingClasses[^1] == node;
        if (hasAlias)
        {
            PopEnclosingClass();
        }

        Node[] decoratorExpressions;
        try
        {
            decoratorExpressions = TransformAllDecoratorsOfDeclaration(allDecorators);
        }
        finally
        {
            if (hasAlias)
            {
                PushEnclosingClass(node);
            }
        }

        if (decoratorExpressions.Length == 0)
        {
            return null;
        }

        Node? classAlias = null;
        if (_classAliases is not null && _classAliases.TryGetValue(node, out var alias))
        {
            classAlias = alias;
        }

        // When we used to transform to ES5/3 this would be moved inside an IIFE and should reference the name
        // without any block-scoped variable collision handling - but we don't support that anymore, so we always
        // use the local name for the class
        var localName = Factory().GetDeclarationNameEx(node, new NodeFactory.NameOptions { AllowComments = false, AllowSourceMaps = true });
        var decorate = Factory().NewDecorateHelper(decoratorExpressions, localName, null, null);
        Node assignmentTarget = decorate;
        if (classAlias is not null)
        {
            assignmentTarget = Factory().NewAssignmentExpression(classAlias, decorate);
        }

        var expression = Factory().NewAssignmentExpression(localName, assignmentTarget);
        EmitContext().SetEmitFlags(expression, EmitFlags.NoComments);
        EmitContext().SetSourceMapRange(expression, MoveRangePastModifiers(node));
        return expression;
    }

    private (NodeList? Members, Node[] DecorationStatements) TransformDecoratorsOfClassElements(ClassDeclaration node, NodeList? members)
    {
        var decorationStatements = new List<Node>();
        decorationStatements.AddRange(GetClassElementDecorationStatements(node, false));
        decorationStatements.AddRange(GetClassElementDecorationStatements(node, true));
        if (HasClassElementWithDecoratorContainingPrivateIdentifierInExpression(node))
        {
            Node[] memberNodes = [];
            if (members is not null && members.Nodes is { Length: > 0 })
            {
                memberNodes = members.Nodes;
            }

            var combined = new Node[memberNodes.Length + 1];
            Array.Copy(memberNodes, combined, memberNodes.Length);
            combined[^1] = Factory().NewClassStaticBlockDeclaration(null, Factory().NewBlock(Factory().NewNodeList(decorationStatements.ToArray()), true));
            members = Factory().NewNodeList(combined);
            return (members, []);
        }

        return (members, decorationStatements.ToArray());
    }

    /// <summary>
    /// Generates statements used to apply decorators to either the static or instance members
    /// of a class.
    /// </summary>
    private Node[] GetClassElementDecorationStatements(ClassDeclaration node, bool isStatic)
    {
        var exprs = GenerateClassElementDecorationExpressions(node, isStatic);
        var statements = new List<Node>();
        foreach (var e in exprs)
        {
            statements.Add(Factory().NewExpressionStatement(e));
        }

        return statements.ToArray();
    }

    /// <summary>
    /// Generates expressions used to apply decorators to either the static or instance members
    /// of a class.
    /// </summary>
    private Node[] GenerateClassElementDecorationExpressions(ClassDeclaration node, bool isStatic)
    {
        var members = GetDecoratedClassElements(node, isStatic);
        var expressions = new List<Node>();
        foreach (var member in members)
        {
            var expr = GenerateClassElementDecorationExpression(node, member);
            if (expr is not null)
            {
                expressions.Add(expr);
            }
        }

        return expressions.ToArray();
    }

    /// <summary>Generates an expression used to evaluate class element decorators at runtime.</summary>
    private Node? GenerateClassElementDecorationExpression(ClassDeclaration node, Node member)
    {
        var allDecorators = GetAllDecoratorsOfClassElement(member, node, true);
        var decoratorExpressions = TransformAllDecoratorsOfDeclaration(allDecorators);
        if (decoratorExpressions.Length == 0)
        {
            return null;
        }

        // Emit the call to __decorate. Given the following:
        //
        //   class C {
        //     @dec method(@dec2 x) {}
        //     @dec get accessor() {}
        //     @dec prop;
        //   }
        //
        // The emit for a method is:
        //
        //   __decorate([
        //       dec,
        //       __param(0, dec2),
        //       __metadata("design:type", Function),
        //       __metadata("design:paramtypes", [Object]),
        //       __metadata("design:returntype", void 0)
        //   ], C.prototype, "method", null);
        //
        // The emit for an accessor is:
        //
        //   __decorate([
        //       dec
        //   ], C.prototype, "accessor", null);
        //
        // The emit for a property is:
        //
        //   __decorate([
        //       dec
        //   ], C.prototype, "prop");
        //

        var prefix = GetClassMemberPrefix(node, member);
        var memberName = GetExpressionForPropertyName(member, (member.Flags & NodeFlags.Ambient) == 0);
        Node descriptor;
        if (IsPropertyDeclaration(member) && !HasAccessorModifier(member))
        {
            // We emit `void 0` here to indicate to `__decorate` that it can invoke `Object.defineProperty` directly, but that it
            // should not invoke `Object.getOwnPropertyDescriptor`.
            descriptor = Factory().NewVoidZeroExpression();
        }
        else
        {
            // We emit `null` here to indicate to `__decorate` that it can invoke `Object.getOwnPropertyDescriptor` directly.
            // We have this extra argument here so that we can inject an explicit property descriptor at a later date.
            descriptor = Factory().NewKeywordExpression(Kind.NullKeyword);
        }

        var helper = Factory().NewDecorateHelper(
            decoratorExpressions,
            prefix,
            memberName,
            descriptor);

        EmitContext().SetEmitFlags(helper, EmitFlags.NoComments);
        EmitContext().SetSourceMapRange(helper, MoveRangePastModifiers(member));
        return helper;
    }

    private bool IsSyntheticMetadataDecorator(Node node)
    {
        return EmitContext().IsCallToHelper(node.Expression()!, "__metadata");
    }

    /// <summary>Transforms all of the decorators for a declaration into an array of expressions.</summary>
    private Node[] TransformAllDecoratorsOfDeclaration(AllDecorators? allDecorators)
    {
        if (allDecorators is null)
        {
            return [];
        }

        // ensure that metadata decorators are last
        var mm = GroupBy(allDecorators._decorators, IsSyntheticMetadataDecorator);
        var metadata = mm.Get(true) ?? [];
        var decorators = mm.Get(false) ?? [];

        var decoratorExpressions = new List<Node>();
        decoratorExpressions.AddRange(TransformDecorators(decorators));
        decoratorExpressions.AddRange(TransformDecoratorsOfParameters(allDecorators._parameters));
        decoratorExpressions.AddRange(TransformDecorators(metadata));
        return decoratorExpressions.ToArray();
    }

    private Node[] TransformDecoratorsOfParameters(Node[][] parameters)
    {
        var results = new List<Node>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var decorators = parameters[i];
            if (decorators is not null && decorators.Length > 0)
            {
                foreach (var decorator in decorators)
                {
                    var helper = Factory().NewParamHelper(
                        Visitor().VisitNode(decorator.Expression()!)!,
                        i,
                        decorator.Expression()!.Loc);
                    EmitContext().SetEmitFlags(helper, EmitFlags.NoComments);
                    results.Add(helper);
                }
            }
        }

        return results.ToArray();
    }

    /// <summary>Transforms a list of decorators into an expression.</summary>
    private Node[] TransformDecorators(IEnumerable<Node> decorators)
    {
        var results = new List<Node>();
        foreach (var d in decorators)
        {
            results.Add(Visitor().VisitNode(d.Expression()!)!);
        }

        return results.ToArray();
    }

    private Node GetClassMemberPrefix(ClassDeclaration node, Node member)
    {
        if (IsStatic(member))
        {
            return Factory().GetDeclarationName(node);
        }

        return GetClassPrototype(node);
    }

    private Node GetClassPrototype(ClassDeclaration node)
    {
        return Factory().NewPropertyAccessExpression(
            Factory().GetDeclarationName(node),
            null,
            Factory().NewIdentifier("prototype"),
            NodeFlags.None);
    }

    private Node? GetExpressionForPropertyName(Node member, bool generateNameForComputedPropertyName)
    {
        var name = member.Name();
        if (IsPrivateIdentifier(name!))
        {
            return Factory().NewIdentifier("");
        }

        if (IsComputedPropertyName(name!))
        {
            if (generateNameForComputedPropertyName && !IsSimpleInlineableExpression(name!.AsComputedPropertyName()!.Expression!))
            {
                return Factory().NewGeneratedNameForNode(name);
            }

            return name!.AsComputedPropertyName()!.Expression;
        }

        if (IsIdentifier(name!))
        {
            return Factory().NewStringLiteral(name!.Text(), TokenFlags.None);
        }

        return Factory().DeepCloneNode(name)!;
    }
}

public static partial class TsTransforms
{
    internal static NodeList? ElideNodes(NodeFactory f, NodeList? nodes)
    {
        if (nodes is null)
        {
            return null;
        }

        if (nodes.Nodes is null || nodes.Nodes.Length == 0)
        {
            return nodes;
        }

        var replacement = f.NewNodeList([]);
        replacement.Loc = nodes.Loc;
        return replacement;
    }

    internal static ModifierList? ElideModifiers(NodeFactory f, ModifierList? nodes)
    {
        if (nodes is null)
        {
            return null;
        }

        if (nodes.Nodes is null || nodes.Nodes.Length == 0)
        {
            return nodes;
        }

        var replacement = f.NewModifierList([]);
        replacement.Loc = nodes.Loc;
        return replacement;
    }

    internal static bool IsClassStaticBlockDeclarationOrStaticProperty(Node node)
    {
        return IsClassStaticBlockDeclaration(node) || (IsPropertyDeclaration(node) && HasStaticModifier(node));
    }

    internal static bool IsNotExportOrDefaultOrDecorator(Node node)
    {
        return !(IsDecorator(node) || node.Kind == Kind.ExportKeyword || node.Kind == Kind.DefaultKeyword);
    }

    internal static bool DecoratorContainsPrivateIdentifierInExpression(Node decorator)
    {
        return (decorator.SubtreeFacts & SubtreeFacts.ContainsPrivateIdentifierInExpression) != 0;
    }

    internal static bool ParameterDecoratorsContainPrivateIdentifierInExpression(Node[] parameterDecorators)
    {
        return Some(parameterDecorators, DecoratorContainsPrivateIdentifierInExpression);
    }

    internal static bool HasClassElementWithDecoratorContainingPrivateIdentifierInExpression(ClassDeclaration node)
    {
        if (node.Members is null || node.Members.Nodes is null || node.Members.Nodes.Length == 0)
        {
            return false;
        }

        foreach (var member in node.Members.Nodes)
        {
            if (!CanHaveDecorators(member))
            {
                continue;
            }

            var allDecorators = GetAllDecoratorsOfClassElement(member, node, true);
            if (allDecorators is null)
            {
                continue;
            }

            if (Some(allDecorators._decorators, DecoratorContainsPrivateIdentifierInExpression))
            {
                return true;
            }

            if (Some(allDecorators._parameters, ParameterDecoratorsContainPrivateIdentifierInExpression))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets an allDecorators object containing the decorators for the class and the decorators for the
    /// parameters of the constructor of the class.
    /// </summary>
    internal static AllDecorators? GetAllDecoratorsOfClass(ClassDeclaration node, bool useLegacyDecorators)
    {
        var decorators = node.Decorators();
        Node[][] parameters = [];
        if (useLegacyDecorators)
        {
            parameters = GetDecoratorsOfParameters(GetFirstConstructorWithBody(node));
        }

        if ((decorators is null || decorators.Length == 0) && parameters.Length == 0)
        {
            return null;
        }

        return new AllDecorators { _decorators = decorators ?? [], _parameters = parameters };
    }

    /// <summary>
    /// Gets an allDecorators object containing the decorators for the member and its parameters.
    /// </summary>
    internal static AllDecorators? GetAllDecoratorsOfClassElement(Node member, ClassDeclaration parent, bool useLegacyDecorators)
    {
        switch (member.Kind)
        {
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                if (!useLegacyDecorators)
                {
                    return GetAllDecoratorsOfMethod(member, false);
                }

                return GetAllDecoratorsOfAccessors(member, parent, true);
            case Kind.MethodDeclaration:
                return GetAllDecoratorsOfMethod(member, useLegacyDecorators);
            case Kind.PropertyDeclaration:
                return GetAllDecoratorsOfProperty(member);
            default:
                return null;
        }
    }

    /// <summary>
    /// Gets an allDecorators object containing the decorators for the accessor and its parameters.
    /// </summary>
    internal static AllDecorators? GetAllDecoratorsOfAccessors(Node accessor, ClassDeclaration parent, bool useLegacyDecorators)
    {
        if (accessor.Body() is null)
        {
            return null;
        }

        var decls = GetAllAccessorDeclarations(parent.Members!.Nodes, accessor);
        Node? firstAccessorWithDecorators = null;
        if (HasDecorators(decls.FirstAccessor!))
        {
            firstAccessorWithDecorators = decls.FirstAccessor;
        }
        else if (decls.SecondAccessor is not null && HasDecorators(decls.SecondAccessor))
        {
            firstAccessorWithDecorators = decls.SecondAccessor;
        }

        if (firstAccessorWithDecorators is null || accessor != firstAccessorWithDecorators)
        {
            return null;
        }

        var decorators = firstAccessorWithDecorators.Decorators();
        Node[][] parameters = [];
        if (useLegacyDecorators && decls.SetAccessor is not null)
        {
            parameters = GetDecoratorsOfParameters(decls.SetAccessor);
        }

        if ((decorators is null || decorators.Length == 0) && parameters.Length == 0)
        {
            return null;
        }

        return new AllDecorators
        {
            _decorators = decorators ?? [],
            _parameters = parameters,
        };
    }

    internal static AllDecorators? GetAllDecoratorsOfProperty(Node property)
    {
        var decorators = property.Decorators();
        if (decorators is null || decorators.Length == 0)
        {
            return null;
        }

        return new AllDecorators { _decorators = decorators };
    }

    internal static AllDecorators? GetAllDecoratorsOfMethod(Node method, bool useLegacyDecorators)
    {
        if (method.Body() is null)
        {
            return null;
        }

        var decorators = method.Decorators();
        Node[][] parameters = [];
        if (useLegacyDecorators)
        {
            parameters = GetDecoratorsOfParameters(method);
        }

        if ((decorators is null || decorators.Length == 0) && parameters.Length == 0)
        {
            return null;
        }

        return new AllDecorators { _decorators = decorators ?? [], _parameters = parameters };
    }

    /// <summary>
    /// Gets an array of arrays of decorators for the parameters of a function-like node.
    /// The offset into the result array should correspond to the offset of the parameter.
    /// </summary>
    internal static Node[][] GetDecoratorsOfParameters(Node? node)
    {
        Node[][] decorators = [];
        if (node is not null)
        {
            var parameters = node.Parameters() ?? [];
            var firstParameterIsThis = parameters.Length > 0 && IsThisParameter(parameters[0]);
            var firstParameterOffset = 0;
            var numParameters = parameters.Length;
            if (firstParameterIsThis)
            {
                firstParameterOffset = 1;
                numParameters--;
            }

            for (var i = 0; i < numParameters; i++)
            {
                var p = parameters[i + firstParameterOffset];
                if (decorators.Length > 0 || HasDecorators(p))
                {
                    if (decorators.Length == 0)
                    {
                        decorators = new Node[numParameters][];
                    }

                    decorators[i] = p.Decorators()!;
                }
            }
        }

        return decorators;
    }

    /// <summary>
    /// Determines whether a class member is either a static or an instance member of a class
    /// that is decorated, or has parameters that are decorated.
    /// </summary>
    internal static bool IsDecoratedClassElement(Node member, bool isStaticElement, ClassDeclaration parent)
    {
        return isStaticElement == IsStatic(member) && NodeOrChildIsDecorated(true, member, parent, null);
    }

    /// <summary>
    /// Gets either the static or instance members of a class that are decorated, or have
    /// parameters that are decorated.
    /// </summary>
    internal static Node[] GetDecoratedClassElements(ClassDeclaration node, bool isStatic)
    {
        if (node.Members is null || node.Members.Nodes is null || node.Members.Nodes.Length == 0)
        {
            return [];
        }

        var members = new List<Node>();
        foreach (var member in node.Members.Nodes)
        {
            if (IsDecoratedClassElement(member, isStatic, node))
            {
                members.Add(member);
            }
        }

        return members.ToArray();
    }
}
