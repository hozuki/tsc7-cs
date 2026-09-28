// ported from: internal/printer/printer.go:1363-1868（签名元素、参数、修饰符、函数体、类/类型/对象字面量元素）
//   + internal/printer/printer.go:1869-1871（emitKeywordTypeNode；票面要求一并落在本文件）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 在 `emitModifierList` 里声明的局部 `type Mode int` + iota 常量在 C# 里无法照抄（C# 不允许局部
//    enum）→ 退化为局部 `const int modeNone/modeModifiers/modeDecorators`（实测：局部 const 也按
//    .editorconfig 的 "local → camelCase" 规则判 IDE1006，PascalCase 名字会被 TreatWarningsAsErrors 判错），
//    循环结构与下标运算逐行不变。
// 2) `EmitFunctionBody(Node body)`：Go 的参数类型是 `*ast.Block`；跨分片约定用 `Node`（调用点两端的
//    `EmitFunctionBody(node.AsBlock())` 与 `EmitFunctionBody(block)` 都能绑上），函数内用一次
//    `var block = body.AsBlock()!;` 拿回 Go 的静态类型（kind 由调用方保证，`!` 与 Go 的类型断言一一对应）。
// 3) `CanEmitSimpleArrowHead(Node parentNode, NodeList? parameters)`：Go 的 `*ast.ParameterList` 是
//    `*ast.NodeList` 别名，且函数体要用 `parameters.Nodes` 与 `parameters.HasTrailingComma()`，
//    所以不能退化成 `Node[]`（票面提示里的 `Node[]` 无法表达 trailing comma）。
// 4) `EmitAccessorDeclaration(Kind token, Node node)`：Go 的 `*ast.AccessorDeclarationBase` 在 C# 里是
//    接口 `IAccessorDeclarationBase`（实现类有两个），跨分片统一收 `Node`，体内用 `Node` 的虚访问器
//    `Name()`/`Modifiers()`/`Body()`（Go 提升字段的等价物）。
// 5) Go 的参数别名（`*ast.BindingName`/`*ast.PropertyName`/`*ast.TypeNode`/`*ast.Expression`/
//    `*ast.Statement`/`*ast.ClassElement`/`*ast.TypeElement`/`*ast.ObjectLiteralElement`/
//    `*ast.ParameterDeclarationNode`/`*ast.TypeParameterDeclarationNode`/`*ast.BlockNode`/`*ast.TokenNode`）
//    在 C# 里都是 `Node`（别名）→ 参数类型写 `Node`/`Node?`（见 docs/ast.md §2）。
// 6) Go 的 nil 切片 `len(x) == 0` 语义（`modifiers.Nodes`/`parent.Modifiers().Nodes`）在 C# 里要显式
//    判 `Nodes is null`，否则会 NRE（`!.Length` 解引用 null）。
// 7) `panic(fmt.Sprintf(...))` → `Debug.Fail($"...")`；`%v` 打在 Kind 上 → `node.Kind.String()`。
// 8) Go 的 `p.OnBeforeEmitNode != nil { p.OnBeforeEmitNode(x) }` → `OnBeforeEmitNode?.Invoke(x)`（本仓库
//    Printer.Emit.cs 的既有写法）。
// 9) `greatestEnd` 的多参数/节点形态按 PrinterCore 钉死的契约写：
//    `internal static int GreatestEnd(int end, params object?[] nodes)`（调用点保持 Go 原样，不选重载）。
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    internal int EmitModifierList(Node parentNode, ModifierList? modifiers, bool allowDecorators)
    {
        if (modifiers is null || modifiers.Nodes is null || modifiers.Nodes.Length == 0)
        {
            return parentNode.Pos();
        }

        if (Every(modifiers.Nodes, IsModifier))
        {
            // if all modifier-likes are `Modifier`, simply emit the list as modifiers.
            EmitList(EmitKeywordNode, parentNode, modifiers, ListFormat.Modifiers);
        }
        else if (Every(modifiers.Nodes, IsDecorator))
        {
            if (!allowDecorators)
            {
                return parentNode.Pos();
            }

            // if all modifier-likes are `Decorator`, simply emit the list as decorators.
            EmitList(EmitModifierLike, parentNode, modifiers, ListFormat.Decorators);
        }
        else
        {
            OnBeforeEmitNodeList?.Invoke(modifiers);

            // partition modifiers into contiguous chunks of `Modifier` or `Decorator` so as to
            // use consistent formatting for each chunk
            // Go 的 iota 常量 ModeNone/ModeModifiers/ModeDecorators → 局部 const；名字按 .editorconfig
            // 的"局部变量 → camelCase"规则（局部 const 也按 local 匹配，PascalCase 会被 IDE1006 判 error）。
            const int modeNone = 0;
            const int modeModifiers = 1;
            const int modeDecorators = 2;

            var lastMode = modeNone;
            var mode = modeNone;
            var start = 0;
            var pos = 0;

            var modifierNodes = modifiers.Nodes ?? [];
            Node lastModifier;
            while (start < modifierNodes.Length)
            {
                while (pos < modifierNodes.Length)
                {
                    lastModifier = modifierNodes[pos];
                    if (IsDecorator(lastModifier))
                    {
                        mode = modeDecorators;
                    }
                    else
                    {
                        mode = modeModifiers;
                    }
                    if (lastMode == modeNone)
                    {
                        lastMode = mode;
                    }
                    else if (mode != lastMode)
                    {
                        break;
                    }
                    pos++;
                }

                var textRange = NewTextRange(-1, -1);
                if (start == 0)
                {
                    textRange = NewTextRange(modifiers.Pos(), textRange.End());
                }
                if (pos == modifierNodes.Length - 1)
                {
                    textRange = NewTextRange(textRange.Pos(), modifiers.End());
                }
                if (allowDecorators || lastMode == modeModifiers)
                {
                    // Go: `p.emitListItems(…, modifierNodes[start:pos], …)`（切片，零拷贝）→ C# 传 (start, count)。
                    EmitListItems(
                        EmitModifierLike,
                        parentNode,
                        modifierNodes,
                        start,
                        pos - start,
                        IfElse(lastMode == modeModifiers, ListFormat.Modifiers, ListFormat.Decorators),
                        false, /*hasTrailingComma*/
                        textRange);
                }
                start = pos;
                lastMode = mode;
                pos++;
            }

            OnAfterEmitNodeList?.Invoke(modifiers);
        }

        return GreatestEnd(parentNode.Pos(), LastOrNil(modifiers.Nodes));
    }

    internal void EmitTypeParameter(TypeParameterDeclaration node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), false /*allowDecorators*/);
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        if (node.Constraint is not null)
        {
            WriteSpace();
            WriteKeyword("extends");
            WriteSpace();
            EmitTypeNodeOutsideExtends(node.Constraint);
        }
        if (node.DefaultType is not null)
        {
            WriteSpace();
            WriteOperator("=");
            WriteSpace();
            EmitTypeNodeOutsideExtends(node.DefaultType);
        }
        ExitNode(node, state);
    }

    internal void EmitTypeParameterDeclarationNode(Node? node)
    {
        // NOTE: QuickInfo uses TypeFormatFlagsWriteTypeArgumentsOfSignature to instruct the NodeBuilder to store type arguments
        // (i.e. type nodes) instead of type parameter declarations in the type parameter list.
        if (IsTypeParameterDeclaration(node!))
        {
            EmitTypeParameter(node!.AsTypeParameterDeclaration()!);
        }
        else
        {
            EmitTypeArgument(node!);
        }
    }

    internal void EmitParameterName(Node name)
    {
        var savedWriteKind = _writeKind;
        _writeKind = WriteKind.Parameter;
        EmitBindingName(name);
        _writeKind = savedWriteKind;
    }

    internal void EmitParameter(ParameterDeclaration node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), true /*allowDecorators*/);
        EmitTokenNode(node.DotDotDotToken);
        EmitParameterName(node.Name()!);
        EmitTokenNode(node.QuestionToken);

        EmitTypeAnnotation(node.Type);

        // The comment position has to fallback to any present node within the parameter declaration because as it turns
        // out, the parser can make parameter declarations with _just_ an initializer.
        EmitInitializer(node.Initializer, GreatestEnd(node.Pos(), node.Type, node.QuestionToken, node.Name(), node.Modifiers()), node);
        ExitNode(node, state);
    }

    internal void EmitParameterDeclarationNode(Node? node)
    {
        EmitParameter(node!.AsParameterDeclaration()!);
    }

    internal void EmitDecorator(Decorator node)
    {
        var state = EnterNode(node);
        WritePunctuation("@");
        EmitExpression(node.Expression!, OperatorPrecedence.LeftHandSide);
        ExitNode(node, state);
    }

    internal void EmitModifierLike(Node node)
    {
        if (IsDecorator(node))
        {
            EmitDecorator(node.AsDecorator()!);
        }
        else if (IsModifier(node))
        {
            EmitKeywordNode(node);
        }
        else
        {
            // Go 的 default 也在 switch 的最后（唯一的兜底分支）。
            Debug.Fail($"unhandled ModifierLike: {node.Kind.String()}");
        }
    }

    internal void EmitTypeParameters(Node parentNode, NodeList? nodes)
    {
        if (nodes is null)
        {
            return;
        }
        // TODO: preserve trailing comma after Strada migration
        EmitList(EmitTypeParameterDeclarationNode, parentNode, nodes, ListFormat.TypeParameters | IfElse(IsArrowFunction(parentNode) /*shouldAllowTrailingComma(parentNode, nodes)*/, ListFormat.AllowTrailingComma, ListFormat.None));
    }

    internal void EmitTypeAnnotation(Node? node)
    {
        if (node is null)
        {
            return;
        }

        WritePunctuation(":");
        WriteSpace();
        EmitTypeNodeOutsideExtends(node);
    }

    internal void EmitInitializer(Node? node, int equalTokenPos, Node contextNode)
    {
        if (node is null)
        {
            return;
        }

        WriteSpace();
        EmitToken(Kind.EqualsToken, equalTokenPos, WriteKind.Operator, contextNode);
        WriteSpace();
        EmitExpression(node, OperatorPrecedenceDisallowComma);
    }

    internal void EmitParameters(Node parentNode, NodeList? parameters)
    {
        GenerateAllNames(parameters);
        // TODO: preserve trailing comma after Strada migration
        EmitList(EmitParameterDeclarationNode, parentNode, parameters, ListFormat.Parameters /*|IfElse(ShouldAllowTrailingComma(parentNode, parameters), ListFormat.AllowTrailingComma, ListFormat.None)*/);
    }

    internal static bool CanEmitSimpleArrowHead(Node parentNode, NodeList? parameters)
    {
        // only arrow functions with a single parameter may have simple arrow head
        // （Go 对 nil 的 `parameters` 会 panic，`parameters.Nodes` 为 nil 时 `len == 0` → 返回 false。）
        if (!IsArrowFunction(parentNode) || parameters is null || parameters.Nodes is null || parameters.Nodes.Length != 1)
        {
            return false;
        }

        var parent = parentNode.AsArrowFunction()!;
        var parameter = parameters.Nodes![0].AsParameterDeclaration()!;
        var parentModifiers = parent.Modifiers();

        return parameter.Pos() == parent.Pos() && // may not have parsed tokens between start of parent and parameter
            parent.TypeParameters is null && // parent may not have type parameters
            parent.Type is null && // parent may not have return type annotation
            (parentModifiers is null || parentModifiers.Nodes is null || parentModifiers.Nodes.Length == 0) && // parent may not have modifiers
            !parameters.HasTrailingComma() && // parameters may not have a trailing comma
            parameter.Modifiers() is null && // parameter may not have decorators or modifiers
            parameter.DotDotDotToken is null && // parameter may not be rest
            parameter.QuestionToken is null && // parameter may not be optional
            parameter.Type is null && // parameter may not have a type annotation
            parameter.Initializer is null && // parameter may not have an initializer
            IsIdentifier(parameter.Name()!); // parameter name must be identifier
    }

    internal void EmitParametersForArrow(Node parentNode /*FunctionType | ConstructorType | ArrowFunction*/, NodeList? parameters)
    {
        if (CanEmitSimpleArrowHead(parentNode, parameters))
        {
            GenerateAllNames(parameters);
            EmitList(EmitParameterDeclarationNode, parentNode, parameters, ListFormat.SingleArrowParameter);
        }
        else
        {
            EmitParameters(parentNode, parameters);
        }
    }

    internal void EmitParametersForIndexSignature(Node parentNode, NodeList? parameters)
    {
        GenerateAllNames(parameters);
        EmitList(EmitParameterDeclarationNode, parentNode, parameters, ListFormat.IndexSignatureParameters);
    }

    internal void EmitSignature(Node node)
    {
        var n = node.FunctionLikeData()!;

        // !!! In old emitter, quickinfo used type arguments in place of type parameters on instantiated signatures
        ////if (n.TypeArguments is not null)
        ////{
        ////    EmitTypeArguments(node, n.TypeArguments);
        ////}
        ////else
        ////{
        EmitTypeParameters(node, n.TypeParameters);
        ////}

        EmitParameters(node, n.Parameters);
        EmitTypeAnnotation(n.Type);
    }

    internal void EmitFunctionBody(Node body)
    {
        var block = body.AsBlock()!;
        _emitContext.AddEmitFlags(body, EmitFlags.NoSourceMap);

        // Use only notification hooks for the body block, not the full comment pipeline.
        // Without this, trailing comments from the original method declaration
        // (e.g., "// Error") leak into synthesized comma expressions when methods
        // are hoisted into pending expressions.
        OnBeforeEmitNode?.Invoke(body);

        GenerateNames(body);

        // !!! Emit with comment after Strada migration
        ////EmitTokenWithComment(Kind.OpenBraceToken, body.Pos(), WriteKind.Punctuation, body);
        WritePunctuation("{");

        IncreaseIndent();
        var detachedState = EmitDetachedCommentsBeforeStatementList(body, block.Statements!.Loc);
        var statementOffset = EmitPrologueDirectives(block.Statements!);
        var pos = _writer.GetTextPos();
        EmitHelpers(body);

        if (ShouldEmitBlockFunctionBodyOnSingleLine(block) && statementOffset == 0 && pos == _writer.GetTextPos())
        {
            DecreaseIndent();
            EmitListRange(EmitStatement, body, block.Statements, ListFormat.SingleLineFunctionBodyStatements, statementOffset, -1);
            IncreaseIndent();
        }
        else
        {
            EmitListRange(EmitStatement, body, block.Statements, ListFormat.MultiLineFunctionBodyStatements, statementOffset, -1);
        }

        EmitDetachedCommentsAfterStatementList(body, block.Statements!.Loc, detachedState);
        DecreaseIndent();

        // !!! Emit comment after Strada migration
        ////EmitTokenEx(Kind.CloseBraceToken, body.Statements.End(), WriteKind.Punctuation, body, TokenEmitFlags.None);
        EmitTokenEx(Kind.CloseBraceToken, block.Statements!.End(), WriteKind.Punctuation, body, TokenEmitFlags.NoComments);

        OnAfterEmitNode?.Invoke(body);
    }

    internal void EmitFunctionBodyNode(Node? node)
    {
        if (node is null)
        {
            WriteTrailingSemicolon();
            return;
        }

        WriteSpace();
        EmitFunctionBody(node);
    }

    //
    // Type Members
    //

    internal void EmitPropertySignature(PropertySignatureDeclaration node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), false /*allowDecorators*/);
        EmitPropertyName(node.Name()!);
        EmitTokenNode(node.PostfixToken);
        EmitTypeAnnotation(node.Type);
        WriteTrailingSemicolon();
        ExitNode(node, state);
    }

    internal void EmitPropertyDeclaration(PropertyDeclaration node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), true /*allowDecorators*/);
        EmitPropertyName(node.Name()!);
        EmitTokenNode(node.PostfixToken);
        EmitTypeAnnotation(node.Type);
        EmitInitializer(node.Initializer, GreatestEnd(node.Name()!.End(), node.Type, node.PostfixToken), node);
        WriteTrailingSemicolon();
        ExitNode(node, state);
    }

    internal void EmitMethodSignature(MethodSignatureDeclaration node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), false /*allowDecorators*/);
        EmitPropertyName(node.Name()!);
        EmitTokenNode(node.PostfixToken);
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitSignature(node);
        WriteTrailingSemicolon();
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitMethodDeclaration(MethodDeclaration node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), true /*allowDecorators*/);
        EmitTokenNode(node.AsteriskToken);
        EmitPropertyName(node.Name()!);
        EmitTokenNode(node.PostfixToken);
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitSignature(node);
        EmitFunctionBodyNode(node.Body);
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitClassStaticBlockDeclaration(ClassStaticBlockDeclaration node)
    {
        var state = EnterNode(node);
        WriteKeyword("static");
        PushNameGenerationScope(node);
        EmitFunctionBodyNode(node.Body);
        PopNameGenerationScope(node);
        ExitNode(node, state);
    }

    internal void EmitConstructor(ConstructorDeclaration node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), false /*allowDecorators*/);
        WriteKeyword("constructor");
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitSignature(node);
        EmitFunctionBodyNode(node.Body);
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitAccessorDeclaration(Kind token, Node node)
    {
        var state = EnterNode(node);
        var pos = EmitModifierList(node, node.Modifiers(), true /*allowDecorators*/);
        EmitToken(token, pos, WriteKind.Keyword, node);
        WriteSpace();
        EmitPropertyName(node.Name()!);
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitSignature(node);
        EmitFunctionBodyNode(node.Body());
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitGetAccessorDeclaration(GetAccessorDeclaration node)
    {
        EmitAccessorDeclaration(Kind.GetKeyword, node);
    }

    internal void EmitSetAccessorDeclaration(SetAccessorDeclaration node)
    {
        EmitAccessorDeclaration(Kind.SetKeyword, node);
    }

    internal void EmitCallSignature(CallSignatureDeclaration node)
    {
        var state = EnterNode(node);
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitSignature(node);
        WriteTrailingSemicolon();
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitConstructSignature(ConstructSignatureDeclaration node)
    {
        var state = EnterNode(node);
        WriteKeyword("new");
        WriteSpace();
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitSignature(node);
        WriteTrailingSemicolon();
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitIndexSignature(IndexSignatureDeclaration node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), false /*allowDecorators*/);
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitParametersForIndexSignature(node, node.Parameters);
        EmitTypeAnnotation(node.Type);
        WriteTrailingSemicolon();
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitClassElement(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PropertyDeclaration:
                EmitPropertyDeclaration(node.AsPropertyDeclaration()!);
                break;
            case Kind.MethodDeclaration:
                EmitMethodDeclaration(node.AsMethodDeclaration()!);
                break;
            case Kind.ClassStaticBlockDeclaration:
                EmitClassStaticBlockDeclaration(node.AsClassStaticBlockDeclaration()!);
                break;
            case Kind.Constructor:
                EmitConstructor(node.AsConstructorDeclaration()!);
                break;
            case Kind.GetAccessor:
                EmitGetAccessorDeclaration(node.AsGetAccessorDeclaration()!);
                break;
            case Kind.SetAccessor:
                EmitSetAccessorDeclaration(node.AsSetAccessorDeclaration()!);
                break;
            case Kind.IndexSignature:
                EmitIndexSignature(node.AsIndexSignatureDeclaration()!);
                break;
            case Kind.SemicolonClassElement:
                EmitSemicolonClassElement(node.AsSemicolonClassElement()!);
                break;
            case Kind.NotEmittedStatement:
                EmitNotEmittedStatement(node.AsNotEmittedStatement()!);
                break;
            case Kind.JSTypeAliasDeclaration:
                EmitTypeAliasDeclaration(node.AsTypeAliasDeclaration()!);
                break;
            // Go 的 default 也在 switch 的最后。
            default:
                Debug.Fail($"unexpected ClassElement: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitTypeElement(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PropertySignature:
                EmitPropertySignature(node.AsPropertySignatureDeclaration()!);
                break;
            case Kind.MethodSignature:
                EmitMethodSignature(node.AsMethodSignatureDeclaration()!);
                break;
            case Kind.CallSignature:
                EmitCallSignature(node.AsCallSignatureDeclaration()!);
                break;
            case Kind.ConstructSignature:
                EmitConstructSignature(node.AsConstructSignatureDeclaration()!);
                break;
            case Kind.GetAccessor:
                EmitGetAccessorDeclaration(node.AsGetAccessorDeclaration()!);
                break;
            case Kind.SetAccessor:
                EmitSetAccessorDeclaration(node.AsSetAccessorDeclaration()!);
                break;
            case Kind.IndexSignature:
                EmitIndexSignature(node.AsIndexSignatureDeclaration()!);
                break;
            case Kind.NotEmittedTypeElement:
                EmitNotEmittedTypeElement(node.AsNotEmittedTypeElement()!);
                break;
            // Go 的 default 也在 switch 的最后。
            default:
                Debug.Fail($"unexpected TypeElement: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitObjectLiteralElement(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PropertyAssignment:
                EmitPropertyAssignment(node.AsPropertyAssignment()!);
                break;
            case Kind.ShorthandPropertyAssignment:
                EmitShorthandPropertyAssignment(node.AsShorthandPropertyAssignment()!);
                break;
            case Kind.SpreadAssignment:
                EmitSpreadAssignment(node.AsSpreadAssignment()!);
                break;
            case Kind.MethodDeclaration:
                EmitMethodDeclaration(node.AsMethodDeclaration()!);
                break;
            case Kind.GetAccessor:
                EmitGetAccessorDeclaration(node.AsGetAccessorDeclaration()!);
                break;
            case Kind.SetAccessor:
                EmitSetAccessorDeclaration(node.AsSetAccessorDeclaration()!);
                break;
            // Go 的 default 也在 switch 的最后。
            default:
                Debug.Fail($"unhandled ObjectLiteralElement: {node.Kind.String()}");
                break;
        }
    }

    //
    // Types
    //

    internal void EmitKeywordTypeNode(Node node)
    {
        EmitKeywordNode(node);
    }
}
