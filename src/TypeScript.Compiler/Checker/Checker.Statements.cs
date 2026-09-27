// ported from: internal/checker/checker.go:3429-4057
//   （函数/方法声明与重载一致性检查、非 void 函数"所有代码路径"检查、语句检查：
//     checkFunctionDeclaration、checkFunctionOrMethodDeclaration、checkFunctionOrConstructorSymbol、
//     checkFunctionOrConstructorSymbolWorker、getEffectiveDeclarationFlags、isImplementationCompatibleWithOverload、
//     checkAllCodePathsInNonVoidFunctionReturnOrThrow、isUnwrappedReturnTypeUndefinedVoidOrAny、checkBlock、
//     checkIfStatement、checkTestingKnownTruthyCallableOrAwaitableOrEnumMemberType、checkTestingKnownTruthyTypes、
//     checkTestingKnownTruthyType、isSymbolUsedInBinaryExpressionChain、isSymbolUsedInConditionBody、
//     checkDoStatement、checkWhileStatement、checkForStatement、checkForInStatement、getIndexTypeOrString）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 命中 artifacts/checker-method-collisions.md 的未导出成员改名：`getEffectiveDeclarationFlags` →
//    `GetEffectiveDeclarationFlagsWorker`（与之配对的导出版 `GetEffectiveDeclarationFlags` 在 exports.go:207）。
// 2) `checkFunctionOrConstructorSymbolWorker` 在 Go 里**没有** `break <label>`（全仓 `grep "\bbreak [A-Za-z]"`
//    0 命中），故本文件的 `break` 都是普通 break，无需 `goto`/局部函数重写。
// 3) Go 的 `map[*ast.SourceFile][]*ast.Node` → `Dictionary<SourceFile, List<Node>>`；`m[k]=append(...)` →
//    `TryGetValue` + `Add`；遍历值用 `groups.Values`（Go 的 map 迭代顺序未定义，C# Dictionary 按插入顺序枚举，
//    只影响同一轮错误的报告顺序）。
// 4) `[]*ast.Diagnostic` + append → `List<Diagnostic>` + `ToArray()`（`Diagnostic.SetRelatedInfo` 收 `Diagnostic[]`）。
// 5) 可空数组：`symbol.Declarations`（Go 的 nil slice：迭代 0 次、len 0）→ `symbol.Declarations ?? []`；
//    `node.Statements()`/`Declarations.Nodes` 传给收 `Node[]` 的 `CheckSourceElements` 时写 `!`（AST 不变量）。
// 6) `if data := fn.FunctionLikeData(); data != nil && ...` → 展开成 `var data = ...; if (data is not null && ...)`
//    （Go 的 if-初始化语句在 C# 没有对应物）。
// 7) Go 的 `switch { case <bool 表达式>: }` → if/else if 链（`checkAllCodePathsInNonVoidFunctionReturnOrThrow`、
//    `checkTestingKnownTruthyType` 的两处）；case 自上而下逐个求值，与 Go 的 switch 等价。
// 8) Go 的 `for ast.IsBinaryExpression(condExpr) && ...` 循环体里 `condExpr = ast.SkipParentheses(...)`（返回可空）
//    → C# 用局部 `Node?` + `!`；`node = node.Parent` 循环 → 局部 `Node? current` + `current is not null` 守卫
//    （Go 在 Parent 为 nil 时会 nil 解引用 panic，C# 走安全分支退出；与 Checker.SymbolResolution.cs 同款处理）。
// 9) 局部函数：Go 的闭包变量 `getCanonicalOverload`/`checkFlagAgreementBetweenOverloads`/
//    `checkQuestionTokenAgreementBetweenOverloads`/`reportImplementationExpectedError` → C# 局部函数（PascalCase，
//    仓库先例 Parser/ParserJsDoc.cs 的 `PushComment`）；Go 的闭包 `visit` → 局部函数 `Visit`
//    （同款先例 Checker.C2.AliasUnused.cs:482）。局部函数的形参与外层局部同名是合法的（实测无 CS0136）。
// 10) `reportImplementationExpectedError` 内层局部的 `errorNode` 与方法末尾外层的 `errorNode` 同名 → C# 嵌套作用域
//     同名局部是 CS0136（已实测），故内层改名为 `subsequentErrorNode`（语义不变）。
// 11) `scanner.DeclarationNameToString` → `TsScanner.DeclarationNameToString`（裸 `Scanner` 在 `TypeScript.Checker`
//     里会解析成命名空间 `TypeScript.Scanner`）。
// 12) `evaluator.IsTruthy` → `IsTruthy`（`using static TypeScript.Evaluator.EvaluatorFunctions;`）。
// 13) `t.AsLiteralType().value` → `t.AsLiteralType()!._value`（C# 的 `As*` 返回可空；字段名照 Go 未导出字段 `_value`）。
// 14) `getSignaturesOfType` 的 C# 契约返回 `Signature[]?` → `len(callSignatures) == 0` 写成
//     `(callSignatures?.Length ?? 0) == 0`（Go 的 len(nil slice) == 0）。
// 15) `core.OrElse`/`core.IfElse` → `OrElse`/`IfElse`；`core.TSTrue` 比较 → `Tristate.IsTrue()` 扩展（仓库既有写法）。
// 16) `ast.NodeIsMissing`/`ast.NodeIsPresent` 等 ast 包级函数 → `using static TypeScript.Ast.Ast;` 后的裸调用；
//     `Kind.X`/`NodeFlags.X`/`ModifierFlags.X`/`SymbolFlags.X`/`FunctionFlags.X` 同理去掉 `ast.` 前缀。
// 17) Go 的 `getEnclosingContainer` 是包级函数 → C# 的 `internal static GetEnclosingContainer(Node) : Node?`（裸调用）。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TsScanner = TypeScript.Scanner.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Evaluator.EvaluatorFunctions;

public sealed partial class Checker
{
    /// <summary>Go: checker.go:3429 <c>checkFunctionDeclaration</c>。</summary>
    internal void CheckFunctionDeclaration(Node node)
    {
        CheckFunctionOrMethodDeclaration(node);
        CheckGrammarForGenerator(node);
        CheckCollisionsForDeclarationName(node, node.Name());
    }

    /// <summary>Go: checker.go:3435 <c>checkFunctionOrMethodDeclaration</c>。</summary>
    internal void CheckFunctionOrMethodDeclaration(Node node)
    {
        CheckDecorators(node);
        CheckSignatureDeclaration(node);
        var functionFlags = GetFunctionFlags(node);
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (node.Name() is not null && IsComputedPropertyName(node.Name()!))
        {
            // This check will account for methods in class/interface declarations,
            // as well as accessors in classes/object literals
            CheckComputedPropertyName(node.Name()!);
        }
        if (HasBindableName(node))
        {
            // first we want to check the local symbol that contain this declaration
            // - if node.localSymbol !== undefined - this is current declaration is exported and localSymbol points to the local symbol
            // - if node.localSymbol === undefined - this node is non-exported so we can just pick the result of getSymbolOfNode
            var symbol = GetSymbolOfDeclaration(node);
            var localSymbol = OrElse(node.LocalSymbol(), symbol);
            // Since the javascript won't do semantic analysis like typescript, ignore javascript function
            // declarations so that redeclaring a function in a JS file is not reported as a duplicate.
            if ((node.Flags & NodeFlags.JavaScriptFile) == 0)
            {
                CheckFunctionOrConstructorSymbol(localSymbol!);
            }
            if (symbol!.Parent is not null)
            {
                // run check on export symbol to check that modifiers agree across all exported declarations
                CheckFunctionOrConstructorSymbol(symbol!);
            }
        }
        var body = node.Body();
        CheckSourceElement(body);
        CheckAllCodePathsInNonVoidFunctionReturnOrThrow(node, GetReturnTypeFromAnnotation(node));
        if (node.FunctionLikeData()?.FullSignature is not null)
        {
            CheckSourceElement(node.FunctionLikeData()!.FullSignature!);
            if (GetContextualCallSignature(GetTypeFromTypeNodeWorker(node.FunctionLikeData()!.FullSignature!), node) is null)
            {
                Error(node.FunctionLikeData()!.FullSignature, Diagnostics.A_JSDoc_type_tag_on_a_function_must_have_a_signature_with_the_correct_number_of_arguments);
            }
        }
        if (node.Type() is null)
        {
            // Report an implicit any error if there is no body, no explicit return type, and node is not a private method
            // in an ambient context
            if (NodeIsMissing(body) && !IsPrivateWithinAmbient(node))
            {
                ReportImplicitAny(node, _anyType, WideningKind.Normal);
            }
            if ((functionFlags & FunctionFlags.Generator) != 0 && NodeIsPresent(body))
            {
                // A generator with a body and no type annotation can still cause errors. It can error if the
                // yielded values have no common supertype, or it can give an implicit any error if it has no
                // yielded values. The only way to trigger these errors is to try checking its return type.
                GetReturnTypeOfSignatureWorker(GetSignatureFromDeclarationWorker(node));
            }
        }
    }

    /// <summary>Go: checker.go:3487 <c>checkFunctionOrConstructorSymbol</c>。</summary>
    internal void CheckFunctionOrConstructorSymbol(Symbol symbol)
    {
        // Only check the symbol once
        var links = _valueSymbolLinks.Get(symbol);
        if (!links._functionOrConstructorChecked)
        {
            links._functionOrConstructorChecked = true;
            CheckFunctionOrConstructorSymbolWorker(symbol);
        }
    }

    /// <summary>Go: checker.go:3495 <c>checkFunctionOrConstructorSymbolWorker</c>。</summary>
    internal void CheckFunctionOrConstructorSymbolWorker(Symbol symbol)
    {
        var flagsToCheck = ModifierFlags.Export | ModifierFlags.Ambient | ModifierFlags.Private | ModifierFlags.Protected | ModifierFlags.Abstract;
        var someNodeFlags = ModifierFlags.None;
        var allNodeFlags = flagsToCheck;
        var someHaveQuestionToken = false;
        var allHaveQuestionToken = true;
        var hasOverloads = false;
        Node? bodyDeclaration = null;
        Node? lastSeenNonAmbientDeclaration = null;
        Node? previousDeclaration = null;
        var declarations = symbol.Declarations ?? [];
        var isConstructor = (symbol.Flags & SymbolFlags.Constructor) != 0;
        var duplicateFunctionDeclaration = false;
        var multipleConstructorImplementation = false;
        var hasNonAmbientClass = false;
        var functionDeclarations = new List<Node>();

        Node GetCanonicalOverload(Node[] overloads, Node? implementation)
        {
            // Consider the canonical set of flags to be the flags of the bodyDeclaration or the first declaration
            // Error on all deviations from this canonical set of flags
            // The caveat is that if some overloads are defined in lib.d.ts, we don't want to
            // report the errors on those. To achieve this, we will say that the implementation is
            // the canonical signature only if it is in the same container as the first overload
            var implementationSharesContainerWithFirstOverload = implementation is not null && implementation.Parent == overloads[0].Parent;
            if (implementationSharesContainerWithFirstOverload)
            {
                return implementation!;
            }
            return overloads[0];
        }

        void CheckFlagAgreementBetweenOverloads(Node[] overloads, Node? implementation, ModifierFlags flagsToCheck, ModifierFlags someOverloadFlags, ModifierFlags allOverloadFlags)
        {
            // Error if some overloads have a flag that is not shared by all overloads. To find the
            // deviations, we XOR someOverloadFlags with allOverloadFlags
            var someButNotAllOverloadFlags = someOverloadFlags ^ allOverloadFlags;
            if (someButNotAllOverloadFlags != 0)
            {
                var canonicalFlags = GetEffectiveDeclarationFlagsWorker(GetCanonicalOverload(overloads, implementation), flagsToCheck);
                var groups = new Dictionary<SourceFile, List<Node>>();
                foreach (var overload in overloads)
                {
                    var sourceFile = GetSourceFileOfNode(overload)!;
                    if (!groups.TryGetValue(sourceFile, out var overloadsInFile))
                    {
                        overloadsInFile = [];
                        groups[sourceFile] = overloadsInFile;
                    }
                    overloadsInFile.Add(overload);
                }
                foreach (var overloadsInFile in groups.Values)
                {
                    var canonicalFlagsForFile = GetEffectiveDeclarationFlagsWorker(GetCanonicalOverload(overloadsInFile.ToArray(), implementation), flagsToCheck);
                    foreach (var overload in overloadsInFile)
                    {
                        var deviation = GetEffectiveDeclarationFlagsWorker(overload, flagsToCheck) ^ canonicalFlags;
                        var deviationInFile = GetEffectiveDeclarationFlagsWorker(overload, flagsToCheck) ^ canonicalFlagsForFile;
                        if ((deviationInFile & ModifierFlags.Export) != 0)
                        {
                            // Overloads in different files need not all have export modifiers. This is ok:
                            //   // lib.d.ts
                            //   declare function foo(s: number): string;
                            //   declare function foo(s: string): number;
                            //   export { foo };
                            //
                            //   // app.ts
                            //   declare module "lib" {
                            //     export function foo(s: boolean): boolean;
                            //   }
                            Error(GetNameOfDeclaration(overload), Diagnostics.Overload_signatures_must_all_be_exported_or_non_exported);
                        }
                        else if ((deviationInFile & ModifierFlags.Ambient) != 0)
                        {
                            // Though rare, a module augmentation (necessarily ambient) is allowed to add overloads
                            // to a non-ambient function in an implementation file.
                            Error(GetNameOfDeclaration(overload), Diagnostics.Overload_signatures_must_all_be_ambient_or_non_ambient);
                        }
                        else if ((deviation & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
                        {
                            Error(OrElse(GetNameOfDeclaration(overload), overload), Diagnostics.Overload_signatures_must_all_be_public_private_or_protected);
                        }
                        else if ((deviation & ModifierFlags.Abstract) != 0)
                        {
                            Error(GetNameOfDeclaration(overload), Diagnostics.Overload_signatures_must_all_be_abstract_or_non_abstract);
                        }
                    }
                }
            }
        }

        void CheckQuestionTokenAgreementBetweenOverloads(Node[] overloads, Node? implementation, bool someHaveQuestionToken, bool allHaveQuestionToken)
        {
            if (someHaveQuestionToken != allHaveQuestionToken)
            {
                var canonicalHasQuestionToken = IsOptionalDeclaration(GetCanonicalOverload(overloads, implementation));
                foreach (var o in overloads)
                {
                    if (IsOptionalDeclaration(o) != canonicalHasQuestionToken)
                    {
                        Error(GetNameOfDeclaration(o), Diagnostics.Overload_signatures_must_all_be_optional_or_required);
                    }
                }
            }
        }

        void ReportImplementationExpectedError(Node node)
        {
            var name = node.Name();
            if (name is not null && NodeIsMissing(name))
            {
                return;
            }
            var seen = false;
            Node? subsequentNode = null;
            node.Parent!.ForEachChild(child =>
            {
                if (seen)
                {
                    subsequentNode = child;
                    return true;
                }
                seen = child == node;
                return false;
            });
            // We may be here because of some extra nodes between overloads that could not be parsed into a valid node.
            // In this case the subsequent node is not really consecutive (.pos !== node.end), and we must ignore it here.
            if (subsequentNode is not null && subsequentNode.Pos() == node.End())
            {
                if (subsequentNode.Kind == node.Kind)
                {
                    var subsequentName = subsequentNode.Name();
                    var subsequentErrorNode = OrElse(subsequentName, subsequentNode);
                    if (name is not null && subsequentName is not null &&
                        (IsPrivateIdentifier(name) && IsPrivateIdentifier(subsequentName) && name.Text() == subsequentName.Text() ||
                            IsComputedPropertyName(name) && IsComputedPropertyName(subsequentName) && IsTypeIdenticalTo(CheckComputedPropertyName(name), CheckComputedPropertyName(subsequentName)) ||
                            IsPropertyNameLiteral(name) && IsPropertyNameLiteral(subsequentName) && name.Text() == subsequentName.Text()))
                    {
                        var reportError = (IsMethodDeclaration(node) || IsMethodSignatureDeclaration(node)) && IsStatic(node) != IsStatic(subsequentNode);
                        // we can get here in two cases
                        // 1. mixed static and instance class members
                        // 2. something with the same name was defined before the set of overloads that prevents them from merging
                        // here we'll report error only for the first case since for second we should already report error in binder
                        if (reportError)
                        {
                            var diagnostic = IfElse(IsStatic(node), Diagnostics.Function_overload_must_be_static, Diagnostics.Function_overload_must_not_be_static);
                            Error(subsequentErrorNode, diagnostic);
                        }
                        return;
                    }
                    if (NodeIsPresent(subsequentNode.Body()))
                    {
                        Error(subsequentErrorNode, Diagnostics.Function_implementation_name_must_be_0, TsScanner.DeclarationNameToString(name));
                        return;
                    }
                }
            }
            var errorNode = OrElse(name, node);
            if (isConstructor)
            {
                Error(errorNode, Diagnostics.Constructor_implementation_is_missing);
            }
            else
            {
                // Report different errors regarding non-consecutive blocks of declarations depending on whether
                // the node in question is abstract.
                if (HasSyntacticModifier(node, ModifierFlags.Abstract))
                {
                    Error(errorNode, Diagnostics.All_declarations_of_an_abstract_method_must_be_consecutive);
                }
                else
                {
                    Error(errorNode, Diagnostics.Function_implementation_is_missing_or_not_immediately_following_the_declaration);
                }
            }
        }

        foreach (var node in declarations)
        {
            var inAmbientContext = (node.Flags & NodeFlags.Ambient) != 0;
            var inAmbientContextOrInterface = inAmbientContext || node.Parent is not null && (IsInterfaceDeclaration(node.Parent) || IsTypeLiteralNode(node.Parent));
            if (inAmbientContextOrInterface)
            {
                // check if declarations are consecutive only if they are non-ambient
                // 1. ambient declarations can be interleaved
                // i.e. this is legal
                //     declare function foo();
                //     declare function bar();
                //     declare function foo();
                // 2. mixing ambient and non-ambient declarations is a separate error that will be reported - do not want to report an extra one
                previousDeclaration = null;
            }
            if (IsClassLike(node) && !inAmbientContext)
            {
                hasNonAmbientClass = true;
            }
            if (IsFunctionDeclaration(node) || IsMethodDeclaration(node) || IsMethodSignatureDeclaration(node) || IsConstructorDeclaration(node))
            {
                functionDeclarations.Add(node);
                var currentNodeFlags = GetEffectiveDeclarationFlagsWorker(node, flagsToCheck);
                someNodeFlags |= currentNodeFlags;
                allNodeFlags &= currentNodeFlags;
                someHaveQuestionToken = someHaveQuestionToken || IsOptionalDeclaration(node);
                allHaveQuestionToken = allHaveQuestionToken && IsOptionalDeclaration(node);
                var bodyIsPresent = NodeIsPresent(node.Body());
                if (bodyIsPresent && bodyDeclaration is not null)
                {
                    if (isConstructor)
                    {
                        multipleConstructorImplementation = true;
                    }
                    else
                    {
                        duplicateFunctionDeclaration = true;
                    }
                }
                else if (previousDeclaration is not null && previousDeclaration.Parent == node.Parent && previousDeclaration.End() != node.Pos() && (previousDeclaration.Flags & NodeFlags.Reparsed) == 0)
                {
                    ReportImplementationExpectedError(previousDeclaration);
                }
                if (bodyIsPresent)
                {
                    bodyDeclaration ??= node;
                }
                else
                {
                    hasOverloads = true;
                }
                previousDeclaration = node;
                if (!inAmbientContextOrInterface)
                {
                    lastSeenNonAmbientDeclaration = node;
                }
            }
        }
        if (multipleConstructorImplementation)
        {
            foreach (var declaration in functionDeclarations)
            {
                Error(declaration, Diagnostics.Multiple_constructor_implementations_are_not_allowed);
            }
        }
        if (duplicateFunctionDeclaration)
        {
            foreach (var declaration in functionDeclarations)
            {
                Error(OrElse(GetNameOfDeclaration(declaration), declaration), Diagnostics.Duplicate_function_implementation);
            }
        }
        if (hasNonAmbientClass && !isConstructor && (symbol.Flags & SymbolFlags.Function) != 0 && declarations.Length != 0)
        {
            var relatedDiagnostics = new List<Diagnostic>();
            foreach (var declaration in declarations)
            {
                if (IsClassDeclaration(declaration))
                {
                    relatedDiagnostics.Add(CreateDiagnosticForNode(declaration, Diagnostics.Consider_adding_a_declare_modifier_to_this_class));
                }
            }
            foreach (var declaration in declarations)
            {
                Message? diagnostic = null;
                switch (declaration.Kind)
                {
                    case Kind.ClassDeclaration:
                        diagnostic = Diagnostics.Class_declaration_cannot_implement_overload_list_for_0;
                        break;
                    case Kind.FunctionDeclaration:
                        diagnostic = Diagnostics.Function_with_bodies_can_only_merge_with_classes_that_are_ambient;
                        break;
                }
                if (diagnostic is not null)
                {
                    Error(OrElse(GetNameOfDeclaration(declaration), declaration), diagnostic, symbol.Name).SetRelatedInfo(relatedDiagnostics.ToArray());
                }
            }
        }
        // Abstract methods can't have an implementation -- in particular, they don't need one.
        if (lastSeenNonAmbientDeclaration is not null && lastSeenNonAmbientDeclaration.Body() is null && !HasSyntacticModifier(lastSeenNonAmbientDeclaration, ModifierFlags.Abstract) && !IsOptionalDeclaration(lastSeenNonAmbientDeclaration))
        {
            ReportImplementationExpectedError(lastSeenNonAmbientDeclaration);
        }
        if (hasOverloads)
        {
            CheckFlagAgreementBetweenOverloads(declarations, bodyDeclaration, flagsToCheck, someNodeFlags, allNodeFlags);
            CheckQuestionTokenAgreementBetweenOverloads(declarations, bodyDeclaration, someHaveQuestionToken, allHaveQuestionToken);
            if (bodyDeclaration is not null)
            {
                var signatures = GetSignaturesOfSymbol(symbol);
                var bodySignature = GetSignatureFromDeclarationWorker(bodyDeclaration);
                foreach (var signature in signatures)
                {
                    if (!IsImplementationCompatibleWithOverload(bodySignature, signature))
                    {
                        var errorNode = signature.Declaration();
                        Error(errorNode, Diagnostics.This_overload_signature_is_not_compatible_with_its_implementation_signature).AddRelatedInfo(CreateDiagnosticForNode(bodyDeclaration, Diagnostics.The_implementation_signature_is_declared_here));
                        break;
                    }
                }
            }
        }
    }

    /// <summary>Go: checker.go:3727 <c>getEffectiveDeclarationFlags</c>（→ Worker，见文件头偏离 1）。</summary>
    internal ModifierFlags GetEffectiveDeclarationFlagsWorker(Node n, ModifierFlags flagsToCheck)
    {
        var flags = GetCombinedModifierFlagsCached(n);
        // children of classes (even ambient classes) should not be marked as ambient or export
        // because those flags have no useful semantics there.
        if (!IsInterfaceDeclaration(n.Parent!) && !IsClassDeclaration(n.Parent!) && !IsClassExpression(n.Parent!) && (n.Flags & NodeFlags.Ambient) != 0)
        {
            var container = GetEnclosingContainer(n);
            if (container is not null && (container.Flags & NodeFlags.ExportContext) != 0 && (flags & ModifierFlags.Ambient) == 0 && !(IsModuleBlock(n.Parent!) && IsGlobalScopeAugmentation(n.Parent!.Parent!)))
            {
                // It is nested in an ambient export context, which means it is automatically exported
                flags |= ModifierFlags.Export;
            }
            flags |= ModifierFlags.Ambient;
        }
        return flags & flagsToCheck;
    }

    /// <summary>Go: checker.go:3742 <c>isImplementationCompatibleWithOverload</c>。</summary>
    internal bool IsImplementationCompatibleWithOverload(Signature implementation, Signature overload)
    {
        var erasedSource = GetErasedSignature(implementation);
        var erasedTarget = GetErasedSignature(overload);
        // First see if the return types are compatible in either direction.
        var sourceReturnType = GetReturnTypeOfSignatureWorker(erasedSource);
        var targetReturnType = GetReturnTypeOfSignatureWorker(erasedTarget);
        if (targetReturnType == _voidType || IsTypeRelatedTo(targetReturnType, sourceReturnType, _assignableRelation) || IsTypeRelatedTo(sourceReturnType, targetReturnType, _assignableRelation))
        {
            return IsSignatureAssignableTo(erasedSource, erasedTarget, true /*ignoreReturnTypes*/);
        }
        return false;
    }

    /// <summary>Go: checker.go:3754 <c>checkAllCodePathsInNonVoidFunctionReturnOrThrow</c>。</summary>
    internal void CheckAllCodePathsInNonVoidFunctionReturnOrThrow(Node fn, Type? returnType)
    {
        var functionFlags = GetFunctionFlags(fn);
        Type? t = null;
        if (returnType is not null)
        {
            t = UnwrapReturnType(returnType, functionFlags);
        }
        // Functions with an explicitly specified return type that includes `void` or is exactly `any` or `undefined` don't
        // need any return statements.
        if (t is not null && (MaybeTypeOfKind(t, TypeFlags.Void) || (t._flags & (TypeFlags.Any | TypeFlags.Undefined)) != 0))
        {
            return;
        }
        // If all we have is a function signature, or an arrow function with an expression body, then there is nothing to check.
        // also if HasImplicitReturn flag is not set this means that all codepaths in function body end with return or throw
        if (IsMethodSignatureDeclaration(fn) || NodeIsMissing(fn.Body()) || !IsBlock(fn.Body()!) || !FunctionHasImplicitReturn(fn))
        {
            return;
        }
        var hasExplicitReturn = (fn.Flags & NodeFlags.HasExplicitReturn) != 0;
        var errorNode = fn.Type();
        if (errorNode is null)
        {
            var data = fn.FunctionLikeData();
            if (data is not null && data.FullSignature is not null)
            {
                errorNode = data.FullSignature;
            }
        }
        if (errorNode is null)
        {
            errorNode = fn;
        }
        if (t is not null && (t._flags & TypeFlags.Never) != 0)
        {
            Error(errorNode, Diagnostics.A_function_returning_never_cannot_have_a_reachable_end_point);
        }
        else if (t is not null && !hasExplicitReturn)
        {
            // minimal check: function has syntactic return type annotation and no explicit return statements in the body
            // this function does not conform to the specification.
            Error(errorNode, Diagnostics.A_function_whose_declared_type_is_neither_undefined_void_nor_any_must_return_a_value);
        }
        else if (t is not null && _strictNullChecks && !IsTypeAssignableToWorker(_undefinedType, t))
        {
            Error(errorNode, Diagnostics.Function_lacks_ending_return_statement_and_return_type_does_not_include_undefined);
        }
        else if (_compilerOptions.NoImplicitReturns.IsTrue())
        {
            if (t is null)
            {
                // If return type annotation is omitted check if function has any explicit return statements.
                // If it does not have any - its inferred return type is void - don't do any checks.
                // Otherwise get inferred return type from function body and report error only if it is not void / anytype
                if (!hasExplicitReturn)
                {
                    return;
                }
                var inferredReturnType = GetReturnTypeOfSignatureWorker(GetSignatureFromDeclarationWorker(fn));
                if (IsUnwrappedReturnTypeUndefinedVoidOrAny(fn, inferredReturnType))
                {
                    return;
                }
            }
            Error(errorNode, Diagnostics.Not_all_code_paths_return_a_value);
        }
    }

    /// <summary>Go: checker.go:3806 <c>isUnwrappedReturnTypeUndefinedVoidOrAny</c>。</summary>
    internal bool IsUnwrappedReturnTypeUndefinedVoidOrAny(Node fn, Type returnType)
    {
        var t = UnwrapReturnType(returnType, GetFunctionFlags(fn));
        return t is not null && (MaybeTypeOfKind(t, TypeFlags.Void) || (t._flags & (TypeFlags.Any | TypeFlags.Undefined)) != 0);
    }

    /// <summary>Go: checker.go:3811 <c>checkBlock</c>。</summary>
    internal void CheckBlock(Node node)
    {
        // Grammar checking for SyntaxKind.Block
        if (node.Kind == Kind.Block)
        {
            CheckGrammarStatementInAmbientContext(node);
        }
        if (IsFunctionOrModuleBlock(node))
        {
            var saveFlowAnalysisDisabled = _flowAnalysisDisabled;
            CheckSourceElements(node.Statements() ?? []);
            _flowAnalysisDisabled = saveFlowAnalysisDisabled;
        }
        else
        {
            CheckSourceElements(node.Statements() ?? []);
        }
        if (node.Locals() is { Count: > 0 })
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    /// <summary>Go: checker.go:3828 <c>checkIfStatement</c>。</summary>
    internal void CheckIfStatement(Node node)
    {
        CheckGrammarStatementInAmbientContext(node);
        var t = CheckTruthinessExpression(node.Expression()!, CheckMode.Normal);
        var data = node.AsIfStatement()!;
        CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(node.Expression()!, t, data.ThenStatement);
        CheckSourceElement(data.ThenStatement);
        if (IsEmptyStatement(data.ThenStatement!))
        {
            Error(data.ThenStatement, Diagnostics.The_body_of_an_if_statement_cannot_be_the_empty_statement);
        }
        CheckSourceElement(data.ElseStatement);
    }

    /// <summary>Go: checker.go:3840 <c>checkTestingKnownTruthyCallableOrAwaitableOrEnumMemberType</c>。</summary>
    internal void CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(Node condExpr, Type condType, Node? body)
    {
        if (!_strictNullChecks)
        {
            return;
        }
        CheckTestingKnownTruthyTypes(condExpr, condType, body);
    }

    /// <summary>Go: checker.go:3847 <c>checkTestingKnownTruthyTypes</c>。</summary>
    internal void CheckTestingKnownTruthyTypes(Node condExpr, Type condType, Node? body)
    {
        var expr = SkipParentheses(condExpr);
        CheckTestingKnownTruthyType(expr!, condType, body);
        while (IsBinaryExpression(expr!) && (expr!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.BarBarToken || expr.AsBinaryExpression()!.OperatorToken!.Kind == Kind.QuestionQuestionToken))
        {
            expr = SkipParentheses(expr.AsBinaryExpression()!.Left);
            CheckTestingKnownTruthyType(expr!, condType, body);
        }
    }

    /// <summary>Go: checker.go:3856 <c>checkTestingKnownTruthyType</c>。</summary>
    internal void CheckTestingKnownTruthyType(Node condExpr, Type condType, Node? body)
    {
        Node? location = condExpr;
        if (IsLogicalOrCoalescingBinaryExpression(condExpr))
        {
            location = SkipParentheses(condExpr.AsBinaryExpression()!.Right);
        }
        if (IsModuleExportsAccessExpression(location))
        {
            return;
        }
        if (IsLogicalOrCoalescingBinaryExpression(location))
        {
            CheckTestingKnownTruthyTypes(location!, condType, body);
            return;
        }
        var t = condType;
        if (location != condExpr)
        {
            t = CheckExpression(location!);
        }
        if ((t._flags & TypeFlags.EnumLiteral) != 0 && IsPropertyAccessExpression(location!) && (OrElse(GetResolvedSymbolOrNil(location!.Expression()!), _unknownSymbol)!.Flags & SymbolFlags.Enum) != 0)
        {
            // EnumLiteral type at condition with known value is always truthy or always falsy, likely an error
            Error(location, Diagnostics.This_condition_will_always_return_0, IfElse(IsTruthy(t.AsLiteralType()!._value), "true", "false"));
            return;
        }
        var isPropertyExpressionCast = IsPropertyAccessExpression(location!) && IsTypeAssertion(location!.Expression()!);
        if (!HasTypeFacts(t, TypeFacts.Truthy) || isPropertyExpressionCast)
        {
            return;
        }
        // While it technically should be invalid for any known-truthy value
        // to be tested, we de-scope to functions and Promises unreferenced in
        // the block as a heuristic to identify the most common bugs. There
        // are too many false positives for values sourced from type
        // definitions without strictNullChecks otherwise.
        var callSignatures = GetSignaturesOfTypeWorker(t, SignatureKind.Call);
        var isPromise = GetAwaitedTypeOfPromise(t) is not null;
        if ((callSignatures?.Length ?? 0) == 0 && !isPromise)
        {
            return;
        }
        Node? testedNode = null;
        if (IsIdentifier(location!))
        {
            testedNode = location;
        }
        else if (IsPropertyAccessExpression(location!))
        {
            testedNode = location!.Name();
        }
        Symbol? testedSymbol = null;
        if (testedNode is not null)
        {
            testedSymbol = GetSymbolAtLocationWorker(testedNode, false);
        }
        if (testedSymbol is null && !isPromise)
        {
            return;
        }
        var isUsed = testedSymbol is not null && IsBinaryExpression(condExpr.Parent!) && IsSymbolUsedInBinaryExpressionChain(condExpr.Parent!, testedSymbol) ||
            testedSymbol is not null && body is not null && IsSymbolUsedInConditionBody(condExpr, body, testedNode, testedSymbol);
        if (!isUsed)
        {
            if (isPromise)
            {
                ErrorAndMaybeSuggestAwait(location, true, Diagnostics.This_condition_will_always_return_true_since_this_0_is_always_defined, GetTypeNameForErrorDisplay(t));
            }
            else
            {
                Error(location, Diagnostics.This_condition_will_always_return_true_since_this_function_is_always_defined_Did_you_mean_to_call_it_instead);
            }
        }
    }

    /// <summary>Go: checker.go:3916 <c>isSymbolUsedInBinaryExpressionChain</c>。</summary>
    internal bool IsSymbolUsedInBinaryExpressionChain(Node node, Symbol testedSymbol)
    {
        bool Visit(Node child)
        {
            if (IsIdentifier(child))
            {
                var symbol = GetSymbolAtLocationWorker(child, false);
                if (symbol is not null && symbol == testedSymbol)
                {
                    return true;
                }
            }
            return child.ForEachChild(Visit);
        }

        Node? current = node;
        while (current is not null && IsBinaryExpression(current) && current.AsBinaryExpression()!.OperatorToken!.Kind == Kind.AmpersandAmpersandToken)
        {
            var isUsed = current.AsBinaryExpression()!.Right!.ForEachChild(Visit);
            if (isUsed)
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    /// <summary>Go: checker.go:3937 <c>isSymbolUsedInConditionBody</c>。</summary>
    internal bool IsSymbolUsedInConditionBody(Node expr, Node body, Node? testedNode, Symbol testedSymbol)
    {
        bool Visit(Node childNode)
        {
            if (IsIdentifier(childNode))
            {
                var childSymbol = GetSymbolAtLocationWorker(childNode, false);
                if (childSymbol is not null && childSymbol == testedSymbol)
                {
                    // If the test was a simple identifier, the above check is sufficient
                    if (IsIdentifier(expr) || IsIdentifier(testedNode!) && IsBinaryExpression(testedNode!.Parent!))
                    {
                        return true;
                    }
                    // Otherwise we need to ensure the symbol is called on the same target
                    var testedExpression = testedNode!.Parent;
                    var childExpression = childNode.Parent;
                    while (testedExpression is not null && childExpression is not null)
                    {
                        if (IsIdentifier(testedExpression) && IsIdentifier(childExpression) || testedExpression.Kind == Kind.ThisKeyword && childExpression.Kind == Kind.ThisKeyword)
                        {
                            return ReferenceEquals(GetSymbolAtLocationWorker(testedExpression, false), GetSymbolAtLocationWorker(childExpression, false));
                        }
                        else if (IsPropertyAccessExpression(testedExpression) && IsPropertyAccessExpression(childExpression))
                        {
                            if (!ReferenceEquals(GetSymbolAtLocationWorker(testedExpression.Name()!, false), GetSymbolAtLocationWorker(childExpression.Name()!, false)))
                            {
                                return false;
                            }
                            childExpression = childExpression.Expression();
                            testedExpression = testedExpression.Expression();
                        }
                        else if (IsCallExpression(testedExpression) && IsCallExpression(childExpression))
                        {
                            childExpression = childExpression.Expression();
                            testedExpression = testedExpression.Expression();
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }
            return childNode.ForEachChild(Visit);
        }

        return body.ForEachChild(Visit);
    }

    /// <summary>Go: checker.go:3973 <c>checkDoStatement</c>。</summary>
    internal void CheckDoStatement(Node node)
    {
        CheckGrammarStatementInAmbientContext(node);
        CheckSourceElement(node.Statement());
        CheckTruthinessExpression(node.Expression()!, CheckMode.Normal);
    }

    /// <summary>Go: checker.go:3979 <c>checkWhileStatement</c>。</summary>
    internal void CheckWhileStatement(Node node)
    {
        CheckGrammarStatementInAmbientContext(node);
        CheckTruthinessExpression(node.Expression()!, CheckMode.Normal);
        CheckSourceElement(node.Statement());
    }

    /// <summary>Go: checker.go:3985 <c>checkForStatement</c>。</summary>
    internal void CheckForStatement(Node node)
    {
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            var init = node.Initializer();
            if (init is not null && init.Kind == Kind.VariableDeclarationList)
            {
                CheckGrammarVariableDeclarationList(init.AsVariableDeclarationList()!);
            }
        }
        var data = node.AsForStatement()!;
        if (data.Initializer is not null)
        {
            if (IsVariableDeclarationList(data.Initializer))
            {
                CheckVariableDeclarationList(data.Initializer!);
            }
            else
            {
                CheckExpression(data.Initializer!);
            }
        }
        if (data.Condition is not null)
        {
            CheckTruthinessExpression(data.Condition!, CheckMode.Normal);
        }
        if (data.Incrementor is not null)
        {
            CheckExpression(data.Incrementor!);
        }
        CheckSourceElement(data.Statement);
        if (node.Locals() is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    /// <summary>Go: checker.go:4011 <c>checkForInStatement</c>。</summary>
    internal void CheckForInStatement(Node node)
    {
        var data = node.AsForInOrOfStatement()!;
        CheckGrammarForInOrForOfStatement(data);
        var rightType = GetNonNullableTypeIfNeeded(CheckExpression(data.Expression!));
        // TypeScript 1.0 spec (April 2014): 5.4
        // In a 'for-in' statement of the form
        // for (let VarDecl in Expr) Statement
        //   VarDecl must be a variable declaration without a type annotation that declares a variable of type Any,
        //   and Expr must be an expression of type Any, an object type, or a type parameter type.
        if (IsVariableDeclarationList(data.Initializer!))
        {
            // Go: `...Declarations.Nodes`（NodeList.Nodes 是 nil 切片）
            var declarations = data.Initializer!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [];
            if (declarations.Length != 0 && IsBindingPattern(declarations[0].Name()))
            {
                Error(declarations[0].Name(), Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_a_destructuring_pattern);
            }
            CheckVariableDeclarationList(data.Initializer!);
        }
        else
        {
            // In a 'for-in' statement of the form
            // for (Var in Expr) Statement
            //   Var must be an expression classified as a reference of type Any or the String primitive type,
            //   and Expr must be an expression of type Any, an object type, or a type parameter type.
            var varExpr = data.Initializer!;
            var leftType = CheckExpression(varExpr);
            if (IsArrayLiteralExpression(varExpr) || IsObjectLiteralExpression(varExpr))
            {
                Error(varExpr, Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_a_destructuring_pattern);
            }
            else if (!IsTypeAssignableToWorker(GetIndexTypeOrString(rightType), leftType))
            {
                Error(varExpr, Diagnostics.The_left_hand_side_of_a_for_in_statement_must_be_of_type_string_or_any);
            }
            else
            {
                // run check only former check succeeded to avoid cascading errors
                CheckReferenceExpression(varExpr, Diagnostics.The_left_hand_side_of_a_for_in_statement_must_be_a_variable_or_a_property_access, Diagnostics.The_left_hand_side_of_a_for_in_statement_may_not_be_an_optional_property_access);
            }
        }
        // unknownType is returned i.e. if node.expression is identifier whose name cannot be resolved
        // in this case error about missing name is already reported - do not report extra one
        if (rightType == _neverType || !IsTypeAssignableToKind(rightType, TypeFlags.NonPrimitive | TypeFlags.InstantiableNonPrimitive))
        {
            Error(data.Expression, Diagnostics.The_right_hand_side_of_a_for_in_statement_must_be_of_type_any_an_object_type_or_a_type_parameter_but_here_has_type_0, TypeToString(rightType));
        }
        CheckSourceElement(data.Statement);
        if (node.Locals() is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    /// <summary>Go: checker.go:4053 <c>getIndexTypeOrString</c>。</summary>
    internal Type GetIndexTypeOrString(Type t)
    {
        var indexType = GetExtractStringType(GetIndexType(t));
        return IfElse((indexType._flags & TypeFlags.Never) != 0, _stringType, indexType);
    }
}
