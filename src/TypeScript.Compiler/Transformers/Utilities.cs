// ported from: internal/transformers/utilities.go（375 行，全部）
//
// Go 包级函数 → 静态类 `Transformers`（partial，跨本包 5 个文件；见 transformer.go 文件头「形状契约」）。
//
// 与 Go 的偏离（C# 语言特性）：
// 1) Go 的节点指针类型分两类：
//    * **别名**（`ast.Expression`/`ast.Statement`/`ast.PropertyName`/`ast.BindingName`/`ast.ObjectLiteralElement`… = `Node`）
//      → C# 写 `Node`（C# 没有类型别名，docs/ast.md §2）；
//    * **具体节点结构体**（`*ast.BindingElement`/`*ast.VariableDeclaration`/`*ast.BindingPattern`…）→ C# 写对应的生成类
//      （`BindingElement`/`VariableDeclaration`/`BindingPattern`），Go 的 `element.AsNode()` 在这里退化为恒等（直接传 `element`）。
// 2) Go 里 `x.Name()`/`x.Initializer()` 等 nil 接收者会 panic；C# 用 `!` 表达同一契约（docs §3.2）。
// 3) `panic(...)` → `throw new DebugFailureException(...)`（与 `panic` 同样终止控制流；`Debug.Fail` 是它的包装）。
// 4) `core.LastOrNil(node.ModifierNodes())` 返回 `T`（内部 `default!`，即 Go 的 nil）→ C# 局部变量声明为 `Node?`。
// 5) 局部：Go 允许在同一方法的不同分支重复 `:=` 同名变量（shadowing）；C# 禁（CS0136），故在 `ConvertBindingElementToObjectAssignmentElement`
//    里第二个 `assignment` 改名 `shorthand`（仅局部变量名，无语义差异）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using NodeFactory = TypeScript.Printer.NodeFactory; // 消歧：Ast 与 Printer 各有一个 NodeFactory

namespace TypeScript.Transformers;

public static partial class Transformers
{
    public static bool IsGeneratedIdentifier(EmitContext emitContext, Node name) => emitContext.HasAutoGenerateInfo(name);

    public static bool IsHelperName(EmitContext emitContext, Node name) => (emitContext.EmitFlags(name) & EmitFlags.HelperName) != 0;

    public static bool IsLocalName(EmitContext emitContext, Node name) => (emitContext.EmitFlags(name) & EmitFlags.LocalName) != 0;

    public static bool IsExportName(EmitContext emitContext, Node name) => (emitContext.EmitFlags(name) & EmitFlags.ExportName) != 0;

    public static bool IsIdentifierReference(Node name, Node parent)
    {
        switch (parent.Kind)
        {
            case Kind.BinaryExpression:
            case Kind.PrefixUnaryExpression:
            case Kind.PostfixUnaryExpression:
            case Kind.YieldExpression:
            case Kind.AsExpression:
            case Kind.SatisfiesExpression:
            case Kind.ElementAccessExpression:
            case Kind.NonNullExpression:
            case Kind.SpreadElement:
            case Kind.SpreadAssignment:
            case Kind.ParenthesizedExpression:
            case Kind.ArrayLiteralExpression:
            case Kind.DeleteExpression:
            case Kind.TypeOfExpression:
            case Kind.VoidExpression:
            case Kind.AwaitExpression:
            case Kind.TypeAssertionExpression:
            case Kind.ExpressionWithTypeArguments:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxSpreadAttribute:
            case Kind.JsxExpression:
            case Kind.PartiallyEmittedExpression:
                // all immediate children that can be `Identifier` would be instances of `IdentifierReference`
                return true;
            case Kind.ComputedPropertyName:
            case Kind.Decorator:
            case Kind.IfStatement:
            case Kind.DoStatement:
            case Kind.WhileStatement:
            case Kind.WithStatement:
            case Kind.ReturnStatement:
            case Kind.SwitchStatement:
            case Kind.CaseClause:
            case Kind.ThrowStatement:
            case Kind.ExpressionStatement:
            case Kind.ExportAssignment:
            case Kind.PropertyAccessExpression:
            case Kind.TemplateSpan:
                // only an `Expression()` child that can be `Identifier` would be an instance of `IdentifierReference`
                return parent.Expression() == name;
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.BindingElement:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.PropertyAssignment:
            case Kind.EnumMember:
            case Kind.JsxAttribute:
                // only an `Initializer()` child that can be `Identifier` would be an instance of `IdentifierReference`
                return parent.Initializer() == name;
            case Kind.ShorthandPropertyAssignment:
                return parent.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer == name;
            case Kind.ForStatement:
                return parent.Initializer() == name
                    || parent.AsForStatement()!.Condition == name
                    || parent.AsForStatement()!.Incrementor == name;
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                return parent.Initializer() == name
                    || parent.Expression() == name;
            case Kind.ImportEqualsDeclaration:
                return parent.AsImportEqualsDeclaration()!.ModuleReference == name;
            case Kind.ArrowFunction:
                return parent.Body() == name;
            case Kind.ConditionalExpression:
                return parent.AsConditionalExpression()!.Condition == name
                    || parent.AsConditionalExpression()!.WhenTrue == name
                    || parent.AsConditionalExpression()!.WhenFalse == name;
            case Kind.CallExpression:
            case Kind.NewExpression:
                // Go: slices.Contains(parent.Arguments(), name)（指针相等）
                return parent.Expression() == name
                    || (parent.Arguments() is { } args && System.Array.IndexOf(args, name) >= 0);
            case Kind.TaggedTemplateExpression:
                return parent.AsTaggedTemplateExpression()!.Tag == name;
            case Kind.ImportAttribute:
                return parent.AsImportAttribute()!.Value == name;
            case Kind.JsxOpeningElement:
            case Kind.JsxClosingElement:
                return parent.TagName() == name;
            default:
                return false;
        }
    }

    private static Node ConvertBindingElementToArrayAssignmentElement(EmitContext emitContext, BindingElement element)
    {
        if (element.Name() is null)
        {
            var elision = emitContext.Factory.NewOmittedExpression();
            emitContext.SetOriginal(elision, element);
            emitContext.AssignCommentAndSourceMapRanges(elision, element);
            return elision;
        }
        if (element.DotDotDotToken is not null)
        {
            var spread = emitContext.Factory.NewSpreadElement(element.Name());
            emitContext.SetOriginal(spread, element);
            emitContext.AssignCommentAndSourceMapRanges(spread, element);
            return spread;
        }
        var expression = ConvertBindingNameToAssignmentElementTarget(emitContext, element.Name()!);
        if (element.Initializer is not null)
        {
            var assignment = emitContext.Factory.NewAssignmentExpression(expression, element.Initializer);
            emitContext.SetOriginal(assignment, element);
            emitContext.AssignCommentAndSourceMapRanges(assignment, element);
            return assignment;
        }
        return expression;
    }

    private static Node ConvertBindingElementToObjectAssignmentElement(EmitContext emitContext, BindingElement element)
    {
        if (element.DotDotDotToken is not null)
        {
            var spread = emitContext.Factory.NewSpreadAssignment(element.Name());
            emitContext.SetOriginal(spread, element);
            emitContext.AssignCommentAndSourceMapRanges(spread, element);
            return spread;
        }
        if (element.PropertyName is not null)
        {
            var expression = ConvertBindingNameToAssignmentElementTarget(emitContext, element.Name()!);
            if (element.Initializer is not null)
            {
                expression = emitContext.Factory.NewAssignmentExpression(expression, element.Initializer);
            }
            var assignment = emitContext.Factory.NewPropertyAssignment(
                null /*modifiers*/,
                element.PropertyName,
                null /*postfixToken*/,
                null /*typeNode*/,
                expression);
            emitContext.SetOriginal(assignment, element);
            emitContext.AssignCommentAndSourceMapRanges(assignment, element);
            return assignment;
        }
        Node? equalsToken = null;
        if (element.Initializer is not null)
        {
            equalsToken = emitContext.Factory.NewToken(Kind.EqualsToken);
        }
        var shorthand = emitContext.Factory.NewShorthandPropertyAssignment(
            null /*modifiers*/,
            element.Name(),
            null /*postfixToken*/,
            null /*typeNode*/,
            equalsToken,
            element.Initializer);
        emitContext.SetOriginal(shorthand, element);
        emitContext.AssignCommentAndSourceMapRanges(shorthand, element);
        return shorthand;
    }

    public static Node ConvertBindingPatternToAssignmentPattern(EmitContext emitContext, BindingPattern element)
    {
        switch (element.Kind)
        {
            case Kind.ArrayBindingPattern:
                return ConvertBindingElementToArrayAssignmentPattern(emitContext, element);
            case Kind.ObjectBindingPattern:
                return ConvertBindingElementToObjectAssignmentPattern(emitContext, element);
            default:
                // Go: panic("Unknown binding pattern")
                throw new DebugFailureException("Unknown binding pattern");
        }
    }

    private static Node ConvertBindingElementToObjectAssignmentPattern(EmitContext emitContext, BindingPattern element)
    {
        var properties = new List<Node>();
        foreach (var child in element.Elements!.Nodes ?? [])
        {
            properties.Add(ConvertBindingElementToObjectAssignmentElement(emitContext, child.AsBindingElement()!));
        }
        var propertyList = emitContext.Factory.NewNodeList(properties.ToArray());
        propertyList.Loc = element.Elements!.Loc;
        var @object = emitContext.Factory.NewObjectLiteralExpression(propertyList, false /*multiLine*/);
        emitContext.SetOriginal(@object, element);
        emitContext.AssignCommentAndSourceMapRanges(@object, element);
        return @object;
    }

    private static Node ConvertBindingElementToArrayAssignmentPattern(EmitContext emitContext, BindingPattern element)
    {
        var elements = new List<Node>();
        foreach (var child in element.Elements!.Nodes ?? [])
        {
            elements.Add(ConvertBindingElementToArrayAssignmentElement(emitContext, child.AsBindingElement()!));
        }
        var elementList = emitContext.Factory.NewNodeList(elements.ToArray());
        elementList.Loc = element.Elements!.Loc;
        var @object = emitContext.Factory.NewArrayLiteralExpression(elementList, false /*multiLine*/);
        emitContext.SetOriginal(@object, element);
        emitContext.AssignCommentAndSourceMapRanges(@object, element);
        return @object;
    }

    private static Node ConvertBindingNameToAssignmentElementTarget(EmitContext emitContext, Node element)
    {
        if (IsBindingPattern(element))
        {
            return ConvertBindingPatternToAssignmentPattern(emitContext, element.AsBindingPattern()!);
        }
        return element;
    }

    public static Node? ConvertVariableDeclarationToAssignmentExpression(EmitContext emitContext, VariableDeclaration element)
    {
        if (element.Initializer is null)
        {
            return null;
        }
        var expression = ConvertBindingNameToAssignmentElementTarget(emitContext, element.Name()!);
        var assignment = emitContext.Factory.NewAssignmentExpression(expression, element.Initializer);
        emitContext.SetOriginal(assignment, element);
        emitContext.AssignCommentAndSourceMapRanges(assignment, element);
        return assignment;
    }

    public static Node? SingleOrMany(Node[]? nodes, NodeFactory factory)
    {
        if (nodes is null)
        {
            return null;
        }
        if (nodes.Length == 1)
        {
            return nodes[0];
        }
        return factory.NewSyntaxList(nodes);
    }

    // Used in the module transformer to check if an expression is reasonably without sideeffect,
    //
    //	and thus better to copy into multiple places rather than to cache in a temporary variable
    //	- this is mostly subjective beyond the requirement that the expression not be sideeffecting
    //
    // Also used by the logical assignment downleveling transform to skip temp variables when they're
    // not needed.
    public static bool IsSimpleCopiableExpression(Node expression)
    {
        return IsStringLiteralLike(expression)
            || IsNumericLiteral(expression)
            || IsKeywordKind(expression.Kind)
            || IsIdentifier(expression);
    }

    public static bool IsOriginalNodeSingleLine(EmitContext emitContext, Node? node)
    {
        if (node is null)
        {
            return false;
        }
        var original = emitContext.MostOriginal(node);
        if (original is null)
        {
            return false;
        }
        var source = GetSourceFileOfNode(original);
        if (source is null)
        {
            return false;
        }
        var startLine = GetECMALineOfPosition(source, original.Loc.Pos());
        var endLine = GetECMALineOfPosition(source, original.Loc.End());
        return startLine == endLine;
    }

    /// <summary>
    /// A simple inlinable expression is an expression which can be copied into multiple locations
    /// without risk of repeating any sideeffects and whose value could not possibly change between
    /// any such locations
    /// </summary>
    public static bool IsSimpleInlineableExpression(Node expression) =>
        !IsIdentifier(expression) && IsSimpleCopiableExpression(expression);

    /// <summary>FindSuperStatementIndexPath finds a path of indices to a statement containing a `super()` call.</summary>
    public static int[] FindSuperStatementIndexPath(Node[]? statements, int start)
    {
        var indices = FindSuperStatementIndexPathWorker(statements, start, null);
        if (indices is null)
        {
            // Go: `slices.Reverse(nil)` + 返回 nil；调用点只用 `len(path) > 0` 与下标（见 classfields/esdecorator/runtimesyntax）。
            return [];
        }
        indices.Reverse();
        return indices.ToArray();
    }

    private static List<int>? FindSuperStatementIndexPathWorker(Node[]? statements, int start, List<int>? indices)
    {
        statements ??= [];
        for (var i = start; i < statements.Length; i++)
        {
            var statement = statements[i];
            if (GetSuperCallFromStatement(statement) is not null)
            {
                (indices ??= []).Add(i);
                return indices;
            }
            else if (IsTryStatement(statement))
            {
                // Go: append(result, i) / append(indices, i) —— 只沿返回路径累加，用 List 就地追加语义相同。
                var result = FindSuperStatementIndexPathWorker(
                    statement.AsTryStatement()!.TryBlock!.Statements(),
                    0,
                    indices);
                if (result is not null)
                {
                    result.Add(i);
                    return result;
                }
            }
        }
        return null;
    }

    /// <summary>GetSuperCallFromStatement extracts the super() call expression from an expression statement, if any.</summary>
    public static Node? GetSuperCallFromStatement(Node? statement)
    {
        if (!IsExpressionStatement(statement!))
        {
            return null;
        }
        var expression = SkipParentheses(statement!.Expression())!;
        if (IsSuperCall(expression))
        {
            return expression;
        }
        return null;
    }

    /// <summary>MoveRangePastModifiers returns a text range that starts past any modifiers on the node.</summary>
    public static TextRange MoveRangePastModifiers(Node node)
    {
        if (IsPropertyDeclaration(node) || IsMethodDeclaration(node))
        {
            return NewTextRange(node.Name()!.Pos(), node.End());
        }

        Node? lastModifier = null;
        if (CanHaveModifiers(node))
        {
            lastModifier = LastOrNil(node.ModifierNodes());
        }

        if (lastModifier is not null && !PositionIsSynthesized(lastModifier.End()))
        {
            return NewTextRange(lastModifier.End(), node.End());
        }
        return MoveRangePastDecorators(node);
    }

    /// <summary>MoveRangePastDecorators returns a text range that starts past any decorators on the node.</summary>
    public static TextRange MoveRangePastDecorators(Node node)
    {
        Node? lastDecorator = null;
        if (CanHaveModifiers(node))
        {
            var nodes = node.ModifierNodes();
            if (nodes is not null)
            {
                lastDecorator = FindLast(nodes, IsDecorator);
            }
        }

        if (lastDecorator is not null && !PositionIsSynthesized(lastDecorator.End()))
        {
            return NewTextRange(lastDecorator.End(), node.End());
        }
        return node.Loc;
    }

    /// <summary>GetNonAssignmentOperatorForCompoundAssignment returns the non-assignment operator for a compound assignment.</summary>
    public static Kind GetNonAssignmentOperatorForCompoundAssignment(Kind kind)
    {
        switch (kind)
        {
            case Kind.PlusEqualsToken:
                return Kind.PlusToken;
            case Kind.MinusEqualsToken:
                return Kind.MinusToken;
            case Kind.AsteriskEqualsToken:
                return Kind.AsteriskToken;
            case Kind.AsteriskAsteriskEqualsToken:
                return Kind.AsteriskAsteriskToken;
            case Kind.SlashEqualsToken:
                return Kind.SlashToken;
            case Kind.PercentEqualsToken:
                return Kind.PercentToken;
            case Kind.LessThanLessThanEqualsToken:
                return Kind.LessThanLessThanToken;
            case Kind.GreaterThanGreaterThanEqualsToken:
                return Kind.GreaterThanGreaterThanToken;
            case Kind.GreaterThanGreaterThanGreaterThanEqualsToken:
                return Kind.GreaterThanGreaterThanGreaterThanToken;
            case Kind.AmpersandEqualsToken:
                return Kind.AmpersandToken;
            case Kind.BarEqualsToken:
                return Kind.BarToken;
            case Kind.CaretEqualsToken:
                return Kind.CaretToken;
            case Kind.BarBarEqualsToken:
                return Kind.BarBarToken;
            case Kind.AmpersandAmpersandEqualsToken:
                return Kind.AmpersandAmpersandToken;
            case Kind.QuestionQuestionEqualsToken:
                return Kind.QuestionQuestionToken;
        }
        return kind;
    }
}
