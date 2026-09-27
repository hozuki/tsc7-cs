// ported from: internal/checker/checker.go:12187-12220（从 this 上下文/this 参数求外围类：
//   getEnclosingClassFromThisParameter、getThisParameterFromNodeContext）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的包级函数 `getThisParameterFromNodeContext` → 同一个 partial 类里的 `internal static`（§5.5）。
//    它同时被 checker.go:12346 的 `tryGetThisTypeAtEx`（C4 窗口）调用。
// 2) 包级 `getTargetType` → `GetTargetTypeWorker`（artifacts/checker-method-collisions.md 追加项：
//    包级 `getTargetType`(checker.go:19904) 与实例 `GetTargetType`(checker.go:31888，C8) 撞名）。
// 3) Worker 改名：`getTypeFromTypeNode` → `GetTypeFromTypeNodeWorker`、`getConstraintOfTypeParameter` →
//    `GetConstraintOfTypeParameterWorker`。
// 4) `thisType.objectFlags` → `thisType._objectFlags`（§5.5）。
// 5) `ast.GetThisContainer` 在 C# 里返回**非空** `Node`（Ast/Utilities.cs:2548 的签名，越界即抛
//    `DebugFailureException`，与 Go 的 `panic` 对应），所以 `thisContainer != nil` 的判断保留（与 Go 逐行对应），
//    它不是 C# 的编译错误。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type? GetEnclosingClassFromThisParameter(Node node)
    {
        // 'this' type for a node comes from, in priority order...
        // 1. The type of a syntactic 'this' parameter in the enclosing function scope
        var thisParameter = GetThisParameterFromNodeContext(node);
        Type? thisType = null;
        if (thisParameter != null && thisParameter.Type() != null)
        {
            thisType = GetTypeFromTypeNodeWorker(thisParameter.Type()!);
        }
        if (thisType != null)
        {
            // 2. The constraint of a type parameter used for an explicit 'this' parameter
            if ((thisType._flags & TypeFlags.TypeParameter) != 0)
            {
                thisType = GetConstraintOfTypeParameterWorker(thisType);
            }
        }
        else
        {
            // 3. The 'this' parameter of a contextual type
            var thisContainer = GetThisContainer(node, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
            if (thisContainer != null && IsFunctionLike(thisContainer))
            {
                thisType = GetContextualThisParameterType(thisContainer);
            }
        }
        if (thisType != null && (thisType._objectFlags & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
        {
            return GetTargetTypeWorker(thisType);
        }
        return null;
    }

    internal static Node? GetThisParameterFromNodeContext(Node node)
    {
        var thisContainer = global::TypeScript.Ast.Ast.GetThisContainer(node, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
        if (thisContainer != null && IsFunctionLike(thisContainer))
        {
            return GetThisParameter(thisContainer);
        }
        return null;
    }
}
