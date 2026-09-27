// ported from: internal/ast/ast.go
//
// 本文件只收录 ast.go 里**手写的**、与具体节点类型相关的 computeSubtreeFacts / propagateSubtreeFacts 覆写
// （对应 C# 的 ComputeSubtreeFacts / PropagateSubtreeFacts），写成 partial class 的覆写方法。
// 每个类一个 partial 块，块上方的 `// Go: ast.go:<行号范围>` 给出 Go 出处；块序与 ast.go 的书写顺序一致。
//
// 覆盖的 ast.go 行号：
//   1619-1621 TypeSyntaxBase（43 个类型语法节点共用，集中列在文件末尾）
//   1623-1670 Token、PrivateIdentifier（1673-1677 是 NodeFactory.NewModifier，不属本文件）
//   1678-1707 Decorator、ForInOrOfStatement、ReturnStatement、CatchClause
//   1709-1793 VariableStatement … FunctionDeclaration
//   1801-1819 ClassLikeBase 的 computeSubtreeFacts（由 ClassDeclaration/ClassExpression 承载）及两者的 propagate
//   1821-1930 HeritageClause … ExportSpecifier
//   1931-2007 ConstructorDeclaration … PropertyDeclaration（含 1950-1967 AccessorDeclarationBase 的两个方法，
//     它们分别由 GetAccessorDeclaration/SetAccessorDeclaration 承载：C# 侧 IAccessorDeclarationBase 只是接口，
//     且这两个节点不在生成器的 generateSubtreeFacts 清单里）
//   2008-2058 ClassStaticBlockDeclaration … BinaryExpression
//   2060-2217 YieldExpression … ExpressionWithTypeArguments
//   2258-2277 TemplateHead、TemplateMiddle、TemplateTail
//   2279-2344 JsxElement … JsxText
//
// 明确不收录（见报告）：
//   ast.go:1242-1248 NodeDefault / 1612-1615 CompositeBase            —— 基础本身，由 Node 统一实现
//   ast.go:2217-2246 ImportAttributesNode.GetResolutionModeOverride   —— 依赖 diagnostics，下一轮
//   ast.go:2821-2823 SourceFile.computeSubtreeFacts                   —— SourceFile 本轮只有最小实现，下一轮
//
// 依赖的 C# 形状（与 docs/ast.md §2、Node.cs 一致）：
//   * Node.SubtreeFacts / Node.ModifierFlags 是**属性**（对应 Go 的 node.SubtreeFacts() / node.ModifierFlags()）。
//     因此裸写 `SubtreeFacts.None`、`ModifierFlags.Ambient` 能解析到同名 enum 类型（C# "Color Color" 规则只覆盖
//     常量/字段/属性/局部变量/参数；若这两个成员是方法，会导致 CS0119）。Node.Kind 是字段，`Kind.Xxx` 同理。
//   * Go 未导出字段 name → _name、modifiers → _modifiers；Go 里不加判空直接解引用的点访问 → C# `!` 后缀。
//   * propagateNodeListSubtreeFacts(x, propagateSubtreeFacts) → Ast.PropagateNodeListSubtreeFacts(x, Ast.PropagateSubtreeFacts)
namespace TypeScript.Ast;

// ---------------------------------------------------------------------------------------------
// ast.go:1623-1793
// ---------------------------------------------------------------------------------------------

// Go: ast.go:1623-1668
public sealed partial class Token
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        switch (Kind)
        {
            case Kind.UsingKeyword:
                return SubtreeFacts.ContainsUsing;
            case Kind.PublicKeyword:
            case Kind.PrivateKeyword:
            case Kind.ProtectedKeyword:
            case Kind.ReadonlyKeyword:
            case Kind.AbstractKeyword:
            case Kind.DeclareKeyword:
            case Kind.ConstKeyword:
            case Kind.AnyKeyword:
            case Kind.NumberKeyword:
            case Kind.BigIntKeyword:
            case Kind.NeverKeyword:
            case Kind.ObjectKeyword:
            case Kind.InKeyword:
            case Kind.OutKeyword:
            case Kind.OverrideKeyword:
            case Kind.StringKeyword:
            case Kind.BooleanKeyword:
            case Kind.SymbolKeyword:
            case Kind.VoidKeyword:
            case Kind.UnknownKeyword:
            case Kind.UndefinedKeyword:
            case Kind.ExportKeyword:
                return SubtreeFacts.ContainsTypeScript;
            case Kind.AccessorKeyword:
                return SubtreeFacts.ContainsClassFields;
            case Kind.AsyncKeyword:
                return SubtreeFacts.ContainsAnyAwait;
            case Kind.SuperKeyword:
                return SubtreeFacts.ContainsLexicalSuper;
            case Kind.ThisKeyword:
                return SubtreeFacts.ContainsLexicalThis;
            case Kind.AsteriskAsteriskToken:
            case Kind.AsteriskAsteriskEqualsToken:
                return SubtreeFacts.ContainsExponentiationOperator;
            case Kind.QuestionQuestionToken:
                return SubtreeFacts.ContainsNullishCoalescing;
            case Kind.QuestionDotToken:
                return SubtreeFacts.ContainsOptionalChaining;
            case Kind.QuestionQuestionEqualsToken:
            case Kind.BarBarEqualsToken:
            case Kind.AmpersandAmpersandEqualsToken:
                return SubtreeFacts.ContainsLogicalAssignments;
        }
        return SubtreeFacts.None;
    }
}

// Go: ast.go:1670-1672
public sealed partial class PrivateIdentifier
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsClassFields;
}

// Go: ast.go:1678-1682
public sealed partial class Decorator
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) |
        SubtreeFacts.ContainsTypeScript |
        SubtreeFacts.ContainsDecorators;
}

// Go: ast.go:1684-1689
public sealed partial class ForInOrOfStatement
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Initializer) |
        Ast.PropagateSubtreeFacts(Expression) |
        Ast.PropagateSubtreeFacts(Statement) |
        (AwaitModifier is not null ? SubtreeFacts.ContainsForAwaitOrAsyncGenerator : SubtreeFacts.None);
}

// Go: ast.go:1691-1694
public sealed partial class ReturnStatement
{
    // return in an ES2018 async generator must be awaited
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsForAwaitOrAsyncGenerator;
}

// Go: ast.go:1696-1707
public sealed partial class CatchClause
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        var res = Ast.PropagateSubtreeFacts(VariableDeclaration) |
            Ast.PropagateSubtreeFacts(Block);
        if (VariableDeclaration is null)
        {
            res |= SubtreeFacts.ContainsMissingCatchClauseVariable;
        }
        return res;
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsCatchClause;
}

// Go: ast.go:1709-1716
public sealed partial class VariableStatement
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (_modifiers is not null && (_modifiers.ModifierFlags & ModifierFlags.Ambient) != 0)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(DeclarationList);
        }
    }
}

// Go: ast.go:1718-1723
public sealed partial class VariableDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(_name) |
        Ast.PropagateEraseableSyntaxSubtreeFacts(ExclamationToken) |
        Ast.PropagateEraseableSyntaxSubtreeFacts(Type) |
        Ast.PropagateSubtreeFacts(Initializer);
}

// Go: ast.go:1725-1732
public sealed partial class VariableDeclarationList
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateNodeListSubtreeFacts(Declarations, Ast.PropagateSubtreeFacts) |
        ((Flags & NodeFlags.Using) != 0 ? SubtreeFacts.ContainsUsing : SubtreeFacts.None);

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsVariableDeclarationList;
}

// Go: ast.go:1734-1747
public sealed partial class BindingPattern
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        switch (Kind)
        {
            case Kind.ObjectBindingPattern:
                return Ast.PropagateNodeListSubtreeFacts(Elements, Ast.PropagateObjectBindingElementSubtreeFacts);
            case Kind.ArrayBindingPattern:
                return Ast.PropagateNodeListSubtreeFacts(Elements, Ast.PropagateBindingElementSubtreeFacts);
            default:
                return SubtreeFacts.None;
        }
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsBindingPattern;
}

// Go: ast.go:1749-1763
public sealed partial class ParameterDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (_name is not null && Ast.IsThisIdentifier(_name))
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(QuestionToken) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(Type) |
                Ast.PropagateSubtreeFacts(Initializer);
        }
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsParameter;
}

// Go: ast.go:1765-1770
public sealed partial class BindingElement
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(PropertyName) |
        Ast.PropagateSubtreeFacts(_name) |
        Ast.PropagateSubtreeFacts(Initializer) |
        (DotDotDotToken is not null ? SubtreeFacts.ContainsRestOrSpread : SubtreeFacts.None);
}

// Go: ast.go:1772-1793
public sealed partial class FunctionDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (Body is null || (ModifierFlags & ModifierFlags.Ambient) != 0)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            var isAsync = (ModifierFlags & ModifierFlags.Async) != 0;
            var isGenerator = AsteriskToken is not null;
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(AsteriskToken) |
                Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeParameters) |
                Ast.PropagateNodeListSubtreeFacts(Parameters, Ast.PropagateSubtreeFacts) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(Type) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(FullSignature) |
                Ast.PropagateSubtreeFacts(Body) |
                (isAsync && isGenerator ? SubtreeFacts.ContainsForAwaitOrAsyncGenerator : SubtreeFacts.None) |
                (isAsync && !isGenerator ? SubtreeFacts.ContainsAnyAwait : SubtreeFacts.None);
        }
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsFunction;
}

// ---------------------------------------------------------------------------------------------
// ast.go:1801-1819（ClassLikeBase 提供的 computeSubtreeFacts 由这两个类各自承载）
// ---------------------------------------------------------------------------------------------

// Go: ast.go:1801-1811（ClassLikeBase 的 computeSubtreeFacts）+ ast.go:1813-1815
public sealed partial class ClassDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (_modifiers is not null && (_modifiers.ModifierFlags & ModifierFlags.Ambient) != 0)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeParameters) |
                Ast.PropagateNodeListSubtreeFacts(HeritageClauses, Ast.PropagateSubtreeFacts) |
                Ast.PropagateNodeListSubtreeFacts(Members, Ast.PropagateSubtreeFacts);
        }
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsClass;
}

// Go: ast.go:1801-1811（ClassLikeBase 的 computeSubtreeFacts）+ ast.go:1817-1819
public sealed partial class ClassExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (_modifiers is not null && (_modifiers.ModifierFlags & ModifierFlags.Ambient) != 0)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeParameters) |
                Ast.PropagateNodeListSubtreeFacts(HeritageClauses, Ast.PropagateSubtreeFacts) |
                Ast.PropagateNodeListSubtreeFacts(Members, Ast.PropagateSubtreeFacts);
        }
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsClass;
}

// Go: ast.go:1821-1834
public sealed partial class HeritageClause
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        switch (Token)
        {
            case Kind.ExtendsKeyword:
                return Ast.PropagateNodeListSubtreeFacts(Types, Ast.PropagateSubtreeFacts);
            case Kind.ImplementsKeyword:
                return SubtreeFacts.ContainsTypeScript;
            default:
                return SubtreeFacts.None;
        }
    }
}

// Go: ast.go:1836-1840
public sealed partial class EnumMember
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(_name) |
        Ast.PropagateSubtreeFacts(Initializer) |
        SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1842-1851
public sealed partial class EnumDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (_modifiers is not null && (_modifiers.ModifierFlags & ModifierFlags.Ambient) != 0)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateNodeListSubtreeFacts(Members, Ast.PropagateSubtreeFacts) |
                SubtreeFacts.ContainsTypeScript;
        }
    }
}

// Go: ast.go:1853-1866
public sealed partial class ModuleDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if ((ModifierFlags & ModifierFlags.Ambient) != 0)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateSubtreeFacts(Body) |
                SubtreeFacts.ContainsTypeScript;
        }
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsModule;
}

// Go: ast.go:1868-1880
public sealed partial class ImportEqualsDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (IsTypeOnly || !Ast.IsExternalModuleReference(ModuleReference!))
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateSubtreeFacts(ModuleReference);
        }
    }
}

// Go: ast.go:1882-1889
public sealed partial class ImportSpecifier
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (IsTypeOnly)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateSubtreeFacts(PropertyName) |
                Ast.PropagateSubtreeFacts(_name);
        }
    }
}

// Go: ast.go:1891-1898
public sealed partial class ImportClause
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (PhaseModifier == Kind.TypeKeyword)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateSubtreeFacts(NamedBindings);
        }
    }
}

// Go: ast.go:1900-1902
public sealed partial class ExportAssignment
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateModifierListSubtreeFacts(_modifiers) | Ast.PropagateSubtreeFacts(Type) | Ast.PropagateSubtreeFacts(Expression) | (IsExportEquals ? SubtreeFacts.ContainsTypeScript : SubtreeFacts.None);
}

// Go: ast.go:1908-1914
public sealed partial class ExportDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateModifierListSubtreeFacts(_modifiers) |
        Ast.PropagateSubtreeFacts(ExportClause) |
        Ast.PropagateSubtreeFacts(ModuleSpecifier) |
        Ast.PropagateSubtreeFacts(Attributes) |
        (IsTypeOnly ? SubtreeFacts.ContainsTypeScript : SubtreeFacts.None);
}

// Go: ast.go:1916-1929
public sealed partial class ExportSpecifier
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (IsTypeOnly)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateSubtreeFacts(PropertyName) |
                Ast.PropagateSubtreeFacts(_name);
        }
    }
}

// Go: ast.go:1931-1946
public sealed partial class ConstructorDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (Body is null)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeParameters) |
                Ast.PropagateNodeListSubtreeFacts(Parameters, Ast.PropagateSubtreeFacts) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(Type) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(FullSignature) |
                Ast.PropagateSubtreeFacts(Body);
        }
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsConstructor;
}

// Go: ast.go:1950-1963（AccessorDeclarationBase.computeSubtreeFacts）
//
// Go 里 Get/Set 访问器自身没有覆写，两个方法都来自 AccessorDeclarationBase（唯一的实现）。
// C# 侧基础是接口（IAccessorDeclarationBase，见 Nodes.Generated.cs），没有方法体可继承，
// 所以把那**一份**实现收敛成 `Ast.ComputeAccessorDeclaration`，两个节点类各写一行薄覆写调用它
// （避免同样的逻辑抄两遍以后漏改——这是"base 展开到节点类"方案的单一来源）。
public static partial class Ast
{
    internal static SubtreeFacts ComputeAccessorDeclaration(Node node)
    {
        var body = node.Body();
        if (body is null)
        {
            return SubtreeFacts.ContainsTypeScript;
        }

        var funcLike = node.FunctionLikeData()!;
        return PropagateModifierListSubtreeFacts(node.Modifiers()) |
            PropagateSubtreeFacts(node.Name()) |
            PropagateEraseableSyntaxListSubtreeFacts(funcLike.TypeParameters) |
            PropagateNodeListSubtreeFacts(funcLike.Parameters, PropagateSubtreeFacts) |
            PropagateEraseableSyntaxSubtreeFacts(funcLike.Type) |
            PropagateEraseableSyntaxSubtreeFacts(funcLike.FullSignature) |
            PropagateSubtreeFacts(body);
    }

    // Go: ast.go:1964-1967（AccessorDeclarationBase.propagateSubtreeFacts，同上：收敛成一份）
    internal static SubtreeFacts PropagateAccessorDeclaration(Node node) =>
        (node.SubtreeFacts & ~SubtreeFacts.ExclusionsAccessor) |
        PropagateSubtreeFacts(node.Name());
}

public sealed partial class GetAccessorDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => Ast.ComputeAccessorDeclaration(this);

    internal override SubtreeFacts PropagateSubtreeFacts() => Ast.PropagateAccessorDeclaration(this);
}

public sealed partial class SetAccessorDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => Ast.ComputeAccessorDeclaration(this);

    internal override SubtreeFacts PropagateSubtreeFacts() => Ast.PropagateAccessorDeclaration(this);
}

// Go: ast.go:1969-1992
public sealed partial class MethodDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if (Body is null)
        {
            return SubtreeFacts.ContainsTypeScript;
        }
        else
        {
            var isAsync = _modifiers is not null && (_modifiers.ModifierFlags & ModifierFlags.Async) != 0;
            var isGenerator = AsteriskToken is not null;
            return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
                Ast.PropagateSubtreeFacts(AsteriskToken) |
                Ast.PropagateSubtreeFacts(_name) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(PostfixToken) |
                Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeParameters) |
                Ast.PropagateNodeListSubtreeFacts(Parameters, Ast.PropagateSubtreeFacts) |
                Ast.PropagateSubtreeFacts(Body) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(Type) |
                Ast.PropagateEraseableSyntaxSubtreeFacts(FullSignature) |
                (isAsync && isGenerator ? SubtreeFacts.ContainsForAwaitOrAsyncGenerator : SubtreeFacts.None) |
                (isAsync && !isGenerator ? SubtreeFacts.ContainsAnyAwait : SubtreeFacts.None);
        }
    }

    internal override SubtreeFacts PropagateSubtreeFacts() =>
        (SubtreeFacts & ~SubtreeFacts.ExclusionsMethod) |
        Ast.PropagateSubtreeFacts(_name);
}

// Go: ast.go:1994-2006
public sealed partial class PropertyDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateModifierListSubtreeFacts(_modifiers) |
        Ast.PropagateSubtreeFacts(_name) |
        Ast.PropagateEraseableSyntaxSubtreeFacts(PostfixToken) |
        Ast.PropagateEraseableSyntaxSubtreeFacts(Type) |
        Ast.PropagateSubtreeFacts(Initializer) |
        SubtreeFacts.ContainsClassFields;

    internal override SubtreeFacts PropagateSubtreeFacts() =>
        (SubtreeFacts & ~SubtreeFacts.ExclusionsProperty) |
        Ast.PropagateSubtreeFacts(_name);
}

// Go: ast.go:2008-2012
public sealed partial class ClassStaticBlockDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateModifierListSubtreeFacts(_modifiers) |
        Ast.PropagateSubtreeFacts(Body) |
        SubtreeFacts.ContainsClassFields;
}

// Go: ast.go:2014-2025
public sealed partial class KeywordExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        switch (Kind)
        {
            case Kind.ThisKeyword:
                return SubtreeFacts.ContainsLexicalThis;
            case Kind.SuperKeyword:
                return SubtreeFacts.ContainsLexicalSuper;
        }
        return SubtreeFacts.None;
    }
}

// Go: ast.go:2028-2030
public sealed partial class BigIntLiteral
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.None; // `bigint` is not downleveled in any way
}

// Go: ast.go:2032-2034
public sealed partial class Identifier
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsIdentifier;
}

// Go: ast.go:2036-2041
public sealed partial class NoSubstitutionTemplateLiteral
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if ((TemplateFlags & TokenFlags.ContainsInvalidEscape) != 0)
        {
            return SubtreeFacts.ContainsInvalidTemplateEscape;
        }
        return SubtreeFacts.None;
    }
}

// Go: ast.go:2043-2058
public sealed partial class BinaryExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        var facts = Ast.PropagateModifierListSubtreeFacts(_modifiers) |
            Ast.PropagateSubtreeFacts(Left) |
            Ast.PropagateSubtreeFacts(Type) |
            Ast.PropagateSubtreeFacts(OperatorToken) |
            Ast.PropagateSubtreeFacts(Right) |
            (OperatorToken!.Kind == Kind.InKeyword && Ast.IsPrivateIdentifier(Left!) ? SubtreeFacts.ContainsClassFields | SubtreeFacts.ContainsPrivateIdentifierInExpression : SubtreeFacts.None);
        // Go: ast.go:2052-2056（上一轮 ContainsObjectRestOrSpread 未移植时留下的缺口，AstUtilities 已交付该谓词）
        if (OperatorToken!.Kind == Kind.EqualsToken &&
            (Ast.IsObjectLiteralExpression(Left!) || Ast.IsArrayLiteralExpression(Left!)) &&
            Ast.ContainsObjectRestOrSpread(Left!))
        {
            facts |= SubtreeFacts.ContainsObjectRestOrSpread;
        }

        return facts;
    }
}

// Go: ast.go:2060-2062
public sealed partial class YieldExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsForAwaitOrAsyncGenerator;
}

// Go: ast.go:2064-2076
public sealed partial class ArrowFunction
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateModifierListSubtreeFacts(_modifiers) |
        Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeParameters) |
        Ast.PropagateNodeListSubtreeFacts(Parameters, Ast.PropagateSubtreeFacts) |
        Ast.PropagateEraseableSyntaxSubtreeFacts(Type) |
        Ast.PropagateEraseableSyntaxSubtreeFacts(FullSignature) |
        Ast.PropagateSubtreeFacts(Body) |
        ((ModifierFlags & ModifierFlags.Async) != 0 ? SubtreeFacts.ContainsAnyAwait : SubtreeFacts.None);

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsArrowFunction;
}

// Go: ast.go:2078-2095
public sealed partial class FunctionExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        var isAsync = _modifiers is not null && (_modifiers.ModifierFlags & ModifierFlags.Async) != 0;
        var isGenerator = AsteriskToken is not null;
        return Ast.PropagateModifierListSubtreeFacts(_modifiers) |
            Ast.PropagateSubtreeFacts(AsteriskToken) |
            Ast.PropagateSubtreeFacts(_name) |
            Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeParameters) |
            Ast.PropagateNodeListSubtreeFacts(Parameters, Ast.PropagateSubtreeFacts) |
            Ast.PropagateEraseableSyntaxSubtreeFacts(Type) |
            Ast.PropagateEraseableSyntaxSubtreeFacts(FullSignature) |
            Ast.PropagateSubtreeFacts(Body) |
            (isAsync && isGenerator ? SubtreeFacts.ContainsForAwaitOrAsyncGenerator : SubtreeFacts.None) |
            (isAsync && !isGenerator ? SubtreeFacts.ContainsAnyAwait : SubtreeFacts.None);
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsFunction;
}

// Go: ast.go:2097-2103
public sealed partial class AsExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsOuterExpression;
}

// Go: ast.go:2105-2111
public sealed partial class SatisfiesExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsOuterExpression;
}

// Go: ast.go:2113-2125
public sealed partial class PropertyAccessExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        var privateName = SubtreeFacts.None;
        if (!Ast.IsIdentifier(_name!))
        {
            privateName = SubtreeFacts.ContainsPrivateIdentifierInExpression;
        }
        return Ast.PropagateSubtreeFacts(Expression) |
            Ast.PropagateSubtreeFacts(QuestionDotToken) |
            Ast.PropagateSubtreeFacts(_name) | privateName;
    }

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsPropertyAccess;
}

// Go: ast.go:2127-2129
public sealed partial class ElementAccessExpression
{
    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsElementAccess;
}

// Go: ast.go:2131-2141
public sealed partial class CallExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) |
        Ast.PropagateSubtreeFacts(QuestionDotToken) |
        Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeArguments) |
        Ast.PropagateNodeListSubtreeFacts(Arguments, Ast.PropagateSubtreeFacts) |
        (Expression!.Kind == Kind.ImportKeyword ? SubtreeFacts.ContainsDynamicImport : SubtreeFacts.None);

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsCall;
}

// Go: ast.go:2143-2151
public sealed partial class NewExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) |
        Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeArguments) |
        Ast.PropagateNodeListSubtreeFacts(Arguments, Ast.PropagateSubtreeFacts);

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsNew;
}

// Go: ast.go:2153-2155
public sealed partial class MetaProperty
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(_name) & ~SubtreeFacts.ContainsIdentifier;
}

// Go: ast.go:2157-2159
public sealed partial class NonNullExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:2161-2163
public sealed partial class SpreadElement
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsRestOrSpread;
}

// Go: ast.go:2165-2172
public sealed partial class TaggedTemplateExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Tag) |
        Ast.PropagateSubtreeFacts(QuestionDotToken) |
        Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeArguments) |
        Ast.PropagateSubtreeFacts(Template);
}

// Go: ast.go:2174-2176
public sealed partial class ArrayLiteralExpression
{
    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsArrayLiteral;
}

// Go: ast.go:2178-2180
public sealed partial class ObjectLiteralExpression
{
    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsObjectLiteral;
}

// Go: ast.go:2182-2184
public sealed partial class SpreadAssignment
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsESObjectRestOrSpread | SubtreeFacts.ContainsObjectRestOrSpread;
}

// Go: ast.go:2186-2191
public sealed partial class PropertyAssignment
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(_name) |
        Ast.PropagateSubtreeFacts(Type) |
        Ast.PropagateSubtreeFacts(Initializer);
}

// Go: ast.go:2192-2198
public sealed partial class ShorthandPropertyAssignment
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(_name) |
        Ast.PropagateSubtreeFacts(Type) |
        Ast.PropagateSubtreeFacts(ObjectAssignmentInitializer) |
        SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:2199-2202
public sealed partial class AwaitExpression
{
    // await in an ES2018 async generator must use `yield __await(expr)`
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsAwait | SubtreeFacts.ContainsAnyAwait | SubtreeFacts.ContainsForAwaitOrAsyncGenerator;
}

// Go: ast.go:2204-2210
public sealed partial class TypeAssertion
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts & ~SubtreeFacts.ExclusionsOuterExpression;
}

// Go: ast.go:2212-2216
public sealed partial class ExpressionWithTypeArguments
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) |
        Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeArguments);
}

// ---------------------------------------------------------------------------------------------
// ast.go:2258-2344（模板与 JSX）
// ---------------------------------------------------------------------------------------------

// Go: ast.go:2258-2263
public sealed partial class TemplateHead
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if ((TemplateFlags & TokenFlags.ContainsInvalidEscape) != 0)
        {
            return SubtreeFacts.ContainsInvalidTemplateEscape;
        }
        return SubtreeFacts.None;
    }
}

// Go: ast.go:2265-2270
public sealed partial class TemplateMiddle
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if ((TemplateFlags & TokenFlags.ContainsInvalidEscape) != 0)
        {
            return SubtreeFacts.ContainsInvalidTemplateEscape;
        }
        return SubtreeFacts.None;
    }
}

// Go: ast.go:2272-2277
public sealed partial class TemplateTail
{
    internal override SubtreeFacts ComputeSubtreeFacts()
    {
        if ((TemplateFlags & TokenFlags.ContainsInvalidEscape) != 0)
        {
            return SubtreeFacts.ContainsInvalidTemplateEscape;
        }
        return SubtreeFacts.None;
    }
}

// Go: ast.go:2279-2285
public sealed partial class JsxElement
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(OpeningElement) |
        Ast.PropagateNodeListSubtreeFacts(Children, Ast.PropagateSubtreeFacts) |
        Ast.PropagateSubtreeFacts(ClosingElement) |
        SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2286-2289
public sealed partial class JsxAttributes
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateNodeListSubtreeFacts(Properties, Ast.PropagateSubtreeFacts) |
        SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2291-2296
public sealed partial class JsxNamespacedName
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Namespace) |
        Ast.PropagateSubtreeFacts(_name) |
        SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2297-2303
public sealed partial class JsxOpeningElement
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(TagName) |
        Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeArguments) |
        Ast.PropagateSubtreeFacts(Attributes) |
        SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2304-2310
public sealed partial class JsxSelfClosingElement
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(TagName) |
        Ast.PropagateEraseableSyntaxListSubtreeFacts(TypeArguments) |
        Ast.PropagateSubtreeFacts(Attributes) |
        SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2311-2314
public sealed partial class JsxFragment
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateNodeListSubtreeFacts(Children, Ast.PropagateSubtreeFacts) |
        SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2316-2318
public sealed partial class JsxOpeningFragment
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2320-2322
public sealed partial class JsxClosingFragment
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2324-2329
public sealed partial class JsxAttribute
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(_name) |
        Ast.PropagateSubtreeFacts(Initializer) |
        SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2330-2332
public sealed partial class JsxSpreadAttribute
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2334-2336
public sealed partial class JsxClosingElement
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(TagName) | SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2338-2340
public sealed partial class JsxExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateSubtreeFacts(Expression) | SubtreeFacts.ContainsJsx;
}

// Go: ast.go:2342-2344
public sealed partial class JsxText
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsJsx;
}

// ---------------------------------------------------------------------------------------------
// ast.go:1619-1621 TypeSyntaxBase
//   以下 43 个类型语法节点在 Go 里共用 TypeSyntaxBase 的这两个方法（各自没有独立覆写），
//   这里逐类写出，以便生成的节点类不依赖 Go 的多层内嵌默认方法。
// ---------------------------------------------------------------------------------------------

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class InterfaceDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TypeAliasDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class NamespaceExportDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class CallSignatureDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class ConstructSignatureDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class IndexSignatureDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class MethodSignatureDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class PropertySignatureDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class KeywordTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class UnionTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class IntersectionTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class ConditionalTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TypeOperatorNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class InferTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class ArrayTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class IndexedAccessTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TypeReferenceNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class LiteralTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class ThisTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TypePredicateNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TypeQueryNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class MappedTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TypeLiteralNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TupleTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class NamedTupleMember
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class OptionalTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class RestTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class ParenthesizedTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class FunctionTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class ConstructorTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TemplateLiteralTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TemplateLiteralTypeSpan
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocTypeExpression
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocNonNullableType
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocNullableType
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocAllType
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocVariadicType
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocOptionalType
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocSignature
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocNameReference
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class ImportTypeNode
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class TypeParameterDeclaration
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}

// Go: ast.go:1619-1621（TypeSyntaxBase）
public sealed partial class JSDocTypeLiteral
{
    internal override SubtreeFacts ComputeSubtreeFacts() => SubtreeFacts.ContainsTypeScript;

    internal override SubtreeFacts PropagateSubtreeFacts() => SubtreeFacts.ContainsTypeScript;
}
