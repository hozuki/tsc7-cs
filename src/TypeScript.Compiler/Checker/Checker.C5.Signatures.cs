// ported from: internal/checker/checker.go:19963-20338（索引签名信息/属性名判定/签名构造/late-bindable 判定）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) **Worker 后缀**（artifacts/checker-method-collisions.md）：Go 未导出的 `getSignatureFromDeclaration`（本文件 20174）
//    与导出的 `GetSignatureFromDeclaration`（services.go:1120）同名 → C# 里本文件这份叫 `GetSignatureFromDeclarationWorker`。
//    本文件调用的包级 `findIndexInfo`（checker.go:19496，不在本文件）同样叫 `FindIndexInfoWorker`；本文件里带接收者的
//    实例方法 `findIndexInfo`（20131）保留 `FindIndexInfo`（两个名字不冲突）。
// 2) Go 用 `append` 构建的本地切片 → C# `List<T>`；Go 里返回 nil 的 `[]T` → C# 返回空数组（docs/architecture.md §3.2：
//    "nil slice 语义等同时用 []"）。已核对本窗口全部调用点（checker.go:19956-19958、21003-21005、29019、
//    emitresolver.go:479/1072）只用 `len()`/`range`/赋值给缓存字段，nil 与空数组语义完全相同。
//    需要把本地列表传给收 `T[]` 的函数时用 `ToArray()`。
// 3) `slices.Collect(maps.Values(m))`（Go 1.23 迭代器，**顺序未定义**）→ `m.Values.ToArray()`（Dictionary 顺序）。
//    只影响 `GetIndexInfosOfIndexSymbol` 里 `propertySymbols` 的遍历顺序（进而影响合并索引信息的联合类型成员顺序），
//    Go 侧本身就不保证顺序。
// 4) `c.resolveName` 在 Go 里是 Checker 的**函数字段**（checker.go:661，`createNameResolver().Resolve` 赋值）
//    → C# 的 `_resolveName` 委托字段（C1 所有者），调用形式与 Go 相同。
// 5) 字段访问：`links.resolvedSignature` → `links._resolvedSignature`、`info.keyType`/`t.flags` → `_keyType`/`_flags`
//    （docs/checker-plan.md §5.5）；Checker 的 `c.anyType`/`c.stringNumberSymbolType`/… → `_anyType`/`_stringNumberSymbolType`/…。
// 6) Go 的 `Symbol` 访问器（param/declaration/classType 等都经 `Symbol` 方法取）在 C# 里是**属性** `x.Symbol`（无括号；
//    Ast/Node.cs 文件头：名字与返回类型名相同者落成属性，避免方法遮蔽类型名）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal IndexInfo[] GetIndexInfosOfSymbol(Symbol symbol)
    {
        var indexSymbol = GetIndexSymbol(symbol);
        if (indexSymbol is not null)
        {
            return GetIndexInfosOfIndexSymbol(indexSymbol, GetMembersOfSymbol(symbol)?.Values.ToArray() ?? []);
        }

        return [];
    }

    // note intentional similarities to index signature building in `checkObjectLiteral` for parity
    internal IndexInfo[] GetIndexInfosOfIndexSymbol(Symbol indexSymbol, Symbol[] siblingSymbols)
    {
        var indexInfos = new List<IndexInfo>();
        var hasComputedStringProperty = false;
        var hasComputedNumberProperty = false;
        var hasComputedSymbolProperty = false;
        var readonlyComputedStringProperty = true;
        var readonlyComputedNumberProperty = true;
        var readonlyComputedSymbolProperty = true;
        var propertySymbols = new List<Symbol>();
        foreach (var declaration in indexSymbol.Declarations ?? [])
        {
            if (IsIndexSignatureDeclaration(declaration))
            {
                var parameters = declaration.Parameters();
                var returnTypeNode = declaration.Type();
                if (parameters?.Length == 1)
                {
                    var typeNode = parameters[0].Type();
                    if (typeNode is not null)
                    {
                        var valueType = _anyType;
                        if (returnTypeNode is not null)
                        {
                            valueType = GetTypeFromTypeNodeWorker(returnTypeNode);
                        }

                        ForEachType(GetTypeFromTypeNodeWorker(typeNode), keyType =>
                        {
                            if (IsValidIndexKeyType(keyType) && FindIndexInfoWorker(indexInfos.ToArray(), keyType) is null)
                            {
                                var indexInfo = NewIndexInfo(keyType, valueType, HasModifier(declaration, ModifierFlags.Readonly), declaration, null);
                                indexInfos.Add(indexInfo);
                            }
                        });
                    }
                }
            }
            else if (HasLateBindableIndexSignature(declaration))
            {
                Node? declName;
                if (IsBinaryExpression(declaration))
                {
                    declName = declaration.AsBinaryExpression()!.Left;
                }
                else
                {
                    declName = declaration.Name();
                }

                Type keyType;
                if (IsElementAccessExpression(declName!))
                {
                    keyType = CheckExpressionCached(declName!.AsElementAccessExpression()!.ArgumentExpression!);
                }
                else
                {
                    keyType = CheckComputedPropertyName(declName!);
                }

                if (FindIndexInfoWorker(indexInfos.ToArray(), keyType) is not null)
                {
                    continue;
                    // Explicit index for key type takes priority
                }

                if (IsTypeAssignableToWorker(keyType, _stringNumberSymbolType))
                {
                    if (IsTypeAssignableToWorker(keyType, _numberType))
                    {
                        hasComputedNumberProperty = true;
                        if (!HasReadonlyModifier(declaration))
                        {
                            readonlyComputedNumberProperty = false;
                        }
                    }
                    else if (IsTypeAssignableToWorker(keyType, _esSymbolType))
                    {
                        hasComputedSymbolProperty = true;
                        if (!HasReadonlyModifier(declaration))
                        {
                            readonlyComputedSymbolProperty = false;
                        }
                    }
                    else
                    {
                        hasComputedStringProperty = true;
                        if (!HasReadonlyModifier(declaration))
                        {
                            readonlyComputedStringProperty = false;
                        }
                    }

                    propertySymbols.Add(declaration.Symbol!);
                }
            }
        }

        if (hasComputedStringProperty || hasComputedNumberProperty || hasComputedSymbolProperty)
        {
            foreach (var sym in siblingSymbols)
            {
                if (sym != indexSymbol)
                {
                    propertySymbols.Add(sym);
                }
            }

            // aggregate similar index infos implied to be the same key to the same combined index info
            if (hasComputedStringProperty && FindIndexInfoWorker(indexInfos.ToArray(), _stringType) is null)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(readonlyComputedStringProperty, propertySymbols.ToArray(), _stringType));
            }

            if (hasComputedNumberProperty && FindIndexInfoWorker(indexInfos.ToArray(), _numberType) is null)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(readonlyComputedNumberProperty, propertySymbols.ToArray(), _numberType));
            }

            if (hasComputedSymbolProperty && FindIndexInfoWorker(indexInfos.ToArray(), _esSymbolType) is null)
            {
                indexInfos.Add(GetObjectLiteralIndexInfo(readonlyComputedSymbolProperty, propertySymbols.ToArray(), _esSymbolType));
            }
        }

        return indexInfos.ToArray();
    }

    // NOTE: currently does not make pattern literal indexers, eg `${number}px`
    internal IndexInfo GetObjectLiteralIndexInfo(bool isReadonly, Symbol[] properties, Type keyType)
    {
        var propTypes = new List<Type>();
        var components = new List<Node>();
        foreach (var prop in properties)
        {
            if (keyType == _stringType && !IsSymbolWithSymbolName(prop) ||
                keyType == _numberType && IsSymbolWithNumericName(prop) ||
                keyType == _esSymbolType && IsSymbolWithSymbolName(prop))
            {
                propTypes.Add(GetTypeOfSymbolWorker(prop));
                if (IsSymbolWithComputedName(prop))
                {
                    components.Add(prop.Declarations![0]);
                }
            }
        }

        var unionType = _undefinedType;
        if (propTypes.Count != 0)
        {
            unionType = GetUnionTypeExWorker(propTypes.ToArray(), UnionReduction.Subtype, null, null);
        }

        return NewIndexInfo(keyType, unionType, isReadonly, null /*declaration*/, components.Count != 0 ? components.ToArray() : null);
    }

    internal bool IsSymbolWithSymbolName(Symbol symbol)
    {
        if (IsKnownSymbol(symbol))
        {
            return true;
        }

        if ((symbol.Declarations?.Length ?? 0) != 0)
        {
            var name = symbol.Declarations![0].Name();
            return name is not null && IsComputedPropertyName(name) && IsTypeAssignableToKind(CheckComputedPropertyName(name), TypeFlags.ESSymbol);
        }

        return false;
    }

    internal bool IsSymbolWithNumericName(Symbol symbol)
    {
        if (IsNumericLiteralName(symbol.Name))
        {
            return true;
        }

        if ((symbol.Declarations?.Length ?? 0) != 0)
        {
            var name = symbol.Declarations![0].Name();
            return name is not null && IsNumericName(name);
        }

        return false;
    }

    internal bool IsSymbolWithComputedName(Symbol symbol)
    {
        if ((symbol.Declarations?.Length ?? 0) != 0)
        {
            var name = symbol.Declarations![0].Name();
            return name is not null && IsComputedPropertyName(name);
        }

        return false;
    }

    internal bool IsNumericName(Node name)
    {
        switch (name.Kind)
        {
            case Kind.ComputedPropertyName:
                return IsNumericComputedName(name);
            case Kind.Identifier:
            case Kind.NumericLiteral:
            case Kind.StringLiteral:
                return IsNumericLiteralName(name.Text());
        }

        return false;
    }

    internal bool IsNumericComputedName(Node name)
    {
        // It seems odd to consider an expression of type Any to result in a numeric name,
        // but this behavior is consistent with checkIndexedAccess
        return IsTypeAssignableToKind(CheckComputedPropertyName(name), TypeFlags.NumberLike);
    }

    internal bool IsValidIndexKeyType(Type t)
    {
        return (t._flags & (TypeFlags.String | TypeFlags.Number | TypeFlags.ESSymbol)) != 0 ||
            IsPatternLiteralType(t) ||
            (t._flags & TypeFlags.Intersection) != 0 && !IsGenericType(t) && Some(t.Types(), keyType => IsValidIndexKeyType(keyType));
    }

    internal IndexInfo? FindIndexInfo(IndexInfo[] indexInfos, Type keyType)
    {
        foreach (var info in indexInfos)
        {
            if (info._keyType == keyType)
            {
                return info;
            }
        }

        return null;
    }

    internal Symbol? GetIndexSymbol(Symbol symbol)
    {
        return GetMembersOfSymbol(symbol)?.GetValueOrDefault(InternalSymbolNameIndex);
    }

    internal Signature[] GetSignaturesOfSymbol(Symbol? symbol)
    {
        if (symbol is null)
        {
            return [];
        }

        var result = new List<Signature>();
        var declarations = symbol.Declarations ?? [];
        for (var i = 0; i < declarations.Length; i++)
        {
            var decl = declarations[i];
            if (!IsFunctionLike(decl))
            {
                continue;
            }

            // Don't include signature if node is the implementation of an overloaded function. A node is considered
            // an implementation node if it has a body and the previous node is of the same kind and immediately
            // precedes the implementation node (i.e. has the same parent and ends where the implementation starts).
            if (i > 0 && decl.Body() is not null)
            {
                var previous = declarations[i - 1];
                if (ReferenceEquals(decl.Parent, previous.Parent) && decl.Kind == previous.Kind &&
                    (decl.Pos() == previous.End() || (previous.Flags & NodeFlags.Reparsed) != 0))
                {
                    continue;
                }
            }

            // If this is a function or method declaration, get the signature from the @type tag for the sake of optional parameters.
            // Exclude contextually-typed kinds because we already apply the @type tag to the context, plus applying it here to the initializer would suppress checks that the two are compatible.
            var sig = GetSignatureOfFullSignatureType(decl);
            if (sig is null)
            {
                sig = GetSignatureFromDeclarationWorker(decl);
            }

            result.Add(sig);
        }

        return result.ToArray();
    }

    internal Signature GetSignatureFromDeclarationWorker(Node declaration)
    {
        var links = _signatureLinks.Get(declaration);
        if (links._resolvedSignature is not null)
        {
            return links._resolvedSignature;
        }

        var parameters = new List<Symbol>();
        var flags = SignatureFlags.None;
        Symbol? thisParameter = null;
        var minArgumentCount = 0;
        var hasThisParameter = false;
        var iife = GetImmediatelyInvokedFunctionExpression(declaration);
        var isUntypedSignatureInJSFile = iife is null &&
            IsInJSFile(declaration) &&
            (IsFunctionExpression(declaration) || IsArrowFunction(declaration) || IsMethodOrAccessor(declaration) || IsFunctionDeclaration(declaration) || IsConstructorDeclaration(declaration)) &&
            Every(declaration.Parameters(), param => param.Type() is null) &&
            GetContextualTypeWorker(declaration, ContextFlags.Signature) is null;
        if (isUntypedSignatureInJSFile)
        {
            flags |= SignatureFlags.IsUntypedSignatureInJSFile;
        }

        var declarationParameters = declaration.Parameters() ?? [];
        for (var i = 0; i < declarationParameters.Length; i++)
        {
            var param = declarationParameters[i];
            var paramSymbol = param.Symbol;
            var typeNode = param.Type();
            // Include parameter symbol instead of property symbol in the signature
            if (paramSymbol is not null && (paramSymbol.Flags & SymbolFlags.Property) != 0 && !IsBindingPattern(param.Name()))
            {
                var resolvedSymbol = _resolveName(param, paramSymbol.Name, SymbolFlags.Value, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/);
                paramSymbol = resolvedSymbol;
            }

            if (i == 0 && paramSymbol!.Name == InternalSymbolNameThis)
            {
                hasThisParameter = true;
                thisParameter = param.Symbol;
            }
            else
            {
                parameters.Add(paramSymbol!);
            }

            if (typeNode is not null && typeNode.Kind == Kind.LiteralType)
            {
                flags |= SignatureFlags.HasLiteralTypes;
            }

            // Record a new minimum argument count if this is not an optional parameter
            var isOptionalParameter = IsOptionalDeclaration(param) ||
                param.Initializer() is not null ||
                IsRestParameter(param) ||
                (iife is not null && parameters.Count > (iife.Arguments()?.Length ?? 0) && typeNode is null);
            if (!isOptionalParameter)
            {
                minArgumentCount = parameters.Count;
            }
        }

        // If only one accessor includes a this-type annotation, the other behaves as if it had the same type annotation
        if ((IsGetAccessorDeclaration(declaration) || IsSetAccessorDeclaration(declaration)) && HasBindableName(declaration) && (!hasThisParameter || thisParameter is null))
        {
            var otherKind = IfElse(IsGetAccessorDeclaration(declaration), Kind.SetAccessor, Kind.GetAccessor);
            var other = GetDeclarationOfKind(GetSymbolOfDeclaration(declaration), otherKind);
            if (other is not null)
            {
                thisParameter = GetAnnotatedAccessorThisParameter(other);
            }
        }

        Type? classType = null;
        if (IsConstructorDeclaration(declaration))
        {
            classType = GetDeclaredTypeOfClassOrInterface(GetMergedSymbolWorker(declaration.Parent!.Symbol!)!);
        }

        Type[] typeParameters;
        if (classType is not null)
        {
            typeParameters = classType.AsInterfaceType()!.LocalTypeParameters();
        }
        else
        {
            typeParameters = GetTypeParametersFromDeclaration(declaration);
        }

        if (HasRestParameter(declaration))
        {
            flags |= SignatureFlags.HasRestParameter;
        }

        if (IsConstructorTypeNode(declaration) || IsConstructorDeclaration(declaration) || IsConstructSignatureDeclaration(declaration))
        {
            flags |= SignatureFlags.Construct;
        }

        if (IsConstructorTypeNode(declaration) && HasSyntacticModifier(declaration, ModifierFlags.Abstract) ||
            IsConstructorDeclaration(declaration) && HasSyntacticModifier(declaration.Parent!, ModifierFlags.Abstract))
        {
            flags |= SignatureFlags.Abstract;
        }

        links._resolvedSignature = NewSignature(flags, declaration, typeParameters, thisParameter, parameters.ToArray(), null /*resolvedReturnType*/, null /*resolvedTypePredicate*/, minArgumentCount);
        return links._resolvedSignature!;
    }

    internal Type[] GetTypeParametersFromDeclaration(Node declaration)
    {
        var sig = GetSignatureOfFullSignatureType(declaration);
        if (sig is not null)
        {
            return sig.TypeParameters();
        }

        Type[]? result = null;
        foreach (var node in declaration.TypeParameters() ?? [])
        {
            result = AppendIfUnique(result, GetDeclaredTypeOfTypeParameter(node.Symbol!));
        }

        return result ?? [];
    }

    internal Symbol? GetAnnotatedAccessorThisParameter(Node accessor)
    {
        var parameter = GetAccessorThisParameter(accessor);
        if (parameter is not null)
        {
            return parameter.Symbol;
        }

        return null;
    }

    internal Node? GetAccessorThisParameter(Node accessor)
    {
        if ((accessor.Parameters()?.Length ?? 0) == IfElse(IsGetAccessorDeclaration(accessor), 1, 2))
        {
            return GetThisParameter(accessor);
        }

        return null;
    }

    /// <summary>
    /// Indicates whether a declaration has an early-bound name or a dynamic name that can be late-bound.
    /// </summary>
    internal bool HasBindableName(Node node)
    {
        return !HasDynamicName(node) || HasLateBindableName(node);
    }

    /// <summary>
    /// Indicates whether a declaration has a late-bindable dynamic name.
    /// </summary>
    internal bool HasLateBindableName(Node node)
    {
        var name = GetNameOfDeclaration(node);
        return name is not null && IsLateBindableName(name);
    }

    /// <summary>
    /// Indicates whether a declaration name is definitely late-bindable.
    /// A declaration name is only late-bindable if:
    /// - It is a `ComputedPropertyName`.
    /// - Its expression is an `Identifier` or either a `PropertyAccessExpression` an
    /// `ElementAccessExpression` consisting only of these same three types of nodes.
    /// - The type of its expression is a string or numeric literal type, or is a `unique symbol` type.
    /// </summary>
    internal bool IsLateBindableName(Node node)
    {
        if (!IsLateBindableAST(node))
        {
            return false;
        }

        if (IsComputedPropertyName(node))
        {
            return IsTypeUsableAsPropertyName(CheckComputedPropertyName(node));
        }

        return IsTypeUsableAsPropertyName(CheckExpressionCached(node.AsElementAccessExpression()!.ArgumentExpression!));
    }

    internal bool HasLateBindableIndexSignature(Node node)
    {
        var name = GetNameOfDeclaration(node);
        return name is not null && IsLateBindableIndexSignature(name);
    }

    internal bool IsLateBindableIndexSignature(Node node)
    {
        if (!IsLateBindableAST(node))
        {
            return false;
        }

        if (IsComputedPropertyName(node))
        {
            return IsTypeUsableAsIndexSignatureDeclaration(CheckComputedPropertyName(node));
        }

        return IsTypeUsableAsIndexSignatureDeclaration(CheckExpressionCached(node.AsElementAccessExpression()!.ArgumentExpression!));
    }

    internal bool IsTypeUsableAsIndexSignatureDeclaration(Type t)
    {
        return IsTypeAssignableToWorker(t, _stringNumberSymbolType);
    }

    internal static bool IsLateBindableAST(Node node)
    {
        Node? expr = null;
        if (IsComputedPropertyName(node))
        {
            expr = node.Expression();
        }
        else if (IsElementAccessExpression(node))
        {
            expr = node.AsElementAccessExpression()!.ArgumentExpression;
        }

        return expr is not null && IsEntityNameExpression(expr);
    }
}
