// ported from: internal/checker/nodebuilderimpl.go:349-583（类型节点列表、复用既有类型节点的判定、抽象构造签名过滤）
//
// 与 Go 的差异（逐条）：
// 1) Go 的函数内局部类型 `seenName` → `NodeBuilderImpl` 的嵌套 `private readonly struct SeenName`（C# 不允许方法内声明类型）。
// 2) `[]*ast.Node` 的占位 nil 槽位 → C# `Node[]` + `null!`（Go 允许 nil 元素；`NodeList.Nodes` 也是 `Node[]?`）。
// 3) `multiMap.Values()` 是 `IEnumerable<List<V>>` → 传给 `ArrayIsHomogeneous(IReadOnlyList<T>, ...)`。
// 4) Go 未导出字段 → `_camelCase`；同名方法冲突见 artifacts/checker-method-collisions.md
//    （`getDeclaredTypeOfSymbol`→`GetDeclaredTypeOfSymbolWorker`、`getMinTypeArgumentCount`→`GetMinTypeArgumentCountWorker`、
//     `resolveAlias`→`ResolveAliasWorker`、`getTypeFromTypeNode`→`GetTypeFromTypeNodeWorker`）。
// 5) `t.AsType()`（Go 里返回自身）→ C# 直接用 `t`；`t.objectTypeWithoutAbstractConstructSignatures` → `t._objectTypeWithoutAbstractConstructSignatures`。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;

public sealed partial class NodeBuilderImpl
{
    private readonly struct SeenName
    {
        internal readonly Type _t;
        internal readonly int _i;

        internal SeenName(Type t, int i)
        {
            _t = t;
            _i = i;
        }
    }

    internal NodeList? MapToTypeNodes(Type[] list, bool isBareList)
    {
        if (list.Length == 0)
        {
            return null;
        }

        if (CheckTruncationLength())
        {
            if (!isBareList)
            {
                Node node;
                if ((_ctx!._flags & NodeBuilderFlags.NoTruncation) != 0)
                {
                    node = _e.AddSyntheticLeadingComment(_f.NewKeywordTypeNode(Kind.AnyKeyword), Kind.MultiLineCommentTrivia, "elided", false /*hasTrailingNewLine*/);
                }
                else
                {
                    node = _f.NewTypeReferenceNode(_f.NewIdentifier("..."), null /*typeArguments*/);
                }

                return _f.NewNodeList(new[] { node });
            }
            else if (list.Length > 2)
            {
                var nodes = new Node[]
                {
                    TypeToTypeNode(list[0])!,
                    null!,
                    TypeToTypeNode(list[list.Length - 1])!,
                };

                if ((_ctx!._flags & NodeBuilderFlags.NoTruncation) != 0)
                {
                    nodes[1] = _e.AddSyntheticLeadingComment(_f.NewKeywordTypeNode(Kind.AnyKeyword), Kind.MultiLineCommentTrivia, $"... {list.Length - 2} more elided ...", false /*hasTrailingNewLine*/);
                }
                else
                {
                    var text = $"... {list.Length - 2} more ...";
                    nodes[1] = _f.NewTypeReferenceNode(_f.NewIdentifier(text), null /*typeArguments*/);
                }

                return _f.NewNodeList(nodes);
            }
        }

        var mayHaveNameCollisions = (_ctx!._flags & NodeBuilderFlags.UseFullyQualifiedType) == 0;
        MultiMap<string, SeenName>? seenNames = null;
        if (mayHaveNameCollisions)
        {
            seenNames = new MultiMap<string, SeenName>();
        }

        var result = new List<Node>(list.Length);

        for (var i = 0; i < list.Length; i++)
        {
            var t = list[i];
            var displayIndex = i + 1;
            if (CheckTruncationLength() && (displayIndex + 2 < list.Length - 1))
            {
                if ((_ctx!._flags & NodeBuilderFlags.NoTruncation) != 0)
                {
                    result.Add(_e.AddSyntheticLeadingComment(_f.NewKeywordTypeNode(Kind.AnyKeyword), Kind.MultiLineCommentTrivia, $"... {list.Length - displayIndex} more elided ...", false /*hasTrailingNewLine*/));
                }
                else
                {
                    var text = $"... {list.Length - displayIndex} more ...";
                    result.Add(_f.NewTypeReferenceNode(_f.NewIdentifier(text), null /*typeArguments*/));
                }

                var typeNode = TypeToTypeNode(list[list.Length - 1]);
                if (typeNode is not null)
                {
                    result.Add(typeNode);
                }

                break;
            }

            _ctx!._approximateLength += 2; // Account for whitespace + separator
            var node2 = TypeToTypeNode(t);
            if (node2 is not null)
            {
                result.Add(node2);
                if (seenNames is not null && IsIdentifierTypeReference(node2))
                {
                    seenNames.Add(node2.AsTypeReferenceNode()!.TypeName!.Text(), new SeenName(t, result.Count - 1));
                }
            }
        }

        if (seenNames is not null)
        {
            // To avoid printing types like `[Foo, Foo]` or `Bar & Bar` where
            // occurrences of the same name actually come from different
            // namespaces, go through the single-identifier type reference nodes
            // we just generated, and see if any names were generated more than
            // once while referring to different types. If so, regenerate the
            // type node for each entry by that name with the
            // `UseFullyQualifiedType` flag enabled.
            var restoreFlags = SaveRestoreFlags();
            _ctx!._flags |= NodeBuilderFlags.UseFullyQualifiedType;
            foreach (var types in seenNames.Values())
            {
                if (!ArrayIsHomogeneous(types, (a, b) => TypesAreSameReference(a._t, b._t)))
                {
                    foreach (var seen in types)
                    {
                        result[seen._i] = TypeToTypeNode(seen._t)!;
                    }
                }
            }

            restoreFlags();
        }

        return _f.NewNodeList(result.ToArray());
    }

    internal Node? SerializeTypeName(Node node, bool isTypeOf, NodeList? typeArguments)
    {
        var meaning = SymbolFlags.Type;
        if (isTypeOf)
        {
            meaning = SymbolFlags.Value;
        }

        var symbol = _ch.ResolveEntityName(node, meaning, true, false, node);
        if (symbol is null)
        {
            return null;
        }

        var resolvedSymbol = symbol;
        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            resolvedSymbol = _ch.ResolveAliasWorker(symbol);
        }

        if (_ch.IsSymbolAccessible(symbol, _ctx!._enclosingDeclaration, meaning, false).Accessibility != SymbolAccessibility.Accessible)
        {
            return null;
        }

        return SymbolToTypeNode(resolvedSymbol, meaning, typeArguments);
    }

    internal void SetCommentRange(Node node, Node? range_)
    {
        if (range_ is not null && _ctx!._enclosingFile is not null && _ctx!._enclosingFile == GetSourceFileOfNode(range_))
        {
            // Copy comments to node for declaration emit
            _e.AssignCommentRange(node, range_);
        }
    }

    internal bool TypeNodeIsEquivalentToType(Node? annotatedDeclaration, Type t, Type? typeFromTypeNode)
    {
        if (typeFromTypeNode == t)
        {
            return true;
        }

        if (annotatedDeclaration is null)
        {
            return false;
        }

        // !!!
        // used to be hasEffectiveQuestionToken for JSDoc
        if (IsOptionalDeclaration(annotatedDeclaration))
        {
            return _ch.GetTypeWithFacts(t, TypeFacts.NEUndefined) == typeFromTypeNode;
        }

        return false;
    }

    internal bool CanReuseExistingJSTypeNode(Node? existing, Type? t)
    {
        return _ch.GetIntendedTypeFromJSDocTypeReference(existing!) is null && ExistingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount(existing!, t!);
    }

    internal Symbol? TryGetResolvedSymbolFromTypeNode(Node? node)
    {
        if (node is null || node.Parent is null)
        {
            return null;
        }

        _ch.GetTypeFromTypeNodeWorker(node);
        // call to ensure symbol is resolved
        var links = _ch._symbolNodeLinks.TryGet(node);
        if (links is null)
        {
            return null;
        }

        return links._resolvedSymbol;
    }

    internal bool ExistingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount(Node existing, Type t)
    {
        // In JS, you can say something like `Foo` and get a `Foo<any>` implicitly - we don't want to preserve that original `Foo` in these cases, though.
        if ((t._objectFlags & ObjectFlags.Reference) == 0)
        {
            return true;
        }

        if (!IsTypeReferenceNode(existing))
        {
            return true;
        }

        var symbol = TryGetResolvedSymbolFromTypeNode(existing);
        if (symbol is null)
        {
            return true;
        }

        // `type` is a reference type, and `existing` is a type reference node, but we still need to make sure they refer to the _same_ target type
        // before we go comparing their type argument counts.

        var existingTarget = _ch.GetDeclaredTypeOfSymbolWorker(symbol);
        if (existingTarget is null || existingTarget != t.AsTypeReference()!._target)
        {
            return true;
        }

        return (existing.TypeArguments()?.Length ?? 0) >= _ch.GetMinTypeArgumentCountWorker(t.AsTypeReference()!._target!.AsInterfaceType()!.TypeParameters());
    }

    internal Node? TryReuseExistingNonParameterTypeNode(Node existing, Type t, Node? host, Type? annotationType)
    {
        host ??= _ctx!._enclosingDeclaration;
        annotationType ??= GetTypeFromTypeNode(existing, true);
        if (annotationType is not null && TypeNodeIsEquivalentToType(host, t, annotationType) && CanReuseExistingJSTypeNode(existing, t))
        {
            var result = TryReuseExistingNodeHelper(existing);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    internal Type GetResolvedTypeWithoutAbstractConstructSignatures(StructuredType t)
    {
        if (t.ConstructSignatures().Length == 0)
        {
            return t;
        }

        if (t._objectTypeWithoutAbstractConstructSignatures is not null)
        {
            return t._objectTypeWithoutAbstractConstructSignatures;
        }

        var constructSignatures = Filter(t.ConstructSignatures(), signature => (signature._flags & SignatureFlags.Abstract) == 0);
        if (constructSignatures.Length == t.ConstructSignatures().Length)
        {
            t._objectTypeWithoutAbstractConstructSignatures = t;
            return t;
        }

        var typeCopy = _ch.NewAnonymousType(t._symbol, t._members, t.CallSignatures(), constructSignatures.Length > 0 ? constructSignatures : [], t._indexInfos);
        t._objectTypeWithoutAbstractConstructSignatures = typeCopy;
        typeCopy.AsStructuredType()!._objectTypeWithoutAbstractConstructSignatures = typeCopy;
        return typeCopy;
    }
}
