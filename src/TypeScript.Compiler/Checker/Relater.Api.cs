// ported from: internal/checker/relater.go:117-1198
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `diagnosticOutput *[]*ast.Diagnostic` → C# `ref Diagnostic[]?`（合同 §3/§5.10 更正）；唯一例外是
//    `CheckTypeAssignableToEx`，用 `out Diagnostic[]?`（调用点写 `out var diags`）；追加写
//    `diagnosticOutput = [.. diagnosticOutput, diagnostic]`（追加分支里已判非空）。
// 2) Go 包级函数（`createDiagnosticChainFromErrorChain`/`isHyphenatedJsxName`/`isExcessPropertyCheckTarget`/
//    `hasMatchingRecursionIdentity`/`getRecursionIdentity`/`getRecursionIdentityTarget`/`getRecursionIdentityFromTarget`/
//    `excludeProperties`/`isObjectOrInstantiableNonPrimitive`）→ 同一个 `partial class Checker` 的 `internal static`
//    成员（C# 没有自由函数）。
// 3) 同名冲突（artifacts/checker-method-collisions.md）：`c.isTypeAssignableTo` → `IsTypeAssignableToWorker`、
//    `c.getPropertiesOfType` → `GetPropertiesOfTypeWorker`、`c.getPropertyOfType` → `GetPropertyOfTypeWorker`、
//    `c.getTypeOfSymbol` → `GetTypeOfSymbolWorker`、`c.getDeclaredTypeOfSymbol` → `GetDeclaredTypeOfSymbolWorker`、
//    `c.getSignaturesOfType` → `GetSignaturesOfTypeWorker`、`c.getReturnTypeOfSignature` → `GetReturnTypeOfSignatureWorker`、
//    `c.getUnionType` → `GetUnionTypeWorker`、`c.getIndexInfoOfType` → `GetIndexInfoOfTypeWorker`、
//    `c.getTypeOfPropertyOfType` → `GetTypeOfPropertyOfTypeWorker`、`c.symbolToString` → `SymbolToStringWorker`、
//    `c.typeToString`(2 参) → `TypeToStringWorker`。Go **导出**的 `c.TypeToString(t)`（1 参）与 `c.TypeToStringEx(...)`
//    保持原名（本片只调用这两个）。
// 4) Go 的 `getRelationKey(...) (CacheHashKey, bool)` → C# `TryGetRelationKey(..., out CacheHashKey)`（C5）。
// 5) Go 的 map 读缺失键返回零值 → `Dictionary.TryGetValue`（`c.enumRelation[key]`、`union.constituentMap[key]`、
//    `typesByKey[key]`；`Dictionary` 索引器读缺失键抛异常）。
// 6) Go 的 `any == any`（字面量值比较 `AsLiteralType().value == ...` / `sourceValue != targetValue`）→ C# `Equals(a, b)`：
//    C# 的 `object` `==` 是引用比较，对装箱的 jsnum.Number/string 语义错误（Go 的 `==` 比动态值）。
// 7) Go 的 nil 切片长度视为 0 → `?? []` / `?.Length ?? 0`（`Node.Properties()`/`Elements()`/`Parameters()`、
//    `GetPropertiesOfObjectType`、`GetSignaturesOfStructuredType`、`Symbol.Declarations`）。
// 8) `jsnum.Number(i)` → `new Number(i)`；`jsnum.Number(i).String()` → `new Number(i).String()`。
// 9) Go 的 `switch` `fallthrough` → C# `goto case`；`case A, B:` → 两个 case 标签（顺序敏感，保持 Go 的判定顺序）。
// 10) 函数值：`c.compareTypesAssignableSimple`/`c.compareTypesSubtypeOf`/`c.compareTypesIdentical`（本片实例方法）
//     直接以方法组传入 `TypeRelatedComparer`；`isSimpleTypeRelatedTo` 的 `errorReporter` 为 `ErrorReporter?`，
//     热路径调用点传 `null`（零分配）。其它内联闭包按合同 §4.5 用 lambda/局部函数（分配量与 Go 的闭包一致）。
// 11) `ast.SymbolFlags*`/`ast.CheckFlags*`/`ast.Kind*` 在 `TypeScript.Ast`；`t.flags`/`t.symbol`/`t.objectFlags`/`t.id`
//     → `t._flags`/`t._symbol`/`t._objectFlags`/`t._id`；`t.AsX()` 返回可空 → 按 AST 不变量加 `!`。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal bool IsTypeIdenticalTo(Type source, Type target)
    {
        return IsTypeRelatedTo(source, target, _identityRelation);
    }

    internal Ternary CompareTypesIdentical(Type source, Type target)
    {
        if (IsTypeRelatedTo(source, target, _identityRelation))
        {
            return Ternary.True;
        }
        return Ternary.False;
    }

    internal Ternary CompareTypesAssignableSimple(Type source, Type target)
    {
        if (IsTypeRelatedTo(source, target, _assignableRelation))
        {
            return Ternary.True;
        }
        return Ternary.False;
    }

    internal Ternary CompareTypesAssignableWorker(Type source, Type target, bool reportErrors)
    {
        if (IsTypeRelatedTo(source, target, _assignableRelation))
        {
            return Ternary.True;
        }
        return Ternary.False;
    }

    internal Ternary CompareTypesSubtypeOf(Type source, Type target)
    {
        if (IsTypeRelatedTo(source, target, _subtypeRelation))
        {
            return Ternary.True;
        }
        return Ternary.False;
    }

    internal bool IsTypeAssignableToWorker(Type source, Type target)
    {
        return IsTypeRelatedTo(source, target, _assignableRelation);
    }

    internal bool IsTypeSubtypeOf(Type source, Type target)
    {
        return IsTypeRelatedTo(source, target, _subtypeRelation);
    }

    internal bool IsTypeStrictSubtypeOf(Type source, Type target)
    {
        return IsTypeRelatedTo(source, target, _strictSubtypeRelation);
    }

    internal bool IsTypeComparableTo(Type source, Type target)
    {
        return IsTypeRelatedTo(source, target, _comparableRelation);
    }

    internal bool AreTypesComparable(Type type1, Type type2)
    {
        return IsTypeComparableTo(type1, type2) || IsTypeComparableTo(type2, type1);
    }

    internal bool IsTypeRelatedTo(Type source, Type target, Relation relation)
    {
        if (IsFreshLiteralType(source))
        {
            source = source.AsLiteralType()!._regularType!;
        }
        if (IsFreshLiteralType(target))
        {
            target = target.AsLiteralType()!._regularType!;
        }
        if (source == target)
        {
            return true;
        }
        if (relation != _identityRelation)
        {
            if (relation == _comparableRelation && (target._flags & TypeFlags.Never) == 0 && IsSimpleTypeRelatedTo(target, source, relation, null) || IsSimpleTypeRelatedTo(source, target, relation, null))
            {
                return true;
            }
        }
        else if (!(((source._flags | target._flags) & (TypeFlags.UnionOrIntersection | TypeFlags.IndexedAccess | TypeFlags.Conditional | TypeFlags.Substitution)) != 0))
        {
            // We have excluded types that may simplify to other forms, so types must have identical flags
            if (source._flags != target._flags)
            {
                return false;
            }
            if ((source._flags & TypeFlags.Singleton) != 0)
            {
                return true;
            }
        }
        if ((source._flags & TypeFlags.Object) != 0 && (target._flags & TypeFlags.Object) != 0)
        {
            TryGetRelationKey(source, target, IntersectionState.None, relation == _identityRelation, false, out var id);
            var related = relation.Get(id);
            if (related != RelationComparisonResult.None)
            {
                return (related & RelationComparisonResult.Succeeded) != 0;
            }
        }
        if ((source._flags & TypeFlags.StructuredOrInstantiable) != 0 || (target._flags & TypeFlags.StructuredOrInstantiable) != 0)
        {
            return CheckTypeRelatedTo(source, target, relation, null /*errorNode*/);
        }
        return false;
    }

    internal bool IsSimpleTypeRelatedTo(Type source, Type target, Relation relation, ErrorReporter? errorReporter)
    {
        var s = source._flags;
        var t = target._flags;
        if ((t & TypeFlags.Any) != 0 || (s & TypeFlags.Never) != 0 || source == _wildcardType)
        {
            return true;
        }
        if ((t & TypeFlags.Unknown) != 0 && !(relation == _strictSubtypeRelation && (s & TypeFlags.Any) != 0))
        {
            return true;
        }
        if ((t & TypeFlags.Never) != 0)
        {
            return false;
        }
        if ((s & TypeFlags.StringLike) != 0 && (t & TypeFlags.String) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.StringLiteral) != 0 && (s & TypeFlags.EnumLiteral) != 0 && (t & TypeFlags.StringLiteral) != 0 && (t & TypeFlags.EnumLiteral) == 0 && Equals(source.AsLiteralType()!._value, target.AsLiteralType()!._value))
        {
            return true;
        }
        if ((s & TypeFlags.NumberLike) != 0 && (t & TypeFlags.Number) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.NumberLiteral) != 0 && (s & TypeFlags.EnumLiteral) != 0 && (t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) == 0 && Equals(source.AsLiteralType()!._value, target.AsLiteralType()!._value))
        {
            return true;
        }
        if ((s & TypeFlags.BigIntLike) != 0 && (t & TypeFlags.BigInt) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.BooleanLike) != 0 && (t & TypeFlags.Boolean) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.ESSymbolLike) != 0 && (t & TypeFlags.ESSymbol) != 0)
        {
            return true;
        }
        if ((s & TypeFlags.Enum) != 0 && (t & TypeFlags.Enum) != 0 && source._symbol!.Name == target._symbol!.Name && IsEnumTypeRelatedTo(source._symbol!, target._symbol!, errorReporter))
        {
            return true;
        }
        if ((s & TypeFlags.EnumLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0)
        {
            if ((s & TypeFlags.Union) != 0 && (t & TypeFlags.Union) != 0 && IsEnumTypeRelatedTo(source._symbol!, target._symbol!, errorReporter))
            {
                return true;
            }
            if ((s & TypeFlags.Literal) != 0 && (t & TypeFlags.Literal) != 0 && Equals(source.AsLiteralType()!._value, target.AsLiteralType()!._value) && IsEnumTypeRelatedTo(source._symbol!, target._symbol!, errorReporter))
            {
                return true;
            }
        }
        // In non-strictNullChecks mode, `undefined` and `null` are assignable to anything except `never`.
        // Since unions and intersections may reduce to `never`, we exclude them here.
        if ((s & TypeFlags.Undefined) != 0 && (!_strictNullChecks && (t & TypeFlags.UnionOrIntersection) == 0 || (t & (TypeFlags.Undefined | TypeFlags.Void)) != 0))
        {
            return true;
        }
        if ((s & TypeFlags.Null) != 0 && (!_strictNullChecks && (t & TypeFlags.UnionOrIntersection) == 0 || (t & TypeFlags.Null) != 0))
        {
            return true;
        }
        if ((s & TypeFlags.Object) != 0 && (t & TypeFlags.NonPrimitive) != 0 && !(relation == _strictSubtypeRelation && IsEmptyAnonymousObjectType(source) && (source._objectFlags & ObjectFlags.FreshLiteral) == 0))
        {
            return true;
        }
        if (relation == _assignableRelation || relation == _comparableRelation)
        {
            if ((s & TypeFlags.Any) != 0)
            {
                return true;
            }
            // Type number is assignable to any computed numeric enum type or any numeric enum literal type, and
            // a numeric literal type is assignable any computed numeric enum type or any numeric enum literal type
            // with a matching value. These rules exist such that enums can be used for bit-flag purposes.
            if ((s & TypeFlags.Number) != 0 && ((t & TypeFlags.Enum) != 0 || (t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0))
            {
                return true;
            }
            if ((s & TypeFlags.NumberLiteral) != 0 && (s & TypeFlags.EnumLiteral) == 0 && ((t & TypeFlags.Enum) != 0 || (t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0 && Equals(source.AsLiteralType()!._value, target.AsLiteralType()!._value)))
            {
                return true;
            }
            // Anything is assignable to a union containing undefined, null, and {}
            if (IsUnknownLikeUnionType(target))
            {
                return true;
            }
        }
        return false;
    }

    internal bool IsEnumTypeRelatedTo(Symbol source, Symbol target, ErrorReporter? errorReporter)
    {
        var sourceSymbol = (source.Flags & SymbolFlags.EnumMember) != 0 ? GetParentOfSymbol(source)! : source;
        var targetSymbol = (target.Flags & SymbolFlags.EnumMember) != 0 ? GetParentOfSymbol(target)! : target;
        if (sourceSymbol == targetSymbol)
        {
            return true;
        }
        if (sourceSymbol.Name != targetSymbol.Name || (sourceSymbol.Flags & SymbolFlags.RegularEnum) == 0 || (targetSymbol.Flags & SymbolFlags.RegularEnum) == 0)
        {
            return false;
        }
        var key = new EnumRelationKey(GetSymbolId(sourceSymbol), GetSymbolId(targetSymbol));
        var entry = _enumRelation.TryGetValue(key, out var foundEntry) ? foundEntry : RelationComparisonResult.None;
        if (entry != RelationComparisonResult.None && !((entry & RelationComparisonResult.Failed) != 0 && errorReporter != null))
        {
            return (entry & RelationComparisonResult.Succeeded) != 0;
        }
        var targetEnumType = GetTypeOfSymbolWorker(targetSymbol);
        foreach (var sourceProperty in GetPropertiesOfTypeWorker(GetTypeOfSymbolWorker(sourceSymbol)) ?? [])
        {
            if ((sourceProperty.Flags & SymbolFlags.EnumMember) != 0)
            {
                var targetProperty = GetPropertyOfTypeWorker(targetEnumType, sourceProperty.Name);
                if (targetProperty == null || (targetProperty.Flags & SymbolFlags.EnumMember) == 0)
                {
                    if (errorReporter != null)
                    {
                        errorReporter(Diagnostics.Property_0_is_missing_in_type_1, SymbolToStringWorker(sourceProperty), TypeToStringEx(GetDeclaredTypeOfSymbolWorker(targetSymbol), null /*enclosingDeclaration*/, TypeFormatFlags.UseFullyQualifiedType, null));
                    }
                    _enumRelation[key] = RelationComparisonResult.Failed;
                    return false;
                }
                var sourceValue = GetEnumMemberValue(GetDeclarationOfKind(sourceProperty, Kind.EnumMember)!).Value;
                var targetValue = GetEnumMemberValue(GetDeclarationOfKind(targetProperty, Kind.EnumMember)!).Value;
                if (!Equals(sourceValue, targetValue))
                {
                    // If we have 2 enums with *known* values that differ, they are incompatible.
                    if (sourceValue != null && targetValue != null)
                    {
                        if (errorReporter != null)
                        {
                            errorReporter(Diagnostics.Each_declaration_of_0_1_differs_in_its_value_where_2_was_expected_but_3_was_given, SymbolToStringWorker(targetSymbol), SymbolToStringWorker(targetProperty), ValueToString(targetValue), ValueToString(sourceValue));
                        }
                        _enumRelation[key] = RelationComparisonResult.Failed;
                        return false;
                    }
                    // At this point we know that at least one of the values is 'undefined'.
                    // This may mean that we have an opaque member from an ambient enum declaration,
                    // or that we were not able to calculate it (which is basically an error).
                    //
                    // Either way, we can assume that it's numeric.
                    // If the other is a string, we have a mismatch in types.
                    var sourceIsString = sourceValue is string;
                    var targetIsString = targetValue is string;
                    if (sourceIsString || targetIsString)
                    {
                        if (errorReporter != null)
                        {
                            var knownStringValue = OrElse(sourceValue, targetValue);
                            errorReporter(Diagnostics.One_value_of_0_1_is_the_string_2_and_the_other_is_assumed_to_be_an_unknown_numeric_value, SymbolToStringWorker(targetSymbol), SymbolToStringWorker(targetProperty), ValueToString(knownStringValue));
                        }
                        _enumRelation[key] = RelationComparisonResult.Failed;
                        return false;
                    }
                }
            }
        }
        _enumRelation[key] = RelationComparisonResult.Succeeded;
        return true;
    }

    internal bool CheckTypeAssignableTo(Type source, Type target, Node? errorNode, Message? headMessage)
    {
        Diagnostic[]? diagnosticOutput = null;
        return CheckTypeRelatedToEx(source, target, _assignableRelation, errorNode, headMessage, ref diagnosticOutput);
    }

    internal bool CheckTypeAssignableToEx(Type source, Type target, Node? errorNode, Message? headMessage, out Diagnostic[]? diagnosticOutput)
    {
        // Go: checkTypeAssignableToEx 的调用点都传 `&diags`（非 nil 指针指向可能为 nil 的切片）→ 诊断进缓冲区，
        // 不落全局。C# 用 `ref Diagnostic[]?` 表示 `*[]*Diagnostic`，null 表示 **nil 指针**（→ AddDiagnostic），
        // 所以这里的"指针"必须用非 null 的空数组起步，否则诊断会被 AddDiagnostic 而 out 仍是 null。
        Diagnostic[]? output = [];
        var result = CheckTypeRelatedToEx(source, target, _assignableRelation, errorNode, headMessage, ref output);
        diagnosticOutput = output;
        return result;
    }

    internal bool CheckTypeComparableTo(Type source, Type target, Node? errorNode, Message? headMessage)
    {
        Diagnostic[]? diagnosticOutput = null;
        return CheckTypeRelatedToEx(source, target, _comparableRelation, errorNode, headMessage, ref diagnosticOutput);
    }

    internal bool CheckTypeRelatedTo(Type source, Type target, Relation relation, Node? errorNode)
    {
        Diagnostic[]? diagnosticOutput = null;
        return CheckTypeRelatedToEx(source, target, relation, errorNode, null, ref diagnosticOutput);
    }

    // Check that source is related to target according to the given relation. When errorNode is non-nil, errors are
    // reported to the checker's diagnostic collection or through diagnosticOutput when non-nil. Callers can assume that
    // this function only reports zero or one error to diagnosticOutput (unlike checkTypeRelatedToAndOptionallyElaborate).
    internal bool CheckTypeRelatedToEx(
        Type source,
        Type target,
        Relation relation,
        Node? errorNode,
        Message? headMessage,
        ref Diagnostic[]? diagnosticOutput)
    {
        var r = GetRelater();
        r._relation = relation;
        r._errorNode = errorNode;
        r._relationCount = (16_000_000 - relation.Size()) / 8;
        var result = r.IsRelatedToEx(source, target, RecursionFlags.Both, errorNode != null /*reportErrors*/, headMessage, IntersectionState.None);
        if (r._overflow)
        {
            // Record this relation as having failed such that we don't attempt the overflowing operation again.
            TryGetRelationKey(source, target, IntersectionState.None, relation == _identityRelation, false /*ignoreConstraints*/, out var id);
            relation.Set(id, RelationComparisonResult.Failed | RelationComparisonResult.ComplexityOverflow);
            var tr = _tracer;
            if (tr != null)
            {
                tr.Instant(Phase.CheckTypes, "checkTypeRelatedTo_DepthLimit", new Dictionary<string, object?> { ["sourceId"] = source._id.Value, ["targetId"] = target._id.Value, ["depth"] = r._sourceStack.Count, ["targetDepth"] = r._targetStack.Count });
            }
            if (errorNode == null)
            {
                errorNode = _currentNode;
            }
            ReportDiagnostic(NewDiagnosticForNode(errorNode, Diagnostics.Excessive_complexity_comparing_types_0_and_1, TypeToString(source), TypeToString(target)), ref diagnosticOutput);
        }
        else if (r._errorChain != null)
        {
            // Check if we should issue an extra diagnostic to produce a quickfix for a slightly incorrect import statement
            if (headMessage != null && errorNode != null && result == Ternary.False && source._symbol != null && _exportTypeLinks.Has(source._symbol))
            {
                var links = _exportTypeLinks.Get(source._symbol);
                if (links._originatingImport != null && !IsImportCall(links._originatingImport))
                {
                    var helpfulRetry = CheckTypeRelatedTo(GetTypeOfSymbolWorker(links._target!), target, relation /*errorNode*/, null);
                    if (helpfulRetry)
                    {
                        // Likely an incorrect import. Issue a helpful diagnostic to produce a quickfix to change the import
                        r._relatedInfo = [.. r._relatedInfo, CreateDiagnosticForNode(links._originatingImport, Diagnostics.Type_originates_at_this_import_A_namespace_style_import_cannot_be_called_or_constructed_and_will_cause_a_failure_at_runtime_Consider_using_a_default_import_or_import_require_here_instead)];
                    }
                }
            }
            ReportDiagnostic(CreateDiagnosticChainFromErrorChain(r._errorChain, r._errorNode, r._relatedInfo), ref diagnosticOutput);
        }
        PutRelater(r);
        return result != Ternary.False;
    }

    internal static Diagnostic? CreateDiagnosticChainFromErrorChain(ErrorChain? chain, Node? errorNode, Diagnostic[] relatedInfo)
    {
        while (chain != null && chain._message.ElidedInCompatibilityPyramid())
        {
            chain = chain._next;
        }
        if (chain == null)
        {
            return null;
        }
        var next = CreateDiagnosticChainFromErrorChain(chain._next, errorNode, relatedInfo);
        if (next == null)
        {
            return NewDiagnosticForNode(errorNode, chain._message, chain._args).SetRelatedInfo(relatedInfo);
        }
        return NewDiagnosticChain(next, chain._message, chain._args);
    }

    internal void ReportDiagnostic(Diagnostic? diagnostic, ref Diagnostic[]? diagnosticOutput)
    {
        if (diagnostic != null)
        {
            if (diagnosticOutput != null)
            {
                diagnosticOutput = [.. diagnosticOutput, diagnostic];
            }
            else
            {
                AddDiagnostic(diagnostic);
            }
        }
    }

    internal bool CheckTypeAssignableToAndOptionallyElaborate(Type source, Type target, Node? errorNode, Node? expr, Message? headMessage, Diagnostic[]? diagnosticOutput)
    {
        return CheckTypeRelatedToAndOptionallyElaborate(source, target, _assignableRelation, errorNode, expr, headMessage, ref diagnosticOutput);
    }

    internal bool CheckTypeRelatedToAndOptionallyElaborate(Type source, Type target, Relation relation, Node? errorNode, Node? expr, Message? headMessage, ref Diagnostic[]? diagnosticOutput)
    {
        if (IsTypeRelatedTo(source, target, relation))
        {
            return true;
        }
        if (errorNode != null && !ElaborateError(expr, source, target, relation, headMessage, ref diagnosticOutput))
        {
            return CheckTypeRelatedToEx(source, target, relation, errorNode, headMessage, ref diagnosticOutput);
        }
        return false;
    }

    internal bool ElaborateError(Node? node, Type source, Type target, Relation relation, Message? headMessage, ref Diagnostic[]? diagnosticOutput)
    {
        if (node == null || IsOrHasGenericConditional(target))
        {
            return false;
        }
        if (_compilerOptions.NoCheck.IsTrue())
        {
            return false;
        }
        if (ElaborateDidYouMeanToCallOrConstruct(node, source, target, relation, SignatureKind.Construct, headMessage, ref diagnosticOutput) ||
            ElaborateDidYouMeanToCallOrConstruct(node, source, target, relation, SignatureKind.Call, headMessage, ref diagnosticOutput))
        {
            return true;
        }
        switch (node.Kind)
        {
            case Kind.AsExpression:
                if (!IsConstAssertion(node))
                {
                    break;
                }
                goto case Kind.JsxExpression;
            case Kind.JsxExpression:
            case Kind.ParenthesizedExpression:
                return ElaborateError(node.Expression(), source, target, relation, headMessage, ref diagnosticOutput);
            case Kind.BinaryExpression:
                switch (node.AsBinaryExpression()!.OperatorToken!.Kind)
                {
                    case Kind.EqualsToken:
                    case Kind.CommaToken:
                        return ElaborateError(node.AsBinaryExpression()!.Right, source, target, relation, headMessage, ref diagnosticOutput);
                }
                break;
            case Kind.ObjectLiteralExpression:
                return ElaborateObjectLiteral(node, source, target, relation, ref diagnosticOutput);
            case Kind.ArrayLiteralExpression:
                return ElaborateArrayLiteral(node, source, target, relation, ref diagnosticOutput);
            case Kind.ArrowFunction:
                return ElaborateArrowFunction(node, source, target, relation, ref diagnosticOutput);
            case Kind.JsxAttributes:
                return ElaborateJsxComponents(node, source, target, relation, ref diagnosticOutput);
        }
        return false;
    }

    internal bool IsOrHasGenericConditional(Type t)
    {
        return (t._flags & TypeFlags.Conditional) != 0 || ((t._flags & TypeFlags.Intersection) != 0 && Some(t.Types(), IsOrHasGenericConditional));
    }

    internal bool ElaborateDidYouMeanToCallOrConstruct(Node? node, Type source, Type target, Relation relation, SignatureKind kind, Message? headMessage, ref Diagnostic[]? diagnosticOutput)
    {
        if (Some(GetSignaturesOfTypeWorker(source, kind), s =>
        {
            var returnType = GetReturnTypeOfSignatureWorker(s);
            return (returnType._flags & (TypeFlags.Any | TypeFlags.Never)) == 0 && CheckTypeRelatedTo(returnType, target, relation, null /*errorNode*/);
        }))
        {
            // Go: `var diags []*ast.Diagnostic; … &diags` → 指针非 nil、切片为 nil ⇒ 这里用非 null 空数组起步。
            Diagnostic[]? diags = [];
            if (!CheckTypeRelatedToEx(source, target, relation, node, headMessage, ref diags))
            {
                var diagnostic = diags![0];
                var message = kind == SignatureKind.Construct
                    ? Diagnostics.Did_you_mean_to_use_new_with_this_expression
                    : Diagnostics.Did_you_mean_to_call_this_expression;
                ReportDiagnostic(diagnostic.AddRelatedInfo(CreateDiagnosticForNode(node!, message)), ref diagnosticOutput);
                return true;
            }
        }
        return false;
    }

    internal bool ElaborateObjectLiteral(Node? node, Type source, Type target, Relation relation, ref Diagnostic[]? diagnosticOutput)
    {
        if ((target._flags & (TypeFlags.Primitive | TypeFlags.Never)) != 0)
        {
            return false;
        }
        var reportedError = false;
        foreach (var prop in node!.Properties() ?? [])
        {
            if (IsSpreadAssignment(prop))
            {
                continue;
            }
            var nameType = GetLiteralTypeFromProperty(GetSymbolOfDeclaration(prop)!, TypeFlags.StringOrNumberLiteralOrUnique, false);
            if (nameType == null || (nameType._flags & TypeFlags.Never) != 0)
            {
                continue;
            }
            switch (prop.Kind)
            {
                case Kind.SetAccessor:
                case Kind.GetAccessor:
                case Kind.MethodDeclaration:
                case Kind.ShorthandPropertyAssignment:
                    reportedError = ElaborateElement(source, target, relation, prop.Name(), null, nameType, null, null, ref diagnosticOutput) || reportedError;
                    break;
                case Kind.PropertyAssignment:
                    Message? message = IsComputedNonLiteralName(prop.Name()) ? Diagnostics.Type_of_computed_property_s_value_is_0_which_is_not_assignable_to_type_1 : null;
                    reportedError = ElaborateElement(source, target, relation, prop.Name(), prop.Initializer(), nameType, message, null, ref diagnosticOutput) || reportedError;
                    break;
            }
        }
        return reportedError;
    }

    internal bool ElaborateArrayLiteral(Node? node, Type source, Type target, Relation relation, ref Diagnostic[]? diagnosticOutput)
    {
        if ((target._flags & (TypeFlags.Primitive | TypeFlags.Never)) != 0)
        {
            return false;
        }
        if (!IsTupleLikeType(source))
        {
            PushContextualType(node!, target, false /*isCache*/);
            source = CheckArrayLiteral(node!, CheckMode.Contextual | CheckMode.ForceTuple);
            PopContextualType();
            if (!IsTupleLikeType(source))
            {
                return false;
            }
        }
        var reportedError = false;
        var elements = node!.Elements() ?? [];
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (IsOmittedExpression(element) || IsTupleLikeType(target) && GetPropertyOfTypeWorker(target, new Number(i).String()) == null)
            {
                continue;
            }
            var nameType = GetNumberLiteralType(new Number(i));
            var checkNode = GetEffectiveCheckNode(element);
            reportedError = ElaborateElement(source, target, relation, checkNode, checkNode, nameType, null, null, ref diagnosticOutput) || reportedError;
        }
        return reportedError;
    }

    internal bool ElaborateElement(Type source, Type target, Relation relation, Node? prop, Node? next, Type nameType, Message? errorMessage, Func<Node, Diagnostic>? diagnosticFactory, ref Diagnostic[]? diagnosticOutput)
    {
        var targetPropType = GetBestMatchIndexedAccessTypeOrUndefined(source, target, nameType);
        if (targetPropType == null || (targetPropType._flags & TypeFlags.IndexedAccess) != 0)
        {
            // Don't elaborate on indexes on generic variables
            return false;
        }
        var sourcePropType = GetIndexedAccessTypeOrUndefined(source, nameType, AccessFlags.None, null, null);
        if (sourcePropType == null || CheckTypeRelatedTo(sourcePropType, targetPropType, relation, null /*errorNode*/))
        {
            // Don't elaborate on indexes on generic variables or when types match
            return false;
        }
        if (next != null && ElaborateError(next, sourcePropType, targetPropType, relation, null /*headMessage*/, ref diagnosticOutput))
        {
            return true;
        }
        // Issue error on the prop itself, since the prop couldn't elaborate the error
        // Go: `var diags []*ast.Diagnostic; … &diags` → 指针非 nil、切片为 nil ⇒ 这里用非 null 空数组起步。
        Diagnostic[]? diags = [];
        // Use the expression type, if available
        var specificSource = sourcePropType;
        if (next != null)
        {
            specificSource = CheckExpressionForMutableLocationWithContextualType(next, sourcePropType);
        }
        if (diagnosticFactory != null)
        {
            // Use the custom diagnostic factory if provided (e.g., for JSX text children with dynamic error messages)
            diags ??= [];
            diags = [.. diags, diagnosticFactory(prop!)];
        }
        else if (_exactOptionalPropertyTypes && IsExactOptionalPropertyMismatch(specificSource, targetPropType))
        {
            diags ??= [];
            diags = [.. diags, CreateDiagnosticForNode(prop!, Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target, TypeToString(specificSource), TypeToString(targetPropType))];
        }
        else
        {
            var propName = GetPropertyNameFromIndex(nameType, null /*accessNode*/);
            var targetIsOptional = (OrElse(GetPropertyOfTypeWorker(target, propName), _unknownSymbol)!.Flags & SymbolFlags.Optional) != 0;
            var sourceIsOptional = (OrElse(GetPropertyOfTypeWorker(source, propName), _unknownSymbol)!.Flags & SymbolFlags.Optional) != 0;
            targetPropType = RemoveMissingType(targetPropType, targetIsOptional);
            sourcePropType = RemoveMissingType(sourcePropType, targetIsOptional && sourceIsOptional);
            var result = CheckTypeRelatedToEx(specificSource, targetPropType, relation, prop, errorMessage, ref diags);
            if (result && specificSource != sourcePropType)
            {
                // If for whatever reason the expression type doesn't yield an error, make sure we still issue an error on the sourcePropType
                CheckTypeRelatedToEx(sourcePropType, targetPropType, relation, prop, errorMessage, ref diags);
            }
        }
        if (diags == null || diags.Length == 0)
        {
            return false;
        }
        var diagnostic = diags[0];
        var propertyName = "";
        Symbol? targetProp = null;
        if (IsTypeUsableAsPropertyNameWorker(nameType))
        {
            propertyName = GetPropertyNameFromTypeWorker(nameType);
            targetProp = GetPropertyOfTypeWorker(target, propertyName);
        }
        var issuedElaboration = false;
        if (targetProp == null)
        {
            var indexInfo = GetApplicableIndexInfo(target, nameType);
            var indexInfoDeclaration = indexInfo?._declaration;
            if (indexInfoDeclaration != null && !_program.IsSourceFileDefaultLibrary(GetSourceFileOfNode(indexInfoDeclaration)!.Path()))
            {
                issuedElaboration = true;
                diagnostic.AddRelatedInfo(CreateDiagnosticForNode(indexInfoDeclaration, Diagnostics.The_expected_type_comes_from_this_index_signature));
            }
        }
        if (!issuedElaboration && (targetProp != null && (targetProp.Declarations?.Length ?? 0) != 0 || target._symbol != null && (target._symbol.Declarations?.Length ?? 0) != 0))
        {
            Node targetNode;
            if (targetProp != null && (targetProp.Declarations?.Length ?? 0) != 0)
            {
                targetNode = targetProp.Declarations![0];
            }
            else
            {
                targetNode = target._symbol!.Declarations![0];
            }
            if (propertyName == "" || (nameType._flags & TypeFlags.UniqueESSymbol) != 0)
            {
                propertyName = TypeToString(nameType);
            }
            if (!_program.IsSourceFileDefaultLibrary(GetSourceFileOfNode(targetNode)!.Path()))
            {
                diagnostic.AddRelatedInfo(CreateDiagnosticForNode(targetNode, Diagnostics.The_expected_type_comes_from_property_0_which_is_declared_here_on_type_1, propertyName, TypeToString(target)));
            }
        }
        ReportDiagnostic(diagnostic, ref diagnosticOutput);
        return true;
    }

    internal Type? GetBestMatchIndexedAccessTypeOrUndefined(Type source, Type target, Type nameType)
    {
        var idx = GetIndexedAccessTypeOrUndefined(target, nameType, AccessFlags.None, null, null);
        if (idx != null)
        {
            return idx;
        }
        if ((target._flags & TypeFlags.Union) != 0)
        {
            var best = GetBestMatchingType(source, target, CompareTypesAssignableSimple);
            if (best != null)
            {
                return GetIndexedAccessTypeOrUndefined(best, nameType, AccessFlags.None, null, null);
            }
        }
        return null;
    }

    internal Type CheckExpressionForMutableLocationWithContextualType(Node next, Type sourcePropType)
    {
        PushContextualType(next, sourcePropType, false /*isCache*/);
        var result = CheckExpressionForMutableLocation(next, CheckMode.Contextual);
        PopContextualType();
        return result;
    }

    internal bool ElaborateArrowFunction(Node? node, Type source, Type target, Relation relation, ref Diagnostic[]? diagnosticOutput)
    {
        var functionNode = node!;
        // Don't elaborate blocks or functions with annotated parameter types
        if (IsBlock(functionNode.Body()!) || Some(functionNode.Parameters(), HasType))
        {
            return false;
        }
        var sourceSig = GetSingleCallSignature(source);
        if (sourceSig == null)
        {
            return false;
        }
        var targetSignatures = GetSignaturesOfTypeWorker(target, SignatureKind.Call) ?? [];
        if (targetSignatures.Length == 0)
        {
            return false;
        }
        var returnExpression = functionNode.Body();
        var sourceReturn = GetReturnTypeOfSignatureWorker(sourceSig);
        var targetReturn = GetUnionTypeWorker(Map(targetSignatures, GetReturnTypeOfSignatureWorker));
        if (CheckTypeRelatedTo(sourceReturn, targetReturn, relation, null /*errorNode*/))
        {
            return false;
        }
        if (returnExpression != null && ElaborateError(returnExpression, sourceReturn, targetReturn, relation, null /*headMessage*/, ref diagnosticOutput))
        {
            return true;
        }
        // Go: `var diags []*ast.Diagnostic; … &diags` → 指针非 nil、切片为 nil ⇒ 这里用非 null 空数组起步。
        Diagnostic[]? diags = [];
        CheckTypeRelatedToEx(sourceReturn, targetReturn, relation, returnExpression, null /*headMessage*/, ref diags);
        if (diags != null && diags.Length != 0)
        {
            var diagnostic = diags[0];
            if (target._symbol != null && (target._symbol.Declarations?.Length ?? 0) != 0)
            {
                diagnostic.AddRelatedInfo(CreateDiagnosticForNode(target._symbol.Declarations![0], Diagnostics.The_expected_type_comes_from_the_return_type_of_this_signature));
            }
            if ((GetFunctionFlags(node) & FunctionFlags.Async) == 0 && GetTypeOfPropertyOfTypeWorker(sourceReturn, "then") == null && CheckTypeRelatedTo(CreatePromiseType(sourceReturn), targetReturn, relation, null /*errorNode*/))
            {
                diagnostic.AddRelatedInfo(CreateDiagnosticForNode(functionNode, Diagnostics.Did_you_mean_to_mark_this_function_as_async));
            }
            ReportDiagnostic(diagnostic, ref diagnosticOutput);
            return true;
        }
        return false;
    }

    // A type is 'weak' if it is an object type with at least one optional property
    // and no required properties, call/construct signatures or index signatures
    internal bool IsWeakType(Type t)
    {
        if ((t._flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(t);
            return (resolved._signatures?.Length ?? 0) == 0 && (resolved._indexInfos?.Length ?? 0) == 0 && (resolved._properties?.Length ?? 0) > 0 && Every(resolved._properties, p => (p.Flags & SymbolFlags.Optional) != 0);
        }
        if ((t._flags & TypeFlags.Substitution) != 0)
        {
            return IsWeakType(t.AsSubstitutionType()!._baseType!);
        }
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return Every(t.Types(), IsWeakType);
        }
        return false;
    }

    internal bool HasCommonProperties(Type source, Type target, bool isComparingJsxAttributes)
    {
        foreach (var prop in GetPropertiesOfTypeWorker(source) ?? [])
        {
            if (IsKnownProperty(target, prop.Name, isComparingJsxAttributes))
            {
                return true;
            }
        }
        return false;
    }

    /**
     * Check if a property with the given name is known anywhere in the given type. In an object type, a property
     * is considered known if
     * 1. the object type is empty and the check is for assignability, or
     * 2. if the object type has index signatures, or
     * 3. if the property is actually declared in the object type
     *    (this means that 'toString', for example, is not usually a known property).
     * 4. In a union or intersection type,
     *    a property is considered known if it is known in any constituent type.
     * @param targetType a type to search a given name in
     * @param name a property name to search
     * @param isComparingJsxAttributes a boolean flag indicating whether we are searching in JsxAttributesType
     */
    internal bool IsKnownProperty(Type targetType, string name, bool isComparingJsxAttributes)
    {
        if ((targetType._flags & TypeFlags.Object) != 0)
        {
            // For backwards compatibility a symbol-named property is satisfied by a string index signature. This
            // is incorrect and inconsistent with element access expressions, where it is an error, so eventually
            // we should remove this exception.
            if (GetPropertyOfObjectType(targetType, name) != null ||
                GetApplicableIndexInfoForName(targetType, name) != null ||
                IsLateBoundName(name) && GetIndexInfoOfTypeWorker(targetType, _stringType) != null ||
                isComparingJsxAttributes && IsHyphenatedJsxName(name))
            {
                // For JSXAttributes, if the attribute has a hyphenated name, consider that the attribute to be known.
                return true;
            }
        }
        if ((targetType._flags & TypeFlags.Substitution) != 0)
        {
            return IsKnownProperty(targetType.AsSubstitutionType()!._baseType!, name, isComparingJsxAttributes);
        }
        if ((targetType._flags & TypeFlags.UnionOrIntersection) != 0 && IsExcessPropertyCheckTarget(targetType))
        {
            foreach (var t in targetType.Types())
            {
                if (IsKnownProperty(t, name, isComparingJsxAttributes))
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal static bool IsHyphenatedJsxName(string name)
    {
        return name.Contains('-');
    }

    internal static bool IsExcessPropertyCheckTarget(Type t)
    {
        return (t._flags & TypeFlags.Object) != 0 && (t._objectFlags & ObjectFlags.ObjectLiteralPatternWithComputedProperties) == 0 ||
            (t._flags & TypeFlags.NonPrimitive) != 0 ||
            (t._flags & TypeFlags.Substitution) != 0 && IsExcessPropertyCheckTarget(t.AsSubstitutionType()!._baseType!) ||
            (t._flags & TypeFlags.Union) != 0 && Some(t.Types(), IsExcessPropertyCheckTarget) ||
            (t._flags & TypeFlags.Intersection) != 0 && Every(t.Types(), IsExcessPropertyCheckTarget);
    }

    // Return true if the given type is deeply nested. We consider this to be the case when the given stack contains
    // maxDepth or more occurrences of types with the same recursion identity as the given type. The recursion identity
    // provides a shared identity for type instantiations that repeat in some (possibly infinite) pattern. For example,
    // in `type Deep<T> = { next: Deep<Deep<T>> }`, repeatedly referencing the `next` property leads to an infinite
    // sequence of ever deeper instantiations with the same recursion identity (in this case the symbol associated with
    // the object type literal).
    // A homomorphic mapped type is considered deeply nested if its target type is deeply nested, and an intersection is
    // considered deeply nested if any constituent of the intersection is deeply nested.
    // It is possible, though highly unlikely, for the deeply nested check to be true in a situation where a chain of
    // instantiations is not infinitely expanding. Effectively, we will generate a false positive when two types are
    // structurally equal to at least maxDepth levels, but unequal at some level beyond that.
    internal bool IsDeeplyNestedType(Type t, List<Type> stack, int maxDepth)
    {
        if (stack.Count >= maxDepth)
        {
            var target = GetRecursionIdentityTarget(t);
            if ((target._flags & TypeFlags.Intersection) != 0)
            {
                foreach (var type in target.Types())
                {
                    if (IsDeeplyNestedType(type, stack, maxDepth))
                    {
                        return true;
                    }
                }
            }
            else
            {
                var identity = GetRecursionIdentityFromTarget(target);
                var count = 0;
                var lastTypeId = new TypeId(0);
                foreach (var type in stack)
                {
                    if (HasMatchingRecursionIdentity(type, identity))
                    {
                        // We only count occurrences with a higher type id than the previous occurrence, since higher
                        // type ids are an indicator of newer instantiations caused by recursion.
                        if (type._id.Value >= lastTypeId.Value)
                        {
                            count++;
                            if (count >= maxDepth)
                            {
                                return true;
                            }
                        }
                        lastTypeId = type._id;
                    }
                }
            }
        }
        return false;
    }

    internal static bool HasMatchingRecursionIdentity(Type t, RecursionId identity)
    {
        var target = GetRecursionIdentityTarget(t);
        if ((target._flags & TypeFlags.Intersection) != 0)
        {
            foreach (var type in target.Types())
            {
                if (HasMatchingRecursionIdentity(type, identity))
                {
                    return true;
                }
            }
            return false;
        }
        return GetRecursionIdentityFromTarget(target) == identity;
    }

    internal static RecursionId GetRecursionIdentity(Type t)
    {
        return GetRecursionIdentityFromTarget(GetRecursionIdentityTarget(t));
    }

    // Get the recursion identity target type from a type. Recursively (a) obtain the target object type of an
    // indexed access (i.e. the T in T[K]), and (b) unwrap nested homomorphic mapped types and return the deepest
    // target type that has a symbol. The unwrapping better preserves unique type identities for mapped types applied
    // to explicitly written object literals. For example in `Mapped<{ x: Mapped<{ x: Mapped<{ x: string }>}>}>`,
    // each of the mapped type applications will have a unique recursion identity (that of their target object type
    // literal) and thus avoid appearing deeply nested.
    internal static Type GetRecursionIdentityTarget(Type t)
    {
        if ((t._flags & TypeFlags.IndexedAccess) != 0)
        {
            return GetRecursionIdentityTarget(t.AsIndexedAccessType()!._objectType!);
        }
        if ((t._objectFlags & ObjectFlags.InstantiatedMapped) == ObjectFlags.InstantiatedMapped)
        {
            var target = t._checker.GetModifiersTypeFromMappedType(t);
            if (target != null && (target._symbol != null || (target._flags & TypeFlags.Intersection) != 0 &&
                Some(target.Types(), static type => type._symbol != null)))
            {
                return GetRecursionIdentityTarget(target);
            }
        }
        return t;
    }

    // The recursion identity of a type is an object identity that is shared among multiple instantiations of the type.
    // We track recursion identities in order to identify deeply nested and possibly infinite type instantiations with
    // the same origin. For example, when type parameters are in scope in an object type such as { x: T }, all
    // instantiations of that type have the same recursion identity. The default recursion identity is the object
    // identity of the type, meaning that every type is unique. Generally, types with constituents that could circularly
    // reference the type have a recursion identity that differs from the object identity.
    internal static RecursionId GetRecursionIdentityFromTarget(Type t)
    {
        // Object and array literals are known not to contain recursive references and don't need a recursion identity.
        if ((t._flags & TypeFlags.Object) != 0 && !IsObjectOrArrayLiteralType(t))
        {
            var typeReference = t.AsTypeReference();
            var typeReferenceNode = typeReference?._node;
            if ((t._objectFlags & ObjectFlags.Reference) != 0 && typeReferenceNode != null)
            {
                // Deferred type references are tracked through their associated AST node. This gives us finer
                // granularity than using their associated target because each manifest type reference has a
                // unique AST node.
                return AsRecursionId(typeReferenceNode);
            }
            if (t._symbol != null && !((t._objectFlags & ObjectFlags.Anonymous) != 0 && (t._symbol.Flags & SymbolFlags.Class) != 0) && (t._objectFlags & ObjectFlags.FromTypeNode) == 0)
            {
                // We track object types that have a symbol by that symbol (representing the origin of the type), but
                // exclude the static sides of classes (since they share their symbols with the instance sides) and type
                // references that originate in resolution of AST type nodes (since such type nodes cannot be the source
                // of generative recursion without first being instantiated).
                return AsRecursionId(t._symbol);
            }
            if (IsTupleTypeWorker(t) && (t._objectFlags & ObjectFlags.FromTypeNode) == 0)
            {
                return AsRecursionId(t.Target());
            }
        }
        if ((t._flags & TypeFlags.TypeParameter) != 0 && t._symbol != null)
        {
            // We use the symbol of the type parameter such that all "fresh" instantiations of that type parameter
            // have the same recursion identity.
            return AsRecursionId(t._symbol);
        }
        if ((t._flags & TypeFlags.Conditional) != 0)
        {
            // The root object represents the origin of the conditional type
            return AsRecursionId(t.AsConditionalType()!._root!._node!.AsNode());
        }
        return AsRecursionId(t);
    }

    internal Type? GetBestMatchingType(Type source, Type target, TypeRelatedComparer isRelatedTo)
    {
        var t = FindMatchingDiscriminantType(source, target, isRelatedTo);
        if (t != null)
        {
            return t;
        }
        t = FindMatchingTypeReferenceOrTypeAliasReference(source, target);
        if (t != null)
        {
            return t;
        }
        t = FindBestTypeForObjectLiteral(source, target);
        if (t != null)
        {
            return t;
        }
        t = FindBestTypeForInvokable(source, target, SignatureKind.Call);
        if (t != null)
        {
            return t;
        }
        t = FindBestTypeForInvokable(source, target, SignatureKind.Construct);
        if (t != null)
        {
            return t;
        }
        return FindMostOverlappyType(source, target);
    }

    internal Type? FindMatchingTypeReferenceOrTypeAliasReference(Type source, Type unionTarget)
    {
        var sourceObjectFlags = source._objectFlags;
        if ((sourceObjectFlags & (ObjectFlags.Reference | ObjectFlags.Anonymous)) != 0 && (unionTarget._flags & TypeFlags.Union) != 0)
        {
            foreach (var target in unionTarget.Types())
            {
                if ((target._flags & TypeFlags.Object) != 0)
                {
                    var overlapObjFlags = sourceObjectFlags & target._objectFlags;
                    if ((overlapObjFlags & ObjectFlags.Reference) != 0 && source.Target() == target.Target())
                    {
                        return target;
                    }
                    if ((overlapObjFlags & ObjectFlags.Anonymous) != 0 && source._alias != null && target._alias != null && source._alias._symbol == target._alias._symbol)
                    {
                        return target;
                    }
                }
            }
        }
        return null;
    }

    internal Type? FindBestTypeForInvokable(Type source, Type unionTarget, SignatureKind kind)
    {
        if ((GetSignaturesOfTypeWorker(source, kind) ?? []).Length != 0)
        {
            return Find(unionTarget.Types(), t => (GetSignaturesOfTypeWorker(t, kind) ?? []).Length != 0);
        }
        return null;
    }

    internal Type? FindMostOverlappyType(Type source, Type unionTarget)
    {
        Type? bestMatch = null;
        if ((source._flags & (TypeFlags.Primitive | TypeFlags.InstantiablePrimitive)) == 0)
        {
            var matchingCount = 0;
            foreach (var target in unionTarget.Types())
            {
                if ((target._flags & (TypeFlags.Primitive | TypeFlags.InstantiablePrimitive)) == 0)
                {
                    var overlap = GetIntersectionType([GetIndexType(source), GetIndexType(target)]);
                    if ((overlap._flags & TypeFlags.Index) != 0)
                    {
                        // perfect overlap of keys
                        return target;
                    }
                    else if (IsUnitType(overlap) || (overlap._flags & TypeFlags.Union) != 0)
                    {
                        // We only want to account for literal types otherwise.
                        // If we have a union of index types, it seems likely that we
                        // needed to elaborate between two generic mapped types anyway.
                        var length = 1;
                        if ((overlap._flags & TypeFlags.Union) != 0)
                        {
                            length = CountWhere(overlap.Types(), IsUnitType);
                        }
                        if (length >= matchingCount)
                        {
                            bestMatch = target;
                            matchingCount = length;
                        }
                    }
                }
            }
        }
        return bestMatch;
    }

    internal Type? FindBestTypeForObjectLiteral(Type source, Type unionTarget)
    {
        if ((source._objectFlags & ObjectFlags.ObjectLiteral) != 0 && SomeType(unionTarget, IsArrayLikeTypeWorker))
        {
            return Find(unionTarget.Types(), t => !IsArrayLikeTypeWorker(t));
        }
        return null;
    }

    internal bool ShouldReportUnmatchedPropertyError(Type source, Type target)
    {
        var typeCallSignatures = GetSignaturesOfStructuredType(source, SignatureKind.Call);
        var typeConstructSignatures = GetSignaturesOfStructuredType(source, SignatureKind.Construct);
        var typeProperties = GetPropertiesOfObjectType(source);
        if (((typeCallSignatures?.Length ?? 0) != 0 || (typeConstructSignatures?.Length ?? 0) != 0) && (typeProperties?.Length ?? 0) == 0)
        {
            if (((GetSignaturesOfTypeWorker(target, SignatureKind.Call) ?? []).Length != 0 && (typeCallSignatures?.Length ?? 0) != 0) ||
                (GetSignaturesOfTypeWorker(target, SignatureKind.Construct) ?? []).Length != 0 && (typeConstructSignatures?.Length ?? 0) != 0)
            {
                // target has similar signature kinds to source, still focus on the unmatched property
                return true;
            }
            return false;
        }
        return true;
    }

    internal Symbol? GetUnmatchedProperty(Type source, Type target, bool requireOptionalProperties, bool matchDiscriminantProperties)
    {
        return GetUnmatchedPropertiesWorker(source, target, requireOptionalProperties, matchDiscriminantProperties, null);
    }

    internal Symbol[] GetUnmatchedProperties(Type source, Type target, bool requireOptionalProperties, bool matchDiscriminantProperties)
    {
        var props = new List<Symbol>();
        GetUnmatchedPropertiesWorker(source, target, requireOptionalProperties, matchDiscriminantProperties, props);
        return props.ToArray();
    }

    internal Symbol? GetUnmatchedPropertiesWorker(Type source, Type target, bool requireOptionalProperties, bool matchDiscriminantProperties, List<Symbol>? propsOut)
    {
        var properties = GetPropertiesOfTypeWorker(target) ?? [];
        foreach (var targetProp in properties)
        {
            // TODO: remove this when we support static private identifier fields and find other solutions to get privateNamesAndStaticFields test to pass
            if (IsStaticPrivateIdentifierProperty(targetProp))
            {
                continue;
            }
            if (requireOptionalProperties || (targetProp.Flags & SymbolFlags.Optional) == 0 && (targetProp.CheckFlags & CheckFlags.Partial) == 0)
            {
                var sourceProp = GetPropertyOfTypeWorker(source, targetProp.Name);
                if (sourceProp == null)
                {
                    if (propsOut == null)
                    {
                        return targetProp;
                    }
                    propsOut.Add(targetProp);
                }
                else if (matchDiscriminantProperties)
                {
                    var targetType = GetTypeOfSymbolWorker(targetProp);
                    if ((targetType._flags & TypeFlags.Unit) != 0)
                    {
                        var sourceType = GetTypeOfSymbolWorker(sourceProp);
                        if (!((sourceType._flags & TypeFlags.Any) != 0 || GetRegularTypeOfLiteralType(sourceType) == GetRegularTypeOfLiteralType(targetType)))
                        {
                            if (propsOut == null)
                            {
                                return targetProp;
                            }
                            propsOut.Add(targetProp);
                        }
                    }
                }
            }
        }
        return null;
    }

    internal static Symbol[] ExcludeProperties(Symbol[] properties, Set<string> excludedProperties)
    {
        if (excludedProperties.Len() == 0 || properties.Length == 0)
        {
            return properties;
        }
        List<Symbol>? reduced = null;
        var excluded = false;
        for (var i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            if (!excludedProperties.Has(prop.Name))
            {
                if (excluded)
                {
                    reduced ??= [];
                    reduced.Add(prop);
                }
            }
            else if (!excluded)
            {
                reduced = [.. properties[..i]];
                excluded = true;
            }
        }
        if (excluded)
        {
            return reduced!.ToArray();
        }
        return properties;
    }

    // Keep this up-to-date with the same logic within `getApparentTypeOfContextualType`, since they should behave similarly
    internal Type? FindMatchingDiscriminantType(Type source, Type target, TypeRelatedComparer isRelatedTo)
    {
        if ((target._flags & TypeFlags.Union) != 0 && (source._flags & (TypeFlags.Intersection | TypeFlags.Object)) != 0)
        {
            var match = GetMatchingUnionConstituentForType(target, source);
            if (match != null)
            {
                return match;
            }
            var discriminantProperties = FindDiscriminantProperties(GetPropertiesOfTypeWorker(source) ?? [], target);
            if (discriminantProperties.Length != 0)
            {
                var discriminator = new TypeDiscriminator { _c = this, _props = discriminantProperties, _isRelatedTo = isRelatedTo };
                var discriminated = DiscriminateTypeByDiscriminableItems(target, discriminator);
                if (discriminated != target)
                {
                    return discriminated;
                }
            }
        }
        return null;
    }

    internal Symbol[] FindDiscriminantProperties(Symbol[] sourceProperties, Type target)
    {
        var result = new List<Symbol>();
        foreach (var sourceProperty in sourceProperties)
        {
            if (IsDiscriminantProperty(target, sourceProperty.Name))
            {
                result.Add(sourceProperty);
            }
        }
        return result.ToArray();
    }

    internal bool IsDiscriminantProperty(Type? t, string name)
    {
        if (t != null && (t._flags & TypeFlags.Union) != 0)
        {
            var prop = GetUnionOrIntersectionProperty(t, name, false /*skipObjectFunctionPropertyAugment*/);
            if (prop != null && (prop.CheckFlags & CheckFlags.SyntheticProperty) != 0)
            {
                if ((prop.CheckFlags & CheckFlags.IsDiscriminantComputed) == 0)
                {
                    prop.CheckFlags |= CheckFlags.IsDiscriminantComputed;
                    if ((prop.CheckFlags & CheckFlags.NonUniformAndLiteral) == CheckFlags.NonUniformAndLiteral && !IsGenericType(GetTypeOfSymbolWorker(prop)))
                    {
                        prop.CheckFlags |= CheckFlags.IsDiscriminant;
                    }
                }
                return (prop.CheckFlags & CheckFlags.IsDiscriminant) != 0;
            }
        }
        return false;
    }

    internal Type? GetMatchingUnionConstituentForType(Type unionType, Type t)
    {
        var keyPropertyName = GetKeyPropertyName(unionType);
        if (keyPropertyName == "")
        {
            return null;
        }
        var propType = GetTypeOfPropertyOfTypeWorker(t, keyPropertyName);
        if (propType == null)
        {
            return null;
        }
        return GetConstituentTypeForKeyType(unionType, propType);
    }

    // Return the name of a discriminant property for which it was possible and feasible to construct a map of
    // constituent types keyed by the literal types of the property by that name in each constituent type. Return
    // an empty string if no such discriminant property exists.
    internal string GetKeyPropertyName(Type t)
    {
        var u = t.AsUnionType()!;
        if (u._keyPropertyName == "")
        {
            (u._keyPropertyName, u._constituentMap) = ComputeKeyPropertyNameAndMap(t);
        }
        if (u._keyPropertyName == InternalSymbolNameMissing)
        {
            return "";
        }
        return u._keyPropertyName;
    }

    // Given a union type for which getKeyPropertyName returned a non-empty string, return the constituent
    // that corresponds to the given key type for that property name.
    internal Type? GetConstituentTypeForKeyType(Type t, Type keyType)
    {
        var map = t.AsUnionType()!._constituentMap;
        Type? result = null;
        if (map != null && map.TryGetValue(GetRegularTypeOfLiteralType(keyType), out var found))
        {
            result = found;
        }
        if (result != _unknownType)
        {
            return result;
        }
        return null;
    }

    internal (string KeyPropertyName, Dictionary<Type, Type>? Map) ComputeKeyPropertyNameAndMap(Type t)
    {
        var types = t.Types();
        if (types.Length < 10 || (t._objectFlags & ObjectFlags.PrimitiveUnion) != 0 || CountWhere(types, IsObjectOrInstantiableNonPrimitive) < 10)
        {
            return (InternalSymbolNameMissing, null);
        }
        var keyPropertyName = GetKeyPropertyCandidateName(types);
        if (keyPropertyName == "")
        {
            return (InternalSymbolNameMissing, null);
        }
        var mapByKeyProperty = MapTypesByKeyProperty(types, keyPropertyName);
        if (mapByKeyProperty == null)
        {
            return (InternalSymbolNameMissing, null);
        }
        return (keyPropertyName, mapByKeyProperty);
    }

    internal static bool IsObjectOrInstantiableNonPrimitive(Type t)
    {
        return (t._flags & (TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0;
    }

    internal string GetKeyPropertyCandidateName(Type[] types)
    {
        foreach (var t in types)
        {
            if ((t._flags & (TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0)
            {
                foreach (var p in GetPropertiesOfTypeWorker(t) ?? [])
                {
                    if (IsUnitType(GetTypeOfSymbolWorker(p)))
                    {
                        return p.Name;
                    }
                }
            }
        }
        return "";
    }

    // Given a set of constituent types and a property name, create and return a map keyed by the literal
    // types of the property by that name in each constituent type. No map is returned if some key property
    // has a non-literal type or if less than 10 or less than 50% of the constituents have a unique key.
    // Entries with duplicate keys have unknownType as the value.
    internal Dictionary<Type, Type>? MapTypesByKeyProperty(Type[] types, string keyPropertyName)
    {
        var typesByKey = new Dictionary<Type, Type>();
        var count = 0;
        foreach (var t in types)
        {
            if ((t._flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0)
            {
                var discriminant = GetTypeOfPropertyOfTypeWorker(t, keyPropertyName);
                if (discriminant == null || !IsLiteralType(discriminant))
                {
                    return null;
                }
                var duplicate = false;
                foreach (var d in discriminant.Distributed())
                {
                    var key = GetRegularTypeOfLiteralType(d);
                    if (!typesByKey.TryGetValue(key, out var existing))
                    {
                        typesByKey[key] = t;
                    }
                    else if (existing != _unknownType)
                    {
                        typesByKey[key] = _unknownType;
                        duplicate = true;
                    }
                }
                if (!duplicate)
                {
                    count++;
                }
            }
        }
        if (count >= 10 && count * 2 >= types.Length)
        {
            return typesByKey;
        }
        return null;
    }
}

/// <summary>Go: <c>type TypeDiscriminator struct</c>（relater.go:1030；实现 <see cref="Discriminator"/>）。</summary>
public sealed class TypeDiscriminator : Discriminator
{
    internal Checker _c = null!;
    internal Symbol[] _props = [];
    internal TypeRelatedComparer _isRelatedTo = null!;

    public int Len()
    {
        return _props.Length;
    }

    public string Name(int index)
    {
        return _props[index].Name;
    }

    public bool Matches(int index, Type t)
    {
        var propType = _c.GetTypeOfSymbolWorker(_props[index]);
        foreach (var s in propType.Distributed())
        {
            if (_isRelatedTo(s, t) != Ternary.False)
            {
                return true;
            }
        }
        return false;
    }
}
