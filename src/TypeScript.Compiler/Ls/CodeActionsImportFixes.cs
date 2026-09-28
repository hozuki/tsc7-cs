// ported from: internal/ls/codeactions_importfixes.go:1-451
//
// 与 Go 的偏离（逐条）：
// 1) 包级未导出变量/常量 → `Ls` 静态类的 `internal static readonly` / `internal const` 字段（`_camelCase`）；
//    包级未导出函数 → `Ls` 静态类的 `internal`/`private static` 方法（PascalCase）。Go 导出的 `ImportFixProvider`
//    → `public static readonly`。
// 2) 未导出结构体 `fixInfo` → `internal sealed class FixInfo`；未导出字段 → `internal _camelCase`。
//    `symbolNameInfo` → `SymbolNameInfo`（同上）。
// 3) `ast.Diagnostic` 与 `lsproto.Diagnostic` 同名 ⇒ 别名 `AstDiag`（先例 Ls/Diagnostics.cs）。LSP 侧 `AsString()`
//    是 `TypeScript.LsProto` 的扩展方法（`using TypeScript.LsProto;` 后在简单名作用域可见）。
// 4) `strings.Contains(s, sub)` → `s.Contains(sub, StringComparison.Ordinal)`。
// 5) `slices.ContainsFunc(decls, ast.IsTypeOnlyImportOrExportDeclaration)` → LINQ `Any`。
// 6) `slices.SortFunc` → `Array.Sort`（同样不稳定）；`core.CompareBooleans` → `Core.CompareBooleans`。
// 7) `tspath.IsDynamicFileName` / `scanner.IsIntrinsicJsxName` / `scanner.GetTokenPosOfNode` /
//    `astnav.GetTokenAtPosition` 走各自 `using static` 的裸名（§0）。
// 8) `(View, error)` → 具名元组 `(View?, Exception?)`；`fixInfo.fix.Edits(...)` 在 C# 返回
//    `(TextEdit[] Edits, string Description, bool Safe)`。
// 9) `fixContext.SourceFile` 在 C# 是 `SourceFile?` ⇒ 取用处 `!`（Go 里解引用 nil 同样 panic）。
// 10) `Fidelity.IsExact()` 是 `SpanMapExtensions` 的扩展方法 ⇒ 顶层 `using TypeScript.SpanMap;` +
//     `using Kind = TypeScript.Ast.Kind;`（消歧，先例 Ls/CallHierarchy.cs）。
using System;
using System.Collections.Generic;
using System.Linq;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;
using AstDiag = TypeScript.Ast.Diagnostic;
using CheckerType = TypeScript.Checker.Checker;
using Kind = TypeScript.Ast.Kind;
using Loc = TypeScript.Locale.Locale;
using Program = TypeScript.Compiler.Program;

namespace TypeScript.Ls;

public static partial class Ls
{
    internal static readonly int[] _importFixErrorCodes =
    [
        Cannot_find_name_0.Code(),
        Cannot_find_name_0_Did_you_mean_1.Code(),
        Cannot_find_name_0_Did_you_mean_the_instance_member_this_0.Code(),
        Cannot_find_name_0_Did_you_mean_the_static_member_1_0.Code(),
        Cannot_find_namespace_0.Code(),
        X_0_refers_to_a_UMD_global_but_the_current_file_is_a_module_Consider_adding_an_import_instead.Code(),
        X_0_only_refers_to_a_type_but_is_being_used_as_a_value_here.Code(),
        No_value_exists_in_scope_for_the_shorthand_property_0_Either_declare_one_or_provide_an_initializer.Code(),
        X_0_cannot_be_used_as_a_value_because_it_was_imported_using_import_type.Code(),
        Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_jQuery_Try_npm_i_save_dev_types_Slashjquery.Code(),
        Cannot_find_name_0_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_1_or_later.Code(),
        Cannot_find_name_0_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_include_dom.Code(),
        Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_a_test_runner_Try_npm_i_save_dev_types_Slashjest_or_npm_i_save_dev_types_Slashmocha_and_then_add_jest_or_mocha_to_the_types_field_in_your_tsconfig.Code(),
        Cannot_find_name_0_Did_you_mean_to_write_this_in_an_async_function.Code(),
        Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_jQuery_Try_npm_i_save_dev_types_Slashjquery_and_then_add_jquery_to_the_types_field_in_your_tsconfig.Code(),
        Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_a_test_runner_Try_npm_i_save_dev_types_Slashjest_or_npm_i_save_dev_types_Slashmocha.Code(),
        Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode.Code(),
        Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode_and_then_add_node_to_the_types_field_in_your_tsconfig.Code(),
        Cannot_find_namespace_0_Did_you_mean_1.Code(),
        Cannot_extend_an_interface_0_Did_you_mean_implements.Code(),
        This_JSX_tag_requires_0_to_be_in_scope_but_it_could_not_be_found.Code(),
    ];

    internal const string _importFixID = "fixMissingImport";

    /// <summary>Go: <c>var ImportFixProvider = &amp;CodeFixProvider{...}</c>（codeactions_importfixes.go:47-53）。</summary>
    public static readonly CodeFixProvider ImportFixProvider = new()
    {
        ErrorCodes = _importFixErrorCodes,
        GetCodeActions = GetImportCodeActions,
        FixIds = [_importFixID],
        GetAllCodeActions = GetAllImportCodeActions,
    };

    /// <summary>Go: <c>type fixInfo struct {...}</c>（codeactions_importfixes.go:55-60）。</summary>
    internal sealed class FixInfo
    {
        internal Fix? _fix;

        internal string _symbolName = "";

        internal string _errorIdentifierText = "";

        internal bool _isJsxNamespaceFix;
    }

    private static (CodeAction[]? Actions, Exception? Error) GetImportCodeActions(Context ctx, CodeFixContext fixContext)
    {
        var (info, err) = GetFixInfos(ctx, fixContext, fixContext.ErrorCode, fixContext.Span.Pos());
        if (err is not null)
        {
            return (null, err);
        }
        if (info.Length == 0)
        {
            return (null, null);
        }

        List<CodeAction>? actions = null;
        foreach (var fixInfo in info)
        {
            var (edits, description, ok) = fixInfo._fix!.Edits(
                ctx,
                fixContext.SourceFile!,
                fixContext.Program!.Options(),
                fixContext.LS!.FormatOptions(),
                fixContext.LS._converters!,
                fixContext.LS.UserPreferences());

            if (ok)
            {
                (actions ??= []).Add(new CodeAction
                {
                    Description = description,
                    Changes = edits,
                    FixID = _importFixID,
                    FixAllDescription = Add_all_missing_imports.Localize(Loc.FromContext(ctx)),
                });
            }
        }
        return (actions?.ToArray(), null);
    }

    private static (CombinedCodeActions? Actions, Exception? Error) GetAllImportCodeActions(Context ctx, CodeFixContext fixContext)
    {
        if (IsDynamicFileName(fixContext.SourceFile!.FileName()))
        {
            return (null, null);
        }

        var allDiagnostics = fixContext.Program!.GetSemanticDiagnostics(ctx, fixContext.SourceFile);

        List<AstDiag>? importDiags = null;
        foreach (var diag in allDiagnostics)
        {
            if (IsFixableDiagnostic(diag, _importFixErrorCodes))
            {
                (importDiags ??= []).Add(diag);
            }
        }

        if (importDiags is null || importDiags.Count == 0)
        {
            return (null, null);
        }

        var (ch, done) = fixContext.Program.GetTypeChecker(ctx);
        try
        {
            var (view, err) = fixContext.LS!.GetPreparedAutoImportView(fixContext.SourceFile, ch);
            if (err is not null)
            {
                return (null, err);
            }
            view ??= fixContext.LS.GetCurrentAutoImportView(fixContext.SourceFile, ch);

            var importAdder = AutoImport.NewImportAdder(
                ctx,
                fixContext.Program,
                ch,
                fixContext.SourceFile,
                view,
                fixContext.LS.FormatOptions(),
                fixContext.LS._converters!,
                fixContext.LS.UserPreferences());

            foreach (var diag in importDiags)
            {
                var diagError = AddImportFromDiagnostic(ctx, importAdder, diag, fixContext);
                if (diagError is not null)
                {
                    return (null, diagError);
                }
            }

            if (!importAdder.HasFixes())
            {
                return (null, null);
            }

            return (new CombinedCodeActions
            {
                Description = Add_all_missing_imports.Localize(Loc.FromContext(ctx)),
                Changes = importAdder.Edits(),
            }, null);
        }
        finally
        {
            done();
        }
    }

    /// <summary>addImportFromDiagnostic finds the best import fix for a diagnostic and adds it to the adder.</summary>
    private static Exception? AddImportFromDiagnostic(Context ctx, ImportAdder importAdder, AstDiag diag, CodeFixContext fixContext)
    {
        var diagFixContext = new CodeFixContext
        {
            SourceFile = fixContext.SourceFile,
            Span = new TextRange(diag.Pos(), diag.End()),
            ErrorCode = diag.Code(),
            Program = fixContext.Program,
            LS = fixContext.LS,
        };

        var (infos, err) = GetFixInfos(ctx, diagFixContext, diag.Code(), diag.Pos());
        if (err is not null)
        {
            return err;
        }
        if (infos.Length > 0)
        {
            importAdder.AddImportFix(infos[0]._fix!);
        }
        return null;
    }

    private static (FixInfo[] Infos, Exception? Error) GetFixInfos(Context ctx, CodeFixContext fixContext, int errorCode, int pos)
    {
        // Can't compute import fixes for dynamic/untitled files since they don't have real file paths
        if (IsDynamicFileName(fixContext.SourceFile!.FileName()))
        {
            return ([], null);
        }

        var symbolToken = GetTokenAtPosition(fixContext.SourceFile, pos);
        if (errorCode != X_0_refers_to_a_UMD_global_but_the_current_file_is_a_module_Consider_adding_an_import_instead.Code() && !IsIdentifier(symbolToken))
        {
            return ([], null);
        }

        var (ch, done) = fixContext.Program!.GetTypeChecker(ctx);
        try
        {
            View? view;
            List<FixInfo>? info = null;

            if (errorCode == X_0_refers_to_a_UMD_global_but_the_current_file_is_a_module_Consider_adding_an_import_instead.Code())
            {
                view = fixContext.LS!.GetCurrentAutoImportView(fixContext.SourceFile, ch);
                info = [.. GetFixesInfoForUMDImport(symbolToken, view, ch)];
            }
            else if (errorCode == X_0_cannot_be_used_as_a_value_because_it_was_imported_using_import_type.Code())
            {
                var compilerOptions = fixContext.Program.Options();
                var symbolNames = GetSymbolNamesToImport(fixContext.SourceFile, ch, symbolToken, compilerOptions);

                List<FixInfo>? allTypeOnlyFixes = null;
                foreach (var sn in symbolNames)
                {
                    if (!sn._isTypeOnly)
                    {
                        continue;
                    }
                    var fix = GetTypeOnlyPromotionFix(fixContext.SourceFile, symbolToken, sn._name, ch);
                    if (fix is not null)
                    {
                        (allTypeOnlyFixes ??= []).Add(new FixInfo { _fix = fix, _symbolName = sn._name, _errorIdentifierText = symbolToken.Text() });
                    }
                }

                // For JSX opening tags, there can be separate type-only errors for both the tag name
                // identifier and the JSX namespace identifier. When both produce valid fixes, we
                // disambiguate using the diagnostic message, which quotes the symbol name in single
                // quotes (e.g., "'React' cannot be used as a value..."). If filtering yields nothing
                // (e.g., due to localization), fall back to returning all candidates.
                var diagnosticMessage = "";
                if (fixContext.Diagnostic is not null)
                {
                    diagnosticMessage = fixContext.Diagnostic.Message.AsString();
                }
                if (allTypeOnlyFixes is { Count: > 1 } && diagnosticMessage != "")
                {
                    foreach (var fi in allTypeOnlyFixes)
                    {
                        if (diagnosticMessage.Contains("'" + fi._symbolName + "'", StringComparison.Ordinal))
                        {
                            (info ??= []).Add(fi);
                        }
                    }
                }
                if (info is null || info.Count == 0)
                {
                    info = allTypeOnlyFixes;
                }
                return (info?.ToArray() ?? [], null);
            }
            else
            {
                var (v, err) = fixContext.LS!.GetPreparedAutoImportView(fixContext.SourceFile, ch);
                if (err is not null)
                {
                    return ([], err);
                }
                view = v;
                if (view is not null)
                {
                    info = [.. GetFixesInfoForNonUMDImport(fixContext, symbolToken, view, ch)];
                }
            }

            // Sort fixes by preference
            view ??= fixContext.LS!.GetCurrentAutoImportView(fixContext.SourceFile, ch);
            return (SortFixInfo(info?.ToArray() ?? [], fixContext, view), null);
        }
        finally
        {
            done();
        }
    }

    private static FixInfo[] GetFixesInfoForUMDImport(Node token, View view, CheckerType ch)
    {
        var umdSymbol = GetUmdSymbol(token, ch);
        if (umdSymbol is null)
        {
            return [];
        }

        var export = AutoImport.SymbolToExport(umdSymbol, ch);
        var isValidTypeOnlyUseSite = IsValidTypeOnlyAliasUseSite(token);

        List<FixInfo>? result = null;
        foreach (var fix in view.GetFixes(export!, false, isValidTypeOnlyUseSite, null))
        {
            var errorIdentifierText = "";
            if (IsIdentifier(token))
            {
                errorIdentifierText = token.Text();
            }
            (result ??= []).Add(new FixInfo
            {
                _fix = fix,
                _symbolName = umdSymbol.Name,
                _errorIdentifierText = errorIdentifierText,
            });
        }
        return result?.ToArray() ?? [];
    }

    private static Symbol? GetUmdSymbol(Node token, CheckerType ch)
    {
        // try the identifier to see if it is the umd symbol
        Symbol? umdSymbol = null;
        if (IsIdentifier(token))
        {
            umdSymbol = ch.GetResolvedSymbol(token);
        }
        if (IsUMDExportSymbol(umdSymbol))
        {
            return umdSymbol;
        }

        // The error wasn't for the symbolAtLocation, it was for the JSX tag itself, which needs access to e.g. `React`.
        var parent = token.Parent;
        if (parent is not null &&
            ((IsJsxOpeningLikeElement(parent) && parent.TagName() == token) || IsJsxOpeningFragment(parent)))
        {
            Node? location;
            if (IsJsxOpeningLikeElement(parent))
            {
                location = token;
            }
            else
            {
                location = parent;
            }
            var jsxNamespace = ch.GetJsxNamespace(parent);
            var parentSymbol = ch.ResolveName(jsxNamespace, location, SymbolFlags.Value, false /* excludeGlobals */);
            if (IsUMDExportSymbol(parentSymbol))
            {
                return parentSymbol;
            }
        }
        return null;
    }

    private static bool IsUMDExportSymbol(Symbol? symbol)
    {
        return symbol is not null && symbol.Declarations is { Length: > 0 } &&
            symbol.Declarations[0] is not null &&
            IsNamespaceExportDeclaration(symbol.Declarations[0]);
    }

    private static FixInfo[] GetFixesInfoForNonUMDImport(CodeFixContext fixContext, Node symbolToken, View view, CheckerType ch)
    {
        var compilerOptions = fixContext.Program!.Options();

        var isValidTypeOnlyUseSite = IsValidTypeOnlyAliasUseSite(symbolToken);
        var symbolNames = GetSymbolNamesToImport(fixContext.SourceFile!, ch, symbolToken, compilerOptions);
        List<FixInfo>? allInfo = null;

        // Compute usage position for JSDoc import type fixes
        var (usagePosition, fidelity) = fixContext.LS!._converters!.ToLSPPosition(fixContext.SourceFile!, new TextPos(GetTokenPosOfNode(symbolToken, fixContext.SourceFile!, false)));
        if (!fidelity.IsExact())
        {
            return [];
        }

        foreach (var sn in symbolNames)
        {
            // Type-only imports are handled by the promotion code path, not the auto-import path.
            if (sn._isTypeOnly)
            {
                continue;
            }

            var symbolName = sn._name;
            // "default" is a keyword and not a legal identifier for the import
            if (symbolName == "default")
            {
                continue;
            }

            var isJSXTagName = symbolName == symbolToken.Text() && IsJsxTagName(symbolToken);
            var queryKind = QueryKind.ExactMatch;
            if (isJSXTagName)
            {
                queryKind = QueryKind.CaseInsensitiveMatch;
            }

            var exports = view.Search(symbolName, queryKind);
            foreach (var export in exports)
            {
                if (isJSXTagName && !(export.Name() == symbolName || export.IsRenameable()))
                {
                    continue;
                }

                var fixes = view.GetFixes(export, isJSXTagName, isValidTypeOnlyUseSite, usagePosition);
                foreach (var fix in fixes)
                {
                    (allInfo ??= []).Add(new FixInfo
                    {
                        _fix = fix,
                        _symbolName = symbolName,
                        _isJsxNamespaceFix = symbolName != symbolToken.Text(),
                    });
                }
            }
        }

        return allInfo?.ToArray() ?? [];
    }

    private static Fix? GetTypeOnlyPromotionFix(SourceFile sourceFile, Node symbolToken, string symbolName, CheckerType ch)
    {
        // Get the symbol at the token location
        var symbol = ch.ResolveName(symbolName, symbolToken, SymbolFlags.Value, true /* excludeGlobals */);
        if (symbol is null)
        {
            return null;
        }

        // Get the type-only alias declaration
        var typeOnlyAliasDeclaration = ch.GetTypeOnlyAliasDeclaration(symbol);
        if (typeOnlyAliasDeclaration is null || GetSourceFileOfNode(typeOnlyAliasDeclaration) != sourceFile)
        {
            return null;
        }

        return new Fix
        {
            AutoImportFix = new AutoImportFix
            {
                Kind = AutoImportFixKind.PromoteTypeOnly,
            },
            TypeOnlyAliasDeclaration = typeOnlyAliasDeclaration,
        };
    }

    /// <summary>Go: <c>type symbolNameInfo struct {...}</c>（codeactions_importfixes.go:381-384）。</summary>
    internal sealed class SymbolNameInfo
    {
        internal string _name = "";

        /// <summary>whether the symbol currently resolves to a type-only import</summary>
        internal bool _isTypeOnly;
    }

    private static SymbolNameInfo[] GetSymbolNamesToImport(SourceFile sourceFile, CheckerType ch, Node symbolToken, CompilerOptions compilerOptions)
    {
        var parent = symbolToken.Parent;
        if (parent is not null &&
            (IsJsxOpeningLikeElement(parent) || IsJsxClosingElement(parent)) &&
            parent.TagName() == symbolToken &&
            JsxModeNeedsExplicitImport(compilerOptions.Jsx))
        {
            var jsxNamespace = ch.GetJsxNamespace(sourceFile.AsNode());
            if (NeedsJsxNamespaceFix(jsxNamespace, symbolToken, ch))
            {
                List<SymbolNameInfo> result = [];
                if (!IsIntrinsicJsxName(symbolToken.Text()))
                {
                    var compSymbol = ch.ResolveName(symbolToken.Text(), symbolToken, SymbolFlags.Value, false /* excludeGlobals */);
                    if (compSymbol is null)
                    {
                        result.Add(new SymbolNameInfo { _name = symbolToken.Text() });
                    }
                    else if (ch.GetTypeOnlyAliasDeclaration(compSymbol) is not null)
                    {
                        result.Add(new SymbolNameInfo { _name = symbolToken.Text(), _isTypeOnly = true });
                    }
                }
                var nsIsTypeOnly = false;
                var nsSymbol = ch.ResolveName(jsxNamespace, symbolToken, SymbolFlags.Value, true /* excludeGlobals */);
                if (nsSymbol is not null)
                {
                    nsIsTypeOnly = ch.GetTypeOnlyAliasDeclaration(nsSymbol) is not null;
                }
                result.Add(new SymbolNameInfo { _name = jsxNamespace, _isTypeOnly = nsIsTypeOnly });
                return result.ToArray();
            }
        }
        var tokenIsTypeOnly = false;
        var sym = ch.ResolveName(symbolToken.Text(), symbolToken, SymbolFlags.Value, true /* excludeGlobals */);
        if (sym is not null)
        {
            tokenIsTypeOnly = ch.GetTypeOnlyAliasDeclaration(sym) is not null;
        }
        return [new SymbolNameInfo { _name = symbolToken.Text(), _isTypeOnly = tokenIsTypeOnly }];
    }

    private static bool NeedsJsxNamespaceFix(string jsxNamespace, Node symbolToken, CheckerType ch)
    {
        if (IsIntrinsicJsxName(symbolToken.Text()))
        {
            return true;
        }
        var namespaceSymbol = ch.ResolveName(jsxNamespace, symbolToken, SymbolFlags.Value, true /* excludeGlobals */);
        if (namespaceSymbol is null)
        {
            return true;
        }
        if ((namespaceSymbol.Declarations ?? []).Any(IsTypeOnlyImportOrExportDeclaration))
        {
            return (namespaceSymbol.Flags & SymbolFlags.Value) == 0;
        }
        return false;
    }

    private static bool JsxModeNeedsExplicitImport(JsxEmit jsx)
    {
        return jsx == JsxEmit.React || jsx == JsxEmit.ReactNative;
    }

    private static FixInfo[] SortFixInfo(FixInfo[] fixes, CodeFixContext fixContext, View view)
    {
        if (fixes.Length == 0)
        {
            return fixes;
        }

        // Create a copy to avoid modifying the original
        var sorted = (FixInfo[])fixes.Clone();

        // Sort by:
        // 1. JSX namespace fixes last
        // 2. Fix comparison using view.CompareFixes
        Array.Sort(sorted, (a, b) =>
        {
            // JSX namespace fixes should come last
            var cmp = CompareBooleans(a._isJsxNamespaceFix, b._isJsxNamespaceFix);
            if (cmp != 0)
            {
                return cmp;
            }
            return view.CompareFixesForSorting(a._fix!, b._fix!);
        });

        return sorted;
    }
}
