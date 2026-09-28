// ported from: internal/ls/signaturehelp.go:1-1440
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的**联合类型别名**（`*ast.CallLikeExpression` / `*ast.SignatureDeclaration` / `*ast.Identifier` /
//    `*ast.Expression` / `*ast.LiteralLikeNode` …）在 Go 里都是 `= Node` 的别名
//    ⇒ C# 参数/返回一律写 `Node`（先例 Ls/InlayHints.cs 文件头第 1 条）。
//
// 2) Go 未导出类型 `signatureInformation` → **`SignatureInfo`**：**不能**直接叫 `SignatureInformation`，本文件同时
//    `using TypeScript.LsProto;`，会与 `LsProto.SignatureInformation` 撞名（CS0104）。
//    其余未导出类型照 Go 名 PascalCase：`callInvocation`→`CallInvocation`、`typeArgsInvocation`→`TypeArgsInvocation`、
//    `contextualInvocation`→`ContextualInvocation`、`invocation`→`Invocation`、`candidateInfo`→`CandidateInfo`、
//    `CandidateOrTypeInfo` 同名、`signatureHelpItemInfo`→`SignatureHelpItemInfo`、`signatureHelpParameter`→
//    `SignatureHelpParameter`、`argumentListInfo`→`ArgumentListInfo`、`contextualSignatureLocationInfo`→
//    `ContextualSignatureLocationInfo`、`argumentOrParameterListInfo`→`ArgumentOrParameterListInfo`、
//    `argumentOrParameterListAndIndex`→`ArgumentOrParameterListAndIndex`。
//    结构体一律落成 `internal sealed class`（Go 用指针传引用），Go 未导出字段 → `_camelCase`（artifacts/ls-g1-helpers.md §0）。
//
// 3) Go 在函数体内声明的 `type signatureHelpTriggerReasonKind int32` + 常量 → namespace 级
//    `internal enum SignatureHelpTriggerReasonKind`（C# 不允许在方法体内声明类型）；数值 0/1/2/3 与 Go 逐条一致。
//
// 4) Go 的**包级**函数（`createTypeHelpItems` / `getTypeHelpItem` / `returnTypeToDisplayParts` /
//    `getEnclosingDeclarationFromInvocation` / `getExpressionFromInvocation` / `getCandidateOrTypeInfo` /
//    `isSyntacticOwner` / `containsPrecedingToken` / `getContainingArgumentInfo` /
//    `getImmediatelyContainingArgumentOrContextualParameterInfo` / `getImmediatelyContainingArgumentInfo` /
//    `getArgumentIndexForTemplatePiece` / `getAdjustedNode` / `getSpreadElementCount` / `getArgumentIndex` /
//    `getArgumentCount` / `getArgumentIndexOrCount` / `getArgumentOrParameterListInfo` /
//    `getApplicableSpanForArguments` / `ensureMinimumSpanSize` / `getArgumentOrParameterListAndIndex` /
//    `getChildListThatStartsWithOpenerToken` / `tryGetParameterInfo` / `chooseBetterSymbol` /
//    `getContextualSignatureLocationInfo` / `getHighestBinary` / `countBinaryExpressionParameters` /
//    `getTokenFromNodeList` / `getArgumentListInfoForTemplate` / `getApplicableRangeForTaggedTemplate` /
//    `createSignatureHelpParameterForTypeParameter`）→ `public static partial class Ls` 的 `internal static` 成员
//    （PascalCase，不加 `_`；§2：包级函数挂同名静态类）。
//
// 5) Go 的包级常量 `signatureHelpNodeBuilderFlags` → `Ls._signatureHelpNodeBuilderFlags`（未导出字段带 `_`）。
//    Go 的包级导出 var `SignatureHelpTriggerCharacters` / `SignatureHelpRetriggerCharacters` → `Ls` 的
//    `public static readonly string[]`（`lsp/server.go` 引用，签名稳定）。
//
// 6) `ctx.Err() != nil` → `ctx.CancellationToken.IsCancellationRequested`（Core/Context.cs 只暴露 CancellationToken）。
//
// 7) `lsproto.GetClientCapabilities(ctx)` → 本仓 `GetClientCapabilities()`（LsProto/Lsp.cs:551 已去掉 ctx 参数）；
//    `lsproto.PreferredMarkupKind(...)` 同名（LsProto/Lsp.cs:555）。
//
// 8) `*T` 返回 → `T?`；`(Response, error)` → C# `(Response, Exception? Error)`（本仓既有约定）。
//
// 9) `signatureInformation.ColorizedRuns []*lsproto.VSClassifiedTextRun` → `VSClassifiedTextRun?[]?`（LsProto 生成物的
//    数组元素是可空引用；`DisplayPartsWriter.GetRuns()` 返回非空元素数组，赋值是恒等转换）。
//
// 10) `strings.Builder` → `System.Text.StringBuilder`（`WriteString`→`Append`）；`strings.HasPrefix(s, p)` →
//     `s.StartsWith(p, StringComparison.Ordinal)`。
//
// 11) `slices.Contains(xs, x)` → `Array.IndexOf(xs, x) >= 0`；`ast.GetDeclarationName(node) == name` 保持字符串比较。
//
// 12) `core.FindIndex(slice, pred)` → `FindIndex(...)`（`using static TypeScript.Core.Core;`）。
//
// 13) ⚠ Go 上游在 JSX 属性列表与 tagged template 两处把**长度**当 `end` 写进 `core.NewTextRange(pos, end-pos)`
//     （signaturehelp.go:1000 与 :1439），已 1:1 保留，勿"顺手修正"（要与 Go 基线一致）。
//
// 14) `checker.GetResolvedSignatureForSignatureHelp` 返回 `(resolvedSignature *Signature, candidates []*Signature)`；
//     本仓同名静态方法返回 `(Signature? Signature, Signature[] Candidates)` ⇒ `CandidateInfo._resolvedSignature`
//     写成非空 `Checker.Signature`（Go 侧该值也是"实际必有"，nil 只会在下游解引用时 panic，C# 语义一致）。
//
// 15) 深递归：本文件与 Go 一样用 `ForEachChild` 递归（`FindSignatureHelpFromNamedDeclarations` 会遍历整棵源文件树），
//     入口由调用方跑在 64 MiB 大栈线程上（本仓既定做法）。
//
// 16) `checker.GetExpandedParameters` 返回的 `[][]*ast.Symbol` 内层在 Go 里**可能是 nil slice**
//     （nodebuilderimpl.go 的 `return [][]*ast.Symbol{sig.parameters}`，无参签名时 `sig.parameters == nil`），
//     Go 的 `len(nil) == 0` 让它天然可用；C# 侧该元素是 **null 数组**，直接取 `.Length` 会 NRE
//     （历史上 6 个用例的 `textDocument/signatureHelp` panic 就是这个）⇒ `itemInfoForTypeParameters` /
//     `itemInfoForParameters` 里统一 `lists[i] ?? []`。
//
// 17) ⚠ `itemInfoForTypeParameters` 的 `signatureHelpItemInfo.parameters` 在 Go 里赋的是
//     **`signatureHelpTypeParameters`**（类型形参），不是循环里累积的 value 形参（signaturehelp.go:582；
//     那个局部 `parameters` 在 Go 里是 dead store）。1:1 保留，勿"顺手改成" value 形参。
using System;
using System.Text;
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Checker;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Ls.Ls;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
// 同名类型消歧（`using System` / `using TypeScript.*` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Type = TypeScript.Checker.Type;
// `NodeBuilderFlags`/`NodeBuilderInternalFlags` 是 `nodebuilder.Flags`/`nodebuilder.InternalFlags` 的别名（先例 Checker/NodeBuilder.cs:25-26）。
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;

namespace TypeScript.Ls;

/// <summary>
/// Go: <c>type signatureHelpTriggerReasonKind int32</c>（signaturehelp.go:82-88 的局部类型 + 常量组，见文件头第 3 条）。
/// 数值：None=0（Go 的 undefined）、Invoked=1、CharacterTyped=2、Retriggered=3。
/// </summary>
internal enum SignatureHelpTriggerReasonKind
{
    None = 0,
    Invoked = 1,
    CharacterTyped = 2,
    Retriggered = 3,
}

/// <summary>Go: <c>type callInvocation struct { node *ast.Node }</c>（signaturehelp.go:31-33）。</summary>
internal sealed class CallInvocation
{
    internal Node _node = null!;
}

/// <summary>Go: <c>type typeArgsInvocation struct { called *ast.Identifier }</c>（signaturehelp.go:35-37）。</summary>
internal sealed class TypeArgsInvocation
{
    internal Node _called = null!;
}

/// <summary>Go: <c>type contextualInvocation struct</c>（signaturehelp.go:39-43）。</summary>
internal sealed class ContextualInvocation
{
    internal Checker.Signature _signature = null!;
    internal Node _node = null!;   // Just for enclosingDeclaration for printing types
    internal Symbol _symbol = null!;
}

/// <summary>Go: <c>type invocation struct</c>（signaturehelp.go:45-49）。</summary>
internal sealed class Invocation
{
    internal CallInvocation? _callInvocation;
    internal TypeArgsInvocation? _typeArgsInvocation;
    internal ContextualInvocation? _contextualInvocation;
}

/// <summary>Go: <c>type candidateInfo struct</c>（signaturehelp.go:757-760）。</summary>
internal sealed class CandidateInfo
{
    internal Checker.Signature[] _candidates = [];
    internal Checker.Signature _resolvedSignature = null!;
}

/// <summary>Go: <c>type CandidateOrTypeInfo struct { candidateInfo *candidateInfo; typeInfo *ast.Symbol }</c>（signaturehelp.go:762-765）。</summary>
internal sealed class CandidateOrTypeInfo
{
    internal CandidateInfo? _candidateInfo;
    internal Symbol? _typeInfo;
}

/// <summary>Go: <c>type argumentListInfo struct</c>（signaturehelp.go:912-921）。</summary>
internal sealed class ArgumentListInfo
{
    internal bool _isTypeParameterList;
    internal Invocation _invocation = null!;
    internal TextRange _argumentsSpan;
    internal int _argumentIndex;
    /// <summary>argumentCount is the *apparent* number of arguments.</summary>
    internal int _argumentCount;
}

/// <summary>Go: <c>type contextualSignatureLocationInfo struct</c>（signaturehelp.go:1067-1072）。</summary>
internal sealed class ContextualSignatureLocationInfo
{
    internal Type _contextualType = null!;
    internal int _argumentIndex;
    internal int _argumentCount;
    internal TextRange _argumentsSpan;
}

/// <summary>Go: <c>type argumentOrParameterListInfo struct</c>（signaturehelp.go:1148-1153）。</summary>
internal sealed class ArgumentOrParameterListInfo
{
    internal NodeList? _list;
    internal int _argumentIndex;
    internal int _argumentCount;
    internal TextRange _argumentsSpan;
}

/// <summary>Go: <c>type argumentOrParameterListAndIndex struct</c>（signaturehelp.go:1211-1214）。</summary>
internal sealed class ArgumentOrParameterListAndIndex
{
    internal NodeList? _list;
    internal int _argumentIndex;
}

/// <summary>
/// Go: <c>type signatureInformation struct</c>（signaturehelp.go:713-726，见文件头第 2 条：C# 改名以免与
/// `LsProto.SignatureInformation` 撞名）。Represents the signature of something callable.
/// </summary>
internal sealed class SignatureInfo
{
    /// <summary>The Label of this signature. Will be shown in the UI.</summary>
    internal string Label = "";

    /// <summary>The human-readable doc-comment of this signature. Will be shown in the UI but can be omitted.</summary>
    internal string? Documentation;

    /// <summary>The Parameters of this signature.</summary>
    internal SignatureHelpParameter[] Parameters = [];

    /// <summary>Needed only here, not in lsp.</summary>
    internal bool IsVariadic;

    /// <summary>Classified text runs for VS colorized label.</summary>
    internal VSClassifiedTextRun?[]? ColorizedRuns;
}

/// <summary>Go: <c>type signatureHelpItemInfo struct</c>（signaturehelp.go:728-732）。</summary>
internal sealed class SignatureHelpItemInfo
{
    internal bool _isVariadic;
    internal SignatureHelpParameter[] _parameters = [];
    internal DisplayPartsWriter _writer = null!;
}

/// <summary>Go: <c>type signatureHelpParameter struct</c>（signaturehelp.go:734-738）。</summary>
internal sealed class SignatureHelpParameter
{
    internal ParameterInformation _parameterInfo = null!;
    internal bool _isRest;
    internal bool _isOptional;
}

public sealed partial class LanguageService
{
    /// <summary>
    /// Go: <c>func (l *LanguageService) ProvideSignatureHelp(ctx, documentURI, position, context *lsproto.SignatureHelpContext) (lsproto.SignatureHelpResponse, error)</c>
    /// （signaturehelp.go:49-73）。
    /// </summary>
    public (SignatureHelpOrNull Response, Exception? Error) ProvideSignatureHelp(
        Context ctx,
        DocumentUri documentURI,
        Position position,
        SignatureHelpContext? @context)
    {
        var (program, sourceFile) = GetProgramAndFile(documentURI);
        var positions = _converters!.FromLSPPositionForSourceFile(sourceFile, position, Feature.SignatureHelp);
        foreach (var projection in positions)
        {
            if (!projection.Fidelity.IsSingleSegment())
            {
                continue;
            }

            var items = GetSignatureHelpItems(
                ctx,
                (int)projection.Position,
                program,
                projection.Script,
                @context);
            if (items is not null)
            {
                return (new SignatureHelpOrNull { SignatureHelp = items }, null);
            }
        }

        return (new SignatureHelpOrNull(), null);
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) GetSignatureHelpItems(ctx, position, program, sourceFile, context) *lsproto.SignatureHelp</c>
    /// （signaturehelp.go:75-167）。
    /// </summary>
    public SignatureHelp? GetSignatureHelpItems(
        Context ctx,
        int position,
        Program program,
        SourceFile sourceFile,
        SignatureHelpContext? @context)
    {
        var (typeChecker, done) = program.GetTypeCheckerForFile(ctx, sourceFile);
        try
        {
            // Decide whether to show signature help
            var startingToken = FindPrecedingToken(sourceFile, position);
            if (startingToken is null)
            {
                // We are at the beginning of the file
                return null;
            }

            // Emulate VS Code's toTsTriggerReason.
            var triggerReasonKind = SignatureHelpTriggerReasonKind.None;
            if (@context is not null)
            {
                switch (@context.TriggerKind)
                {
                    case SignatureHelpTriggerKind.TriggerCharacter:
                        if (@context.TriggerCharacter is not null)
                        {
                            triggerReasonKind = @context.IsRetrigger
                                ? SignatureHelpTriggerReasonKind.Retriggered
                                : SignatureHelpTriggerReasonKind.CharacterTyped;
                        }
                        else
                        {
                            triggerReasonKind = SignatureHelpTriggerReasonKind.Invoked;
                        }

                        break;
                    case SignatureHelpTriggerKind.ContentChange:
                        triggerReasonKind = @context.IsRetrigger
                            ? SignatureHelpTriggerReasonKind.Retriggered
                            : SignatureHelpTriggerReasonKind.CharacterTyped;
                        break;
                    case SignatureHelpTriggerKind.Invoked:
                        triggerReasonKind = SignatureHelpTriggerReasonKind.Invoked;
                        break;
                    default:
                        triggerReasonKind = SignatureHelpTriggerReasonKind.Invoked;
                        break;
                }
            }

            // Only need to be careful if the user typed a character and signature help wasn't showing.
            var onlyUseSyntacticOwners = triggerReasonKind == SignatureHelpTriggerReasonKind.CharacterTyped;

            // Bail out quickly in the middle of a string or comment, don't provide signature help unless the user explicitly requested it.
            if (onlyUseSyntacticOwners && (Ls.IsInString(sourceFile, position, startingToken) || Ls.IsInComment(sourceFile, position, startingToken) is not null))
            {
                return null;
            }

            var isManuallyInvoked = triggerReasonKind == SignatureHelpTriggerReasonKind.Invoked;
            var argumentInfo = Ls.GetContainingArgumentInfo(startingToken, sourceFile, typeChecker, isManuallyInvoked, position);
            if (argumentInfo is null)
            {
                return null;
            }

            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return null;
            }

            // Extra syntactic and semantic filtering of signature help
            var candidateInfo = Ls.GetCandidateOrTypeInfo(argumentInfo, typeChecker, sourceFile, startingToken, onlyUseSyntacticOwners);

            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (candidateInfo is null)
            {
                // For JS files, try a fallback that searches all source files for declarations
                // with matching names that have call signatures. This is a heuristic for untyped JS code.
                if (IsSourceFileJS(sourceFile))
                {
                    return CreateJSSignatureHelpItems(ctx, argumentInfo, program, typeChecker);
                }

                return null;
            }

            if (candidateInfo._candidateInfo is not null)
            {
                return CreateSignatureHelpItems(
                    ctx,
                    candidateInfo._candidateInfo._candidates,
                    candidateInfo._candidateInfo._resolvedSignature,
                    argumentInfo,
                    sourceFile,
                    typeChecker,
                    onlyUseSyntacticOwners);
            }

            return Ls.CreateTypeHelpItems(ctx, candidateInfo._typeInfo!, argumentInfo, sourceFile, typeChecker);
        }
        finally
        {
            done();
        }
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) createJSSignatureHelpItems(ctx, argumentInfo, program, c) *lsproto.SignatureHelp</c>
    /// （signaturehelp.go:244-265）。createJSSignatureHelpItems is a fallback for JavaScript files when normal
    /// signature help doesn't produce results. It searches all source files for declarations with matching names
    /// that have call signatures.
    /// </summary>
    private SignatureHelp? CreateJSSignatureHelpItems(Context ctx, ArgumentListInfo argumentInfo, Program program, Checker.Checker c)
    {
        if (argumentInfo._invocation._contextualInvocation is not null)
        {
            return null;
        }

        // See if we can find some symbol with the call expression name that has call signatures.
        var expression = Ls.GetExpressionFromInvocation(argumentInfo);
        if (expression is null || !IsPropertyAccessExpression(expression))
        {
            return null;
        }

        var name = expression.AsPropertyAccessExpression()!.Name()!.Text();
        if (name == "")
        {
            return null;
        }

        foreach (var sf in program.GetSourceFiles())
        {
            var result = FindSignatureHelpFromNamedDeclarations(ctx, sf, name, argumentInfo, c);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) findSignatureHelpFromNamedDeclarations(ctx, sourceFile, name, argumentInfo, c) *lsproto.SignatureHelp</c>
    /// （signaturehelp.go:267-293）。
    /// </summary>
    private SignatureHelp? FindSignatureHelpFromNamedDeclarations(Context ctx, SourceFile sourceFile, string name, ArgumentListInfo argumentInfo, Checker.Checker c)
    {
        SignatureHelp? result = null;
        bool Visit(Node node)
        {
            if (result is not null)
            {
                return true;
            }

            if (GetDeclarationName(node) == name)
            {
                if (node.Symbol is { } symbol)
                {
                    if (c.GetTypeOfSymbolAtLocation(symbol, node) is { } t)
                    {
                        if (c.GetCallSignatures(t) is { Length: > 0 } callSignatures)
                        {
                            result = CreateSignatureHelpItems(ctx, callSignatures, callSignatures[0], argumentInfo, sourceFile, c, true /*useFullPrefix*/);
                            if (result is not null)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            node.ForEachChild(child => Visit(child));
            return result is not null;
        }

        Visit(sourceFile.AsNode());
        return result;
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) createSignatureHelpItems(ctx, candidates, resolvedSignature, argumentInfo, sourceFile, c, useFullPrefix) *lsproto.SignatureHelp</c>
    /// （signaturehelp.go:295-417）。
    /// </summary>
    private SignatureHelp? CreateSignatureHelpItems(
        Context ctx,
        Checker.Signature[] candidates,
        Checker.Signature resolvedSignature,
        ArgumentListInfo argumentInfo,
        SourceFile sourceFile,
        Checker.Checker c,
        bool useFullPrefix)
    {
        var caps = GetClientCapabilities();
        var docFormat = PreferredMarkupKind(caps.TextDocument.SignatureHelp.SignatureInformation.DocumentationFormat);
        var vsCapability = caps.VSSupportsVisualStudioExtensions;

        var enclosingDeclaration = Ls.GetEnclosingDeclarationFromInvocation(argumentInfo._invocation);
        if (enclosingDeclaration is null)
        {
            return null;
        }

        Symbol? callTargetSymbol;
        if (argumentInfo._invocation._contextualInvocation is not null)
        {
            callTargetSymbol = argumentInfo._invocation._contextualInvocation._symbol;
        }
        else
        {
            callTargetSymbol = c.GetSymbolAtLocation(Ls.GetExpressionFromInvocation(argumentInfo)!);
            if (callTargetSymbol is null && useFullPrefix && resolvedSignature.Declaration() is not null)
            {
                callTargetSymbol = resolvedSignature.Declaration()!.Symbol;
            }
        }

        var callTargetDisplayParts = new StringBuilder();
        // A contextual signature for an anonymous inline function type (e.g. a callback
        // argument) has a synthetic symbol whose name is an internal marker such as
        // "\xFEtype". There is no meaningful name to show, so render the signature with
        // no prefix (as we already do when there is no call target symbol) rather than
        // leaking the internal name.
        if (callTargetSymbol is not null && !callTargetSymbol.Name.StartsWith(InternalSymbolNamePrefix, StringComparison.Ordinal))
        {
            if (useFullPrefix)
            {
                callTargetDisplayParts.Append(c.SymbolToStringEx(callTargetSymbol, sourceFile.AsNode(), SymbolFlags.None, SymbolFormatFlags.UseAliasDefinedOutsideCurrentScope));
            }
            else
            {
                callTargetDisplayParts.Append(c.SymbolToString(callTargetSymbol));
            }
        }

        var items = new SignatureInfo[candidates.Length][];
        for (var i = 0; i < candidates.Length; i++)
        {
            items[i] = GetSignatureHelpItem(candidates[i], argumentInfo._isTypeParameterList, callTargetDisplayParts.ToString(), callTargetSymbol, enclosingDeclaration, sourceFile, c, docFormat, vsCapability);
        }

        var selectedItemIndex = 0;
        var itemSeen = 0;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (ReferenceEquals(candidates[i], resolvedSignature))
            {
                selectedItemIndex = itemSeen;
                if (item.Length > 1)
                {
                    var count = 0;
                    foreach (var j in item)
                    {
                        if (j.IsVariadic || j.Parameters.Length >= argumentInfo._argumentCount)
                        {
                            selectedItemIndex = itemSeen + count;
                            break;
                        }

                        count++;
                    }
                }
            }

            itemSeen = itemSeen + item.Length;
        }

        Debug.Assert(selectedItemIndex != -1);
        var flattenedSignatures = new System.Collections.Generic.List<SignatureInfo>();
        foreach (var item in items)
        {
            flattenedSignatures.AddRange(item);
        }

        if (flattenedSignatures.Count == 0)
        {
            return null;
        }

        // Check client capabilities for activeParameter handling
        var sigInfoCaps = caps.TextDocument.SignatureHelp.SignatureInformation;
        var supportsPerSignatureActiveParam = sigInfoCaps.ActiveParameterSupport;
        var supportsNullActiveParam = sigInfoCaps.NoActiveParameterSupport;

        // Converting List<SignatureInfo> to SignatureInformation?[]
        var signatureInformation = new SignatureInformation?[flattenedSignatures.Count];
        for (var i = 0; i < flattenedSignatures.Count; i++)
        {
            var item = flattenedSignatures[i];
            var parameters = new ParameterInformation?[item.Parameters.Length];
            for (var j = 0; j < item.Parameters.Length; j++)
            {
                parameters[j] = item.Parameters[j]._parameterInfo;
            }

            StringOrMarkupContent? documentation = null;
            if (item.Documentation is not null)
            {
                documentation = new StringOrMarkupContent
                {
                    MarkupContent = new MarkupContent
                    {
                        Kind = docFormat,
                        Value = item.Documentation,
                    },
                };
            }

            var sigInfo = new SignatureInformation
            {
                Label = item.Label,
                Documentation = documentation,
                Parameters = parameters,
            };

            // Set VS-specific colorized label if we have classified runs
            if (item.ColorizedRuns is { Length: > 0 })
            {
                sigInfo.VSColorizedLabel = new VSClassifiedTextElement
                {
                    Runs = item.ColorizedRuns,
                };
            }

            // If client supports per-signature activeParameter, set it on each SignatureInformation
            if (supportsPerSignatureActiveParam)
            {
                sigInfo.ActiveParameter = ComputeActiveParameter(item, argumentInfo._argumentIndex, supportsNullActiveParam);
            }

            signatureInformation[i] = sigInfo;
        }

        var help = new SignatureHelp
        {
            Signatures = signatureInformation,
            ActiveSignature = (uint)selectedItemIndex,
        };

        // If client doesn't support per-signature activeParameter, set it on the top-level SignatureHelp
        if (!supportsPerSignatureActiveParam)
        {
            var activeSignature = flattenedSignatures[selectedItemIndex];
            help.ActiveParameter = ComputeActiveParameter(activeSignature, argumentInfo._argumentIndex, supportsNullActiveParam);
        }

        return help;
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) computeActiveParameter(sig signatureInformation, argumentIndex int, supportsNull bool) *lsproto.UintegerOrNull</c>
    /// （signaturehelp.go:419-447）。computeActiveParameter calculates the active parameter index for a signature,
    /// handling variadic signatures and null support appropriately.
    /// </summary>
    private UintegerOrNull? ComputeActiveParameter(SignatureInfo sig, int argumentIndex, bool supportsNull)
    {
        var paramCount = sig.Parameters.Length;
        if (paramCount == 0)
        {
            // No parameters, return nil (omit the field)
            return null;
        }

        var activeParam = (uint)argumentIndex;

        if (sig.IsVariadic)
        {
            var firstRest = FindIndex(sig.Parameters, p => p._isRest);
            if (-1 < firstRest && firstRest < paramCount - 1)
            {
                // Middle rest parameter - we can't accurately highlight, so indicate "no active parameter"
                if (supportsNull)
                {
                    return new UintegerOrNull(); // null means "no parameter is active"
                }

                // Client doesn't support null, use out-of-range index (defaults to 0 per LSP spec)
                return new UintegerOrNull { Uinteger = (uint)paramCount };
            }

            // Clamp to last parameter for trailing rest parameters
            if (activeParam > (uint)(paramCount - 1))
            {
                activeParam = (uint)(paramCount - 1);
            }
        }

        return new UintegerOrNull { Uinteger = activeParam };
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) getSignatureHelpItem(candidate, isTypeParameterList, callTargetSymbol, callTargetSym, enclosingDeclaration, sourceFile, c, docFormat, vsCapability) []signatureInformation</c>
    /// （signaturehelp.go:449-486）。
    /// </summary>
    private SignatureInfo[] GetSignatureHelpItem(
        Checker.Signature candidate,
        bool isTypeParameterList,
        string callTargetSymbol,
        Symbol? callTargetSym,
        Node? enclosingDeclaration,
        SourceFile sourceFile,
        Checker.Checker c,
        MarkupKind docFormat,
        bool vsCapability)
    {
        SignatureHelpItemInfo[] infos;
        if (isTypeParameterList)
        {
            infos = ItemInfoForTypeParameters(candidate, c, enclosingDeclaration, sourceFile, docFormat, vsCapability);
        }
        else
        {
            infos = ItemInfoForParameters(candidate, c, enclosingDeclaration, sourceFile, docFormat, vsCapability);
        }

        var suffixDpw = ReturnTypeToDisplayParts(candidate, c, enclosingDeclaration, sourceFile, vsCapability);

        // Generate documentation from the signature's declaration
        string? documentation = null;
        if (candidate.Declaration() is { } declaration)
        {
            var doc = Ls.GetDocumentationFromDeclaration(DocumentationLocationMapper(Feature.SignatureHelp), c, null, declaration, null, docFormat, true /*commentOnly*/);
            if (doc != "")
            {
                documentation = doc;
            }
        }

        var result = new SignatureInfo[infos.Length];
        for (var i = 0; i < infos.Length; i++)
        {
            var info = infos[i];
            var labelDpw = new DisplayPartsWriter(vsCapability);
            if (callTargetSymbol != "")
            {
                labelDpw.WriteSymbol(callTargetSymbol, callTargetSym);
            }

            labelDpw.WriteFrom(info._writer);
            labelDpw.WriteFrom(suffixDpw);

            result[i] = new SignatureInfo
            {
                Label = labelDpw.String(),
                Documentation = documentation,
                Parameters = info._parameters,
                IsVariadic = info._isVariadic,
                ColorizedRuns = labelDpw.GetRuns(),
            };
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) itemInfoForTypeParameters(candidateSignature, c, enclosingDeclaration, sourceFile, docFormat, vsCapability) []*signatureHelpItemInfo</c>
    /// （signaturehelp.go:513-586）。
    /// </summary>
    private SignatureHelpItemInfo[] ItemInfoForTypeParameters(
        Checker.Signature candidateSignature,
        Checker.Checker c,
        Node? enclosingDeclaration,
        SourceFile sourceFile,
        MarkupKind docFormat,
        bool vsCapability)
    {
        var emitContext = NewEmitContext();
        var p = NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF }, new PrintHandlers(), emitContext);

        Type[] typeParameters;
        if (candidateSignature.Target() is not null)
        {
            typeParameters = candidateSignature.Target()!.TypeParameters();
        }
        else
        {
            typeParameters = candidateSignature.TypeParameters();
        }

        var signatureHelpTypeParameters = new SignatureHelpParameter[typeParameters.Length];
        for (var i = 0; i < typeParameters.Length; i++)
        {
            signatureHelpTypeParameters[i] = Ls.CreateSignatureHelpParameterForTypeParameter(typeParameters[i], sourceFile, enclosingDeclaration, c, p);
        }

        var thisParameter = new System.Collections.Generic.List<SignatureHelpParameter>();
        if (candidateSignature.ThisParameter() is { } thisParam)
        {
            thisParameter.Add(CreateSignatureHelpParameterForParameter(thisParam, enclosingDeclaration, p, sourceFile, c, docFormat));
        }

        // Creating type parameter display label
        var dpw = new DisplayPartsWriter(vsCapability);

        var lessThanToken = TokenToString(Kind.LessThanToken);
        dpw.WritePunctuation(lessThanToken);
        for (var i = 0; i < signatureHelpTypeParameters.Length; i++)
        {
            if (i > 0)
            {
                dpw.WritePunctuation(", ");
            }

            var label = signatureHelpTypeParameters[i]._parameterInfo.Label.String!;
            dpw.WriteClassified(label, ClassificationTypeName.TypeParameterName);
        }

        var greaterThanToken = TokenToString(Kind.GreaterThanToken);
        dpw.WritePunctuation(greaterThanToken);

        // Creating display label for parameters like, (a: string, b: number)
        var lists = c.GetExpandedParameters(candidateSignature, false);
        if (lists.Length != 0)
        {
            var openParen = TokenToString(Kind.OpenParenToken);
            dpw.WritePunctuation(openParen);
        }

        var result = new SignatureHelpItemInfo[lists.Length];
        for (var i = 0; i < lists.Length; i++)
        {
            // Go 的 `[][]*ast.Symbol` 内层可能是 **nil slice**（无参签名：`CheckFlags.restParameter` 检查外的
            // `return [][]*ast.Symbol{sig.parameters}`），Go 里 `len(nil) == 0`；C# 里元素是 null
            // ⇒ 必须显式补 `?? []`（先例：本仓"nil 切片 → T[]? + ?? []"约定）。
            var parameterList = lists[i] ?? [];
            var paramDpw = new DisplayPartsWriter(vsCapability);
            paramDpw.WriteFrom(dpw);

            var parameters = new System.Collections.Generic.List<SignatureHelpParameter>(thisParameter);
            for (var j = 0; j < parameterList.Length; j++)
            {
                var param = parameterList[j];
                var paramNode = Checker.Checker.NewNodeBuilder(c, emitContext).SymbolToParameterDeclaration(param, enclosingDeclaration, _signatureHelpNodeBuilderFlags, NodeBuilderInternalFlags.None, null);

                if (j > 0)
                {
                    paramDpw.WritePunctuation(", ");
                }

                // Use a temporary writer for p.Write since the printer calls Clear() on its writer
                var tempDpw = new DisplayPartsWriter(vsCapability);
                p.Write(paramNode!, sourceFile, tempDpw, null);
                var paramLabel = tempDpw.String();
                paramDpw.WriteFrom(tempDpw);

                var parameter = CreateSignatureHelpParameterFromLabel(param, paramLabel, c, docFormat);
                parameters.Add(parameter);
            }

            var closeParen = TokenToString(Kind.CloseParenToken);
            paramDpw.WritePunctuation(closeParen);

            result[i] = new SignatureHelpItemInfo
            {
                _isVariadic = false,
                // ⚠ Go 在这里赋的是 **类型形参**（`parameters: signatureHelpTypeParameters`），而不是上面循环里
                // 累积的 value 形参 —— 后者在 Go 里是 dead store（signaturehelp.go:553-584）。类型实参列表
                // （`f<…`）的 activeParameter 因此指向 `<T, U, …>` 里的类型形参；用例
                // TestSignatureHelpTypeArguments（ParameterName "T"）与 TestSignatureHelpTypeParametersNotVariadic
                // （isVariadic=false，harness 按 Parameters 里是否有 "..." 判定）都依赖这一点。
                _parameters = signatureHelpTypeParameters,
                _writer = paramDpw,
            };
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) itemInfoForParameters(candidateSignature, c, enclosingDeclaratipn, sourceFile, docFormat, vsCapability) []*signatureHelpItemInfo</c>
    /// （signaturehelp.go:588-664）。
    /// </summary>
    private SignatureHelpItemInfo[] ItemInfoForParameters(
        Checker.Signature candidateSignature,
        Checker.Checker c,
        Node? enclosingDeclaratipn,
        SourceFile sourceFile,
        MarkupKind docFormat,
        bool vsCapability)
    {
        var emitContext = NewEmitContext();
        var p = NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF }, new PrintHandlers(), emitContext);

        var candidateTypeParameters = candidateSignature.TypeParameters();
        var signatureHelpTypeParameters = new SignatureHelpParameter[candidateTypeParameters.Length];
        if (candidateTypeParameters.Length != 0)
        {
            for (var i = 0; i < candidateTypeParameters.Length; i++)
            {
                signatureHelpTypeParameters[i] = Ls.CreateSignatureHelpParameterForTypeParameter(candidateTypeParameters[i], sourceFile, enclosingDeclaratipn, c, p);
            }
        }

        // Creating display label for type parameters like, <T, U>
        var dpw = new DisplayPartsWriter(vsCapability);

        if (signatureHelpTypeParameters.Length != 0)
        {
            var lessThanToken = TokenToString(Kind.LessThanToken);
            dpw.WritePunctuation(lessThanToken);
            for (var i = 0; i < signatureHelpTypeParameters.Length; i++)
            {
                if (i > 0)
                {
                    dpw.WritePunctuation(", ");
                }

                var label = signatureHelpTypeParameters[i]._parameterInfo.Label.String!;
                dpw.WriteClassified(label, ClassificationTypeName.TypeParameterName);
            }

            var greaterThanToken = TokenToString(Kind.GreaterThanToken);
            dpw.WritePunctuation(greaterThanToken);
        }

        // Creating display parts for parameters. For example, (a: string, b: number)
        var lists = c.GetExpandedParameters(candidateSignature, false);
        if (lists.Length != 0)
        {
            var openParen = TokenToString(Kind.OpenParenToken);
            dpw.WritePunctuation(openParen);
        }

        bool IsVariadicList(Symbol[] parameterList)
        {
            if (!c.HasEffectiveRestParameter(candidateSignature))
            {
                return false;
            }

            if (lists.Length == 1)
            {
                return true;
            }

            return parameterList.Length != 0 && parameterList[^1] is not null && (parameterList[^1].CheckFlags & CheckFlags.RestParameter) != 0;
        }

        var result = new SignatureHelpItemInfo[lists.Length];
        for (var i = 0; i < lists.Length; i++)
        {
            // 同上：Go 的内层切片可能是 nil（无参签名），Go `len(nil) == 0` ⇒ C# 补 `?? []`。
            var parameterList = lists[i] ?? [];
            var parameters = new SignatureHelpParameter[parameterList.Length];
            var paramDpw = new DisplayPartsWriter(vsCapability);
            paramDpw.WriteFrom(dpw);

            for (var j = 0; j < parameterList.Length; j++)
            {
                var param = parameterList[j];
                var paramNode = Checker.Checker.NewNodeBuilder(c, emitContext).SymbolToParameterDeclaration(param, enclosingDeclaratipn, _signatureHelpNodeBuilderFlags, NodeBuilderInternalFlags.None, null);

                if (j > 0)
                {
                    paramDpw.WritePunctuation(", ");
                }

                // Use a temporary writer for p.Write since the printer calls Clear() on its writer
                var tempDpw = new DisplayPartsWriter(vsCapability);
                p.Write(paramNode!, sourceFile, tempDpw, null);
                var paramLabel = tempDpw.String();
                paramDpw.WriteFrom(tempDpw);

                var parameter = CreateSignatureHelpParameterFromLabel(param, paramLabel, c, docFormat);
                parameters[j] = parameter;
            }

            var closeParen = TokenToString(Kind.CloseParenToken);
            paramDpw.WritePunctuation(closeParen);

            result[i] = new SignatureHelpItemInfo
            {
                _isVariadic = IsVariadicList(parameterList),
                _parameters = parameters,
                _writer = paramDpw,
            };
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) createSignatureHelpParameterFromLabel(parameter, label, c, docFormat) signatureHelpParameter</c>
    /// （signaturehelp.go:669-692）。createSignatureHelpParameterFromLabel creates a signatureHelpParameter from a
    /// pre-computed label string.
    /// </summary>
    private SignatureHelpParameter CreateSignatureHelpParameterFromLabel(Symbol parameter, string label, Checker.Checker c, MarkupKind docFormat)
    {
        var isOptional = (parameter.CheckFlags & CheckFlags.OptionalParameter) != 0;
        var isRest = (parameter.CheckFlags & CheckFlags.RestParameter) != 0;
        StringOrMarkupContent? documentation = null;
        if (parameter.ValueDeclaration is not null)
        {
            var doc = Ls.GetDocumentationFromDeclaration(DocumentationLocationMapper(Feature.SignatureHelp), c, null, parameter.ValueDeclaration, null, docFormat, true /*commentOnly*/);
            if (doc != "")
            {
                documentation = new StringOrMarkupContent
                {
                    MarkupContent = new MarkupContent
                    {
                        Kind = docFormat,
                        Value = doc,
                    },
                };
            }
        }

        return new SignatureHelpParameter
        {
            _parameterInfo = new ParameterInformation
            {
                Label = new StringOrTuple { String = label },
                Documentation = documentation,
            },
            _isRest = isRest,
            _isOptional = isOptional,
        };
    }

    /// <summary>
    /// Go: <c>func (l *LanguageService) createSignatureHelpParameterForParameter(parameter, enclosingDeclaratipn, p, sourceFile, c, docFormat) signatureHelpParameter</c>
    /// （signaturehelp.go:694-697）。
    /// </summary>
    private SignatureHelpParameter CreateSignatureHelpParameterForParameter(Symbol parameter, Node? enclosingDeclaratipn, Printer.Printer p, SourceFile sourceFile, Checker.Checker c, MarkupKind docFormat)
    {
        var display = p.Emit(Checker.Checker.NewNodeBuilder(c, NewEmitContext()).SymbolToParameterDeclaration(parameter, enclosingDeclaratipn, _signatureHelpNodeBuilderFlags, NodeBuilderInternalFlags.None, null)!, sourceFile);
        return CreateSignatureHelpParameterFromLabel(parameter, display, c, docFormat);
    }
}

public static partial class Ls
{
    /// <summary>
    /// Go: <c>SignatureHelpTriggerCharacters</c> / <c>SignatureHelpRetriggerCharacters</c>（signaturehelp.go:24-27）。
    /// The characters that trigger and re-trigger signature help; advertised both in the static server capabilities and
    /// in the dynamic content-mapper registration.
    /// </summary>
    public static readonly string[] SignatureHelpTriggerCharacters = ["(", ",", "<"];

    /// <inheritdoc cref="SignatureHelpTriggerCharacters"/>
    public static readonly string[] SignatureHelpRetriggerCharacters = [")"];

    /// <summary>Go: <c>const signatureHelpNodeBuilderFlags = ...</c>（signaturehelp.go:666）。</summary>
    internal const NodeBuilderFlags _signatureHelpNodeBuilderFlags = NodeBuilderFlags.OmitParameterModifiers | NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope;

    /// <summary>
    /// Go: <c>func createTypeHelpItems(ctx, symbol, argumentInfo, sourceFile, c) *lsproto.SignatureHelp</c>（signaturehelp.go:169-209）。
    /// </summary>
    internal static SignatureHelp? CreateTypeHelpItems(Context ctx, Symbol symbol, ArgumentListInfo argumentInfo, SourceFile sourceFile, Checker.Checker c)
    {
        var typeParameters = c.GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
        if (typeParameters is null)
        {
            return null;
        }

        var item = GetTypeHelpItem(symbol, typeParameters, GetEnclosingDeclarationFromInvocation(argumentInfo._invocation), sourceFile, c);

        // Check client capabilities for activeParameter handling
        var caps = GetClientCapabilities();
        var sigInfoCaps = caps.TextDocument.SignatureHelp.SignatureInformation;
        var supportsPerSignatureActiveParam = sigInfoCaps.ActiveParameterSupport;

        // Converting signatureHelpParameter to *lsproto.ParameterInformation
        var parameters = new ParameterInformation?[item.Parameters.Length];
        for (var i = 0; i < item.Parameters.Length; i++)
        {
            parameters[i] = item.Parameters[i]._parameterInfo;
        }

        var sigInfo = new SignatureInformation
        {
            Label = item.Label,
            Documentation = null,
            Parameters = parameters,
        };

        // If client supports per-signature activeParameter, set it on SignatureInformation
        if (supportsPerSignatureActiveParam && item.Parameters.Length > 0)
        {
            sigInfo.ActiveParameter = new UintegerOrNull { Uinteger = (uint)argumentInfo._argumentIndex };
        }

        var help = new SignatureHelp
        {
            Signatures = [sigInfo],
            ActiveSignature = 0,
        };

        // If client doesn't support per-signature activeParameter, set it on the top-level SignatureHelp
        if (!supportsPerSignatureActiveParam && item.Parameters.Length > 0)
        {
            help.ActiveParameter = new UintegerOrNull { Uinteger = (uint)argumentInfo._argumentIndex };
        }

        return help;
    }

    /// <summary>
    /// Go: <c>func getTypeHelpItem(symbol, typeParameter, enclosingDeclaration, sourceFile, c) signatureInformation</c>
    /// （signaturehelp.go:211-242）。
    /// </summary>
    internal static SignatureInfo GetTypeHelpItem(Symbol symbol, Type[] typeParameter, Node? enclosingDeclaration, SourceFile sourceFile, Checker.Checker c)
    {
        var printer = NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF }, new PrintHandlers(), null);

        var parameters = new SignatureHelpParameter[typeParameter.Length];
        for (var i = 0; i < typeParameter.Length; i++)
        {
            parameters[i] = CreateSignatureHelpParameterForTypeParameter(typeParameter[i], sourceFile, enclosingDeclaration, c, printer);
        }

        // Creating display label
        var displayParts = new StringBuilder();
        displayParts.Append(c.SymbolToString(symbol));
        if (parameters.Length != 0)
        {
            displayParts.Append(TokenToString(Kind.LessThanToken));
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    displayParts.Append(", ");
                }

                displayParts.Append(parameters[i]._parameterInfo.Label.String!);
            }

            displayParts.Append(TokenToString(Kind.GreaterThanToken));
        }

        return new SignatureInfo
        {
            Label = displayParts.ToString(),
            Documentation = null,
            Parameters = parameters,
            IsVariadic = false,
        };
    }

    /// <summary>
    /// Go: <c>func returnTypeToDisplayParts(candidateSignature, c, enclosingDeclaration, sourceFile, vsCapability) *displayPartsWriter</c>
    /// （signaturehelp.go:488-511）。
    /// </summary>
    internal static DisplayPartsWriter ReturnTypeToDisplayParts(Checker.Signature candidateSignature, Checker.Checker c, Node? enclosingDeclaration, SourceFile sourceFile, bool vsCapability)
    {
        var dpw = new DisplayPartsWriter(vsCapability);

        // Add ": " prefix
        dpw.WritePunctuation(": ");

        var predicate = c.GetTypePredicateOfSignature(candidateSignature);
        if (predicate is not null)
        {
            dpw.Write(c.TypePredicateToString(predicate));
        }
        else
        {
            var returnType = c.GetReturnTypeOfSignature(candidateSignature);
            var typeNode = c.TypeToTypeNode(returnType, enclosingDeclaration, _signatureHelpNodeBuilderFlags, null);
            if (typeNode is not null)
            {
                var p = NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF }, new PrintHandlers(), NewEmitContext());
                // Use a temporary writer for p.Write since the printer calls Clear() on its writer
                var tempDpw = new DisplayPartsWriter(vsCapability);
                p.Write(typeNode, sourceFile, tempDpw, null);
                dpw.WriteFrom(tempDpw);
            }
            else
            {
                dpw.Write(c.TypeToString(returnType));
            }
        }

        return dpw;
    }

    /// <summary>
    /// Go: <c>func createSignatureHelpParameterForTypeParameter(t, sourceFile, enclosingDeclaration, c, p) signatureHelpParameter</c>
    /// （signaturehelp.go:699-711）。
    /// </summary>
    internal static SignatureHelpParameter CreateSignatureHelpParameterForTypeParameter(Type t, SourceFile sourceFile, Node? enclosingDeclaration, Checker.Checker c, Printer.Printer p)
    {
        var display = p.Emit(Checker.Checker.NewNodeBuilder(c, NewEmitContext()).TypeParameterToDeclaration(t, enclosingDeclaration, _signatureHelpNodeBuilderFlags, NodeBuilderInternalFlags.None, null)!, sourceFile);
        return new SignatureHelpParameter
        {
            _parameterInfo = new ParameterInformation
            {
                Label = new StringOrTuple { String = display },
            },
            _isRest = false,
            _isOptional = false,
        };
    }

    /// <summary>
    /// Go: <c>func getEnclosingDeclarationFromInvocation(invocation *invocation) *ast.Node</c>（signaturehelp.go:740-748）。
    /// </summary>
    internal static Node? GetEnclosingDeclarationFromInvocation(Invocation invocation)
    {
        if (invocation._callInvocation is not null)
        {
            return invocation._callInvocation._node;
        }
        else if (invocation._typeArgsInvocation is not null)
        {
            return invocation._typeArgsInvocation._called;
        }
        else
        {
            return invocation._contextualInvocation!._node;
        }
    }

    /// <summary>
    /// Go: <c>func getExpressionFromInvocation(argumentInfo *argumentListInfo) *ast.Node</c>（signaturehelp.go:750-755）。
    /// </summary>
    internal static Node? GetExpressionFromInvocation(ArgumentListInfo argumentInfo)
    {
        if (argumentInfo._invocation._callInvocation is not null)
        {
            return GetInvokedExpression(argumentInfo._invocation._callInvocation._node);
        }

        return argumentInfo._invocation._typeArgsInvocation!._called;
    }

    /// <summary>
    /// Go: <c>func getCandidateOrTypeInfo(info, c, sourceFile, startingToken, onlyUseSyntacticOwners) *CandidateOrTypeInfo</c>
    /// （signaturehelp.go:767-826）。
    /// </summary>
    internal static CandidateOrTypeInfo? GetCandidateOrTypeInfo(ArgumentListInfo info, Checker.Checker c, SourceFile sourceFile, Node startingToken, bool onlyUseSyntacticOwners)
    {
        if (info._invocation._callInvocation is not null)
        {
            if (onlyUseSyntacticOwners && !IsSyntacticOwner(startingToken, info._invocation._callInvocation._node, sourceFile))
            {
                return null;
            }

            var (resolvedSignature, candidates) = Checker.Checker.GetResolvedSignatureForSignatureHelp(info._invocation._callInvocation._node, info._argumentCount, c);
            if (candidates.Length == 0)
            {
                return null;
            }

            return new CandidateOrTypeInfo
            {
                _candidateInfo = new CandidateInfo
                {
                    _candidates = candidates,
                    _resolvedSignature = resolvedSignature!,
                },
            };
        }

        if (info._invocation._typeArgsInvocation is not null)
        {
            var called = info._invocation._typeArgsInvocation._called;
            var container = called;
            if (IsIdentifier(called))
            {
                container = called.Parent!;
            }

            if (onlyUseSyntacticOwners && !ContainsPrecedingToken(startingToken, sourceFile, container))
            {
                return null;
            }

            var candidates = GetPossibleGenericSignatures(called, info._argumentCount, c);
            if (candidates.Length != 0)
            {
                return new CandidateOrTypeInfo
                {
                    _candidateInfo = new CandidateInfo
                    {
                        _candidates = candidates,
                        _resolvedSignature = candidates[0],
                    },
                };
            }

            if (c.GetSymbolAtLocation(called) is { } symbol)
            {
                return new CandidateOrTypeInfo
                {
                    _typeInfo = symbol,
                };
            }

            // This can happen in the case of an unresolved symbol.
            return null;
        }

        if (info._invocation._contextualInvocation is not null)
        {
            return new CandidateOrTypeInfo
            {
                _candidateInfo = new CandidateInfo
                {
                    _candidates = [info._invocation._contextualInvocation._signature],
                    _resolvedSignature = info._invocation._contextualInvocation._signature,
                },
            };
        }

        Debug.AssertNever(info._invocation);
        return null;
    }

    /// <summary>
    /// Go: <c>func isSyntacticOwner(startingToken, node *ast.CallLikeExpression, sourceFile) bool</c>（signaturehelp.go:828-841）。
    /// </summary>
    internal static bool IsSyntacticOwner(Node startingToken, Node node, SourceFile sourceFile)
    {
        if (!IsCallOrNewExpression(node))
        {
            return false;
        }

        var invocationChildren = GetChildrenFromNonJSDocNode(node, sourceFile) ?? [];
        switch (startingToken.Kind)
        {
            case Kind.OpenParenToken:
            case Kind.CommaToken:
                return Array.IndexOf(invocationChildren, startingToken) >= 0;
            case Kind.LessThanToken:
                return ContainsPrecedingToken(startingToken, sourceFile, node.Expression()!);
            default:
                return false;
        }
    }

    /// <summary>
    /// Go: <c>func containsPrecedingToken(startingToken, sourceFile, container) bool</c>（signaturehelp.go:843-859）。
    /// </summary>
    internal static bool ContainsPrecedingToken(Node startingToken, SourceFile sourceFile, Node container)
    {
        var pos = startingToken.Pos();
        // There's a possibility that `startingToken.parent` contains only `startingToken` and
        // missing nodes, none of which are valid to be returned by `findPrecedingToken`. In that
        // case, the preceding token we want is actually higher up the tree—almost definitely the
        // next parent, but theoretically the situation with missing nodes might be happening on
        // multiple nested levels.
        var currentParent = startingToken.Parent;
        while (currentParent is not null)
        {
            var precedingToken = FindPrecedingTokenEx(sourceFile, pos, currentParent, true /*excludeJSDoc*/);
            if (precedingToken is not null)
            {
                return RangeContainsRange(container.Loc, precedingToken.Loc);
            }

            currentParent = currentParent.Parent;
        }

        return false;
    }

    /// <summary>
    /// Go: <c>func getContainingArgumentInfo(node, sourceFile, checker, isManuallyInvoked, position) *argumentListInfo</c>
    /// （signaturehelp.go:861-902）。
    /// </summary>
    internal static ArgumentListInfo? GetContainingArgumentInfo(Node node, SourceFile sourceFile, Checker.Checker checker, bool isManuallyInvoked, int position)
    {
        ArgumentListInfo? firstArgumentInfo = null;
        for (var n = node; !IsSourceFile(n) && (isManuallyInvoked || !IsBlock(n)); n = n.Parent!)
        {
            // If the node is not a subspan of its parent, this is a big problem.
            // There have been crashes that might be caused by this violation.
            Debug.Assert(RangeContainsRange(n.Parent!.Loc, n.Loc), "Not a subspan. Child: ", n.KindString(), ", parent: ", n.Parent!.KindString());
            var argumentInfo = GetImmediatelyContainingArgumentOrContextualParameterInfo(n, position, sourceFile, checker);
            if (argumentInfo is not null)
            {
                // For contextual invocations (e.g., arrow functions with contextual types),
                // always return immediately without checking the position.
                // This ensures that when inside a callback's parameter list, we show the callback's
                // signature, not the outer call's signature.
                if (argumentInfo._invocation._contextualInvocation is not null)
                {
                    return argumentInfo;
                }

                // Remember the first (innermost) argument info we find
                firstArgumentInfo ??= argumentInfo;

                // If the position is at the end boundary of an argument list, keep the
                // innermost call. This covers cases like foo(bar("x"|)) where the cursor is
                // still inside the inner invocation, just before its closing paren.
                if (argumentInfo._argumentsSpan.End() == position)
                {
                    return argumentInfo;
                }

                // If any call's span contains the position, return it.
                // We walk from inner to outer, so this naturally prefers the innermost call
                // when multiple calls contain the position.
                if (argumentInfo._argumentsSpan.Contains(position))
                {
                    return argumentInfo;
                }
            }
        }

        // No call's span contains the position. Fall back to the innermost call we found.
        // This covers boundary positions that are still syntactically associated with that
        // invocation, such as being at the end of the argument list or on the close paren.
        return firstArgumentInfo;
    }

    /// <summary>
    /// Go: <c>func getImmediatelyContainingArgumentOrContextualParameterInfo(node, position, sourceFile, checker) *argumentListInfo</c>
    /// （signaturehelp.go:904-910）。
    /// </summary>
    internal static ArgumentListInfo? GetImmediatelyContainingArgumentOrContextualParameterInfo(Node node, int position, SourceFile sourceFile, Checker.Checker checker)
    {
        var result = TryGetParameterInfo(node, sourceFile, checker);
        if (result is null)
        {
            return GetImmediatelyContainingArgumentInfo(node, position, sourceFile, checker);
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func getImmediatelyContainingArgumentInfo(node, position, sourceFile, c) *argumentListInfo</c>（signaturehelp.go:923-1027）。
    /// Returns relevant information for the argument list and the current argument if we are
    /// in the argument of an invocation; returns undefined otherwise.
    /// </summary>
    internal static ArgumentListInfo? GetImmediatelyContainingArgumentInfo(Node node, int position, SourceFile sourceFile, Checker.Checker c)
    {
        var parent = node.Parent;
        if (parent is not null && IsCallOrNewExpression(parent))
        {
            // There are 3 cases to handle:
            //   1. The token introduces a list, and should begin a signature help session
            //   2. The token is either not associated with a list, or ends a list, so the session should end
            //   3. The token is buried inside a list, and should give signature help
            //
            // The following are examples of each:
            //
            //    Case 1:
            //          foo<#T, U>(#a, b)    -> The token introduces a list, and should begin a signature help session
            //    Case 2:
            //          fo#o<T, U>#(a, b)#   -> The token is either not associated with a list, or ends a list, so the session should end
            //    Case 3:
            //          foo<T#, U#>(a#, #b#) -> The token is buried inside a list, and should give signature help
            // Find out if 'node' is an argument, a type argument, or neither
            var info = GetArgumentOrParameterListInfo(node, sourceFile, c);
            if (info is null)
            {
                return null;
            }

            var list = info._list;
            var argumentIndex = info._argumentIndex;
            var argumentCount = info._argumentCount;
            var argumentsSpan = info._argumentsSpan;
            var isTypeParameterList = false;
            var parentTypeArgumentList = parent.TypeArgumentList();
            if (parentTypeArgumentList is not null)
            {
                if (parentTypeArgumentList.Pos() == list!.Pos())
                {
                    isTypeParameterList = true;
                }
            }


            return new ArgumentListInfo
            {
                _isTypeParameterList = isTypeParameterList,
                _invocation = new Invocation { _callInvocation = new CallInvocation { _node = parent } },
                _argumentsSpan = argumentsSpan,
                _argumentIndex = argumentIndex,
                _argumentCount = argumentCount,
            };
        }
        else if (Ls.IsNoSubstitutionTemplateLiteral(node) && Ls.IsTaggedTemplateExpression(parent))
        {
            // Check if we're actually inside the template;
            // otherwise we'll fall out and return undefined.
            if (Ls.IsInsideTemplateLiteral(node, position, sourceFile))
            {
                return GetArgumentListInfoForTemplate(parent!.AsTaggedTemplateExpression()!, 0, sourceFile);
            }

            return null;
        }
        else if (Ls.IsTemplateHead(node) && parent!.Parent!.Kind == Kind.TaggedTemplateExpression)
        {
            var templateExpression = parent.AsTemplateExpression()!;
            var tagExpression = templateExpression.Parent!.AsTaggedTemplateExpression()!;

            var argumentIndex = 1;
            if (Ls.IsInsideTemplateLiteral(node, position, sourceFile))
            {
                argumentIndex = 0;
            }

            return GetArgumentListInfoForTemplate(tagExpression, argumentIndex, sourceFile);
        }
        else if (parent is not null && IsTemplateSpan(parent) && Ls.IsTaggedTemplateExpression(parent.Parent!.Parent))
        {
            var templateSpan = parent;
            var tagExpression = parent.Parent!.Parent!;

            // If we're just after a template tail, don't show signature help.
            if (Ls.IsTemplateTail(node) && !Ls.IsInsideTemplateLiteral(node, position, sourceFile))
            {
                return null;
            }

            var spanIndex = IndexOfNode(templateSpan.Parent!.AsTemplateExpression()!.TemplateSpans!.Nodes ?? [], templateSpan);
            var argumentIndex = GetArgumentIndexForTemplatePiece(spanIndex, node, position, sourceFile);

            return GetArgumentListInfoForTemplate(tagExpression.AsTaggedTemplateExpression()!, argumentIndex, sourceFile);
        }
        else if (parent is not null && IsJsxOpeningLikeElement(parent))
        {
            // Provide a signature help for JSX opening element or JSX self-closing element.
            // This is not guarantee that JSX tag-name is resolved into stateless function component. (that is done in "getSignatureHelpItems")
            // i.e
            //      export function MainButton(props: ButtonProps, context: any): JSX.Element { ... }
            //      <MainButton /*signatureHelp*/
            var attributeSpanStart = parent.Attributes()!.Loc.Pos();
            var attributeSpanEnd = SkipTrivia(sourceFile.Text(), parent.Attributes()!.End());
            return new ArgumentListInfo
            {
                _isTypeParameterList = false,
                _invocation = new Invocation { _callInvocation = new CallInvocation { _node = parent } },
                // ⚠ 与 Go 一致：第二个实参是"长度"而非 end（见文件头第 13 条）。
                _argumentsSpan = NewTextRange(attributeSpanStart, attributeSpanEnd - attributeSpanStart),
                _argumentIndex = 0,
                _argumentCount = 1,
            };
        }
        else
        {
            var typeArgInfo = Ls.GetPossibleTypeArgumentsInfo(node, sourceFile);
            if (typeArgInfo is not null)
            {
                var called = typeArgInfo._called;
                var nTypeArguments = typeArgInfo._nTypeArguments;
                var invoc = new TypeArgsInvocation { _called = called.AsIdentifier()! };
                var argumentRange = NewTextRange(called.Loc.Pos(), node.End());
                return new ArgumentListInfo
                {
                    _isTypeParameterList = true,
                    _invocation = new Invocation
                    {
                        _typeArgsInvocation = invoc,
                    },
                    _argumentsSpan = argumentRange,
                    _argumentIndex = nTypeArguments,
                    _argumentCount = nTypeArguments + 1,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Go: <c>func getArgumentIndexForTemplatePiece(spanIndex, node, position, sourceFile) int</c>（signaturehelp.go:1029-1049）。
    /// spanIndex is either the index for a given template span.
    /// This does not give appropriate results for a NoSubstitutionTemplateLiteral.
    /// </summary>
    internal static int GetArgumentIndexForTemplatePiece(int spanIndex, Node node, int position, SourceFile sourceFile)
    {
        // Because the TemplateStringsArray is the first argument, we have to offset each substitution expression by 1.
        // There are three cases we can encounter:
        //      1. We are precisely in the template literal (argIndex = 0).
        //      2. We are in or to the right of the substitution expression (argIndex = spanIndex + 1).
        //      3. We are directly to the right of the template literal, but because we look for the token on the left,
        //          not enough to put us in the substitution expression; we should consider ourselves part of
        //          the *next* span's expression by offsetting the index (argIndex = (spanIndex + 1) + 1).
        //
        // Example: f  `# abcd $#{#  1 + 1#  }# efghi ${ #"#hello"#  }  #  `
        //              ^       ^ ^       ^   ^          ^ ^      ^     ^
        // Case:        1       1 3       2   1          3 2      2     1
        Debug.Assert(position >= node.Loc.Pos(), "Assumed 'position' could not occur before node.");
        if (IsTemplateLiteralToken(node))
        {
            if (Ls.IsInsideTemplateLiteral(node, position, sourceFile))
            {
                return 0;
            }

            return spanIndex + 2;
        }

        return spanIndex + 1;
    }

    /// <summary>Go: <c>func getAdjustedNode(node *ast.Node) *ast.Node</c>（signaturehelp.go:1051-1065）。</summary>
    internal static Node? GetAdjustedNode(Node node)
    {
        switch (node.Kind)
        {
            case Kind.OpenParenToken:
            case Kind.CommaToken:
                return node;
            default:
                return FindAncestor(node.Parent, n =>
                {
                    if (IsParameterDeclaration(n))
                    {
                        return true;
                    }
                    else if (IsBindingElement(n) || IsObjectBindingPattern(n) || IsArrayBindingPattern(n))
                    {
                        return false;
                    }

                    return false;
                });
        }
    }

    /// <summary>
    /// Go: <c>func getSpreadElementCount(node *ast.SpreadElement, c *checker.Checker) int</c>（signaturehelp.go:1074-1096）。
    /// </summary>
    internal static int GetSpreadElementCount(Node node, Checker.Checker c)
    {
        var spreadType = c.GetTypeAtLocation(node.Expression()!);
        if (Checker.Checker.IsTupleType(spreadType))
        {
            var tupleType = spreadType.Target()?.AsTupleType();
            if (tupleType is null)
            {
                return 0;
            }

            var elementFlags = tupleType.ElementFlags();
            var fixedLength = tupleType.FixedLength();
            if (fixedLength == 0)
            {
                return 0;
            }

            var firstOptionalIndex = FindIndex(elementFlags, f => (f & ElementFlags.Required) == 0);
            if (firstOptionalIndex < 0)
            {
                return fixedLength;
            }

            return firstOptionalIndex;
        }

        return 0;
    }

    /// <summary>
    /// Go: <c>func getArgumentIndex(node *ast.Node, arguments *ast.NodeList, sourceFile *ast.SourceFile, c *checker.Checker) int</c>
    /// （signaturehelp.go:1098-1100）。
    /// </summary>
    internal static int GetArgumentIndex(Node node, NodeList? arguments, SourceFile sourceFile, Checker.Checker c)
    {
        return GetArgumentIndexOrCount(GetTokenFromNodeList(arguments, node.Parent, sourceFile), node, c);
    }

    /// <summary>
    /// Go: <c>func getArgumentCount(node *ast.Node, arguments *ast.NodeList, sourceFile *ast.SourceFile, c *checker.Checker) int</c>
    /// （signaturehelp.go:1102-1104）。
    /// </summary>
    internal static int GetArgumentCount(Node node, NodeList? arguments, SourceFile sourceFile, Checker.Checker c)
    {
        return GetArgumentIndexOrCount(GetTokenFromNodeList(arguments, node.Parent, sourceFile), null, c);
    }

    /// <summary>
    /// Go: <c>func getArgumentIndexOrCount(arguments []*ast.Node, node *ast.Node, c *checker.Checker) int</c>（signaturehelp.go:1106-1146）。
    /// </summary>
    internal static int GetArgumentIndexOrCount(Node[]? arguments, Node? node, Checker.Checker c)
    {
        var argumentIndex = 0;
        var skipComma = false;
        // Go 遍历 nil 切片不迭代 ⇒ C# 先兜成空数组（artifacts/ls-g1-helpers.md §4.3）。
        var args = arguments ?? [];
        foreach (var arg in args)
        {
            if (node is not null && arg == node)
            {
                if (!skipComma && arg.Kind == Kind.CommaToken)
                {
                    argumentIndex++;
                }

                return argumentIndex;
            }

            if (IsSpreadElement(arg))
            {
                argumentIndex += GetSpreadElementCount(arg, c);
                skipComma = true;
                continue;
            }

            if (arg.Kind != Kind.CommaToken)
            {
                argumentIndex++;
                skipComma = true;
                continue;
            }

            if (skipComma)
            {
                skipComma = false;
                continue;
            }

            argumentIndex++;
        }

        if (node is not null)
        {
            return argumentIndex;
        }

        // The argument count for a list is normally the number of non-comma children it has.
        // For example, if you have "Foo(a,b)" then there will be three children of the arg
        // list 'a' '<comma>' 'b'. So, in this case the arg count will be 2. However, there
        // is a small subtlety. If you have "Foo(a,)", then the child list will just have
        // 'a' '<comma>'. So, in the case where the last child is a comma, we increase the
        // arg count by one to compensate.
        var argumentCount = argumentIndex;
        if (args.Length > 0 && args[^1].Kind == Kind.CommaToken)
        {
            argumentCount = argumentIndex + 1;
        }

        return argumentCount;
    }

    /// <summary>
    /// Go: <c>func getArgumentOrParameterListInfo(node *ast.Node, sourceFile *ast.SourceFile, c *checker.Checker) *argumentOrParameterListInfo</c>
    /// （signaturehelp.go:1155-1170）。
    /// </summary>
    internal static ArgumentOrParameterListInfo? GetArgumentOrParameterListInfo(Node node, SourceFile sourceFile, Checker.Checker c)
    {
        var info = GetArgumentOrParameterListAndIndex(node, sourceFile, c);
        if (info is null)
        {
            return null;
        }

        var list = info._list;
        var argumentIndex = info._argumentIndex;
        var argumentCount = GetArgumentCount(node, list, sourceFile, c);
        var argumentsSpan = GetApplicableSpanForArguments(list, node, sourceFile);
        return new ArgumentOrParameterListInfo
        {
            _list = list,
            _argumentIndex = argumentIndex,
            _argumentCount = argumentCount,
            _argumentsSpan = argumentsSpan,
        };
    }

    /// <summary>
    /// Go: <c>func getApplicableSpanForArguments(argumentList *ast.NodeList, node *ast.Node, sourceFile *ast.SourceFile) core.TextRange</c>
    /// （signaturehelp.go:1172-1202）。
    /// </summary>
    internal static TextRange GetApplicableSpanForArguments(NodeList? argumentList, Node node, SourceFile sourceFile)
    {
        // We use full start and skip trivia on the end because we want to include trivia on
        // both sides. For example,
        //
        //    foo(   /*comment */     a, b, c      /*comment*/     )
        //        |                                               |
        //
        // The applicable span is from the first bar to the second bar (inclusive,
        // but not including parentheses).
        if (argumentList is null)
        {
            // If the user has just opened a list, and there are no arguments.
            // For example, foo(    )
            //                  |  |
            // The span should include positions inside the parentheses.
            var spanStart = node.End();
            var spanEnd = SkipTrivia(sourceFile.Text(), node.End());
            spanEnd = EnsureMinimumSpanSize(spanStart, spanEnd);
            return NewTextRange(spanStart, spanEnd);
        }

        var applicableSpanStart = argumentList.Pos();
        var applicableSpanEnd = SkipTrivia(sourceFile.Text(), argumentList.End());

        // If the argument list is empty (Pos == End), extend the span to include at least
        // one position. This handles foo(|) where the cursor is right after the opening paren.
        applicableSpanEnd = EnsureMinimumSpanSize(applicableSpanStart, applicableSpanEnd);

        return NewTextRange(applicableSpanStart, applicableSpanEnd);
    }

    /// <summary>
    /// ensureMinimumSpanSize ensures that a span includes at least one position.
    /// TextRange.Contains uses a half-open interval, so an empty span would not contain
    /// the cursor immediately after typing an opening paren in a call like foo(bar(|)).
    /// Go: <c>func ensureMinimumSpanSize(start, end int) int</c>（signaturehelp.go:1204-1209）。
    /// </summary>
    internal static int EnsureMinimumSpanSize(int start, int end)
    {
        if (end <= start)
        {
            return start + 1;
        }

        return end;
    }

    /// <summary>
    /// Go: <c>func getArgumentOrParameterListAndIndex(node *ast.Node, sourceFile *ast.SourceFile, c *checker.Checker) *argumentOrParameterListAndIndex</c>
    /// （signaturehelp.go:1216-1242）。
    /// </summary>
    internal static ArgumentOrParameterListAndIndex? GetArgumentOrParameterListAndIndex(Node node, SourceFile sourceFile, Checker.Checker c)
    {
        if (node.Kind == Kind.LessThanToken || node.Kind == Kind.OpenParenToken)
        {
            // Find the list that starts right *after* the < or ( token.
            // If the user has just opened a list, consider this item 0.
            var list = GetChildListThatStartsWithOpenerToken(node.Parent!, node);
            return new ArgumentOrParameterListAndIndex
            {
                _list = list,
                _argumentIndex = 0,
            };
        }
        else
        {
            // findListItemInfo can return undefined if we are not in parent's argument list
            // or type argument list. This includes cases where the cursor is:
            //   - To the right of the closing parenthesis, non-substitution template, or template tail.
            //   - Between the type arguments and the arguments (greater than token)
            //   - On the target of the call (parent.func)
            //   - On the 'new' keyword in a 'new' expression
            var list = FindContainingList(node, sourceFile);
            if (list is null)
            {
                return null;
            }

            return new ArgumentOrParameterListAndIndex
            {
                _list = list,
                // Find the index of the argument that contains the node.
                _argumentIndex = GetArgumentIndex(node, list, sourceFile, c),
            };
        }
    }

    /// <summary>
    /// Go: <c>func getChildListThatStartsWithOpenerToken(parent *ast.Node, openerToken *ast.Node) *ast.NodeList</c>
    /// （signaturehelp.go:1244-1259）。
    /// </summary>
    internal static NodeList? GetChildListThatStartsWithOpenerToken(Node parent, Node openerToken)
    {
        if (IsCallExpression(parent))
        {
            var parentCallExpression = parent.AsCallExpression()!;
            if (openerToken.Kind == Kind.LessThanToken)
            {
                return parentCallExpression.TypeArgumentList();
            }

            return parentCallExpression.Arguments;
        }
        else if (IsNewExpression(parent))
        {
            var parentNewExpression = parent.AsNewExpression()!;
            if (openerToken.Kind == Kind.LessThanToken)
            {
                return parentNewExpression.TypeArgumentList();
            }

            return parentNewExpression.Arguments;
        }

        return null;
    }

    /// <summary>
    /// Go: <c>func tryGetParameterInfo(startingToken *ast.Node, sourceFile *ast.SourceFile, c *checker.Checker) *argumentListInfo</c>
    /// （signaturehelp.go:1261-1300）。
    /// </summary>
    internal static ArgumentListInfo? TryGetParameterInfo(Node startingToken, SourceFile sourceFile, Checker.Checker c)
    {
        var node = GetAdjustedNode(startingToken);
        if (node is null)
        {
            return null;
        }

        var info = GetContextualSignatureLocationInfo(node, sourceFile, c);
        if (info is null)
        {
            return null;
        }

        // for optional function condition
        var nonNullableContextualType = c.GetNonNullableType(info._contextualType);
        if (nonNullableContextualType is null)
        {
            return null;
        }

        var symbol = nonNullableContextualType.Symbol();
        if (symbol is null)
        {
            return null;
        }

        var signatures = c.GetSignaturesOfType(nonNullableContextualType, SignatureKind.Call);
        if (signatures.Length == 0)
        {
            return null;
        }

        var signature = signatures[^1];

        var contextualInvocation = new ContextualInvocation
        {
            _signature = signature,
            _node = startingToken,
            _symbol = ChooseBetterSymbol(symbol),
        };
        return new ArgumentListInfo
        {
            _isTypeParameterList = false,
            _invocation = new Invocation { _contextualInvocation = contextualInvocation },
            _argumentsSpan = info._argumentsSpan,
            _argumentIndex = info._argumentIndex,
            _argumentCount = info._argumentCount,
        };
    }

    /// <summary>Go: <c>func chooseBetterSymbol(s *ast.Symbol) *ast.Symbol</c>（signaturehelp.go:1302-1311）。</summary>
    internal static Symbol ChooseBetterSymbol(Symbol s)
    {
        if (s.Name == InternalSymbolNameType)
        {
            foreach (var d in s.Declarations ?? [])
            {
                if (IsFunctionTypeNode(d) && CanHaveSymbol(d.Parent))
                {
                    return d.Parent!.Symbol!;
                }
            }
        }

        return s;
    }

    /// <summary>
    /// Go: <c>func getContextualSignatureLocationInfo(node *ast.Node, sourceFile *ast.SourceFile, c *checker.Checker) *contextualSignatureLocationInfo</c>
    /// （signaturehelp.go:1313-1359）。
    /// </summary>
    internal static ContextualSignatureLocationInfo? GetContextualSignatureLocationInfo(Node node, SourceFile sourceFile, Checker.Checker c)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case Kind.ParenthesizedExpression:
            case Kind.MethodDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
                {
                    var info = GetArgumentOrParameterListInfo(node, sourceFile, c);
                    if (info is null)
                    {
                        return null;
                    }

                    var argumentIndex = info._argumentIndex;
                    var argumentCount = info._argumentCount;
                    var argumentsSpan = info._argumentsSpan;

                    Type? contextualType;
                    if (IsMethodDeclaration(parent))
                    {
                        contextualType = c.GetContextualTypeForObjectLiteralElement(parent, ContextFlags.None);
                    }
                    else
                    {
                        contextualType = c.GetContextualType(parent, ContextFlags.None);
                    }

                    if (contextualType is not null)
                    {
                        return new ContextualSignatureLocationInfo
                        {
                            _contextualType = contextualType,
                            _argumentIndex = argumentIndex,
                            _argumentCount = argumentCount,
                            _argumentsSpan = argumentsSpan,
                        };
                    }

                    return null;
                }

            case Kind.BinaryExpression:
                {
                    var highestBinary = GetHighestBinary(parent.AsBinaryExpression()!);
                    var contextualType = c.GetContextualType(highestBinary, ContextFlags.None);
                    var argumentIndex = 0;
                    if (node.Kind != Kind.OpenParenToken)
                    {
                        argumentIndex = CountBinaryExpressionParameters(parent.AsBinaryExpression()!) - 1;
                        var argumentCount = CountBinaryExpressionParameters(highestBinary);
                        if (contextualType is not null)
                        {
                            return new ContextualSignatureLocationInfo
                            {
                                _contextualType = contextualType,
                                _argumentIndex = argumentIndex,
                                _argumentCount = argumentCount,
                                _argumentsSpan = NewTextRange(parent.Pos(), parent.End()),
                            };
                        }

                        return null;
                    }

                    break;
                }
        }

        return null;
    }

    /// <summary>Go: <c>func getHighestBinary(b *ast.BinaryExpression) *ast.BinaryExpression</c>（signaturehelp.go:1361-1366）。</summary>
    internal static BinaryExpression GetHighestBinary(BinaryExpression b)
    {
        if (b.Parent is not null && IsBinaryExpression(b.Parent))
        {
            return GetHighestBinary(b.Parent.AsBinaryExpression()!);
        }

        return b;
    }

    /// <summary>Go: <c>func countBinaryExpressionParameters(b *ast.BinaryExpression) int</c>（signaturehelp.go:1368-1373）。</summary>
    internal static int CountBinaryExpressionParameters(BinaryExpression b)
    {
        if (b.Left is not null && IsBinaryExpression(b.Left))
        {
            return CountBinaryExpressionParameters(b.Left.AsBinaryExpression()!) + 1;
        }

        return 2;
    }

    /// <summary>
    /// Go: <c>func getTokenFromNodeList(nodeList *ast.NodeList, nodeListParent *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>
    /// （signaturehelp.go:1375-1397）。
    /// </summary>
    internal static Node[]? GetTokenFromNodeList(NodeList? nodeList, Node? nodeListParent, SourceFile sourceFile)
    {
        if (nodeList is null || nodeListParent is null)
        {
            return null;
        }

        var left = nodeList.Pos();
        var nodeListIndex = 0;
        var tokens = new List<Node>();
        var nodes = nodeList.Nodes ?? [];
        while (left < nodeList.End())
        {
            if (nodes.Length > nodeListIndex && left == nodes[nodeListIndex].Pos())
            {
                tokens.Add(nodes[nodeListIndex]);
                left = nodes[nodeListIndex].End();
                nodeListIndex++;
            }
            else
            {
                var scanner = GetScannerForSourceFile(sourceFile, left);
                var token = scanner.Token();
                var tokenFullStart = scanner.TokenFullStart();
                var tokenEnd = scanner.TokenEnd();
                tokens.Add(sourceFile.GetOrCreateToken(token, tokenFullStart, tokenEnd, nodeListParent, scanner.TokenFlags));
                left = tokenEnd;
            }
        }

        return tokens.ToArray();
    }

    /// <summary>
    /// Go: <c>func getArgumentListInfoForTemplate(tagExpression *ast.TaggedTemplateExpression, argumentIndex int, sourceFile *ast.SourceFile) *argumentListInfo</c>
    /// （signaturehelp.go:1399-1415）。
    /// </summary>
    internal static ArgumentListInfo GetArgumentListInfoForTemplate(TaggedTemplateExpression tagExpression, int argumentIndex, SourceFile sourceFile)
    {
        // argumentCount is either 1 or (numSpans + 1) to account for the template strings array argument.
        var argumentCount = 1;
        if (!Ls.IsNoSubstitutionTemplateLiteral(tagExpression.Template))
        {
            argumentCount = (tagExpression.Template!.AsTemplateExpression()!.TemplateSpans!.Nodes ?? []).Length + 1;
        }

        if (argumentIndex != 0)
        {
            Debug.Assert(argumentIndex < argumentCount);
        }

        return new ArgumentListInfo
        {
            _isTypeParameterList = false,
            _invocation = new Invocation { _callInvocation = new CallInvocation { _node = tagExpression } },
            _argumentIndex = argumentIndex,
            _argumentCount = argumentCount,
            _argumentsSpan = GetApplicableRangeForTaggedTemplate(tagExpression, sourceFile),
        };
    }

    /// <summary>
    /// Go: <c>func getApplicableRangeForTaggedTemplate(taggedTemplate *ast.TaggedTemplateExpression, sourceFile *ast.SourceFile) core.TextRange</c>
    /// （signaturehelp.go:1417-1439）。
    /// </summary>
    internal static TextRange GetApplicableRangeForTaggedTemplate(TaggedTemplateExpression taggedTemplate, SourceFile sourceFile)
    {
        var template = taggedTemplate.Template!;
        var applicableSpanStart = GetTokenPosOfNode(template, sourceFile, false);
        var applicableSpanEnd = template.End();

        // We need to adjust the end position for the case where the template does not have a tail.
        // Otherwise, we will not show signature help past the expression.
        // For example,
        //
        //      ` ${ 1 + 1 foo(10)
        //       |       |
        // This is because a Missing node has no width. However, what we actually want is to include trivia
        // leading up to the next token in case the user is about to type in a TemplateMiddle or TemplateTail.
        if (template.Kind == Kind.TemplateExpression)
        {
            var templateSpans = template.AsTemplateExpression()!.TemplateSpans!;
            var lastSpan = templateSpans.Nodes![^1];
            if (lastSpan.AsTemplateSpan()!.Literal!.End() - lastSpan.AsTemplateSpan()!.Literal!.Pos() == 0)
            {
                applicableSpanEnd = SkipTrivia(sourceFile.Text(), applicableSpanEnd);
            }
        }

        // ⚠ 与 Go 一致：第二个实参是"长度"而非 end（见文件头第 13 条），照抄不改。
        return NewTextRange(applicableSpanStart, applicableSpanEnd - applicableSpanStart);
    }
}