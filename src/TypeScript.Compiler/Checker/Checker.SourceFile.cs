// ported from: internal/checker/checker.go:2238-2744
//   （checkSourceFile、checkSourceElements/Element/ElementWorker/Unreachable、checkNodeDeferred、
//     checkDeferredNodes/Node、JSDoc 注释与类型检查、checkTypeParameter(+Deferred)、shouldCheckErasableSyntax、
//     checkParameter）
//
// 与 Go 的偏离（逐条）：
// 1) `if tr := c.tracer; tr != nil { defer tr.Push(...)() }` → `var pop = _tracer?.Push(...)` + try/finally
//    （C# 没有 defer；Tracer.Push 返回的 `Action` 就是要 defer 执行的那个 pop）。
// 2) `tracing.PhaseCheck`/`PhaseCheckTypes` → `Phase.Check`/`Phase.CheckTypes`（Tracing/Tracing.cs 的 static class Phase）。
// 3) `slices.Index(xs, x)` → `Array.IndexOf(xs, x)`。
// 4) `collections.OrderedSet[*ast.Node]{}` → `new OrderedSet<Node>()`；`links.deferredNodes.Values()` → `Values()`。
// 5) `node.Symbol()` → `node.Symbol`（Ast/Node.cs 把它落成**属性**，见 docs/ast.md §2）。
// 6) 命中 artifacts/checker-method-collisions.md 的未导出成员改名：`getBaseConstraintOfType`→`GetBaseConstraintOfTypeWorker`、
//    `getConstraintOfTypeParameter`→`GetConstraintOfTypeParameterWorker`、`getDefaultFromTypeParameter`→`GetDefaultFromTypeParameterWorker`、
//    `getTypeFromTypeNode`→`GetTypeFromTypeNodeWorker`、`getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`getReducedType`→`GetReducedTypeWorker`、
//    `isTypeAssignableTo`→`IsTypeAssignableToWorker`、`typeToString`→`TypeToStringWorker`。
// 7) `ast.EagerJSDoc(nil)` → `node.EagerJSDoc(null)`（C# 签名 `EagerJSDoc(SourceFile?)`）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    /// <summary>Go: checker.go:2238 <c>checkSourceFile</c>。</summary>
    internal void CheckSourceFile(Context? ctx, SourceFile sourceFile, bool checkUnused)
    {
        _ctx = ctx;
        var links = _sourceFileLinks.Get(sourceFile);
        if (!links._typeChecked)
        {
            var pop = _tracer?.Push(Phase.Check, "checkSourceFile", new Dictionary<string, object?> { ["path"] = sourceFile.FileName() }, true);
            try
            {
                // Grammar checking
                CheckGrammarSourceFile(sourceFile);
                _renamedBindingElementsInTypes = [];
                // Go checker.go:2248 `checkSourceElements(sourceFile.Statements.Nodes)`：Statements 由 parser 恒非 nil（Go 同处直接解引用），
                // 而 Nodes 是 [] 切片，Go 的 range 对 nil 合法 ⇒ 用 `?? []`。
                CheckSourceElements(sourceFile.Statements!.Nodes ?? []);
                CheckDeferredNodes(sourceFile);
                if (IsExternalOrCommonJSModule(sourceFile))
                {
                    CheckExternalModuleExports(sourceFile);
                    RegisterForUnusedIdentifiersCheck(sourceFile);
                }
                if (!sourceFile.IsDeclarationFile && !IsCanceled())
                {
                    CheckUnusedRenamedBindingElements();
                }
                ProduceDeferredDiagnostics();
                _reportedUnreachableNodes.Clear();
                links._typeChecked = true;
            }
            finally
            {
                pop?.Invoke();
            }
        }
        if (checkUnused && !links._unusedChecked)
        {
            // The unused identifiers check relies on a full type check having first been performed
            if (!sourceFile.IsDeclarationFile && !IsCanceled())
            {
                CheckUnusedIdentifiers(links._identifierCheckNodes);
            }
            links._unusedChecked = true;
        }
        if (IsCanceled())
        {
            _wasCanceled = true;
        }
        _ctx = null;
    }

    /// <summary>Go: checker.go:2274 <c>checkSourceElements</c>。</summary>
    internal void CheckSourceElements(Node[] nodes)
    {
        foreach (var node in nodes)
        {
            if (IsCanceled())
            {
                break;
            }
            CheckSourceElement(node);
        }
    }

    /// <summary>Go: checker.go:2283 <c>checkSourceElement</c>。</summary>
    internal bool CheckSourceElement(Node? node)
    {
        if (node is not null)
        {
            var saveCurrentNode = _currentNode;
            var saveWithinUnreachableCode = _withinUnreachableCode;
            _currentNode = node;
            _instantiationCount = 0;
            CheckSourceElementWorker(node);
            _currentNode = saveCurrentNode;
            _withinUnreachableCode = saveWithinUnreachableCode;
        }
        return false;
    }

    /// <summary>Go: checker.go:2296 <c>checkSourceElementWorker</c>。</summary>
    internal void CheckSourceElementWorker(Node node)
    {
        foreach (var jsdoc in node.EagerJSDoc(null) ?? [])
        {
            CheckJSDocComments(jsdoc);
            if (jsdoc.AsJSDoc()!.Tags is { } tags)
            {
                foreach (var tag in tags.Nodes ?? [])
                {
                    CheckJSDocComments(tag);
                }
            }
        }

        if (!_withinUnreachableCode && _compilerOptions.AllowUnreachableCode != Tristate.TSTrue)
        {
            if (CheckSourceElementUnreachable(node))
            {
                _withinUnreachableCode = true;
            }
        }

        switch (node.Kind)
        {
            case Kind.TypeParameter:
                CheckTypeParameter(node);
                break;
            case Kind.Parameter:
                CheckParameter(node);
                break;
            case Kind.PropertyDeclaration:
                CheckPropertyDeclaration(node);
                break;
            case Kind.PropertySignature:
                CheckPropertySignature(node);
                break;
            case Kind.ConstructorType:
            case Kind.FunctionType:
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.IndexSignature:
                CheckSignatureDeclaration(node);
                break;
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
                CheckMethodDeclaration(node);
                break;
            case Kind.ClassStaticBlockDeclaration:
                CheckClassStaticBlockDeclaration(node);
                break;
            case Kind.Constructor:
                CheckConstructorDeclaration(node);
                break;
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                CheckAccessorDeclaration(node);
                break;
            case Kind.TypeReference:
                CheckTypeReferenceNode(node);
                break;
            case Kind.TypePredicate:
                CheckTypePredicate(node);
                break;
            case Kind.TypeQuery:
                CheckTypeQuery(node);
                break;
            case Kind.TypeLiteral:
                CheckTypeLiteral(node);
                break;
            case Kind.ArrayType:
                CheckArrayType(node);
                break;
            case Kind.TupleType:
                CheckTupleType(node);
                break;
            case Kind.UnionType:
            case Kind.IntersectionType:
                CheckUnionOrIntersectionType(node);
                break;
            case Kind.ParenthesizedType:
            case Kind.OptionalType:
            case Kind.RestType:
                node.ForEachChild(CheckSourceElement);
                break;
            case Kind.ThisType:
                CheckThisType(node);
                break;
            case Kind.TypeOperator:
                CheckTypeOperator(node);
                break;
            case Kind.ConditionalType:
                CheckConditionalType(node);
                break;
            case Kind.InferType:
                CheckInferType(node);
                break;
            case Kind.TemplateLiteralType:
                CheckTemplateLiteralType(node);
                break;
            case Kind.ImportType:
                CheckImportType(node);
                break;
            case Kind.NamedTupleMember:
                CheckNamedTupleMember(node);
                break;
            case Kind.IndexedAccessType:
                CheckIndexedAccessType(node);
                break;
            case Kind.MappedType:
                CheckMappedType(node);
                break;
            case Kind.FunctionDeclaration:
                CheckFunctionDeclaration(node);
                break;
            case Kind.Block:
            case Kind.ModuleBlock:
                CheckBlock(node);
                break;
            case Kind.VariableStatement:
                CheckVariableStatement(node);
                break;
            case Kind.ExpressionStatement:
                CheckExpressionStatement(node);
                break;
            case Kind.IfStatement:
                CheckIfStatement(node);
                break;
            case Kind.DoStatement:
                CheckDoStatement(node);
                break;
            case Kind.WhileStatement:
                CheckWhileStatement(node);
                break;
            case Kind.ForStatement:
                CheckForStatement(node);
                break;
            case Kind.ForInStatement:
                CheckForInStatement(node);
                break;
            case Kind.ForOfStatement:
                CheckForOfStatement(node);
                break;
            case Kind.ContinueStatement:
            case Kind.BreakStatement:
                CheckBreakOrContinueStatement(node);
                break;
            case Kind.ReturnStatement:
                CheckReturnStatement(node);
                break;
            case Kind.WithStatement:
                CheckWithStatement(node);
                break;
            case Kind.SwitchStatement:
                CheckSwitchStatement(node);
                break;
            case Kind.LabeledStatement:
                CheckLabeledStatement(node);
                break;
            case Kind.ThrowStatement:
                CheckThrowStatement(node);
                break;
            case Kind.TryStatement:
                CheckTryStatement(node);
                break;
            case Kind.VariableDeclaration:
                CheckVariableDeclaration(node);
                break;
            case Kind.BindingElement:
                CheckBindingElement(node);
                break;
            case Kind.ClassDeclaration:
                CheckClassDeclaration(node);
                break;
            case Kind.InterfaceDeclaration:
                CheckInterfaceDeclaration(node);
                break;
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                CheckTypeAliasDeclaration(node);
                break;
            case Kind.EnumDeclaration:
                CheckEnumDeclaration(node);
                break;
            case Kind.EnumMember:
                CheckEnumMember(node);
                break;
            case Kind.ModuleDeclaration:
                CheckModuleDeclaration(node);
                break;
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                CheckImportDeclaration(node);
                break;
            case Kind.ImportEqualsDeclaration:
                CheckImportEqualsDeclaration(node);
                break;
            case Kind.ExportDeclaration:
                CheckExportDeclaration(node);
                break;
            case Kind.ExportAssignment:
                CheckExportAssignment(node);
                break;
            case Kind.EmptyStatement:
                CheckGrammarStatementInAmbientContext(node);
                break;
            case Kind.DebuggerStatement:
                CheckGrammarStatementInAmbientContext(node);
                break;
            case Kind.MissingDeclaration:
                CheckMissingDeclaration(node);
                break;
            case Kind.JSDocNonNullableType:
            case Kind.JSDocNullableType:
            case Kind.JSDocAllType:
            case Kind.JSDocTypeLiteral:
                CheckJSDocType(node);
                break;
        }
    }

    /// <summary>Go: checker.go:2434 <c>checkSourceElementUnreachable</c>。</summary>
    internal bool CheckSourceElementUnreachable(Node node)
    {
        if (!IsPotentiallyExecutableNode(node))
        {
            return false;
        }

        if (_reportedUnreachableNodes.Has(node))
        {
            return true;
        }

        if (!IsSourceElementUnreachable(node))
        {
            return false;
        }

        _reportedUnreachableNodes.Add(node);

        var sourceFile = GetSourceFileOfNode(node)!;

        var startNode = node;
        var endNode = node;

        var parent = node.Parent!;
        if (parent.CanHaveStatements())
        {
            var statements = parent.Statements();
            if (statements is not null)
            {
                var offset = Array.IndexOf(statements, node);
                if (offset >= 0)
                {
                    // Scan backwards to find the first unreachable unreported node;
                    // this may happen when producing region diagnostics where not all nodes
                    // will have been visited.
                    // TODO: enable this code once we support region diagnostics again.
                    var first = offset;
                    var last = offset;
                    for (var i = offset + 1; i < statements.Length; i++)
                    {
                        var nextNode = statements[i];
                        if (!IsPotentiallyExecutableNode(nextNode) || !IsSourceElementUnreachable(nextNode))
                        {
                            break;
                        }
                        last = i;
                        _reportedUnreachableNodes.Add(nextNode);
                    }

                    startNode = statements[first];
                    endNode = statements[last];
                }
            }
        }

        var start = Scanner.Scanner.GetTokenPosOfNode(startNode, sourceFile, false /*includeJSDoc*/);

        var diagnostic = NewDiagnostic(sourceFile, NewTextRange(start, endNode.End()), Diagnostics.Unreachable_code_detected);
        AddErrorOrSuggestion(_compilerOptions.AllowUnreachableCode == Tristate.TSFalse, diagnostic);

        return true;
    }

    /// <summary>Go: checker.go:2495 <c>isSourceElementUnreachable</c>。</summary>
    internal bool IsSourceElementUnreachable(Node node)
    {
        // Precondition: ast.IsPotentiallyExecutableNode is true
        if ((node.Flags & NodeFlags.Unreachable) != 0)
        {
            // The binder has determined that this code is unreachable.
            // Ignore const enums unless preserveConstEnums is set.
            switch (node.Kind)
            {
                case Kind.EnumDeclaration:
                    return !IsEnumConst(node) || _compilerOptions.ShouldPreserveConstEnums();
                case Kind.ModuleDeclaration:
                    return IsInstantiatedModule(node, _compilerOptions.ShouldPreserveConstEnums());
                default:
                    return true;
            }
        }
        // Go checker.go:2508 `node.FlowNodeData().FlowNode`：前置条件 IsPotentiallyExecutableNode ⇒ Go 侧 FlowNodeData() 非 nil。
        else if (node.FlowNodeData()!.FlowNode is { } flowNode)
        {
            // For code the binder doesn't know is unreachable, use control flow / types.
            return !IsReachableFlowNode(flowNode);
        }
        return false;
    }

    // Function and class expression bodies are checked after all statements in the enclosing body. This is
    // to ensure constructs like the following are permitted:
    //
    //	const foo = function () {
    //	   const s = foo();
    //	   return "hello";
    //	}
    //
    // Here, performing a full type check of the body of the function expression whilst in the process of
    // determining the type of foo would cause foo to be given type any because of the recursive reference.
    // Delaying the type check of the body ensures foo has been assigned a type.
    /// <summary>Go: checker.go:2526 <c>checkNodeDeferred</c>。</summary>
    internal void CheckNodeDeferred(Node node)
    {
        var enclosingFile = GetSourceFileOfNode(node)!;
        var links = _sourceFileLinks.Get(enclosingFile);
        if (!links._typeChecked)
        {
            links._deferredNodes.Add(node);
        }
    }

    /// <summary>Go: checker.go:2534 <c>checkDeferredNodes</c>。</summary>
    internal void CheckDeferredNodes(SourceFile context)
    {
        var links = _sourceFileLinks.Get(context);
        foreach (var node in links._deferredNodes.Values())
        {
            if (IsCanceled())
            {
                break;
            }
            CheckDeferredNode(node);
        }
        links._deferredNodes = new OrderedSet<Node>();
    }

    /// <summary>Go: checker.go:2545 <c>checkDeferredNode</c>。</summary>
    internal void CheckDeferredNode(Node node)
    {
        var pop = _tracer?.Push(Phase.Check, "checkDeferredNode", new Dictionary<string, object?>
        {
            ["kind"] = node.Kind,
            ["pos"] = node.Pos(),
            ["end"] = node.End(),
            ["path"] = GetSourceFileOfNode(node)!.FileName(),
        }, false);
        try
        {
            var saveCurrentNode = _currentNode;
            _currentNode = node;
            _instantiationCount = 0;
            switch (node.Kind)
            {
                case Kind.CallExpression:
                case Kind.NewExpression:
                case Kind.TaggedTemplateExpression:
                case Kind.Decorator:
                case Kind.JsxOpeningElement:
                    // These node kinds are deferred checked when overload resolution fails. To save on work,
                    // we ensure the arguments are checked just once in a deferred way.
                    ResolveUntypedCall(node);
                    break;
                case Kind.FunctionExpression:
                case Kind.ArrowFunction:
                case Kind.MethodDeclaration:
                case Kind.MethodSignature:
                    CheckFunctionExpressionOrObjectLiteralMethodDeferred(node);
                    break;
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                    CheckAccessorDeclaration(node);
                    break;
                case Kind.ClassExpression:
                    CheckClassExpressionDeferred(node);
                    break;
                case Kind.TypeParameter:
                    CheckTypeParameterDeferred(node);
                    break;
                case Kind.JsxSelfClosingElement:
                    CheckJsxSelfClosingElementDeferred(node);
                    break;
                case Kind.JsxElement:
                    CheckJsxElementDeferred(node);
                    break;
                case Kind.TypeAssertionExpression:
                case Kind.AsExpression:
                    CheckAssertionDeferred(node);
                    break;
                case Kind.VoidExpression:
                    CheckExpression(node.Expression()!);
                    break;
                case Kind.BinaryExpression:
                    if (IsInstanceOfExpression(node))
                    {
                        ResolveUntypedCall(node);
                    }
                    break;
                case Kind.ObjectLiteralExpression:
                case Kind.JsxAttributes:
                    CheckContextualDeprecations(node);
                    break;
            }
            _currentNode = saveCurrentNode;
        }
        finally
        {
            pop?.Invoke();
        }
    }

    /// <summary>Go: checker.go:2583 <c>checkJSDocComments</c>。</summary>
    internal void CheckJSDocComments(Node node)
    {
        foreach (var comment in node.Comments() ?? [])
        {
            CheckJSDocComment(comment);
        }
    }

    /// <summary>Go: checker.go:2589 <c>checkJSDocComment</c>。</summary>
    internal void CheckJSDocComment(Node node)
    {
        // This performs minimal checking of JSDoc nodes to ensure that @link references to entities are recorded
        // for purposes of checking unused identifiers.
        switch (node.Kind)
        {
            case Kind.JSDocLink:
            case Kind.JSDocLinkCode:
            case Kind.JSDocLinkPlain:
                ResolveJSDocMemberName(node.Name());
                break;
        }
    }

    /// <summary>Go: checker.go:2598 <c>resolveJSDocMemberName</c>。</summary>
    internal Symbol? ResolveJSDocMemberName(Node? name)
    {
        if (name is not null && IsEntityName(name))
        {
            var meaning = SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Value;
            var symbol = ResolveEntityName(name, meaning, true /*ignoreErrors*/, true /*dontResolveAlias*/, GetHostSignatureFromJSDoc(name));
            if (symbol is not null)
            {
                return symbol;
            }
            if (IsQualifiedName(name))
            {
                var left = ResolveJSDocMemberName(name.AsQualifiedName()!.Left);
                if (left is not null)
                {
                    Type? t = null;
                    if ((left.Flags & SymbolFlags.Value) != 0)
                    {
                        var proto = GetPropertyOfTypeWorker(GetTypeOfSymbolWorker(left), "prototype");
                        if (proto is not null)
                        {
                            t = GetTypeOfSymbolWorker(proto);
                        }
                    }
                    t ??= GetDeclaredTypeOfSymbolWorker(left);
                    return GetPropertyOfTypeWorker(t, name.AsQualifiedName()!.Right!.Text());
                }
            }
        }
        return null;
    }

    /// <summary>Go: checker.go:2623 <c>checkJSDocType</c>。</summary>
    internal void CheckJSDocType(Node node)
    {
        CheckJSDocTypeIsInJsFile(node);
        node.ForEachChild(CheckSourceElement);
    }

    /// <summary>Go: checker.go:2628 <c>checkJSDocTypeIsInJsFile</c>。</summary>
    internal void CheckJSDocTypeIsInJsFile(Node node)
    {
        if (!IsInJSFile(node))
        {
            if (IsJSDocNonNullableType(node) || IsJSDocNullableType(node))
            {
                var token = IfElse(IsJSDocNonNullableType(node), "!", "?");
                var postfix = node.Pos() == node.Type()!.Pos();
                var message = IfElse(postfix,
                    Diagnostics.X_0_at_the_end_of_a_type_is_not_valid_TypeScript_syntax_Did_you_mean_to_write_1,
                    Diagnostics.X_0_at_the_start_of_a_type_is_not_valid_TypeScript_syntax_Did_you_mean_to_write_1);
                var t = GetTypeFromTypeNodeWorker(node.Type()!);
                if (IsJSDocNullableType(node) && !ReferenceEquals(t, _neverType) && !ReferenceEquals(t, _voidType))
                {
                    t = GetNullableType(t, IfElse(postfix, TypeFlags.Undefined, TypeFlags.Nullable));
                }
                GrammarErrorOnNode(node, message, token, TypeToString(t));
            }
            else
            {
                GrammarErrorOnNode(node, Diagnostics.JSDoc_types_can_only_be_used_inside_documentation_comments);
            }
        }
    }

    /// <summary>Go: checker.go:2647 <c>checkTypeParameter</c>。</summary>
    internal void CheckTypeParameter(Node node)
    {
        // Grammar Checking
        CheckGrammarModifiers(node);
        if (node.AsTypeParameterDeclaration()!.Expression is { } expr)
        {
            GrammarErrorOnFirstToken(expr, Diagnostics.Type_expected);
        }
        var tpNode = node.AsTypeParameterDeclaration()!;
        CheckSourceElement(tpNode.Constraint);
        CheckSourceElement(tpNode.DefaultType);
        var typeParameter = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node)!);
        // Resolve base constraint to reveal circularity errors
        GetBaseConstraintOfTypeWorker(typeParameter);
        if (ReferenceEquals(GetResolvedTypeParameterDefault(typeParameter), _circularConstraintType))
        {
            Error(tpNode.DefaultType, Diagnostics.Type_parameter_0_has_a_circular_default, TypeToString(typeParameter));
        }
        var constraintType = GetConstraintOfTypeParameterWorker(typeParameter);
        var defaultType = GetDefaultFromTypeParameterWorker(typeParameter);
        if (constraintType is not null && defaultType is not null)
        {
            CheckTypeAssignableTo(defaultType, GetTypeWithThisArgument(InstantiateType(constraintType, NewSimpleTypeMapper(typeParameter, defaultType)), defaultType, false), tpNode.DefaultType, Diagnostics.Type_0_does_not_satisfy_the_constraint_1);
        }
        CheckTypeNameIsReserved(node.Name()!, Diagnostics.Type_parameter_name_cannot_be_0);
        CheckNodeDeferred(node);
    }

    /// <summary>Go: checker.go:2671 <c>checkTypeParameterDeferred</c>。</summary>
    internal void CheckTypeParameterDeferred(Node node)
    {
        if (IsInterfaceDeclaration(node.Parent!) || IsClassLike(node.Parent!) || IsTypeOrJSTypeAliasDeclaration(node.Parent!))
        {
            var typeParameter = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node)!);
            var modifiers = GetTypeParameterModifiers(typeParameter) & (ModifierFlags.In | ModifierFlags.Out);
            if (modifiers != 0)
            {
                var symbol = GetSymbolOfDeclaration(node.Parent!)!;
                if (IsTypeOrJSTypeAliasDeclaration(node.Parent!) &&
                    (GetDeclaredTypeOfSymbolWorker(symbol)._objectFlags & (ObjectFlags.Anonymous | ObjectFlags.Mapped)) == 0)
                {
                    Error(node, Diagnostics.Variance_annotations_are_only_supported_in_type_aliases_for_object_function_constructor_and_mapped_types);
                }
                else if (modifiers == ModifierFlags.In || modifiers == ModifierFlags.Out)
                {
                    var pop = _tracer?.Push(Phase.CheckTypes, "checkTypeParameterDeferred", new Dictionary<string, object?>
                    {
                        ["parent"] = GetDeclaredTypeOfSymbolWorker(symbol)._id,
                        ["id"] = typeParameter._id,
                    }, false);
                    try
                    {
                        var source = CreateMarkerType(symbol, typeParameter, IfElse(modifiers == ModifierFlags.Out, _markerSubTypeForCheck, _markerSuperTypeForCheck));
                        var target = CreateMarkerType(symbol, typeParameter, IfElse(modifiers == ModifierFlags.Out, _markerSuperTypeForCheck, _markerSubTypeForCheck));
                        var saveVarianceTypeParameter = typeParameter;
                        _varianceTypeParameter = typeParameter;
                        CheckTypeAssignableTo(source, target, node, Diagnostics.Type_0_is_not_assignable_to_type_1_as_implied_by_variance_annotation);
                        _varianceTypeParameter = saveVarianceTypeParameter;
                    }
                    finally
                    {
                        pop?.Invoke();
                    }
                }
            }
        }
    }

    /// <summary>Go: checker.go:2694 <c>shouldCheckErasableSyntax</c>。</summary>
    internal bool ShouldCheckErasableSyntax(Node node)
    {
        return _compilerOptions.ErasableSyntaxOnly.IsTrue() && !IsInJSFile(node);
    }

    /// <summary>Go: checker.go:2698 <c>checkParameter</c>。</summary>
    internal void CheckParameter(Node node)
    {
        // Grammar checking
        // It is a SyntaxError if the Identifier "eval" or the Identifier "arguments" occurs as the
        // Identifier in a PropertySetParameterList of a PropertyAssignment that is contained in strict code
        // or if its FunctionBody is strict code(11.1.5).
        CheckGrammarModifiers(node);
        CheckVariableLikeDeclaration(node);
        var fn = GetContainingFunction(node)!;
        var paramName = "";
        if (node.Name() is { } nameNode && IsIdentifier(nameNode))
        {
            paramName = nameNode.Text();
        }
        if (HasSyntacticModifier(node, ModifierFlags.ParameterPropertyModifier))
        {
            if (ShouldCheckErasableSyntax(node))
            {
                Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
            }
            if (!(IsConstructorDeclaration(fn) && NodeIsPresent(fn.Body()!)))
            {
                Error(node, Diagnostics.A_parameter_property_is_only_allowed_in_a_constructor_implementation);
            }
            if (IsConstructorDeclaration(fn) && paramName == "constructor")
            {
                Error(node.Name(), Diagnostics.X_constructor_cannot_be_used_as_a_parameter_property_name);
            }
        }
        if (node.Initializer() is null && IsOptionalDeclaration(node) && IsBindingPattern(node.Name()!) && fn.Body() is not null)
        {
            Error(node, Diagnostics.A_binding_pattern_parameter_cannot_be_optional_in_an_implementation_signature);
        }
        if (paramName == "this" || paramName == "new")
        {
            if (Array.IndexOf(fn.Parameters()!, node) != 0)
            {
                Error(node, Diagnostics.A_0_parameter_must_be_the_first_parameter, paramName);
            }
            if (IsConstructorDeclaration(fn) || IsConstructSignatureDeclaration(fn) || IsConstructorTypeNode(fn))
            {
                Error(node, Diagnostics.A_constructor_cannot_have_a_this_parameter);
            }
            if (IsArrowFunction(fn))
            {
                Error(node, Diagnostics.An_arrow_function_cannot_have_a_this_parameter);
            }
            if (IsAccessor(fn))
            {
                Error(node, Diagnostics.X_get_and_set_accessors_cannot_declare_this_parameters);
            }
        }
        // Only check rest parameter type if it's not a binding pattern. Since binding patterns are
        // not allowed in a rest parameter, we already have an error from checkGrammarParameterList.
        if (HasDotDotDotToken(node) && !IsBindingPattern(node.Name()!) &&
            !IsTypeAssignableToWorker(GetReducedTypeWorker(GetTypeOfSymbolWorker(node.Symbol!)), _anyReadonlyArrayType))
        {
            Error(node, Diagnostics.A_rest_parameter_must_be_of_an_array_type);
        }
    }
}
