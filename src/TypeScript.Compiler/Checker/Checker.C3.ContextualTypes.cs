// ported from: internal/checker/checker.go:10459-10638（上下文签名与参数类型推入：getContextualSignature、
//   createUnionSignature、getContextualCallSignature、getIntersectedSignatures、isAritySmaller、
//   assignContextualParameterTypes、assignNonContextualParameterTypes、assignParameterType、assignBindingElementTypes）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Worker 改名（artifacts/checker-method-collisions.md）：`getSignaturesOfType` → `GetSignaturesOfTypeWorker`、
//    `getTypeOfSymbol` → `GetTypeOfSymbolWorker`、`getTypeAtPosition` → `GetTypeAtPositionWorker`、
//    `isTypeAssignableTo` → `IsTypeAssignableToWorker`、`getWidenedType` → `GetWidenedTypeWorker`。
// 2) Go 的 `c.compareTypesIdentical`（方法值）→ lambda `(s, t) => CompareTypesIdentical(s, t)`：它作为
//    `compareSignaturesIdentical` 的 `TypeComparer` 实参，C2 的 CompareProperties 调用点用同一写法（保持全局一致）。
// 3) Go 的 `switch { case A: … }`（无标签 switch）→ if/else-if 链。
// 4) Go 的 `len(x)` 对 `[]*Type`/`[]*Signature` → `.Length`（Go 的 nil 切片长度 0，C# 侧这些值都显式初始化为 `[]`，
//    只有 `Signature.parameters` 之类的构造参数可能为 null，用 `?? []` 兜底）。
// 5) `core.LastOrNil`/`core.Filter`/`core.FirstOrNil` → `LastOrNil`/`Filter`/`FirstOrNil`（Core 静态导入）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    // Return the contextual signature for a given expression node. A contextual type provides a
    // contextual signature if it has a single call signature and if that call signature is non-generic.
    // If the contextual type is a union type, get the signature from each type possible and if they are
    // all identical ignoring their return type, the result is same signature but with return type as
    // union type of return types from these signatures
    internal Signature? GetContextualSignature(Node node)
    {
        var t = GetApparentTypeOfContextualType(node, ContextFlags.Signature);
        if (t == null)
        {
            return null;
        }
        if ((t._flags & TypeFlags.Union) == 0)
        {
            return GetContextualCallSignature(t, node);
        }
        List<Signature>? signatureList = null;
        var types = t.Types();
        foreach (var current in types)
        {
            var signature = GetContextualCallSignature(current, node);
            if (signature != null)
            {
                if (signatureList != null && signatureList.Count != 0 && CompareSignaturesIdentical(signatureList[0], signature, false /*partialMatch*/, true /*ignoreThisTypes*/, true /*ignoreReturnTypes*/, (s, t2) => CompareTypesIdentical(s, t2)) == Ternary.False)
                {
                    // Signatures aren't identical, do not use
                    return null;
                }
                // Use this signature for contextual union signature
                (signatureList ??= []).Add(signature);
            }
        }
        if (signatureList == null || signatureList.Count == 0)
        {
            return null;
        }
        if (signatureList.Count == 1)
        {
            return signatureList[0];
        }
        // Result is union of signatures collected (return type is union of return types of this signature set)
        return CreateUnionSignature(signatureList[0], signatureList.ToArray());
    }

    internal Signature CreateUnionSignature(Signature sig, Signature[] unionSignatures)
    {
        var result = CloneSignature(sig);
        result._composite = new CompositeSignature { _isUnion = true, _signatures = unionSignatures };
        result._target = null;
        result._mapper = null;
        return result;
    }

    // If the given type is an object or union type with a single signature, and if that signature has at
    // least as many parameters as the given function, return the signature. Otherwise return undefined.
    internal Signature? GetContextualCallSignature(Type t, Node node)
    {
        var signatures = GetSignaturesOfTypeWorker(t, SignatureKind.Call);
        var applicableByArity = Filter(signatures, s => !IsAritySmaller(s, node));
        if (applicableByArity.Length == 1)
        {
            return applicableByArity[0];
        }
        return GetIntersectedSignatures(applicableByArity);
    }

    internal Signature? GetIntersectedSignatures(Signature[] signatures)
    {
        if (!_noImplicitAny)
        {
            return null;
        }
        Signature? combined = null;
        foreach (var sig in signatures)
        {
            if (combined == sig || combined == null)
            {
                combined = sig;
            }
            else if (CompareTypeParametersIdentical(combined._typeParameters, sig._typeParameters))
            {
                combined = CombineUnionOrIntersectionMemberSignatures(combined, sig, false /*isUnion*/);
            }
            else
            {
                return null;
            }
        }
        return combined;
    }

    /** If the contextual signature has fewer parameters than the function expression, do not use it */
    internal bool IsAritySmaller(Signature signature, Node target)
    {
        var parameters = target.Parameters() ?? [];
        var targetParameterCount = 0;
        while (targetParameterCount < parameters.Length)
        {
            var param = parameters[targetParameterCount];
            if (param.Initializer() != null || param.QuestionToken() != null || HasDotDotDotToken(param))
            {
                break;
            }
            targetParameterCount++;
        }
        if (parameters.Length != 0 && IsThisParameter(parameters[0]))
        {
            targetParameterCount--;
        }
        return !HasEffectiveRestParameterWorker(signature) && GetParameterCount(signature) < targetParameterCount;
    }

    internal void AssignContextualParameterTypes(Signature sig, Signature context)
    {
        if (context.TypeParameters().Length != 0)
        {
            if (sig.TypeParameters().Length != 0)
            {
                // This signature has already has a contextual inference performed and cached on it
                return;
            }
            sig._typeParameters = context._typeParameters;
        }
        if (context._thisParameter != null)
        {
            var thisParameter = sig._thisParameter;
            if (thisParameter == null || thisParameter.ValueDeclaration != null && thisParameter.ValueDeclaration.Type() == null)
            {
                if (thisParameter == null)
                {
                    sig._thisParameter = CreateSymbolWithType(context._thisParameter, null /*type*/);
                }
                AssignParameterType(sig._thisParameter!, GetTypeOfSymbolWorker(context._thisParameter));
            }
        }
        var length = sig.Parameters().Length - IfElse(SignatureHasRestParameter(sig), 1, 0);
        for (var i = 0; i < length; i++)
        {
            var parameter = sig.Parameters()[i];
            var declaration = parameter.ValueDeclaration!;
            if (declaration.Type() == null)
            {
                var t = TryGetTypeAtPosition(context, i);
                if (t != null && declaration.Initializer() != null)
                {
                    var initializerType = CheckDeclarationInitializer(declaration, CheckMode.Normal, null);
                    if (!IsTypeAssignableToWorker(initializerType, t))
                    {
                        initializerType = WidenTypeInferredFromInitializer(declaration, initializerType);
                        if (IsTypeAssignableToWorker(t, initializerType))
                        {
                            t = initializerType;
                        }
                    }
                }
                AssignParameterType(parameter, t);
            }
        }
        if (SignatureHasRestParameter(sig))
        {
            // parameter might be a transient symbol generated by use of `arguments` in the function body.
            var parameter = LastOrNil(sig._parameters)!;
            if (parameter.ValueDeclaration != null && parameter.ValueDeclaration.Type() == null ||
                parameter.ValueDeclaration == null && (parameter.CheckFlags & CheckFlags.DeferredType) != 0)
            {
                var contextualParameterType = GetRestTypeAtPosition(context, length, false);
                AssignParameterType(parameter, contextualParameterType);
            }
        }
    }

    internal void AssignNonContextualParameterTypes(Signature signature)
    {
        if (signature._thisParameter != null)
        {
            AssignParameterType(signature._thisParameter, null);
        }
        foreach (var parameter in signature.Parameters())
        {
            AssignParameterType(parameter, null);
        }
    }

    internal void AssignParameterType(Symbol parameter, Type? contextualType)
    {
        var links = _valueSymbolLinks.Get(parameter);
        if (links._resolvedType != null)
        {
            return;
        }
        var declaration = parameter.ValueDeclaration;
        var t = contextualType;
        if (t == null)
        {
            if (declaration != null)
            {
                t = GetWidenedTypeForVariableLikeDeclaration(declaration, true /*reportErrors*/);
            }
            else
            {
                t = GetTypeOfSymbolWorker(parameter);
            }
        }
        links._resolvedType = AddOptionalityEx(t, false, declaration != null && declaration.Initializer() == null && IsOptionalDeclaration(declaration));
        if (declaration != null && !IsIdentifier(declaration.Name()!))
        {
            // if inference didn't come up with anything but unknown, fall back to the binding pattern if present.
            if (links._resolvedType == _unknownType)
            {
                links._resolvedType = GetTypeFromBindingPattern(declaration.Name()!, false, false);
            }
            AssignBindingElementTypes(declaration.Name()!, links._resolvedType);
        }
    }

    // When contextual typing assigns a type to a parameter that contains a binding pattern, we also need to push
    // the destructured type into the contained binding elements.
    internal void AssignBindingElementTypes(Node pattern, Type parentType)
    {
        foreach (var element in pattern.Elements() ?? [])
        {
            var name = element.Name();
            if (name != null)
            {
                var t = GetBindingElementTypeFromParentType(element, parentType, false /*noTupleBoundsCheck*/);
                if (IsIdentifier(name))
                {
                    // Go: `c.valueSymbolLinks.Get(c.getSymbolOfDeclaration(element)).resolvedType = t`（Go 的 nil 键
                    // 合法、静默丢弃赋值），保持同样的 nil 行为而不是抛异常。
                    var elementSymbol = GetSymbolOfDeclaration(element);
                    if (elementSymbol != null)
                    {
                        _valueSymbolLinks.Get(elementSymbol)._resolvedType = t;
                    }
                }
                else
                {
                    AssignBindingElementTypes(name, t);
                }
            }
        }
    }
}
