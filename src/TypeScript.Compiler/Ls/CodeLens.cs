// ported from: internal/ls/codelens.go:1-243
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的**包级**函数 `keyForCodeLens` / `isValidImplementationsCodeLensNode` / `isValidReferenceLensNode` →
//    `public static partial class Ls` 的 `internal static` 成员（§2：包级函数挂同名静态类；未导出 → internal、
//    PascalCase 不加 `_`）。
//
// 2) Go 的 `type codeLensKey struct { kind lsproto.CodeLensKind; startLine, startCharacter, endLine, endCharacter uint32 }`
//    → `readonly record struct CodeLensKey(CodeLensKind Kind, uint StartLine, uint StartCharacter, uint EndLine, uint EndCharacter)`：
//    Go 用该 struct 作 `collections.Set[codeLensKey]` 的键（要求可比较 + 值相等）；C# 的 `record struct` 提供
//    逐字段的值相等/哈希（与 Go 的"可比较 struct"语义一致）。字段在 Go 里是未导出的，但 record 的位置参数会
//    生成 PascalCase 属性——比 `_camelCase` 字段更贴合 .editorconfig，故取属性形式。
//
// 3) `ctx.Err() != nil` → `ctx.CancellationToken.IsCancellationRequested`（Core/Context.cs 只暴露 CancellationToken）。
//
// 4) `node.Symbol()`（Go 的方法）→ C# 的 `node.Symbol`（属性；Node.cs 文件头第 2 条）。
//    `lastSymbol != currentSymbol` 是 Go 的**指针比较** → C# `!ReferenceEquals(lastSymbol, currentSymbol)`（Node/Symbol 不覆写 Equals）。
//
// 5) Go 的 `visit` 是**递归闭包**（捕获 `lastSymbol`/`result`/`seen`/`userPrefs`/`documentURI`/`projection`）→
//    `LanguageService.ProvideCodeLenses` 内的 C# 局部函数 `Visit`（局部函数可前向引用自身；命名走 PascalCase），
//    捕获同样的状态（`lastSymbol` 用局部变量 + 显式保存/恢复，与 Go 的 `savedLastSymbol` 一致）。
//
// 6) ⚠ **跨切片依赖（G6/G8）**：
//    * `provideSymbolsAndEntriesAtPosition` / `provideReferencesFromData` / `provideImplementationsFromData`
//      （Go 在 findallreferences.go，属 G6）→ `ProvideSymbolsAndEntriesAtPosition` / `ProvideReferencesFromData` /
//      `ProvideImplementationsFromData`。签名已与 G6 约定（见交付说明）。
//    * `supplementalFileIndex` / `sourceFileForSupplementalFileIndex`（Go 在 completions.go:139-161，属 G8）在本文件
//      末尾的 `Ls` 静态类里落地：codelens 与 completions 都要用 ⇒ 由本切片先落地，**G8 请直接复用、勿重复声明**
//      （CS0111）。二者语义与 Go 逐行一致。
//
// 7) `codeLens.Data` 在 Go 里是 `*lsproto.CodeLensData`（可空指针）→ C# `CodeLensData?`，调用点判空后 `!`
//    （`CodeLensData.Uri`/`Kind`/`Position`/`SupplementalFileIndex` 都是必填字段）。
//
// 8) `fmt.Errorf("supplemental source file index not found: %d", *codeLens.Data.SupplementalFileIndex)`：
//    Go 在这里会解引用可能为 nil 的 index（实际不可达：index 为 nil 时 `sourceFileForSupplementalFileIndex`
//    直接返回传入的 file，恒非 nil）→ C# 用可空的 `codeLens.Data.SupplementalFileIndex` 插值（数值一致，且不再有
//    不可达的解引用）。
//
// 9) `*lsproto.CodeLens` 返回 → C# 的 `CodeLens?`；`(Response, error)` → C# `(Response, Exception? Error)`。
//    `cmd.Command = *showLocationsCommandName` → C# 的 `Command.Value`（生成物把 `command` 字段改名，见
//    LsProto/Lsp.Generated.Structures.cs:8583 的注释）。
//
// 10) 深递归：`visit` 是 `ForEachChild` 递归（同 Go），入口由调用方跑在大栈线程上。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Locale;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Ls.Ls;
using static TypeScript.Scanner.Scanner;
using Kind = TypeScript.Ast.Kind;

namespace TypeScript.Ls;

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideCodeLenses(ctx, documentURI lsproto.DocumentUri) (lsproto.CodeLensResponse, error)</c>（codelens.go:17-63）。</summary>
    public (CodeLensesOrNull Response, Exception? Error) ProvideCodeLenses(Context ctx, DocumentUri documentURI)
    {
        var (_, file) = GetProgramAndFile(documentURI);

        var userPrefs = UserPreferences().CodeLens;
        if (!userPrefs.ReferencesCodeLensEnabled.IsTrue() && !userPrefs.ImplementationsCodeLensEnabled.IsTrue())
        {
            return (new CodeLensesOrNull(), null);
        }

        var result = new List<CodeLens>();
        var seen = new Set<CodeLensKey>();
        var projections = Concatenate<SourceFile>([file], file.SupplementalSourceFiles());
        foreach (var projection in projections)
        {
            // Keeps track of the last symbol to avoid duplicating code lenses across overloads.
            Symbol? lastSymbol = null;
            bool Visit(Node node)
            {
                if (ctx.CancellationToken.IsCancellationRequested)
                {
                    return true;
                }

                var currentSymbol = node.Symbol;
                if (!ReferenceEquals(lastSymbol, currentSymbol))
                {
                    lastSymbol = currentSymbol;

                    if (userPrefs.ReferencesCodeLensEnabled.IsTrue() && IsValidReferenceLensNode(node, userPrefs))
                    {
                        var codeLens = NewCodeLensForNode(documentURI, projection, node, CodeLensKind.References);
                        if (codeLens is not null && seen.AddIfAbsent(KeyForCodeLens(codeLens)))
                        {
                            result.Add(codeLens);
                        }
                    }

                    if (userPrefs.ImplementationsCodeLensEnabled.IsTrue() && IsValidImplementationsCodeLensNode(node, userPrefs))
                    {
                        var codeLens = NewCodeLensForNode(documentURI, projection, node, CodeLensKind.Implementations);
                        if (codeLens is not null && seen.AddIfAbsent(KeyForCodeLens(codeLens)))
                        {
                            result.Add(codeLens);
                        }
                    }
                }

                var savedLastSymbol = lastSymbol;
                node.ForEachChild(Visit);
                lastSymbol = savedLastSymbol;
                return false;
            }

            Visit(projection.AsNode());
        }

        return (new CodeLensesOrNull { CodeLenses = result.ToArray() }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) ResolveCodeLens(ctx, codeLens *lsproto.CodeLens, showLocationsCommandName *string, orchestrator CrossProjectOrchestrator) (*lsproto.CodeLens, error)</c>（codelens.go:73-155）。</summary>
    public (CodeLens? Response, Exception? Error) ResolveCodeLens(
        Context ctx,
        CodeLens codeLens,
        string? showLocationsCommandName,
        CrossProjectOrchestrator orchestrator)
    {
        var data = codeLens.Data!;
        var uri = data.Uri;
        var textDoc = new TextDocumentIdentifier { Uri = uri };
        var (program, file) = GetProgramAndFile(uri);
        file = SourceFileForSupplementalFileIndex(file, data.SupplementalFileIndex);
        if (file is null)
        {
            return (null, new Exception($"supplemental source file index not found: {data.SupplementalFileIndex}"));
        }
        var locale = TypeScript.Locale.Locale.FromContext(ctx);
        var locs = Array.Empty<Location>();
        var lensTitle = "";
        if (data.Kind == CodeLensKind.References)
        {
            var (symbolData, _) = ProvideSymbolsAndEntriesAtPosition(ctx, program, file, data.Position, false, false);
            var (referencesResp, err) = ProvideReferencesFromData(ctx, new ReferenceParams
            {
                TextDocument = textDoc,
                Position = codeLens.Range.Start,
                Context = new ReferenceContext
                {
                    // Don't include the declaration in the references count.
                    IncludeDeclaration = false,
                },
            }, orchestrator, symbolData);
            if (err is not null)
            {
                return (null, err);
            }
            if (referencesResp.Locations is not null)
            {
                locs = referencesResp.Locations;
            }

            if (locs.Length == 1)
            {
                lensTitle = X_1_reference.Localize(locale);
            }
            else
            {
                lensTitle = X_0_references.Localize(locale, locs.Length);
            }
        }
        else if (data.Kind == CodeLensKind.Implementations)
        {
            var (symbolData, _) = ProvideSymbolsAndEntriesAtPosition(ctx, program, file, data.Position, false, true);
            var (implementations, err) = ProvideImplementationsFromData(
                ctx,
                new ImplementationParams
                {
                    TextDocument = textDoc,
                    Position = codeLens.Range.Start,
                },
                // "Force" link support to be false so that we only get `Locations` back,
                // and don't include the "current" node in the results.
                new SymbolEntryTransformOptions
                {
                    _requireLocationsResult = true,
                    _dropOriginNodes = true,
                },
                orchestrator,
                symbolData);
            if (err is not null)
            {
                return (null, err);
            }

            if (implementations.Locations is not null)
            {
                locs = implementations.Locations;
            }

            if (locs.Length == 1)
            {
                lensTitle = X_1_implementation.Localize(locale);
            }
            else
            {
                lensTitle = X_0_implementations.Localize(locale, locs.Length);
            }
        }

        var cmd = new Command
        {
            Title = lensTitle,
        };
        if (locs.Length > 0 && showLocationsCommandName is not null)
        {
            cmd.Value = showLocationsCommandName;
            cmd.Arguments = [uri, codeLens.Range.Start, locs];
        }

        codeLens.Command = cmd;
        return (codeLens, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) newCodeLensForNode(fileUri lsproto.DocumentUri, file *ast.SourceFile, node *ast.Node, kind lsproto.CodeLensKind) *lsproto.CodeLens</c>（codelens.go:170-190）。</summary>
    private CodeLens? NewCodeLensForNode(DocumentUri fileUri, SourceFile file, Node node, CodeLensKind kind)
    {
        var nodeForRange = node;
        var nodeName = node.Name();
        if (nodeName is not null)
        {
            nodeForRange = nodeName;
        }
        var pos = SkipTrivia(file.Text(), nodeForRange.Pos());
        var (lspRange, fidelity) = _converters!.ToLSPRangeForFeature(file, NewTextRange(pos, node.End()), Feature.CodeLens);
        if (fidelity.IsNone())
        {
            return null;
        }

        return new CodeLens
        {
            Range = lspRange,
            Data = new CodeLensData
            {
                Kind = kind,
                Uri = fileUri,
                Position = pos,
                SupplementalFileIndex = SupplementalFileIndex(file),
            },
        };
    }
}

public static partial class Ls
{
    /// <summary>Go: <c>func keyForCodeLens(codeLens *lsproto.CodeLens) codeLensKey</c>（codelens.go:65-73）。</summary>
    internal static CodeLensKey KeyForCodeLens(CodeLens codeLens)
    {
        return new CodeLensKey(
            codeLens.Data!.Kind,
            codeLens.Range.Start.Line,
            codeLens.Range.Start.Character,
            codeLens.Range.End.Line,
            codeLens.Range.End.Character);
    }

    /// <summary>Go: <c>func isValidImplementationsCodeLensNode(node *ast.Node, userPrefs lsutil.CodeLensUserPreferences) bool</c>（codelens.go:192-215）。</summary>
    internal static bool IsValidImplementationsCodeLensNode(Node node, LsUtil.CodeLensUserPreferences userPrefs)
    {
        switch (node.Kind)
        {
            // Always show on interfaces
            case Kind.InterfaceDeclaration:
                // TODO: ast.KindTypeAliasDeclaration?
                return true;

            // If configured, show on interface methods
            case Kind.MethodSignature:
                return userPrefs.ImplementationsCodeLensShowOnInterfaceMethods.IsTrue() &&
                    node.Parent!.Kind == Kind.InterfaceDeclaration;

            // If configured, show on all class methods - but not private ones.
            case Kind.MethodDeclaration:
                if (userPrefs.ImplementationsCodeLensShowOnAllClassMethods.IsTrue() && node.Parent!.Kind == Kind.ClassDeclaration)
                {
                    return !HasModifier(node, ModifierFlags.Private) && node.Name()!.Kind != Kind.PrivateIdentifier;
                }
                goto case Kind.ClassDeclaration;

            // Always show on abstract classes/properties/methods
            case Kind.ClassDeclaration:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.PropertyDeclaration:
                return HasModifier(node, ModifierFlags.Abstract);
        }

        return false;
    }

    /// <summary>Go: <c>func isValidReferenceLensNode(node *ast.Node, userPrefs lsutil.CodeLensUserPreferences) bool</c>（codelens.go:217-243）。</summary>
    internal static bool IsValidReferenceLensNode(Node node, LsUtil.CodeLensUserPreferences userPrefs)
    {
        switch (node.Kind)
        {
            case Kind.FunctionDeclaration:
                if (userPrefs.ReferencesCodeLensShowOnAllFunctions.IsTrue())
                {
                    return true;
                }
                goto case Kind.VariableDeclaration;

            case Kind.VariableDeclaration:
                return (GetCombinedModifierFlags(node) & ModifierFlags.Export) != 0;

            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.EnumDeclaration:
            case Kind.EnumMember:
                return true;

            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
                // Don't show if child and parent have same start
                // For https://github.com/microsoft/vscode/issues/90396
                // !!!
                switch (node.Parent!.Kind)
                {
                    case Kind.ClassDeclaration:
                    case Kind.InterfaceDeclaration:
                    case Kind.TypeLiteral:
                        return true;
                }
                break;
        }

        return false;
    }

    // ── 以下两个成员在 Go 里位于 completions.go:139-161（属 G8 切片），但 codelens.go 也直接调用它们 ──
    // ⚠ G8：请直接复用本文件已落的这两个成员，勿在 Completions*.cs 里重复声明（CS0111）。

    /// <summary>Go: <c>func supplementalFileIndex(file *ast.SourceFile) *int32</c>（completions.go:139-150）。</summary>
    internal static int? SupplementalFileIndex(SourceFile file)
    {
        var canonical = file.CanonicalSourceFile();
        if (canonical is null)
        {
            return null;
        }
        var supplemental = canonical.SupplementalSourceFiles();
        for (var i = 0; i < supplemental.Length; i++)
        {
            if (ReferenceEquals(supplemental[i], file))
            {
                return i;
            }
        }
        Debug.Fail("supplemental source file is not linked from its canonical source file");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go: <c>func sourceFileForSupplementalFileIndex(file *ast.SourceFile, index *int32) *ast.SourceFile</c>（completions.go:152-161）。</summary>
    internal static SourceFile? SourceFileForSupplementalFileIndex(SourceFile file, int? index)
    {
        if (index is null)
        {
            return file;
        }
        var supplemental = file.SupplementalSourceFiles();
        if (index.Value >= 0 && index.Value < supplemental.Length)
        {
            return supplemental[index.Value];
        }
        return null;
    }
}

/// <summary>Go: <c>type codeLensKey struct</c>（codelens.go:65-70，见文件头第 2 条）。</summary>
internal readonly record struct CodeLensKey(
    CodeLensKind Kind,
    uint StartLine,
    uint StartCharacter,
    uint EndLine,
    uint EndCharacter);
