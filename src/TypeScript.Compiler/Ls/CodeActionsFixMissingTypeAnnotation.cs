// ported from: internal/ls/codeactions_fixmissingtypeannotation.go:1-1421
//
// 与 Go 的偏离（逐条）：
// 1) 包级未导出变量/常量 → `Ls` 静态类的 `internal static readonly` / `internal const` 字段（`_camelCase`）；
//    包级未导出函数 → `Ls` 静态类的 `internal static` 方法（PascalCase，方法不带 `_`）。Go 导出的
//    `IsolatedDeclarationsFixProvider` → `public static readonly`。
// 2) 未导出类型 `typePrintMode` → `internal enum TypePrintMode`（成员去前缀：`Full`/`Relative`/`Widened`）；
//    未导出结构体 `isolatedDeclarationsFixer` → `internal sealed class IsolatedDeclarationsFixer`，未导出字段 →
//    `internal _camelCase`。
// 3) `change.Tracker` → `LsChange.Tracker`；`change.NodeOptions{Prefix: …}` → `new NodeOptions { Prefix = … }`；
//    `change.LeadingTriviaOptionNone` → `LeadingTriviaOption.None`。
// 4) ⚠ Go 的 `ls.isConstAssertion` 与既有 `ast.IsConstAssertion`（Ast/Utilities.cs:3670）**语义完全相同** ⇒ 本文件
//    **不再声明** `Ls.IsConstAssertion`（避免与 `using static TypeScript.Ast.Ast;` 的 CS0121 二义），直接调用 Ast 版。
// 5) `nodebuilder.Flags*` → 别名 `NodeBuilderFlags`；`nodebuilder.InternalFlags*` → `NodeBuilderInternalFlags`。
//    `checker.NewNodeBuilderEx` 的 `idToSymbol`（Go `map[*ast.IdentifierNode]*ast.Symbol`）→ `Dictionary<Node, Symbol>`。
// 6) `printer.AutoGenerateOptions{Flags: printer.GeneratedIdentifierFlagsOptimistic}` →
//    `new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic }`（`TypeScript.Printer` 命名空间）。
//    注意 `Tracker.NodeFactory` 声明为 `Ast.NodeFactory`，而 `NewUniqueName*`/`NewGeneratedNameForNode` 是
//    `Printer.NodeFactory` 上的成员 ⇒ 这些调用点必须走 `changeTracker.EmitContext.Factory`（类型是 `Printer.NodeFactory`）。
// 7) `factory.NewModuleDeclaration(...)` 在 C# 收 `ModifierList?`（Go 是 `*ast.ModifierList`）；
//    `TypeScript.Checker.NodeBuilder` 类与 `TypeScript.NodeBuilder` 命名空间同名 ⇒ 别名 `CheckerNodeBuilder`
//    （本文件目前未直接引用它，但保留别名以便 `Getter` 语义一致）；`NodeFactory` 在 `Ast`/`Printer` 两个命名空间都有
//    ⇒ 别名 `NodeFactory = TypeScript.Ast.NodeFactory`。
// 8) `TypePrintMode` 的闭包捕获：Go 1.22+ 的 `for _, mode := range modes` 是每迭代变量 ⇒ C# `foreach` 一致。
// 9) `strconv.Itoa(i)` → `i.ToString(CultureInfo.InvariantCulture)`。
// 10) `t.Flags()`/`t.ObjectFlags()`/`t.Target()` 是 `Type` 上的方法（Go 同名）；`typeNode.TypeArguments()` 在
//     `Node` 上是方法、在 `TypeReferenceNode` 实例上是字段 ⇒ 本文件里 `typeNode` 声明为 `Node` ⇒ 用方法。
// 11) Go 的 `[]*ast.TypeNode` / `[]*ast.Node`（可 nil）→ `Node[]?` / `List<Node>?`；`len(x) == 0` 前先 `?? []`。
// 12) `f.checker.RequiresAddingImplicitUndefined(node)`（单参重载）与 `GetUnionTypeEx(types, UnionReduction.None)`。
// 13) `autoimport.TryGetAutoImportableReferenceFromTypeNode` → `AutoImport.TryGetAutoImportableReferenceFromTypeNode`
//     （返回具名元组 `(Node? TypeNode, Symbol[] Symbols)`）。
// 14) Go 的 `factory.UpdateExportAssignment(exportAssignment, defaultExport.Modifiers(), false, nil, defaultIdentifier.AsNode())`
//     在 C# 是 `UpdateExportAssignment(ExportAssignment, ModifierList?, bool, Node?, Node?)`。
using System;
using System.Collections.Generic;
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsChange;
using TypeScript.LsUtil;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.LsChange.LsChange;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using CheckerType = TypeScript.Checker.Checker;
using ObjectFlags = TypeScript.Checker.ObjectFlags;
using TypeFlags = TypeScript.Checker.TypeFlags;
using UnionReduction = TypeScript.Checker.UnionReduction;
using Loc = TypeScript.Locale.Locale;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;
using NodeFactory = TypeScript.Ast.NodeFactory;
using Program = TypeScript.Compiler.Program;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

public static partial class Ls
{
    internal static readonly int[] _isolatedDeclarationsFixErrorCodes =
    [
        Function_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations.Code(),
        Method_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations.Code(),
        At_least_one_accessor_must_have_an_explicit_type_annotation_with_isolatedDeclarations.Code(),
        Variable_must_have_an_explicit_type_annotation_with_isolatedDeclarations.Code(),
        Parameter_must_have_an_explicit_type_annotation_with_isolatedDeclarations.Code(),
        Property_must_have_an_explicit_type_annotation_with_isolatedDeclarations.Code(),
        Expression_type_can_t_be_inferred_with_isolatedDeclarations.Code(),
        Binding_elements_with_initializers_can_t_be_exported_directly_with_isolatedDeclarations.Code(),
        Computed_property_names_on_class_or_object_literals_cannot_be_inferred_with_isolatedDeclarations.Code(),
        Computed_properties_must_be_number_or_string_literals_variables_or_dotted_expressions_with_isolatedDeclarations.Code(),
        Enum_member_initializers_must_be_computable_without_references_to_external_symbols_with_isolatedDeclarations.Code(),
        Extends_clause_can_t_contain_an_expression_with_isolatedDeclarations.Code(),
        Objects_that_contain_shorthand_properties_can_t_be_inferred_with_isolatedDeclarations.Code(),
        Objects_that_contain_spread_assignments_can_t_be_inferred_with_isolatedDeclarations.Code(),
        Arrays_with_spread_elements_can_t_inferred_with_isolatedDeclarations.Code(),
        Default_exports_can_t_be_inferred_with_isolatedDeclarations.Code(),
        Only_const_arrays_can_be_inferred_with_isolatedDeclarations.Code(),
        Assigning_properties_to_functions_without_declaring_them_is_not_supported_with_isolatedDeclarations_Add_an_explicit_declaration_for_the_properties_assigned_to_this_function.Code(),
        Declaration_emit_for_this_parameter_requires_implicitly_adding_undefined_to_its_type_This_is_not_supported_with_isolatedDeclarations.Code(),
        Type_containing_private_name_0_can_t_be_used_with_isolatedDeclarations.Code(),
        Add_satisfies_and_a_type_assertion_to_this_expression_satisfies_T_as_T_to_make_the_type_explicit.Code(),
    ];

    internal const string _fixMissingTypeAnnotationOnExportsFixID = "fixMissingTypeAnnotationOnExports";

    /// <summary>Go: <c>var IsolatedDeclarationsFixProvider = &amp;CodeFixProvider{...}</c>（codeactions_fixmissingtypeannotation.go:46-51）。</summary>
    public static readonly CodeFixProvider IsolatedDeclarationsFixProvider = new()
    {
        ErrorCodes = _isolatedDeclarationsFixErrorCodes,
        GetCodeActions = GetIsolatedDeclarationsCodeActions,
        FixIds = [_fixMissingTypeAnnotationOnExportsFixID],
        GetAllCodeActions = GetAllIsolatedDeclarationsCodeActions,
    };

    /// <summary>canHaveTypeAnnotationKinds are the node kinds that can have type annotations added（Go 的 `map[ast.Kind]bool`）。</summary>
    internal static readonly HashSet<Kind> _canHaveTypeAnnotationKinds =
    [
        Kind.GetAccessor,
        Kind.MethodDeclaration,
        Kind.PropertyDeclaration,
        Kind.FunctionDeclaration,
        Kind.FunctionExpression,
        Kind.ArrowFunction,
        Kind.VariableDeclaration,
        Kind.Parameter,
        Kind.ExportAssignment,
        Kind.ClassDeclaration,
        Kind.ObjectBindingPattern,
        Kind.ArrayBindingPattern,
    ];

    /// <summary>declarationEmitNodeBuilderFlags are the node builder flags used for declaration emit.</summary>
    internal const NodeBuilderFlags _declarationEmitNodeBuilderFlags =
        NodeBuilderFlags.MultilineObjectLiterals |
        NodeBuilderFlags.WriteClassExpressionAsTypeLiteral |
        NodeBuilderFlags.UseTypeOfFunction |
        NodeBuilderFlags.UseStructuralFallback |
        NodeBuilderFlags.AllowEmptyTuple |
        NodeBuilderFlags.GenerateNamesForShadowedTypeParams |
        NodeBuilderFlags.NoTruncation;

    private static (CodeAction[]? Actions, Exception? Error) GetIsolatedDeclarationsCodeActions(Context ctx, CodeFixContext fixContext)
    {
        var (ch, done) = fixContext.Program!.GetTypeCheckerForFile(ctx, fixContext.SourceFile!);
        try
        {
            List<CodeAction>? fixes = null;

            void AddFix(CodeAction? action)
            {
                if (action is null)
                {
                    return;
                }
                (fixes ??= []).Add(action);
            }

            // Match TS ordering: Full annotation, Relative annotation, Widened annotation,
            // Full inline, Relative inline, Widened inline, Full extract
            TypePrintMode[] modes = [TypePrintMode.Full, TypePrintMode.Relative, TypePrintMode.Widened];

            foreach (var mode in modes)
            {
                AddFix(TryCodeAction(ctx, fixContext, ch, f =>
                {
                    f._typePrintMode = mode;
                    return f.AddTypeAnnotation(fixContext.Span);
                }));
            }

            foreach (var mode in modes)
            {
                AddFix(TryCodeAction(ctx, fixContext, ch, f =>
                {
                    f._typePrintMode = mode;
                    return f.AddInlineAssertion(fixContext.Span);
                }));
            }

            // extractAsVariable only in Full mode
            AddFix(TryCodeAction(ctx, fixContext, ch, f =>
            {
                f._typePrintMode = TypePrintMode.Full;
                return f.ExtractAsVariable(fixContext.Span);
            }));

            return (fixes?.ToArray(), null);
        }
        finally
        {
            done();
        }
    }

    private static (CombinedCodeActions? Actions, Exception? Error) GetAllIsolatedDeclarationsCodeActions(Context ctx, CodeFixContext fixContext)
    {
        var (ch, done) = fixContext.Program!.GetTypeCheckerForFile(ctx, fixContext.SourceFile!);
        try
        {
            var changeTracker = NewTracker(ctx, fixContext.Program.Options(), fixContext.LS!.FormatOptions(), fixContext.LS._converters!);

            var fixer = new IsolatedDeclarationsFixer(
                sourceFile: fixContext.SourceFile!,
                program: fixContext.Program,
                checker: ch,
                changeTracker: changeTracker,
                importAdder: null,
                locale: Loc.FromContext(ctx),
                fixedNodes: [],
                typePrintMode: TypePrintMode.Full);

            var allDiags = GetAllDiagnostics(ctx, fixContext.Program, fixContext.SourceFile!);
            foreach (var diag in allDiags)
            {
                if (IsFixableDiagnostic(diag, _isolatedDeclarationsFixErrorCodes))
                {
                    fixer.AddTypeAnnotation(new TextRange(diag.Loc().Pos(), diag.Loc().End()));
                }
            }

            foreach (var sym in fixer._symbolsToImport)
            {
                fixer.AddSymbolToExistingImport(sym);
            }

            var (changes, _) = changeTracker.GetChanges();
            var fileChanges = changes.TryGetValue(fixContext.SourceFile!.OriginalFileName(), out var fc) ? fc : [];
            if (fileChanges.Length == 0)
            {
                return (null, null);
            }

            return (new CombinedCodeActions
            {
                Description = Add_all_missing_type_annotations.Localize(Loc.FromContext(ctx)),
                Changes = fileChanges,
            }, null);
        }
        finally
        {
            done();
        }
    }

    private static CodeAction? TryCodeAction(Context ctx, CodeFixContext fixContext, CheckerType ch, Func<IsolatedDeclarationsFixer, string> fn)
    {
        var changeTracker = NewTracker(ctx, fixContext.Program!.Options(), fixContext.LS!.FormatOptions(), fixContext.LS._converters!);

        ImportAdder? importAdder = null;
        // importAdder may be nil if the auto-import registry is not available;
        // type node transformation still works without it, just without adding imports.

        var fixer = new IsolatedDeclarationsFixer(
            sourceFile: fixContext.SourceFile!,
            program: fixContext.Program,
            checker: ch,
            changeTracker: changeTracker,
            importAdder: importAdder,
            locale: Loc.FromContext(ctx),
            fixedNodes: []);

        var description = fn(fixer);
        if (description == "")
        {
            return null;
        }

        // Add any symbols that need to be imported to existing import declarations
        foreach (var sym in fixer._symbolsToImport)
        {
            fixer.AddSymbolToExistingImport(sym);
        }

        var (changes, _) = changeTracker.GetChanges();
        var fileChanges = changes.TryGetValue(fixContext.SourceFile!.OriginalFileName(), out var fc) ? fc : [];

        // Add import edits if import adder has fixes
        if (importAdder is not null && importAdder.HasFixes())
        {
            fileChanges = [.. fileChanges, .. importAdder.Edits()];
        }

        if (fileChanges.Length == 0)
        {
            return null;
        }

        return new CodeAction
        {
            Description = description,
            Changes = fileChanges,
            FixID = _fixMissingTypeAnnotationOnExportsFixID,
            FixAllDescription = Add_all_missing_type_annotations.Localize(Loc.FromContext(ctx)),
        };
    }

    /// <summary>Go: <c>type typePrintMode int</c>（未导出；成员名去 Go 的 `typePrintMode` 前缀）。</summary>
    internal enum TypePrintMode
    {
        Full = 0,
        /// <summary>typeof X</summary>
        Relative = 1,
        /// <summary>widened literal type</summary>
        Widened = 2,
    }

    /// <summary>isExpandoPropertyDeclarationForFix matches TS's isExpandoPropertyDeclaration which includes
    /// PropertyAccessExpression, ElementAccessExpression, and BinaryExpression. The shared
    /// ast.IsExpandoPropertyDeclaration was narrowed to BinaryExpression only for checker purposes.</summary>
    internal static bool IsExpandoPropertyDeclarationForFix(Node? node)
    {
        return node is not null && (IsPropertyAccessExpression(node) || IsElementAccessExpression(node) || IsBinaryExpression(node));
    }

    internal static Node? FindExpandoFunction(CheckerType ch, Node node)
    {
        var expandoDeclaration = FindAncestorOrQuit(node, n =>
        {
            if (IsStatement(n))
            {
                return FindAncestorResult.Quit;
            }
            if (IsExpandoPropertyDeclarationForFix(n))
            {
                return FindAncestorResult.True;
            }
            return FindAncestorResult.False;
        });

        if (expandoDeclaration is null || !IsExpandoPropertyDeclarationForFix(expandoDeclaration))
        {
            return null;
        }

        var assignmentTarget = expandoDeclaration;
        // Some late bound expando members use the whole expression as the declaration.
        if (IsBinaryExpression(assignmentTarget))
        {
            assignmentTarget = assignmentTarget.AsBinaryExpression()!.Left!;
            if (!IsExpandoPropertyDeclarationForFix(assignmentTarget))
            {
                return null;
            }
        }

        Node? expression;
        if (IsPropertyAccessExpression(assignmentTarget))
        {
            expression = assignmentTarget.AsPropertyAccessExpression()!.Expression;
        }
        else if (IsElementAccessExpression(assignmentTarget))
        {
            expression = assignmentTarget.AsElementAccessExpression()!.Expression;
        }
        else
        {
            return null;
        }

        var targetType = ch.GetTypeAtLocation(expression!);
        if (targetType is null)
        {
            return null;
        }

        var properties = ch.GetPropertiesOfType(targetType);
        var found = false;
        foreach (var p in properties)
        {
            if (p.ValueDeclaration == expandoDeclaration || p.ValueDeclaration == expandoDeclaration.Parent)
            {
                found = true;
                break;
            }
        }
        if (!found)
        {
            return null;
        }

        var symbol = targetType.Symbol();
        if (symbol is null || symbol.ValueDeclaration is null)
        {
            return null;
        }

        var fn = symbol.ValueDeclaration;
        if ((IsFunctionExpression(fn) || IsArrowFunction(fn)) && fn.Parent is not null && IsVariableDeclaration(fn.Parent))
        {
            return fn.Parent;
        }
        if (IsFunctionDeclaration(fn))
        {
            return fn;
        }

        return null;
    }

    /// <summary>needsParenthesizedExpressionForAssertion checks if an expression needs parentheses for an assertion.</summary>
    internal static bool NeedsParenthesizedExpressionForAssertion(Node node)
    {
        return !IsEntityNameExpression(node) && !IsCallExpression(node) && !IsObjectLiteralExpression(node) && !IsArrayLiteralExpression(node);
    }

    /// <summary>createAsExpression creates an <c>expr as Type</c> expression, parenthesizing if needed.</summary>
    internal static Node CreateAsExpression(NodeFactory factory, Node node, Node typeNode)
    {
        if (NeedsParenthesizedExpressionForAssertion(node))
        {
            node = factory.NewParenthesizedExpression(node);
        }
        return factory.NewAsExpression(node, typeNode);
    }

    /// <summary>typeToStringForDiag converts a type node to a string for use in diagnostic descriptions.
    /// It reuses the change tracker's EmitContext so that generated identifier names are resolved
    /// consistently with the actual code edits, and passes the source file so that the printer's
    /// name generator can check for conflicts with existing file-level identifiers.</summary>
    internal static string TypeToStringForDiag(Node typeNode, SourceFile sourceFile, Tracker ct)
    {
        var savedFlags = ct.EmitContext.EmitFlags(typeNode);
        ct.EmitContext.SetEmitFlags(typeNode, savedFlags | EmitFlags.SingleLine);
        var p = NewPrinter(
            new PrinterOptions
            {
                NewLine = NewLineKind.LF,
            },
            new PrintHandlers(),
            ct.EmitContext);
        var (writer, release) = GetSingleLineStringWriter();
        try
        {
            p.Write(typeNode, sourceFile, writer, null);
            ct.EmitContext.SetEmitFlags(typeNode, savedFlags);
            var result = writer.String();
            if (result.Length > 160)
            {
                return result[..157] + "...";
            }
            return result;
        }
        finally
        {
            release();
        }
    }

    /// <summary>findAncestorWithMissingType walks up the ancestor chain to find a node that
    /// can have a type annotation and is missing one.</summary>
    internal static Node? FindAncestorWithMissingType(Node node)
    {
        return FindAncestor(node, n =>
        {
            if (!_canHaveTypeAnnotationKinds.Contains(n.Kind))
            {
                return false;
            }
            if (IsObjectBindingPattern(n) || IsArrayBindingPattern(n))
            {
                return n.Parent is not null && IsVariableDeclaration(n.Parent);
            }
            return true;
        });
    }

    /// <summary>findBestFittingNode walks up from the token to find the node that best fits the diagnostic span.</summary>
    internal static Node? FindBestFittingNode(Node? node, TextRange span)
    {
        if (node is null)
        {
            return null;
        }
        while (node is not null && node.End() < span.Pos() + span.Len())
        {
            node = node.Parent;
        }
        while (node!.Parent is not null && node.Parent.Pos() == node.Pos() && node.Parent.End() == node.End())
        {
            node = node.Parent;
        }
        if (IsIdentifier(node) && HasInitializer(node.Parent) && node.Parent!.Initializer() is not null)
        {
            return node.Parent.Initializer();
        }
        if (IsIdentifier(node) && node.Parent is not null && IsShorthandPropertyAssignment(node.Parent))
        {
            return node.Parent;
        }
        return node;
    }

    /// <summary>isNamedDeclarationKind matches TS's isDeclarationKind, which is narrower than Go's IsDeclaration.
    /// Go's IsDeclaration returns true for any node with DeclarationData (including CallExpression),
    /// while TS's isDeclaration only returns true for specific named declaration kinds.</summary>
    internal static bool IsNamedDeclarationKind(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ArrowFunction:
            case Kind.BindingElement:
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.ClassStaticBlockDeclaration:
            case Kind.Constructor:
            case Kind.EnumDeclaration:
            case Kind.EnumMember:
            case Kind.ExportSpecifier:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.GetAccessor:
            case Kind.ImportClause:
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportSpecifier:
            case Kind.InterfaceDeclaration:
            case Kind.JsxAttribute:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.ModuleDeclaration:
            case Kind.NamespaceExportDeclaration:
            case Kind.NamespaceImport:
            case Kind.NamespaceExport:
            case Kind.Parameter:
            case Kind.PropertyAssignment:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.SetAccessor:
            case Kind.ShorthandPropertyAssignment:
            case Kind.TypeAliasDeclaration:
            case Kind.TypeParameter:
            case Kind.VariableDeclaration:
            case Kind.JSDocTypedefTag:
            case Kind.JSDocCallbackTag:
            case Kind.JSDocPropertyTag:
            case Kind.NamedTupleMember:
                return true;
        }
        return false;
    }

    /// <summary>isValueSignatureDeclaration checks if a node is a function-like declaration that produces a value.</summary>
    internal static bool IsValueSignatureDeclaration(Node? node)
    {
        return node is not null &&
            (IsFunctionExpression(node) || IsArrowFunction(node) || IsMethodDeclaration(node) ||
                IsAccessor(node) || IsFunctionDeclaration(node) || IsConstructorDeclaration(node));
    }

    /// <summary>getIdentifierNameForNode derives a meaningful variable name from a node expression.
    /// For property access expressions like <c>obj.foo</c>, returns "foo". Otherwise returns "newLocal".</summary>
    internal static string GetIdentifierNameForNode(Node node)
    {
        if (IsPropertyAccessExpression(node))
        {
            var name = node.AsPropertyAccessExpression()!.Name();
            if (name is not null && IsIdentifier(name) && !IsPrivateIdentifier(name) && IdentifierToKeywordKind(name.AsIdentifier()!) == Kind.Unknown)
            {
                return name.Text();
            }
        }
        return "newLocal";
    }

    /// <summary>endOfRequiredTypeParameters finds the number of type arguments that are
    /// actually required (i.e., differ from their defaults).</summary>
    internal static int EndOfRequiredTypeParameters(CheckerType ch, Type t)
    {
        var typeArgs = ch.GetTypeArguments(t);
        if (typeArgs.Length == 0)
        {
            return 0;
        }
        var target = t.Target();
        if (target is null || target.AsInterfaceType() is null)
        {
            return typeArgs.Length;
        }
        var typeParams = target.AsInterfaceType()!.TypeParameters();
        var localTypeParams = target.AsInterfaceType()!.LocalTypeParameters();
        var outerCount = typeParams.Length - localTypeParams.Length;
        for (var cutoff = 0; cutoff < typeArgs.Length; cutoff++)
        {
            // Skip cutoff positions where the local type parameter has no default.
            // This matches TS's check for constraint === undefined on localTypeParameters,
            // which in practice skips type parameters without defaults (e.g. Set<T>
            // where T has no default should not have <unknown> elided).
            var localIdx = cutoff - outerCount;
            if (localIdx < 0 || localIdx >= localTypeParams.Length || !TypeParamHasDefault(localTypeParams[localIdx]))
            {
                continue;
            }
            var filledIn = ch.FillMissingTypeArguments(typeArgs[..cutoff], typeParams, cutoff, false);
            var allMatch = true;
            for (var i = 0; i < filledIn.Length; i++)
            {
                if (filledIn[i] != typeArgs[i])
                {
                    allMatch = false;
                    break;
                }
            }
            if (allMatch)
            {
                return cutoff;
            }
        }
        return typeArgs.Length;
    }

    /// <summary>typeParamHasDefault checks if a type parameter has a default type declaration.</summary>
    internal static bool TypeParamHasDefault(Type tp)
    {
        var sym = tp.Symbol();
        if (sym is null)
        {
            return false;
        }
        foreach (var decl in sym.Declarations ?? [])
        {
            if (IsTypeParameterDeclaration(decl) && decl.AsTypeParameterDeclaration()!.DefaultType is not null)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>isolatedDeclarationsFixer encapsulates the state for fixing isolated declarations errors.</summary>
    internal sealed class IsolatedDeclarationsFixer
    {
        internal SourceFile _sourceFile;

        internal Program _program;

        internal CheckerType _checker;

        internal Tracker _changeTracker;

        internal ImportAdder? _importAdder;

        internal Loc _locale;

        internal HashSet<Node> _fixedNodes;

        internal TypePrintMode _typePrintMode;

        internal List<Symbol> _symbolsToImport = [];

        /// <summary>set by inferType/relativeType when the target was mutated (e.g., spread decomposition)</summary>
        internal bool _mutatedTarget;

        internal IsolatedDeclarationsFixer(
            SourceFile sourceFile,
            Program program,
            CheckerType checker,
            Tracker changeTracker,
            ImportAdder? importAdder,
            Loc locale,
            HashSet<Node> fixedNodes,
            TypePrintMode typePrintMode = TypePrintMode.Full)
        {
            _sourceFile = sourceFile;
            _program = program;
            _checker = checker;
            _changeTracker = changeTracker;
            _importAdder = importAdder;
            _locale = locale;
            _fixedNodes = fixedNodes;
            _typePrintMode = typePrintMode;
        }

        internal string AddTypeAnnotation(TextRange span)
        {
            var nodeWithDiag = GetTokenAtPosition(_sourceFile, span.Pos());

            var expandoFunction = FindExpandoFunction(_checker, nodeWithDiag);
            if (expandoFunction is not null)
            {
                if (IsFunctionDeclaration(expandoFunction))
                {
                    return CreateNamespaceForExpandoProperties(expandoFunction);
                }
                return FixIsolatedDeclarationError(expandoFunction);
            }

            var nodeMissingType = FindAncestorWithMissingType(nodeWithDiag);
            if (nodeMissingType is not null)
            {
                return FixIsolatedDeclarationError(nodeMissingType);
            }
            return "";
        }

        internal string CreateNamespaceForExpandoProperties(Node expandoFunc)
        {
            var funcDecl = expandoFunc.AsFunctionDeclaration()!;
            if (funcDecl.Name() is null)
            {
                return "";
            }

            var t = _checker.GetTypeAtLocation(expandoFunc);
            var elements = _checker.GetPropertiesOfType(t);
            if (elements.Length == 0)
            {
                return "";
            }

            var factory = _changeTracker.NodeFactory;

            List<Node> newProperties = [];
            foreach (var symbol in elements)
            {
                if (!IsIdentifierText(symbol.Name, LanguageVariant.Standard))
                {
                    continue;
                }
                // skip symbols that already have a variable declaration
                if (symbol.ValueDeclaration is not null && IsVariableDeclaration(symbol.ValueDeclaration))
                {
                    continue;
                }

                var symType = _checker.GetTypeOfSymbol(symbol);
                var typeNode = TypeToMinimizedReferenceType(symType, expandoFunc, _declarationEmitNodeBuilderFlags);
                if (typeNode is null)
                {
                    continue;
                }

                var varDecl = factory.NewVariableDeclaration(factory.NewIdentifier(symbol.Name), null, typeNode, null);
                var exportToken = factory.NewToken(Kind.ExportKeyword);
                var varDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([varDecl]), NodeFlags.None);
                var varStmt = factory.NewVariableStatement(factory.NewModifierList([exportToken]), varDeclList);
                newProperties.Add(varStmt);
            }

            if (newProperties.Count == 0)
            {
                return "";
            }

            List<Node> modifiers = [];
            if (HasSyntacticModifier(expandoFunc, ModifierFlags.Export))
            {
                modifiers.Add(factory.NewToken(Kind.ExportKeyword));
            }
            modifiers.Add(factory.NewToken(Kind.DeclareKeyword));

            var namespaceNode = factory.NewModuleDeclaration(
                factory.NewModifierList(modifiers.ToArray()),
                Kind.NamespaceKeyword,
                factory.NewIdentifier(funcDecl.Name()!.Text()),
                null, /*attributes*/
                factory.NewModuleBlock(factory.NewNodeList(newProperties.ToArray())));
            // Set the flags for namespace
            namespaceNode.Flags = NodeFlags.Ambient | NodeFlags.ExportContext | NodeFlags.ContextFlags;

            _changeTracker.InsertNodeAfter(_sourceFile, expandoFunc, namespaceNode);
            return Annotate_types_of_properties_expando_function_in_a_namespace.Localize(_locale);
        }

        internal string AddInlineAssertion(TextRange span)
        {
            var nodeWithDiag = GetTokenAtPosition(_sourceFile, span.Pos());

            // No inline assertions for expando members
            var expandoFunction = FindExpandoFunction(_checker, nodeWithDiag);
            if (expandoFunction is not null)
            {
                return "";
            }

            var targetNode = FindBestFittingNode(nodeWithDiag, span);
            if (targetNode is null || IsValueSignatureDeclaration(targetNode) || IsValueSignatureDeclaration(targetNode.Parent))
            {
                return "";
            }

            var isExpressionTarget = IsExpression(targetNode);
            var isShorthandPropertyAssignmentTarget = IsShorthandPropertyAssignment(targetNode);

            // Go's IsDeclaration is broader than TS's isDeclaration (e.g. CallExpression has DeclarationData
            // in Go but is not a declaration kind in TS). Use isNamedDeclarationKind to match TS behavior.
            if (!isShorthandPropertyAssignmentTarget && IsNamedDeclarationKind(targetNode))
            {
                return "";
            }
            // No inline assertions on binding patterns
            if (FindAncestor(targetNode, IsBindingPattern) is not null)
            {
                return "";
            }
            // No inline assertions on enum members
            if (FindAncestor(targetNode, IsEnumMember) is not null)
            {
                return "";
            }
            // No support for typeof in extends clauses
            if (isExpressionTarget && (FindAncestorKind(targetNode, Kind.HeritageClause) is not null || FindAncestor(targetNode, IsTypeNode) is not null))
            {
                return "";
            }
            // Can't inline type spread elements
            if (IsSpreadElement(targetNode))
            {
                return "";
            }

            var variableDeclaration = FindAncestorKind(targetNode, Kind.VariableDeclaration);
            Type? variableType = null;
            if (variableDeclaration is not null)
            {
                variableType = _checker.GetTypeAtLocation(variableDeclaration);
            }
            // Can't use typeof on unique symbols
            if (variableType is not null && (variableType.Flags() & TypeFlags.UniqueESSymbol) != 0)
            {
                return "";
            }

            if (!isExpressionTarget && !isShorthandPropertyAssignmentTarget)
            {
                return "";
            }

            var typeNode = InferType(targetNode, variableType);
            if (typeNode is null || _mutatedTarget)
            {
                return "";
            }

            var factory = _changeTracker.NodeFactory;

            if (isShorthandPropertyAssignmentTarget)
            {
                // Insert `: expr as Type` after the shorthand property name
                var clonedName = factory.DeepCloneNode(targetNode.AsShorthandPropertyAssignment()!.Name())!;
                var asExpr = CreateAsExpression(factory, clonedName, typeNode);
                _changeTracker.InsertNodeAt(_sourceFile, new TextPos(targetNode.End()), asExpr, new NodeOptions { Prefix = ": " });
            }
            else if (isExpressionTarget)
            {
                // Replace expression with `(expression) satisfies Type as Type` or `expression satisfies Type as Type`
                var clonedTarget = factory.DeepCloneNode(targetNode)!;
                if (NeedsParenthesizedExpressionForAssertion(targetNode))
                {
                    clonedTarget = factory.NewParenthesizedExpression(clonedTarget);
                }
                var clonedType = factory.DeepCloneNode(typeNode)!;
                var satisfiesAsExpr = factory.NewAsExpression(
                    factory.NewSatisfiesExpression(clonedTarget, clonedType),
                    typeNode);
                _changeTracker.ReplaceNode(_sourceFile, targetNode, satisfiesAsExpr, null);
            }
            else
            {
                return "";
            }

            return Add_satisfies_and_an_inline_type_assertion_with_0.Localize(_locale, TypeToStringForDiag(typeNode, _sourceFile, _changeTracker));
        }

        internal string ExtractAsVariable(TextRange span)
        {
            var nodeWithDiag = GetTokenAtPosition(_sourceFile, span.Pos());
            var targetNode = FindBestFittingNode(nodeWithDiag, span);
            if (targetNode is null || IsValueSignatureDeclaration(targetNode) || IsValueSignatureDeclaration(targetNode.Parent))
            {
                return "";
            }

            if (!IsExpression(targetNode))
            {
                return "";
            }

            var factory = _changeTracker.NodeFactory;

            // Array literals should be marked as const
            if (IsArrayLiteralExpression(targetNode))
            {
                var constRef = factory.NewTypeReferenceNode(factory.NewIdentifier("const"), null);
                var cloned = factory.DeepCloneNode(targetNode)!;
                _changeTracker.ReplaceNode(_sourceFile, targetNode, CreateAsExpression(factory, cloned, constRef), null);
                return Mark_array_literal_as_const.Localize(_locale);
            }

            var parentPropertyAssignment = FindAncestorKind(targetNode, Kind.PropertyAssignment);
            if (parentPropertyAssignment is not null)
            {
                // Identifiers or entity names can already be typeof-ed
                if (parentPropertyAssignment == targetNode.Parent && IsEntityNameExpression(targetNode))
                {
                    return "";
                }

                var tempName = _changeTracker.EmitContext.Factory.NewUniqueNameEx(GetIdentifierNameForNode(targetNode), new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });

                var replacementTarget = targetNode;
                var initializationNode = targetNode;

                // Handle spread elements: walk up to the spread's parent and handle const assertions
                if (IsSpreadElement(replacementTarget))
                {
                    replacementTarget = WalkUpParenthesizedExpressions(replacementTarget.Parent)!;
                    if (replacementTarget.Parent is not null && IsConstAssertion(replacementTarget.Parent))
                    {
                        replacementTarget = replacementTarget.Parent;
                        initializationNode = replacementTarget;
                    }
                    else
                    {
                        var constRef = factory.NewTypeReferenceNode(factory.NewIdentifier("const"), null);
                        initializationNode = CreateAsExpression(factory, factory.DeepCloneNode(replacementTarget)!, constRef);
                    }
                }

                if (IsEntityNameExpression(replacementTarget))
                {
                    return "";
                }

                var clonedInit = factory.DeepCloneNode(initializationNode)!;
                var varDecl = factory.NewVariableDeclaration(tempName, null, null, clonedInit);
                var varDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([varDecl]), NodeFlags.Const);
                var varStmt = factory.NewVariableStatement(null, varDeclList);

                var statement = FindAncestor(targetNode, IsStatement);
                if (statement is null)
                {
                    return "";
                }
                _changeTracker.InsertNodeBefore(_sourceFile, statement, varStmt, false, LeadingTriviaOption.None);

                var typeQuery = factory.NewTypeQueryNode(tempName, null);
                var asExpr = factory.NewAsExpression(tempName, typeQuery);
                _changeTracker.ReplaceNode(_sourceFile, replacementTarget, asExpr, null);

                var idText = TypeToStringForDiag(tempName, _sourceFile, _changeTracker);
                return Extract_to_variable_and_replace_with_0_as_typeof_0.Localize(_locale, idText);
            }

            return "";
        }

        internal string FixIsolatedDeclarationError(Node node)
        {
            // Avoid creating duplicate fixes for the same node
            if (_fixedNodes.Contains(node))
            {
                return "";
            }
            _fixedNodes.Add(node);

            switch (node.Kind)
            {
                case Kind.Parameter:
                case Kind.PropertyDeclaration:
                case Kind.VariableDeclaration:
                    return AddTypeToVariableLike(node);
                case Kind.ArrowFunction:
                case Kind.FunctionExpression:
                case Kind.FunctionDeclaration:
                case Kind.MethodDeclaration:
                case Kind.GetAccessor:
                    return AddTypeToSignatureDeclaration(node);
                case Kind.ExportAssignment:
                    return TransformExportAssignment(node);
                case Kind.ClassDeclaration:
                    return TransformExtendsClauseWithExpression(node);
                case Kind.ObjectBindingPattern:
                case Kind.ArrayBindingPattern:
                    return TransformDestructuringPatterns(node);
                default:
                    return "";
            }
        }

        internal string AddTypeToSignatureDeclaration(Node funcNode)
        {
            if (funcNode.Type() is not null)
            {
                return "";
            }
            var typeNode = InferType(funcNode, null);
            if (typeNode is null)
            {
                return "";
            }
            _changeTracker.TryInsertTypeAnnotation(_sourceFile, funcNode, typeNode);
            return Add_return_type_0.Localize(_locale, TypeToStringForDiag(typeNode, _sourceFile, _changeTracker));
        }

        internal string TransformExportAssignment(Node defaultExport)
        {
            var exportAssignment = defaultExport.AsExportAssignment()!;
            if (exportAssignment.IsExportEquals)
            {
                return "";
            }

            var expression = exportAssignment.Expression!;
            var typeNode = InferType(expression, null);
            if (typeNode is null)
            {
                return "";
            }

            var factory = _changeTracker.NodeFactory;

            var defaultIdentifier = _changeTracker.EmitContext.Factory.NewUniqueName("_default");

            // Deep clone the expression so synthesized nodes don't reference original source positions
            var clonedExpression = factory.DeepCloneNode(expression)!;

            var varDecl = factory.NewVariableDeclaration(defaultIdentifier, null, typeNode, clonedExpression);
            var varDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([varDecl]), NodeFlags.Const);
            var varStmt = factory.NewVariableStatement(null, varDeclList);

            var newExport = factory.UpdateExportAssignment(defaultExport.AsExportAssignment()!, defaultExport.Modifiers(), false, null, defaultIdentifier);

            _changeTracker.ReplaceNodeWithNodes(_sourceFile, defaultExport, [varStmt, newExport], null);
            return Extract_default_export_to_variable.Localize(_locale);
        }

        internal string TransformExtendsClauseWithExpression(Node classDecl)
        {
            var cd = classDecl.AsClassDeclaration()!;
            Node? extendsClause = null;
            if (cd.HeritageClauses is not null)
            {
                foreach (var clause in cd.HeritageClauses.Nodes ?? [])
                {
                    if (clause.AsHeritageClause()!.Token == Kind.ExtendsKeyword)
                    {
                        extendsClause = clause;
                        break;
                    }
                }
            }
            if (extendsClause is null)
            {
                return "";
            }

            var heritageTypes = extendsClause.AsHeritageClause()!.Types;
            if (heritageTypes is null || (heritageTypes.Nodes?.Length ?? 0) == 0)
            {
                return "";
            }
            var heritageExpression = heritageTypes.Nodes![0];
            var expression = heritageExpression.AsExpressionWithTypeArguments()!.Expression!;

            var heritageTypeNode = InferType(expression, null);
            if (heritageTypeNode is null)
            {
                return "";
            }

            var factory = _changeTracker.NodeFactory;

            var baseName = "Anonymous";
            if (cd.Name() is not null)
            {
                baseName = cd.Name()!.Text() + "Base";
            }
            var baseClassName = _changeTracker.EmitContext.Factory.NewUniqueNameEx(baseName, new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });

            // Create: const <BaseName>: <type> = <expression>;
            var clonedExpression = factory.DeepCloneNode(expression)!;
            var varDecl = factory.NewVariableDeclaration(baseClassName, null, heritageTypeNode, clonedExpression);
            var varDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([varDecl]), NodeFlags.Const);
            var varStmt = factory.NewVariableStatement(null, varDeclList);

            _changeTracker.InsertNodeBefore(_sourceFile, classDecl, varStmt, false, LeadingTriviaOption.None);

            // Replace the heritage expression with the base class name
            _changeTracker.ReplaceNode(_sourceFile, heritageExpression, factory.NewExpressionWithTypeArguments(baseClassName, null), null);

            return Extract_base_class_to_variable.Localize(_locale);
        }

        internal string TransformDestructuringPatterns(Node bindingPattern)
        {
            var enclosingVariableDeclaration = bindingPattern.Parent;
            if (enclosingVariableDeclaration is null || !IsVariableDeclaration(enclosingVariableDeclaration))
            {
                return "";
            }
            var enclosingVarStmt = enclosingVariableDeclaration.Parent?.Parent;
            if (enclosingVarStmt is null || !IsVariableStatement(enclosingVarStmt))
            {
                return "";
            }

            var initializer = enclosingVariableDeclaration.Initializer();
            if (initializer is null)
            {
                return "";
            }

            var factory = _changeTracker.NodeFactory;
            List<Node> newNodes = [];

            Node baseExprNode;
            if (!IsIdentifier(initializer))
            {
                // Create a temporary variable for complex expressions
                var tempName = _changeTracker.EmitContext.Factory.NewUniqueNameEx("dest", new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
                var clonedInitializer = factory.DeepCloneNode(initializer)!;
                var varDecl = factory.NewVariableDeclaration(tempName, null, null, clonedInitializer);
                var varDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([varDecl]), NodeFlags.Const);
                var varStmt = factory.NewVariableStatement(null, varDeclList);
                newNodes.Add(varStmt);
                baseExprNode = tempName;
            }
            else
            {
                // Use a new identifier to avoid referencing original source positions
                baseExprNode = factory.NewIdentifier(initializer.Text());
            }

            // Extract each binding element as a separate variable with type annotation
            ExtractBindingElements(bindingPattern, baseExprNode, newNodes, enclosingVarStmt);

            if (newNodes.Count == 0)
            {
                return "";
            }

            // If the enclosing variable statement has multiple declarations, preserve the non-destructuring ones
            var declList = enclosingVarStmt.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!;
            if ((declList.Declarations!.Nodes?.Length ?? 0) > 1)
            {
                List<Node> remainingDecls = [];
                foreach (var d in declList.Declarations.Nodes!)
                {
                    if (d != enclosingVariableDeclaration)
                    {
                        remainingDecls.Add(d);
                    }
                }
                if (remainingDecls.Count > 0)
                {
                    newNodes.Add(factory.UpdateVariableStatement(
                        enclosingVarStmt.AsVariableStatement()!,
                        enclosingVarStmt.AsVariableStatement()!.Modifiers(),
                        factory.UpdateVariableDeclarationList(
                            declList,
                            factory.NewNodeList(remainingDecls.ToArray()),
                            declList.Flags)));
                }
            }

            _changeTracker.ReplaceNodeWithNodes(_sourceFile, enclosingVarStmt, newNodes.ToArray(), null);
            return Extract_binding_expressions_to_variable.Localize(_locale);
        }

        internal void ExtractBindingElements(Node bindingPattern, Node baseExpr, List<Node> newNodes, Node enclosingVarStmt)
        {
            var factory = _changeTracker.NodeFactory;

            if (IsObjectBindingPattern(bindingPattern))
            {
                foreach (var element in bindingPattern.AsBindingPattern()!.Elements!.Nodes ?? [])
                {
                    if (IsOmittedExpression(element))
                    {
                        continue;
                    }
                    var be = element.AsBindingElement()!;
                    var name = be.Name();
                    if (name is null)
                    {
                        continue;
                    }

                    // Build property access expression
                    Node? accessExpr;
                    if (be.PropertyName is not null && IsComputedPropertyName(be.PropertyName))
                    {
                        // Handle computed property names: create a temp variable for the computed expression
                        var computedExpression = be.PropertyName.AsComputedPropertyName()!.Expression!;
                        var identifierForComputedProperty = _changeTracker.EmitContext.Factory.NewGeneratedNameForNode(computedExpression);
                        var compVarDecl = factory.NewVariableDeclaration(identifierForComputedProperty, null, null, computedExpression);
                        var compVarDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([compVarDecl]), NodeFlags.Const);
                        var compVarStmt = factory.NewVariableStatement(null, compVarDeclList);
                        newNodes.Add(compVarStmt);
                        accessExpr = factory.NewElementAccessExpression(baseExpr, null, identifierForComputedProperty, NodeFlags.None);
                    }
                    else if (be.PropertyName is not null)
                    {
                        // Use property name text (handles identifiers, string literals, numeric literals)
                        var propText = be.PropertyName.Text();
                        accessExpr = factory.NewPropertyAccessExpression(baseExpr, null, factory.NewIdentifier(propText), NodeFlags.None);
                    }
                    else if (IsIdentifier(name))
                    {
                        accessExpr = factory.NewPropertyAccessExpression(baseExpr, null, factory.NewIdentifier(name.Text()), NodeFlags.None);
                    }
                    else
                    {
                        continue;
                    }

                    if (IsBindingPattern(name))
                    {
                        ExtractBindingElements(name, accessExpr, newNodes, enclosingVarStmt);
                    }
                    else
                    {
                        EmitBindingElementVariable(factory, name, be, accessExpr, newNodes, enclosingVarStmt);
                    }
                }
            }
            else if (IsArrayBindingPattern(bindingPattern))
            {
                var elements = bindingPattern.AsBindingPattern()!.Elements!.Nodes ?? [];
                for (var i = 0; i < elements.Length; i++)
                {
                    var element = elements[i];
                    if (IsOmittedExpression(element))
                    {
                        continue;
                    }
                    var be = element.AsBindingElement()!;
                    var name = be.Name();
                    if (name is null)
                    {
                        continue;
                    }

                    var accessExpr = factory.NewElementAccessExpression(baseExpr, null, factory.NewNumericLiteral(i.ToString(CultureInfo.InvariantCulture), TokenFlags.None), NodeFlags.None);

                    if (IsBindingPattern(name))
                    {
                        ExtractBindingElements(name, accessExpr, newNodes, enclosingVarStmt);
                    }
                    else
                    {
                        EmitBindingElementVariable(factory, name, be, accessExpr, newNodes, enclosingVarStmt);
                    }
                }
            }
        }

        /// <summary>emitBindingElementVariable creates a variable declaration for a single binding element,
        /// handling default initializers by creating a ternary <c>temp === undefined ? default : temp</c>.</summary>
        internal void EmitBindingElementVariable(NodeFactory factory, Node name, Node be, Node accessExpr, List<Node> newNodes, Node enclosingVarStmt)
        {
            var typeNode = InferType(name, null);
            var variableInitializer = accessExpr;

            if (be.AsBindingElement()!.Initializer is not null)
            {
                // Create a temp variable to hold the accessed value, then use a conditional expression
                // to apply the default: temp === undefined ? defaultValue : temp
                var propName = be.AsBindingElement()!.PropertyName;
                var tempBaseName = "temp";
                if (propName is not null && IsIdentifier(propName))
                {
                    tempBaseName = propName.Text();
                }
                var tempName = _changeTracker.EmitContext.Factory.NewUniqueNameEx(tempBaseName, new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });
                var tempVarDecl = factory.NewVariableDeclaration(tempName, null, null, variableInitializer);
                var tempVarDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([tempVarDecl]), NodeFlags.Const);
                var tempVarStmt = factory.NewVariableStatement(null, tempVarDeclList);
                newNodes.Add(tempVarStmt);

                variableInitializer = factory.NewConditionalExpression(
                    factory.NewBinaryExpression(
                        null,
                        tempName,
                        null,
                        factory.NewToken(Kind.EqualsEqualsEqualsToken),
                        factory.NewIdentifier("undefined")),
                    factory.NewToken(Kind.QuestionToken),
                    be.AsBindingElement()!.Initializer,
                    factory.NewToken(Kind.ColonToken),
                    variableInitializer);
            }

            var exportModifier = GetExportModifier(enclosingVarStmt);
            var varDecl = factory.NewVariableDeclaration(factory.NewIdentifier(name.Text()), null, typeNode, variableInitializer);
            var varDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([varDecl]), NodeFlags.Const);
            var varStmt = factory.NewVariableStatement(exportModifier, varDeclList);
            newNodes.Add(varStmt);
        }

        internal ModifierList? GetExportModifier(Node enclosingVarStmt)
        {
            if (HasSyntacticModifier(enclosingVarStmt, ModifierFlags.Export))
            {
                var exportToken = _changeTracker.NodeFactory.NewToken(Kind.ExportKeyword);
                return _changeTracker.NodeFactory.NewModifierList([exportToken]);
            }
            return null;
        }

        internal Node? InferType(Node node, Type? variableType)
        {
            _mutatedTarget = false;

            // Handle Relative mode first: return typeof X for identifiers
            if (_typePrintMode == TypePrintMode.Relative)
            {
                return RelativeType(node);
            }

            Type? t;

            if (IsValueSignatureDeclaration(node))
            {
                var signature = _checker.GetSignatureFromDeclaration(node);
                if (signature is not null)
                {
                    var typePredicate = _checker.GetTypePredicateOfSignature(signature);
                    if (typePredicate is not null)
                    {
                        if (typePredicate.Type() is null)
                        {
                            return null;
                        }
                        var enclosingDecl = FindAncestor(node, IsDeclaration);
                        enclosingDecl ??= _sourceFile.AsNode();
                        var flags = _declarationEmitNodeBuilderFlags;
                        if ((typePredicate.Type()!.Flags() & TypeFlags.UniqueESSymbol) != 0)
                        {
                            flags |= NodeBuilderFlags.AllowUniqueESSymbolType;
                        }
                        var result = _checker.TypePredicateToTypePredicateNode(typePredicate, enclosingDecl, flags, null);
                        if (result is not null)
                        {
                            return result.AsNode();
                        }
                        return null;
                    }
                    t = _checker.GetReturnTypeOfSignature(signature);
                }
                else
                {
                    t = null;
                }
            }
            else
            {
                t = _checker.GetTypeAtLocation(node);
            }

            if (t is null)
            {
                return null;
            }

            // Handle Widened mode: return widened literal type if different
            if (_typePrintMode == TypePrintMode.Widened)
            {
                if (variableType is not null)
                {
                    t = variableType;
                }
                var widenedType = _checker.GetWidenedLiteralType(t);
                if (_checker.IsTypeAssignableTo(widenedType, t))
                {
                    return null; // widened type is same, no fix needed
                }
                t = widenedType;
            }

            var enclosingDeclaration = FindAncestor(node, IsDeclaration);
            enclosingDeclaration ??= _sourceFile.AsNode();

            var nodeBuilderFlags = _declarationEmitNodeBuilderFlags | GetExtraFlags(node, t);

            // For parameters that require adding implicit undefined, add it to the type
            if (IsParameterDeclaration(node) && _checker.RequiresAddingImplicitUndefined(node))
            {
                t = _checker.GetUnionTypeEx([_checker.GetUndefinedType(), t], UnionReduction.None);
            }

            return TypeToMinimizedReferenceType(t, enclosingDeclaration, nodeBuilderFlags);
        }

        internal NodeBuilderFlags GetExtraFlags(Node node, Type t)
        {
            if ((IsVariableDeclaration(node) ||
                (IsPropertyDeclaration(node) && HasSyntacticModifier(node, ModifierFlags.Static | ModifierFlags.Readonly))) &&
                (t.Flags() & TypeFlags.UniqueESSymbol) != 0)
            {
                return NodeBuilderFlags.AllowUniqueESSymbolType;
            }
            return NodeBuilderFlags.None;
        }

        /// <summary>createTypeOfFromEntityNameExpression creates a <c>typeof X</c> type query node.</summary>
        internal Node CreateTypeOfFromEntityNameExpression(Node node)
        {
            return _changeTracker.NodeFactory.NewTypeQueryNode(
                _changeTracker.NodeFactory.DeepCloneNode(node)!, null);
        }

        /// <summary>typeFromArraySpreadElements decomposes an array literal with spread elements into
        /// separate variables, returning a tuple type of typeof references.</summary>
        internal Node? TypeFromArraySpreadElements(ArrayLiteralExpression node, string name)
        {
            var isInConstContext = FindAncestor(node, IsConstAssertion) is not null;
            if (!isInConstContext)
            {
                return null;
            }
            if (name == "")
            {
                name = "temp";
            }
            var factory = _changeTracker.NodeFactory;
            return TypeFromSpreads(
                node,
                name,
                isInConstContext,
                n => n.AsArrayLiteralExpression()!.Elements!.Nodes ?? [],
                IsSpreadElement,
                expr => factory.NewSpreadElement(expr),
                elements => factory.NewArrayLiteralExpression(factory.NewNodeList(elements), true),
                types =>
                {
                    var restTypes = new Node[types.Length];
                    for (var i = 0; i < types.Length; i++)
                    {
                        restTypes[i] = factory.NewRestTypeNode(types[i]);
                    }
                    return factory.NewTupleTypeNode(factory.NewNodeList(restTypes));
                });
        }

        /// <summary>typeFromObjectSpreadAssignment decomposes an object literal with spread assignments into
        /// separate variables, returning an intersection type of typeof references.</summary>
        internal Node? TypeFromObjectSpreadAssignment(ObjectLiteralExpression node, string name)
        {
            var isInConstContext = FindAncestor(node, IsConstAssertion) is not null;
            if (name == "")
            {
                name = "temp";
            }
            var factory = _changeTracker.NodeFactory;
            return TypeFromSpreads(
                node,
                name,
                isInConstContext,
                n => n.AsObjectLiteralExpression()!.Properties?.Nodes ?? [],
                IsSpreadAssignment,
                expr => factory.NewSpreadAssignment(expr),
                elements => factory.NewObjectLiteralExpression(factory.NewNodeList(elements), true),
                types => factory.NewIntersectionTypeNode(factory.NewNodeList(types)));
        }

        /// <summary>typeFromSpreads is the generic spread decomposition function, ported from TS's typeFromSpreads.
        /// It splits a literal with spread elements into separate const variables and returns a composed type.</summary>
        internal Node? TypeFromSpreads(
            Node node,
            string name,
            bool isInConstContext,
            Func<Node, Node[]> getChildren,
            Func<Node, bool> isSpread,
            Func<Node, Node> createSpread,
            Func<Node[], Node> makeNodeOfKind,
            Func<Node[], Node> finalType)
        {
            var factory = _changeTracker.NodeFactory;
            List<Node>? intersectionTypes = null;
            List<Node>? newSpreads = null;
            List<Node>? currentVariableProperties = null;

            var statement = FindAncestor(node, IsStatement);

            var children = getChildren(node);
            foreach (var prop in children)
            {
                if (isSpread(prop))
                {
                    FinalizesVariablePart(factory, name, isInConstContext, statement, makeNodeOfKind, createSpread, ref currentVariableProperties, ref intersectionTypes, ref newSpreads);
                    if (IsEntityNameExpression(prop.Expression()))
                    {
                        (intersectionTypes ??= []).Add(CreateTypeOfFromEntityNameExpression(prop.Expression()!));
                        (newSpreads ??= []).Add(prop);
                    }
                    else
                    {
                        MakeSpreadVariable(factory, name, isInConstContext, statement, createSpread, prop.Expression()!, ref intersectionTypes, ref newSpreads);
                    }
                }
                else
                {
                    (currentVariableProperties ??= []).Add(prop);
                }
            }

            if (newSpreads is null || newSpreads.Count == 0)
            {
                return null;
            }

            FinalizesVariablePart(factory, name, isInConstContext, statement, makeNodeOfKind, createSpread, ref currentVariableProperties, ref intersectionTypes, ref newSpreads);

            _changeTracker.ReplaceNode(_sourceFile, node, makeNodeOfKind(newSpreads!.ToArray()), null);
            _mutatedTarget = true;

            return finalType(intersectionTypes?.ToArray() ?? []);
        }

        /// <summary>makeSpreadVariable creates a const variable for a spread expression and adds it to the decomposition.</summary>
        internal void MakeSpreadVariable(
            NodeFactory factory,
            string name,
            bool isInConstContext,
            Node? statement,
            Func<Node, Node> createSpread,
            Node expression,
            ref List<Node>? intersectionTypes,
            ref List<Node>? newSpreads)
        {
            var tempName = _changeTracker.EmitContext.Factory.NewUniqueNameEx(
                name + "_Part" + ((newSpreads?.Count ?? 0) + 1).ToString(CultureInfo.InvariantCulture),
                new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.Optimistic });

            Node initializer;
            if (!isInConstContext)
            {
                initializer = factory.DeepCloneNode(expression)!;
            }
            else
            {
                var constRef = factory.NewTypeReferenceNode(factory.NewIdentifier("const"), null);
                initializer = factory.NewAsExpression(factory.DeepCloneNode(expression)!, constRef);
            }

            var varDecl = factory.NewVariableDeclaration(tempName, null, null, initializer);
            var varDeclList = factory.NewVariableDeclarationList(factory.NewNodeList([varDecl]), NodeFlags.Const);
            var varStmt = factory.NewVariableStatement(null, varDeclList);

            if (statement is not null)
            {
                _changeTracker.InsertNodeBefore(_sourceFile, statement, varStmt, false, LeadingTriviaOption.None);
            }

            (intersectionTypes ??= []).Add(CreateTypeOfFromEntityNameExpression(tempName));
            (newSpreads ??= []).Add(createSpread(tempName));
        }

        /// <summary>finalizesVariablePart finalizes accumulated non-spread properties into a variable.</summary>
        internal void FinalizesVariablePart(
            NodeFactory factory,
            string name,
            bool isInConstContext,
            Node? statement,
            Func<Node[], Node> makeNodeOfKind,
            Func<Node, Node> createSpread,
            ref List<Node>? currentVariableProperties,
            ref List<Node>? intersectionTypes,
            ref List<Node>? newSpreads)
        {
            if (currentVariableProperties is { Count: > 0 })
            {
                MakeSpreadVariable(factory, name, isInConstContext, statement, createSpread, makeNodeOfKind(currentVariableProperties.ToArray()), ref intersectionTypes, ref newSpreads);
                currentVariableProperties = null;
            }
        }

        /// <summary>relativeType creates a typeof expression for a node, used in TypePrintMode.Relative.
        /// Instead of spelling out the full type, returns <c>typeof X</c> for identifiers.
        /// For object/array literals with spreads, decomposes into separate variables.</summary>
        internal Node? RelativeType(Node node)
        {
            if (IsParameterDeclaration(node))
            {
                return null;
            }
            if (IsShorthandPropertyAssignment(node))
            {
                return CreateTypeOfFromEntityNameExpression(node.AsShorthandPropertyAssignment()!.Name()!);
            }
            if (IsEntityNameExpression(node))
            {
                return CreateTypeOfFromEntityNameExpression(node);
            }
            if (IsConstAssertion(node))
            {
                return RelativeType(node.Expression()!);
            }
            if (IsArrayLiteralExpression(node))
            {
                var varDecl = FindAncestorKind(node, Kind.VariableDeclaration);
                var partName = "";
                if (varDecl is not null && IsIdentifier(varDecl.Name()!))
                {
                    partName = varDecl.Name()!.AsIdentifier()!.Text;
                }
                return TypeFromArraySpreadElements(node.AsArrayLiteralExpression()!, partName);
            }
            if (IsObjectLiteralExpression(node))
            {
                var varDecl = FindAncestorKind(node, Kind.VariableDeclaration);
                var partName = "";
                if (varDecl is not null && IsIdentifier(varDecl.Name()!))
                {
                    partName = varDecl.Name()!.AsIdentifier()!.Text;
                }
                return TypeFromObjectSpreadAssignment(node.AsObjectLiteralExpression()!, partName);
            }
            if (IsVariableDeclaration(node) && node.Initializer() is not null)
            {
                return RelativeType(node.Initializer()!);
            }
            if (IsConditionalExpression(node))
            {
                var cond = node.AsConditionalExpression()!;
                var trueType = RelativeType(cond.WhenTrue!);
                if (trueType is null)
                {
                    return null;
                }
                var trueMutated = _mutatedTarget;
                var falseType = RelativeType(cond.WhenFalse!);
                if (falseType is null)
                {
                    return null;
                }
                _mutatedTarget = trueMutated || _mutatedTarget;
                var factory = _changeTracker.NodeFactory;
                return factory.NewUnionTypeNode(factory.NewNodeList([trueType, falseType]));
            }
            return null;
        }

        /// <summary>typeToMinimizedReferenceType converts a type to a type node, then trims trailing
        /// type arguments that match their defaults.</summary>
        internal Node? TypeToMinimizedReferenceType(Type t, Node enclosingDecl, NodeBuilderFlags flags)
        {
            Dictionary<Node, Symbol> idToSymbol = [];
            // !!! When truncation tracking is supported, check if the type was truncated
            // and return factory.NewKeywordTypeNode(ast.KindAnyKeyword) instead of the truncated node.
            var typeNode = _checker.TypeToTypeNodeEx(t, enclosingDecl, flags, NodeBuilderInternalFlags.WriteComputedProps, idToSymbol);
            if (typeNode is null)
            {
                return null;
            }
            if (IsTypeReferenceNode(typeNode) && (t.ObjectFlags() & ObjectFlags.Reference) != 0)
            {
                var typeArgs = _checker.GetTypeArguments(t);
                var nodeTypeArgs = typeNode.TypeArguments() ?? [];
                if (typeArgs.Length > 0 && nodeTypeArgs.Length > 0)
                {
                    var cutoff = EndOfRequiredTypeParameters(_checker, t);
                    if (cutoff < nodeTypeArgs.Length)
                    {
                        // Trim trailing default type arguments
                        var trimmedArgs = _changeTracker.NodeFactory.NewNodeList(nodeTypeArgs[..cutoff]);
                        typeNode = _changeTracker.NodeFactory.UpdateTypeReferenceNode(
                            typeNode.AsTypeReferenceNode()!,
                            typeNode.AsTypeReferenceNode()!.TypeName,
                            trimmedArgs);
                    }
                }
            }
            // Convert import type references (e.g. import("./path").Name) to simple type references
            // and collect symbols that need to be imported
            var (referenceTypeNode, importableSymbols) = AutoImport.TryGetAutoImportableReferenceFromTypeNode(typeNode, idToSymbol);
            if (referenceTypeNode is not null)
            {
                typeNode = referenceTypeNode;
                _symbolsToImport.AddRange(importableSymbols);
            }
            return typeNode;
        }

        internal string AddTypeToVariableLike(Node decl)
        {
            var typeNode = InferType(decl, null);
            if (typeNode is null)
            {
                return "";
            }
            if (decl.Type() is not null)
            {
                _changeTracker.ReplaceNode(_sourceFile, decl.Type()!, typeNode, null);
            }
            else
            {
                _changeTracker.TryInsertTypeAnnotation(_sourceFile, decl, typeNode);
                // Parenthesize paren-less arrow function parameters (`x => ...`) so the inserted `: T`
                // produces `(x: T) => ...` instead of the invalid `x: T => ...`. Queued after the type
                // annotation so that the `)` edit at param.End() sorts after the annotation insertion.
                if (IsParameterDeclaration(decl) && decl.Parent is not null && IsArrowFunction(decl.Parent))
                {
                    _changeTracker.ParenthesizeArrowParameters(_sourceFile, decl.Parent);
                }
            }
            return Add_annotation_of_type_0.Localize(_locale, TypeToStringForDiag(typeNode, _sourceFile, _changeTracker));
        }

        /// <summary>addSymbolToExistingImport finds the existing import declaration for the symbol's module
        /// and adds the symbol name to the named imports.</summary>
        internal void AddSymbolToExistingImport(Symbol? sym)
        {
            if (sym is null || sym.Parent is null)
            {
                return;
            }

            // Find the module specifier for this symbol
            var moduleSymbol = sym.Parent;
            var symbolName = sym.Name;

            // Walk the source file's import declarations to find the one importing from the same module
            foreach (var stmt in _sourceFile.Statements?.Nodes ?? [])
            {
                if (!IsImportDeclaration(stmt))
                {
                    continue;
                }
                var importDecl = stmt.AsImportDeclaration()!;
                if (importDecl.ImportClause is null)
                {
                    continue;
                }

                // Check if this import is from the same module
                var importModuleSymbol = _checker.GetSymbolAtLocation(importDecl.ModuleSpecifier!);
                if (importModuleSymbol is null || _checker.GetMergedSymbol(importModuleSymbol) != _checker.GetMergedSymbol(moduleSymbol))
                {
                    continue;
                }

                // Found the matching import - add the symbol to named imports
                var importClause = importDecl.ImportClause.AsImportClause()!;
                if (importClause.NamedBindings is not null && IsNamedImports(importClause.NamedBindings))
                {
                    // Add to existing named imports
                    var existingElements = importClause.NamedBindings.AsNamedImports()!.Elements!.Nodes ?? [];
                    var factory = _changeTracker.NodeFactory;
                    var newSpecifier = factory.NewImportSpecifier(false, null, factory.NewIdentifier(symbolName));
                    var newNamedImports = factory.NewNamedImports(factory.NewNodeList([.. existingElements, newSpecifier]));
                    var newImportClause = factory.UpdateImportClause(importClause, importClause.PhaseModifier, importClause.Name(), newNamedImports);
                    var newImportDecl = factory.UpdateImportDeclaration(importDecl, importDecl.Modifiers(), newImportClause, importDecl.ModuleSpecifier, importDecl.Attributes);
                    _changeTracker.ReplaceNode(_sourceFile, stmt, newImportDecl, null);
                }
                return;
            }
        }
    }
}
