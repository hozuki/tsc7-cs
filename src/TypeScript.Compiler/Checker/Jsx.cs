// ported from: internal/checker/jsx.go:1-1488（整文件）
//
// 与 Go 的偏离（逐条；其余地方与 Go 逐行对应）：
//
// 1) 包级函数 `markAsSynthetic`（Go 的 `func X(...)`）→ 同一个 `public sealed partial class Checker` 里的
//    `private static` 成员（docs/checker-plan.md §5.5：C# 没有自由函数，`public static partial class Checker`
//    与实例类同名会 CS0708；先例 `Parser`/`Binder`）。本文件内只有 `ParseIsolatedEntityName` 调用它。
// 2) Go 的 `iter.Seq[JsxElaborationElement]`（`generateJsxChildren` 返回、`elaborateIterableOrArrayLikeTargetElementwise`
//    消费）→ C# 的 `IEnumerable<JsxElaborationElement>`：Go 的 `yield(e) == false`（消费方提前停）在 C# 里由
//    `foreach` + `break`/迭代器 Dispose 等价承担，`yield` 的返回值不再需要。`for e := range iterator` → `foreach`。
// 3) `JsxElaborationElement` 是 Go 结构体（值语义、零值合法）→ C# `sealed class`：Go 的 `JsxElaborationElement{}`
//    零值只用于「没有真实的 child」的哨兵返回（调用点只看 `errorNode`），class 的 `new JsxElaborationElement()`
//    逐字段为 null 与之等价，且对象初始化器与 Go 的具名字段构造一一对应。
// 4) `JsxFlags`/`JsxReferenceKind`（Go 导出类型）→ `[Flags] enum`/`enum`，成员名去 Go 常量名的类型前缀，
//    数值与 Go 一致（docs/architecture.md §3.3）。`JsxNames`/`ReactNames`（Go 里是匿名结构体变量）→ 同名
//    `public static class` + `public const string`，访问语法与 Go 的 `JsxNames.X` 完全一致。
// 5) `JsxElementLinks`（Go 未导出结构体，`core.LinkStore[*ast.Node, JsxElementLinks]` 的值类型）→ 引用类型，
//    因为 `LinkStore<K,V>` 要求 `V : class, new()`（Core/LinkStore.cs）；字段名按 §5.5 的 `_camelCase`。
// 6) `*[]*ast.Diagnostic`（`diagnosticOutput`）→ `ref Diagnostic[]?`（ChkRelater 已落地的定义点形状；
//    `checkTypeAssignableToEx` 那类用 `out`，本文件不涉及）。`nil`（不收集）→ `null`。
//    **C# 的本地函数不能捕获 `ref` 形参**（CS1628）→ `CheckApplicableSignatureForJsxCallLikeElement` 与
//    `ElaborateJsxComponents` 里把 ref 形参复制到局部变量 `outputDiagnostics`，在每个出口写回。
// 7) 同名方法冲突（artifacts/checker-method-collisions.md）：`getContextualTypeForJsxAttribute` →
//    `GetContextualTypeForJsxAttributeWorker`（导出者 `GetContextualTypeForJsxAttribute` 在 Services.cs）、
//    `getJsxNamespace` → `GetJsxNamespaceWorker`（导出者 `GetJsxNamespace` 在 Exports.cs）；
//    其余未导出者一律去掉 `Worker`、用 Go 名 PascalCase（`getJsxType` → `GetJsxType` 等）。
//    被我调用的未导出同名者：`GetApparentTypeWorker`、`GetContextualTypeWorker`、`GetContextualTypeForArgumentAtIndexWorker`、
//    `GetDeclaredTypeOfSymbolWorker`、`GetGlobalSymbolWorker`、`GetIndexTypeOfTypeWorker`、`GetMergedSymbolWorker`、
//    `GetMinTypeArgumentCountWorker`、`GetPropertiesOfTypeWorker`、`GetPropertyOfTypeWorker`、`GetReducedTypeWorker`、
//    `GetResolvedSignatureWorker`、`GetReturnTypeOfSignatureWorker`、`GetSignaturesOfTypeWorker`、`GetTypeAtPositionWorker`、
//    `GetTypeFromTypeNodeWorker`、`GetTypeOfPropertyOfContextualTypeWorker`、`GetTypeOfPropertyOfTypeWorker`、
//    `GetTypeOfSymbolWorker`、`GetUnionTypeWorker`、`HasEffectiveRestParameterWorker`、`IsArrayLikeTypeWorker`、
//    `IsArrayTypeWorker`、`IsContextSensitiveWorker`、`IsTypeAssignableToWorker`、`ResolveAliasWorker`、
//    `GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker`、`FillMissingTypeArgumentsWorker`。
// 8) func 类型字段（Go 的 `c.getGlobalIterableType` 是字段不是方法，见 checker.go:858）→ `_getGlobalIterableType()`。
// 9) `x.Symbol()` → `x.Symbol`（Ast/Node.cs 里 `Symbol`/`SubtreeFacts`/`ModifierFlags` 是**属性**）；
//    `x.AsX()` 在 C# 返回可空 → 按 AST 不变量加 `!`（docs/ast.md §2）。
// 10) Go 的 `panic("...")` → `Debug.Fail("...")` + `throw new System.Diagnostics.UnreachableException()`
//    （`Debug.Fail` 无 `[DoesNotReturn]`；parser/binder/utilities 先例）。
// 11) 诊断消息常量按 `using static TypeScript.Diagnostics.Diagnostics;` 后的**裸名**引用；写
//    `Diagnostics.X` 会先解析到同级命名空间 `TypeScript.Diagnostics`（实测 CS0234，Binder.Declarations.cs 文件头）。
// 12) Go 的 `slices.Index` → `Array.IndexOf`；`slices.Values(properties)` 在
//    `GetSpellingSuggestionForName` 的 C# 形参里直接接收 `Symbol[]`（已落地调用点证实）。
// 13) `core.Map`/`Filter`/`Find`/`OrElse`/`IfElse` 等 → 裸名（`using static TypeScript.Core.Core`）；
//    `ast.*` 判定/工具 → 裸名（`using static TypeScript.Ast.Ast`）；`scanner.SkipTrivia`/`GetTextOfNode` → 裸名。
// 14) `c.program.GetJSXRuntimeImportSpecifier(file.Path())` 沿用 C1 落地的 `Program` 接口
//    （Checker.Program.cs:50 返回 `(string moduleReference, Node? specifier)` 元组）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

/// <summary>Go: <c>type JsxFlags uint32</c> + 常量组（jsx.go:17-24）；成员名去 `JsxFlags` 前缀，数值一致。</summary>
[Flags]
public enum JsxFlags : uint
{
    /// <summary>Go: <c>JsxFlagsNone</c>。</summary>
    None = 0,
    /// <summary>Go: <c>JsxFlagsIntrinsicNamedElement</c>（JSX.IntrinsicElements 的具名属性）。</summary>
    IntrinsicNamedElement = 1 << 0,
    /// <summary>Go: <c>JsxFlagsIntrinsicIndexedElement</c>（JSX.IntrinsicElements 的字符串索引签名）。</summary>
    IntrinsicIndexedElement = 1 << 1,
    /// <summary>Go: <c>JsxFlagsIntrinsicElement</c>。</summary>
    IntrinsicElement = IntrinsicNamedElement | IntrinsicIndexedElement,
}

/// <summary>Go: <c>type JsxReferenceKind int32</c> + iota 常量组（jsx.go:26-32）。</summary>
public enum JsxReferenceKind
{
    /// <summary>Go: <c>JsxReferenceKindComponent</c>。</summary>
    Component = 0,
    /// <summary>Go: <c>JsxReferenceKindFunction</c>。</summary>
    Function = 1,
    /// <summary>Go: <c>JsxReferenceKindMixed</c>。</summary>
    Mixed = 2,
}

/// <summary>Go: <c>type JsxElementLinks struct</c>（jsx.go:34-40）；`LinkStore&lt;Node, JsxElementLinks&gt;` 的值类型
/// （Core 的 `PagedLinkStore&lt;V&gt;` 要求 `V : class, new()`）。字段名按 §5.5。</summary>
public sealed class JsxElementLinks
{
    /// <summary>Go: <c>jsxFlags</c>（Flags for the JSX element）。</summary>
    internal JsxFlags _jsxFlags;
    /// <summary>Go: <c>resolvedJsxElementAttributesType</c>（Resolved element attributes type of a JSX opening-like element）。</summary>
    internal Type? _resolvedJsxElementAttributesType;
    /// <summary>Go: <c>jsxNamespace</c>（Resolved JSX namespace symbol for this node）。</summary>
    internal Symbol? _jsxNamespace;
    /// <summary>Go: <c>jsxImplicitImportContainer</c>（Resolved module symbol the implicit JSX import of this file should refer to）。</summary>
    internal Symbol? _jsxImplicitImportContainer;
    /// <summary>Go: <c>firstJSXTagInFile</c>（The first JSX tag in the file）。</summary>
    internal Node? _firstJSXTagInFile;
}

/// <summary>Go: <c>var JsxNames = struct{...}{...}</c>（jsx.go:42-64）→ 同名静态类 + const 字段（访问语法一致）。</summary>
public static class JsxNames
{
    public const string JSX = "JSX";
    public const string IntrinsicElements = "IntrinsicElements";
    public const string ElementClass = "ElementClass";
    public const string ElementAttributesPropertyNameContainer = "ElementAttributesProperty";
    public const string ElementChildrenAttributeNameContainer = "ElementChildrenAttribute";
    public const string Element = "Element";
    public const string ElementType = "ElementType";
    public const string IntrinsicAttributes = "IntrinsicAttributes";
    public const string IntrinsicClassAttributes = "IntrinsicClassAttributes";
    public const string LibraryManagedAttributes = "LibraryManagedAttributes";
}

/// <summary>Go: <c>var ReactNames = struct{ Fragment string }{"Fragment"}</c>（jsx.go:66-70）。</summary>
public static class ReactNames
{
    public const string Fragment = "Fragment";
}

/// <summary>Go: <c>type JsxElaborationElement struct</c>（jsx.go:369-374）；见文件头偏离 3。</summary>
public sealed class JsxElaborationElement
{
    /// <summary>Go: <c>errorNode</c>。</summary>
    internal Node? _errorNode;
    /// <summary>Go: <c>innerExpression</c>。</summary>
    internal Node? _innerExpression;
    /// <summary>Go: <c>nameType</c>。</summary>
    internal Type? _nameType;
    /// <summary>Go: <c>createDiagnostic func(prop *ast.Node) *ast.Diagnostic</c>（Optional：为该元素定制诊断）。</summary>
    internal Func<Node, Diagnostic>? _createDiagnostic;
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) checkJsxElement(node *ast.Node, checkMode CheckMode) *Type</c>（jsx.go:72-75）。</summary>
    internal Type CheckJsxElement(Node node, CheckMode checkMode)
    {
        CheckNodeDeferred(node);
        return GetJsxElementTypeAt(node);
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxElementDeferred(node *ast.Node)</c>（jsx.go:77-87）。</summary>
    internal void CheckJsxElementDeferred(Node node)
    {
        var jsxElement = node.AsJsxElement()!;
        CheckJsxOpeningLikeElementOrOpeningFragment(jsxElement.OpeningElement!);
        // Perform resolution on the closing tag so that rename/go to definition/etc work
        if (IsJsxIntrinsicTagName(jsxElement.ClosingElement!.TagName()!))
        {
            GetIntrinsicTagSymbol(jsxElement.ClosingElement!);
        }
        else
        {
            CheckExpression(jsxElement.ClosingElement!.TagName()!);
        }
        CheckJsxChildren(node, CheckMode.Normal);
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxExpression(node *ast.Node, checkMode CheckMode) *Type</c>（jsx.go:89-99）。</summary>
    internal Type CheckJsxExpression(Node node, CheckMode checkMode)
    {
        CheckGrammarJsxExpression(node.AsJsxExpression()!);
        if (node.Expression() is null)
        {
            return _errorType;
        }
        var t = CheckExpressionEx(node.Expression()!, checkMode);
        if (node.AsJsxExpression()!.DotDotDotToken is not null && t != _anyType && !IsArrayTypeWorker(t))
        {
            Error(node, JSX_spread_child_must_be_an_array_type);
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxSelfClosingElement(node *ast.Node, checkMode CheckMode) *Type</c>（jsx.go:101-104）。</summary>
    internal Type CheckJsxSelfClosingElement(Node node, CheckMode checkMode)
    {
        CheckNodeDeferred(node);
        return GetJsxElementTypeAt(node);
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxSelfClosingElementDeferred(node *ast.Node)</c>（jsx.go:106-108）。</summary>
    internal void CheckJsxSelfClosingElementDeferred(Node node)
    {
        CheckJsxOpeningLikeElementOrOpeningFragment(node);
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxFragment(node *ast.Node) *Type</c>（jsx.go:110-124）。</summary>
    internal Type CheckJsxFragment(Node node)
    {
        CheckJsxOpeningLikeElementOrOpeningFragment(node.AsJsxFragment()!.OpeningFragment!);
        // by default, jsx:'react' will use jsxFactory = React.createElement and jsxFragmentFactory = React.Fragment
        // if jsxFactory compiler option is provided, ensure jsxFragmentFactory compiler option or @jsxFrag pragma is provided too
        var nodeSourceFile = GetSourceFileOfNode(node);
        if (_compilerOptions.GetJSXTransformEnabled() && (_compilerOptions.JsxFactory != "" || GetPragmaFromSourceFile(nodeSourceFile, "jsx") is not null) && _compilerOptions.JsxFragmentFactory == "" && GetPragmaFromSourceFile(nodeSourceFile, "jsxfrag") is null)
        {
            var message = IfElse(_compilerOptions.JsxFactory != "",
                The_jsxFragmentFactory_compiler_option_must_be_provided_to_use_JSX_fragments_with_the_jsxFactory_compiler_option,
                An_jsxFrag_pragma_is_required_when_using_an_jsx_pragma_with_JSX_fragments);
            Error(node, message);
        }
        CheckJsxChildren(node, CheckMode.Normal);
        var t = GetJsxElementTypeAt(node);
        return IfElse(IsErrorType(t), _anyType, t);
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxAttributes(node *ast.Node, checkMode CheckMode) *Type</c>（jsx.go:126-129）。</summary>
    internal Type CheckJsxAttributes(Node node, CheckMode checkMode)
    {
        CheckNodeDeferred(node);
        return CreateJsxAttributesTypeFromAttributesProperty(node.Parent!, checkMode);
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxOpeningLikeElementOrOpeningFragment(node *ast.Node)</c>（jsx.go:131-158）。</summary>
    internal void CheckJsxOpeningLikeElementOrOpeningFragment(Node node)
    {
        var isNodeOpeningLikeElement = IsJsxOpeningLikeElement(node);
        if (isNodeOpeningLikeElement)
        {
            CheckGrammarJsxElement(node);
        }
        CheckJsxPreconditions(node);
        MarkJsxAliasReferenced(node);
        Signature[]? noCandidates = null;
        var sig = GetResolvedSignatureWorker(node, ref noCandidates, CheckMode.Normal);
        CheckDeprecatedSignature(sig, node);
        if (isNodeOpeningLikeElement)
        {
            var elementTypeConstraint = GetJsxElementTypeTypeAt(node);
            if (elementTypeConstraint is not null)
            {
                var tagName = node.TagName()!;
                Type tagType;
                if (IsJsxIntrinsicTagName(tagName))
                {
                    tagType = GetStringLiteralType(tagName.Text());
                }
                else
                {
                    tagType = CheckExpression(tagName);
                }
                Diagnostic[]? diags = null;
                if (!CheckTypeRelatedToEx(tagType, elementTypeConstraint, _assignableRelation, tagName, Its_type_0_is_not_a_valid_JSX_element_type, ref diags))
                {
                    AddDiagnostic(NewDiagnosticChain(diags![0], X_0_cannot_be_used_as_a_JSX_component, GetTextOfNode(tagName))!);
                }
            }
            else
            {
                CheckJsxReturnAssignableToAppropriateBound(GetJsxReferenceKind(node), GetReturnTypeOfSignatureWorker(sig), node);
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxPreconditions(errorNode *ast.Node)</c>（jsx.go:160-168）。</summary>
    internal void CheckJsxPreconditions(Node errorNode)
    {
        // Preconditions for using JSX
        if (_compilerOptions.Jsx == JsxEmit.None)
        {
            Error(errorNode, Cannot_use_JSX_unless_the_jsx_flag_is_provided);
        }
        if (_noImplicitAny && GetJsxElementTypeAt(errorNode) is null)
        {
            Error(errorNode, JSX_element_implicitly_has_type_any_because_the_global_type_JSX_Element_does_not_exist);
        }
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxReturnAssignableToAppropriateBound(refKind JsxReferenceKind, elemInstanceType *Type, openingLikeElement *ast.Node)</c>（jsx.go:170-196）。</summary>
    internal void CheckJsxReturnAssignableToAppropriateBound(JsxReferenceKind refKind, Type elemInstanceType, Node openingLikeElement)
    {
        Diagnostic[]? diags = null;
        switch (refKind)
        {
            case JsxReferenceKind.Function:
                {
                    var sfcReturnConstraint = GetJsxStatelessElementTypeAt(openingLikeElement);
                    if (sfcReturnConstraint is not null)
                    {
                        CheckTypeRelatedToEx(elemInstanceType, sfcReturnConstraint, _assignableRelation, openingLikeElement.TagName(), Its_return_type_0_is_not_a_valid_JSX_element, ref diags);
                    }
                    break;
                }
            case JsxReferenceKind.Component:
                {
                    var classConstraint = GetJsxElementClassTypeAt(openingLikeElement);
                    if (classConstraint is not null)
                    {
                        // Issue an error if this return type isn't assignable to JSX.ElementClass, failing that
                        CheckTypeRelatedToEx(elemInstanceType, classConstraint, _assignableRelation, openingLikeElement.TagName(), Its_instance_type_0_is_not_a_valid_JSX_element, ref diags);
                    }
                    break;
                }
            default:
                {
                    var sfcReturnConstraint = GetJsxStatelessElementTypeAt(openingLikeElement);
                    var classConstraint = GetJsxElementClassTypeAt(openingLikeElement);
                    if (sfcReturnConstraint is null || classConstraint is null)
                    {
                        return;
                    }
                    var combined = GetUnionTypeWorker([sfcReturnConstraint, classConstraint]);
                    CheckTypeRelatedToEx(elemInstanceType, combined, _assignableRelation, openingLikeElement.TagName(), Its_element_type_0_is_not_a_valid_JSX_element, ref diags);
                    break;
                }
        }
        if (diags is not null && diags.Length != 0)
        {
            AddDiagnostic(NewDiagnosticChain(diags[0], X_0_cannot_be_used_as_a_JSX_component, GetTextOfNode(openingLikeElement.TagName()!))!);
        }
    }

    /// <summary>Go: <c>func (c *Checker) inferJsxTypeArguments(node *ast.Node, signature *Signature, checkMode CheckMode, context *InferenceContext) []*Type</c>（jsx.go:198-203）。</summary>
    internal Type[] InferJsxTypeArguments(Node node, Signature signature, CheckMode checkMode, InferenceContext context)
    {
        var paramType = GetEffectiveFirstArgumentForJsxSignature(signature, node);
        var checkAttrType = CheckExpressionWithContextualType(node.Attributes()!, paramType, context, checkMode);
        InferTypes(context._inferences, checkAttrType, paramType, InferencePriority.None, false);
        return GetInferredTypes(context);
    }

    /// <summary>Go: <c>func (c *Checker) getContextualTypeForJsxExpression(node *ast.Node, contextFlags ContextFlags) *Type</c>（jsx.go:205-213）。</summary>
    internal Type? GetContextualTypeForJsxExpression(Node node, ContextFlags contextFlags)
    {
        if (IsJsxAttributeLike(node.Parent))
        {
            return GetContextualTypeWorker(node, contextFlags);
        }
        if (IsJsxElement(node.Parent!))
        {
            return GetContextualTypeForChildJsxExpression(node.Parent!, node, contextFlags);
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getContextualTypeForJsxAttribute(attribute *ast.Node, contextFlags ContextFlags) *Type</c>
    /// （jsx.go:215-227）→ `...Worker`（同名导出者在 Services.cs，artifacts/checker-method-collisions.md）。</summary>
    internal Type? GetContextualTypeForJsxAttributeWorker(Node attribute, ContextFlags contextFlags)
    {
        // When we trying to resolve JsxOpeningLikeElement as a stateless function element, we will already give its attributes a contextual type
        // which is a type of the parameter of the signature we are trying out.
        // If there is no contextual type (e.g. we are trying to resolve stateful component), get attributes type from resolving element's tagName
        if (IsJsxAttribute(attribute))
        {
            var attributesType = GetApparentTypeOfContextualType(attribute.Parent!, contextFlags);
            if (attributesType is null || IsTypeAny(attributesType))
            {
                return null;
            }
            return GetTypeOfPropertyOfContextualTypeWorker(attributesType, attribute.Name()!.Text());
        }
        return GetContextualTypeWorker(attribute.Parent!, contextFlags);
    }

    /// <summary>Go: <c>func (c *Checker) getContextualJsxElementAttributesType(node *ast.Node, contextFlags ContextFlags) *Type</c>（jsx.go:229-240）。</summary>
    internal Type? GetContextualJsxElementAttributesType(Node node, ContextFlags contextFlags)
    {
        if (IsJsxOpeningElement(node) && contextFlags != ContextFlags.IgnoreNodeInferences)
        {
            var index = FindContextualNode(node.Parent!, contextFlags == ContextFlags.None);
            if (index >= 0)
            {
                // Contextually applied type is moved from attributes up to the outer jsx attributes so when walking up from the children they get hit
                // _However_ to hit them from the _attributes_ we must look for them here; otherwise we'll used the declared type
                // (as below) instead!
                return _contextualInfos[index]._t;
            }
        }
        return GetContextualTypeForArgumentAtIndexWorker(node, 0);
    }

    /// <summary>Go: <c>func (c *Checker) getContextualTypeForChildJsxExpression(node *ast.Node, child *ast.JsxChild, contextFlags ContextFlags) *Type</c>（jsx.go:242-264）。</summary>
    internal Type? GetContextualTypeForChildJsxExpression(Node node, Node child, ContextFlags contextFlags)
    {
        var attributesType = GetApparentTypeOfContextualType(node.AsJsxElement()!.OpeningElement!.Attributes()!, contextFlags);
        // JSX expression is in children of JSX Element, we will look for an "children" attribute (we get the name from JSX.ElementAttributesProperty)
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
        if (!(attributesType is not null && !IsTypeAny(attributesType) && jsxChildrenPropertyName != InternalSymbolNameMissing && jsxChildrenPropertyName != ""))
        {
            return null;
        }
        var realChildren = GetSemanticJsxChildren(node.Children()!.Nodes ?? []);
        var childIndex = Array.IndexOf(realChildren, child);
        var childFieldType = GetTypeOfPropertyOfContextualTypeWorker(attributesType!, jsxChildrenPropertyName);
        if (childFieldType is null)
        {
            return null;
        }
        if (realChildren.Length == 1)
        {
            return childFieldType;
        }
        return MapTypeEx(childFieldType, t =>
        {
            if (IsArrayLikeTypeWorker(t))
            {
                return GetIndexedAccessType(t, GetNumberLiteralType(childIndex));
            }
            return t;
        }, true /*noReductions*/);
    }

    /// <summary>Go: <c>func (c *Checker) discriminateContextualTypeByJSXAttributes(node *ast.Node, contextualType *Type) *Type</c>（jsx.go:266-294）。</summary>
    internal Type DiscriminateContextualTypeByJSXAttributes(Node node, Type contextualType)
    {
        var key = new DiscriminatedContextualTypeKey { _nodeId = GetNodeId(node), _typeId = contextualType._id };
        if (_discriminatedContextualTypes.TryGetValue(key, out var cached) && cached is not null)
        {
            return cached;
        }
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
        var discriminantProperties = Filter(node.Properties(), p =>
        {
            var symbol = p.Symbol;
            if (symbol is null || !IsJsxAttribute(p))
            {
                return false;
            }
            var initializer = p.Initializer();
            return (initializer is null || IsPossiblyDiscriminantValue(initializer)) && IsDiscriminantProperty(contextualType, symbol.Name);
        });
        var discriminantMembers = Filter(GetPropertiesOfTypeWorker(contextualType), s =>
        {
            if ((s.Flags & SymbolFlags.Optional) == 0 || node.Symbol is null)
            {
                return false;
            }
            var element = node.Parent!.Parent;
            if (s.Name == jsxChildrenPropertyName && IsJsxElement(element!) && GetSemanticJsxChildren(element!.Children()!.Nodes ?? []).Length != 0)
            {
                return false;
            }
            return node.Symbol.Members?.GetValueOrDefault(s.Name) is null && IsDiscriminantProperty(contextualType, s.Name);
        });
        var discriminator = new ObjectLiteralDiscriminator { _c = this, _props = discriminantProperties, _members = discriminantMembers };
        var discriminated = DiscriminateTypeByDiscriminableItems(contextualType, discriminator)!;
        _discriminatedContextualTypes[key] = discriminated;
        return discriminated;
    }

    /// <summary>Go: <c>func (c *Checker) elaborateJsxComponents(node *ast.Node, source *Type, target *Type, relation *Relation, diagnosticOutput *[]*ast.Diagnostic) bool</c>（jsx.go:296-367）。</summary>
    internal bool ElaborateJsxComponents(Node node, Type source, Type target, Relation relation, ref Diagnostic[]? diagnosticOutput)
    {
        var outputDiagnostics = diagnosticOutput; // 本地函数不能捕获 ref 形参（文件头偏离 6）
        var reportedError = false;
        foreach (var prop in node.Properties() ?? [])
        {
            if (!IsJsxSpreadAttribute(prop) && !IsHyphenatedJsxName(prop.Name()!.Text()))
            {
                var nameType = GetStringLiteralType(prop.Name()!.Text());
                if (nameType is not null && (nameType._flags & TypeFlags.Never) == 0)
                {
                    reportedError = ElaborateElement(source, target, relation, prop.Name(), prop.Initializer(), nameType, null, null, ref outputDiagnostics) || reportedError;
                }
            }
        }
        if (IsJsxOpeningElement(node.Parent!) && IsJsxElement(node.Parent!.Parent!))
        {
            var containingElement = node.Parent!.Parent!; // Containing JSXElement
            var childrenPropName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
            if (childrenPropName == InternalSymbolNameMissing)
            {
                childrenPropName = "children";
            }
            var childrenNameType = GetStringLiteralType(childrenPropName);
            var childrenTargetType = GetIndexedAccessType(target, childrenNameType);
            var validChildren = GetSemanticJsxChildren(containingElement.Children()!.Nodes ?? []);
            if (validChildren.Length == 0)
            {
                diagnosticOutput = outputDiagnostics;
                return reportedError;
            }
            var moreThanOneRealChildren = validChildren.Length > 1;
            Type arrayLikeTargetParts;
            Type nonArrayLikeTargetParts;
            var iterableType = _getGlobalIterableType();
            if (iterableType != _emptyGenericType)
            {
                var anyIterable = CreateIterableType(_anyType);
                arrayLikeTargetParts = FilterType(childrenTargetType, t => IsTypeAssignableToWorker(t, anyIterable));
                nonArrayLikeTargetParts = FilterType(childrenTargetType, t => !IsTypeAssignableToWorker(t, anyIterable));
            }
            else
            {
                arrayLikeTargetParts = FilterType(childrenTargetType, IsArrayOrTupleLikeType);
                nonArrayLikeTargetParts = FilterType(childrenTargetType, t => !IsArrayOrTupleLikeType(t));
            }
            Message? invalidTextDiagnostic = null;
            object?[] invalidTextDiagnosticArgs = [];
            (Message Message, object?[] Args) getInvalidTextualChildDiagnostic()
            {
                if (invalidTextDiagnostic is null)
                {
                    var tagNameText = GetTextOfNode(node.Parent!.TagName()!);
                    invalidTextDiagnostic = X_0_components_don_t_accept_text_as_child_elements_Text_in_JSX_has_the_type_string_but_the_expected_type_of_1_is_2;
                    invalidTextDiagnosticArgs = [tagNameText, childrenPropName, TypeToString(childrenTargetType)];
                }
                return (invalidTextDiagnostic!, invalidTextDiagnosticArgs);
            }
            if (moreThanOneRealChildren)
            {
                if (arrayLikeTargetParts != _neverType)
                {
                    var realSource = CreateTupleType(CheckJsxChildren(containingElement, CheckMode.Normal));
                    var children = GenerateJsxChildren(containingElement, getInvalidTextualChildDiagnostic);
                    reportedError = ElaborateIterableOrArrayLikeTargetElementwise(children, realSource, arrayLikeTargetParts, relation, ref outputDiagnostics) || reportedError;
                }
                else if (!IsTypeRelatedTo(GetIndexedAccessType(source, childrenNameType), childrenTargetType, relation))
                {
                    // arity mismatch
                    var diag = Error(containingElement.AsJsxElement()!.OpeningElement!.TagName(), This_JSX_tag_s_0_prop_expects_a_single_child_of_type_1_but_multiple_children_were_provided, childrenPropName, TypeToString(childrenTargetType));
                    ReportDiagnostic(diag, ref outputDiagnostics);
                    reportedError = true;
                }
            }
            else
            {
                if (nonArrayLikeTargetParts != _neverType)
                {
                    var child = validChildren[0];
                    var e = GetElaborationElementForJsxChild(child, childrenNameType, getInvalidTextualChildDiagnostic);
                    if (e._errorNode is not null)
                    {
                        reportedError = ElaborateElement(source, target, relation, e._errorNode, e._innerExpression, e._nameType!, null, e._createDiagnostic, ref outputDiagnostics) || reportedError;
                    }
                }
                else if (!IsTypeRelatedTo(GetIndexedAccessType(source, childrenNameType), childrenTargetType, relation))
                {
                    // arity mismatch
                    var diag = Error(containingElement.AsJsxElement()!.OpeningElement!.TagName(), This_JSX_tag_s_0_prop_expects_type_1_which_requires_multiple_children_but_only_a_single_child_was_provided, childrenPropName, TypeToString(childrenTargetType));
                    ReportDiagnostic(diag, ref outputDiagnostics);
                    reportedError = true;
                }
            }
        }
        diagnosticOutput = outputDiagnostics;
        return reportedError;
    }

    /// <summary>Go: <c>func (c *Checker) generateJsxChildren(node *ast.Node, getInvalidTextDiagnostic func() (*diagnostics.Message, []any)) iter.Seq[JsxElaborationElement]</c>（jsx.go:376-391）。</summary>
    private IEnumerable<JsxElaborationElement> GenerateJsxChildren(Node node, Func<(Message Message, object?[] Args)> getInvalidTextDiagnostic)
    {
        var memberOffset = 0;
        var children = node.Children()!.Nodes ?? [];
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            var nameType = GetNumberLiteralType(i - memberOffset);
            var e = GetElaborationElementForJsxChild(child, nameType, getInvalidTextDiagnostic);
            if (e._errorNode is not null)
            {
                yield return e;
            }
            else
            {
                memberOffset++;
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) getElaborationElementForJsxChild(child *ast.Node, nameType *Type, getInvalidTextDiagnostic func() (*diagnostics.Message, []any)) JsxElaborationElement</c>（jsx.go:393-418）。</summary>
    private JsxElaborationElement GetElaborationElementForJsxChild(Node child, Type nameType, Func<(Message Message, object?[] Args)> getInvalidTextDiagnostic)
    {
        switch (child.Kind)
        {
            case Kind.JsxExpression:
                // child is of the type of the expression
                return new JsxElaborationElement { _errorNode = child, _innerExpression = child.Expression(), _nameType = nameType };
            case Kind.JsxText:
                if (child.AsJsxText()!.ContainsOnlyTriviaWhiteSpaces)
                {
                    // Whitespace only jsx text isn't real jsx text
                    return new JsxElaborationElement();
                }
                // child is a string
                return new JsxElaborationElement
                {
                    _errorNode = child,
                    _innerExpression = null,
                    _nameType = nameType,
                    _createDiagnostic = prop =>
                    {
                        var (errorMessage, errorArgs) = getInvalidTextDiagnostic();
                        return NewDiagnosticForNode(prop, errorMessage, errorArgs);
                    },
                };
            case Kind.JsxElement:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxFragment:
                // child is of type JSX.Element
                return new JsxElaborationElement { _errorNode = child, _innerExpression = child, _nameType = nameType };
        }
        Debug.Fail("Unhandled case in getElaborationElementForJsxChild");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go: <c>func (c *Checker) elaborateIterableOrArrayLikeTargetElementwise(iterator iter.Seq[JsxElaborationElement], source *Type, target *Type, relation *Relation, diagnosticOutput *[]*ast.Diagnostic) bool</c>（jsx.go:420-483）。</summary>
    private bool ElaborateIterableOrArrayLikeTargetElementwise(IEnumerable<JsxElaborationElement> iterator, Type source, Type target, Relation relation, ref Diagnostic[]? diagnosticOutput)
    {
        var outputDiagnostics = diagnosticOutput; // 本地迭代器/闭包不能捕获 ref 形参（文件头偏离 6）
        var tupleOrArrayLikeTargetParts = FilterType(target, IsArrayOrTupleLikeType);
        var nonTupleOrArrayLikeTargetParts = FilterType(target, t => !IsArrayOrTupleLikeType(t));
        // If `nonTupleOrArrayLikeTargetParts` is not `never`, then that should mean `Iterable` is defined.
        Type? iterationType = null;
        if (nonTupleOrArrayLikeTargetParts != _neverType)
        {
            iterationType = GetIterationTypeOfIterable(IterationUse.ForOf, IterationTypeKind.Yield, nonTupleOrArrayLikeTargetParts, null /*errorNode*/);
        }
        var reportedError = false;
        foreach (var e in iterator)
        {
            var prop = e._errorNode;
            var next = e._innerExpression;
            var nameType = e._nameType!;
            var targetPropType = iterationType;
            Type? targetIndexedPropType = null;
            if (tupleOrArrayLikeTargetParts != _neverType)
            {
                targetIndexedPropType = GetBestMatchIndexedAccessTypeOrUndefined(source, tupleOrArrayLikeTargetParts, nameType);
            }
            if (targetIndexedPropType is not null && (targetIndexedPropType._flags & TypeFlags.IndexedAccess) == 0)
            {
                if (iterationType is not null)
                {
                    targetPropType = GetUnionTypeWorker([iterationType, targetIndexedPropType]);
                }
                else
                {
                    targetPropType = targetIndexedPropType;
                }
            }
            if (targetPropType is null)
            {
                continue;
            }
            var sourcePropType = GetIndexedAccessTypeOrUndefined(source, nameType, AccessFlags.None, null, null);
            if (sourcePropType is null)
            {
                continue;
            }
            var propName = GetPropertyNameFromIndex(nameType, null /*accessNode*/);
            if (!CheckTypeRelatedTo(sourcePropType, targetPropType, relation, null /*errorNode*/))
            {
                var elaborated = next is not null && ElaborateError(next, sourcePropType, targetPropType, relation, null /*headMessage*/, ref outputDiagnostics);
                reportedError = true;
                if (!elaborated)
                {
                    // Issue error on the prop itself, since the prop couldn't elaborate the error. Use the expression type, if available.
                    var specificSource = sourcePropType;
                    if (next is not null)
                    {
                        specificSource = CheckExpressionForMutableLocationWithContextualType(next, sourcePropType);
                    }
                    var createDiagnostic = e._createDiagnostic;
                    if (createDiagnostic is not null)
                    {
                        // Use the custom diagnostic factory if provided (e.g., for JSX text children with dynamic error messages)
                        ReportDiagnostic(createDiagnostic(prop!), ref outputDiagnostics);
                    }
                    else if (_exactOptionalPropertyTypes && IsExactOptionalPropertyMismatch(specificSource, targetPropType))
                    {
                        var diag = CreateDiagnosticForNode(prop!, Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target, TypeToString(specificSource), TypeToString(targetPropType));
                        ReportDiagnostic(diag, ref outputDiagnostics);
                    }
                    else
                    {
                        var targetIsOptional = propName != InternalSymbolNameMissing && (OrElse(GetPropertyOfTypeWorker(tupleOrArrayLikeTargetParts, propName), _unknownSymbol)!.Flags & SymbolFlags.Optional) != 0;
                        var sourceIsOptional = propName != InternalSymbolNameMissing && (OrElse(GetPropertyOfTypeWorker(source, propName), _unknownSymbol)!.Flags & SymbolFlags.Optional) != 0;
                        targetPropType = RemoveMissingType(targetPropType, targetIsOptional);
                        sourcePropType = RemoveMissingType(sourcePropType, targetIsOptional && sourceIsOptional);
                        var result = CheckTypeRelatedToEx(specificSource, targetPropType, relation, prop, null, ref outputDiagnostics);
                        if (result && specificSource != sourcePropType)
                        {
                            // If for whatever reason the expression type doesn't yield an error, make sure we still issue an error on the sourcePropType
                            CheckTypeRelatedToEx(sourcePropType, targetPropType, relation, prop, null, ref outputDiagnostics);
                        }
                    }
                }
            }
        }
        diagnosticOutput = outputDiagnostics;
        return reportedError;
    }

    /// <summary>Go: <c>func (c *Checker) getSuggestedSymbolForNonexistentJSXAttribute(name string, containingType *Type) *ast.Symbol</c>（jsx.go:485-498）。</summary>
    internal Symbol? GetSuggestedSymbolForNonexistentJSXAttribute(string name, Type containingType)
    {
        var properties = GetPropertiesOfTypeWorker(containingType) ?? [];
        Symbol? jsxSpecific = null;
        switch (name)
        {
            case "for":
                jsxSpecific = Find(properties, x => SymbolName(x) == "htmlFor");
                break;
            case "class":
                jsxSpecific = Find(properties, x => SymbolName(x) == "className");
                break;
        }
        if (jsxSpecific is not null)
        {
            return jsxSpecific;
        }
        return GetSpellingSuggestionForName(name, properties, SymbolFlags.Value);
    }

    /// <summary>Go: <c>func (c *Checker) getJSXFragmentType(node *ast.Node) *Type</c>（jsx.go:500-543）。</summary>
    internal Type GetJSXFragmentType(Node node)
    {
        // An opening fragment is required in order for `getJsxNamespace` to give the fragment factory
        var links = _sourceFileLinks.Get(GetSourceFileOfNode(node)!);
        if (links._jsxFragmentType is not null)
        {
            return links._jsxFragmentType;
        }
        var jsxFragmentFactoryName = GetJsxNamespaceWorker(node);
        // #38720/60122, allow null as jsxFragmentFactory
        var shouldResolveFactoryReference = (_compilerOptions.Jsx == JsxEmit.React || _compilerOptions.JsxFragmentFactory != "") && jsxFragmentFactoryName != "null";
        if (!shouldResolveFactoryReference)
        {
            links._jsxFragmentType = _anyType;
            return links._jsxFragmentType;
        }
        var jsxFactorySymbol = GetJsxNamespaceContainerForImplicitImport(node);
        if (jsxFactorySymbol is null)
        {
            var shouldModuleRefErr = _compilerOptions.Jsx != JsxEmit.Preserve && _compilerOptions.Jsx != JsxEmit.ReactNative;
            var flags = SymbolFlags.Value;
            if (!shouldModuleRefErr)
            {
                flags &= ~SymbolFlags.Enum;
            }
            jsxFactorySymbol = _resolveName(node, jsxFragmentFactoryName, flags, Using_JSX_fragments_requires_fragment_factory_0_to_be_in_scope_but_it_could_not_be_found, true /*isUse*/, false /*excludeGlobals*/);
        }

        if (jsxFactorySymbol is null)
        {
            links._jsxFragmentType = _errorType;
            return links._jsxFragmentType;
        }
        if (jsxFactorySymbol.Name == ReactNames.Fragment)
        {
            links._jsxFragmentType = GetTypeOfSymbolWorker(jsxFactorySymbol);
            return links._jsxFragmentType;
        }
        var resolvedAlias = jsxFactorySymbol;
        if ((jsxFactorySymbol.Flags & SymbolFlags.Alias) != 0)
        {
            resolvedAlias = ResolveAliasWorker(jsxFactorySymbol);
        }

        var reactExports = GetExportsOfSymbol(resolvedAlias);
        var typeSymbol = GetSymbol(reactExports, ReactNames.Fragment, SymbolFlags.BlockScopedVariable);
        if (typeSymbol is not null)
        {
            links._jsxFragmentType = GetTypeOfSymbolWorker(typeSymbol);
        }
        else
        {
            links._jsxFragmentType = _errorType;
        }
        return links._jsxFragmentType;
    }

    /// <summary>Go: <c>func (c *Checker) resolveJsxOpeningLikeElement(node *ast.Node, candidatesOutArray *[]*Signature, checkMode CheckMode) *Signature</c>（jsx.go:545-589）。</summary>
    internal Signature ResolveJsxOpeningLikeElement(Node node, ref Signature[]? candidatesOutArray, CheckMode checkMode)
    {
        var isJsxOpenFragment = IsJsxOpeningFragment(node);
        Type exprTypes;
        if (!isJsxOpenFragment)
        {
            if (IsJsxIntrinsicTagName(node.TagName()!))
            {
                var result = GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(node);
                var fakeSignature = CreateSignatureForJSXIntrinsic(node, result);
                CheckTypeAssignableToAndOptionallyElaborate(CheckExpressionWithContextualType(node.Attributes()!, GetEffectiveFirstArgumentForJsxSignature(fakeSignature, node), null /*inferenceContext*/, CheckMode.Normal), result, node.TagName(), node.Attributes(), null, null);
                var typeArguments = node.TypeArguments();
                if (typeArguments is not null && typeArguments.Length != 0)
                {
                    CheckSourceElements(typeArguments);
                    var sourceFile = GetSourceFileOfNode(node)!;
                    var typeArgumentList = node.TypeArgumentList()!;
                    var loc = NewTextRange(SkipTrivia(sourceFile.Text(), typeArgumentList.Loc.Pos()), typeArgumentList.Loc.End());
                    AddDiagnostic(NewDiagnostic(sourceFile, loc, Expected_0_type_arguments_but_got_1, 0, typeArguments.Length)!);
                }
                return fakeSignature;
            }
            exprTypes = CheckExpression(node.TagName()!);
        }
        else
        {
            exprTypes = GetJSXFragmentType(node);
        }
        var apparentType = GetApparentTypeWorker(exprTypes);
        if (IsErrorType(apparentType))
        {
            return ResolveErrorCall(node);
        }
        var signatures = GetUninstantiatedJsxSignaturesOfType(exprTypes, node);
        if (IsUntypedFunctionCall(exprTypes, apparentType, signatures.Length, 0 /*constructSignatures*/))
        {
            return ResolveUntypedCall(node);
        }
        if (signatures.Length == 0)
        {
            // We found no signatures at all, which is an error
            if (isJsxOpenFragment)
            {
                Error(node, JSX_element_type_0_does_not_have_any_construct_or_call_signatures, GetTextOfNode(node));
            }
            else
            {
                Error(node.TagName(), JSX_element_type_0_does_not_have_any_construct_or_call_signatures, GetTextOfNode(node.TagName()!));
            }
            return ResolveErrorCall(node);
        }
        return ResolveCall(node, signatures, ref candidatesOutArray, checkMode, SignatureFlags.None, null);
    }

    // Check if the given signature can possibly be a signature called by the JSX opening-like element.
    // @param node a JSX opening-like element we are trying to figure its call signature
    // @param signature a candidate signature we are trying whether it is a call signature
    // @param relation a relationship to check parameter and argument type
    /// <summary>Go: <c>func (c *Checker) checkApplicableSignatureForJsxCallLikeElement(node *ast.Node, signature *Signature, relation *Relation, checkMode CheckMode, reportErrors bool, diagnosticOutput *[]*ast.Diagnostic) bool</c>（jsx.go:591-708）。</summary>
    internal bool CheckApplicableSignatureForJsxCallLikeElement(Node node, Signature signature, Relation relation, CheckMode checkMode, bool reportErrors, ref Diagnostic[]? diagnosticOutput)
    {
        // Stateless function components can have maximum of three arguments: "props", "context", and "updater".
        // However "context" and "updater" are implicit and can't be specify by users. Only the first parameter, props,
        // can be specified by users through attributes property.
        var paramType = GetEffectiveFirstArgumentForJsxSignature(signature, node);
        Type attributesType;
        if (IsJsxOpeningFragment(node))
        {
            attributesType = CreateJsxAttributesTypeFromAttributesProperty(node, CheckMode.Normal);
        }
        else
        {
            attributesType = CheckExpressionWithContextualType(node.Attributes()!, paramType, null /*inferenceContext*/, checkMode);
        }
        // C# 的本地函数不能捕获 ref 形参（CS1628）→ 用局部变量承载，出口写回（文件头偏离 6）。
        var outputDiagnostics = diagnosticOutput;
        bool checkTagNameDoesNotExpectTooManyArguments()
        {
            if (GetJsxNamespaceContainerForImplicitImport(node) is not null)
            {
                return true; // factory is implicitly jsx/jsxdev - assume it fits the bill, since we don't strongly look for the jsx/jsxs/jsxDEV factory APIs anywhere else (at least not yet)
            }
            // We assume fragments have the correct arity since the node does not have attributes
            Type? tagType = null;
            if ((IsJsxOpeningElement(node) || IsJsxSelfClosingElement(node)) && !(IsJsxIntrinsicTagName(node.TagName()!) || IsJsxNamespacedName(node.TagName()!)))
            {
                tagType = CheckExpression(node.TagName()!);
            }
            if (tagType is null)
            {
                return true;
            }
            var tagCallSignatures = GetSignaturesOfTypeWorker(tagType, SignatureKind.Call) ?? [];
            if (tagCallSignatures.Length == 0)
            {
                return true;
            }
            var factory = GetJsxFactoryEntity(node);
            if (factory is null)
            {
                return true;
            }
            var factorySymbol = ResolveEntityName(factory, SymbolFlags.Value, true /*ignoreErrors*/, false /*dontResolveAlias*/, node);
            if (factorySymbol is null)
            {
                return true;
            }

            var factoryType = GetTypeOfSymbolWorker(factorySymbol);
            var callSignatures = GetSignaturesOfTypeWorker(factoryType, SignatureKind.Call) ?? [];
            if (callSignatures.Length == 0)
            {
                return true;
            }
            var hasFirstParamSignatures = false;
            var maxParamCount = 0;
            // Check that _some_ first parameter expects a FC-like thing, and that some overload of the SFC expects an acceptable number of arguments
            foreach (var sig in callSignatures)
            {
                var firstparam = GetTypeAtPositionWorker(sig, 0);
                var signaturesOfParam = GetSignaturesOfTypeWorker(firstparam, SignatureKind.Call) ?? [];
                if (signaturesOfParam.Length == 0)
                {
                    continue;
                }
                foreach (var paramSig in signaturesOfParam)
                {
                    hasFirstParamSignatures = true;
                    if (HasEffectiveRestParameterWorker(paramSig))
                    {
                        return true; // some signature has a rest param, so function components can have an arbitrary number of arguments
                    }
                    var paramCount = GetParameterCount(paramSig);
                    if (paramCount > maxParamCount)
                    {
                        maxParamCount = paramCount;
                    }
                }
            }
            if (!hasFirstParamSignatures)
            {
                // Not a single signature had a first parameter which expected a signature - for back compat, and
                // to guard against generic factories which won't have signatures directly, do not error
                return true;
            }
            var absoluteMinArgCount = int.MaxValue;
            foreach (var tagSig in tagCallSignatures)
            {
                var tagRequiredArgCount = GetMinArgumentCount(tagSig);
                if (tagRequiredArgCount < absoluteMinArgCount)
                {
                    absoluteMinArgCount = tagRequiredArgCount;
                }
            }
            if (absoluteMinArgCount <= maxParamCount)
            {
                return true; // some signature accepts the number of arguments the function component provides
            }
            if (reportErrors)
            {
                var tagName = node.TagName()!;
                // We will not report errors in this function for fragments, since we do not check them in this function
                var diag = NewDiagnosticForNode(tagName, Tag_0_expects_at_least_1_arguments_but_the_JSX_factory_2_provides_at_most_3, EntityNameToString(tagName), absoluteMinArgCount, EntityNameToString(factory), maxParamCount);
                var tagNameSymbol = GetSymbolAtLocationWorker(tagName, false);
                if (tagNameSymbol is not null && tagNameSymbol.ValueDeclaration is not null)
                {
                    diag.AddRelatedInfo(NewDiagnosticForNode(tagNameSymbol.ValueDeclaration, X_0_is_declared_here, EntityNameToString(tagName)));
                }
                ReportDiagnostic(diag, ref outputDiagnostics);
            }
            return false;
        }
        Type checkAttributesType;
        if ((checkMode & CheckMode.SkipContextSensitive) != 0)
        {
            checkAttributesType = GetRegularTypeOfObjectLiteral(attributesType);
        }
        else
        {
            checkAttributesType = attributesType;
        }
        if (!checkTagNameDoesNotExpectTooManyArguments())
        {
            diagnosticOutput = outputDiagnostics;
            return false;
        }
        Node? errorNode = null;
        if (reportErrors)
        {
            if (IsJsxOpeningFragment(node))
            {
                errorNode = node;
            }
            else
            {
                errorNode = node.TagName();
            }
        }
        Node? attributes = null;
        if (!IsJsxOpeningFragment(node))
        {
            attributes = node.Attributes();
        }
        var checkResult = CheckTypeRelatedToAndOptionallyElaborate(checkAttributesType, paramType, relation, errorNode, attributes, null, ref outputDiagnostics);
        diagnosticOutput = outputDiagnostics;
        return checkResult;
    }

    // Get attributes type of the JSX opening-like element. The result is from resolving "attributes" property of the opening-like element.
    //
    // @param openingLikeElement a JSX opening-like element
    // @param filter a function to remove attributes that will not participate in checking whether attributes are assignable
    // @return an anonymous type (similar to the one returned by checkObjectLiteral) in which its properties are attributes property.
    // @remarks Because this function calls getSpreadType, it needs to use the same checks as checkObjectLiteral,
    // which also calls getSpreadType.
    /// <summary>Go: <c>func (c *Checker) createJsxAttributesTypeFromAttributesProperty(openingLikeElement *ast.Node, checkMode CheckMode) *Type</c>（jsx.go:710-867）。</summary>
    internal Type CreateJsxAttributesTypeFromAttributesProperty(Node openingLikeElement, CheckMode checkMode)
    {
        SymbolTable? allAttributesTable = null;
        if (_strictNullChecks)
        {
            allAttributesTable = new SymbolTable();
        }
        var attributesTable = new SymbolTable();
        Symbol? attributesSymbol = null;
        var attributeParent = openingLikeElement;
        var spread = _emptyJsxObjectType;
        var hasSpreadAnyType = false;
        Type? typeToIntersect = null;
        var explicitlySpecifyChildrenAttribute = false;
        var objectFlags = ObjectFlags.JsxAttributes;
        Type createJsxAttributesType()
        {
            objectFlags |= ObjectFlags.FreshLiteral;
            var result = NewAnonymousType(attributesSymbol!, attributesTable, null, null, null);
            result._objectFlags |= objectFlags | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
            return result;
        }
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(openingLikeElement));
        var isJsxOpenFragment = IsJsxOpeningFragment(openingLikeElement);
        Type? contextualType = null;
        if (!isJsxOpenFragment)
        {
            var attributes = openingLikeElement.Attributes()!;
            attributesSymbol = attributes.Symbol;
            attributeParent = attributes;
            contextualType = GetContextualTypeWorker(attributes, ContextFlags.None);
            // Create anonymous type from given attributes symbol table.
            // @param symbol a symbol of JsxAttributes containing attributes corresponding to attributesTable
            // @param attributesTable a symbol table of attributes property
            foreach (var attributeDecl in attributes.Properties() ?? [])
            {
                var member = attributeDecl.Symbol!;
                if (IsJsxAttribute(attributeDecl))
                {
                    var exprType = CheckJsxAttribute(attributeDecl, checkMode);
                    objectFlags |= exprType._objectFlags & ObjectFlags.PropagatingFlags;
                    var attributeSymbol = NewSymbol(SymbolFlags.Property | member.Flags, member.Name);
                    attributeSymbol.Declarations = member.Declarations;
                    attributeSymbol.Parent = member.Parent;
                    if (member.ValueDeclaration is not null)
                    {
                        attributeSymbol.ValueDeclaration = member.ValueDeclaration;
                    }
                    var links = _valueSymbolLinks.Get(attributeSymbol);
                    links._resolvedType = exprType;
                    links._target = member;
                    attributesTable[attributeSymbol.Name] = attributeSymbol;
                    if (allAttributesTable is not null)
                    {
                        allAttributesTable[attributeSymbol.Name] = attributeSymbol;
                    }
                    if (attributeDecl.Name()!.Text() == jsxChildrenPropertyName)
                    {
                        explicitlySpecifyChildrenAttribute = true;
                    }
                    if (contextualType is not null && (checkMode & CheckMode.Inferential) != 0 && (checkMode & CheckMode.SkipContextSensitive) == 0 && IsContextSensitiveWorker(attributeDecl))
                    {
                        var inferenceContext = GetInferenceContext(attributes)!;
                        Debug.Assert(inferenceContext is not null);
                        // In CheckMode.Inferential we should always have an inference context
                        var inferenceNode = attributeDecl.Initializer()!.Expression()!;
                        AddIntraExpressionInferenceSite(inferenceContext!, inferenceNode, exprType);
                    }
                }
                else
                {
                    Debug.Assert(attributeDecl.Kind == Kind.JsxSpreadAttribute);
                    if (attributesTable.Count != 0)
                    {
                        spread = GetSpreadType(spread, createJsxAttributesType(), attributesSymbol!, objectFlags, false /*readonly*/);
                        attributesTable = new SymbolTable();
                    }
                    var exprType = GetReducedTypeWorker(CheckExpressionEx(attributeDecl.Expression()!, checkMode & CheckMode.Inferential));
                    if (IsTypeAny(exprType))
                    {
                        hasSpreadAnyType = true;
                    }
                    if (IsValidSpreadType(exprType))
                    {
                        spread = GetSpreadType(spread, exprType, attributesSymbol!, objectFlags, false /*readonly*/);
                        if (allAttributesTable is not null)
                        {
                            CheckSpreadPropOverrides(exprType, allAttributesTable, attributeDecl);
                        }
                    }
                    else
                    {
                        Error(attributeDecl.Expression(), Spread_types_may_only_be_created_from_object_types);
                        if (typeToIntersect is not null)
                        {
                            typeToIntersect = GetIntersectionType([typeToIntersect, exprType]);
                        }
                        else
                        {
                            typeToIntersect = exprType;
                        }
                    }
                }
            }
            if (!hasSpreadAnyType)
            {
                if (attributesTable.Count != 0)
                {
                    spread = GetSpreadType(spread, createJsxAttributesType(), attributesSymbol!, objectFlags, false /*readonly*/);
                }
            }
        }
        bool parentHasSemanticJsxChildren(Node element)
        {
            // Handle children attribute
            var parent = element.Parent;
            if (parent is null)
            {
                return false;
            }
            Node[] children = [];

            if (IsJsxElement(parent))
            {
                // We have to check that openingElement of the parent is the one we are visiting as this may not be true for selfClosingElement
                if (parent.AsJsxElement()!.OpeningElement == openingLikeElement)
                {
                    children = parent.Children()!.Nodes ?? [];
                }
            }
            else if (IsJsxFragment(parent))
            {
                if (parent.AsJsxFragment()!.OpeningFragment == openingLikeElement)
                {
                    children = parent.Children()!.Nodes ?? [];
                }
            }
            return GetSemanticJsxChildren(children).Length != 0;
        }
        if (parentHasSemanticJsxChildren(openingLikeElement))
        {
            var childTypes = CheckJsxChildren(openingLikeElement.Parent!, checkMode);
            if (!hasSpreadAnyType && jsxChildrenPropertyName != InternalSymbolNameMissing && jsxChildrenPropertyName != "")
            {
                // Error if there is a attribute named "children" explicitly specified and children element.
                // This is because children element will overwrite the value from attributes.
                // Note: we will not warn "children" attribute overwritten if "children" attribute is specified in object spread.
                if (explicitlySpecifyChildrenAttribute)
                {
                    Error(attributeParent, X_0_are_specified_twice_The_attribute_named_0_will_be_overwritten, jsxChildrenPropertyName);
                }
                Type? childrenContextualType = null;
                if (IsJsxOpeningElement(openingLikeElement))
                {
                    var contextualType2 = GetApparentTypeOfContextualType(openingLikeElement.Attributes()!, ContextFlags.None);
                    if (contextualType2 is not null)
                    {
                        childrenContextualType = GetTypeOfPropertyOfContextualTypeWorker(contextualType2, jsxChildrenPropertyName);
                    }
                }
                // If there are children in the body of JSX element, create dummy attribute "children" with the union of children types so that it will pass the attribute checking process
                var childrenPropSymbol = NewSymbol(SymbolFlags.Property, jsxChildrenPropertyName);
                var links = _valueSymbolLinks.Get(childrenPropSymbol);
                if (childTypes.Length == 1)
                {
                    links._resolvedType = childTypes[0];
                }
                else if (childrenContextualType is not null && SomeType(childrenContextualType, IsTupleLikeType))
                {
                    links._resolvedType = CreateTupleType(childTypes);
                }
                else
                {
                    links._resolvedType = CreateArrayType(GetUnionTypeWorker(childTypes));
                }
                // Fake up a property declaration for the children
                childrenPropSymbol.ValueDeclaration = _factory.NewPropertySignatureDeclaration(null, _factory.NewIdentifier(jsxChildrenPropertyName), null /*postfixToken*/, null /*type*/, null /*initializer*/);
                childrenPropSymbol.ValueDeclaration.Parent = attributeParent;
                childrenPropSymbol.ValueDeclaration.AsPropertySignatureDeclaration()!.Symbol = childrenPropSymbol;
                var childPropMap = new SymbolTable();
                childPropMap[jsxChildrenPropertyName] = childrenPropSymbol;
                spread = GetSpreadType(spread, NewAnonymousType(attributesSymbol!, childPropMap, null, null, null), attributesSymbol!, objectFlags | GetPropagatingFlagsOfTypes(childTypes, TypeFlags.None), false /*readonly*/);
            }
        }
        if (hasSpreadAnyType)
        {
            return _anyType;
        }
        if (typeToIntersect is not null)
        {
            if (spread != _emptyJsxObjectType)
            {
                return GetIntersectionType([typeToIntersect, spread]);
            }
            return typeToIntersect;
        }
        if (spread == _emptyJsxObjectType)
        {
            return createJsxAttributesType();
        }
        return spread;
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxAttribute(node *ast.Node, checkMode CheckMode) *Type</c>（jsx.go:869-875）。</summary>
    internal Type CheckJsxAttribute(Node node, CheckMode checkMode)
    {
        if (node.Initializer() is not null)
        {
            return CheckExpressionForMutableLocation(node.Initializer()!, checkMode);
        }
        // <Elem attr /> is sugar for <Elem attr={true} />
        return _trueType;
    }

    /// <summary>Go: <c>func (c *Checker) checkJsxChildren(node *ast.Node, checkMode CheckMode) []*Type</c>（jsx.go:877-894）。</summary>
    internal Type[] CheckJsxChildren(Node node, CheckMode checkMode)
    {
        var childTypes = new List<Type>();
        foreach (var child in node.Children()!.Nodes ?? [])
        {
            // In React, JSX text that contains only whitespaces will be ignored so we don't want to type-check that
            // because then type of children property will have constituent of string type.
            if (IsJsxText(child))
            {
                if (!child.AsJsxText()!.ContainsOnlyTriviaWhiteSpaces)
                {
                    childTypes.Add(_stringType);
                }
            }
            else if (IsJsxExpression(child) && child.Expression() is null)
            {
                // empty jsx expressions don't *really* count as present children
                continue;
            }
            else
            {
                childTypes.Add(CheckExpressionForMutableLocation(child, checkMode));
            }
        }
        return [.. childTypes];
    }

    /// <summary>Go: <c>func (c *Checker) getUninstantiatedJsxSignaturesOfType(elementType *Type, caller *ast.Node) []*Signature</c>（jsx.go:896-923）。</summary>
    internal Signature[] GetUninstantiatedJsxSignaturesOfType(Type elementType, Node caller)
    {
        if ((elementType._flags & TypeFlags.String) != 0)
        {
            return [_anySignature];
        }
        if ((elementType._flags & TypeFlags.StringLiteral) != 0)
        {
            var intrinsicType = GetIntrinsicAttributesTypeFromStringLiteralType(elementType, caller);
            if (intrinsicType is null)
            {
                Error(caller, Property_0_does_not_exist_on_type_1, GetStringLiteralValue(elementType), "JSX." + JsxNames.IntrinsicElements);
                return [];
            }
            var fakeSignature = CreateSignatureForJSXIntrinsic(caller, intrinsicType);
            return [fakeSignature];
        }
        var apparentElemType = GetApparentTypeWorker(elementType);
        // Resolve the signatures, preferring constructor
        var signatures = GetSignaturesOfTypeWorker(apparentElemType, SignatureKind.Construct) ?? [];
        if (signatures.Length == 0)
        {
            // No construct signatures, try call signatures
            signatures = GetSignaturesOfTypeWorker(apparentElemType, SignatureKind.Call) ?? [];
        }
        if (signatures.Length == 0 && (apparentElemType._flags & TypeFlags.Union) != 0)
        {
            // If each member has some combination of new/call signatures; make a union signature list for those
            signatures = GetUnionSignatures(Map(apparentElemType.Types(), t => GetUninstantiatedJsxSignaturesOfType(t, caller)));
        }
        return signatures ?? [];
    }

    /// <summary>Go: <c>func (c *Checker) getEffectiveFirstArgumentForJsxSignature(signature *Signature, node *ast.Node) *Type</c>（jsx.go:925-930）。</summary>
    internal Type GetEffectiveFirstArgumentForJsxSignature(Signature signature, Node node)
    {
        if (IsJsxOpeningFragment(node) || GetJsxReferenceKind(node) != JsxReferenceKind.Component)
        {
            return GetJsxPropsTypeFromCallSignature(signature, node);
        }
        return GetJsxPropsTypeFromClassType(signature, node);
    }

    /// <summary>Go: <c>func (c *Checker) getJsxPropsTypeFromCallSignature(sig *Signature, context *ast.Node) *Type</c>（jsx.go:932-940）。</summary>
    internal Type GetJsxPropsTypeFromCallSignature(Signature sig, Node context)
    {
        var propsType = GetTypeOfFirstParameterOfSignatureWithFallback(sig, _unknownType);
        propsType = GetJsxManagedAttributesFromLocatedAttributes(context, GetJsxNamespaceAt(context), propsType);
        var intrinsicAttribs = GetJsxType(JsxNames.IntrinsicAttributes, context);
        if (!IsErrorType(intrinsicAttribs))
        {
            propsType = IntersectTypes(intrinsicAttribs, propsType);
        }
        return propsType;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxPropsTypeFromClassType(sig *Signature, context *ast.Node) *Type</c>（jsx.go:942-987）。</summary>
    internal Type GetJsxPropsTypeFromClassType(Signature sig, Node context)
    {
        var ns = GetJsxNamespaceAt(context);
        var forcedLookupLocation = GetJsxElementPropertiesName(ns);
        Type? attributesType;
        switch (forcedLookupLocation)
        {
            case InternalSymbolNameMissing:
                attributesType = GetTypeOfFirstParameterOfSignatureWithFallback(sig, _unknownType);
                break;
            case "":
                attributesType = GetReturnTypeOfSignatureWorker(sig);
                break;
            default:
                attributesType = GetJsxPropsTypeForSignatureFromMember(sig, forcedLookupLocation);
                if (attributesType is null && (context.Attributes()?.Properties()?.Length ?? 0) != 0)
                {
                    // There is no property named 'props' on this instance type
                    Error(context, JSX_element_class_does_not_support_attributes_because_it_does_not_have_a_0_property, forcedLookupLocation);
                }
                break;
        }
        if (attributesType is null)
        {
            return _unknownType;
        }
        attributesType = GetJsxManagedAttributesFromLocatedAttributes(context, ns, attributesType);
        if (IsTypeAny(attributesType))
        {
            // Props is of type 'any' or unknown
            return attributesType;
        }
        // Normal case -- add in IntrinsicClassAttributes<T> and IntrinsicAttributes
        var apparentAttributesType = attributesType;
        var intrinsicClassAttribs = GetJsxType(JsxNames.IntrinsicClassAttributes, context);
        if (!IsErrorType(intrinsicClassAttribs))
        {
            var typeParams = GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(intrinsicClassAttribs._symbol!);
            var hostClassType = GetReturnTypeOfSignatureWorker(sig);
            Type libraryManagedAttributeType;
            if (typeParams is not null)
            {
                // apply JSX.IntrinsicClassAttributes<hostClassType, ...>
                var inferredArgs = FillMissingTypeArgumentsWorker([hostClassType], typeParams, GetMinTypeArgumentCountWorker(typeParams), IsInJSFile(context));
                libraryManagedAttributeType = InstantiateType(intrinsicClassAttribs, NewTypeMapper(typeParams, inferredArgs ?? []));
            }
            else
            {
                libraryManagedAttributeType = intrinsicClassAttribs;
            }
            apparentAttributesType = IntersectTypes(libraryManagedAttributeType, apparentAttributesType);
        }
        var intrinsicAttribs = GetJsxType(JsxNames.IntrinsicAttributes, context);
        if (!IsErrorType(intrinsicAttribs))
        {
            apparentAttributesType = IntersectTypes(intrinsicAttribs, apparentAttributesType);
        }
        return apparentAttributesType;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxPropsTypeForSignatureFromMember(sig *Signature, forcedLookupLocation string) *Type</c>（jsx.go:989-1016）。</summary>
    internal Type? GetJsxPropsTypeForSignatureFromMember(Signature sig, string forcedLookupLocation)
    {
        if (sig._composite is not null)
        {
            // JSX Elements using the legacy `props`-field based lookup (eg, react class components) need to treat the `props` member as an input
            // instead of an output position when resolving the signature. We need to go back to the input signatures of the composite signature,
            // get the type of `props` on each return type individually, and then _intersect them_, rather than union them (as would normally occur
            // for a union signature). It's an unfortunate quirk of looking in the output of the signature for the type we want to use for the input.
            // The default behavior of `getTypeOfFirstParameterOfSignatureWithFallback` when no `props` member name is defined is much more sane.
            var results = new List<Type>();
            foreach (var signature in sig._composite._signatures ?? [])
            {
                var instance = GetReturnTypeOfSignatureWorker(signature);
                if (IsTypeAny(instance))
                {
                    return instance;
                }
                var propType = GetTypeOfPropertyOfTypeWorker(instance, forcedLookupLocation);
                if (propType is null)
                {
                    return null;
                }
                results.Add(propType);
            }
            return GetIntersectionType([.. results]);
            // Same result for both union and intersection signatures
        }
        var instanceType = GetReturnTypeOfSignatureWorker(sig);
        if (IsTypeAny(instanceType))
        {
            return instanceType;
        }
        return GetTypeOfPropertyOfTypeWorker(instanceType, forcedLookupLocation);
    }

    /// <summary>Go: <c>func (c *Checker) getJsxManagedAttributesFromLocatedAttributes(context *ast.Node, ns *ast.Symbol, attributesType *Type) *Type</c>（jsx.go:1018-1028）。</summary>
    internal Type GetJsxManagedAttributesFromLocatedAttributes(Node context, Symbol? ns, Type attributesType)
    {
        var managedSym = GetJsxLibraryManagedAttributes(ns);
        if (managedSym is not null)
        {
            var ctorType = GetStaticTypeOfReferencedJsxConstructor(context);
            var result = InstantiateAliasOrInterfaceWithDefaults(managedSym, [ctorType, attributesType], IsInJSFile(context));
            if (result is not null)
            {
                return result;
            }
        }
        return attributesType;
    }

    /// <summary>Go: <c>func (c *Checker) instantiateAliasOrInterfaceWithDefaults(managedSym *ast.Symbol, typeArguments []*Type, inJavaScript bool) *Type</c>（jsx.go:1030-1048）。</summary>
    internal Type? InstantiateAliasOrInterfaceWithDefaults(Symbol managedSym, Type[] typeArguments, bool inJavaScript)
    {
        var declaredManagedType = GetDeclaredTypeOfSymbolWorker(managedSym);
        // fetches interface type, or initializes symbol links type parameters
        if ((managedSym.Flags & SymbolFlags.TypeAlias) != 0)
        {
            var parameters = _typeAliasLinks.Get(managedSym)._typeParameters;
            if ((parameters?.Length ?? 0) >= typeArguments.Length)
            {
                var args = FillMissingTypeArgumentsWorker(typeArguments, parameters, typeArguments.Length, inJavaScript) ?? [];
                if (args.Length == 0)
                {
                    return declaredManagedType;
                }
                return GetTypeAliasInstantiation(managedSym, args, null);
            }
        }
        if ((declaredManagedType._objectFlags & ObjectFlags.ClassOrInterface) != 0 && declaredManagedType.AsInterfaceType()!.TypeParameters().Length >= typeArguments.Length)
        {
            var args = FillMissingTypeArgumentsWorker(typeArguments, declaredManagedType.AsInterfaceType()!.TypeParameters(), typeArguments.Length, inJavaScript) ?? [];
            return CreateTypeReference(declaredManagedType, args);
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxLibraryManagedAttributes(jsxNamespace *ast.Symbol) *ast.Symbol</c>（jsx.go:1050-1055）。</summary>
    internal Symbol? GetJsxLibraryManagedAttributes(Symbol? jsxNamespace)
    {
        if (jsxNamespace is not null)
        {
            return GetSymbol(jsxNamespace.Exports!, JsxNames.LibraryManagedAttributes, SymbolFlags.Type);
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxElementTypeSymbol(jsxNamespace *ast.Symbol) *ast.Symbol</c>（jsx.go:1057-1063）。</summary>
    internal Symbol? GetJsxElementTypeSymbol(Symbol? jsxNamespace)
    {
        // JSX.ElementType [symbol]
        if (jsxNamespace is not null)
        {
            return GetSymbol(jsxNamespace.Exports!, JsxNames.ElementType, SymbolFlags.Type);
        }
        return null;
    }

    // e.g. "props" for React.d.ts,
    // or InternalSymbolNameMissing if ElementAttributesProperty doesn't exist (which means all
    // non-intrinsic elements' attributes type is 'any'),
    // or "" if it has 0 properties (which means every non-intrinsic elements' attributes type is the element instance type)
    /// <summary>Go: <c>func (c *Checker) getJsxElementPropertiesName(jsxNamespace *ast.Symbol) string</c>（jsx.go:1073-1075）。</summary>
    internal string GetJsxElementPropertiesName(Symbol? jsxNamespace)
    {
        return GetNameFromJsxElementAttributesContainer(JsxNames.ElementAttributesPropertyNameContainer, jsxNamespace);
    }

    /// <summary>Go: <c>func (c *Checker) getJsxElementChildrenPropertyName(jsxNamespace *ast.Symbol) string</c>（jsx.go:1077-1083）。</summary>
    internal string GetJsxElementChildrenPropertyName(Symbol? jsxNamespace)
    {
        if (_compilerOptions.Jsx == JsxEmit.ReactJSX || _compilerOptions.Jsx == JsxEmit.ReactJSXDev)
        {
            // In these JsxEmit modes the children property is fixed to 'children'
            return "children";
        }
        return GetNameFromJsxElementAttributesContainer(JsxNames.ElementChildrenAttributeNameContainer, jsxNamespace);
    }

    // Look into JSX namespace and then look for container with matching name as nameOfAttribPropContainer.
    // Get a single property from that container if existed. Report an error if there are more than one property.
    //
    // @param nameOfAttribPropContainer a string of value JsxNames.ElementAttributesPropertyNameContainer or JsxNames.ElementChildrenAttributeNameContainer
    //        if other string is given or the container doesn't exist, return undefined.
    /// <summary>Go: <c>func (c *Checker) getNameFromJsxElementAttributesContainer(nameOfAttribPropContainer string, jsxNamespace *ast.Symbol) string</c>（jsx.go:1091-1112）。</summary>
    internal string GetNameFromJsxElementAttributesContainer(string nameOfAttribPropContainer, Symbol? jsxNamespace)
    {
        // JSX.ElementAttributesProperty | JSX.ElementChildrenAttribute [symbol]
        if (jsxNamespace is not null)
        {
            var jsxElementAttribPropInterfaceSym = GetSymbol(jsxNamespace.Exports!, nameOfAttribPropContainer, SymbolFlags.Type);
            if (jsxElementAttribPropInterfaceSym is not null)
            {
                var jsxElementAttribPropInterfaceType = GetDeclaredTypeOfSymbolWorker(jsxElementAttribPropInterfaceSym);
                var propertiesOfJsxElementAttribPropInterface = GetPropertiesOfTypeWorker(jsxElementAttribPropInterfaceType) ?? [];
                // Element Attributes has zero properties, so the element attributes type will be the class instance type
                if (propertiesOfJsxElementAttribPropInterface.Length == 0)
                {
                    return "";
                }
                if (propertiesOfJsxElementAttribPropInterface.Length == 1)
                {
                    return propertiesOfJsxElementAttribPropInterface[0].Name;
                }
                if (propertiesOfJsxElementAttribPropInterface.Length > 1 && (jsxElementAttribPropInterfaceSym.Declarations?.Length ?? 0) != 0)
                {
                    // More than one property on ElementAttributesProperty is an error
                    Error(jsxElementAttribPropInterfaceSym.Declarations![0], The_global_type_JSX_0_may_not_have_more_than_one_property, nameOfAttribPropContainer);
                }
            }
        }
        return InternalSymbolNameMissing;
    }

    /// <summary>Go: <c>func (c *Checker) getStaticTypeOfReferencedJsxConstructor(context *ast.Node) *Type</c>（jsx.go:1114-1133）。</summary>
    internal Type GetStaticTypeOfReferencedJsxConstructor(Node context)
    {
        if (IsJsxOpeningFragment(context))
        {
            return GetJSXFragmentType(context);
        }
        if (IsJsxIntrinsicTagName(context.TagName()!))
        {
            var result = GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(context);
            var fakeSignature = CreateSignatureForJSXIntrinsic(context, result);
            return GetOrCreateTypeFromSignature(fakeSignature);
        }
        var tagType = CheckExpressionCached(context.TagName()!);
        if ((tagType._flags & TypeFlags.StringLiteral) != 0)
        {
            var result = GetIntrinsicAttributesTypeFromStringLiteralType(tagType, context);
            if (result is null)
            {
                return _errorType;
            }
            var fakeSignature = CreateSignatureForJSXIntrinsic(context, result);
            return GetOrCreateTypeFromSignature(fakeSignature);
        }
        return tagType;
    }

    /// <summary>Go: <c>func (c *Checker) getIntrinsicAttributesTypeFromStringLiteralType(t *Type, location *ast.Node) *Type</c>（jsx.go:1135-1155）。</summary>
    internal Type? GetIntrinsicAttributesTypeFromStringLiteralType(Type t, Node location)
    {
        // If the elemType is a stringLiteral type, we can then provide a check to make sure that the string literal type is one of the Jsx intrinsic element type
        // For example:
        //      var CustomTag: "h1" = "h1";
        //      <CustomTag> Hello World </CustomTag>
        var intrinsicElementsType = GetJsxType(JsxNames.IntrinsicElements, location);
        if (!IsErrorType(intrinsicElementsType))
        {
            var stringLiteralTypeName = GetStringLiteralValue(t);
            var intrinsicProp = GetPropertyOfTypeWorker(intrinsicElementsType, stringLiteralTypeName);
            if (intrinsicProp is not null)
            {
                return GetTypeOfSymbolWorker(intrinsicProp);
            }
            var indexSignatureType = GetIndexTypeOfTypeWorker(intrinsicElementsType, _stringType);
            if (indexSignatureType is not null)
            {
                return indexSignatureType;
            }
            return null;
        }
        // If we need to report an error, we already done so here. So just return any to prevent any more error downstream
        return _anyType;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxReferenceKind(node *ast.Node) JsxReferenceKind</c>（jsx.go:1157-1169）。</summary>
    internal JsxReferenceKind GetJsxReferenceKind(Node node)
    {
        if (IsJsxIntrinsicTagName(node.TagName()!))
        {
            return JsxReferenceKind.Mixed;
        }
        var tagType = GetApparentTypeWorker(CheckExpression(node.TagName()!));
        if ((GetSignaturesOfTypeWorker(tagType, SignatureKind.Construct) ?? []).Length != 0)
        {
            return JsxReferenceKind.Component;
        }
        if ((GetSignaturesOfTypeWorker(tagType, SignatureKind.Call) ?? []).Length != 0)
        {
            return JsxReferenceKind.Function;
        }
        return JsxReferenceKind.Mixed;
    }

    /// <summary>Go: <c>func (c *Checker) createSignatureForJSXIntrinsic(node *ast.Node, result *Type) *Signature</c>（jsx.go:1171-1186）。</summary>
    internal Signature CreateSignatureForJSXIntrinsic(Node node, Type result)
    {
        var elementType = _errorType;
        var ns = GetJsxNamespaceAt(node);
        if (ns is not null)
        {
            var typeSymbol = GetSymbol(GetExportsOfSymbol(ns), JsxNames.Element, SymbolFlags.Type);
            if (typeSymbol is not null)
            {
                elementType = GetDeclaredTypeOfSymbolWorker(typeSymbol);
            }
        }
        // returnNode := typeSymbol && c.nodeBuilder.symbolToEntityName(typeSymbol, ast.SymbolFlagsType, node)
        // declaration := factory.createFunctionTypeNode(nil, []ParameterDeclaration{factory.createParameterDeclaration(nil, nil /*dotDotDotToken*/, "props", nil /*questionToken*/, c.nodeBuilder.typeToTypeNode(result, node))}, ifElse(returnNode != nil, factory.createTypeReferenceNode(returnNode, nil /*typeArguments*/), factory.createKeywordTypeNode(ast.KindAnyKeyword)))
        var parameterSymbol = NewSymbol(SymbolFlags.FunctionScopedVariable, "props");
        _valueSymbolLinks.Get(parameterSymbol)._resolvedType = result;
        return NewSignature(SignatureFlags.None, null!, [], null, [parameterSymbol], elementType, null, 1);
    }

    // Get attributes type of the given intrinsic opening-like Jsx element by resolving the tag name.
    // The function is intended to be called from a function which has checked that the opening element is an intrinsic element.
    // @param node an intrinsic JSX opening-like element
    /// <summary>Go: <c>func (c *Checker) getIntrinsicAttributesTypeFromJsxOpeningLikeElement(node *ast.Node) *Type</c>（jsx.go:1188-1208）。</summary>
    internal Type GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(Node node)
    {
        Debug.Assert(IsJsxIntrinsicTagName(node.TagName()!));
        var links = _jsxElementLinks.Get(node);
        if (links._resolvedJsxElementAttributesType is not null)
        {
            return links._resolvedJsxElementAttributesType!;
        }
        var symbol = GetIntrinsicTagSymbol(node);
        if ((links._jsxFlags & JsxFlags.IntrinsicNamedElement) != 0)
        {
            links._resolvedJsxElementAttributesType = OrElse(GetTypeOfSymbolWorker(symbol), _errorType);
            return links._resolvedJsxElementAttributesType!;
        }
        if ((links._jsxFlags & JsxFlags.IntrinsicIndexedElement) != 0)
        {
            var indexInfo = GetApplicableIndexInfoForName(GetJsxType(JsxNames.IntrinsicElements, node), node.TagName()!.Text());
            if (indexInfo is not null)
            {
                links._resolvedJsxElementAttributesType = indexInfo._valueType;
                return links._resolvedJsxElementAttributesType!;
            }
        }
        links._resolvedJsxElementAttributesType = _errorType;
        return links._resolvedJsxElementAttributesType;
    }

    // Looks up an intrinsic tag name and returns a symbol that either points to an intrinsic
    // property (in which case nodeLinks.jsxFlags will be IntrinsicNamedElement) or an intrinsic
    // string index signature (in which case nodeLinks.jsxFlags will be IntrinsicIndexedElement).
    // May also return unknownSymbol if both of these lookups fail.
    /// <summary>Go: <c>func (c *Checker) getIntrinsicTagSymbol(node *ast.Node) *ast.Symbol</c>（jsx.go:1214-1255）。</summary>
    internal Symbol GetIntrinsicTagSymbol(Node node)
    {
        var links = _symbolNodeLinks.Get(node);
        if (links._resolvedSymbol is not null)
        {
            return links._resolvedSymbol;
        }
        var intrinsicElementsType = GetJsxType(JsxNames.IntrinsicElements, node);
        if (!IsErrorType(intrinsicElementsType))
        {
            // Property case
            var tagName = node.TagName()!;
            if (!IsIdentifier(tagName) && !IsJsxNamespacedName(tagName))
            {
                Debug.Fail("Invalid tag name");
                throw new System.Diagnostics.UnreachableException();
            }
            var propName = tagName.Text();
            var intrinsicProp = GetPropertyOfTypeWorker(intrinsicElementsType, propName);
            if (intrinsicProp is not null)
            {
                _jsxElementLinks.Get(node)._jsxFlags |= JsxFlags.IntrinsicNamedElement;
                links._resolvedSymbol = intrinsicProp;
                return links._resolvedSymbol!;
            }
            // Intrinsic string indexer case
            var indexSymbol = GetApplicableIndexSymbol(intrinsicElementsType, GetStringLiteralType(propName));
            if (indexSymbol is not null)
            {
                _jsxElementLinks.Get(node)._jsxFlags |= JsxFlags.IntrinsicIndexedElement;
                links._resolvedSymbol = indexSymbol;
                return links._resolvedSymbol!;
            }
            if (GetTypeOfPropertyOrIndexSignatureOfType(intrinsicElementsType, propName) is not null)
            {
                _jsxElementLinks.Get(node)._jsxFlags |= JsxFlags.IntrinsicIndexedElement;
                links._resolvedSymbol = intrinsicElementsType._symbol;
                return links._resolvedSymbol!;
            }
            // Wasn't found
            Error(node, Property_0_does_not_exist_on_type_1, tagName.Text(), "JSX." + JsxNames.IntrinsicElements);
            links._resolvedSymbol = _unknownSymbol;
            return links._resolvedSymbol;
        }
        if (_noImplicitAny)
        {
            Error(node, JSX_element_implicitly_has_type_any_because_no_interface_JSX_0_exists, JsxNames.IntrinsicElements);
        }
        links._resolvedSymbol = _unknownSymbol;
        return links._resolvedSymbol;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxStatelessElementTypeAt(location *ast.Node) *Type</c>（jsx.go:1257-1263）。</summary>
    internal Type? GetJsxStatelessElementTypeAt(Node location)
    {
        var jsxElementType = GetJsxElementTypeAt(location);
        if (jsxElementType is null)
        {
            return null;
        }
        return GetUnionTypeWorker([jsxElementType, _nullType]);
    }

    /// <summary>Go: <c>func (c *Checker) getJsxElementClassTypeAt(location *ast.Node) *Type</c>（jsx.go:1265-1271）。</summary>
    internal Type? GetJsxElementClassTypeAt(Node location)
    {
        var t = GetJsxType(JsxNames.ElementClass, location);
        if (IsErrorType(t))
        {
            return null;
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxElementTypeAt(location *ast.Node) *Type</c>（jsx.go:1273-1275）。</summary>
    internal Type GetJsxElementTypeAt(Node location)
    {
        return GetJsxType(JsxNames.Element, location);
    }

    /// <summary>Go: <c>func (c *Checker) getJsxElementTypeTypeAt(location *ast.Node) *Type</c>（jsx.go:1277-1291）。</summary>
    internal Type? GetJsxElementTypeTypeAt(Node location)
    {
        var ns = GetJsxNamespaceAt(location);
        if (ns is null)
        {
            return null;
        }
        var sym = GetJsxElementTypeSymbol(ns);
        if (sym is null)
        {
            return null;
        }
        var t = InstantiateAliasOrInterfaceWithDefaults(sym, [], IsInJSFile(location));
        if (t is null || IsErrorType(t))
        {
            return null;
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxType(name string, location *ast.Node) *Type</c>（jsx.go:1293-1302）。</summary>
    internal Type GetJsxType(string name, Node? location)
    {
        var ns = GetJsxNamespaceAt(location);
        if (ns is not null)
        {
            var exports = GetExportsOfSymbol(ns);
            if (exports is not null)
            {
                var typeSymbol = GetSymbol(exports, name, SymbolFlags.Type);
                if (typeSymbol is not null)
                {
                    return GetDeclaredTypeOfSymbolWorker(typeSymbol);
                }
            }
        }
        return _errorType;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxNamespaceAt(location *ast.Node) *ast.Symbol</c>（jsx.go:1304-1337）。</summary>
    internal Symbol? GetJsxNamespaceAt(Node? location)
    {
        JsxElementLinks? links = null;
        if (location is not null)
        {
            links = _jsxElementLinks.Get(location);
        }
        if (links is not null && links._jsxNamespace is not null && links._jsxNamespace != _unknownSymbol)
        {
            return links._jsxNamespace;
        }
        if (links is null || links._jsxNamespace != _unknownSymbol)
        {
            var resolvedNamespace = GetJsxNamespaceContainerForImplicitImport(location);
            if (resolvedNamespace is null || resolvedNamespace == _unknownSymbol)
            {
                var namespaceName = GetJsxNamespaceWorker(location);
                resolvedNamespace = _resolveName(location, namespaceName, SymbolFlags.Namespace, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/);
            }
            if (resolvedNamespace is not null)
            {
                var candidate = ResolveSymbol(GetSymbol(GetExportsOfSymbol(ResolveSymbol(resolvedNamespace)!), JsxNames.JSX, SymbolFlags.Namespace));
                if (candidate is not null && candidate != _unknownSymbol)
                {
                    if (links is not null)
                    {
                        links._jsxNamespace = candidate;
                    }
                    return candidate;
                }
            }
            if (links is not null)
            {
                links._jsxNamespace = _unknownSymbol;
            }
        }
        // JSX global fallback
        var s = ResolveSymbol(GetGlobalSymbolWorker(JsxNames.JSX, SymbolFlags.Namespace, null /*diagnostic*/));
        if (s == _unknownSymbol)
        {
            return null;
        }
        return s;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxNamespace(location *ast.Node) string</c>（jsx.go:1339-1386）→ `...Worker`
    /// （同名导出者 `GetJsxNamespace` 在 Exports.cs，artifacts/checker-method-collisions.md）。</summary>
    internal string GetJsxNamespaceWorker(Node? location)
    {
        if (location is not null)
        {
            var file = GetSourceFileOfNode(location);
            if (file is not null)
            {
                var links = _sourceFileLinks.Get(file);
                if (IsJsxOpeningFragment(location))
                {
                    if (links._localJsxFragmentNamespace != "")
                    {
                        return links._localJsxFragmentNamespace;
                    }
                    var jsxFragmentPragma = GetPragmaFromSourceFile(file, "jsxfrag");
                    if (jsxFragmentPragma is not null)
                    {
                        links._localJsxFragmentFactory = ParseIsolatedEntityName(jsxFragmentPragma.Args?.GetValueOrDefault("factory")?.Value ?? "");
                        if (links._localJsxFragmentFactory is not null)
                        {
                            links._localJsxFragmentNamespace = GetFirstIdentifier(links._localJsxFragmentFactory)!.Text();
                            return links._localJsxFragmentNamespace;
                        }
                    }
                    var entity = GetJsxFragmentFactoryEntity(location);
                    if (entity is not null)
                    {
                        links._localJsxFragmentFactory = entity;
                        links._localJsxFragmentNamespace = GetFirstIdentifier(entity)!.Text();
                        return links._localJsxFragmentNamespace;
                    }
                }
                else
                {
                    var localJsxNamespace = GetLocalJsxNamespace(file);
                    if (localJsxNamespace != "")
                    {
                        links._localJsxNamespace = localJsxNamespace;
                        return links._localJsxNamespace;
                    }
                }
            }
        }
        if (_jsxNamespace == "")
        {
            _jsxNamespace = "React";
            if (_compilerOptions.JsxFactory != "")
            {
                _jsxFactoryEntity = ParseIsolatedEntityName(_compilerOptions.JsxFactory);
                if (_jsxFactoryEntity is not null)
                {
                    _jsxNamespace = GetFirstIdentifier(_jsxFactoryEntity)!.Text();
                }
            }
            else if (_compilerOptions.ReactNamespace != "")
            {
                _jsxNamespace = _compilerOptions.ReactNamespace;
            }
        }
        if (_jsxFactoryEntity is null)
        {
            _jsxFactoryEntity = _factory.NewQualifiedName(_factory.NewIdentifier(_jsxNamespace), _factory.NewIdentifier("createElement"));
        }
        return _jsxNamespace;
    }

    /// <summary>Go: <c>func (c *Checker) getLocalJsxNamespace(file *ast.SourceFile) string</c>（jsx.go:1388-1402）。</summary>
    internal string GetLocalJsxNamespace(SourceFile file)
    {
        var links = _sourceFileLinks.Get(file);
        if (links._localJsxNamespace != "")
        {
            return links._localJsxNamespace;
        }
        var jsxPragma = GetPragmaFromSourceFile(file, "jsx");
        if (jsxPragma is not null)
        {
            links._localJsxFactory = ParseIsolatedEntityName(jsxPragma.Args?.GetValueOrDefault("factory")?.Value ?? "");
            if (links._localJsxFactory is not null)
            {
                links._localJsxNamespace = GetFirstIdentifier(links._localJsxFactory)!.Text();
                return links._localJsxNamespace;
            }
        }
        return "";
    }

    /// <summary>Go: <c>func (c *Checker) getJsxFactoryEntity(location *ast.Node) *ast.Node</c>（jsx.go:1404-1412）。</summary>
    internal Node? GetJsxFactoryEntity(Node? location)
    {
        if (location is not null)
        {
            GetJsxNamespaceWorker(location);
            var localJsxFactory = _sourceFileLinks.Get(GetSourceFileOfNode(location)!)._localJsxFactory;
            if (localJsxFactory is not null)
            {
                return localJsxFactory;
            }
        }
        return _jsxFactoryEntity;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxFragmentFactoryEntity(location *ast.Node) *ast.EntityName</c>（jsx.go:1414-1433）
    /// （Go 的 `*ast.EntityName` = C# 的 `Node?`）。</summary>
    internal Node? GetJsxFragmentFactoryEntity(Node? location)
    {
        if (location is not null)
        {
            var file = GetSourceFileOfNode(location);
            if (file is not null)
            {
                var links = _sourceFileLinks.Get(file);
                if (links._localJsxFragmentFactory is not null)
                {
                    return links._localJsxFragmentFactory;
                }
                var jsxFragPragma = GetPragmaFromSourceFile(file, "jsxfrag");
                if (jsxFragPragma is not null)
                {
                    links._localJsxFragmentFactory = ParseIsolatedEntityName(jsxFragPragma.Args?.GetValueOrDefault("factory")?.Value ?? "");
                    return links._localJsxFragmentFactory;
                }
            }
        }
        if (_compilerOptions.JsxFragmentFactory != "")
        {
            return ParseIsolatedEntityName(_compilerOptions.JsxFragmentFactory);
        }
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) parseIsolatedEntityName(name string) *ast.Node</c>（jsx.go:1435-1441）。</summary>
    internal Node? ParseIsolatedEntityName(string name)
    {
        var result = TypeScript.Parser.Parser.ParseIsolatedEntityName(name);
        if (result is not null)
        {
            MarkAsSynthetic(result);
        }
        return result;
    }

    /// <summary>Go: <c>func markAsSynthetic(node *ast.Node) bool</c>（jsx.go:1443-1447）→ 同一个 partial class 的 static 成员。</summary>
    private static bool MarkAsSynthetic(Node node)
    {
        node.Loc = NewTextRange(-1, -1);
        node.ForEachChild(MarkAsSynthetic);
        return false;
    }

    /// <summary>Go: <c>func (c *Checker) getJsxNamespaceContainerForImplicitImport(location *ast.Node) *ast.Symbol</c>（jsx.go:1449-1484）。</summary>
    internal Symbol? GetJsxNamespaceContainerForImplicitImport(Node? location)
    {
        var file = GetSourceFileOfNode(location)!;
        var links = _jsxElementLinks.Get(file);
        if (links._jsxImplicitImportContainer is not null)
        {
            return IfElse(links._jsxImplicitImportContainer == _unknownSymbol, null, links._jsxImplicitImportContainer);
        }
        var canonicalErrorTag = links._firstJSXTagInFile;
        if (canonicalErrorTag is null)
        {
            bool Visit(Node node)
            {
                if (IsJsxElement(node) || IsJsxSelfClosingElement(node))
                {
                    links._firstJSXTagInFile = node;
                    return true;
                }
                if (IsJsxFragment(node))
                {
                    links._firstJSXTagInFile = node.AsJsxFragment()!.OpeningFragment; // to match strada, fragments issue errors on the opening fragment instead of the whole tag
                    return true;
                }
                return node.ForEachChild(Visit);
            }
            file.ForEachChild(Visit);
            canonicalErrorTag = links._firstJSXTagInFile;
        }
        var (moduleReference, specifier) = GetJSXRuntimeImportSpecifier(file);
        if (moduleReference == "")
        {
            return null;
        }
        var errorMessage = This_JSX_tag_requires_the_module_path_0_to_exist_but_none_could_be_found_Make_sure_you_have_types_for_the_appropriate_package_installed;
        var mod = ResolveExternalModule(OrElse(specifier, canonicalErrorTag)!, moduleReference, errorMessage, canonicalErrorTag, false, null /*importAttributesType*/);
        Symbol? result = null;
        if (mod is not null && mod != _unknownSymbol)
        {
            result = GetMergedSymbolWorker(ResolveSymbol(mod)!);
        }
        links._jsxImplicitImportContainer = OrElse(result, _unknownSymbol);
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) getJSXRuntimeImportSpecifier(file *ast.SourceFile) (moduleReference string, specifier *ast.Node)</c>（jsx.go:1486-1488）。</summary>
    private (string moduleReference, Node? specifier) GetJSXRuntimeImportSpecifier(SourceFile file)
    {
        return _program.GetJSXRuntimeImportSpecifier(file.Path());
    }
}
           