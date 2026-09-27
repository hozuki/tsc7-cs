// ported from: internal/compiler/program.go:819-883,1455-1690（GetProgramDiagnostics / content mapper 选项诊断 /
// include processor 诊断 / global・declaration 诊断 / 带前置指令的过滤 / SortAndDeduplicateDiagnostics）
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 包级函数 `SortAndDeduplicateDiagnostics`/`FilterNoEmitSemanticDiagnostics` 放与包同名的静态类 `Compiler`；
//    同时（应 T12EmitterHost 的调用契约）在 `Program` 上给 `SortAndDeduplicateDiagnostics` 一个 public static 转发。
// 2) `sync.Once` 的 `computedDiagnosticsOnce` 等由 T11 的 `IncludeProcessor` 内部实现，这里只调 `GetDiagnostics(this)`。
// 3) `slices.BinarySearchFunc(previousGlobals, diagnostic, ast.CompareDiagnostics)` → 本文件的二分查找 helper
//    （C# 的 `Array.BinarySearch` 不接受比较器）。
// 4) `CommentDirective` 在 C# 是 class，所以 Go 里 `directivesByLine[line] = directive` 的回写是多余的（同一对象），省去。
// 5) `slices.SortFunc`（不稳定排序）→ `Array.Sort`（同为不稳定 introsort）。
namespace TypeScript.Compiler;

using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;

public sealed partial class Program
{
    /// <summary>
    /// Go: <c>func SortAndDeduplicateDiagnostics(...)</c>（program.go:1651）的转发（T12EmitterHost 的 CheckerPool 按
    /// `Program.SortAndDeduplicateDiagnostics(...)` 调用；实现落在与包同名的静态类 `Compiler` 里）。
    /// </summary>
    public static Diagnostic[] SortAndDeduplicateDiagnostics(Diagnostic[] diagnostics) =>
        Compiler.SortAndDeduplicateDiagnostics(diagnostics);

    /// <summary>Go: <c>func (p *Program) GetProgramDiagnostics() []*ast.Diagnostic</c>（program.go:819）。</summary>
    public Diagnostic[] GetProgramDiagnostics() =>
        Compiler.SortAndDeduplicateDiagnostics(
        [
            .. _programDiagnostics,
            .. _contentMapperDiagnostics,
            .. _contentMapperOptionDiagnostics,
            .. _includeProcessor.GetDiagnostics(this).GetGlobalDiagnostics(),
        ]);

    /// <summary>Go: <c>func (p *Program) collectContentMapperOptionDiagnostics()</c>（program.go:828）。</summary>
    internal void CollectContentMapperOptionDiagnostics()
    {
        var project = ContentMapperProject();
        if (project is null)
        {
            return;
        }
        var optionDiagnostics = project.Diagnostics();
        var result = new Diagnostic[optionDiagnostics.Length];
        for (var i = 0; i < optionDiagnostics.Length; i++)
        {
            var diagnostic = optionDiagnostics[i];
            var (file, loc) = TypeScript.TsOptions.TsOptions.GetContentMapperOptionDiagnosticLocation(_opts.Config, diagnostic.Mapper!, diagnostic.Path);
            result[i] = NewExternalDiagnostic(file, loc, diagnostic.Source, Category.Error, diagnostic.Code, diagnostic.MessageText);
        }
        _contentMapperOptionDiagnostics = result;
    }

    /// <summary>Go: <c>func (p *Program) GetIncludeProcessorDiagnostics(sourceFile *ast.SourceFile) []*ast.Diagnostic</c>（program.go:840）。</summary>
    public Diagnostic[] GetIncludeProcessorDiagnostics(SourceFile sourceFile)
    {
        if (SkipTypeChecking(sourceFile, false))
        {
            return [];
        }
        var (filtered, _) = GetDiagnosticsWithPrecedingDirectives(sourceFile, _includeProcessor.GetDiagnostics(this).GetDiagnosticsForFile(sourceFile));
        return filtered;
    }

    /// <summary>Go: <c>func (p *Program) GetGlobalDiagnostics(ctx context.Context) []*ast.Diagnostic</c>（program.go:1455）。</summary>
    public Diagnostic[] GetGlobalDiagnostics(Context ctx)
    {
        if (_files.Length == 0)
        {
            return [];
        }
        if (_compilerCheckerPool is not null)
        {
            return _compilerCheckerPool.GetGlobalDiagnostics();
        }
        // 外部池（项目系统）的 global 诊断是在使用 checker 的过程中增量收集的，没有批量查询。
        return [];
    }

    /// <summary>Go: <c>func (p *Program) GetDeclarationDiagnostics(ctx context.Context, sourceFile *ast.SourceFile) []*ast.Diagnostic</c>（program.go:1467）。</summary>
    public Diagnostic[] GetDeclarationDiagnostics(Context ctx, SourceFile? sourceFile) =>
        CollectDiagnostics(ctx, sourceFile, true /*concurrent*/, GetDeclarationDiagnosticsForFile);

    /// <summary>Go: <c>func (p *Program) getSemanticDiagnosticsWithChecker(ctx, c, sourceFile) []*ast.Diagnostic</c>（program.go:1480）。</summary>
    private Diagnostic[] GetSemanticDiagnosticsWithChecker(Context ctx, Checker c, SourceFile sourceFile) =>
    [
        .. Compiler.FilterNoEmitSemanticDiagnostics(GetBindAndCheckDiagnosticsWithChecker(ctx, c, sourceFile, false /*includeDeferredGlobals*/), Options()),
        .. GetIncludeProcessorDiagnostics(sourceFile),
    ];

    /// <summary>
    /// Go: <c>func (p *Program) getBindAndCheckDiagnosticsWithChecker(ctx, fileChecker, sourceFile, includeDeferredGlobals) []*ast.Diagnostic</c>（program.go:1490）：
    /// 用调用方给的 checker 取单个文件的语义诊断（bind 诊断 + checker 诊断 + @ts-ignore/@ts-expect-error 处理）。
    /// </summary>
    private Diagnostic[] GetBindAndCheckDiagnosticsWithChecker(Context ctx, Checker fileChecker, SourceFile sourceFile, bool includeDeferredGlobals)
    {
        var compilerOptions = Options();
        if (SkipTypeChecking(sourceFile, false))
        {
            return [];
        }
        Diagnostic[] previousGlobals = [];
        if (includeDeferredGlobals)
        {
            previousGlobals = fileChecker.GetGlobalDiagnostics();
        }

        // 创建 checker 会强制 bind，所以此时 bind 诊断已经填好。
        var diags = new List<Diagnostic>(sourceFile.BindDiagnostics());
        diags.AddRange(fileChecker.GetDiagnostics(ctx, sourceFile));

        if (includeDeferredGlobals)
        {
            if (fileChecker.WasCanceled())
            {
                return [];
            }
            var currentGlobals = fileChecker.GetGlobalDiagnostics();
            if (currentGlobals.Length > previousGlobals.Length)
            {
                foreach (var diagnostic in currentGlobals)
                {
                    if (!BinarySearchDiagnostic(previousGlobals, diagnostic))
                    {
                        diags.Add(diagnostic);
                    }
                }
            }
        }

        var isPlainJS = IsPlainJSFile(sourceFile, compilerOptions.CheckJs);
        if (isPlainJS)
        {
            return Filter([.. diags], d => _plainJSErrors.Has(d.Code()));
        }

        var isJS = sourceFile.ScriptKind == ScriptKind.JS || sourceFile.ScriptKind == ScriptKind.JSX;
        var isCheckJS = isJS && IsCheckJSEnabledForFile(sourceFile, compilerOptions);
        if (isCheckJS)
        {
            diags.AddRange(sourceFile.JSDocDiagnostics());
        }

        var (filtered, directivesByLine) = GetDiagnosticsWithPrecedingDirectives(sourceFile, [.. diags]);
        if (directivesByLine is not null)
        {
            foreach (var directive in directivesByLine.Values)
            {
                // 上面把所有用过的指令类型都改成了 @ts-ignore，所以剩下的 @ts-expect-error 就是未使用的，属错误。
                if (directive.Kind == CommentDirectiveKind.ExpectError)
                {
                    filtered = [.. filtered, NewDiagnostic(sourceFile, directive.Loc, Unused_ts_expect_error_directive)];
                }
            }
        }
        return ApplyContentMapperDiagnosticDirectives(sourceFile, filtered);
    }

    /// <summary>Go: <c>func applyContentMapperDiagnosticDirectives(sourceFile, diags) []*ast.Diagnostic</c>（program.go:1543）——包级函数。</summary>
    private static Diagnostic[] ApplyContentMapperDiagnosticDirectives(SourceFile sourceFile, Diagnostic[] diags)
    {
        var directives = sourceFile.DiagnosticDirectives();
        if (directives.Length == 0)
        {
            return diags;
        }
        var used = new bool[directives.Length];
        bool MarkUsed(Diagnostic diag)
        {
            if (!ReferenceEquals(diag.File(), sourceFile) || diag.Source() != "")
            {
                return false;
            }
            for (var i = 0; i < directives.Length; i++)
            {
                var directive = directives[i];
                if (diag.Pos() >= directive.VirtualRange.Pos() && diag.Pos() < directive.VirtualRange.End())
                {
                    used[i] = true;
                    return true;
                }
            }
            return false;
        }

        var filtered = new List<Diagnostic>();
        foreach (var diag in diags)
        {
            if (!MarkUsed(diag))
            {
                filtered.Add(diag);
            }
        }
        for (var i = 0; i < directives.Length; i++)
        {
            var directive = directives[i];
            if (directive.Policy == MappedDiagnosticDirectivePolicy.Expect && !used[i])
            {
                filtered.Add(NewExternalDiagnostic(
                    sourceFile,
                    directive.OriginalRange,
                    directive.Source,
                    Category.Error,
                    directive.UnusedCode,
                    directive.UnusedMessageText));
            }
        }
        return [.. filtered];
    }

    /// <summary>Go: <c>func (p *Program) getDiagnosticsWithPrecedingDirectives(sourceFile, diags)</c>（program.go:1579）。</summary>
    private (Diagnostic[] Filtered, Dictionary<int, CommentDirective>? DirectivesByLine) GetDiagnosticsWithPrecedingDirectives(SourceFile sourceFile, Diagnostic[] diags)
    {
        if (sourceFile.CommentDirectives is null || sourceFile.CommentDirectives.Length == 0)
        {
            return (diags, null);
        }
        // 按行号建指令表
        var directivesByLine = new Dictionary<int, CommentDirective>();
        foreach (var directive in sourceFile.CommentDirectives)
        {
            var line = Scanner.GetECMALineOfPosition(sourceFile, directive.Loc.Pos());
            directivesByLine[line] = directive;
        }
        var lineStarts = Scanner.GetECMALineStarts(sourceFile);
        var filtered = new List<Diagnostic>(diags.Length);
        foreach (var diagnostic in diags)
        {
            var ignoreDiagnostic = false;
            if (!ReferenceEquals(diagnostic.File(), sourceFile))
            {
                filtered.Add(diagnostic);
                continue;
            }
            for (var line = Scanner.ComputeLineOfPosition(lineStarts, diagnostic.Pos()) - 1; line >= 0; line--)
            {
                // 该行有 @ts-ignore 或 @ts-expect-error 指令 → 忽略这条诊断，并把指令类型改成 @ts-ignore 表示已使用。
                if (directivesByLine.TryGetValue(line, out var directive))
                {
                    ignoreDiagnostic = true;
                    directive.Kind = CommentDirectiveKind.Ignore;
                    break;
                }
                // 遇到既不是空行也不是注释的行就停止向前搜索。
                if (!IsCommentOrBlankLine(sourceFile.Text(), lineStarts[line].Value))
                {
                    break;
                }
            }
            if (!ignoreDiagnostic)
            {
                filtered.Add(diagnostic);
            }
        }
        return ([.. filtered], directivesByLine);
    }

    /// <summary>Go: <c>func (p *Program) getDeclarationDiagnosticsForFile(ctx, sourceFile) []*ast.Diagnostic</c>（program.go:1618）。</summary>
    private Diagnostic[] GetDeclarationDiagnosticsForFile(Context ctx, SourceFile sourceFile)
    {
        if (sourceFile.IsDeclarationFile)
        {
            return [];
        }

        var (cached, ok) = _declarationDiagnosticCache.Load(sourceFile);
        if (ok)
        {
            return cached!;
        }

        var (host, done) = EmitHostImpl.NewEmitHost(ctx, this, sourceFile);
        Diagnostic[] diagnostics;
        try
        {
            diagnostics = Compiler.GetDeclarationDiagnostics(host, sourceFile);
        }
        finally
        {
            done();
        }
        var (stored, _) = _declarationDiagnosticCache.LoadOrStore(sourceFile, diagnostics);
        return stored!;
    }

    /// <summary>Go: <c>func (p *Program) getSuggestionDiagnosticsWithChecker(ctx, fileChecker, sourceFile) []*ast.Diagnostic</c>（program.go:1634）。</summary>
    private Diagnostic[] GetSuggestionDiagnosticsWithChecker(Context ctx, Checker fileChecker, SourceFile sourceFile)
    {
        if (SkipTypeChecking(sourceFile, false))
        {
            return [];
        }

        return fileChecker.GetSuggestionDiagnostics(ctx, sourceFile);
    }

    /// <summary>Go: <c>func isCommentOrBlankLine(text string, pos int) bool</c>（program.go:1642）——包级函数（C# 索引是 UTF-16 代码单元）。</summary>
    private static bool IsCommentOrBlankLine(string text, int pos)
    {
        while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t'))
        {
            pos++;
        }
        return pos == text.Length ||
            pos < text.Length && (text[pos] == '\r' || text[pos] == '\n') ||
            pos + 1 < text.Length && text[pos] == '/' && text[pos + 1] == '/';
    }

    /// <summary>Go: <c>func compactAndMergeRelatedInfos(diagnostics) []*ast.Diagnostic</c>（program.go:1659）——包级函数。</summary>
    internal static Diagnostic[] CompactAndMergeRelatedInfos(Diagnostic[] diagnostics)
    {
        if (diagnostics.Length < 2)
        {
            return diagnostics;
        }
        var i = 0;
        var j = 0;
        while (i < diagnostics.Length)
        {
            var d = diagnostics[i];
            var n = 1;
            while (i + n < diagnostics.Length && EqualDiagnosticsNoRelatedInfo(d, diagnostics[i + n]))
            {
                n++;
            }
            if (n > 1)
            {
                List<Diagnostic>? relatedInfos = null;
                for (var k = 0; k < n; k++)
                {
                    var related = diagnostics[i + k].RelatedInformation();
                    if (related is not null)
                    {
                        relatedInfos ??= [];
                        relatedInfos.AddRange(related);
                    }
                }
                if (relatedInfos is not null)
                {
                    var sorted = relatedInfos.ToArray();
                    Array.Sort(sorted, CompareDiagnostics);
                    sorted = CompactEqualDiagnostics(sorted);
                    d = d.Clone().SetRelatedInfo(sorted);
                }
            }
            diagnostics[j] = d;
            i += n;
            j++;
        }
        Array.Clear(diagnostics, j, diagnostics.Length - j);
        return diagnostics[..j];
    }

    /// <summary>Go 的 <c>slices.CompactFunc(xs, ast.EqualDiagnostics)</c>。</summary>
    private static Diagnostic[] CompactEqualDiagnostics(Diagnostic[] diagnostics)
    {
        if (diagnostics.Length < 2)
        {
            return diagnostics;
        }
        var j = 0;
        for (var i = 1; i < diagnostics.Length; i++)
        {
            if (!EqualDiagnostics(diagnostics[j], diagnostics[i]))
            {
                diagnostics[++j] = diagnostics[i];
            }
        }
        return diagnostics[..(j + 1)];
    }

    /// <summary>Go 的 <c>slices.BinarySearchFunc(sorted, target, ast.CompareDiagnostics)</c>（返回是否命中）。</summary>
    private static bool BinarySearchDiagnostic(Diagnostic[] sorted, Diagnostic target)
    {
        var lo = 0;
        var hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (int)(((uint)lo + (uint)hi) >> 1);
            if (CompareDiagnostics(sorted[mid], target) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo < sorted.Length && CompareDiagnostics(sorted[lo], target) == 0;
    }

    /// <summary>Go: <c>var plainJSErrors = collections.NewSetFromItems(...)</c>（program.go:2461）——包级变量。</summary>
    private static readonly Set<int> _plainJSErrors = Collections.NewSetFromItems(
        // binder errors
        Cannot_redeclare_block_scoped_variable_0.Code(),
        A_module_cannot_have_multiple_default_exports.Code(),
        Another_export_default_is_here.Code(),
        The_first_export_default_is_here.Code(),
        Identifier_expected_0_is_a_reserved_word_at_the_top_level_of_a_module.Code(),
        Identifier_expected_0_is_a_reserved_word_in_strict_mode_Modules_are_automatically_in_strict_mode.Code(),
        Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here.Code(),
        X_constructor_is_a_reserved_word.Code(),
        X_delete_cannot_be_called_on_an_identifier_in_strict_mode.Code(),
        Code_contained_in_a_class_is_evaluated_in_JavaScript_s_strict_mode_which_does_not_allow_this_use_of_0_For_more_information_see_https_Colon_Slash_Slashdeveloper_mozilla_org_Slashen_US_Slashdocs_SlashWeb_SlashJavaScript_SlashReference_SlashStrict_mode.Code(),
        Invalid_use_of_0_Modules_are_automatically_in_strict_mode.Code(),
        Invalid_use_of_0_in_strict_mode.Code(),
        A_label_is_not_allowed_here.Code(),
        X_with_statements_are_not_allowed_in_strict_mode.Code(),
        // grammar errors
        A_break_statement_can_only_be_used_within_an_enclosing_iteration_or_switch_statement.Code(),
        A_break_statement_can_only_jump_to_a_label_of_an_enclosing_statement.Code(),
        A_class_declaration_without_the_default_modifier_must_have_a_name.Code(),
        A_class_member_cannot_have_the_0_keyword.Code(),
        A_comma_expression_is_not_allowed_in_a_computed_property_name.Code(),
        A_continue_statement_can_only_be_used_within_an_enclosing_iteration_statement.Code(),
        A_continue_statement_can_only_jump_to_a_label_of_an_enclosing_iteration_statement.Code(),
        A_default_clause_cannot_appear_more_than_once_in_a_switch_statement.Code(),
        A_default_export_must_be_at_the_top_level_of_a_file_or_module_declaration.Code(),
        A_definite_assignment_assertion_is_not_permitted_in_this_context.Code(),
        A_destructuring_declaration_must_have_an_initializer.Code(),
        A_get_accessor_cannot_have_parameters.Code(),
        A_rest_element_cannot_contain_a_binding_pattern.Code(),
        A_rest_element_cannot_have_a_property_name.Code(),
        A_rest_element_cannot_have_an_initializer.Code(),
        A_rest_element_must_be_last_in_a_destructuring_pattern.Code(),
        A_rest_parameter_cannot_have_an_initializer.Code(),
        A_rest_parameter_must_be_last_in_a_parameter_list.Code(),
        A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma.Code(),
        A_return_statement_cannot_be_used_inside_a_class_static_block.Code(),
        A_set_accessor_cannot_have_rest_parameter.Code(),
        A_set_accessor_must_have_exactly_one_parameter.Code(),
        An_export_declaration_can_only_be_used_at_the_top_level_of_a_module.Code(),
        An_export_declaration_cannot_have_modifiers.Code(),
        An_import_declaration_can_only_be_used_at_the_top_level_of_a_module.Code(),
        An_import_declaration_cannot_have_modifiers.Code(),
        An_object_member_cannot_be_declared_optional.Code(),
        Argument_of_dynamic_import_cannot_be_spread_element.Code(),
        Cannot_assign_to_private_method_0_Private_methods_are_not_writable.Code(),
        Cannot_redeclare_identifier_0_in_catch_clause.Code(),
        Catch_clause_variable_cannot_have_an_initializer.Code(),
        Class_decorators_can_t_be_used_with_static_private_identifier_Consider_removing_the_experimental_decorator.Code(),
        Classes_can_only_extend_a_single_class.Code(),
        Classes_may_not_have_a_field_named_constructor.Code(),
        Did_you_mean_to_use_a_Colon_An_can_only_follow_a_property_name_when_the_containing_object_literal_is_part_of_a_destructuring_pattern.Code(),
        Duplicate_label_0.Code(),
        Dynamic_imports_can_only_accept_a_module_specifier_and_an_optional_set_of_attributes_as_arguments.Code(),
        X_for_await_loops_cannot_be_used_inside_a_class_static_block.Code(),
        JSX_attributes_must_only_be_assigned_a_non_empty_expression.Code(),
        JSX_elements_cannot_have_multiple_attributes_with_the_same_name.Code(),
        JSX_expressions_may_not_use_the_comma_operator_Did_you_mean_to_write_an_array.Code(),
        JSX_property_access_expressions_cannot_include_JSX_namespace_names.Code(),
        Jump_target_cannot_cross_function_boundary.Code(),
        Line_terminator_not_permitted_before_arrow.Code(),
        Modifiers_cannot_appear_here.Code(),
        Only_a_single_variable_declaration_is_allowed_in_a_for_in_statement.Code(),
        Only_a_single_variable_declaration_is_allowed_in_a_for_of_statement.Code(),
        Private_identifiers_are_not_allowed_outside_class_bodies.Code(),
        Private_identifiers_are_only_allowed_in_class_bodies_and_may_only_be_used_as_part_of_a_class_member_declaration_property_access_or_on_the_left_hand_side_of_an_in_expression.Code(),
        Property_0_is_not_accessible_outside_class_1_because_it_has_a_private_identifier.Code(),
        Tagged_template_expressions_are_not_permitted_in_an_optional_chain.Code(),
        The_left_hand_side_of_a_for_of_statement_may_not_be_async.Code(),
        The_variable_declaration_of_a_for_in_statement_cannot_have_an_initializer.Code(),
        The_variable_declaration_of_a_for_of_statement_cannot_have_an_initializer.Code(),
        Trailing_comma_not_allowed.Code(),
        Variable_declaration_list_cannot_be_empty.Code(),
        X_0_and_1_operations_cannot_be_mixed_without_parentheses.Code(),
        X_0_expected.Code(),
        X_0_is_not_a_valid_meta_property_for_keyword_1_Did_you_mean_2.Code(),
        X_0_list_cannot_be_empty.Code(),
        X_0_modifier_already_seen.Code(),
        X_0_modifier_cannot_appear_on_a_constructor_declaration.Code(),
        X_0_modifier_cannot_appear_on_a_module_or_namespace_element.Code(),
        X_0_modifier_cannot_appear_on_a_parameter.Code(),
        X_0_modifier_cannot_appear_on_class_elements_of_this_kind.Code(),
        X_0_modifier_cannot_be_used_here.Code(),
        X_0_modifier_must_precede_1_modifier.Code(),
        X_0_declarations_can_only_be_declared_inside_a_block.Code(),
        X_0_declarations_must_be_initialized.Code(),
        X_extends_clause_already_seen.Code(),
        X_let_is_not_allowed_to_be_used_as_a_name_in_let_or_const_declarations.Code(),
        Class_constructor_may_not_be_a_generator.Code(),
        Class_constructor_may_not_be_an_accessor.Code(),
        X_await_expressions_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules.Code(),
        X_await_using_statements_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules.Code(),
        Private_field_0_must_be_declared_in_an_enclosing_class.Code(),
        // Type errors
        This_condition_will_always_return_0_since_JavaScript_compares_objects_by_reference_not_value.Code());
}

/// <summary>Go 的包级函数（无接收者）落在与包同名的静态类里（docs/architecture.md §2）。</summary>
public static partial class Compiler
{
    /// <summary>
    /// Go: <c>func SortAndDeduplicateDiagnostics(diagnostics []*ast.Diagnostic) []*ast.Diagnostic</c>（program.go:1651）——包级函数。
    /// </summary>
    public static Diagnostic[] SortAndDeduplicateDiagnostics(Diagnostic[] diagnostics)
    {
        var clone = (Diagnostic[])diagnostics.Clone();
        Array.Sort(clone, CompareDiagnostics);
        return Program.CompactAndMergeRelatedInfos(clone);
    }

    /// <summary>Go: <c>func FilterNoEmitSemanticDiagnostics(diagnostics, options) []*ast.Diagnostic</c>（program.go:1471）——包级函数。</summary>
    public static Diagnostic[] FilterNoEmitSemanticDiagnostics(Diagnostic[] diagnostics, CompilerOptions options)
    {
        if (!options.NoEmit.IsTrue())
        {
            return diagnostics;
        }
        return Filter(diagnostics, static d => !d.SkippedOnNoEmit());
    }
}
