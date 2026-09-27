// ported from: internal/transformers/declarations/transform.go:2084-2995（this-属性赋值/变量语句/枚举/修饰符与参数表/绑定名/import 声明/JSDoc/declare 剥离/CJS 与 expando 收集）
//
// 与 Go 的偏离（逐条）：
// 1) Go 的包级函数 `isClassExtendingNull` / `isNotDeclareModifier` / `extractExpandoHostParams` 落在静态类
//    `Declarations`（docs/architecture.md §2）。`isNotDeclareModifier` 的 Go 形参是 `*ast.Modifier`——它只是
//    `*ast.Node` 的类型别名（docs/ast.md §2），C# 里落成 `Node`。
// 2) `extractExpandoHostParams` 的 3 个返回值 → C# 具名元组（仓库先例：Core/Core.cs 的 PositionToLineAndByteOffset）。
//    Go 的 `*ast.TypeParameterList` / `*ast.ParameterList` 都是 `*ast.NodeList` 的别名，C# 没有别名类型，
//    一律落成 `NodeList?`（可空性照 Go 的指针）。
// 3) `switch` 上的 `caseBlock:` 标签 + `break caseBlock` → C# 的普通 `break`：标签标的本来就是该 switch 语句，
//    且本方法内没有包围 switch 的循环，`break` 的唯一含义就是跳出 switch。
// 4) 两个 `defer func(){…}()` → `try { … } finally { … }`（docs/ast.md §3.1）。`collectThisPropertyAssignments`
//    的两个 defer 按 Go 的注册顺序展开成嵌套 try/finally（Go 是 LIFO：内层先跑）。返回值在 `return` 处就已求值，
//    与 Go 的"先算返回值再跑 defer"一致。
// 5) `append(slice, xs...)` → C# 集合表达式 `[.. slice ?? [], …]`（与 Binder/*.cs、Checker.C5.LateBind.cs 同款）；
//    Go 的 `m[k] = append(m[k], x)`（缺键读零值）→ `Dictionary.TryGetValue` 兜空数组后再拼。
// 6) `map[NodeId]*Node` 上的 `_, ok := m[id]`（只判存在，nil 值与缺键不同，见 Transform.cs 文件头第 6 条）→
//    `_lateStatementReplacementMap.ContainsKey(id)` / `_expandoHosts.ContainsKey(id)`。
// 7) `symbol.Flags&ast.SymbolFlagsAssignment` 里的 `symbol` 来自 `node.Symbol`：Go 是方法调用，C# 落成属性
//    （docs/ast.md §2），写成 `node.Symbol`（无括号）。
// 8) `scanner.IdentifierToKeywordKind(result.AsIdentifier())`：C# 的形参是具体的 `Identifier` 而非 `Node`
//    （同 Tracker.cs 文件头第 7 条的处理），故写成 `IdentifierToKeywordKind(result.AsIdentifier()!)`。
// 9) `tx.state.reportExpandoFunctionErrors(declaration)` 是 Go 的函数字段调用 → C# `Action<Node>?`，写
//    `_state._reportExpandoFunctionErrors!(declaration)`。
// 10) `-value`（Go 的 jsnum.Number 一元负号）→ C# 的 `JsNum.Number.operator -`（`(-value).String()`）。
// 11) `tx.Visitor().Visit(x)`（NodeVisitor 的 `Visit` 字段，Go 的回调自己处理 nil）→ C# 的
//    `Visitor().VisitNode(x)`：既有约定与理由见 Transformers/JsxTransforms/Jsx.cs 文件头第 7 条（`VisitNode`
//    对 null 直接返回 null，与本处 Go 回调 `visit` 里 `if node == nil { return null }` 等价；唯一差异是"回调返回
//    SyntaxList"时 `VisitNode` 会做单元素提升——这里 Visit 的输入是 JSDoc 类型节点，分片 B 的
//    `VisitDeclarationSubtree` 对这些 Kind 只会产出类型节点，不会出现 SyntaxList）。
// 12) 少数几处按 Go 的非 nil 保证加 `!`（`decl.ImportClause!`、`_enclosingDeclaration!`、`name!` 等）：
//    C# 的可空流分析不追踪跨调用的成员状态（如 `decl.ImportClause` 在 `if (x is null) return;` 之后，
//    又被后一个 `is not null && …` 条件在合并点重置为 maybe-null），这些 `!` 只影响编译期告警，无语义变化。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.JsNum.JsNum;
using static TypeScript.Scanner.Scanner;
using static TypeScript.Transformers.Declarations.Declarations;

namespace TypeScript.Transformers.Declarations;

public sealed partial class DeclarationTransformer
{
    internal Node? VisitThisPropertyAssignments(Node node)
    {
        Node? thisTarget;
        var isStatic = false;
        var thisContainer = GetThisContainer(node, false, false);
        thisTarget = thisContainer.Parent;
        if (thisTarget is null)
        {
            return null; // thisContainer was source file, can't have expando-this
        }
        if (HasStaticModifier(thisContainer) || IsClassStaticBlockDeclaration(thisContainer))
        {
            isStatic = true;
        }
        if (!ReferenceEquals(thisTarget, _enclosingDeclaration))
        {
            return null; // stop searching within new `this` contexts
        }
        switch (GetAssignmentDeclarationKind(node))
        {
            case JSDeclarationKind.ThisProperty:
                var name = GetNameOfDeclaration(node);
                var baseDeclaration = _resolver.GetReferencedMemberValueDeclaration(node);
                var key = GetThisPropertyAssignmentKey(name, node, isStatic);
                if (baseDeclaration is null || _seenProperties.Has(key))
                {
                    break;
                }
                _seenProperties.Add(key);

                // problem: this prop might be overriding a prop from a base type. The checker has special bails for override compat comparisons for binary expression properties,
                // but what we transform to won't - so we either need to match the base type (for example, if it's a getter/setter) or emit nothing
                // See `checkKindsOfPropertyMemberOverrides` in the checker for what we're trying to satisfy here
                if (thisTarget.ClassLikeData()!.HeritageClauses is { Nodes.Length: > 0 } && !IsClassExtendingNull(thisTarget))
                {
                    // there is a base type any assignments might be "from"
                    _tracker.ReportInferenceFallback(thisTarget); // Add an isolated declarations error on this class - we can't know how to transform this prop into an assignment without referring to type information
                    if (_resolver.IsThisPropertyAssignmentDeclarationRedundant(node))
                    {
                        break; // skip assignments whose member is already provided by an `extends` base type (an inherited accessor/method, or an identical inherited property)
                        // TODO: If the property has an explicit `@type` annotation, we should probably emit it (maybe with an `override` modifier) instead of skipping it
                    }
                }

                ModifierList? mods = null;
                if (isStatic)
                {
                    mods = Factory().NewModifierList([Factory().NewModifier(Kind.StaticKeyword)]);
                }
                if (HasDynamicName(node))
                {
                    if (!Transformers.IsSimpleInlineableExpression(name!))
                    {
                        break; // Member either becomes an index signature or is a reassignment
                    }
                    CheckName(node);
                    name = Factory().NewComputedPropertyName(name); // Convert `this[foo] = expr` to `[foo]: Type`
                }
                if (GetTextOfPropertyName(name) == "constructor")
                {
                    break; // `constructor` is a builtin class member, not allowed to redeclare it
                }
                if (IsIdentifier(name!) && !IsIdentifierText(name!.Text(), LanguageVariant.Standard))
                {
                    name = Factory().NewStringLiteralFromNode(name!);
                }
                var prop = Factory().NewPropertyDeclaration(
                    mods,
                    name,
                    null,
                    EnsureType(node, false),
                    null);
                if (IsExpressionStatement(node.Parent!))
                {
                    PreserveJsDoc(prop, node.Parent!);
                }
                _thisPropertyAssignmentsCollected = [.. _thisPropertyAssignmentsCollected, prop];
                break;
        }
        return _thisPropertyVisitor.VisitEachChild(node);
    }

    // collectThisPropertyAssignments finds `this.x = expr` assignments in constructors, methods, and static blocks
    // of JS classes and synthesizes PropertyDeclaration nodes for each unique property name.
    internal Node[] CollectThisPropertyAssignments(Node classNode)
    {
        var members = classNode.ClassLikeData()!.Members!;
        var seen = new Set<ThisPropertyAssignmentKey>();
        // Pre-populate seen with existing direct member nodes to avoid duplicates
        foreach (var member in members.Nodes ?? [])
        {
            if (member.Name() is not null)
            {
                var isStatic = IsStatic(member);
                seen.Add(GetThisPropertyAssignmentKey(member.Name(), member, isStatic));
            }
        }
        _seenProperties = seen;
        try
        {
            _thisPropertyAssignmentsCollected = [];
            try
            {
                foreach (var n in members.Nodes ?? [])
                {
                    _thisPropertyVisitor.VisitEachChild(n);
                }
                return _thisPropertyAssignmentsCollected;
            }
            finally
            {
                _thisPropertyAssignmentsCollected = null!;
            }
        }
        finally
        {
            _seenProperties.Clear();
        }
    }

    internal Node[] WalkBindingPattern(BindingPattern pattern, Node param)
    {
        var elems = new List<Node>();
        foreach (var elem in pattern.Elements!.Nodes ?? [])
        {
            if (IsOmittedExpression(elem))
            {
                continue;
            }
            if (IsBindingPattern(elem.Name()))
            {
                elems.AddRange(WalkBindingPattern(elem.Name()!.AsBindingPattern()!, param));
                continue;
            }
            elems.Add(Factory().NewPropertyDeclaration(
                EnsureModifiers(param),
                elem.Name(),
                null, /*questionOrExclamationToken*/
                EnsureType(elem, false),
                null /*initializer*/
            ));
        }
        return elems.ToArray();
    }

    internal Node? TransformVariableStatement(VariableStatement input)
    {
        var visible = false;
        foreach (var decl in input.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [])
        {
            visible = GetBindingNameVisible(_resolver, decl);
            if (visible)
            {
                break;
            }
        }
        if (!visible)
        {
            return null;
        }

        Node[] inputNodes = input.DeclarationList.AsVariableDeclarationList()!.Declarations!.Nodes ?? [];
        Node[]? extraImports = null;
        if (_state._currentSourceFile!.CommonJSModuleIndicator is not null)
        {
            var normalDeclarations = new List<Node>();
            var imports = new List<Node>();
            foreach (var n in inputNodes)
            {
                if (IsVariableDeclarationInitializedToRequire(n))
                {
                    imports.Add(n);
                }
                else
                {
                    normalDeclarations.Add(n);
                }
            }
            inputNodes = normalDeclarations.ToArray();
            extraImports = Visitor().VisitSlice(imports.ToArray()).Result;
        }

        var nodes = Visitor().VisitSlice(inputNodes).Result;
        if (nodes is null || nodes.Length == 0)
        {
            if (extraImports is { Length: > 0 })
            {
                return Factory().NewSyntaxList(extraImports);
            }
            return null;
        }
        var nodeList = Factory().NewNodeList(nodes);

        var modifiers = EnsureModifiers(input);

        Node declList;
        if (IsVarUsing(input.DeclarationList) || IsVarAwaitUsing(input.DeclarationList))
        {
            declList = Factory().NewVariableDeclarationList(nodeList, NodeFlags.Const);
            EmitContext().SetOriginal(declList, input.DeclarationList);
            EmitContext().SetCommentRange(declList, input.DeclarationList!.Loc);
            declList.Loc = input.DeclarationList.Loc;
        }
        else
        {
            declList = Factory().UpdateVariableDeclarationList(input.DeclarationList.AsVariableDeclarationList()!, nodeList, input.DeclarationList.Flags);
        }
        var res = Factory().UpdateVariableStatement(input, modifiers, declList);
        if (extraImports is { Length: > 0 })
        {
            return Factory().NewSyntaxList([.. extraImports, res]);
        }
        return res;
    }

    internal Node TransformEnumDeclaration(EnumDeclaration input)
    {
        return Factory().UpdateEnumDeclaration(
            input,
            EnsureModifiers(input),
            input.Name(),
            Factory().NewNodeList(MapNonNil<Node, Node>(input.Members!.Nodes, m =>
            {
                if (ShouldStripInternal(m))
                {
                    return null!;
                }

                // Rewrite enum values to their constants, if available
                var enumValue = _resolver.GetEnumMemberValue(m);

                if (_state._isolatedDeclarations && m.Initializer() is not null && enumValue.HasExternalReferences &&
                    // This will be its own compiler error instead, so don't report.
                    !IsComputedPropertyName(m.Name()!))
                {
                    _state.AddDiagnostic(CreateDiagnosticForNode(m, Enum_member_initializers_must_be_computable_without_references_to_external_symbols_with_isolatedDeclarations));
                }

                Node? newInitializer;
                switch (enumValue.Value)
                {
                    case Number value:
                        if (value.IsInf())
                        {
                            if (value > 0)
                            {
                                newInitializer = Factory().NewIdentifier("Infinity");
                            }
                            else
                            {
                                newInitializer = Factory().NewPrefixUnaryExpression(Kind.MinusToken, Factory().NewIdentifier("Infinity"));
                            }
                        }
                        else if (value.IsNaN())
                        {
                            newInitializer = Factory().NewIdentifier("NaN");
                        }
                        else if (value >= 0)
                        {
                            newInitializer = Factory().NewNumericLiteral(value.String(), TokenFlags.None);
                        }
                        else
                        {
                            newInitializer = Factory().NewPrefixUnaryExpression(
                                Kind.MinusToken,
                                Factory().NewNumericLiteral((-value).String(), TokenFlags.None));
                        }
                        break;
                    case string value:
                        newInitializer = Factory().NewStringLiteral(value, TokenFlags.None);
                        break;
                    default:
                        // nil
                        newInitializer = null;
                        break;
                }
                var result = Factory().UpdateEnumMember(m.AsEnumMember()!, m.Name(), newInitializer);
                PreserveJsDoc(result, m);
                return result;
            }))
        );
    }

    internal ModifierList? EnsureModifiers(Node node)
    {
        var currentFlags = GetCombinedModifierFlags(EmitContext().ParseNode(node)) & ModifierFlags.All;
        var newFlags = EnsureModifierFlags(node);
        if (currentFlags == newFlags)
        {
            // Elide decorators
            var mods = node.Modifiers();
            if (mods is null)
            {
                return mods;
            }
            if (CanReuseModifierNodes(mods.Nodes!))
            {
                return Factory().NewModifierList(Filter(mods.Nodes, IsModifier));
            }
        }
        var result = CreateModifiersFromModifierFlags(newFlags, Factory().NewModifier);
        if (result.Length == 0)
        {
            return null;
        }
        return Factory().NewModifierList(result);
    }

    internal ModifierFlags EnsureModifierFlags(Node node)
    {
        var mask = ModifierFlags.All ^ (ModifierFlags.Public | ModifierFlags.Async | ModifierFlags.Override); // No async and override modifiers in declaration files
        var additions = ModifierFlags.None;
        if (_needsDeclare && !IsAlwaysType(node))
        {
            additions = ModifierFlags.Ambient;
        }
        var parentIsFile = node.Parent!.Kind == Kind.SourceFile;
        if (!parentIsFile)
        {
            mask ^= ModifierFlags.Ambient;
            additions = ModifierFlags.None;
        }
        if (IsImplicitlyExportedJSDocDeclaration(node))
        {
            additions |= ModifierFlags.Export;
        }
        return MaskModifierFlags(node, mask, additions);
    }

    internal NodeList? EnsureTypeParams(Node node, NodeList? parameters)
    {
        if (_host.GetEffectiveDeclarationFlags(EmitContext().ParseNode(node)!, ModifierFlags.Private) != 0)
        {
            return null;
        }
        NodeList? typeParameters;
        if ((typeParameters = Visitor().VisitNodes(parameters)) is not null)
        {
            return typeParameters;
        }
        var oldErrorNameNode = _state._errorNameNode;
        _state._errorNameNode = node.Name();
        GetSymbolAccessibilityDiagnostic? oldDiag = null;
        if (!_suppressNewDiagnosticContexts)
        {
            oldDiag = _state._getSymbolAccessibilityDiagnostic;
            if (CanProduceDiagnostics(node))
            {
                _state._getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(node);
            }
        }

        var data = node.FunctionLikeData();
        if (data is not null && data.FullSignature is not null)
        {
            var nodes = _resolver.CreateTypeParametersOfSignatureDeclaration(EmitContext(), node, _enclosingDeclaration!, DeclarationEmitNodeBuilderFlags, DeclarationEmitInternalNodeBuilderFlags, _tracker);
            if (nodes is not null)
            {
                typeParameters = new NodeList
                {
                    Loc = node.Loc,
                    Nodes = nodes,
                };
            }
        }

        _state._errorNameNode = oldErrorNameNode;
        if (!_suppressNewDiagnosticContexts)
        {
            _state._getSymbolAccessibilityDiagnostic = oldDiag;
        }
        return typeParameters;
    }

    internal NodeList UpdateParamList(Node node, NodeList? parameters)
    {
        if (_host.GetEffectiveDeclarationFlags(EmitContext().ParseNode(node)!, ModifierFlags.Private) != 0 || parameters!.Nodes is null || parameters.Nodes.Length == 0)
        {
            return Factory().NewNodeList([]);
        }
        var results = new Node[parameters.Nodes.Length];
        for (var i = 0; i < parameters.Nodes.Length; i++)
        {
            results[i] = EnsureParameter(parameters.Nodes[i].AsParameterDeclaration()!);
        }
        return Factory().NewNodeList(results);
    }

    internal Node EnsureParameter(ParameterDeclaration p)
    {
        var oldDiag = _state._getSymbolAccessibilityDiagnostic;
        if (!_suppressNewDiagnosticContexts)
        {
            _state._getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(p);
        }
        Node? questionToken = null;
        if (_resolver.IsOptionalParameter(p))
        {
            if (p.QuestionToken is not null)
            {
                questionToken = p.QuestionToken;
            }
            else
            {
                questionToken = Factory().NewToken(Kind.QuestionToken);
            }
        }
        var result = Factory().UpdateParameterDeclaration(
            p,
            null,
            p.DotDotDotToken,
            _bindingNameVisitor.VisitNode(p.Name()),
            questionToken,
            EnsureType(p, true),
            EnsureNoInitializer(p));
        _state._getSymbolAccessibilityDiagnostic = oldDiag;
        return result;
    }

    internal Node? EnsureNoInitializer(Node node)
    {
        if (ShouldPrintWithInitializer(node))
        {
            var unwrappedInitializer = UnwrapParenthesizedExpression(node.Initializer()!);
            if (!IsPrimitiveLiteralValue(unwrappedInitializer, true))
            {
                _tracker.ReportInferenceFallback(node);
            }
            return _resolver.CreateLiteralConstValue(EmitContext(), EmitContext().ParseNode(node)!, _tracker);
        }
        return null;
    }

    internal Node? VisitBindingName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.OmittedExpression:
                return node;
            case Kind.ArrayBindingPattern:
            case Kind.ObjectBindingPattern:
                return node.VisitEachChild(_bindingNameVisitor);
            case Kind.BindingElement:
                if (node.PropertyName() is not null && IsComputedPropertyName(node.PropertyName()!) && IsEntityNameExpression(node.PropertyName()!.Expression()))
                {
                    CheckEntityNameVisibility(node.PropertyName()!.Expression()!, _enclosingDeclaration!);
                }
                return Factory().UpdateBindingElement(node.AsBindingElement()!, node.AsBindingElement()!.DotDotDotToken, node.PropertyName(), _bindingNameVisitor.VisitNode(node.Name()), null /*initializer*/);
            default:
                return node;
        }
    }

    internal Node? TransformImportEqualsDeclaration(ImportEqualsDeclaration decl)
    {
        if (!_resolver.IsDeclarationVisible(decl))
        {
            return null;
        }
        if (decl.ModuleReference!.Kind == Kind.ExternalModuleReference)
        {
            // Rewrite external module names if necessary
            var specifier = GetExternalModuleImportEqualsDeclarationExpression(decl);
            return Factory().UpdateImportEqualsDeclaration(
                decl,
                decl.Modifiers(),
                decl.IsTypeOnly,
                decl.Name(),
                Factory().UpdateExternalModuleReference(decl.ModuleReference.AsExternalModuleReference()!, RewriteModuleSpecifier(decl, specifier)));
        }
        else
        {
            var oldDiag = _state._getSymbolAccessibilityDiagnostic;
            _state._getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(decl);
            CheckEntityNameVisibility(decl.ModuleReference, _enclosingDeclaration!);
            _state._getSymbolAccessibilityDiagnostic = oldDiag;
            return decl;
        }
    }

    internal Node? TransformImportDeclaration(ImportDeclaration decl)
    {
        if (decl.ImportClause is null)
        {
            // import "mod" - possibly needed for side effects? (global interface patches, module augmentations, etc)
            return Factory().UpdateImportDeclaration(
                decl,
                decl.Modifiers(),
                decl.ImportClause,
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier),
                decl.Attributes);
        }
        var phaseModifier = decl.ImportClause.AsImportClause()!.PhaseModifier;
        if (phaseModifier == Kind.DeferKeyword)
        {
            phaseModifier = Kind.Unknown;
        }
        // The `importClause` visibility corresponds to the default's visibility.
        Node? visibleDefaultBinding = null;
        if (decl.ImportClause is not null && decl.ImportClause.Name() is not null && _resolver.IsDeclarationVisible(decl.ImportClause))
        {
            visibleDefaultBinding = decl.ImportClause.Name();
        }
        if (decl.ImportClause!.AsImportClause()!.NamedBindings is null)
        {
            // No named bindings (either namespace or list), meaning the import is just default or should be elided
            if (visibleDefaultBinding is null)
            {
                return null;
            }
            return Factory().UpdateImportDeclaration(
                decl,
                decl.Modifiers(),
                Factory().UpdateImportClause(
                    decl.ImportClause.AsImportClause()!,
                    phaseModifier,
                    visibleDefaultBinding,
                    /*namedBindings*/ null),
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier),
                decl.Attributes);
        }
        if (decl.ImportClause.AsImportClause()!.NamedBindings!.Kind == Kind.NamespaceImport)
        {
            // Namespace import (optionally with visible default)
            Node? namedBindings = null;
            if (_resolver.IsDeclarationVisible(decl.ImportClause.AsImportClause()!.NamedBindings!))
            {
                namedBindings = decl.ImportClause.AsImportClause()!.NamedBindings;
            }
            if (visibleDefaultBinding is null && namedBindings is null)
            {
                return null;
            }
            return Factory().UpdateImportDeclaration(
                decl,
                decl.Modifiers(),
                Factory().UpdateImportClause(
                    decl.ImportClause.AsImportClause()!,
                    phaseModifier,
                    visibleDefaultBinding,
                    namedBindings),
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier),
                decl.Attributes);
        }
        // Named imports (optionally with visible default)
        var bindingList = Filter(
            decl.ImportClause.AsImportClause()!.NamedBindings!.Elements(),
            b => _resolver.IsDeclarationVisible(b));
        if (bindingList.Length > 0 || visibleDefaultBinding is not null)
        {
            Node? namedImports = null;
            if (bindingList.Length > 0)
            {
                namedImports = Factory().UpdateNamedImports(
                    decl.ImportClause.AsImportClause()!.NamedBindings!.AsNamedImports()!,
                    Factory().NewNodeList(bindingList));
            }
            return Factory().UpdateImportDeclaration(
                decl,
                decl.Modifiers(),
                Factory().UpdateImportClause(
                    decl.ImportClause.AsImportClause()!,
                    phaseModifier,
                    visibleDefaultBinding,
                    namedImports),
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier),
                decl.Attributes);
        }
        // Augmentation of export depends on import
        if (_resolver.IsImportRequiredByAugmentation(decl))
        {
            if (_state._isolatedDeclarations)
            {
                _state.AddDiagnostic(CreateDiagnosticForNode(decl, Declaration_emit_for_this_file_requires_preserving_this_import_for_augmentations_This_is_not_supported_with_isolatedDeclarations));
            }
            return Factory().UpdateImportDeclaration(
                decl,
                decl.Modifiers(),
                /*importClause*/ null,
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier),
                decl.Attributes);
        }
        // Nothing visible
        return null;
    }

    internal Node? TransformJSDocTypeExpression(JSDocTypeExpression input)
    {
        return Visitor().VisitNode(input.Type);
    }

    internal Node? TransformJSDocTypeLiteral(JSDocTypeLiteral input)
    {
        var members = Visitor().VisitSlice(input.JSDocPropertyTags).Result;
        var replacement = Factory().NewTypeLiteralNode(Factory().NewNodeList(members));
        EmitContext().SetOriginal(replacement, input);
        return replacement;
    }

    internal Node? TransformJSDocPropertyTag(JSDocParameterOrPropertyTag input)
    {
        var replacement = Factory().NewPropertySignatureDeclaration(
            null,
            Visitor().VisitNode(input.TagName),
            null,
            Visitor().VisitNode(input.TypeExpression),
            null);
        EmitContext().SetOriginal(replacement, input);
        return replacement;
    }

    internal Node? TransformJSDocAllType(JSDocAllType input)
    {
        var replacement = Factory().NewKeywordTypeNode(Kind.AnyKeyword);
        EmitContext().SetOriginal(replacement, input);
        return replacement;
    }

    internal Node? TransformJSDocNullableType(JSDocNullableType input)
    {
        var replacement = Factory().NewUnionTypeNode(Factory().NewNodeList(new Node[]
        {
            Visitor().VisitNode(input.Type)!,
            Factory().NewLiteralTypeNode(Factory().NewKeywordExpression(Kind.NullKeyword)),
        }));
        EmitContext().SetOriginal(replacement, input);
        return replacement;
    }

    internal Node? TransformJSDocNonNullableType(JSDocNonNullableType input)
    {
        return Visitor().VisitNode(input.Type);
    }

    internal Node? TransformJSDocVariadicType(JSDocVariadicType input)
    {
        var replacement = Factory().NewArrayTypeNode(Visitor().VisitNode(input.Type));
        EmitContext().SetOriginal(replacement, input);
        return replacement;
    }

    internal Node? TransformJSDocOptionalType(JSDocOptionalType input)
    {
        var replacement = Factory().NewUnionTypeNode(Factory().NewNodeList(new Node[]
        {
            Visitor().VisitNode(input.Type)!,
            Factory().NewKeywordTypeNode(Kind.UndefinedKeyword),
        }));
        EmitContext().SetOriginal(replacement, input);
        return replacement;
    }

    internal Node GetNameExpressionPreferringIdentifier(Node nameExpr)
    {
        if (IsNumericLiteral(nameExpr))
        {
            // Numeric property names are string properties in JS; convert to string literal
            nameExpr = Factory().NewStringLiteral(nameExpr.Text(), TokenFlags.None);
        }
        if (IsStringLiteralLike(nameExpr) && IsIdentifierText(nameExpr.Text(), LanguageVariant.Standard))
        {
            var result = Factory().NewIdentifier(nameExpr.Text()); // prefer non-string literal names where possible
            var kwKind = IdentifierToKeywordKind(result.AsIdentifier()!);
            // keep keywords as strings, except `default`, which has special reformulations in the transformer
            if (kwKind == Kind.Unknown || kwKind == Kind.DefaultKeyword)
            {
                // fake this into a parse tree node so the reference resolver resolves the node via `resolveName`
                result.Parent = nameExpr.Parent;
                result.Flags &= ~NodeFlags.Synthesized;
                // intentionally leave Loc unset so the string isn't used as the text source of the identifier
                return result;
            }
        }
        return nameExpr;
    }

    internal Node? StripDeclareModifiers(Node node)
    {
        if (node is null)
        {
            return null;
        }
        var mods = node.Modifiers();
        if (mods is not null)
        {
            var flags = node.ModifierFlags;
            if ((flags & ModifierFlags.Ambient) != 0)
            {
                var filtered = Filter(mods.Nodes, IsNotDeclareModifier);
                node.AsMutable().SetModifiers(Factory().NewModifierList(filtered));
            }
        }
        return node; // no need to recur into children, only strip at top-level
    }

    internal Node? VisitCJSExportAssignments(Node expression)
    {
        if (expression is not null)
        {
            var (_, cleanupDiagnosticContext) = SetupDiagnosticContext(expression);
            try
            {
                switch (GetAssignmentDeclarationKind(expression))
                {
                    case JSDeclarationKind.ModuleExports:
                        if (_state._currentSourceFile!.CommonJSModuleIndicator is not null)
                        {
                            var result = TransformExportAssignment(expression.Parent!, expression, expression.AsBinaryExpression()!.Right!, true /*isExportEquals*/);
                            if (result is not null)
                            {
                                _cjsExportAssignment = result;
                                _resultHasScopeMarker = true;
                                _resultHasExternalModuleIndicator = true;
                            }
                        }
                        break;
                }
                return _cjsExportAssignmentVisitor.VisitEachChild(expression); // recur through the whole tree, looking for module.exports=
            }
            finally
            {
                cleanupDiagnosticContext();
            }
        }
        return null;
    }

    internal Node? VisitNestedExpression(Node expression)
    {
        if (expression is not null)
        {
            var (_, cleanupDiagnosticContext) = SetupDiagnosticContext(expression);
            try
            {
                switch (GetAssignmentDeclarationKind(expression))
                {
                    case JSDeclarationKind.Property:
                        TransformExpandoAssignment(expression.AsBinaryExpression()!);
                        break;
                    case JSDeclarationKind.ExportsProperty:
                        if (_state._currentSourceFile!.CommonJSModuleIndicator is not null)
                        {
                            var result = TransformCommonJSExport(expression, GetNameExpressionPreferringIdentifier(GetElementOrPropertyAccessName(expression.AsBinaryExpression()!.Left)!));
                            if (result is not null)
                            {
                                _cjsExportMembers = [.. _cjsExportMembers, result];
                            }
                        }
                        break;
                    case JSDeclarationKind.ObjectDefinePropertyExports:
                        if (_state._currentSourceFile!.CommonJSModuleIndicator is not null)
                        {
                            var result = TransformCommonJSExport(expression, GetNameExpressionPreferringIdentifier(expression.Arguments()![1]));
                            if (result is not null)
                            {
                                _cjsExportMembers = [.. _cjsExportMembers, result];
                            }
                        }
                        break;
                }
                return _expressionVisitor.VisitEachChild(expression); // recur through the whole tree, looking for special assignments
            }
            finally
            {
                cleanupDiagnosticContext();
            }
        }
        return null;
    }

    internal void TransformExpandoAssignment(BinaryExpression node)
    {
        var left = node.Left;

        var symbol = node.Symbol;
        if (symbol is null || (symbol.Flags & SymbolFlags.Assignment) == 0)
        {
            return;
        }

        var ns = GetLeftmostAccessExpression(left);
        if (ns is null || ns.Kind != Kind.Identifier)
        {
            return;
        }

        var declaration = _resolver.GetReferencedValueDeclaration(ns);
        if (declaration is null)
        {
            return;
        }

        if (ShouldStripInternal(declaration))
        {
            return;
        }

        if (IsVariableDeclaration(declaration) && declaration.Type() is not null)
        {
            return;
        }

        if (IsFunctionDeclaration(declaration) && declaration.FunctionLikeData()?.FullSignature is not null)
        {
            return;
        }

        if (IsVariableDeclaration(declaration) && !IsFunctionLike(declaration.Initializer()))
        {
            return; // We're going to add a type, no need to dupe members with a namespace
        }

        var host = declaration.Symbol;
        if (host is null)
        {
            return;
        }

        var name = Factory().NewIdentifier(ns.Text());
        var property = TryGetPropertyName(left!);
        if (property == "" || !IsIdentifierText(property, LanguageVariant.Standard))
        {
            return;
        }

        var hostId = GetExpandoHostId(declaration);

        if (IsDeclaration(declaration) && IsDeclarationAndNotVisible(EmitContext(), _resolver, declaration))
        {
            // The host isn't visible (yet) - printing the type of a visible declaration may still
            // late-mark it as visible (e.g. an exported variable whose type prints as `typeof host`),
            // so defer the assignment to be processed if and when that happens.
            BinaryExpression[] deferredAssignments = _deferredExpandoAssignments.TryGetValue(hostId, out var existingDeferred) ? existingDeferred : [];
            _deferredExpandoAssignments[hostId] = [.. deferredAssignments, node];
            return;
        }

        if (IsFunctionDeclaration(declaration) && !ShouldEmitFunctionProperties(declaration.AsFunctionDeclaration()!))
        {
            return;
        }

        TransformExpandoHost(name, declaration);

        var exportName = Factory().NewIdentifier(property);
        var localName = TryGetNameOfAssignedExpression(node);
        if (localName is null && !_resolver.IsNameResolvable(_enclosingDeclaration!, property) && !IsNonContextualKeyword(StringToToken(exportName.Text())))
        {
            // use exportName as localName if there won't be any conflicts or keyword issues
            localName = exportName;
        }
        if (localName is null || IsNonContextualKeyword(StringToToken(localName.Text())))
        {
            // fallback to a generated name if the localName doesn't exist or is a keyword
            localName = Factory().NewGeneratedNameForNode(node);
        }

        var (_, cleanupDiagnosticContext) = SetupDiagnosticContext(node);
        try
        {
            if (IsIdentifier(node.Right!))
            {
                // alias-like, emit an `export {name}` or `export {name as alias}`
                var aliasResult = TransformBinaryExpressionToExportDeclaration(node, exportName);
                Node[] preexistingMembers = _expandoMembers.TryGetValue(hostId, out var membersBeforeAlias) ? membersBeforeAlias : [];
                _expandoMembers[hostId] = [.. preexistingMembers, aliasResult];
                return;
            }

            Node[] membersForExportCheck = _expandoMembers.TryGetValue(hostId, out var existingMembers) ? existingMembers : [];
            var preexistingExpandoHasExport = Some(membersForExportCheck, IsExportDeclaration);
            ModifierList? varModifiers = null;

            if (preexistingExpandoHasExport)
            {
                varModifiers = Factory().NewModifierList(CreateModifiersFromModifierFlags(ModifierFlags.Export, Factory().NewModifier));
            }

            var synthesizedNamespace = Factory().NewModuleDeclaration(null /*modifiers*/, Kind.NamespaceKeyword, name, null, Factory().NewModuleBlock(Factory().NewNodeList([])));
            synthesizedNamespace.Parent = _enclosingDeclaration;
            var declarationData = synthesizedNamespace.DeclarationData()!;
            declarationData.Symbol = host;
            var containerData = synthesizedNamespace.LocalsContainerData()!;
            var locals = new SymbolTable();
            locals[localName.Text()] = symbol;
            containerData.Locals = locals;

            var oldEnclosing = _enclosingDeclaration;
            _enclosingDeclaration = synthesizedNamespace;
            try
            {
                var statements = new List<Node>
                {
                    Factory().NewVariableStatement(
                        varModifiers,
                        Factory().NewVariableDeclarationList(
                            Factory().NewNodeList(new Node[]
                            {
                                Factory().NewVariableDeclaration(localName, null /*exclamationToken*/, EnsureType(node, false), null /*initializer*/),
                            }),
                            NodeFlags.None)),
                };

                if (localName.Text() != exportName.Text())
                {
                    var namedExports = Factory().NewNamedExports(Factory().NewNodeList(new Node[]
                    {
                        Factory().NewExportSpecifier(false /*isTypeOnly*/, localName, exportName),
                    }));
                    statements.Add(Factory().NewExportDeclaration(null /*modifiers*/, false /*isTypeOnly*/, namedExports, null /*moduleSpecifier*/, null /*attributes*/));
                }

                if (statements.Count > 1 && !preexistingExpandoHasExport)
                {
                    // Add an `export` modifier to all existing expando members so they remain exported after the `export {}` is added
                    Node[] existingExpandoMembers = _expandoMembers.TryGetValue(hostId, out var membersForModifiers) ? membersForModifiers : [];
                    foreach (var decl in existingExpandoMembers)
                    {
                        var modifierFlags = ModifierFlags.Export | GetCombinedModifierFlags(decl);
                        decl.AsMutable().SetModifiers(Factory().NewModifierList(CreateModifiersFromModifierFlags(modifierFlags, Factory().NewModifier)));
                    }
                }
                Node[] currentMembers = _expandoMembers.TryGetValue(hostId, out var membersAtEnd) ? membersAtEnd : [];
                _expandoMembers[hostId] = [.. currentMembers, .. statements];
            }
            finally
            {
                _enclosingDeclaration = oldEnclosing;
            }
        }
        finally
        {
            cleanupDiagnosticContext();
        }
    }

    internal NodeId GetExpandoHostId(Node declaration)
    {
        var root = IfElse(IsVariableDeclaration(declaration), declaration.Parent!.Parent, declaration);
        var id = GetNodeId(EmitContext().MostOriginal(root)!);
        return id;
    }

    internal void TransformExpandoHost(Node name, Node declaration)
    {
        var root = IfElse(IsVariableDeclaration(declaration), declaration.Parent!.Parent, declaration);
        var id = GetExpandoHostId(declaration);

        if (_expandoHosts.ContainsKey(id))
        {
            return;
        }

        var saveNeedsDeclare = _needsDeclare;
        _needsDeclare = true;

        var modifierFlags = EnsureModifierFlags(root!);
        var defaultExport = (modifierFlags & ModifierFlags.Export) != 0 && (modifierFlags & ModifierFlags.Default) != 0;

        _needsDeclare = saveNeedsDeclare;

        if (defaultExport)
        {
            modifierFlags |= ModifierFlags.Ambient;
            modifierFlags ^= ModifierFlags.Default;
            modifierFlags ^= ModifierFlags.Export;
        }

        var (_, cleanupDiagnosticContext) = SetupDiagnosticContext(declaration);
        try
        {
            var modifiers = Factory().NewModifierList(CreateModifiersFromModifierFlags(modifierFlags, Factory().NewModifier));
            var replacement = new List<Node>();

            if (IsFunctionDeclaration(declaration))
            {
                var (typeParameters, parameters, asteriskToken) = ExtractExpandoHostParams(declaration);
                replacement.Add(Factory().UpdateFunctionDeclaration(declaration.AsFunctionDeclaration()!, modifiers, asteriskToken, declaration.Name(), EnsureTypeParams(declaration, typeParameters), UpdateParamList(declaration, parameters), EnsureType(declaration, false), null /*fullSignature*/, null /*body*/));
            }
            else if (IsVariableDeclaration(declaration) && IsFunctionExpressionOrArrowFunction(declaration.Initializer()))
            {
                var fn = declaration.Initializer()!;
                var (typeParameters, parameters, asteriskToken) = ExtractExpandoHostParams(fn);
                replacement.Add(Factory().NewFunctionDeclaration(modifiers, asteriskToken, Factory().NewIdentifier(name.Text()), EnsureTypeParams(fn, typeParameters), UpdateParamList(fn, parameters), EnsureType(fn, false), null /*fullSignature*/, null /*body*/));
            }
            else
            {
                _expandoHosts[id] = TransformTopLevelDeclaration(declaration);
                return;
            }

            _state._reportExpandoFunctionErrors!(declaration);

            if (defaultExport)
            {
                if (IsSourceFile(declaration.Parent!))
                {
                    _resultHasExternalModuleIndicator = true;
                }
                _resultHasScopeMarker = true;
                replacement.Add(Factory().NewExportAssignment(null /*modifiers*/, false /*isExportEquals*/, null /*typeNode*/, name));
            }

            // store host result to be added to the output when it's actually visited
            _expandoHosts[id] = Factory().NewSyntaxList(replacement.ToArray());
            if (_lateStatementReplacementMap.ContainsKey(id))
            {
                _lateStatementReplacementMap[id] = CreateFullExpandoBlock(id);
            }
        }
        finally
        {
            cleanupDiagnosticContext();
        }
    }

    internal Node? CreateFullExpandoBlock(NodeId id)
    {
        // Process any expando assignments on this host that were skipped because it wasn't
        // visible when they were collected - if it's still not visible, they simply get
        // re-deferred, and are dropped if the host is never late-marked visible.
        if (_deferredExpandoAssignments.TryGetValue(id, out var deferred))
        {
            _deferredExpandoAssignments.Remove(id);
            foreach (var assignment in deferred)
            {
                TransformExpandoAssignment(assignment);
            }
        }
        var n = _expandoHosts.TryGetValue(id, out var host) ? host : null;
        if (_expandoMembers.TryGetValue(id, out var addOns))
        {
            ModifierList? modifiers = null;
            Node? name = null;
            Node[]? expandoHost;
            if (n is not null && n.Kind == Kind.SyntaxList)
            {
                // find the first named syntax list element and use its' name & modifiers
                foreach (var c in n.AsSyntaxList()!.IterChildren())
                {
                    if (c.Name() is not null)
                    {
                        name = c.Name()!.Clone(Factory());
                        if (c.Modifiers() is not null)
                        {
                            modifiers = c.Modifiers()!.Clone(Factory().AsNodeFactory());
                        }
                        break;
                    }
                }
                expandoHost = n.AsSyntaxList()!.Children;
            }
            else if (n is not null)
            {
                name = n.Name()!.Clone(Factory());
                if (n.Modifiers() is not null)
                {
                    modifiers = n.Modifiers()!.Clone(Factory().AsNodeFactory());
                }
                expandoHost = [n];
            }
            else
            {
                expandoHost = null;
            }
            if (name is not null)
            {
                var moduleDecl = Factory().NewModuleDeclaration(
                    modifiers,
                    Kind.NamespaceKeyword,
                    name,
                    null,
                    Factory().NewModuleBlock(Factory().NewNodeList(addOns)));
                Node[] members = [.. expandoHost ?? [], moduleDecl];
                return Factory().NewSyntaxList(members);
            }
        }
        return n;
    }

    internal string TryGetPropertyName(Node node)
    {
        if (IsElementAccessExpression(node))
        {
            return _resolver.GetElementAccessExpressionName(node.AsElementAccessExpression()!);
        }
        if (IsPropertyAccessExpression(node))
        {
            return node.Name()!.Text();
        }
        return "";
    }
}

public static partial class Declarations
{
    internal static bool IsClassExtendingNull(Node? node)
    {
        if (node is null)
        {
            return false;
        }
        var extendsClause = GetHeritageClause(node, Kind.ExtendsKeyword);
        if (extendsClause is null)
        {
            return false;
        }
        var types = extendsClause.AsHeritageClause()!.Types;
        if (types?.Nodes is null || types.Nodes.Length != 1)
        {
            return false;
        }
        var expr = types.Nodes[0].AsExpressionWithTypeArguments()!.Expression;
        return expr is not null && expr.Kind == Kind.NullKeyword;
    }

    internal static bool IsNotDeclareModifier(Node mod)
    {
        return mod.Kind != Kind.DeclareKeyword;
    }

    internal static (NodeList? TypeParameters, NodeList? Parameters, Node? AsteriskToken) ExtractExpandoHostParams(Node node)
    {
        switch (node.Kind)
        {
            case Kind.FunctionExpression:
                var fnExpression = node.AsFunctionExpression()!;
                return (fnExpression.TypeParameters, fnExpression.Parameters, fnExpression.AsteriskToken);
            case Kind.ArrowFunction:
                var arrowFunction = node.AsArrowFunction()!;
                return (arrowFunction.TypeParameters, arrowFunction.Parameters, arrowFunction.AsteriskToken);
            default:
                var functionDeclaration = node.AsFunctionDeclaration()!;
                return (functionDeclaration.TypeParameters, functionDeclaration.Parameters, functionDeclaration.AsteriskToken);
        }
    }
}
