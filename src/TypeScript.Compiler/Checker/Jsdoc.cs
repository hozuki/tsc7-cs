// ported from: internal/checker/jsdoc.go:1-100
//
// 与 Go 的偏离（逐条；其余地方与 Go 逐行对应）：
// 1) 包级函数 `getAllJSDocTags`（Go 的 `func X(...)`）→ 同一个 `public sealed partial class Checker` 里的
//    `private static` 成员（docs/checker-plan.md §5.5：C# 没有自由函数，`public static partial class Checker`
//    与实例类同名会 CS0708；先例 `Parser`/`Binder`）。本文件内只有 `checkUnmatchedJSDocParameters` 调用它。
// 2) `collections.Set[T]` → `TypeScript.Collections.Set<T>`（`Add`/`Has` 与 Go 同名）。
// 3) Go map 读缺失键：本文件不涉及。
// 4) 诊断消息常量按 `using static TypeScript.Diagnostics.Diagnostics;` 后的**裸名**引用；写
//    `Diagnostics.JSDoc_param_tag_has_name_0_...` 会先解析到同级命名空间 `TypeScript.Diagnostics`
//    （实测 CS0234，见 Binder/Binder.Declarations.cs 文件头）。
// 5) Go 的 `ast.IsIdentifier`/`ast.IsQualifiedName` 对 nil 是安全的（Go 的 `node != nil && node.Kind == ...`，
//    见 jsdoc.go:50/63 的 `lastJSDocParam == nil || !ast.IsIdentifier(...)` 用法），C# 侧的
//    `Ast.IsIdentifier(Node)` 形参非空 → 逐处补 `name is not null &&`，语义与 Go 一致（nil 走 else 分支）。
// 6) Go 未导出字段/方法 → `_camelCase`/PascalCase（docs/checker-plan.md §5.5）；
//    Worker 改名：`getTypeFromTypeNode` → `GetTypeFromTypeNodeWorker`、`isArrayType` → `IsArrayTypeWorker`
//    （artifacts/checker-method-collisions.md）。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using static TypeScript.Ast.Ast;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) checkUnmatchedJSDocParameters(node *ast.Node)</c>（jsdoc.go:9-84）。</summary>
    internal void CheckUnmatchedJSDocParameters(Node node)
    {
        var jsdocParameters = new List<Node>();
        foreach (var tag in GetAllJSDocTags(node))
        {
            if (tag.Kind == Kind.JSDocParameterTag)
            {
                var name = tag.AsJSDocParameterOrPropertyTag()!.Name();
                if (name is not null && IsIdentifier(name) && name.Text().Length == 0)
                {
                    continue;
                }
                jsdocParameters.Add(tag);
            }
        }

        if (jsdocParameters.Count == 0)
        {
            return;
        }

        var isJs = IsInJSFile(node);
        var parameters = new Set<string>();
        var excludedParameters = new Set<int>();

        var nodeParameters = node.Parameters() ?? [];
        for (var i = 0; i < nodeParameters.Length; i++)
        {
            var name = nodeParameters[i].AsParameterDeclaration()!.Name();
            if (name is not null && IsIdentifier(name))
            {
                parameters.Add(name.Text());
            }
            if (IsBindingPattern(name))
            {
                excludedParameters.Add(i);
            }
        }
        if (ContainsArgumentsReference(node))
        {
            if (isJs)
            {
                var lastJSDocParamIndex = jsdocParameters.Count - 1;
                var lastJSDocParam = jsdocParameters[lastJSDocParamIndex].AsJSDocParameterOrPropertyTag();
                if (lastJSDocParam is null || lastJSDocParam.Name() is null || !IsIdentifier(lastJSDocParam.Name()!))
                {
                    return;
                }
                var lastName = lastJSDocParam.Name()!;
                if (excludedParameters.Has(lastJSDocParamIndex) || parameters.Has(lastName.Text()))
                {
                    return;
                }
                if (lastJSDocParam.TypeExpression is null || lastJSDocParam.TypeExpression.Type() is null)
                {
                    return;
                }
                if (IsArrayTypeWorker(GetTypeFromTypeNodeWorker(lastJSDocParam.TypeExpression.Type()!)))
                {
                    return;
                }
                Error(lastName, JSDoc_param_tag_has_name_0_but_there_is_no_parameter_with_that_name_It_would_match_arguments_if_it_had_an_array_type, lastName.Text());
            }
        }
        else
        {
            for (var index = 0; index < jsdocParameters.Count; index++)
            {
                var tag = jsdocParameters[index];
                var tagData = tag.AsJSDocParameterOrPropertyTag()!;
                var name = tagData.Name();
                var isNameFirst = tagData.IsNameFirst;

                if (excludedParameters.Has(index) || (name is not null && IsIdentifier(name) && parameters.Has(name.Text())))
                {
                    continue;
                }

                if (name is not null && IsQualifiedName(name))
                {
                    if (isJs)
                    {
                        Error(
                            name, Qualified_name_0_is_not_allowed_without_a_leading_param_object_1,
                            EntityNameToString(name),
                            EntityNameToString(name.AsQualifiedName()!.Left!));
                    }
                }
                else
                {
                    if (!isNameFirst)
                    {
                        ErrorOrSuggestion(
                            isJs, name,
                            JSDoc_param_tag_has_name_0_but_there_is_no_parameter_with_that_name,
                            name!.Text());
                    }
                }
            }
        }
    }

    /// <summary>Go: <c>func getAllJSDocTags(node *ast.Node) []*ast.Node</c>（jsdoc.go:86-100）。</summary>
    private static Node[] GetAllJSDocTags(Node node)
    {
        if ((node.Flags & NodeFlags.JSDoc) == 0)
        {
            for (Node? current = node; current is not null; current = GetNextJSDocCommentLocation(current!))
            {
                var jsdocs = current.JSDoc(null);
                if (jsdocs is null || jsdocs.Length == 0)
                {
                    continue;
                }
                var lastJSDoc = jsdocs[^1].AsJSDoc()!;
                if (lastJSDoc.Tags is not null)
                {
                    return lastJSDoc.Tags.Nodes ?? [];
                }
            }
        }
        return [];
    }
}
