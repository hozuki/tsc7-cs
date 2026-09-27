// ported from: internal/checker/nodebuilder_hover.go（整文件，1-605）
//
// 与 Go 的差异（逐条）：
// 1) 包级函数（`isExpanding`/`typeElementsToClassElements`/`isHashPrivate`）→ `partial class Checker` 的
//    static 成员（§5.5）；Go 的函数内局部类型 `hoverStatement` → `NodeBuilderImpl` 的嵌套 `private sealed class HoverStatement`。
// 2) Go 未导出字段 → `_camelCase`（§5.5）；同名方法冲突用 Worker 名（`getPropertiesOfType`→`GetPropertiesOfTypeWorker`、
//    `getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`getBaseTypes`→`GetBaseTypesWorker`、`getWidenedType`→`GetWidenedTypeWorker`、
//    `getMergedSymbol`→`GetMergedSymbolWorker`、`getSignaturesOfType`→`GetSignaturesOfTypeWorker`、
//    `getIndexInfosOfType`→`GetIndexInfosOfTypeWorker`、`getIndexInfoOfType`→`GetIndexInfoOfTypeWorker`、
//    `getBaseConstructorTypeOfClass`→`GetBaseConstructorTypeOfClassWorker`、`getSignatureFromDeclaration`→
//    `GetSignatureFromDeclarationWorker`）。
// 3) `collections.Set[*ast.Symbol]` 的零值 → 显式 `new Set<Symbol>()`；`AddIfAbsent` 同名。
// 4) `printer.EFSingleLine` → `EmitFlags.SingleLine`；`core.*` → 同名 static；`fmt.Sprintf` → 内插字符串。
// 5) `resolved.properties`/`resolved.indexInfos` → `resolved._properties`/`resolved._indexInfos`（types.go 未导出字段）。
// 6) `&SignatureToSignatureDeclarationOptions{name: ...}` → `new SignatureToSignatureDeclarationOptions { _name = ... }`。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;
using static TypeScript.Checker.Checker;
using static TypeScript.Scanner.Scanner;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;
using AstNodeFactory = TypeScript.Ast.NodeFactory;

internal sealed partial class NodeBuilderImpl
{
    private sealed class HoverStatement
    {
        internal Node _node = null!;
        internal bool _isLocal; // local declarations (e.g. alias targets) should not get export modifier
    }

    /// <summary>
    /// expandSymbolForHover produces declaration nodes (class, interface, enum, module) for a symbol
    /// for expandable hover. This is a focused alternative to the full symbolTableToDeclarationStatements
    /// machinery used by declaration emit — it directly builds the declaration nodes hover needs
    /// without the declaration-emit scaffolding (deferred privates, symbol name remapping, export
    /// modifier computation, alias resolution, visited symbols tracking).
    /// </summary>
    internal Node[] ExpandSymbolForHover(Symbol symbol)
    {
        var results = new List<Node>();
        if ((symbol.Flags & SymbolFlags.Enum) != 0)
        {
            var node = ExpandEnumDecl(symbol);
            if (node is not null)
            {
                results.Add(node);
            }
        }

        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            var node = ExpandClassDecl(symbol);
            if (node is not null)
            {
                results.Add(node);
            }
        }

        // Module/namespace before interface (matching Strada ordering for merged declarations)
        if ((symbol.Flags & (SymbolFlags.ValueModule | SymbolFlags.NamespaceModule)) != 0)
        {
            var node = ExpandModuleDecl(symbol);
            if (node is not null)
            {
                results.Add(node);
            }
        }

        if ((symbol.Flags & SymbolFlags.Interface) != 0 && (symbol.Flags & SymbolFlags.Class) == 0)
        {
            var node = ExpandInterfaceDecl(symbol);
            if (node is not null)
            {
                results.Add(node);
            }
        }

        return results.ToArray();
    }

    /// <summary>expandEnumDecl produces an EnumDeclaration node with all members.</summary>
    internal Node? ExpandEnumDecl(Symbol symbol)
    {
        var name = SymbolName(symbol);
        _ctx!._approximateLength += 9 + name.Length;
        var memberProps = Filter(_ch.GetPropertiesOfTypeWorker(_ch.GetTypeOfSymbolWorker(symbol)), p => (p.Flags & SymbolFlags.EnumMember) != 0);
        var members = new List<Node>();
        for (var i = 0; i < memberProps.Length; i++)
        {
            var p = memberProps[i];
            if (CheckTruncationLengthIfExpanding() && i + 3 < memberProps.Length - 1)
            {
                _ctx!._expansionTruncated = true;
                members.Add(_f.NewEnumMember(_f.NewStringLiteral($" ... {memberProps.Length - i - 1} more ... ", TokenFlags.None), null));
                var last = memberProps[^1];
                members.Add(_f.NewEnumMember(_f.NewIdentifier(last.Name), EnumMemberInitializer(last)));
                break;
            }

            var memberDecl = Find(p.Declarations, IsEnumMember);
            Node? initializer;
            if (memberDecl is not null && memberDecl.AsEnumMember()!.Initializer is not null)
            {
                initializer = _f.DeepCloneNode(memberDecl.AsEnumMember()!.Initializer!);
            }
            else
            {
                initializer = EnumMemberInitializer(p);
            }

            _ctx!._approximateLength += 4 + p.Name.Length;
            if (initializer is not null)
            {
                _ctx!._approximateLength += 5; // " = " + value estimate
            }

            members.Add(_f.NewEnumMember(_f.NewIdentifier(p.Name), initializer));
        }

        var constModifier = ModifierFlags.None;
        if (IsConstEnumSymbol(symbol))
        {
            constModifier = ModifierFlags.Const;
        }

        ModifierList? mods = null;
        if (constModifier != 0)
        {
            mods = _f.NewModifierList(CreateModifiersFromModifierFlags(constModifier, _f.NewModifier));
        }

        return _f.NewEnumDeclaration(mods, _f.NewIdentifier(name), _f.NewNodeList(members.ToArray()));
    }

    internal Node? EnumMemberInitializer(Symbol p)
    {
        var memberDecl = Find(p.Declarations, IsEnumMember);
        if (memberDecl is null)
        {
            return null;
        }

        var val = _ch.GetConstantValue(memberDecl);
        if (val is null)
        {
            return null;
        }

        if (val is string s)
        {
            return _f.NewStringLiteral(s, TokenFlags.None);
        }

        if (val is global::TypeScript.JsNum.Number n)
        {
            return _f.NewNumericLiteral(n.String(), TokenFlags.None);
        }

        return null;
    }

    /// <summary>expandClassDecl produces a ClassDeclaration node with heritage clauses and members.</summary>
    internal Node? ExpandClassDecl(Symbol symbol)
    {
        var name = SymbolName(symbol);
        _ctx!._approximateLength += 9 + name.Length;

        var classLikeDeclarations = Filter(symbol.Declarations, IsClassLike);
        var originalDecl = FirstOrNil(classLikeDeclarations);
        var oldEnclosing = _ctx!._enclosingDeclaration;
        if (originalDecl is not null)
        {
            _ctx!._enclosingDeclaration = originalDecl;
        }

        try
        {
            var localParams = _ch.GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(symbol);
            var typeParamDecls = Map(localParams, p => TypeParameterToDeclaration(p)!);

            var declaredType = _ch.GetDeclaredTypeOfClassOrInterface(symbol);
            var classType = _ch.GetTypeWithThisArgument(declaredType, null, false);
            var baseTypes = _ch.GetBaseTypesWorker(_ch.GetTargetType(classType)) ?? [];
            var staticType = _ch.GetTypeOfSymbolWorker(symbol);
            var isClass = staticType._symbol is not null && staticType._symbol.ValueDeclaration is not null && IsClassLike(staticType._symbol.ValueDeclaration);
            Type? staticBaseType;
            if (isClass)
            {
                staticBaseType = _ch.GetBaseConstructorTypeOfClassWorker(declaredType);
            }
            else
            {
                staticBaseType = _ch._anyType;
            }

            // Heritage clauses
            var heritageClauses = HoverHeritageClauses(classLikeDeclarations);

            // Instance members via addPropertyToElementList (reusing existing serialization),
            // then convert TypeElements to ClassElements and add class-specific modifiers
            var allProps = _ch.GetPropertiesOfTypeWorker(classType);
            var symbolProps = FilterInheritedProperties(classType, baseTypes, allProps);
            var publicProps = Filter(symbolProps, s => !IsHashPrivate(s));
            var hasPrivate = Some(symbolProps, IsHashPrivate);

            var instanceMembers = new List<Node>();
            instanceMembers = new List<Node>(SerializePropertiesWithTruncation(publicProps, instanceMembers.ToArray()));
            instanceMembers = new List<Node>(TypeElementsToClassElements(_f, instanceMembers.ToArray()));
            instanceMembers = new List<Node>(AddClassModifiers(instanceMembers.ToArray(), false));

            // Static members
            var staticProps = Filter(_ch.GetPropertiesOfTypeWorker(staticType), p =>
                (p.Flags & SymbolFlags.Prototype) == 0 && p.Name != "prototype" && !IsNamespaceMember(p));
            var staticMembers = new List<Node>();
            staticMembers = new List<Node>(SerializePropertiesWithTruncation(staticProps, staticMembers.ToArray()));
            staticMembers = new List<Node>(TypeElementsToClassElements(_f, staticMembers.ToArray()));
            staticMembers = new List<Node>(AddClassModifiers(staticMembers.ToArray(), true));

            // Hash-private members
            var privateMembers = new List<Node>();
            if (hasPrivate)
            {
                privateMembers = new List<Node>(SerializePropertiesWithTruncation(Filter(symbolProps, IsHashPrivate), privateMembers.ToArray()));
                privateMembers = new List<Node>(TypeElementsToClassElements(_f, privateMembers.ToArray()));
            }

            // Constructors
            var constructors = SerializeConstructors(staticType, staticBaseType, isClass, symbol);

            // Index signatures
            var indexSigs = SerializeIndexSignaturesOfType(classType, FirstOrNil(baseTypes));

            var allMembers = new List<Node>(indexSigs.Length + staticMembers.Count + constructors.Length + instanceMembers.Count + privateMembers.Count);
            allMembers.AddRange(indexSigs);
            allMembers.AddRange(staticMembers);
            allMembers.AddRange(constructors);
            allMembers.AddRange(instanceMembers);
            allMembers.AddRange(privateMembers);

            return _f.NewClassDeclaration(null, _f.NewIdentifier(name), _f.NewNodeList(typeParamDecls), _f.NewNodeList(heritageClauses), _f.NewNodeList(allMembers.ToArray()));
        }
        finally
        {
            _ctx!._enclosingDeclaration = oldEnclosing;
        }
    }

    /// <summary>
    /// addClassModifiers post-processes class member nodes to add class-specific modifiers
    /// (private, protected, public, abstract, static) based on the original symbol declarations.
    /// </summary>
    internal Node[] AddClassModifiers(Node[] members, bool isStatic)
    {
        for (var i = 0; i < members.Length; i++)
        {
            var m = members[i];
            // Find the symbol for this member by matching the property name
            Symbol? memberSymbol = null;
            var memberName = m.Name();
            if (memberName is not null)
            {
                if (_idToSymbol.TryGetValue(memberName, out var sym))
                {
                    memberSymbol = sym;
                }
            }

            if (memberSymbol is null)
            {
                continue;
            }

            var modFlags = GetDeclarationModifierFlagsFromSymbol(memberSymbol) & ~ModifierFlags.Async;
            if (isStatic)
            {
                modFlags |= ModifierFlags.Static;
            }

            if (modFlags != 0 && CanHaveModifiers(m))
            {
                var existing = m.ModifierFlags;
                if (modFlags != existing)
                {
                    members[i] = ReplaceModifiers(_f, m, _f.NewModifierList(CreateModifiersFromModifierFlags(modFlags | existing, _f.NewModifier)));
                }
            }
        }

        return members;
    }

    /// <summary>
    /// expandInterfaceDecl produces an InterfaceDeclaration with members.
    /// Reuses addPropertyToElementList for property serialization and
    /// signatureToSignatureDeclarationHelper for signatures.
    /// </summary>
    internal Node? ExpandInterfaceDecl(Symbol symbol)
    {
        var name = SymbolName(symbol);
        _ctx!._approximateLength += 14 + name.Length;

        var interfaceType = _ch.GetDeclaredTypeOfClassOrInterface(symbol);
        var interfaceDeclarations = Filter(symbol.Declarations, IsInterfaceDeclaration);
        var localParams = _ch.GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(symbol);
        var typeParamDecls = Map(localParams, p => TypeParameterToDeclaration(p)!);
        var baseTypes = _ch.GetBaseTypesWorker(interfaceType) ?? [];
        Type? baseType = null;
        if (baseTypes.Length > 0)
        {
            baseType = _ch.GetIntersectionType(baseTypes);
        }

        // Members: reuse existing serialization functions
        var resolved = _ch.ResolveStructuredTypeMembers(interfaceType);
        var members = new List<Node>();

        // Index signatures, filtering those identical to base
        members.AddRange(SerializeIndexSignaturesOfType(interfaceType, baseType));
        // Construct signatures (skip abstract)
        foreach (var sig in resolved.ConstructSignatures())
        {
            if ((sig._flags & SignatureFlags.Abstract) != 0)
            {
                continue;
            }

            members.Add(SignatureToSignatureDeclarationHelper(sig, Kind.ConstructSignature, null)!);
        }

        // Call signatures
        foreach (var sig in resolved.CallSignatures())
        {
            members.Add(SignatureToSignatureDeclarationHelper(sig, Kind.CallSignature, null)!);
        }

        // Properties, filtering inherited
        var filteredProps = FilterInheritedProperties(interfaceType, baseTypes, resolved._properties);
        members = new List<Node>(SerializePropertiesWithTruncation(filteredProps, members.ToArray()));

        // Heritage clauses
        var heritageClauses = HoverHeritageClauses(interfaceDeclarations);

        return _f.NewInterfaceDeclaration(null, _f.NewIdentifier(name), _f.NewNodeList(typeParamDecls), _f.NewNodeList(heritageClauses), _f.NewNodeList(members.ToArray()));
    }

    internal Node[] HoverHeritageClauses(Node[] declarations)
    {
        var extendsTypes = new List<Node>();
        var implementsTypes = new List<Node>();
        foreach (var declaration in declarations)
        {
            foreach (var heritageElement in GetExtendsHeritageClauseElements(declaration))
            {
                extendsTypes.Add(_f.DeepCloneNode(heritageElement.AsNode())!);
            }

            foreach (var heritageElement in GetImplementsHeritageClauseElements(declaration))
            {
                implementsTypes.Add(_f.DeepCloneNode(heritageElement.AsNode())!);
            }
        }

        var heritageClauses = new List<Node>();
        if (extendsTypes.Count > 0)
        {
            heritageClauses.Add(_f.NewHeritageClause(Kind.ExtendsKeyword, _f.NewNodeList(extendsTypes.ToArray())));
        }

        if (implementsTypes.Count > 0)
        {
            heritageClauses.Add(_f.NewHeritageClause(Kind.ImplementsKeyword, _f.NewNodeList(implementsTypes.ToArray())));
        }

        return heritageClauses.ToArray();
    }

    /// <summary>
    /// serializePropertiesWithTruncation iterates properties using addPropertyToElementList,
    /// with truncation checks matching Strada's createTypeNodesFromResolvedType behavior.
    /// </summary>
    internal Node[] SerializePropertiesWithTruncation(Symbol[] properties, Node[] elements)
    {
        var list = new List<Node>(elements);
        properties = Filter(properties, p => (p.Flags & SymbolFlags.Prototype) == 0);
        for (var i = 0; i < properties.Length; i++)
        {
            var p = properties[i];
            if (CheckTruncationLengthIfExpanding() && (i + 3 < properties.Length - 1))
            {
                _ctx!._expansionTruncated = true;
                var text = $"... {properties.Length - i - 1} more ...";
                list.Add(_f.NewPropertySignatureDeclaration(null, _f.NewIdentifier(text), null, null, null));
                return AddPropertyToElementList(properties[^1], list.ToArray());
            }

            list = new List<Node>(AddPropertyToElementList(p, list.ToArray()));
        }

        return list.ToArray();
    }

    /// <summary>serializeConstructors builds constructor signature(s) for a class, with base type filtering.</summary>
    internal Node[] SerializeConstructors(Type staticType, Type? staticBaseType, bool isClass, Symbol symbol)
    {
        var isNonConstructable = !isClass &&
            symbol.ValueDeclaration is not null &&
            IsInJSFile(symbol.ValueDeclaration) &&
            (_ch.GetSignaturesOfTypeWorker(staticType, SignatureKind.Construct)?.Length ?? 0) == 0;
        if (isNonConstructable)
        {
            _ctx!._approximateLength += 21;
            var modifiers = CreateModifiersFromModifierFlags(ModifierFlags.Private, _f.NewModifier);
            return [_f.NewConstructorDeclaration(_f.NewModifierList(modifiers), null, _f.NewNodeList([]), null, null, null)];
        }

        var signatures = _ch.GetSignaturesOfTypeWorker(staticType, SignatureKind.Construct) ?? [];
        if (staticBaseType is not null)
        {
            var baseSigs = _ch.GetSignaturesOfTypeWorker(staticBaseType, SignatureKind.Construct) ?? [];
            if (baseSigs.Length == 0 && Every(signatures, sig => sig._parameters!.Length == 0))
            {
                return [];
            }

            if (baseSigs.Length == signatures.Length)
            {
                var allMatch = true;
                for (var i = 0; i < baseSigs.Length; i++)
                {
                    if (_ch.CompareSignaturesIdentical(signatures[i], baseSigs[i], false, false, true, _ch.CompareTypesIdentical) != Ternary.True)
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch)
                {
                    return [];
                }
            }

            var privateProtected = ModifierFlags.None;
            foreach (var sig in signatures)
            {
                if (sig._declaration is not null)
                {
                    privateProtected |= sig._declaration.ModifierFlags & (ModifierFlags.Private | ModifierFlags.Protected);
                }
            }

            if (privateProtected != 0)
            {
                return [_f.NewConstructorDeclaration(
                    _f.NewModifierList(CreateModifiersFromModifierFlags(privateProtected, _f.NewModifier)),
                    null, _f.NewNodeList([]), null, null, null)];
            }
        }
        else if (Every(signatures, sig => sig._parameters!.Length == 0))
        {
            return [];
        }

        var result = new List<Node>();
        foreach (var sig in signatures)
        {
            _ctx!._approximateLength++;
            result.Add(SignatureToSignatureDeclarationHelper(sig, Kind.Constructor, null)!);
        }

        return result.ToArray();
    }

    /// <summary>serializeIndexSignaturesOfType builds index signature declarations, filtering those identical to baseType.</summary>
    internal Node[] SerializeIndexSignaturesOfType(Type input, Type? baseType)
    {
        var result = new List<Node>();
        foreach (var info in _ch.GetIndexInfosOfTypeWorker(input)!)
        {
            if (baseType is not null)
            {
                var baseInfo = _ch.GetIndexInfoOfTypeWorker(baseType, info._keyType!);
                if (baseInfo is not null && _ch.IsTypeIdenticalTo(info._valueType!, baseInfo._valueType!))
                {
                    continue;
                }
            }

            result.Add(IndexInfoToIndexSignatureDeclarationHelper(info, null));
        }

        return result.ToArray();
    }

    /// <summary>
    /// serializeNamespaceMember produces the appropriate declaration node for a namespace member
    /// based on its symbol flags (type alias, enum, class, interface, nested namespace, or variable).
    /// </summary>
    internal Node? SerializeNamespaceMember(Symbol resolved, string name)
    {
        if ((resolved.Flags & SymbolFlags.TypeAlias) != 0)
        {
            return SerializeTypeAliasForNamespace(resolved, name);
        }
        else if ((resolved.Flags & SymbolFlags.Enum) != 0)
        {
            return ExpandEnumDecl(resolved);
        }
        else if ((resolved.Flags & SymbolFlags.Class) != 0)
        {
            return ExpandClassDecl(resolved);
        }
        else if ((resolved.Flags & SymbolFlags.Interface) != 0)
        {
            return ExpandInterfaceDecl(resolved);
        }
        else if ((resolved.Flags & (SymbolFlags.ValueModule | SymbolFlags.NamespaceModule)) != 0)
        {
            return ExpandModuleDecl(resolved);
        }
        else
        {
            var t = _ch.GetWidenedTypeWorker(_ch.GetTypeOfSymbolWorker(resolved));
            _ctx!._approximateLength += name.Length + 5;
            return _f.NewVariableStatement(
                null,
                _f.NewVariableDeclarationList(
                    _f.NewNodeList([
                        _f.NewVariableDeclaration(_f.NewIdentifier(name), null, SerializeTypeForDeclaration(null, t, resolved, true), null),
                    ]),
                    NodeFlags.Let));
        }
    }

    /// <summary>expandModuleDecl produces a ModuleDeclaration with exported members.</summary>
    internal Node? ExpandModuleDecl(Symbol symbol)
    {
        var exports = _ch.GetExportsOfSymbol(symbol);
        var members = new List<Symbol>();
        foreach (var sym in exports.Values)
        {
            // Filter to namespace-relevant members
            if (!IsNamespaceMember(sym))
            {
                continue;
            }

            if (!IsIdentifierText(sym.Name, LanguageVariant.Standard))
            {
                continue;
            }

            members.Add(sym);
        }

        _ch.SortSymbols(members.ToArray());
        _ctx!._approximateLength += 14;

        // Use the same name as symbol display.
        var oldFlags = _ctx!._flags;
        Node? localName;
        try
        {
            _ctx!._flags |= NodeBuilderFlags.WriteTypeParametersInQualifiedName | (NodeBuilderFlags)SymbolFormatFlags.UseOnlyExternalAliasing;
            localName = SymbolToNode(symbol, SymbolFlags.All);
        }
        finally
        {
            _ctx!._flags = oldFlags;
        }

        var bodyStmts = new List<HoverStatement>();
        var emittedLocals = new Set<Symbol>();
        for (var i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (CheckTruncationLengthIfExpanding() && i + 3 < members.Count - 1)
            {
                _ctx!._expansionTruncated = true;
                bodyStmts.Add(new HoverStatement { _node = _f.NewExpressionStatement(_f.NewIdentifier($"... ({members.Count - i - 1} more) ...")) });
                i = members.Count - 2; // skip to last member after i++ at end of iteration
                continue;
            }

            // Handle alias/re-export symbols
            if ((m.Flags & SymbolFlags.Alias) != 0)
            {
                var aliasDecl = _ch.GetDeclarationOfAliasSymbol(m);
                var target = _ch.GetMergedSymbolWorker(_ch.GetTargetOfAliasDeclaration(aliasDecl));
                if (target is not null)
                {
                    // If the alias target is a local symbol (not itself an export), emit its declaration first
                    if ((target.Flags & (SymbolFlags.BlockScopedVariable | SymbolFlags.FunctionScopedVariable | SymbolFlags.Property)) != 0)
                    {
                        if (emittedLocals.AddIfAbsent(target))
                        {
                            var localType = _ch.GetWidenedTypeWorker(_ch.GetTypeOfSymbolWorker(target));
                            _ctx!._approximateLength += target.Name.Length + 5;
                            var localStmt = _f.NewVariableStatement(null,
                                _f.NewVariableDeclarationList(_f.NewNodeList([
                                    _f.NewVariableDeclaration(_f.NewIdentifier(target.Name), null, SerializeTypeForDeclaration(null, localType, target, true), null),
                                ]), NodeFlags.Let));
                            bodyStmts.Add(new HoverStatement { _node = localStmt, _isLocal = true });
                        }
                    }

                    var targetName = target.Name;
                    _ctx!._approximateLength += 16 + m.Name.Length;
                    Node? propertyName = null;
                    if (m.Name != targetName)
                    {
                        propertyName = _f.NewIdentifier(targetName);
                    }

                    var stmt = _f.NewExportDeclaration(
                        null, false,
                        _f.NewNamedExports(_f.NewNodeList([
                            _f.NewExportSpecifier(false, propertyName, _f.NewIdentifier(m.Name)),
                        ])),
                        null, null);
                    bodyStmts.Add(new HoverStatement { _node = stmt });
                    continue;
                }
            }

            var resolved = _ch.ResolveSymbol(m)!;

            // Handle functions as function declarations
            if ((resolved.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0)
            {
                var t = _ch.GetTypeOfSymbolWorker(resolved);
                var sigs = _ch.GetSignaturesOfTypeWorker(t, SignatureKind.Call) ?? [];
                foreach (var sig in sigs)
                {
                    _ctx!._approximateLength++;
                    var decl = SignatureToSignatureDeclarationHelper(sig, Kind.FunctionDeclaration, new SignatureToSignatureDeclarationOptions
                    {
                        _name = _f.NewIdentifier(m.Name),
                    });
                    bodyStmts.Add(new HoverStatement { _node = decl! });
                }

                // If the function also has namespace characteristics, emit an empty namespace.
                var merged = _ch.GetMergedSymbolWorker(resolved)!;
                var hasModuleExports = (merged.Flags & (SymbolFlags.ValueModule | SymbolFlags.NamespaceModule)) != 0 && merged.Exports is not null && merged.Exports.Count != 0;
                if (!hasModuleExports)
                {
                    bodyStmts.Add(new HoverStatement { _node = _f.NewModuleDeclaration(null, Kind.NamespaceKeyword, _f.NewIdentifier(m.Name), null /*attributes*/, _f.NewModuleBlock(_f.NewNodeList([]))) });
                }

                continue;
            }

            // Handle remaining member kinds (type alias, enum, class, interface, namespace, variable)
            var member = SerializeNamespaceMember(resolved, m.Name);
            if (member is not null)
            {
                bodyStmts.Add(new HoverStatement { _node = member });
            }
        }

        // Add export modifier to exported statements (skip local declarations and ExportDeclarations).
        for (var i = 0; i < bodyStmts.Count; i++)
        {
            var s = bodyStmts[i];
            if (s._isLocal || IsExportDeclaration(s._node))
            {
                continue;
            }

            if (CanHaveModifiers(s._node))
            {
                var mf = s._node.ModifierFlags | ModifierFlags.Export;
                s._node = ReplaceModifiers(_f, s._node, _f.NewModifierList(CreateModifiersFromModifierFlags(mf, _f.NewModifier)));
            }
        }

        // Collect nodes, stripping export if all statements are exported.
        var bodyStatements = new Node[bodyStmts.Count];
        for (var i = 0; i < bodyStmts.Count; i++)
        {
            bodyStatements[i] = bodyStmts[i]._node;
        }

        var allExported = bodyStatements.Length > 0 && Every(bodyStatements, d => HasSyntacticModifier(d, ModifierFlags.Export));
        if (allExported)
        {
            for (var i = 0; i < bodyStatements.Length; i++)
            {
                var stmt = bodyStatements[i];
                if (CanHaveModifiers(stmt))
                {
                    var mf = stmt.ModifierFlags & ~ModifierFlags.Export;
                    bodyStatements[i] = ReplaceModifiers(_f, stmt, _f.NewModifierList(CreateModifiersFromModifierFlags(mf, _f.NewModifier)));
                }
            }
        }

        var keyword = Kind.NamespaceKeyword;
        if (!IsIdentifier(localName!))
        {
            keyword = Kind.ModuleKeyword;
        }

        Node? attributes = null;
        var declaration = Find(symbol.Declarations, declaration => IsModuleDeclaration(declaration) && declaration.AsModuleDeclaration()!.Attributes is not null);
        if (declaration is not null)
        {
            attributes = _f.DeepCloneNode(declaration.AsModuleDeclaration()!.Attributes!);
            _e.SetEmitFlags(attributes!, EmitFlags.SingleLine);
        }

        return _f.NewModuleDeclaration(null, keyword, localName, attributes, _f.NewModuleBlock(_f.NewNodeList(bodyStatements)));
    }

    /// <summary>serializeTypeAliasForNamespace produces a TypeAliasDeclaration for a type alias inside a namespace body.</summary>
    internal Node? SerializeTypeAliasForNamespace(Symbol symbol, string name)
    {
        var aliasType = _ch.GetDeclaredTypeOfTypeAlias(symbol);
        var typeParams = _ch.GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(symbol);
        var typeParamDecls = Map(typeParams, p => TypeParameterToDeclaration(p)!);
        var restoreFlags = SaveRestoreFlags();
        _ctx!._flags |= NodeBuilderFlags.InTypeAlias;
        var typeNode = TypeToTypeNode(aliasType);
        restoreFlags();
        _ctx!._approximateLength += 8 + name.Length;
        return _f.NewTypeAliasDeclaration(null, _f.NewIdentifier(name), _f.NewNodeList(typeParamDecls), typeNode);
    }

    /// <summary>filterInheritedProperties removes properties already present in base types.</summary>
    internal Symbol[] FilterInheritedProperties(Type t, Type[]? baseTypes, Symbol[]? properties)
    {
        if (baseTypes is null || baseTypes.Length == 0)
        {
            return properties ?? [];
        }

        properties ??= [];

        // Build a lookup from property name to symbol for parent-identity comparison.
        var propsByName = new Dictionary<string, Symbol>(properties.Length);
        foreach (var p in properties)
        {
            propsByName[p.Name] = p;
        }

        // Collect names of properties inherited unchanged from base types.
        var inherited = new Set<string>();
        foreach (var @base in baseTypes)
        {
            var baseWithThis = _ch.GetTypeWithThisArgument(@base, _ch.GetTargetType(t).AsInterfaceType()!._thisType, false);
            foreach (var prop in _ch.GetPropertiesOfTypeWorker(baseWithThis)!)
            {
                if (propsByName.TryGetValue(prop.Name, out var existing) && prop.Parent == existing.Parent)
                {
                    inherited.Add(prop.Name);
                }
            }
        }

        if (inherited.Len() == 0)
        {
            return properties;
        }

        return Filter(properties, p => !inherited.Has(p.Name));
    }

    internal bool IsNamespaceMember(Symbol p)
    {
        return (p.Flags & (SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias)) != 0 ||
            !((p.Flags & SymbolFlags.Prototype) != 0 || p.Name == "prototype" || (p.ValueDeclaration is not null && HasStaticModifier(p.ValueDeclaration) && IsClassLike(p.ValueDeclaration.Parent!)));
    }
}

public sealed partial class Checker
{
    /// <summary>isExpanding returns whether the node builder context is operating in hover-expansion mode.</summary>
    internal static bool IsExpanding(NodeBuilderContext ctx)
    {
        return ctx._maxExpansionDepth != -1;
    }

    /// <summary>
    /// typeElementsToClassElements converts TypeElement nodes (PropertySignature, MethodSignature)
    /// to their ClassElement equivalents (PropertyDeclaration, MethodDeclaration) so they can be
    /// used as members of a ClassDeclaration. Nodes that are already ClassElements pass through unchanged.
    /// </summary>
    internal static Node[] TypeElementsToClassElements(AstNodeFactory f, Node[] members)
    {
        for (var i = 0; i < members.Length; i++)
        {
            var m = members[i];
            switch (m.Kind)
            {
                case Kind.PropertySignature:
                {
                    var ps = m.AsPropertySignatureDeclaration()!;
                    members[i] = f.NewPropertyDeclaration(m.Modifiers(), ps.Name(), ps.QuestionToken(), ps.Type, null);
                    break;
                }

                case Kind.MethodSignature:
                {
                    var ms = m.AsMethodSignatureDeclaration()!;
                    members[i] = f.NewMethodDeclaration(m.Modifiers(), null, ms.Name(), ms.QuestionToken(), ms.TypeParameters, ms.Parameters, ms.Type, null, null);
                    break;
                }
            }
        }

        return members;
    }

    /// <summary>Go: <c>func isHashPrivate(s *ast.Symbol) bool</c>（nodebuilder_hover.go:604）。</summary>
    internal static bool IsHashPrivate(Symbol s)
    {
        return s.ValueDeclaration is not null && s.ValueDeclaration.Name() is not null && IsPrivateIdentifier(s.ValueDeclaration.Name()!);
    }
}
