// ported from: internal/ls/rename.go:1-454
//
// ⚠ **`nodeIsEligibleForRename` 不在本文件**：G6 已把它就地落成 `Ls.NodeIsEligibleForRename`（`Ls/FindAllReferences.cs:2313`），
//   本文件只调用（CS0111 风险，勿重复声明）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级类型 → 本文件的 `internal` 类型（Go 导出的 `RenameInfo` 也只被 `ls`/`lsp` 同装配件使用 ⇒ `internal`）：
//    `RenameInfo`（Go 的结构体会被逐字段填写 ⇒ `sealed class` + public 字段）、`MappedRenameEdit`(mappedRenameEdit)、
//    `RenameEditKey`(renameEditKey)。Go 未导出的字段 → `_camelCase`（`_uri`/`_edit`）。
//
// 2) `renameEditKey` 是 Go 的 map 键 ⇒ C# 的 `readonly struct` 并**显式**实现值相等：`LsProto.Range` 是 class
//    （生成物），record struct 合成的 `Equals` 会退化成引用相等 —— 与 Go 的 Range 值语义不符，dedup 会永远不命中。
//    故 `Equals` 调 `Ls.RangeEquals`（G6 落地），`GetHashCode` 用两端点的 Line/Character。
//
// 3) `deduplicateRenameEdits` 的 `(map, bool)` → 本仓 §3.2 约定的 `bool TryDeduplicateRenameEdits(..., out …)`。
//    返回值用 `Dictionary<DocumentUri, TextEdit?[]?>`（= `WorkspaceEdit.Changes` 的类型）；Go 的 `append` 到
//    `map[uri]` 用中间的 `List<TextEdit>` 表达，最后 `ToArray`。
//
// 4) `Program.GetTypeChecker(ctx)` → `var (ch, done) = program.GetTypeChecker(ctx);` + `try/finally { done(); }`。
//    `l.UserPreferences()`/`l.GetProgram()`/`l.UseCaseSensitiveFileNames()`/`l._converters` 照 C# 侧的既有成员名
//    （`converters` 字段 → `_converters`，见 `Ls/LanguageService.cs:57`）。
//
// 5) `strings.Compare` → `string.CompareOrdinal`；`slices.Contains(s, x)` 是**指针**成员判定 ⇒ C# 用
//    `ReferenceEquals` 线性扫描（`Node` 未重写 `Equals`，但显式写 ReferenceEquals 才是 Go 的 `==` 语义）。
//
// 6) `strings.LastIndex(text, "/") + 1` / `text[i:]` / `len(text) - i`：Go 这里是**字节**偏移，C# 是 UTF-16 偏移。
//    本仓口径是"位置一律 UTF-16"（见任务书），且 `start` 要和 `AstNav.GetStartOfNode` 的 UTF-16 位置相加 ⇒
//    C# 侧统一用 UTF-16 偏移（对纯 ASCII 的模块说明符两者逐位相同；对含非 ASCII 的说明符，C# 版本自洽而 Go 版本混用）。
//
// 7) `ast.GetSourceFileOfNode(declaration).FileName()` 等在 Go 里绑定到 `ast` 包的同名函数；本文件 `using static
//    TypeScript.Ast.Ast;` ⇒ 裸名。`isInsideNodeModules` 由 G4 落在 `Ls.IsInsideNodeModules`（`Ls/Symbols.cs:510`）。
//
// 8) `module.ParseNodeModuleFromPath`（`TypeScript.Module` 既是命名空间又是静态类 ⇒ 只能 `using static`）→ 裸名。
//
// 9) `tspath.GetAnyExtensionFromPath(path, nil, ignoreCase)` 的 nil 扩展名列表 → C# 传 `[]`
//    （先例 `OutputPaths/OutputPaths.cs:10,183`，C# 该形参是非空数组）。
//
// 10) `strings.CutSuffix(candidate, "/index")` → `EndsWith` + 切片（Go 的 `ok` 就是 `EndsWith` 的结果）。
//
// 11) 深递归：本文件只沿声明/父链迭代，不递归 ⇒ 无需大栈线程（`HandleCrossProject` 自带编排）。
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Locale;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Locale.Locale;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Module.Module;
using static TypeScript.TSPath.TSPath;
using DiagMessage = TypeScript.Diagnostics.Message;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.Ls;

/// <summary>Go: <c>type RenameInfo struct</c>（rename.go:22-30）：`textDocument/prepareRename` 的结果。</summary>
public sealed class RenameInfo
{
    public bool CanRename;
    public string LocalizedErrorMessage = "";
    public string DisplayName = "";
    public Range TriggerSpan = new();
    public string FileToRename = "";
    public string NewFileName = "";
}

/// <summary>Go: <c>type mappedRenameEdit struct</c>（rename.go:32-35）。</summary>
internal sealed class MappedRenameEdit
{
    internal DocumentUri _uri;
    internal TextEdit _edit = null!;
}

/// <summary>
/// Go: <c>type renameEditKey struct { uri lsproto.DocumentUri; textRange lsproto.Range }</c>（rename.go:37-40）：
/// Go 的 map 键 ⇒ C# 值相等结构体（见文件头第 2 条）。
/// </summary>
internal readonly struct RenameEditKey : IEquatable<RenameEditKey>
{
    internal readonly DocumentUri _uri;
    internal readonly Range _textRange;

    internal RenameEditKey(DocumentUri uri, Range textRange)
    {
        _uri = uri;
        _textRange = textRange;
    }

    public bool Equals(RenameEditKey other) => _uri.Equals(other._uri) && Ls.RangeEquals(_textRange, other._textRange);

    public override bool Equals(object? obj) => obj is RenameEditKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _uri,
        _textRange.Start.Line,
        _textRange.Start.Character,
        _textRange.End.Line,
        _textRange.End.Character);
}

public static partial class Ls
{
    /// <summary>
    /// Go: <c>func deduplicateRenameEdits(mappedEdits []mappedRenameEdit) (map[lsproto.DocumentUri][]*lsproto.TextEdit, bool)</c>
    /// （rename.go:42-63，见文件头第 3 条）。
    /// </summary>
    internal static bool TryDeduplicateRenameEdits(
        List<MappedRenameEdit> mappedEdits,
        out Dictionary<DocumentUri, TextEdit?[]?> changes)
    {
        var editTexts = new Dictionary<RenameEditKey, string>();
        var uniqueEdits = new List<MappedRenameEdit>(mappedEdits.Count);
        foreach (var mappedEdit in mappedEdits)
        {
            var key = new RenameEditKey(mappedEdit._uri, mappedEdit._edit.Range);
            if (editTexts.TryGetValue(key, out var existingText))
            {
                if (existingText != mappedEdit._edit.NewText)
                {
                    changes = [];
                    return false;
                }

                continue;
            }

            editTexts[key] = mappedEdit._edit.NewText;
            uniqueEdits.Add(mappedEdit);
        }

        var byUri = new Dictionary<DocumentUri, List<TextEdit>>();
        foreach (var mappedEdit in uniqueEdits)
        {
            if (!byUri.TryGetValue(mappedEdit._uri, out var list))
            {
                byUri[mappedEdit._uri] = list = [];
            }

            list.Add(mappedEdit._edit);
        }

        changes = new Dictionary<DocumentUri, TextEdit?[]?>();
        foreach (var (uri, edits) in byUri)
        {
            changes[uri] = edits.ToArray();
        }

        return true;
    }

    /// <summary>Go: <c>func ClientSupportsWillRenameFiles(ctx context.Context) bool</c>（rename.go:239-241）。</summary>
    public static bool ClientSupportsWillRenameFiles(Context ctx)
    {
        return TypeScript.LsProto.LsProto.GetClientCapabilities().Workspace.FileOperations.WillRename;
    }

    /// <summary>Go: <c>func ClientSupportsDocumentChanges(ctx context.Context) bool</c>（rename.go:243-245）。</summary>
    public static bool ClientSupportsDocumentChanges(Context ctx)
    {
        return TypeScript.LsProto.LsProto.GetClientCapabilities().Workspace.WorkspaceEdit.DocumentChanges;
    }

    /// <summary>Go: <c>func ClientSupportsRenameResourceOperations(ctx context.Context) bool</c>（rename.go:247-249）。</summary>
    public static bool ClientSupportsRenameResourceOperations(Context ctx)
    {
        return (TypeScript.LsProto.LsProto.GetClientCapabilities().Workspace.WorkspaceEdit.ResourceOperations ?? [])
            .Contains(ResourceOperationKind.Rename);
    }

    /// <summary>Go: <c>func getQuoteFromPreference(quotePreference lsutil.QuotePreference) string</c>（rename.go:411-416）。</summary>
    internal static string GetQuoteFromPreference(QuotePreference quotePreference)
    {
        if (quotePreference == QuotePreferenceSingle)
        {
            return "'";
        }

        return "\"";
    }

    /// <summary>Go: <c>func getRenameInfoError(ctx context.Context, message *diagnostics.Message) RenameInfo</c>（rename.go:418-423）。</summary>
    internal static RenameInfo GetRenameInfoError(Context ctx, DiagMessage message)
    {
        return new RenameInfo
        {
            CanRename = false,
            LocalizedErrorMessage = message.Localize(FromContext(ctx)),
        };
    }

    /// <summary>Go: <c>func getRenameInfoSuccess(node *ast.Node, sourceFile *ast.SourceFile, displayName string, converters *lsconv.Converters) RenameInfo</c>（rename.go:425-448）。</summary>
    internal static RenameInfo GetRenameInfoSuccess(Node node, SourceFile sourceFile, string displayName, LsConv.Converters converters)
    {
        var start = GetStartOfNode(node, sourceFile, false /*includeJSDoc*/);
        var end = node.End();
        if (IsStringLiteralLike(node))
        {
            // Exclude the quotes
            start++;
            end--;
        }

        var (triggerSpan, fidelity) = converters.ToLSPRange(sourceFile, new TextRange(start, end));
        if (!fidelity.IsExact())
        {
            return new RenameInfo { CanRename = false };
        }

        return new RenameInfo
        {
            CanRename = true,
            DisplayName = displayName,
            TriggerSpan = triggerSpan,
        };
    }

    /// <summary>Go: <c>func isDefinedInLibraryFile(program *compiler.Program, declaration *ast.Node) bool</c>（rename.go:196-199）。</summary>
    internal static bool IsDefinedInLibraryFile(Program program, Node declaration)
    {
        var declSourceFile = GetSourceFileOfNode(declaration);
        return program.IsSourceFileDefaultLibrary(declSourceFile!.Path()) && IsDeclarationFileName(declSourceFile.FileName());
    }

    /// <summary>Go: <c>func wouldRenameInOtherNodeModules(originalFile *ast.SourceFile, symbol *ast.Symbol, ch *checker.Checker, preferences lsutil.UserPreferences) *diagnostics.Message</c>（rename.go:201-237）。</summary>
    internal static DiagMessage? WouldRenameInOtherNodeModules(SourceFile originalFile, Symbol symbol, TypeScript.Checker.Checker ch, UserPreferences preferences)
    {
        var sym = symbol;
        if (!preferences.UseAliasesForRename.IsTrueOrUnknown() && (sym.Flags & SymbolFlags.Alias) != 0)
        {
            var importSpecifier = Find(sym.Declarations, IsImportSpecifier);
            if (importSpecifier is not null && importSpecifier.AsImportSpecifier()!.PropertyName is null)
            {
                sym = ch.GetAliasedSymbol(sym);
            }
        }

        var declarations = sym.Declarations;
        if (declarations is null || declarations.Length == 0)
        {
            return null;
        }

        var originalPackage = ParseNodeModuleFromPath(originalFile.FileName(), false /*isFolder*/);
        if (originalPackage == "")
        {
            // Original source file is not in node_modules.
            foreach (var declaration in declarations)
            {
                if (Ls.IsInsideNodeModules(GetSourceFileOfNode(declaration)!.FileName()))
                {
                    return You_cannot_rename_elements_that_are_defined_in_a_node_modules_folder;
                }
            }

            return null;
        }

        // Original source file is in node_modules.
        foreach (var declaration in declarations)
        {
            var declPackage = ParseNodeModuleFromPath(GetSourceFileOfNode(declaration)!.FileName(), false /*isFolder*/);
            if (declPackage != "" && declPackage != originalPackage)
            {
                return You_cannot_rename_elements_that_are_defined_in_another_node_modules_folder;
            }
        }

        return null;
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideRename(ctx context.Context, params *lsproto.RenameParams, orchestrator CrossProjectOrchestrator) (lsproto.WorkspaceEditOrNull, error)</c>（rename.go:65-77）。</summary>
    public (WorkspaceEditOrNull Response, Exception? Error) ProvideRename(Context ctx, RenameParams @params, CrossProjectOrchestrator orchestrator)
    {
        return HandleCrossProject(
            ctx,
            @params,
            orchestrator,
            static (ls, context, p, data, options) => ls.SymbolAndEntriesToRename(context, p, data, options),
            Ls.CombineRenameResponse,
            isRename: true,
            implementations: false,
            new SymbolEntryTransformOptions(),
            defaultProjectData: null,
            provideSymbolsAndEntries: static (ls, context, uri, pos, rename, impl) => ls.ProvideSymbolsAndEntries(context, uri, pos, rename, impl));
    }

    /// <summary>Go: <c>func (l *LanguageService) GetRenameInfo(ctx context.Context, newName string, documentURI lsproto.DocumentUri, position lsproto.Position) RenameInfo</c>（rename.go:79-95）。</summary>
    public RenameInfo GetRenameInfo(Context ctx, string newName, DocumentUri documentURI, Position position)
    {
        var (program, sourceFile) = GetProgramAndFile(documentURI);
        var positions = _converters!.FromLSPPositionForSourceFile(sourceFile, position, Feature.Rename);
        foreach (var mapped in positions)
        {
            if (!mapped.Fidelity.IsExact())
            {
                continue;
            }

            sourceFile = mapped.Script;
            var node = GetTouchingPropertyName(sourceFile, mapped.Position);
            node = Ls.GetAdjustedLocation(node, true /*forRename*/, sourceFile);
            if (Ls.NodeIsEligibleForRename(node))
            {
                var (renameInfo, ok) = GetRenameInfoForNode(ctx, newName, node!, sourceFile, program);
                if (ok)
                {
                    return renameInfo;
                }
            }
        }

        return Ls.GetRenameInfoError(ctx, You_cannot_rename_this_element);
    }

    /// <summary>Go: <c>func (l *LanguageService) symbolAndEntriesToRename(ctx context.Context, params *lsproto.RenameParams, data SymbolAndEntriesData, options symbolEntryTransformOptions) (lsproto.WorkspaceEditOrNull, error)</c>（rename.go:97-149）。</summary>
    internal (WorkspaceEditOrNull Response, Exception? Error) SymbolAndEntriesToRename(Context ctx, RenameParams @params, SymbolAndEntriesData data, SymbolEntryTransformOptions options)
    {
        if (!Ls.NodeIsEligibleForRename(data.OriginalNode))
        {
            return (new WorkspaceEditOrNull(), null);
        }

        var program = GetProgram()!;

        // Defense-in-depth: validate rename eligibility even if the client skipped prepareRename.
        // Use getRenameInfoForNode directly with the already-resolved node to avoid
        // re-resolving the position and polluting state baselines.
        var sourceFile = GetSourceFileOfNode(data.OriginalNode!);
        var (info, ok) = GetRenameInfoForNode(ctx, @params.NewName, data.OriginalNode!, sourceFile!, program);
        if (!ok || !info.CanRename)
        {
            return (new WorkspaceEditOrNull(), null);
        }

        var entries = FlatMap(data.SymbolsAndEntries, static s => s._references ?? []);
        var mappedEdits = new List<MappedRenameEdit>();
        var (ch, done) = program.GetTypeChecker(ctx);
        try
        {
            var quotePreference = GetQuotePreference(sourceFile!, UserPreferences());
            var useAliasesForRename = UserPreferences().UseAliasesForRename.IsTrueOrUnknown();

            foreach (var entry in entries)
            {
                var uri = GetFileNameOfEntry(entry);
                if (UserPreferences().AllowRenameOfImportPath != Tristate.TSTrue && entry._node is not null &&
                    IsStringLiteralLike(entry._node) && TryGetImportFromModuleSpecifier(entry._node) is not null)
                {
                    continue;
                }

                var (rng, ok2) = RenameEditRange(entry);
                if (!ok2)
                {
                    // The occurrence lies outside a verbatim span of a content-mapped file, so it cannot be
                    // written back to the original text. Skip it and keep renaming the remaining occurrences.
                    continue;
                }

                var textEdit = new TextEdit
                {
                    Range = rng,
                    NewText = GetTextForRename(data.OriginalNode!, entry, @params.NewName, ch, quotePreference, useAliasesForRename),
                };
                mappedEdits.Add(new MappedRenameEdit { _uri = uri, _edit = textEdit });
            }
        }
        finally
        {
            done();
        }

        if (!Ls.TryDeduplicateRenameEdits(mappedEdits, out var changes))
        {
            return (new WorkspaceEditOrNull(), null);
        }

        return (new WorkspaceEditOrNull
        {
            WorkspaceEdit = new WorkspaceEdit
            {
                Changes = changes,
            },
        }, null);
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) renameEditRange(entry *ReferenceEntry) (lsproto.Range, bool)</c>（rename.go:151-166）。
    /// renameEditRange returns the LSP range at which a rename occurrence should be edited. For occurrences in
    /// content-mapped files it maps the transformed range strictly, returning ok=false when the occurrence is
    /// not fully within a single verbatim span, so the caller can skip an edit that cannot be applied to the
    /// original text.
    /// </summary>
    private (Range Range, bool Ok) RenameEditRange(ReferenceEntry entry)
    {
        ResolveEntry(entry);
        if (entry._node is null)
        {
            var (location, fidelity) = SourceFileRangeToLSPLocation(entry._sourceFile!, entry._textRange!.Value);
            return (location.Range, fidelity.IsExact());
        }

        var sourceFile = GetSourceFileOfNode(entry._node);
        if (sourceFile is null || sourceFile.SpanMap() is null)
        {
            return (GetRangeOfEntry(entry), true);
        }

        var (lspRange, fidelity2) = _converters!.ToLSPRange(sourceFile, entry._textRange!.Value);
        return (lspRange, fidelity2.IsExact());
    }

    /// <summary>Go: <c>func (l *LanguageService) getRenameInfoForNode(ctx context.Context, newName string, node *ast.Node, sourceFile *ast.SourceFile, program *compiler.Program) (RenameInfo, bool)</c>（rename.go:168-216）。</summary>
    private (RenameInfo Info, bool Ok) GetRenameInfoForNode(Context ctx, string newName, Node node, SourceFile sourceFile, Program program)
    {
        var (ch, done) = program.GetTypeChecker(ctx);
        try
        {
            var symbol = ch.GetSymbolAtLocation(node);
            if (symbol is null)
            {
                if (IsStringLiteralLike(node))
                {
                    // Allow renaming of string literal types with contextual string literal types
                    var typ = Ls.GetContextualTypeFromParentOrAncestorTypeNode(node, ch);
                    if (typ is not null && (typ.IsStringLiteral() ||
                        (typ.IsUnion() && Every(typ.Types(), static t => t.IsStringLiteral()))))
                    {
                        return (Ls.GetRenameInfoSuccess(node, sourceFile, node.Text(), _converters!), true);
                    }
                }
                else if (IsLabelName(node))
                {
                    var name = node.Text();
                    return (Ls.GetRenameInfoSuccess(node, sourceFile, name, _converters!), true);
                }

                return (new RenameInfo(), false);
            }

            // Only allow a symbol to be renamed if it actually has at least one declaration.
            if (symbol.Declarations is null || symbol.Declarations.Length == 0)
            {
                return (new RenameInfo(), false);
            }

            if (RenameBlockedReason(sourceFile, node, symbol, ch, program) is { } msg)
            {
                return (Ls.GetRenameInfoError(ctx, msg), true);
            }

            if (IsStringLiteralLike(node) && TryGetImportFromModuleSpecifier(node) is not null)
            {
                if (UserPreferences().AllowRenameOfImportPath.IsTrue())
                {
                    return GetRenameInfoForModule(ctx, newName, node, sourceFile, symbol);
                }

                return (new RenameInfo(), false);
            }

            return (Ls.GetRenameInfoSuccess(node, sourceFile, ch.SymbolToString(symbol), _converters!), true);
        }
        finally
        {
            done();
        }
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) renameBlockedReason(sourceFile *ast.SourceFile, node *ast.Node, symbol *ast.Symbol, ch *checker.Checker, program *compiler.Program) *diagnostics.Message</c>
    /// （rename.go:219-237）。renameBlockedReason returns a non-nil diagnostic message if the rename should be blocked
    /// because the symbol is a library definition, a default keyword, or would cross node_modules boundaries.
    /// </summary>
    private DiagMessage? RenameBlockedReason(SourceFile sourceFile, Node node, Symbol symbol, TypeScript.Checker.Checker ch, Program program)
    {
        foreach (var declaration in symbol.Declarations ?? [])
        {
            if (Ls.IsDefinedInLibraryFile(program, declaration))
            {
                return You_cannot_rename_elements_that_are_defined_in_the_standard_TypeScript_library;
            }
        }

        // Cannot rename `default` as in `import { default as foo } from "./someModule"`
        if (IsIdentifier(node) && node.Text() == "default" && symbol.Parent is not null && (symbol.Parent.Flags & SymbolFlags.Module) != 0)
        {
            return You_cannot_rename_this_element;
        }

        if (Ls.WouldRenameInOtherNodeModules(sourceFile, symbol, ch, UserPreferences()) is { } msg)
        {
            return msg;
        }

        return null;
    }

    /// <summary>Go: <c>func (l *LanguageService) getRenameInfoForModule(ctx context.Context, newName string, specifier *ast.StringLiteralLike, sourceFile *ast.SourceFile, moduleSymbol *ast.Symbol) (RenameInfo, bool)</c>（rename.go:251-296）。</summary>
    private (RenameInfo Info, bool Ok) GetRenameInfoForModule(Context ctx, string newName, Node specifier, SourceFile sourceFile, Symbol moduleSymbol)
    {
        if (!IsExternalModuleNameRelative(specifier.Text()))
        {
            return (Ls.GetRenameInfoError(ctx, You_cannot_rename_a_module_via_a_global_import), true);
        }

        if (!Ls.ClientSupportsDocumentChanges(ctx) || !Ls.ClientSupportsRenameResourceOperations(ctx))
        {
            return (Ls.GetRenameInfoError(ctx, File_rename_is_not_supported_by_the_editor), true);
        }

        var moduleSourceFile = Find(moduleSymbol.Declarations, IsSourceFile);
        if (moduleSourceFile is null)
        {
            return (new RenameInfo(), false);
        }

        var fileName = moduleSourceFile.AsSourceFile()!.FileName();
        var withoutIndex = "";
        if (!specifier.Text().EndsWith("/index", StringComparison.Ordinal) && !specifier.Text().EndsWith("/index.js", StringComparison.Ordinal))
        {
            var candidate = RemoveFileExtension(fileName);
            if (candidate.EndsWith("/index", StringComparison.Ordinal))
            {
                withoutIndex = candidate[..^"/index".Length];
            }
        }

        var displayName = fileName;
        if (withoutIndex != "")
        {
            displayName = withoutIndex;
        }

        var newFileName = GetNewFileNameForModuleRename(displayName, specifier.Text(), newName);

        // Span should only be the last component of the path. + 1 to account for the quote character.
        var indexAfterLastSlash = specifier.Text().LastIndexOf('/') + 1;
        var start = GetStartOfNode(specifier, sourceFile, false /*includeJSDoc*/) + 1 + indexAfterLastSlash;
        var length = specifier.Text().Length - indexAfterLastSlash;

        var (triggerSpan, fidelity) = _converters!.ToLSPRange(sourceFile, new TextRange(start, start + length));
        if (!fidelity.IsExact())
        {
            return (new RenameInfo(), false);
        }

        return (new RenameInfo
        {
            CanRename = true,
            DisplayName = specifier.Text()[indexAfterLastSlash..],
            TriggerSpan = triggerSpan,
            FileToRename = displayName,
            NewFileName = newFileName,
        }, true);
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) getNewFileNameForModuleRename(oldPath, specifierText, newName string) string</c>
    /// （rename.go:298-317）。Adjust the new name based on the old path that an import specifier resolves to.
    /// For example, if specifier "a.js" resolves to file a.ts, renaming "a.js" -> "b.js" should mean file rename a.ts -> b.ts.
    /// </summary>
    private string GetNewFileNameForModuleRename(string oldPath, string specifierText, string newName)
    {
        var newPath = CombinePaths(GetDirectoryPath(oldPath), newName);
        var ignoreCase = !UseCaseSensitiveFileNames();
        string oldExt;
        if (IsDeclarationFileName(oldPath))
        {
            oldExt = GetDeclarationFileExtension(oldPath);
        }
        else
        {
            oldExt = GetAnyExtensionFromPath(oldPath, [] /*extensions*/, ignoreCase);
        }

        if (!HasExtension(newPath))
        {
            newPath = newPath + oldExt;
        }
        else if (GetAnyExtensionFromPath(newPath, [] /*extensions*/, ignoreCase) == GetAnyExtensionFromPath(specifierText, [] /*extensions*/, ignoreCase))
        {
            newPath = ChangeAnyExtension(newPath, oldExt, [] /*extensions*/, ignoreCase);
        }

        return newPath;
    }

    /// <summary>Go: <c>func (l *LanguageService) getTextForRename(originalNode *ast.Node, entry *ReferenceEntry, newText string, ch *checker.Checker, quotePreference lsutil.QuotePreference, useAliasesForRename bool) string</c>（rename.go:319-409）。</summary>
    private string GetTextForRename(Node originalNode, ReferenceEntry entry, string newText, TypeScript.Checker.Checker ch, QuotePreference quotePreference, bool useAliasesForRename)
    {
        if (useAliasesForRename && entry._kind != EntryKind.Range && (IsIdentifier(originalNode) || IsStringLiteralLike(originalNode)))
        {
            var node = GetReparsedNodeForNode(entry._node);
            var kind = entry._kind;
            var parent = node!.Parent!;
            var name = originalNode.Text();
            var isShorthandAssignment = IsShorthandPropertyAssignment(parent);
            if (isShorthandAssignment || (Ls.IsObjectBindingElementWithoutPropertyName(parent) && parent.Name() == node && parent.AsBindingElement()!.DotDotDotToken is null))
            {
                if (kind == EntryKind.SearchedLocalFoundProperty)
                {
                    return name + ": " + newText;
                }

                if (kind == EntryKind.SearchedPropertyFoundLocal)
                {
                    return newText + ": " + name;
                }

                // In `const o = { x }; o.x`, symbolAtLocation at `x` in `{ x }` is the property symbol.
                // For a binding element `const { x } = o;`, symbolAtLocation at `x` is the property symbol.
                if (isShorthandAssignment)
                {
                    var grandParent = parent.Parent;
                    if (grandParent is { } gp && IsObjectLiteralExpression(gp) && gp.Parent is { } gpParent && IsBinaryExpression(gpParent) && IsModuleExportsAccessExpression(gpParent.AsBinaryExpression()!.Left!))
                    {
                        return name + ": " + newText;
                    }

                    return newText + ": " + name;
                }

                return name + ": " + newText;
            }
            else if (IsImportSpecifier(parent) && parent.AsImportSpecifier()!.PropertyName is null)
            {
                // If the original symbol was using this alias, just rename the alias.
                Symbol? originalSymbol;
                if (originalNode.Parent is { } exportParent && IsExportSpecifier(exportParent))
                {
                    originalSymbol = ch.GetExportSpecifierLocalTargetSymbol(exportParent);
                }
                else
                {
                    originalSymbol = ch.GetSymbolAtLocation(originalNode);
                }

                if (originalSymbol is not null && (originalSymbol.Declarations ?? []).Any(d => ReferenceEquals(d, parent)))
                {
                    return name + " as " + newText;
                }

                return newText;
            }
            else if (IsExportSpecifier(parent) && parent.AsExportSpecifier()!.PropertyName is null)
            {
                // If the symbol for the node is same as declared node symbol use prefix text
                if (originalNode == entry._node || ch.GetSymbolAtLocation(originalNode) == ch.GetSymbolAtLocation(entry._node!))
                {
                    return name + " as " + newText;
                }

                return newText + " as " + name;
            }
        }

        // If the node is a numerical indexing literal, then add quotes around the property access.
        if (entry._kind != EntryKind.Range && entry._node is { } numericNode && IsNumericLiteral(numericNode) && IsAccessExpression(numericNode.Parent!))
        {
            var quote = Ls.GetQuoteFromPreference(quotePreference);
            return quote + newText + quote;
        }

        return newText;
    }
}
