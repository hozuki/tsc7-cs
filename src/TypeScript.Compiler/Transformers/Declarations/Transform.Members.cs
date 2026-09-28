// ported from: internal/transformers/declarations/transform.go:464-1160（引用文件/诊断上下文/声明子树访问/成员与签名变换）
//
// 与 Go 的差异（C# 语言特性，逐条对应 docs/architecture.md §2/§3）：
// 1) `ref` 是 C# 关键字，Go 的局部变量名 `ref`（getReferencedFiles/getLibReferences/getTypeReferences 里遍历的
//    `*ast.FileReference`）改名为 `reference`；`ref.Preserve` → `reference.Preserve`，语义不变。
// 2) Go 的 `setupDiagnosticContext` 返回 `(bool, func())` → C# `(bool CanProduceDiagnostic, DiagnosticContextCleanup Cleanup)`；
//    闭包体逐行照抄（Go 的 `defer cleanupDiagnosticContext()` → 调用点 try/finally）。返回的 `func()` **不落成
//    `Action` 闭包**：Go 的闭包只分配一个 funcval，而 C# 的 `Action` 每次分配 `<>c__DisplayClass` + `Action`
//    两个对象（实测 = 180.6 MB／1.67 GB，全量 TS6 src/compiler 编译）→ 改用值类型 `DiagnosticContextCleanup`
//    （栈上、不逃逸、零分配），调用点从 `cleanupDiagnosticContext()` 改成 `cleanupDiagnosticContext.Restore()`。
// 3) Go 的 `core.Filter(clause.Types.Nodes, func(t *ast.HeritageClauseElement) bool)` → C# 数组版
//    `Filter(clause.Types!.Nodes, t => ...)`（`HeritageClauseElement` 就是 `Node`（别名），C# 的 `T` = `Node`）。
// 4) Go 的切片 append → C# `List<T>` + `ToArray()`（`NewNodeList`/`NewSyntaxList`/`DeclarationEmitHost` 的返回值
//    都要数组）：`results`（getReferencedFiles/getLibReferences/getTypeReferences）、
//    `importSpecifiers`（transformCjsRequireVariableDeclaration）、`results`（recreateBindingPattern）、
//    `newParams`（updateAccessorParamList）。`append(rs, xs...)` → `AddRange`。
//    `len(slice)` → `slice.Length`（C# 数组可为 null，按 Go 的非 nil 前提加 `!`）。
// 5) Go 的 `ast.FileReference{TextRange: ..., FileName: ..., ResolutionMode: ..., Preserve: ...}` 结构体字面量
//    → C# 对象初始化器（类，见 Ast/AstTypes.cs；Go 内嵌的 `core.TextRange` 在 C# 里叫 `Range`）。
// 6) Go 的 `tspath.ComparePathsOptions{CurrentDirectory: x, UseCaseSensitiveFileNames: y}` → C# 位置构造
//    `new ComparePathsOptions(y, x)`（C# 构造签名是 `(bool useCaseSensitiveFileNames = false, string? currentDirectory = null)`，
//    与既有 OutputPaths/OutputPaths.cs 的写法一致）。
// 7) 可空性：Go 的指针可以是 nil，C# 开了 `Nullable` + `TreatWarningsAsErrors`，所以对「Go 里假定非 nil」的
//    表达式一律用 `!`（编译期抑制，**不产生运行时检查**，行为与 Go 完全一致）。
// 8) `input.Symbol()` → C# 属性 `input.Symbol`（无括号，见 docs/ast.md §2）；`input.AsNode()` → C# 就是该节点本身。
// 9) Go 的包级函数 `hasAnyBindingInitializers` → 静态类 `Declarations` 的 `internal static` 成员（§2）。
// 10) `transformers.IsOriginalNodeSingleLine` 写全限定名 `TypeScript.Transformers.Transformers.IsOriginalNodeSingleLine`
//     （本命名空间叫 `TypeScript.Transformers.Declarations`，裸写 `Transformers` 会先命中同名类，全限定最稳）。
// 11) `debug.Assert` → `Debug.Assert`（`TypeScript.Debug`，靠外层命名空间解析；**不** using System.Diagnostics）。
// 12) `visitDeclarationSubtree` 里的 heritage clause 守卫：Go 的
//     `ast.IsHeritageClause(input) && (len(input.AsHeritageClause().Types.Nodes) == 0 || (...))`
//     → C# `var heritageClause = input.AsHeritageClause(); if (heritageClause != null) { var heritageTypes = ...; ... }`。
//     理由：`IsHeritageClause` 的定义就是 `node.Kind == Kind.HeritageClause`，所以 `AsHeritageClause() != null`
//     是同一个谓词；同时把 Go 里两次读的 `.Types.Nodes` 提成局部量（纯读、无副作用，语义不变）。
// 13) `updateAccessorParamList` 的返回值：Go `*ast.ParameterList` → C# `NodeList?`
//     （`Factory().NewNodeList(...)` 返回 `NodeList`；Go 的 nil 分支在 C# 里是 `return null`）。
// 14) D 片的 `EnsureParameter`/`EnsureModifiers` 等签名是非空返回（Go 返回 `*ast.Node` 可为 nil），
//     调用点按 Go 的「可能为 nil」写法保留：`newParams.Add(valueParam!)` 只做编译期抑制，运行时仍可为 nil。
namespace TypeScript.Transformers.Declarations;

// 布局说明（T6 全仓探针实测）：usings 必须放在 file-scoped `namespace …;` **之后**。否则裸写
// `Diagnostics.X` 会先在外层命名空间链里命中命名空间 `TypeScript.Diagnostics`（CS0234），且**编译单元级**
// 的 `using Diagnostics = …` 别名不参与命名空间体内的简单名解析（别名同样报 CS0234）；只有写在 namespace
// 体内的 using 才成为该声明空间的成员、优先于外层命名空间。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using TypeScript.TSPath;
using Diagnostics = TypeScript.Diagnostics.Diagnostics; // 消歧：外层命名空间 TypeScript.Diagnostics 优先于 using 导入的类型
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Transformers.Declarations.Declarations;

public sealed partial class DeclarationTransformer : Transformer
{
    /// <summary>Go: <c>func (tx *DeclarationTransformer) getReferencedFiles(outputFilePath string) (results []*ast.FileReference)</c>（transform.go:464-517）。</summary>
    private FileReference[] GetReferencedFiles(string outputFilePath)
    {
        var results = new List<FileReference>();
        // Handle path rewrites for triple slash ref comments
        foreach (var pair in _rawReferencedFiles)
        {
            var sourceFile = pair._file;
            var reference = pair._ref;

            if (!reference.Preserve)
            {
                continue;
            }

            var file = _host.GetSourceFileFromReference(sourceFile, reference);
            if (file == null)
            {
                continue;
            }

            string declFileName;
            if (file.IsDeclarationFile)
            {
                declFileName = file.FileName();
            }
            else
            {
                var paths = _host.GetOutputPathsFor(file, true);
                // Try to use output path for referenced file, or output js path if that doesn't exist, or the input path if all else fails
                declFileName = paths.DeclarationFilePath();
                if (declFileName.Length == 0)
                {
                    declFileName = paths.JsFilePath();
                }
                if (declFileName.Length == 0)
                {
                    declFileName = file.FileName();
                }
            }
            // Should only be missing if the source file is missing a fileName (at which point we can't name a reference to it anyway)
            // TODO: Shouldn't this be a crash or assert instead of a silent continue?
            if (declFileName.Length == 0)
            {
                continue;
            }

            var fileName = GetRelativePathToDirectoryOrUrl(
                outputFilePath,
                declFileName,
                false, // TODO: Probably unsafe to assume this isn't a URL, but that's what strada does
                new ComparePathsOptions(_host.UseCaseSensitiveFileNames(), _host.GetCurrentDirectory()));

            results.Add(new FileReference
            {
                Range = NewTextRange(-1, -1),
                FileName = fileName,
                ResolutionMode = reference.ResolutionMode,
                Preserve = reference.Preserve,
            });
        }
        return results.ToArray();
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) getLibReferences() (result []*ast.FileReference)</c>（transform.go:519-533）。</summary>
    private FileReference[] GetLibReferences()
    {
        var result = new List<FileReference>();
        // clone retained references
        foreach (var reference in _rawLibReferenceDirectives)
        {
            if (!reference.Preserve)
            {
                continue;
            }
            result.Add(new FileReference
            {
                Range = NewTextRange(-1, -1),
                FileName = reference.FileName,
                ResolutionMode = reference.ResolutionMode,
                Preserve = reference.Preserve,
            });
        }
        return result.ToArray();
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) getTypeReferences() (result []*ast.FileReference)</c>（transform.go:535-549）。</summary>
    private FileReference[] GetTypeReferences()
    {
        var result = new List<FileReference>();
        // clone retained references
        foreach (var reference in _rawTypeReferenceDirectives)
        {
            if (!reference.Preserve)
            {
                continue;
            }
            result.Add(new FileReference
            {
                Range = NewTextRange(-1, -1),
                FileName = reference.FileName,
                ResolutionMode = reference.ResolutionMode,
                Preserve = reference.Preserve,
            });
        }
        return result.ToArray();
    }

    /// <summary>
    /// Go: <c>setupDiagnosticContext</c> 返回的 <c>func()</c>（transform.go:551-571）。
    /// Go 的一个闭包只分配一个 funcval；C# 的 `Action` 闭包每次调用会分配**两个**对象
    /// （捕获三个局部量的 `&lt;&gt;c__DisplayClass` + `Action` 委托本身）。本类型把 Go 闭包捕获的四个值
    /// （tx / oldDiag / oldName / oldWithinObjectLiteralType）装进**值类型**，由
    /// <see cref="SetupDiagnosticContext"/> 按值返回：调用点只把它当 `defer` 用（栈上的局部量，不逃逸）
    /// ⇒ 零堆分配。实测（TS6 src/compiler 全量编译）该闭包＝180.6 MB／1.67 GB 总分配。
    /// </summary>
    private readonly struct DiagnosticContextCleanup
    {
        private readonly DeclarationTransformer _tx;
        private readonly GetSymbolAccessibilityDiagnostic? _oldDiag;
        private readonly Node? _oldName;
        private readonly bool _oldWithinObjectLiteralType;

        internal DiagnosticContextCleanup(DeclarationTransformer tx, GetSymbolAccessibilityDiagnostic? oldDiag, Node? oldName, bool oldWithinObjectLiteralType)
        {
            _tx = tx;
            _oldDiag = oldDiag;
            _oldName = oldName;
            _oldWithinObjectLiteralType = oldWithinObjectLiteralType;
        }

        /// <summary>Go 闭包体：`tx.state.getSymbolAccessibilityDiagnostic = oldDiag; tx.state.errorNameNode = oldName; tx.suppressNewDiagnosticContexts = oldWithinObjectLiteralType`。</summary>
        internal void Restore()
        {
            _tx._state._getSymbolAccessibilityDiagnostic = _oldDiag;
            _tx._state._errorNameNode = _oldName;
            _tx._suppressNewDiagnosticContexts = _oldWithinObjectLiteralType;
        }
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) setupDiagnosticContext(input *ast.Node) (bool, func())</c>（transform.go:551-571）。</summary>
    private (bool CanProduceDiagnostic, DiagnosticContextCleanup Cleanup) SetupDiagnosticContext(Node input)
    {
        var canProduceDiagnostic = CanProduceDiagnostics(input);
        var oldWithinObjectLiteralType = _suppressNewDiagnosticContexts;
        var shouldEnterSuppressNewDiagnosticsContextContext = (input.Kind == Kind.TypeLiteral || input.Kind == Kind.MappedType) && !(input.Parent!.Kind == Kind.TypeAliasDeclaration || input.Parent!.Kind == Kind.JSTypeAliasDeclaration);

        var oldDiag = _state._getSymbolAccessibilityDiagnostic;
        if (canProduceDiagnostic && !_suppressNewDiagnosticContexts)
        {
            _state._getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNode(input);
        }
        var oldName = _state._errorNameNode;

        if (shouldEnterSuppressNewDiagnosticsContextContext)
        {
            _suppressNewDiagnosticContexts = true;
        }

        return (canProduceDiagnostic, new DiagnosticContextCleanup(this, oldDiag, oldName, oldWithinObjectLiteralType));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) visitDeclarationSubtree(input *ast.Node) *ast.Node</c>（transform.go:573-706）。</summary>
    private Node? VisitDeclarationSubtree(Node input)
    {
        if (ShouldStripInternal(input))
        {
            return null;
        }
        if (IsDeclaration(input))
        {
            if (IsDeclarationAndNotVisible(EmitContext(), _resolver, input))
            {
                return null;
            }
            if (HasDynamicName(input))
            {
                if (_state._isolatedDeclarations)
                {
                    // Classes and object literals usually elide properties with computed names that are not of a literal type
                    // In isolated declarations TSC needs to error on these as we don't know the type in a DTE.
                    if (!_resolver.IsDefinitelyReferenceToGlobalSymbolObject(input.Name()!.Expression()!))
                    {
                        if (IsClassDeclaration(input.Parent!) || IsObjectLiteralExpression(input.Parent!))
                        {
                            _state.AddDiagnostic(CreateDiagnosticForNode(input, Diagnostics.Computed_property_names_on_class_or_object_literals_cannot_be_inferred_with_isolatedDeclarations));
                            return null;
                        }
                        else if ((IsInterfaceDeclaration(input.Parent!) || IsTypeLiteralNode(input.Parent!)) && !IsEntityNameExpression(input.Name()!.Expression()!))
                        {
                            // Type declarations just need to double-check that the input computed name is an entity name expression
                            _state.AddDiagnostic(CreateDiagnosticForNode(input, Diagnostics.Computed_properties_must_be_number_or_string_literals_variables_or_dotted_expressions_with_isolatedDeclarations));
                            return null;
                        }
                    }
                }
                else if (!_resolver.IsLateBound(EmitContext().ParseNode(input)!) || !IsEntityNameExpression(input.Name()!.Expression()!))
                {
                    return null;
                }
            }
        }

        // Elide implementation signatures from overload sets
        if (IsFunctionLike(input) && _resolver.IsImplementationOfOverload(input))
        {
            return null;
        }

        if (input.Kind == Kind.SemicolonClassElement)
        {
            return null;
        }

        var heritageClause = input.AsHeritageClause();
        if (heritageClause != null)
        {
            var heritageTypes = heritageClause.Types!.Nodes ?? [];
            if (heritageTypes.Length == 0 || (heritageTypes.Length == 1 && NodeIsMissing(heritageTypes[0])))
            {
                return null;
            }
        }

        var previousEnclosingDeclaration = _enclosingDeclaration;
        if (IsEnclosingDeclaration(input))
        {
            _enclosingDeclaration = input;
        }

        var (canProduceDiagnostic, cleanupDiagnosticContext) = SetupDiagnosticContext(input);
        try
        {
            Node? result;

            switch (input.Kind)
            {
                case Kind.MappedType:
                    result = TransformMappedTypeNode(input.AsMappedTypeNode()!);
                    break;
                case Kind.HeritageClause:
                    result = TransformHeritageClause(input.AsHeritageClause()!);
                    break;
                case Kind.MethodSignature:
                    result = TransformMethodSignatureDeclaration(input.AsMethodSignatureDeclaration()!);
                    break;
                case Kind.MethodDeclaration:
                    result = TransformMethodDeclaration(input.AsMethodDeclaration()!);
                    break;
                case Kind.ConstructSignature:
                    result = TransformConstructSignatureDeclaration(input.AsConstructSignatureDeclaration()!);
                    break;
                case Kind.Constructor:
                    result = TransformConstructorDeclaration(input.AsConstructorDeclaration()!);
                    break;
                case Kind.GetAccessor:
                    result = TransformGetAccesorDeclaration(input.AsGetAccessorDeclaration()!);
                    break;
                case Kind.SetAccessor:
                    result = TransformSetAccessorDeclaration(input.AsSetAccessorDeclaration()!);
                    break;
                case Kind.PropertyDeclaration:
                    result = TransformPropertyDeclaration(input.AsPropertyDeclaration()!);
                    break;
                case Kind.PropertySignature:
                    result = TransformPropertySignatureDeclaration(input.AsPropertySignatureDeclaration()!);
                    break;
                case Kind.CallSignature:
                    result = TransformCallSignatureDeclaration(input.AsCallSignatureDeclaration()!);
                    break;
                case Kind.IndexSignature:
                    result = TransformIndexSignatureDeclaration(input.AsIndexSignatureDeclaration()!);
                    break;
                case Kind.VariableDeclaration:
                    result = TransformVariableDeclaration(input.AsVariableDeclaration()!);
                    break;
                case Kind.TypeParameter:
                    result = TransformTypeParameterDeclaration(input.AsTypeParameterDeclaration()!);
                    break;
                case Kind.ExpressionWithTypeArguments:
                    result = TransformExpressionWithTypeArguments(input.AsExpressionWithTypeArguments()!);
                    break;
                case Kind.TypeReference:
                    result = TransformTypeReference(input.AsTypeReferenceNode()!);
                    break;
                case Kind.ConditionalType:
                    result = TransformConditionalTypeNode(input.AsConditionalTypeNode()!);
                    break;
                case Kind.FunctionType:
                    result = TransformFunctionTypeNode(input.AsFunctionTypeNode()!);
                    break;
                case Kind.ConstructorType:
                    result = TransformConstructorTypeNode(input.AsConstructorTypeNode()!);
                    break;
                case Kind.ImportType:
                    result = TransformImportTypeNode(input.AsImportTypeNode()!);
                    break;
                case Kind.TypeQuery:
                    CheckEntityNameVisibility(input.AsTypeQueryNode()!.ExprName!, _enclosingDeclaration!);
                    result = Visitor().VisitEachChild(input);
                    break;
                case Kind.QualifiedName:
                    if (input.AsQualifiedName()!.Right!.Kind == Kind.PrivateIdentifier)
                    {
                        _state.AddDiagnostic(CreateDiagnosticForNode(input, Diagnostics.Declaration_emit_elides_private_members_but_0_refers_to_a_private_member_Write_an_explicit_type_here, input.AsQualifiedName()!.Right!.Text()));
                    }
                    result = Visitor().VisitEachChild(input);
                    break;
                case Kind.TupleType:
                    result = Visitor().VisitEachChild(input);
                    if (result != null)
                    {
                        if (TypeScript.Transformers.Transformers.IsOriginalNodeSingleLine(EmitContext(), input))
                        {
                            EmitContext().AddEmitFlags(result, EmitFlags.SingleLine);
                        }
                    }
                    break;
                case Kind.JSDocTypeExpression:
                    result = TransformJSDocTypeExpression(input.AsJSDocTypeExpression()!);
                    break;
                case Kind.JSDocTypeLiteral:
                    result = TransformJSDocTypeLiteral(input.AsJSDocTypeLiteral()!);
                    break;
                case Kind.JSDocPropertyTag:
                    result = TransformJSDocPropertyTag(input.AsJSDocParameterOrPropertyTag()!);
                    break;
                case Kind.JSDocAllType:
                    result = TransformJSDocAllType(input.AsJSDocAllType()!);
                    break;
                case Kind.JSDocNullableType:
                    result = TransformJSDocNullableType(input.AsJSDocNullableType()!);
                    break;
                case Kind.JSDocNonNullableType:
                    result = TransformJSDocNonNullableType(input.AsJSDocNonNullableType()!);
                    break;
                case Kind.JSDocOptionalType:
                    result = TransformJSDocOptionalType(input.AsJSDocOptionalType()!);
                    break;
                case Kind.JSDocVariadicType:
                    result = TransformJSDocVariadicType(input.AsJSDocVariadicType()!);
                    break;
                default:
                    result = Visitor().VisitEachChild(input);
                    break;
            }

            if (result != null && canProduceDiagnostic && HasDynamicName(input))
            {
                CheckName(input);
            }

            _enclosingDeclaration = previousEnclosingDeclaration;
            return result;
        }
        finally
        {
            cleanupDiagnosticContext.Restore();
        }
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) checkName(node *ast.Node)</c>（transform.go:708-721）。</summary>
    private void CheckName(Node node)
    {
        var oldDiag = _state._getSymbolAccessibilityDiagnostic;
        if (!_suppressNewDiagnosticContexts)
        {
            _state._getSymbolAccessibilityDiagnostic = CreateGetSymbolAccessibilityDiagnosticForNodeName(node);
        }
        _state._errorNameNode = node.Name();
        Debug.Assert(HasDynamicName(node)); // Should only be called with dynamic names
        var entityName = node.Name()!.Expression();
        CheckEntityNameVisibility(entityName!, _enclosingDeclaration!);
        if (!_suppressNewDiagnosticContexts)
        {
            _state._getSymbolAccessibilityDiagnostic = oldDiag;
        }
        _state._errorNameNode = null;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformMappedTypeNode(input *ast.MappedTypeNode) *ast.Node</c>（transform.go:723-740）。</summary>
    private Node? TransformMappedTypeNode(MappedTypeNode input)
    {
        // handle missing template type nodes, since the printer does not
        Node? typeNode;
        if (input.Type == null)
        {
            typeNode = Factory().NewKeywordTypeNode(Kind.AnyKeyword);
        }
        else
        {
            typeNode = Visitor().Visit!(input.Type!);
        }
        return Factory().UpdateMappedTypeNode(
            input,
            input.ReadonlyToken,
            Visitor().Visit!(input.TypeParameter!),
            Visitor().Visit!(input.NameType!),
            input.QuestionToken,
            typeNode,
            null);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformHeritageClause(clause *ast.HeritageClause) *ast.Node</c>（transform.go:742-759）。</summary>
    private Node? TransformHeritageClause(HeritageClause clause)
    {
        var retainedClauses = Filter(clause.Types!.Nodes, t =>
        {
            var name = GetHeritageClauseElementName(t);
            return IsEntityName(name) || IsEntityNameExpression(name) ||
                (clause.Token == Kind.ExtendsKeyword && IsExpressionWithTypeArguments(t) && t.Expression()!.Kind == Kind.NullKeyword);
        });
        if (retainedClauses.Length == 0)
        {
            return null; // elide empty clause
        }
        if (retainedClauses.Length == (clause.Types!.Nodes?.Length ?? 0))
        {
            return Visitor().VisitEachChild(clause);
        }
        return Factory().UpdateHeritageClause(
            clause,
            clause.Token,
            Visitor().VisitNodes(Factory().NewNodeList(retainedClauses)));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformImportTypeNode(input *ast.ImportTypeNode) *ast.Node</c>（transform.go:761-776）。</summary>
    private Node? TransformImportTypeNode(ImportTypeNode input)
    {
        if (!IsLiteralImportTypeNode(input))
        {
            return input;
        }
        return Factory().UpdateImportTypeNode(
            input,
            input.IsTypeOf,
            Factory().UpdateLiteralTypeNode(
                input.Argument!.AsLiteralTypeNode()!,
                RewriteModuleSpecifier(input, input.Argument!.AsLiteralTypeNode()!.Literal)),
            input.Attributes,
            input.Qualifier,
            Visitor().VisitNodes(input.TypeArguments));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformConstructorTypeNode(input *ast.ConstructorTypeNode) *ast.Node</c>（transform.go:778-786）。</summary>
    private Node? TransformConstructorTypeNode(ConstructorTypeNode input)
    {
        return Factory().UpdateConstructorTypeNode(
            input,
            EnsureModifiers(input),
            Visitor().VisitNodes(input.TypeParameters),
            UpdateParamList(input, input.Parameters!),
            Visitor().Visit!(input.Type!));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformFunctionTypeNode(input *ast.FunctionTypeNode) *ast.Node</c>（transform.go:788-795）。</summary>
    private Node? TransformFunctionTypeNode(FunctionTypeNode input)
    {
        return Factory().UpdateFunctionTypeNode(
            input,
            Visitor().VisitNodes(input.TypeParameters),
            UpdateParamList(input, input.Parameters!),
            Visitor().Visit!(input.Type!));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformConditionalTypeNode(input *ast.ConditionalTypeNode) *ast.Node</c>（transform.go:797-813）。</summary>
    private Node? TransformConditionalTypeNode(ConditionalTypeNode input)
    {
        var checkType = Visitor().Visit!(input.CheckType!);
        var extendsType = Visitor().Visit!(input.ExtendsType!);
        var oldEnclosingDecl = _enclosingDeclaration;
        _enclosingDeclaration = input.TrueType;
        var trueType = Visitor().Visit!(input.TrueType!);
        _enclosingDeclaration = oldEnclosingDecl;
        var falseType = Visitor().Visit!(input.FalseType!);

        return Factory().UpdateConditionalTypeNode(
            input,
            checkType,
            extendsType,
            trueType,
            falseType);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformTypeReference(input *ast.TypeReferenceNode) *ast.Node</c>（transform.go:815-818）。</summary>
    private Node? TransformTypeReference(TypeReferenceNode input)
    {
        CheckEntityNameVisibility(input.TypeName!, _enclosingDeclaration!);
        return Visitor().VisitEachChild(input);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformExpressionWithTypeArguments(input *ast.ExpressionWithTypeArguments) *ast.Node</c>（transform.go:820-825）。</summary>
    private Node? TransformExpressionWithTypeArguments(ExpressionWithTypeArguments input)
    {
        if (IsEntityName(input.Expression) || IsEntityNameExpression(input.Expression))
        {
            CheckEntityNameVisibility(input.Expression!, _enclosingDeclaration!);
        }
        return Visitor().VisitEachChild(input);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformTypeParameterDeclaration(input *ast.TypeParameterDeclaration) *ast.Node</c>（transform.go:827-839）。</summary>
    private Node? TransformTypeParameterDeclaration(TypeParameterDeclaration input)
    {
        if (IsPrivateMethodTypeParameter(_host, input) && (input.DefaultType != null || input.Constraint != null))
        {
            return Factory().UpdateTypeParameterDeclaration(
                input,
                input.Modifiers(),
                input.Name(),
                null,
                input.Expression,
                null);
        }
        return Visitor().VisitEachChild(input);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformVariableDeclaration(input *ast.VariableDeclaration) *ast.Node</c>（transform.go:841-857）。</summary>
    private Node? TransformVariableDeclaration(VariableDeclaration input)
    {
        if (_state._currentSourceFile!.CommonJSModuleIndicator != null && IsVariableDeclarationInitializedToRequire(input))
        {
            return TransformCjsRequireVariableDeclaration(input);
        }
        if (IsBindingPattern(input.Name()) && HasAnyBindingInitializers(input.Name()!.AsBindingPattern()!))
        {
            return RecreateBindingPattern(input.Name()!.AsBindingPattern()!);
        }
        // Variable declaration types also suppress new diagnostic contexts, provided the contexts wouldn't be made for binding pattern types
        _suppressNewDiagnosticContexts = true;
        return Factory().UpdateVariableDeclaration(
            input,
            _bindingNameVisitor.VisitNode(input.Name()),
            null,
            EnsureType(input, false),
            EnsureNoInitializer(input));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformCjsRequireVariableDeclaration(input *ast.VariableDeclaration) *ast.Node</c>（transform.go:875-905）。</summary>
    private Node? TransformCjsRequireVariableDeclaration(VariableDeclaration input)
    {
        var specifier = RewriteModuleSpecifier(input, input.Initializer!.AsCallExpression()!.Arguments!.Nodes![0]);
        if (IsIdentifier(input.Name()!))
        {
            // `const x = require("something")` -> `import x = require("something")`
            return Factory().NewImportEqualsDeclaration(null, false, input.Name(), Factory().NewExternalModuleReference(specifier));
        }
        else if (IsArrayBindingPattern(input.Name()!))
        {
            // TODO: Is this actually reachable? should we error on this?
            return null;
        }
        else
        { // object binding pattern

            // `const {x, y: z} = require("something")` -> `import {x, y as z} from "something"`
            var b = input.Name()!.AsBindingPattern()!;
            var importSpecifiers = new List<Node>();
            foreach (var elem in b.Elements!.Nodes ?? [])
            {
                if (!IsIdentifier(elem.Name()!))
                {
                    continue; // nested destructuring, bail
                }
                importSpecifiers.Add(Factory().NewImportSpecifier(false, elem.PropertyName(), elem.Name()));
            }
            return Factory().NewImportDeclaration(
                null,
                Factory().NewImportClause(
                    Kind.Unknown,
                    null,
                    Factory().NewNamedImports(Factory().NewNodeList(importSpecifiers.ToArray()))),
                specifier,
                null);
        }
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) recreateBindingPattern(input *ast.BindingPattern) *ast.Node</c>（transform.go:907-927）。</summary>
    private Node? RecreateBindingPattern(BindingPattern input)
    {
        var results = new List<Node>();
        foreach (var elem in input.Elements!.Nodes ?? [])
        {
            var result = RecreateBindingElement(elem.AsBindingElement()!);
            if (result == null)
            {
                continue;
            }
            if (result.Kind == Kind.SyntaxList)
            {
                results.AddRange(result.AsSyntaxList()!.Children!);
            }
            else
            {
                results.Add(result);
            }
        }
        if (results.Count == 0)
        {
            return null;
        }
        if (results.Count == 1)
        {
            return results[0];
        }
        return Factory().NewSyntaxList(results.ToArray());
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) recreateBindingElement(e *ast.BindingElement) *ast.Node</c>（transform.go:929-945）。</summary>
    private Node? RecreateBindingElement(BindingElement e)
    {
        if (e.Name() == null)
        {
            return null;
        }
        if (!GetBindingNameVisible(_resolver, e))
        {
            return null;
        }
        if (IsBindingPattern(e.Name()!))
        {
            return RecreateBindingPattern(e.Name()!.AsBindingPattern()!);
        }
        return Factory().NewVariableDeclaration(
            e.Name(),
            null,
            EnsureType(e, false),
            null); // TODO: possible strada bug - not emitting const initialized binding pattern elements?
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformIndexSignatureDeclaration(input *ast.IndexSignatureDeclaration) *ast.Node</c>（transform.go:947-958）。</summary>
    private Node? TransformIndexSignatureDeclaration(IndexSignatureDeclaration input)
    {
        var t = Visitor().Visit!(input.Type!);
        if (t == null)
        {
            t = Factory().NewKeywordTypeNode(Kind.AnyKeyword);
        }
        return Factory().UpdateIndexSignatureDeclaration(
            input,
            EnsureModifiers(input),
            UpdateParamList(input, input.Parameters!),
            t);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformCallSignatureDeclaration(input *ast.CallSignatureDeclaration) *ast.Node</c>（transform.go:960-967）。</summary>
    private Node? TransformCallSignatureDeclaration(CallSignatureDeclaration input)
    {
        return Factory().UpdateCallSignatureDeclaration(
            input,
            EnsureTypeParams(input, input.TypeParameters!),
            UpdateParamList(input, input.Parameters!),
            EnsureType(input, false));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformPropertySignatureDeclaration(input *ast.PropertySignatureDeclaration) *ast.Node</c>（transform.go:969-983）。</summary>
    private Node? TransformPropertySignatureDeclaration(PropertySignatureDeclaration input)
    {
        if (IsPrivateIdentifier(input.Name()!))
        {
            return null;
        }
        var result = Factory().UpdatePropertySignatureDeclaration(
            input,
            EnsureModifiers(input),
            input.Name(),
            input.PostfixToken,
            EnsureType(input, false),
            EnsureNoInitializer(input)); // TODO: possible strada bug (fixed here) - const property signatures never initialized
        PreservePartialJsDoc(result, input);
        return result;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformPropertyDeclaration(input *ast.PropertyDeclaration) *ast.Node</c>（transform.go:985-1002）。</summary>
    private Node? TransformPropertyDeclaration(PropertyDeclaration input)
    {
        if (IsPrivateIdentifier(input.Name()!))
        {
            return null;
        }
        // Remove definite assignment assertion (!) from declaration files
        var postfixToken = input.PostfixToken;
        if (postfixToken != null && postfixToken.Kind == Kind.ExclamationToken)
        {
            postfixToken = null;
        }
        return Factory().UpdatePropertyDeclaration(
            input,
            EnsureModifiers(input),
            input.Name(),
            postfixToken,
            EnsureType(input, false),
            EnsureNoInitializer(input));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformSetAccessorDeclaration(input *ast.SetAccessorDeclaration) *ast.Node</c>（transform.go:1004-1019）。</summary>
    private Node? TransformSetAccessorDeclaration(SetAccessorDeclaration input)
    {
        if (IsPrivateIdentifier(input.Name()!))
        {
            return null;
        }

        return Factory().UpdateSetAccessorDeclaration(
            input,
            EnsureModifiers(input),
            input.Name(),
            null, // accessors shouldn't have type params
            UpdateAccessorParamList(input, _host.GetEffectiveDeclarationFlags(EmitContext().ParseNode(input)!, ModifierFlags.Private) != 0),
            null,
            null,
            null);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformGetAccesorDeclaration(input *ast.GetAccessorDeclaration) *ast.Node</c>（transform.go:1021-1035）。</summary>
    private Node? TransformGetAccesorDeclaration(GetAccessorDeclaration input)
    {
        if (IsPrivateIdentifier(input.Name()!))
        {
            return null;
        }
        return Factory().UpdateGetAccessorDeclaration(
            input,
            EnsureModifiers(input),
            input.Name(),
            null, // accessors shouldn't have type params
            UpdateAccessorParamList(input, _host.GetEffectiveDeclarationFlags(EmitContext().ParseNode(input)!, ModifierFlags.Private) != 0),
            EnsureType(input, false),
            null,
            null);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) updateAccessorParamList(input *ast.Node, isPrivate bool) *ast.ParameterList</c>（transform.go:1037-1072）。</summary>
    private NodeList? UpdateAccessorParamList(Node input, bool isPrivate)
    {
        var newParams = new List<Node>();
        if (!isPrivate)
        {
            var thisParam = GetThisParameter(input);
            if (thisParam != null)
            {
                newParams.Add(EnsureParameter(thisParam.AsParameterDeclaration()!));
            }
        }
        if (IsSetAccessorDeclaration(input))
        {
            Node? valueParam = null;
            if (!isPrivate)
            {
                var setAccessorParameters = input.AsSetAccessorDeclaration()!.Parameters!.Nodes ?? [];
                if (newParams.Count == 1 && setAccessorParameters.Length >= 2)
                {
                    valueParam = EnsureParameter(setAccessorParameters[1].AsParameterDeclaration()!);
                }
                else if (newParams.Count == 0 && setAccessorParameters.Length >= 1)
                {
                    valueParam = EnsureParameter(setAccessorParameters[0].AsParameterDeclaration()!);
                }
            }
            if (valueParam == null)
            {
                // When synthesizing a missing value parameter, emit `value: any` for non-private accessors to match TypeScript's declaration emit behavior.
                Node? t = null;
                if (!isPrivate)
                {
                    t = Factory().NewKeywordTypeNode(Kind.AnyKeyword);
                }
                valueParam = Factory().NewParameterDeclaration(
                    null,
                    null,
                    Factory().NewIdentifier("value"),
                    null,
                    t,
                    null);
            }
            newParams.Add(valueParam!);
        }
        return Factory().NewNodeList(newParams.ToArray());
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformConstructorDeclaration(input *ast.ConstructorDeclaration) *ast.Node</c>（transform.go:1074-1085）。</summary>
    private Node? TransformConstructorDeclaration(ConstructorDeclaration input)
    {
        // A constructor declaration may not have a type annotation
        return Factory().UpdateConstructorDeclaration(
            input,
            EnsureModifiers(input),
            null, // no type params
            UpdateParamList(input, input.Parameters!),
            null, // no return type
            null,
            null);
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformConstructSignatureDeclaration(input *ast.ConstructSignatureDeclaration) *ast.Node</c>（transform.go:1087-1094）。</summary>
    private Node? TransformConstructSignatureDeclaration(ConstructSignatureDeclaration input)
    {
        return Factory().UpdateConstructSignatureDeclaration(
            input,
            EnsureTypeParams(input, input.TypeParameters!),
            UpdateParamList(input, input.Parameters!),
            EnsureType(input, false));
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) omitPrivateMethodType(input *ast.Node) *ast.Node</c>（transform.go:1096-1120）。</summary>
    private Node? OmitPrivateMethodType(Node input)
    {
        var symbolDeclarations = input.Symbol?.Declarations ?? [];
        if (input.Symbol != null && symbolDeclarations.Length > 0 && symbolDeclarations[0] != input)
        {
            return null;
        }
        Node result;
        if (IsMethodSignatureDeclaration(input))
        {
            result = Factory().NewPropertySignatureDeclaration(
                EnsureModifiers(input),
                input.Name(),
                null, /*postfixToken*/
                null, /*typeNode*/
                null /*initializer*/);
        }
        else
        {
            result = Factory().NewPropertyDeclaration(
                EnsureModifiers(input),
                input.Name(),
                null, /*postfixToken*/
                null, /*typeNode*/
                null /*initializer*/);
        }
        PreserveJsDoc(result, input);
        return result;
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformMethodSignatureDeclaration(input *ast.MethodSignatureDeclaration) *ast.Node</c>（transform.go:1122-1138）。</summary>
    private Node? TransformMethodSignatureDeclaration(MethodSignatureDeclaration input)
    {
        if (_host.GetEffectiveDeclarationFlags(EmitContext().ParseNode(input)!, ModifierFlags.Private) != 0)
        {
            return OmitPrivateMethodType(input);
        }
        else if (IsPrivateIdentifier(input.Name()!))
        {
            return null;
        }
        else
        {
            return Factory().UpdateMethodSignatureDeclaration(
                input,
                EnsureModifiers(input),
                input.Name(),
                input.PostfixToken,
                EnsureTypeParams(input, input.TypeParameters!),
                UpdateParamList(input, input.Parameters!),
                EnsureType(input, false));
        }
    }

    /// <summary>Go: <c>func (tx *DeclarationTransformer) transformMethodDeclaration(input *ast.MethodDeclaration) *ast.Node</c>（transform.go:1140-1159）。</summary>
    private Node? TransformMethodDeclaration(MethodDeclaration input)
    {
        if (_host.GetEffectiveDeclarationFlags(EmitContext().ParseNode(input)!, ModifierFlags.Private) != 0)
        {
            return OmitPrivateMethodType(input);
        }
        else if (IsPrivateIdentifier(input.Name()!))
        {
            return null;
        }
        else
        {
            return Factory().UpdateMethodDeclaration(
                input,
                EnsureModifiers(input),
                null,
                input.Name(),
                input.PostfixToken,
                EnsureTypeParams(input, input.TypeParameters!),
                UpdateParamList(input, input.Parameters!),
                EnsureType(input, false),
                null,
                null);
        }
    }
}

public static partial class Declarations
{
    /// <summary>Go: <c>func hasAnyBindingInitializers(bindingPattern *ast.BindingPattern) bool</c>（transform.go:859-873）。</summary>
    internal static bool HasAnyBindingInitializers(BindingPattern bindingPattern)
    {
        foreach (var elem in bindingPattern.Elements!.Nodes ?? [])
        {
            if (!IsBindingElement(elem))
            {
                continue;
            }
            var e = elem.AsBindingElement()!;
            if (e.Initializer != null)
            {
                return true;
            }
            if (e.Name() != null && IsBindingPattern(e.Name()!) && HasAnyBindingInitializers(e.Name()!.AsBindingPattern()!))
            {
                return true;
            }
        }
        return false;
    }
}
