// ported from: internal/ls/codeactions.go:1-402
//
// 本文件是 **code-action 框架**：`CodeFixProvider`/`CodeFixContext`/`CodeAction`/`CombinedCodeActions` 四个类型、
// 注册表 `codeFixProviders`、以及 `ProvideCodeActions` / `getFixAllQuickFixes` / `createFixAllAction` /
// `getOrganizeImportsActionsForKind` / `createOrganizeImportsAction` / `convertToLSPCodeAction` 等框架函数。
// **具体 provider 归 G10**（`ImportFixProvider` / `IsolatedDeclarationsFixProvider` /
// `FixClassIncorrectlyImplementsInterfaceProvider`），见本文件 `Ls.CodeFixProviders` 处的说明。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级类型 → 本文件内的 `internal` 类型（Go 的"导出"在 `ls` 包里只表示"同包跨文件可见" ⇒ C# 的 `internal`
//    就是对应可见性，先例 `Ls/FindAllReferences.cs` 的 `SymbolAndEntries`/`DefinitionKind`）。Go 的**导出字段**
//    保留 PascalCase（`ErrorCodes`/`GetCodeActions`/`SourceFile`/`Span`/`Description`/`Changes`/`FixID`…）；
//    四个类型都是 Go 的可变结构体且会被闭包/实现方填写 ⇒ C# 用 `sealed class` + public 字段。
//
// 2) Go 的 `func(ctx, *CodeFixContext) ([]*CodeAction, error)` 函数**字段** → C# 的委托字段，`error` 按本仓既有约定
//    落成元组的第二分量：`Func<Context, CodeFixContext, (CodeAction[]? Actions, Exception? Error)>?`（先例
//    `Ls/CrossProject.cs` 的 `HandleCrossProject` 委托参数）。Go 的 `nil` 函数值 ⇒ C# 的 null 委托
//    （`codeFixProviders` 里的三个 provider 都设了 `GetCodeActions`，与 Go 一致；Go 若为 nil 会 panic，
//    C# 对应 NRE）。
//
// 3) `codeFixProviders`（包级未导出 var）→ 静态类 `Ls` 上的**惰性属性** `CodeFixProviders`。理由：Go 的包级 var
//    初始化顺序由编译器按文件依赖排定，而 C# 的静态字段初始化器**跨 partial 文件**的顺序由编译单元顺序决定
//    （不可靠）——provider 由 G10 的另外几个文件声明，若写成静态字段初始化器可能拿到 null。惰性属性保证首次访问时
//    三个 provider 字段都已初始化。语义等价（Go 里该切片也只被读取）。
//
// 4) `slices.BinarySearchFunc`（seen 的去重插入）→ 本文件的私有 `BinarySearchFunc<T>` + `InsertSorted`。原因：
//    `Core.InsertSorted` 只返回插入后的切片、丢掉了 Go 的 `found`；`Core` 里唯一的二分查找
//    `TryBinarySearchUniqueFunc` 的比较器签名是 `Func<int, E, int>`（要求"至多一个元素匹配"），与 Go 这里
//    "可能已有相同元素、要判 found"的用法不同。故就地复刻 `slices.BinarySearchFunc`（见该方法的注释）。
//
// 5) `strings.Compare` → `string.CompareOrdinal`；`cmp.Compare(len(a), len(b))` → `int.CompareTo`（都是 -1/0/1）。
//
// 6) `getAllDiagnostics` 已由 G1 落成 `Ls.GetAllDiagnostics`（`Ls/Diagnostics.cs`）；`isFixableDiagnostic` /
//    `containsErrorCode` / `hasMultipleFixableDiagnostics` / `codeFixProviderMatchesLSPDiagnostic` /
//    `getOrganizeImportsActionTitle` / `getOrganizeImportsActionsForKind` / `convertToLSPCodeAction` 是**本文件**
//    的包级未导出函数 ⇒ `Ls` 静态类的 `internal static` 成员（PascalCase、不加 `_`）。
//
// 7) 两个 `Diagnostic` / `CodeAction` 同名类型（`TypeScript.Ast.Diagnostic` vs `TypeScript.LsProto.Diagnostic`；
//    本文件的 `CodeAction` vs `TypeScript.LsProto.CodeAction`）⇒ 沿用本仓别名约定（`Ls/Diagnostics.cs:51-52`）：
//    `AstDiag` / `LspDiag` / `LspCodeAction`（本文件的 `CodeAction` 保留 Go 的裸名，因为它是本切片的公开契约，
//    G10 按裸名引用）。
//
// 8) `params.Context.Only`（Go 的 `*[]lsproto.CodeActionKind`，可 nil）→ `CodeActionKind[]?`，遍历前判空
//    （Go 遍历 nil 切片不迭代）；`params.Context.Diagnostics`（`[]*lsproto.Diagnostic`，元素可 nil）→
//    `LspDiag?[]?`，遍历时判空元素。
//
// 9) Go 的 `switch kind { case … }` 用常量；C# 的 `CodeActionKind` 常量是 `static readonly`（不是 const）⇒
//    改成 if/else 链（`getOrganizeImportsActionTitle`）。`slices.Contains(result, requestedKind)` →
//    `List.Contains`。
//
// 10) Go 的 `&map[lsproto.DocumentUri][]*lsproto.TextEdit{}`（空但非 nil 的 map）→
//     `new Dictionary<DocumentUri, TextEdit?[]?>()`。
//
// 11) 深递归：本文件不递归（provider 内部才递归）⇒ 无需大栈线程；调用方（lsp）自带。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Locale;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Locale.Locale;
using static TypeScript.LsConv.LsConv;
using AstDiag = TypeScript.Ast.Diagnostic;
using LspCodeAction = TypeScript.LsProto.CodeAction;
using LspDiag = TypeScript.LsProto.Diagnostic;

namespace TypeScript.Ls;

/// <summary>Go: <c>type CodeFixProvider struct</c>（codeactions.go:20-25）。**具体 provider 由 G10 落地**。</summary>
public sealed class CodeFixProvider
{
    /// <summary>Go: <c>ErrorCodes []int32</c>。</summary>
    public int[]? ErrorCodes;

    /// <summary>Go: <c>GetCodeActions func(ctx context.Context, fixContext *CodeFixContext) ([]*CodeAction, error)</c>。</summary>
    public Func<Context, CodeFixContext, (CodeAction[]? Actions, Exception? Error)>? GetCodeActions;

    /// <summary>Go: <c>FixIds []string</c>。</summary>
    public string[]? FixIds;

    /// <summary>Go: <c>GetAllCodeActions func(ctx context.Context, fixContext *CodeFixContext) (*CombinedCodeActions, error)</c>。</summary>
    public Func<Context, CodeFixContext, (CombinedCodeActions? Actions, Exception? Error)>? GetAllCodeActions;
}

/// <summary>Go: <c>type CodeFixContext struct</c>（codeactions.go:28-36）。</summary>
public sealed class CodeFixContext
{
    /// <summary>Go: <c>SourceFile *ast.SourceFile</c>。</summary>
    public SourceFile? SourceFile;

    /// <summary>Go: <c>Span core.TextRange</c>。</summary>
    public TextRange Span;

    /// <summary>Go: <c>ErrorCode int32</c>。</summary>
    public int ErrorCode;

    /// <summary>Go: <c>Program *compiler.Program</c>。</summary>
    public Program? Program;

    /// <summary>Go: <c>LS *LanguageService</c>。</summary>
    public LanguageService? LS;

    /// <summary>Go: <c>Diagnostic *lsproto.Diagnostic</c>。</summary>
    public LspDiag? Diagnostic;

    /// <summary>Go: <c>Params *lsproto.CodeActionParams</c>。</summary>
    public CodeActionParams? Params;
}

/// <summary>Go: <c>type CodeAction struct</c>（codeactions.go:39-44）。</summary>
public sealed class CodeAction
{
    /// <summary>Go: <c>Description string</c>。</summary>
    public string Description = "";

    /// <summary>Go: <c>Changes []*lsproto.TextEdit</c>。</summary>
    public TextEdit[]? Changes;

    /// <summary>Go: <c>FixID string</c>。</summary>
    public string FixID = "";

    /// <summary>Go: <c>FixAllDescription string</c>。</summary>
    public string FixAllDescription = "";

    /// <summary>
    /// Go: <c>func (a *CodeAction) Compare(b *CodeAction) int</c>（codeactions.go:48-60）；
    /// Compare defines a total ordering for CodeAction values, comparing description
    /// then text edits lexicographically. Used with slices.BinarySearchFunc.
    /// </summary>
    public int Compare(CodeAction b)
    {
        var c = string.CompareOrdinal(Description, b.Description);
        if (c != 0)
        {
            return c;
        }

        var changes = Changes ?? [];
        var otherChanges = b.Changes ?? [];
        c = changes.Length.CompareTo(otherChanges.Length);
        if (c != 0)
        {
            return c;
        }

        for (var i = 0; i < changes.Length; i++)
        {
            c = changes[i].Compare(otherChanges[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }
}

/// <summary>Go: <c>type CombinedCodeActions struct</c>（codeactions.go:62-65）。</summary>
public sealed class CombinedCodeActions
{
    /// <summary>Go: <c>Description string</c>。</summary>
    public string Description = "";

    /// <summary>Go: <c>Changes []*lsproto.TextEdit</c>。</summary>
    public TextEdit[]? Changes;
}

public static partial class Ls
{
    private static CodeFixProvider[]? _codeFixProviders;

    /// <summary>
    /// Go: <c>var codeFixProviders = []*CodeFixProvider{ ImportFixProvider, IsolatedDeclarationsFixProvider,
    /// FixClassIncorrectlyImplementsInterfaceProvider }</c>（codeactions.go:68-72，见文件头第 3 条：惰性构造以保证
    /// 跨 partial 文件的静态初始化顺序无关）。
    /// 三个 provider 是 G10 落地的 `Ls` 静态成员（同名）；「Add more code fix providers here as they are implemented」。
    /// </summary>
    internal static CodeFixProvider[] CodeFixProviders => _codeFixProviders ??=
    [
        ImportFixProvider,
        IsolatedDeclarationsFixProvider,
        FixClassIncorrectlyImplementsInterfaceProvider,
    ];

    /// <summary>
    /// Go: <c>getFixAllQuickFixes</c> 用到的 <c>slices.BinarySearchFunc</c>（见文件头第 4 条）：
    /// 返回插入点与是否命中；`cmp` 为「元素, 目标」的顺序比较。
    /// </summary>
    internal static (int Index, bool Found) BinarySearchFunc<T>(T[] slice, T target, Comparison<T> cmp)
    {
        var low = 0;
        var high = slice.Length;
        while (low < high)
        {
            var mid = low + ((high - low) >> 1);
            var c = cmp(slice[mid], target);
            if (c < 0)
            {
                low = mid + 1;
            }
            else if (c > 0)
            {
                high = mid;
            }
            else
            {
                return (mid, true);
            }
        }

        return (low, false);
    }

    /// <summary>Go: <c>func containsErrorCode(codes []int32, code int32) bool</c>（codeactions.go:373-375）。</summary>
    internal static bool ContainsErrorCode(int[]? codes, int code)
    {
        return codes is not null && Array.IndexOf(codes, code) >= 0;
    }

    /// <summary>Go: <c>func isFixableDiagnostic(diagnostic *ast.Diagnostic, errorCodes []int32) bool</c>（codeactions.go:214-216）。</summary>
    internal static bool IsFixableDiagnostic(AstDiag diagnostic, int[]? errorCodes)
    {
        return diagnostic.Source() == "" && ContainsErrorCode(errorCodes, diagnostic.Code());
    }

    /// <summary>Go: <c>func codeFixProviderMatchesLSPDiagnostic(provider *CodeFixProvider, diagnostic *lsproto.Diagnostic) bool</c>（codeactions.go:208-212）。</summary>
    internal static bool CodeFixProviderMatchesLSPDiagnostic(CodeFixProvider provider, LspDiag diagnostic)
    {
        if (diagnostic.Source is not null && diagnostic.Source != "ts")
        {
            return false;
        }

        return diagnostic.Code is not null && diagnostic.Code.Integer is not null &&
            ContainsErrorCode(provider.ErrorCodes, diagnostic.Code.Integer.Value);
    }

    /// <summary>Go: <c>func isFixAllKind(kind lsproto.CodeActionKind) bool</c>（codeactions.go:219-221）。</summary>
    internal static bool IsFixAllKind(CodeActionKind kind)
    {
        return kind.Contains(CodeActionKind.SourceFixAllTs);
    }

    /// <summary>Go: <c>func wantsQuickFixes(only *[]lsproto.CodeActionKind) bool</c>（codeactions.go:225-233）。</summary>
    internal static bool WantsQuickFixes(CodeActionKind[]? only)
    {
        if (only is null || only.Length == 0)
        {
            return true;
        }

        foreach (var kind in only)
        {
            if (kind.Contains(CodeActionKind.QuickFix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func hasMultipleFixableDiagnostics(ctx context.Context, program *compiler.Program, file *ast.SourceFile, errorCodes []int32) bool</c>（codeactions.go:209-222）。</summary>
    internal static bool HasMultipleFixableDiagnostics(Context ctx, Program program, SourceFile file, int[]? errorCodes)
    {
        var allDiags = GetAllDiagnostics(ctx, program, file);
        var count = 0;
        foreach (var d in allDiags)
        {
            if (IsFixableDiagnostic(d, errorCodes))
            {
                count++;
                if (count >= 2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Go: <c>func getOrganizeImportsActionTitle(ctx context.Context, kind lsproto.CodeActionKind) string</c>（codeactions.go:278-288）。</summary>
    internal static string GetOrganizeImportsActionTitle(Context ctx, CodeActionKind kind)
    {
        var loc = FromContext(ctx);
        if (kind == CodeActionKind.SourceRemoveUnusedImportsTs)
        {
            return Remove_Unused_Imports.Localize(loc);
        }

        if (kind == CodeActionKind.SourceSortImportsTs)
        {
            return Sort_Imports.Localize(loc);
        }

        return Organize_Imports.Localize(loc);
    }

    /// <summary>Go: <c>func getOrganizeImportsActionsForKind(requestedKind lsproto.CodeActionKind) []lsproto.CodeActionKind</c>（codeactions.go:291-313）。</summary>
    internal static List<CodeActionKind> GetOrganizeImportsActionsForKind(CodeActionKind requestedKind)
    {
        CodeActionKind[] organizeImportsKinds =
        [
            CodeActionKind.SourceOrganizeImportsTs,
            CodeActionKind.SourceRemoveUnusedImportsTs,
            CodeActionKind.SourceSortImportsTs,
        ];

        var result = new List<CodeActionKind>();
        foreach (var organizeKind in organizeImportsKinds)
        {
            if (requestedKind.Contains(organizeKind))
            {
                result.Add(organizeKind);
            }
        }

        if (result.Contains(requestedKind))
        {
            return [requestedKind];
        }

        return result;
    }

    /// <summary>Go: <c>func convertToLSPCodeAction(action *CodeAction, diag *lsproto.Diagnostic, uri lsproto.DocumentUri) lsproto.CommandOrCodeAction</c>（codeactions.go:378-396）。</summary>
    internal static CommandOrCodeAction ConvertToLSPCodeAction(CodeAction action, LspDiag diag, DocumentUri uri)
    {
        var kind = CodeActionKind.QuickFix;
        var changes = new Dictionary<DocumentUri, TextEdit?[]?>
        {
            [uri] = action.Changes,
        };
        LspDiag?[] diagnostics = [diag];

        return new CommandOrCodeAction
        {
            CodeAction = new LspCodeAction
            {
                Title = action.Description,
                Kind = kind,
                Edit = new WorkspaceEdit { Changes = changes },
                Diagnostics = diagnostics,
            },
        };
    }
}

public sealed partial class LanguageService
{
    /// <summary>
    /// Go: <c>func (l *LanguageService) ProvideCodeActions(ctx context.Context, params *lsproto.CodeActionParams) (lsproto.CodeActionResponse, error)</c>
    /// （codeactions.go:75-168）。
    /// </summary>
    public (CommandOrCodeActionArrayOrNull Response, Exception? Error) ProvideCodeActions(Context ctx, CodeActionParams @params)
    {
        var (program, file) = GetProgramAndFile(@params.TextDocument.Uri);

        var actions = new List<CommandOrCodeAction>();

        if (@params.Context is not null && @params.Context.Only is not null)
        {
            foreach (var kind in @params.Context.Only)
            {
                foreach (var matchingKind in Ls.GetOrganizeImportsActionsForKind(kind))
                {
                    actions.Add(CreateOrganizeImportsAction(ctx, program, file, matchingKind));
                }

                if (Ls.IsFixAllKind(kind))
                {
                    var (fixAllAction, err) = CreateFixAllAction(ctx, program, file, @params.TextDocument.Uri);
                    if (err is not null)
                    {
                        return (new CommandOrCodeActionArrayOrNull(), err);
                    }

                    if (fixAllAction is not null)
                    {
                        actions.Add(fixAllAction);
                    }
                }
            }
        }

        if (@params.Context is not null && @params.Context.Diagnostics is not null && Ls.WantsQuickFixes(@params.Context.Only))
        {
            var fixIdSeen = new Dictionary<string, CodeFixProvider>();

            // sorted for binary search dedup, dedup across all diagnostics and providers so if multiple diags produce the same codefix, only one is returned
            var seen = Array.Empty<CodeAction>();

            foreach (var diag in @params.Context.Diagnostics)
            {
                if (diag?.Code?.Integer is not { } errorCode)
                {
                    continue;
                }

                foreach (var provider in Ls.CodeFixProviders)
                {
                    if (!Ls.CodeFixProviderMatchesLSPDiagnostic(provider, diag))
                    {
                        continue;
                    }

                    foreach (var mapped in _converters!.FromLSPRangeForSourceFile(file, diag.Range, Feature.CodeActions))
                    {
                        var fixContext = new CodeFixContext
                        {
                            SourceFile = mapped.Script,
                            Span = mapped.Span,
                            ErrorCode = errorCode,
                            Program = program,
                            LS = this,
                            Diagnostic = diag,
                            Params = @params,
                        };

                        var (providerActions, err) = provider.GetCodeActions!(ctx, fixContext);
                        if (err is not null)
                        {
                            return (new CommandOrCodeActionArrayOrNull(), err);
                        }

                        foreach (var action in providerActions ?? [])
                        {
                            var (_, found) = Ls.BinarySearchFunc(seen, action, static (a, b) => a.Compare(b));
                            if (found)
                            {
                                continue;
                            }

                            seen = InsertSorted(seen, action, static (a, b) => a.Compare(b));
                            actions.Add(Ls.ConvertToLSPCodeAction(action, diag, @params.TextDocument.Uri));
                            if (action.FixID != "")
                            {
                                fixIdSeen[action.FixID] = provider;
                            }
                        }
                    }
                }
            }

            var (fixAllActions, err2) = GetFixAllQuickFixes(ctx, program, file, @params.TextDocument.Uri, fixIdSeen);
            if (err2 is not null)
            {
                return (new CommandOrCodeActionArrayOrNull(), err2);
            }

            actions.AddRange(fixAllActions ?? []);
        }

        return (new CommandOrCodeActionArrayOrNull { CommandOrCodeActionArray = actions.ToArray() }, null);
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) getFixAllQuickFixes(ctx context.Context, program *compiler.Program, file *ast.SourceFile, uri lsproto.DocumentUri, fixIdSeen map[string]*CodeFixProvider) ([]lsproto.CommandOrCodeAction, error)</c>
    /// （codeactions.go:172-206）。
    /// </summary>
    private (CommandOrCodeAction[]? Actions, Exception? Error) GetFixAllQuickFixes(
        Context ctx,
        Program program,
        SourceFile file,
        DocumentUri uri,
        Dictionary<string, CodeFixProvider> fixIdSeen)
    {
        List<CommandOrCodeAction>? actions = null;

        // Deduplicate providers; multiple fixIds may map to the same provider.
        var seen = new Set<CodeFixProvider>();
        foreach (var provider in fixIdSeen.Values)
        {
            if (seen.Has(provider))
            {
                continue;
            }

            seen.Add(provider);

            if (provider.GetAllCodeActions is null)
            {
                continue;
            }

            if (!Ls.HasMultipleFixableDiagnostics(ctx, program, file, provider.ErrorCodes))
            {
                continue;
            }

            var fixContext = new CodeFixContext
            {
                SourceFile = file,
                Program = program,
                LS = this,
            };
            var (combined, err) = provider.GetAllCodeActions(ctx, fixContext);
            if (err is not null)
            {
                return (null, err);
            }

            if (combined is not null && combined.Changes is { Length: > 0 } combinedChanges)
            {
                var kind = CodeActionKind.QuickFix;
                var changes = new Dictionary<DocumentUri, TextEdit?[]?>
                {
                    [uri] = combinedChanges,
                };
                (actions ??= []).Add(new CommandOrCodeAction
                {
                    CodeAction = new LspCodeAction
                    {
                        Title = combined.Description,
                        Kind = kind,
                        Edit = new WorkspaceEdit { Changes = changes },
                    },
                });
            }
        }

        return (actions?.ToArray(), null);
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) createFixAllAction(ctx context.Context, program *compiler.Program, file *ast.SourceFile, uri lsproto.DocumentUri) (*lsproto.CommandOrCodeAction, error)</c>
    /// （codeactions.go:236-276）。
    /// </summary>
    private (CommandOrCodeAction? Action, Exception? Error) CreateFixAllAction(Context ctx, Program program, SourceFile file, DocumentUri uri)
    {
        var kind = CodeActionKind.SourceFixAllTs;
        var lspChanges = new Dictionary<DocumentUri, List<TextEdit>>();

        foreach (var provider in Ls.CodeFixProviders)
        {
            if (provider.GetAllCodeActions is null)
            {
                continue;
            }

            var fixContext = new CodeFixContext
            {
                SourceFile = file,
                Program = program,
                LS = this,
            };

            var (combined, err) = provider.GetAllCodeActions(ctx, fixContext);
            if (err is not null)
            {
                return (null, err);
            }

            if (combined is not null && combined.Changes is { Length: > 0 } combinedChanges)
            {
                if (!lspChanges.TryGetValue(uri, out var list))
                {
                    lspChanges[uri] = list = [];
                }

                list.AddRange(combinedChanges);
            }
        }

        if (lspChanges.Count == 0)
        {
            return (null, null);
        }

        var changes = new Dictionary<DocumentUri, TextEdit?[]?>();
        foreach (var (documentUri, edits) in lspChanges)
        {
            changes[documentUri] = edits.ToArray();
        }

        return (new CommandOrCodeAction
        {
            CodeAction = new LspCodeAction
            {
                Title = Fix_All.Localize(FromContext(ctx)),
                Kind = kind,
                Edit = new WorkspaceEdit { Changes = changes },
            },
        }, null);
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) createOrganizeImportsAction(ctx context.Context, program *compiler.Program, file *ast.SourceFile, kind lsproto.CodeActionKind) *lsproto.CommandOrCodeAction</c>
    /// （codeactions.go:316-347）。
    /// </summary>
    private CommandOrCodeAction CreateOrganizeImportsAction(Context ctx, Program program, SourceFile file, CodeActionKind kind)
    {
        var title = Ls.GetOrganizeImportsActionTitle(ctx, kind);
        var changes = OrganizeImports(ctx, file, program, kind);
        if (changes.Count == 0)
        {
            return new CommandOrCodeAction
            {
                CodeAction = new LspCodeAction
                {
                    Title = title,
                    Kind = kind,
                    Edit = new WorkspaceEdit { Changes = new Dictionary<DocumentUri, TextEdit?[]?>() },
                },
            };
        }

        var lspChanges = new Dictionary<DocumentUri, TextEdit?[]?>();
        foreach (var (fileName, edits) in changes)
        {
            var fileURI = FileNameToDocumentURI(fileName);
            lspChanges[fileURI] = edits;
        }

        return new CommandOrCodeAction
        {
            CodeAction = new LspCodeAction
            {
                Title = title,
                Kind = kind,
                Edit = new WorkspaceEdit { Changes = lspChanges },
            },
        };
    }
}
