// ported from: internal/ls/codeactions_missingmemberfixer.go:1-515
//
// 与 Go 的偏离（逐条）：
// 1) 未导出类型 `preserveOptionalFlags` → `internal enum PreserveOptionalFlags`（`[Flags]`），iota 常量去类型前缀：
//    `preserveOptionalFlagsMethod` → `Method`、…`Property`、`All = Method | Property`（§3.3）。
// 2) 未导出结构体 `missingMemberFixer` → `internal sealed class MissingMemberFixer`；未导出方法 → `internal` +
//    PascalCase（§0）；未导出字段 → `internal _camelCase`。Go 包级函数 `createDummyParameters`/`createDeclarationName`/
//    `createPropertyName` → `Ls` 静态类的 `internal static` 成员（PascalCase）。
// 3) Go 的 `body *ast.FunctionBody`：`FunctionBody = Node` 是类型别名（ast_generated.go:588）⇒ C# 用 `Node?`。
// 4) `checker.NewNodeBuilderEx(...)` / `checker.NewNodeBuilder(...)` → `CheckerType.NewNodeBuilderEx/NewNodeBuilder`
//    （别名 `CheckerType` 而非 `Checker`：命名空间 `TypeScript.Checker` 与类 `Checker` 同名，`Checker` 单独写会先绑到
//     命名空间 → CS0118；先例 Ls/CallHierarchy.cs 的 `using CheckerType = TypeScript.Checker.Checker;`）
//    静态成员；`TypeScript.Checker.NodeBuilder` 类与 `TypeScript.NodeBuilder` 命名空间同名 ⇒ 别名 `CheckerNodeBuilder`。
// 5) `nodebuilder.Flags*` / `nodebuilder.InternalFlags*` → 别名 `NodeBuilderFlags` / `NodeBuilderInternalFlags`。
// 6) `ast.SymbolTable` = `Dictionary<string, Symbol>`（Go 的 map）；读不存在的键在 Go 得到 nil ⇒ C# 用 `TryGetValue`/
//    `ContainsKey`。
// 7) `symbol.Declarations`（`[]*ast.Node`，可能 nil）→ `Node[]?`，Go 的 `len(declarations)` 用 `declarations.Length`
//    前先 `?? []`（nil 切片等价空切片）。
// 8) `IdToSymbol` 的键在 Go 是 `*ast.IdentifierNode`，C# 统一 `Node`（见 LsAutoImport/Fix.cs 文件头第 11 条）。
// 9) `core.FirstOrNil`/`LastOrNil`/`IfElse`/`OrElse` → `Core.*`（本文件未 `using static TypeScript.Core.Core;`，
//    避免 `Core` 命名空间遮蔽；直接写 `Core.Xxx`）。
// 10) `strconv.Itoa(i)` → `i.ToString(CultureInfo.InvariantCulture)`。
// 11) Go 的 `panic("Expected set accessor to have a parameter.")` → `Debug.Fail(...)` + `UnreachableException`
//     （Debug.Fail 无 `[DoesNotReturn]`）；禁止 `using System.Diagnostics;`。
// 12) `ast.GetAllAccessorDeclarations(symbol.Declarations, declaration)` 在 C# 收 `ReadOnlySpan<Node>` ⇒
//     `(declarations ?? []).AsSpan()`。
using System;
using System.Collections.Generic;
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsChange;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.LsUtil.LsUtil;
using CheckerType = TypeScript.Checker.Checker;
using Signature = TypeScript.Checker.Signature;
using CheckerNodeBuilder = TypeScript.Checker.NodeBuilder;
using Loc = TypeScript.Locale.Locale;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;
using Program = TypeScript.Compiler.Program;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>Go: <c>type preserveOptionalFlags int</c>（未导出；成员名去掉 Go 的 `preserveOptionalFlags` 前缀）。</summary>
[Flags]
internal enum PreserveOptionalFlags
{
    Method = 1,
    Property = 2,
    All = Method | Property,
}

/// <summary>Go: <c>type missingMemberFixer struct</c>（codeactions_missingmemberfixer.go:22-30）。</summary>
internal sealed class MissingMemberFixer
{
    internal Tracker _changeTracker;

    internal CheckerType _typeChecker;

    internal Program _program;

    internal UserPreferences _preferences;

    internal ImportAdder? _importAdder;

    internal Loc _locale;

    internal MissingMemberFixer(Tracker changeTracker, Program program, CheckerType typeChecker, UserPreferences preferences, ImportAdder? importAdder, Loc locale)
    {
        _changeTracker = changeTracker;
        _typeChecker = typeChecker;
        _program = program;
        _preferences = preferences;
        _importAdder = importAdder;
        _locale = locale;
    }

    internal (CheckerNodeBuilder NodeBuilder, Dictionary<Node, Symbol> IdToSymbol) CreateNodeBuilder()
    {
        Dictionary<Node, Symbol> idToSymbol = [];
        var nodeBuilder = CheckerType.NewNodeBuilderEx(_typeChecker, _changeTracker.EmitContext, idToSymbol);
        return (nodeBuilder, idToSymbol);
    }

    internal Node[] CreateMemberFromSymbol(Symbol symbol, Node enclosingDeclaration, SourceFile sourceFile, Node? body, PreserveOptionalFlags preserveOptional, bool isAbstract)
    {
        var declarations = symbol.Declarations ?? [];
        var declaration = FirstOrNil(declarations);

        var quotePreference = GetQuotePreference(sourceFile, _preferences);
        var ambient = (enclosingDeclaration.Flags & NodeFlags.Ambient) != 0;
        var signatureOnly = ambient || isAbstract;
        var optional = (symbol.Flags & SymbolFlags.Optional) != 0;
        var kind = Kind.PropertySignature;
        if (declaration is not null)
        {
            kind = declaration.Kind;
        }

        var declarationName = CreateDeclarationName(_changeTracker.NodeFactory, _typeChecker, symbol, declaration);
        var modifiers = CreateModifiers(symbol, declaration);

        var flags = NodeBuilderFlags.NoTruncation;
        if (quotePreference == QuotePreferenceSingle)
        {
            flags |= NodeBuilderFlags.UseSingleQuotesForStringLiteralType;
        }

        var t = _typeChecker.GetWidenedType(_typeChecker.GetTypeOfSymbolAtLocation(symbol, enclosingDeclaration));
        List<Node>? nodes = null;

        switch (kind)
        {
            case Kind.PropertySignature:
            case Kind.PropertyDeclaration:
            {
                var (nodeBuilder, idToSymbol) = CreateNodeBuilder();
                var typeNode = CreateTypeNode(t, enclosingDeclaration, flags, nodeBuilder, idToSymbol);
                Node? questionToken = null;
                if (optional && (preserveOptional & PreserveOptionalFlags.Property) != 0)
                {
                    questionToken = _changeTracker.NodeFactory.NewToken(Kind.QuestionToken);
                }

                (nodes ??= []).Add(_changeTracker.NodeFactory.NewPropertyDeclaration(modifiers, CreatePropertyName(_changeTracker.NodeFactory, declarationName, quotePreference), questionToken, typeNode, null /*initializer*/));
                return nodes.ToArray();
            }

            case Kind.GetAccessor:
            case Kind.SetAccessor:
            {
                var (nodeBuilder, idToSymbol) = CreateNodeBuilder();
                var accessors = GetAllAccessorDeclarations(declarations.AsSpan(), declaration);
                List<Node> orderedAccessors = [];
                if (accessors.SecondAccessor is null)
                {
                    orderedAccessors.Add(accessors.FirstAccessor!);
                }
                else
                {
                    orderedAccessors.Add(accessors.FirstAccessor!);
                    orderedAccessors.Add(accessors.SecondAccessor);
                }

                foreach (var accessor in orderedAccessors)
                {
                    if (IsGetAccessorDeclaration(accessor))
                    {
                        (nodes ??= []).Add(
                            _changeTracker.NodeFactory.NewGetAccessorDeclaration(
                                modifiers, CreatePropertyName(_changeTracker.NodeFactory, declarationName, quotePreference),
                                null /*typeParameters*/, null /*parameters*/, CreateTypeNode(t, enclosingDeclaration, flags, nodeBuilder, idToSymbol), null /*fullSignature*/, CreateBody(body, quotePreference, signatureOnly)));
                    }

                    if (IsSetAccessorDeclaration(accessor))
                    {
                        var parameter = TypeScript.Checker.Checker.GetSetAccessorValueParameter(accessor);
                        if (parameter is null)
                        {
                            Debug.Fail("Expected set accessor to have a parameter.");
                            throw new System.Diagnostics.UnreachableException();
                        }

                        (nodes ??= []).Add(
                            _changeTracker.NodeFactory.NewSetAccessorDeclaration(
                                modifiers, CreatePropertyName(_changeTracker.NodeFactory, declarationName, quotePreference),
                                null /*typeParameters*/, CreateDummyParameters(_changeTracker.NodeFactory, 1, [parameter.Name()!.Text()], [CreateTypeNode(t, enclosingDeclaration, flags, nodeBuilder, idToSymbol)], 1, IsInJSFile(enclosingDeclaration)),
                                null /*type*/, null /*fullSignature*/, CreateBody(body, quotePreference, signatureOnly)));
                    }
                }

                return nodes?.ToArray() ?? [];
            }

            case Kind.MethodSignature:
            case Kind.MethodDeclaration:
            {
                var signatures = GetCallSignatures(t);
                var preserveOptionalMethod = optional && (preserveOptional & PreserveOptionalFlags.Method) != 0;
                if (signatures.Length == 0)
                {
                    return [];
                }

                if (declarations.Length == 1)
                {
                    var method = CreateSignatureDeclarationFromSignature(FirstOrNil(signatures), Kind.MethodDeclaration, sourceFile, enclosingDeclaration, CreateBody(body, quotePreference, signatureOnly), modifiers, declarationName, preserveOptionalMethod);
                    if (method is not null)
                    {
                        (nodes ??= []).Add(method);
                    }

                    return nodes?.ToArray() ?? [];
                }

                foreach (var signature in signatures)
                {
                    if (signature.Declaration() is not null && (signature.Declaration()!.Flags & NodeFlags.Ambient) != 0)
                    {
                        continue;
                    }

                    var method = CreateSignatureDeclarationFromSignature(signature, Kind.MethodDeclaration, sourceFile, enclosingDeclaration, null /*body*/, modifiers, declarationName, preserveOptionalMethod);
                    if (method is not null)
                    {
                        (nodes ??= []).Add(method);
                    }
                }

                if (signatureOnly)
                {
                    return nodes?.ToArray() ?? [];
                }

                if (declarations.Length > signatures.Length)
                {
                    var signature = _typeChecker.GetSignatureFromDeclaration(LastOrNil(declarations));
                    var method = CreateSignatureDeclarationFromSignature(signature, Kind.MethodDeclaration, sourceFile, enclosingDeclaration, CreateBody(body, quotePreference, false /*signatureOnly*/), modifiers, declarationName, preserveOptionalMethod);
                    if (method is not null)
                    {
                        (nodes ??= []).Add(method);
                    }
                }
                else
                {
                    var method = CreateSignatureDeclarationFromSignatures(signatures, declarationName, preserveOptionalMethod, modifiers, quotePreference, body, enclosingDeclaration);
                    if (method is not null)
                    {
                        (nodes ??= []).Add(method);
                    }
                }

                return nodes?.ToArray() ?? [];
            }
        }
        return [];
    }

    internal Signature[] GetCallSignatures(Type t)
    {
        if (t.IsUnion())
        {
            return FlatMap(t.Types(), _typeChecker.GetCallSignatures);
        }
        return _typeChecker.GetCallSignatures(t);
    }

    internal Node? CreateTypeNode(Type t, Node enclosingDeclaration, NodeBuilderFlags flags, CheckerNodeBuilder nodeBuilder, Dictionary<Node, Symbol> idToSymbol)
    {
        return ImportTypeNode(nodeBuilder.TypeToTypeNode(t, enclosingDeclaration, flags, NodeBuilderInternalFlags.None, null /*tracker*/), idToSymbol);
    }

    internal ModifierList? CreateModifiers(Symbol symbol, Node? declaration)
    {
        var modifierFlags = ModifierFlags.None;
        if (declaration is not null)
        {
            var effective = CheckerType.GetDeclarationModifierFlagsFromSymbol(symbol);
            modifierFlags = effective & ModifierFlags.Static;
            if ((effective & ModifierFlags.Public) != 0)
            {
                modifierFlags |= ModifierFlags.Public;
            }
            else if ((effective & ModifierFlags.Protected) != 0)
            {
                modifierFlags |= ModifierFlags.Protected;
            }
            if (IsAutoAccessorPropertyDeclaration(declaration))
            {
                modifierFlags |= ModifierFlags.Accessor;
            }
        }
        if (ShouldAddOverrideKeyword(declaration))
        {
            modifierFlags |= ModifierFlags.Override;
        }
        if (modifierFlags == ModifierFlags.None)
        {
            return null;
        }
        return _changeTracker.NodeFactory.NewModifierList(CreateModifiersFromModifierFlags(modifierFlags, _changeTracker.NodeFactory.NewModifier));
    }

    internal bool ShouldAddOverrideKeyword(Node? declaration)
    {
        return declaration is not null && _program.Options().NoImplicitOverride.IsTrue() && HasAbstractModifier(declaration);
    }

    internal Node? CreateSignatureDeclarationFromSignature(Signature? signature, Kind kind, SourceFile sourceFile, Node enclosingDeclaration, Node? body, ModifierList? modifiers, Node? name, bool optional)
    {
        var quotePreference = GetQuotePreference(sourceFile, _preferences);
        var flags = NodeBuilderFlags.NoTruncation | NodeBuilderFlags.SuppressAnyReturnType | NodeBuilderFlags.AllowEmptyTuple;
        if (quotePreference == QuotePreferenceSingle)
        {
            flags |= NodeBuilderFlags.UseSingleQuotesForStringLiteralType;
        }

        var (nodeBuilder, idToSymbol) = CreateNodeBuilder();
        var signatureDeclaration = nodeBuilder.SignatureToSignatureDeclaration(signature!, kind, enclosingDeclaration, flags, NodeBuilderInternalFlags.AllowUnresolvedNames, null /*tracker*/);
        if (signatureDeclaration is null)
        {
            return null;
        }

        var isJS = IsInJSFile(enclosingDeclaration);
        var parameters = signatureDeclaration.ParameterList();
        var typeParameters = IfElse(isJS, null, signatureDeclaration.TypeParameterList());
        var typeNode = IfElse(isJS, null, signatureDeclaration.Type());

        if (typeParameters is not null && typeParameters.Nodes is { Length: > 0 })
        {
            List<Node> typeParameterNodes = new(typeParameters.Nodes.Length);
            foreach (var tp in typeParameters.Nodes)
            {
                if (tp is null)
                {
                    continue;
                }

                if (IsTypeParameterDeclaration(tp))
                {
                    var typeParameter = tp.AsTypeParameterDeclaration()!;

                    var constraint = typeParameter.Constraint;
                    if (constraint is not null)
                    {
                        constraint = ImportTypeNode(constraint, idToSymbol);
                    }

                    var defaultType = typeParameter.DefaultType;
                    if (defaultType is not null)
                    {
                        defaultType = ImportTypeNode(defaultType, idToSymbol);
                    }

                    typeParameterNodes.Add(
                        _changeTracker.NodeFactory.UpdateTypeParameterDeclaration(typeParameter, typeParameter.Modifiers(), typeParameter.Name(), constraint, typeParameter.Expression, defaultType));
                }
                else
                {
                    typeParameterNodes.Add(tp);
                }
            }
            typeParameters = _changeTracker.NodeFactory.NewNodeList(typeParameterNodes.ToArray());
        }

        if (parameters is not null)
        {
            List<Node> parameterNodes = new(parameters.Nodes?.Length ?? 0);
            foreach (var p in parameters.Nodes ?? [])
            {
                if (p is null)
                {
                    continue;
                }

                var parameter = p.AsParameterDeclaration()!;
                var parameterTypeNode = parameter.Type;
                if (parameterTypeNode is not null)
                {
                    parameterTypeNode = ImportTypeNode(parameterTypeNode, idToSymbol);
                }

                parameterNodes.Add(
                    _changeTracker.NodeFactory.UpdateParameterDeclaration(parameter, parameter.Modifiers(), parameter.DotDotDotToken, parameter.Name(), IfElse(isJS, null, parameter.QuestionToken), parameterTypeNode, parameter.Initializer));
            }
            parameters = _changeTracker.NodeFactory.NewNodeList(parameterNodes.ToArray());
        }

        if (typeNode is not null)
        {
            typeNode = ImportTypeNode(typeNode, idToSymbol);
        }

        Node? questionToken = null;
        if (optional)
        {
            questionToken = _changeTracker.NodeFactory.NewToken(Kind.QuestionToken);
        }

        switch (kind)
        {
            case Kind.FunctionExpression:
            {
                var fn = signatureDeclaration.AsFunctionExpression()!;
                return _changeTracker.NodeFactory.UpdateFunctionExpression(fn, modifiers, fn.AsteriskToken, IfElse(name is not null && IsIdentifier(name), name, null), typeParameters, parameters, typeNode, fn.FullSignature, OrElse(body, fn.Body));
            }

            case Kind.ArrowFunction:
            {
                var fn = signatureDeclaration.AsArrowFunction()!;
                return _changeTracker.NodeFactory.UpdateArrowFunction(fn, modifiers, typeParameters, parameters, typeNode, fn.FullSignature, fn.EqualsGreaterThanToken, OrElse(body, fn.Body));
            }

            case Kind.MethodDeclaration:
            {
                var method = signatureDeclaration.AsMethodDeclaration()!;
                var methodName = IfElse(name is null, _changeTracker.NodeFactory.NewIdentifier(""), CreatePropertyName(_changeTracker.NodeFactory, name, quotePreference));
                return _changeTracker.NodeFactory.UpdateMethodDeclaration(method, modifiers, method.AsteriskToken, methodName, questionToken, typeParameters, parameters, typeNode, method.FullSignature, body);
            }

            case Kind.FunctionDeclaration:
            {
                var fn = signatureDeclaration.AsFunctionDeclaration()!;
                return _changeTracker.NodeFactory.UpdateFunctionDeclaration(fn, modifiers, fn.AsteriskToken, IfElse(name is not null && IsIdentifier(name), name, null), typeParameters, parameters, typeNode, fn.FullSignature, OrElse(body, fn.Body));
            }
        }

        return null;
    }

    internal Node? CreateSignatureDeclarationFromSignatures(Signature[] signatures, Node? name, bool optional, ModifierList? modifiers, QuotePreference quotePreference, Node? body, Node enclosingDeclaration)
    {
        if (signatures.Length == 0)
        {
            return null;
        }

        var (nodeBuilder, idToSymbol) = CreateNodeBuilder();
        var maxArgsSignature = signatures[0];
        var minArgumentCount = signatures[0].MinArgumentCount();

        var hasRestParameter = false;
        foreach (var signature in signatures)
        {
            minArgumentCount = Math.Min(minArgumentCount, signature.MinArgumentCount());
            if (signature.HasRestParameter())
            {
                hasRestParameter = true;
            }
            if (signature.Parameters().Length >= maxArgsSignature.Parameters().Length && (!signature.HasRestParameter() || maxArgsSignature.HasRestParameter()))
            {
                maxArgsSignature = signature;
            }
        }

        var maxNonRestArgs = maxArgsSignature.Parameters().Length - IfElse(maxArgsSignature.HasRestParameter(), 1, 0);
        List<string> parameterNames = new(maxArgsSignature.Parameters().Length);
        foreach (var symbol in maxArgsSignature.Parameters())
        {
            parameterNames.Add(symbol.Name);
        }
        var parameters = CreateDummyParameters(_changeTracker.NodeFactory, maxNonRestArgs, parameterNames.ToArray(), null /*types*/, minArgumentCount, IsInJSFile(enclosingDeclaration));

        if (hasRestParameter)
        {
            var restParameterName = "rest";
            if (maxNonRestArgs < parameterNames.Count && parameterNames[maxNonRestArgs] != "")
            {
                restParameterName = parameterNames[maxNonRestArgs];
            }

            Node? questionToken = null;
            if (maxNonRestArgs >= minArgumentCount)
            {
                questionToken = _changeTracker.NodeFactory.NewToken(Kind.QuestionToken);
            }

            parameters.Nodes = [.. (parameters.Nodes ?? []), _changeTracker.NodeFactory.NewParameterDeclaration(
                null /*modifiers*/, _changeTracker.NodeFactory.NewToken(Kind.DotDotDotToken),
                _changeTracker.NodeFactory.NewIdentifier(restParameterName), questionToken,
                _changeTracker.NodeFactory.NewArrayTypeNode(_changeTracker.NodeFactory.NewKeywordTypeNode(Kind.UnknownKeyword)), null /*initializer*/
            )];
        }

        var methodName = IfElse(name is null, _changeTracker.NodeFactory.NewIdentifier(""), CreatePropertyName(_changeTracker.NodeFactory, name, quotePreference));

        return _changeTracker.NodeFactory.NewMethodDeclaration(
            modifiers, null /*asteriskToken*/, methodName, IfElse(optional, _changeTracker.NodeFactory.NewToken(Kind.QuestionToken), null),
            null /*typeParameters*/, parameters, GetReturnTypeFromSignatures(signatures, enclosingDeclaration, nodeBuilder, idToSymbol),
            null /*fullSignature*/, CreateBody(body, quotePreference, false /*signatureOnly*/));
    }

    internal Node? GetReturnTypeFromSignatures(Signature[] signatures, Node enclosingDeclaration, CheckerNodeBuilder nodeBuilder, Dictionary<Node, Symbol> idToSymbol)
    {
        if (signatures.Length == 0)
        {
            return null;
        }

        List<Type> returnTypes = new(signatures.Length);
        foreach (var signature in signatures)
        {
            returnTypes.Add(_typeChecker.GetReturnTypeOfSignature(signature));
        }

        var unionType = _typeChecker.GetUnionType(returnTypes.ToArray());
        return ImportTypeNode(nodeBuilder.TypeToTypeNode(unionType, enclosingDeclaration, NodeBuilderFlags.NoTruncation, NodeBuilderInternalFlags.AllowUnresolvedNames, null /*typeArguments*/), idToSymbol);
    }

    internal Node? ImportTypeNode(Node? typeNode, Dictionary<Node, Symbol> idToSymbol)
    {
        if (typeNode is null || _importAdder is null)
        {
            return typeNode;
        }

        var (importedTypeNode, symbols) = AutoImport.TryGetAutoImportableReferenceFromTypeNode(typeNode, idToSymbol);
        if (importedTypeNode is not null)
        {
            foreach (var symbol in symbols)
            {
                var exportSymbol = GetExportedSymbol(symbol);
                if (exportSymbol is null)
                {
                    continue;
                }
                _importAdder.AddImportFromExportedSymbol(exportSymbol, true /*isValidTypeOnlyUseSite*/);
            }
            return importedTypeNode;
        }

        var seen = new HashSet<Symbol>();
        foreach (var symbol in idToSymbol.Values)
        {
            if (symbol is null || !seen.Add(symbol))
            {
                continue;
            }
            var exportSymbol = GetExportedSymbol(symbol);
            if (exportSymbol is null)
            {
                continue;
            }
            _importAdder.AddImportFromExportedSymbol(exportSymbol, true /*isValidTypeOnlyUseSite*/);
        }
        return typeNode;
    }

    internal Symbol? GetExportedSymbol(Symbol symbol)
    {
        symbol = _typeChecker.GetExportSymbolOfSymbol(symbol);
        if (symbol is null || symbol.Parent is null)
        {
            return null;
        }
        return symbol;
    }

    internal Node? CreateIndexSignatureDeclarationFromType(Node classDeclaration, Type implementedType, Type keyType)
    {
        var indexInfo = _typeChecker.GetIndexInfoOfType(implementedType, keyType);
        if (indexInfo is null)
        {
            return null;
        }

        var builder = CheckerType.NewNodeBuilder(_typeChecker, _changeTracker.EmitContext);
        return builder.IndexInfoToIndexSignatureDeclaration(indexInfo, classDeclaration, NodeBuilderFlags.None, NodeBuilderInternalFlags.None, null);
    }

    internal Node? CreateBody(Node? body, QuotePreference quotePreference, bool signatureOnly)
    {
        if (signatureOnly)
        {
            return null;
        }
        body = _changeTracker.NodeFactory.DeepCloneNode(body);
        if (body is null)
        {
            return CreateStubbedMethodBody(quotePreference);
        }
        return body;
    }

    internal Node CreateStubbedMethodBody(QuotePreference quotePreference)
    {
        var tokenFlags = TokenFlags.None;
        if (quotePreference == QuotePreferenceSingle)
        {
            tokenFlags = TokenFlags.SingleQuote;
        }

        return _changeTracker.NodeFactory.NewBlock(_changeTracker.NodeFactory.NewNodeList([
            _changeTracker.NodeFactory.NewThrowStatement(
                _changeTracker.NodeFactory.NewNewExpression(
                    _changeTracker.NodeFactory.NewIdentifier("Error"), null /*typeArguments*/, _changeTracker.NodeFactory.NewNodeList([
                        _changeTracker.NodeFactory.NewStringLiteral(Method_not_implemented.Localize(_locale), tokenFlags),
                    ]))),
        ]), true /*multiLine*/);
    }

    internal static NodeList CreateDummyParameters(NodeFactory factory, int argCount, string[]? names, Node?[]? types, int minArgumentCount, bool inJS)
    {
        List<Node> parameters = new(argCount);
        var parameterNameCounts = new Dictionary<string, int>();

        for (var i = 0; i < argCount; i++)
        {
            string parameterName;
            if (names is not null && i < names.Length && names[i] != "")
            {
                parameterName = names[i];
            }
            else
            {
                parameterName = "arg" + i.ToString(CultureInfo.InvariantCulture);
            }

            parameterNameCounts.TryGetValue(parameterName, out var count);
            parameterNameCounts[parameterName] = count + 1;

            if (count > 0)
            {
                parameterName += count.ToString(CultureInfo.InvariantCulture);
            }

            Node? questionToken = null;
            if (i >= minArgumentCount)
            {
                questionToken = factory.NewToken(Kind.QuestionToken);
            }

            Node? typeNode;
            if (inJS)
            {
                typeNode = null;
            }
            else if (types is not null && i < types.Length && types[i] is not null)
            {
                typeNode = types[i];
            }
            else
            {
                typeNode = factory.NewKeywordTypeNode(Kind.UnknownKeyword);
            }
            parameters.Add(
                factory.NewParameterDeclaration(null /*modifiers*/, null /*dotDotDotToken*/, factory.NewIdentifier(parameterName), questionToken, typeNode, null /*initializer*/));
        }
        return factory.NewNodeList(parameters.ToArray());
    }

    internal static Node? CreateDeclarationName(NodeFactory factory, CheckerType typeChecker, Symbol? symbol, Node? declaration)
    {
        if (symbol is not null && (symbol.CheckFlags & CheckFlags.Mapped) != 0)
        {
            var nameType = typeChecker.GetNameTypeOfSymbol(symbol);
            if (nameType is not null && CheckerType.IsTypeUsableAsPropertyName(nameType))
            {
                return factory.NewIdentifier(CheckerType.GetPropertyNameFromType(nameType));
            }
        }
        if (declaration is not null && declaration.Name() is not null)
        {
            return declaration.Name()!.Clone(factory);
        }
        if (symbol is not null)
        {
            return factory.NewIdentifier(symbol.Name);
        }
        return null;
    }

    internal static Node? CreatePropertyName(NodeFactory factory, Node? node, QuotePreference quotePreference)
    {
        if (node is not null && IsIdentifier(node) && node.Text() == "constructor")
        {
            var tokenFlags = TokenFlags.None;
            if (quotePreference == QuotePreferenceSingle)
            {
                tokenFlags = TokenFlags.SingleQuote;
            }
            return factory.NewComputedPropertyName(factory.NewStringLiteral(node.Text(), tokenFlags));
        }
        return factory.DeepCloneNode(node);
    }
}
