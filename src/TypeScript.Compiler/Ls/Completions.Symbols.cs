// ported from: internal/ls/completions.go:2474-4839
//
// 本文件是 `completions.go` 的 **符号/成员/关键字补全** 分节（分节 C，Go 行 2474-4839）。
// 类型声明全部在 `Ls/Completions.cs`（分节 A）里，本文件不重复声明（CS0101/CS0111 规避）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级 var `wordSeparators`（Go 2876）→ `Ls` 的 `private static readonly Set<char> _wordSeparators`
//    （`Collections.NewSetFromItems`，先例 `Ls/Utilities.cs` 的 `_typeKeywords`）。Go 的 set 元素是 rune，
//    但 28 个分隔符全是 ASCII ⇒ `char` 等价（比较点见第 8 条）。
// 2) 包级 `keywordCompletionsCache`（Go 3846-3847）→ `internal static readonly SyncMap<KeywordCompletionFilters,
//    LsProto.CompletionItem[]> KeywordCompletionsCache`；`allKeywordCompletions`（Go 3847-3859，`sync.OnceValue`）
//    → `internal static readonly Lazy<LsProto.CompletionItem[]> AllKeywordCompletions`（`Lazy<T>` 默认
//    ExecutionAndPublication 与 `sync.OnceValue` 同语义）。
// 3) ⚠ `lsproto.CompletionItem` / `lsproto.CompletionList`（协议类型）→ 一律写 `LsProto.CompletionItem` /
//    `LsProto.CompletionList`：本命名空间里有同名**本地**类型 `CompletionItem`（Go 的 `*completions.CompletionItem`，
//    带 `_completionItem`/`Symbol` 字段）与 `CompletionList`（带 `Items`/`ItemDefaults`），同命名空间的类型优先于
//    `using` 导入 ⇒ 裸写 `CompletionItem` 会解析成本地类型。
// 4) `fmt.Sprintf("…")` 的 `panic` → `throw new Exception($"…")`（Go 里这些分支都是不可达的"未知枚举值"防御）。
// 5) `map[K]V` → `Dictionary<K,V>`；`collections.Set[T]{}` → `new Set<T>()`；`[]T`（可 nil）→ `T[]?` + `?? []`。
// 6) `data.symbolToSortTextMap[ast.GetSymbolId(symbol)]`：分节 A 把该 map 落成 `Dictionary<SymbolId, string>`
//    （`SymbolId` 即 `Ast.SymbolId`）⇒ 本文件直接用 `GetSymbolId(symbol)` 作键（`TryGetValue` 与 Go 读缺失键得 "" 等价）。
// 7) 结构体字面量 `presentMemberModifiers{...}` → 对象初始化器；`make([]T, 0, n)` → `new List<T>(n)`；
//    `append` → `List.Add` / `AddRange`。
// 8) Go 的 `utf8.DecodeLastRuneInString`/`DecodeRuneInString` 在 C# 侧用 `System.Text.Rune` 的
//    `DecodeLastFromUtf16`/`DecodeFromUtf16`（本仓文本是 UTF-16，位置也是 UTF-16 下标；见 A 的文件头第 12 条）。
//    `getWordLengthAndStart` 的返回类型 `rune` → `char`（见 §4 的冻结签名；非 BMP 首字符返回 '\0'，
//    与 Go 的 rune 在 `== '@'`/`== '#'`/`== ' '` 三个比较点上的结果一致）。
// 9) Go 的 `strings.CutPrefix/TrimPrefix/TrimSuffix/HasPrefix/HasSuffix` → C# 的
//    `StartsWith(char/string, StringComparison.Ordinal)` / 范围切片 `text[1..]` / `text[..^1]`（逐处保留 Go 的
//    判断顺序，含单字符输入时的空串结果）。
// 10) `ast.SymbolName(x)` / `ast.GetSymbolId(x)` / `ast.SymbolFlagsXxx` → `using static TypeScript.Ast.Ast;` 后裸名。
// 11) Go 的 `printer.PrinterOptions{…}` → 对象初始化器；`printer.PrintHandlers{}` → `new PrintHandlers()`；
//     `change.NewTracker(…)` → `Tracker.NewTracker(…)`；`change.GetFormatCodeSettingsForWriting(…)` →
//     全限定 `TypeScript.LsChange.LsChange.GetFormatCodeSettingsForWriting(…)`（`TypeScript.LsChange` 既是命名空间
//     又有同名静态类，写 `LsChange.X` 会先绑到命名空间 ⇒ 不用 `using static`，见先例 OrganizeImports.cs:23）。
// 12) Go 的 `l.converters` → `_converters!`；`l.GetProgram()` → `GetProgram()!`；
//     `locale.FromContext(ctx)` → `Loc.FromContext(ctx)`（`using Loc = TypeScript.Locale.Locale;`）。
// 13) `checker.SkipAlias`/`IsKnownSymbol`/`IsExternalModuleSymbol`/`GetDeclarationModifierFlagsFromSymbol`
//     是 Checker 的 **static** 方法 ⇒ 写 `CheckerType.SkipAlias(…)` 等。
// 14) Go 的 `ast.*` 谓词里，凡声明成 `Node?` 的（`IsClassLike`/`IsStatement`/`IsTypeNode`/…）直接传可空实参；
//     声明成 `Node` 的（生成的 `IsIdentifier` 等）Go 侧传 nil 会 panic，C# 用 `!` 保持同样的"不检查"语义。
// 15) Go 的 `getLineOfPosition`/`getLineEndOfPosition`/`getSwitchedType`/`isEqualityOperatorKind`/`escapeSnippetText`
//     也已由别的切片落盘（`Ls/Folding.cs`、`Ls/Utilities.cs`、`Ls/Completions.cs`）⇒ 本文件直接调用，不重复声明。
// 16) 深递归入口（`GetScopeNode` 只是沿父链走，`FilterObjectMembersList` 等只做线性遍历）不建大栈线程。
using System;
using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsChange;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Ls.Ls;
using static TypeScript.LsAutoImport.AutoImport;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;
using CheckerType = TypeScript.Checker.Checker;
using Kind = TypeScript.Ast.Kind;
using Loc = TypeScript.Locale.Locale;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeFactory = TypeScript.Ast.NodeFactory;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

public static partial class Ls
{
    // ── 包级 var / const（Go 2876-2878 / 3846-3859）────────────────────────────────

    /// <summary>Go: <c>var wordSeparators</c>（completions.go:2876-2879，Ported from vscode）。</summary>
    internal static readonly Set<char> _wordSeparators = NewSetFromItems<char>(
        '`', '~', '!', '@', '%', '^', '&', '*', '(', ')', '-', '=', '+', '[', '{', ']', '}', '\\', '|',
        ';', ':', '\'', '"', ',', '.', '<', '>', '/', '?');

    /// <summary>Go: <c>var keywordCompletionsCache = collections.SyncMap[KeywordCompletionFilters, []*lsproto.CompletionItem]{}</c>（completions.go:3846-3847）。</summary>
    internal static readonly SyncMap<KeywordCompletionFilters, LsProto.CompletionItem[]> KeywordCompletionsCache = new();

    /// <summary>Go: <c>var allKeywordCompletions = sync.OnceValue(func() []*lsproto.CompletionItem { … })</c>（completions.go:3847-3859）。</summary>
    internal static readonly Lazy<LsProto.CompletionItem[]> AllKeywordCompletions = new(() =>
    {
        var result = new List<LsProto.CompletionItem>((int)Kind.LastKeyword - (int)Kind.FirstKeyword + 1);
        for (var i = Kind.FirstKeyword; i <= Kind.LastKeyword; i++)
        {
            result.Add(new LsProto.CompletionItem
            {
                Label = TokenToString(i),
                Kind = CompletionItemKind.Keyword,
                SortText = SortTextGlobalsOrKeywords,
            });
        }

        return result.ToArray();
    });
}

public sealed partial class LanguageService
{
    // ── getEntryForObjectLiteralMethodCompletion（Go 2474-2497）───────────────────

    /// <summary>Go: <c>func (l *LanguageService) getEntryForObjectLiteralMethodCompletion(ctx, typeChecker, symbol, enclosingDeclaration, file) *symbolOriginInfoObjectLiteralMethod</c>（completions.go:2474-2497）。</summary>
    internal SymbolOriginInfoObjectLiteralMethod? GetEntryForObjectLiteralMethodCompletion(
        Context ctx,
        CheckerType typeChecker,
        Symbol symbol,
        Node enclosingDeclaration,
        SourceFile file)
    {
        var snippetPrinter = CreateSnippetPrinter(
            new PrinterOptions
            {
                RemoveComments = true,
                NewLine = GetNewLineKind(FormatOptions().NewLineCharacter),
                Target = GetProgram()!.Options().GetEmitScriptTarget(),
            },
            null! /*emitContext*/);

        var isSnippet = ClientSupportsItemSnippet(ctx);
        var method = CreateObjectLiteralMethod(snippetPrinter, typeChecker, symbol, enclosingDeclaration, file, isSnippet);
        if (method is null)
        {
            return null;
        }

        var insertText = snippetPrinter.PrintAndFormatNodeWithSettings(
            ctx,
            method,
            file,
            TypeScript.LsChange.LsChange.GetFormatCodeSettingsForWriting(FormatOptions(), file));
        insertText += ",";

        return new SymbolOriginInfoObjectLiteralMethod
        {
            _insertText = insertText,
            _labelDetails = new CompletionItemLabelDetails
            {
                Detail = PrintObjectLiteralMethodLabelDetail(method, file, snippetPrinter._factory),
            },
            _isSnippet = isSnippet,
        };
    }

    // ── createObjectLiteralMethod（Go 2499-2571）─────────────────────────────────

    /// <summary>Go: <c>func (l *LanguageService) createObjectLiteralMethod(snippetPrinter, typeChecker, symbol, enclosingDeclaration, file, isSnippet) *ast.Node</c>（completions.go:2499-2571）。</summary>
    internal Node? CreateObjectLiteralMethod(
        SnippetPrinter snippetPrinter,
        CheckerType typeChecker,
        Symbol symbol,
        Node enclosingDeclaration,
        SourceFile file,
        bool isSnippet)
    {
        var factory = snippetPrinter._factory;
        var emitContext = snippetPrinter._emitContext;

        Node? declaration = FirstOrNil(symbol.Declarations);
        if (!IsObjectLiteralMethodCompletionCandidateDeclaration(declaration))
        {
            return null;
        }

        var effectiveType = typeChecker.GetWidenedType(typeChecker.GetTypeOfSymbolAtLocation(symbol, enclosingDeclaration));
        if ((effectiveType.Flags() & TypeFlags.Union) != 0 && effectiveType.Types().Length < 10)
        {
            effectiveType = typeChecker.GetUnionTypeEx(effectiveType.Types(), UnionReduction.Subtype);
        }

        if ((effectiveType.Flags() & TypeFlags.Union) != 0)
        {
            Type? functionType = null;
            foreach (var unionType in effectiveType.Types())
            {
                if (typeChecker.GetSignaturesOfType(unionType, SignatureKind.Call).Length == 0)
                {
                    continue;
                }

                if (functionType is not null)
                {
                    return null;
                }

                functionType = unionType;
            }

            if (functionType is null)
            {
                return null;
            }

            effectiveType = functionType;
        }

        var signatures = typeChecker.GetSignaturesOfType(effectiveType, SignatureKind.Call);
        if (signatures.Length != 1)
        {
            return null;
        }

        var flags = NodeBuilderFlags.OmitThisParameter;
        if (GetQuotePreference(file, UserPreferences()) == QuotePreferenceSingle)
        {
            flags |= NodeBuilderFlags.UseSingleQuotesForStringLiteralType;
        }

        var typeNode = typeChecker.TypeToTypeNode(effectiveType, enclosingDeclaration, flags, null /*idToSymbol*/);
        if (typeNode is null || typeNode.Kind != Kind.FunctionType)
        {
            return null;
        }

        var functionTypeNode = typeNode.AsFunctionTypeNode()!;
        var parameterNodes = functionTypeNode.Parameters?.Nodes ?? [];
        var parameters = new List<Node>(parameterNodes.Length);
        foreach (var parameter in parameterNodes)
        {
            parameters.Add(factory.NewParameterDeclaration(
                null /*modifiers*/,
                parameter.AsParameterDeclaration()!.DotDotDotToken,
                parameter.Name()!.Clone(factory),
                null /*questionToken*/,
                null /*typeNode*/,
                parameter.AsParameterDeclaration()!.Initializer));
        }

        var body = factory.NewBlock(factory.NewNodeList(null /*nodes*/), true /*multiLine*/);
        if (isSnippet)
        {
            body = CreateSnippetTabStopBody(factory, emitContext);
        }

        return factory.NewMethodDeclaration(
            null /*modifiers*/,
            null /*asteriskToken*/,
            declaration!.Name()!.Clone(factory),
            null /*postfixToken*/,
            null /*typeParameters*/,
            factory.NewNodeList(parameters.ToArray()),
            null /*typeNode*/,
            null /*fullSignature*/,
            body);
    }

    // ── isObjectLiteralMethodCompletionCandidateDeclaration（Go 2573-2583）────────

    /// <summary>Go: <c>func isObjectLiteralMethodCompletionCandidateDeclaration(declaration *ast.Node) bool</c>（completions.go:2573-2583）。</summary>
    internal static bool IsObjectLiteralMethodCompletionCandidateDeclaration(Node? declaration)
    {
        if (declaration is null)
        {
            return false;
        }

        switch (declaration.Kind)
        {
            case Kind.PropertySignature:
            case Kind.PropertyDeclaration:
            case Kind.MethodSignature:
            case Kind.MethodDeclaration:
                return true;
            default:
                return false;
        }
    }

    // ── collectObjectLiteralMethodSymbols（Go 2590-2618）─────────────────────────

    /// <summary>Go: <c>func (l *LanguageService) collectObjectLiteralMethodSymbols(ctx, typeChecker, members, enclosingDeclaration, file) []objectLiteralMethodSymbol</c>（completions.go:2590-2618）。</summary>
    internal ObjectLiteralMethodSymbol[] CollectObjectLiteralMethodSymbols(
        Context ctx,
        CheckerType typeChecker,
        Symbol[] members,
        Node enclosingDeclaration,
        SourceFile file)
    {
        if (IsSourceFileJS(file))
        {
            return [];
        }

        var preferences = UserPreferences();
        List<ObjectLiteralMethodSymbol> methods = [];
        foreach (var member in members)
        {
            if (!IsObjectLiteralMethodSymbol(member))
            {
                continue;
            }

            var (displayName, _) = GetCompletionEntryDisplayNameForSymbol(
                file,
                preferences,
                member,
                null /*origin*/,
                CompletionKind.ObjectPropertyDeclaration,
                false /*isJsxIdentifierExpected*/);
            if (displayName == "")
            {
                continue;
            }

            var entry = GetEntryForObjectLiteralMethodCompletion(ctx, typeChecker, member, enclosingDeclaration, file);
            if (entry is null)
            {
                continue;
            }

            methods.Add(new ObjectLiteralMethodSymbol
            {
                _symbol = member,
                _origin = new SymbolOriginInfo
                {
                    _kind = SymbolOriginInfoKind.ObjectLiteralMethod,
                    _data = entry,
                },
            });
        }

        return methods.ToArray();
    }

    // ── isObjectLiteralMethodSymbol（Go 2620-2622）──────────────────────────────

    /// <summary>Go: <c>func isObjectLiteralMethodSymbol(symbol *ast.Symbol) bool</c>（completions.go:2620-2622）。</summary>
    internal static bool IsObjectLiteralMethodSymbol(Symbol symbol) =>
        (symbol.Flags & (SymbolFlags.Property | SymbolFlags.Method)) != 0;

    // ── printObjectLiteralMethodLabelDetail（Go 2624-2641）──────────────────────

    /// <summary>Go: <c>func (l *LanguageService) printObjectLiteralMethodLabelDetail(method, file, factory) string</c>（completions.go:2624-2641）。</summary>
    internal string PrintObjectLiteralMethodLabelDetail(Node method, SourceFile file, NodeFactory factory)
    {
        var methodDeclaration = method.AsMethodDeclaration()!;
        var methodSignature = factory.NewMethodSignatureDeclaration(
            null /*modifiers*/,
            factory.NewIdentifier(""),
            methodDeclaration.PostfixToken,
            methodDeclaration.TypeParameters,
            methodDeclaration.Parameters,
            methodDeclaration.Type);
        var signaturePrinter = NewPrinter(
            new PrinterOptions
            {
                RemoveComments = true,
                OmitTrailingSemicolon = true,
                NewLine = GetNewLineKind(FormatOptions().NewLineCharacter),
                Target = GetProgram()!.Options().GetEmitScriptTarget(),
            },
            new PrintHandlers(),
            null /*emitContext*/);
        return signaturePrinter.Emit(methodSignature, file);
    }

    // ── getEntryForMemberCompletion（Go 2643-2758）──────────────────────────────

    /// <summary>Go: <c>func (l *LanguageService) getEntryForMemberCompletion(ctx, typeChecker, symbol, name, location, position, contextToken, file) (*memberCompletionEntry, error)</c>（completions.go:2643-2758）。</summary>
    internal (MemberCompletionEntry? Entry, Exception? Error) GetEntryForMemberCompletion(
        Context ctx,
        CheckerType typeChecker,
        Symbol symbol,
        string name,
        Node location,
        int position,
        Node? contextToken,
        SourceFile file)
    {
        var classLikeDeclaration = FindAncestor(location, IsClassLike);
        if (classLikeDeclaration is null)
        {
            return (null, null);
        }

        var (importAdder, err) = CreateImportAdder(ctx, typeChecker, file);
        if (err is not null)
        {
            return (null, err);
        }

        var changeTracker = Tracker.NewTracker(ctx, GetProgram()!.Options(), FormatOptions(), _converters!);
        var fixer = new MissingMemberFixer(
            changeTracker,
            GetProgram()!,
            typeChecker,
            UserPreferences(),
            importAdder,
            Loc.FromContext(ctx));

        var presentModifiers = GetPresentMemberModifiers(contextToken, file, position);
        var isAbstract = (presentModifiers._modifiers & ModifierFlags.Abstract) != 0
            && (classLikeDeclaration.ModifierFlags & ModifierFlags.Abstract) != 0;
        var isSnippet = ClientSupportsItemSnippet(ctx);
        var body = changeTracker.NodeFactory.NewBlock(changeTracker.NodeFactory.NewNodeList(null), true /*multiLine*/);
        if (isSnippet)
        {
            body = CreateSnippetTabStopBody(changeTracker.NodeFactory, changeTracker.EmitContext);
        }

        var nodes = fixer.CreateMemberFromSymbol(symbol, classLikeDeclaration, file, body, PreserveOptionalFlags.Property, isAbstract);
        List<TextEdit> additionalTextEdits = [];
        if (importAdder is not null && importAdder.HasFixes())
        {
            additionalTextEdits.AddRange(importAdder.Edits());
        }

        if (presentModifiers._eraseRange is not null)
        {
            additionalTextEdits.Add(new TextEdit
            {
                Range = presentModifiers._eraseRange,
                NewText = "",
            });
        }

        var modifiers = ModifierFlags.None;
        var completionNodes = new List<Node>(nodes.Length);
        foreach (var node in nodes)
        {
            if (node is null)
            {
                continue;
            }

            if (completionNodes.Count == 0)
            {
                modifiers = node.ModifierFlags;
                if (isAbstract)
                {
                    modifiers |= ModifierFlags.Abstract;
                }

                if (IsClassElement(node) && typeChecker.GetMemberOverrideModifierStatus(classLikeDeclaration, node, symbol) == MemberOverrideStatus.NeedsOverride)
                {
                    modifiers |= ModifierFlags.Override;
                }
            }

            completionNodes.Add(node);
        }

        if (completionNodes.Count == 0)
        {
            return (new MemberCompletionEntry
            {
                _insertText = name,
                _filterText = name,
                _isSnippet = isSnippet,
                _additionalTextEdits = additionalTextEdits.ToArray(),
            }, null);
        }

        var allowedModifiers = modifiers | ModifierFlags.Override | ModifierFlags.Public;
        if ((symbol.Flags & SymbolFlags.Method) != 0)
        {
            allowedModifiers |= ModifierFlags.Async;
        }
        else
        {
            allowedModifiers |= ModifierFlags.Ambient | ModifierFlags.Readonly;
        }

        var allowedAndPresent = presentModifiers._modifiers & allowedModifiers;
        if ((presentModifiers._modifiers & ~allowedModifiers) != 0)
        {
            return (null, null);
        }

        if ((modifiers & ModifierFlags.Protected) != 0 && (allowedAndPresent & ModifierFlags.Public) != 0)
        {
            modifiers &= ~ModifierFlags.Protected;
        }

        if (allowedAndPresent != ModifierFlags.None && (allowedAndPresent & ModifierFlags.Public) == 0)
        {
            modifiers &= ~ModifierFlags.Public;
        }

        modifiers |= allowedAndPresent;
        var newLine = FormatOptions().NewLineCharacter;
        var snippetPrinter = CreateSnippetPrinter(
            new PrinterOptions
            {
                RemoveComments = true,
                NewLine = GetNewLineKind(newLine),
                Target = GetProgram()!.Options().GetEmitScriptTarget(),
            },
            changeTracker.EmitContext);

        Node? decoratedNode = null;
        if (presentModifiers._decorators.Length > 0)
        {
            var lastNodeIndex = completionNodes.Count - 1;
            if (CanHaveDecorators(completionNodes[lastNodeIndex]))
            {
                decoratedNode = completionNodes[lastNodeIndex];
            }
        }

        var texts = new List<string>(completionNodes.Count);
        foreach (var node in completionNodes)
        {
            var modified = ReplaceModifiers(
                changeTracker.NodeFactory,
                node,
                CreateModifierList(
                    changeTracker.NodeFactory,
                    modifiers,
                    node == decoratedNode ? presentModifiers._decorators : null));
            var text = snippetPrinter.PrintAndFormatNodeWithSettings(
                ctx,
                modified,
                file,
                TypeScript.LsChange.LsChange.GetFormatCodeSettingsForWriting(FormatOptions(), file));
            texts.Add(text);
        }

        var insertText = string.Join(newLine, texts);
        if (insertText == "")
        {
            return (null, null);
        }

        return (new MemberCompletionEntry
        {
            _insertText = insertText,
            _filterText = name,
            _isSnippet = isSnippet,
            _additionalTextEdits = additionalTextEdits.ToArray(),
        }, null);
    }

    // ── getPresentMemberModifiers（Go 2766-2817）────────────────────────────────

    /// <summary>Go: <c>func (l *LanguageService) getPresentMemberModifiers(contextToken, file, position) presentMemberModifiers</c>（completions.go:2766-2817）。</summary>
    internal PresentMemberModifiers GetPresentMemberModifiers(Node? contextToken, SourceFile file, int position)
    {
        if (contextToken is null || GetLineOfPosition(file, position) > GetLineOfPosition(file, contextToken.End()))
        {
            return new PresentMemberModifiers();
        }

        var modifiers = ModifierFlags.None;
        List<Node> decorators = [];
        var rangePos = position;
        var rangeEnd = position;

        if (IsPropertyDeclaration(contextToken.Parent!))
        {
            var contextModifierKind = ModifierLikeKind(contextToken);
            if (contextModifierKind == Kind.Unknown)
            {
                return new PresentMemberModifiers();
            }

            var modifierNodes = contextToken.Parent!.ModifierNodes();
            if (modifierNodes is not null && modifierNodes.Length > 0)
            {
                modifiers |= ModifiersToFlags(modifierNodes) & ModifierFlags.Modifier;
                foreach (var modifier in modifierNodes)
                {
                    if (IsDecorator(modifier))
                    {
                        decorators.Add(modifier);
                    }

                    rangePos = Math.Min(rangePos, GetTokenPosOfNode(modifier, file, false /*includeJSDoc*/));
                }
            }

            var contextModifierFlag = ModifierToFlag(contextModifierKind);
            if ((modifiers & contextModifierFlag) == 0)
            {
                modifiers |= contextModifierFlag;
                rangePos = Math.Min(rangePos, GetStartOfNode(contextToken, file, false /*includeJSDoc*/));
            }

            if (contextToken.Parent!.Name() != contextToken)
            {
                rangeEnd = GetStartOfNode(contextToken.Parent!.Name()!, file, false /*includeJSDoc*/);
            }
        }

        Range? eraseRange = null;
        if (rangePos < rangeEnd)
        {
            var (lspRange, fidelity) = CreateLspRangeFromBounds(rangePos, rangeEnd, file);
            if (fidelity.IsExact())
            {
                eraseRange = lspRange;
            }
        }

        return new PresentMemberModifiers
        {
            _modifiers = modifiers,
            _decorators = decorators.ToArray(),
            _eraseRange = eraseRange,
        };
    }

    // ── createImportAdder（Go 2856-2868）───────────────────────────────────────

    /// <summary>Go: <c>func (l *LanguageService) createImportAdder(ctx, typeChecker, file) (autoimport.ImportAdder, error)</c>（completions.go:2856-2868）。</summary>
    internal (ImportAdder? ImportAdder, Exception? Error) CreateImportAdder(Context ctx, CheckerType typeChecker, SourceFile file)
    {
        if (IsDynamicFileName(file.FileName()))
        {
            return (null, null);
        }

        var (view, err) = GetPreparedAutoImportView(file, typeChecker);
        if (err is not null)
        {
            return (null, err);
        }

        if (view is null)
        {
            return (null, null);
        }

        return (NewImportAdder(ctx, GetProgram()!, typeChecker, file, view, FormatOptions(), _converters!, UserPreferences()), null);
    }
}

public static partial class Ls
{
    // ── modifierLikeKind（Go 2819-2833）─────────────────────────────────────────

    /// <summary>Go: <c>func modifierLikeKind(node *ast.Node) ast.Kind</c>（completions.go:2819-2833）。</summary>
    internal static Kind ModifierLikeKind(Node? node)
    {
        if (node is null)
        {
            return Kind.Unknown;
        }

        if (IsModifier(node))
        {
            return node.Kind;
        }

        if (IsIdentifier(node))
        {
            var keywordKind = IdentifierToKeywordKind(node.AsIdentifier()!);
            if (keywordKind != Kind.Unknown && IsModifierKind(keywordKind))
            {
                return keywordKind;
            }
        }

        return Kind.Unknown;
    }

    // ── createModifierList（Go 2835-2845）──────────────────────────────────────

    /// <summary>Go: <c>func createModifierList(factory, flags, decorators) *ast.ModifierList</c>（completions.go:2835-2845）。</summary>
    internal static ModifierList? CreateModifierList(NodeFactory factory, ModifierFlags flags, Node[]? decorators)
    {
        List<Node> nodes = [];
        foreach (var decorator in decorators ?? [])
        {
            nodes.Add(decorator.Clone(factory)!);
        }

        nodes.AddRange(CreateModifiersFromModifierFlags(flags, factory.NewModifier));
        if (nodes.Count == 0)
        {
            return null;
        }

        return factory.NewModifierList(nodes.ToArray());
    }

    // ── createSnippetTabStopBody（Go 2847-2854）────────────────────────────────

    /// <summary>Go: <c>func createSnippetTabStopBody(factory, emitContext) *ast.FunctionBody</c>（completions.go:2847-2854）。</summary>
    internal static Node CreateSnippetTabStopBody(NodeFactory factory, EmitContext emitContext)
    {
        var emptyStatement = factory.NewEmptyStatement();
        emitContext.SetSnippetElement(emptyStatement, new SnippetElement(SnippetKind.TabStop, 0));
        return factory.NewBlock(factory.NewNodeList([emptyStatement]), true /*multiLine*/);
    }

    // ── isRecommendedCompletionMatch（Go 2870-2874）────────────────────────────

    /// <summary>Go: <c>func isRecommendedCompletionMatch(localSymbol, recommendedCompletion, typeChecker) bool</c>（completions.go:2870-2874）。</summary>
    internal static bool IsRecommendedCompletionMatch(Symbol localSymbol, Symbol? recommendedCompletion, CheckerType typeChecker) =>
        localSymbol == recommendedCompletion ||
        (localSymbol.Flags & SymbolFlags.ExportValue) != 0 && typeChecker.GetExportSymbolOfSymbol(localSymbol) == recommendedCompletion;

    // ── getWordLengthAndStart（Go 2883-2904）───────────────────────────────────

    /// <summary>Go: <c>func getWordLengthAndStart(sourceFile, position) (wordLength int, wordStart rune)</c>（completions.go:2883-2904）。
    /// Finds the length and first rune of the word that ends at the given position.
    /// e.g. for <c>"abc def.ghi|jkl"</c>, the word length is 3 and the word start is 'g'。</summary>
    internal static (int WordLength, char WordStart) GetWordLengthAndStart(SourceFile sourceFile, int position)
    {
        // !!! Port other case of vscode's `DEFAULT_WORD_REGEXP` that covers words that start like numbers, e.g. -123.456abcd.
        var text = sourceFile.Text()[..position];
        var totalSize = 0;
        Rune firstRune = default; // Go 的 rune 零值 0
        while (totalSize < text.Length)
        {
            Rune.DecodeLastFromUtf16(text.AsSpan(0, text.Length - totalSize), out var r, out var size);
            if (size == 0)
            {
                // 孤立代理项：Go 的 utf8.DecodeLastRuneInString 返回 (RuneError, 1) ⇒ 这里按 1 个 UTF-16 单元前进。
                size = 1;
                r = Rune.ReplacementChar;
            }

            if ((r.IsAscii && _wordSeparators.Has((char)r.Value)) || Rune.IsWhiteSpace(r))
            {
                break;
            }

            totalSize += size;
            firstRune = r;
        }

        // If word starts with `@`, disregard this first character.
        if (firstRune.Value == '@')
        {
            totalSize -= 1;
            Rune.DecodeFromUtf16(text.AsSpan(text.Length - totalSize), out firstRune, out _);
        }

        return (totalSize, firstRune.IsBmp ? (char)firstRune.Value : '\0');
    }

    // ── trimElementAccess（Go 2906-2917）───────────────────────────────────────

    /// <summary>Go: <c>func trimElementAccess(text string) string</c>（completions.go:2906-2917）。
    /// <c>["ab c"]</c> -&gt; <c>ab c</c>；<c>['ab c']</c> -&gt; <c>ab c</c>；<c>[123]</c> -&gt; <c>123</c>。</summary>
    internal static string TrimElementAccess(string text)
    {
        text = text.StartsWith('[') ? text[1..] : text;
        text = text.EndsWith(']') ? text[..^1] : text;
        if (text.StartsWith('\'') && text.EndsWith('\''))
        {
            text = text[..^1];
            text = text.StartsWith('\'') ? text[1..] : text;
        }

        if (text.StartsWith('"') && text.EndsWith('"'))
        {
            text = text[..^1];
            text = text.StartsWith('"') ? text[1..] : text;
        }

        return text;
    }

    // ── getFilterText（Go 2919-2991）───────────────────────────────────────────

    /// <summary>Go: <c>func getFilterText(file, position, insertText, label, wordStart, dotAccessor) string</c>（completions.go:2919-2991）。
    /// Ported from vscode ts extension: <c>getFilterText</c>。</summary>
    internal static string GetFilterText(
        SourceFile file,
        int position,
        string insertText,
        string label,
        char wordStart,
        string dotAccessor)
    {
        // Private field completion, e.g. label `#bar`.
        if (label.StartsWith('#'))
        {
            var afterLabel = label[1..];
            if (insertText != "")
            {
                if (insertText.StartsWith("this.#", StringComparison.Ordinal))
                {
                    if (wordStart == '#')
                    {
                        // `method() { this.#| }`
                        // `method() { #| }`
                        return "";
                    }

                    // `method() { this.| }`
                    // `method() { | }`
                    return insertText[6..];
                }
            }
            else
            {
                if (wordStart == '#')
                {
                    // `method() { this.#| }`
                    return "";
                }

                // `method() { this.| }`
                // `method() { | }`
                return afterLabel;
            }
        }

        // For `this.` completions, generally don't set the filter text since we don't want them to be overly deprioritized. microsoft/vscode#74164
        if (insertText.StartsWith("this.", StringComparison.Ordinal))
        {
            return "";
        }

        // Handle the case:
        // ```
        // const xyz = { 'ab c': 1 };
        // xyz.ab|
        // ```
        // In which case we want to insert a bracket accessor but should use `.abc` as the filter text instead of
        // the bracketed insert text.
        if (insertText.StartsWith('['))
        {
            return dotAccessor + TrimElementAccess(insertText);
        }

        if (insertText.StartsWith("?.", StringComparison.Ordinal))
        {
            // Handle this case like the case above:
            // ```
            // const xyz = { 'ab c': 1 } | undefined;
            // xyz.ab|
            // ```
            // filterText should be `.ab c` instead of `?.['ab c']`.
            if (insertText.StartsWith("?.[", StringComparison.Ordinal))
            {
                return dotAccessor + TrimElementAccess(insertText[2..]);
            }

            // ```
            // const xyz = { abc: 1 } | undefined;
            // xyz.ab|
            // ```
            // filterText should be `.abc` instead of `?.abc.
            return dotAccessor + insertText[2..];
        }

        // In all other cases, fall back to using the insertText.
        return insertText;
    }

    // ── getDotAccessor（Go 2993-3005）──────────────────────────────────────────

    /// <summary>Go: <c>func getDotAccessor(file, position) string</c>（completions.go:2993-3005）。Ported from vscode's `provideCompletionItems`。</summary>
    internal static string GetDotAccessor(SourceFile file, int position)
    {
        var text = file.Text()[..position];
        var totalSize = 0;
        if (text.EndsWith("?.", StringComparison.Ordinal))
        {
            totalSize += 2;
            return file.Text()[(position - totalSize)..position];
        }

        if (text.EndsWith('.'))
        {
            totalSize += 1;
            return file.Text()[(position - totalSize)..position];
        }

        return "";
    }

    // ── strPtr* / boolToPtr（Go 3007-3026）─────────────────────────────────────

    /// <summary>Go: <c>func strPtrIsEmpty(ptr *string) bool</c>（completions.go:3007-3012）。</summary>
    internal static bool StrPtrIsEmpty(string? ptr) => ptr is null || ptr == "";

    /// <summary>Go: <c>func strPtrTo(v string) *string</c>（completions.go:3014-3019）。</summary>
    internal static string? StrPtrTo(string v) => v == "" ? null : v;

    /// <summary>Go: <c>func boolToPtr(v bool) *bool</c>（completions.go:3021-3026）。</summary>
    internal static bool? BoolToPtr(bool v) => v ? true : null;

    // ── isClassLikeMemberCompletion（Go 3049-3058）─────────────────────────────

    /// <summary>Go: <c>func isClassLikeMemberCompletion(symbol, location, file) bool</c>（completions.go:3049-3058）。</summary>
    internal static bool IsClassLikeMemberCompletion(Symbol symbol, Node location, SourceFile file)
    {
        if (IsInJSFile(location))
        {
            return false;
        }

        var memberFlags = SymbolFlags.ClassMember & SymbolFlags.EnumMemberExcludes;
        return (symbol.Flags & memberFlags) != 0
            && (IsClassLike(location)
                || (location.Parent is not null && location.Parent.Parent is not null
                    && IsClassElement(location.Parent)
                    && location == location.Parent.Name()
                    && GetLastToken(location.Parent, file) == location.Parent.Name()
                    && IsClassLike(location.Parent.Parent))
                || (location.Parent is not null && IsSyntaxList(location) && IsClassLike(location.Parent)));
    }

    // ── symbolAppearsToBeTypeOnly（Go 3060-3064）───────────────────────────────

    /// <summary>Go: <c>func symbolAppearsToBeTypeOnly(symbol, typeChecker) bool</c>（completions.go:3060-3064）。</summary>
    internal static bool SymbolAppearsToBeTypeOnly(Symbol symbol, CheckerType typeChecker)
    {
        var flags = CheckerType.SkipAlias(symbol, typeChecker).CombinedLocalAndExportSymbolFlags();
        return (flags & SymbolFlags.Value) == 0
            && ((symbol.Declarations ?? []).Length == 0
                || !IsInJSFile(symbol.Declarations![0])
                || (flags & SymbolFlags.Type) != 0);
    }

    // ── shouldIncludeSymbol（Go 3066-3156）─────────────────────────────────────

    /// <summary>Go: <c>func shouldIncludeSymbol(symbol, data, closestSymbolDeclaration, file, typeChecker, compilerOptions) bool</c>（completions.go:3066-3156）。</summary>
    internal static bool ShouldIncludeSymbol(
        Symbol symbol,
        CompletionDataData data,
        Node? closestSymbolDeclaration,
        SourceFile file,
        CheckerType typeChecker,
        CompilerOptions compilerOptions)
    {
        var allFlags = symbol.Flags;
        var location = data._location;

        // export = /**/ here we want to get all meanings, so any symbol is ok
        if (location.Parent is not null && IsExportAssignment(location.Parent))
        {
            return true;
        }

        // Filter out variables from their own initializers
        // `const a = /* no 'a' here */`
        if (closestSymbolDeclaration is not null
            && IsVariableDeclaration(closestSymbolDeclaration)
            && symbol.ValueDeclaration == closestSymbolDeclaration)
        {
            return false;
        }

        // Filter out current and latter parameters from defaults
        // `function f(a = /* no 'a' and 'b' here */, b) { }` or
        // `function f<T = /* no 'T' and 'T2' here */>(a: T, b: T2) { }`
        Node? symbolDeclaration = null;
        if (symbol.ValueDeclaration is not null)
        {
            symbolDeclaration = symbol.ValueDeclaration;
        }
        else if ((symbol.Declarations ?? []).Length > 0)
        {
            symbolDeclaration = symbol.Declarations![0];
        }

        if (closestSymbolDeclaration is not null && symbolDeclaration is not null)
        {
            if (IsParameterDeclaration(closestSymbolDeclaration) && IsParameterDeclaration(symbolDeclaration))
            {
                var parameters = closestSymbolDeclaration.Parent!.ParameterList()!;
                if (symbolDeclaration.Pos() >= closestSymbolDeclaration.Pos()
                    && symbolDeclaration.Pos() < parameters.End())
                {
                    return false;
                }
            }
            else if (IsTypeParameterDeclaration(closestSymbolDeclaration) && IsTypeParameterDeclaration(symbolDeclaration))
            {
                if (closestSymbolDeclaration == symbolDeclaration
                    && data._contextToken is not null
                    && data._contextToken.Kind == Kind.ExtendsKeyword)
                {
                    // filter out the directly self-recursive type parameters
                    // `type A<K extends /* no 'K' here*/> = K`
                    return false;
                }

                if (IsInTypeParameterDefault(data._contextToken) && !IsInferTypeNode(closestSymbolDeclaration.Parent!))
                {
                    var typeParameters = closestSymbolDeclaration.Parent!.TypeParameterList();
                    if (typeParameters is not null
                        && symbolDeclaration.Pos() >= closestSymbolDeclaration.Pos()
                        && symbolDeclaration.Pos() < typeParameters.End())
                    {
                        return false;
                    }
                }
            }
        }

        // External modules can have global export declarations that will be
        // available as global keywords in all scopes. But if the external module
        // already has an explicit export and user only wants to use explicit
        // module imports then the global keywords will be filtered out so auto
        // import suggestions will win in the completion.
        var symbolOrigin = CheckerType.SkipAlias(symbol, typeChecker);

        // We only want to filter out the global keywords.
        // Auto Imports are not available for scripts so this conditional is always false.
        if (file.ExternalModuleIndicator is not null
            && compilerOptions.AllowUmdGlobalAccess != Tristate.TSTrue
            && symbol != symbolOrigin
            && data._symbolToSortTextMap.TryGetValue(GetSymbolId(symbol), out var sortText)
            && sortText == SortTextGlobalsOrKeywords
            && symbol.Parent is not null
            && CheckerType.IsExternalModuleSymbol(symbol.Parent))
        {
            return false;
        }

        allFlags |= symbolOrigin.CombinedLocalAndExportSymbolFlags();
        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            allFlags |= typeChecker.GetSymbolFlags(symbol);
        }

        // import m = /**/ <-- It can only access namespace (if typing import = x. this would get member symbols and not namespace)
        if (IsInRightSideOfInternalImportEqualsDeclaration(data._location))
        {
            return (allFlags & SymbolFlags.Namespace) != 0;
        }

        if (data._isTypeOnlyLocation)
        {
            // It's a type, but you can reach it by namespace.type as well.
            return SymbolCanBeReferencedAtTypeLocation(symbol, typeChecker, new Set<SymbolId>());
        }

        // expressions are value space (which includes the value namespaces)
        return (allFlags & SymbolFlags.Value) != 0;
    }

    // ── getCompletionEntryDisplayNameForSymbol（Go 3158-3218）──────────────────

    /// <summary>Go: <c>func getCompletionEntryDisplayNameForSymbol(file, preferences, symbol, origin, completionKind, isJsxIdentifierExpected) (string, bool)</c>（completions.go:3158-3218）。</summary>
    internal static (string DisplayName, bool NeedsConvertPropertyAccess) GetCompletionEntryDisplayNameForSymbol(
        SourceFile file,
        UserPreferences preferences,
        Symbol symbol,
        SymbolOriginInfo? origin,
        CompletionKind completionKind,
        bool isJsxIdentifierExpected)
    {
        if (OriginIsIgnore(origin))
        {
            return ("", false);
        }

        string name;
        if (OriginIncludesSymbolName(origin))
        {
            name = origin!.SymbolName();
        }
        else
        {
            name = SymbolName(symbol);
        }

        if (name == ""
            // If the symbol is external module, don't show it in the completion list
            // (i.e declare module "http" { const x; } | // <= request completion here, "http" should not be there)
            || (symbol.Flags & SymbolFlags.Module) != 0 && StartsWithQuote(name)
            // If the symbol is the internal name of an ES symbol, it is not a valid entry. Internal names for ES symbols start with "__@"
            || CheckerType.IsKnownSymbol(symbol))
        {
            return ("", false);
        }

        var variant = IfElse(isJsxIdentifierExpected, LanguageVariant.JSX, LanguageVariant.Standard);

        // name is a valid identifier or private identifier text
        if (IsIdentifierText(name, variant)
            || symbol.ValueDeclaration is not null && IsPrivateIdentifierClassElementDeclaration(symbol.ValueDeclaration))
        {
            return (name, false);
        }

        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            // Allow non-identifier import/export aliases since we can insert them as string literals
            return (name, true);
        }

        switch (completionKind)
        {
            case CompletionKind.MemberLike:
                if (OriginIsComputedPropertyName(origin))
                {
                    return (origin!.SymbolName(), false);
                }

                return ("", false);
            case CompletionKind.ObjectPropertyDeclaration:
                return (Quote(file, preferences, name), false);
            case CompletionKind.PropertyAccess:
            case CompletionKind.Global:
                // For a 'this.' completion it will be in a global context, but may have a non-identifier name.
                // Don't add a completion for a name starting with a space. See https://github.com/Microsoft/TypeScript/pull/20547
                Rune.DecodeFromUtf16(name, out var ch, out _);
                if (ch.Value == ' ')
                {
                    return ("", false);
                }

                return (name, true);
            case CompletionKind.None:
            case CompletionKind.String:
                return (name, false);
            default:
                throw new Exception($"Unexpected completion kind: {completionKind}");
        }
    }

    // ── originIs*（Go 3220-3254）───────────────────────────────────────────────

    // !!! refactor symbolOriginInfo so that we can tell the difference between flags and the kind of data it has
    /// <summary>Go: <c>func originIsIgnore(origin *symbolOriginInfo) bool</c>（completions.go:3220-3222）。</summary>
    internal static bool OriginIsIgnore(SymbolOriginInfo? origin) =>
        origin is not null && (origin._kind & SymbolOriginInfoKind.Ignore) != 0;

    /// <summary>Go: <c>func originIncludesSymbolName(origin *symbolOriginInfo) bool</c>（completions.go:3224-3226）。</summary>
    internal static bool OriginIncludesSymbolName(SymbolOriginInfo? origin) => OriginIsComputedPropertyName(origin);

    /// <summary>Go: <c>func originIsComputedPropertyName(origin *symbolOriginInfo) bool</c>（completions.go:3228-3230）。</summary>
    internal static bool OriginIsComputedPropertyName(SymbolOriginInfo? origin) =>
        origin is not null && (origin._kind & SymbolOriginInfoKind.ComputedPropertyName) != 0;

    /// <summary>Go: <c>func originIsObjectLiteralMethod(origin *symbolOriginInfo) bool</c>（completions.go:3232-3234）。</summary>
    internal static bool OriginIsObjectLiteralMethod(SymbolOriginInfo? origin) =>
        origin is not null && (origin._kind & SymbolOriginInfoKind.ObjectLiteralMethod) != 0;

    /// <summary>Go: <c>func originIsThisTypeNode(origin *symbolOriginInfo) bool</c>（completions.go:3236-3238）。</summary>
    internal static bool OriginIsThisTypeNode(SymbolOriginInfo? origin) =>
        origin is not null && (origin._kind & SymbolOriginInfoKind.ThisType) != 0;

    /// <summary>Go: <c>func originIsTypeOnlyAlias(origin *symbolOriginInfo) bool</c>（completions.go:3240-3242）。</summary>
    internal static bool OriginIsTypeOnlyAlias(SymbolOriginInfo? origin) =>
        origin is not null && (origin._kind & SymbolOriginInfoKind.TypeOnlyAlias) != 0;

    /// <summary>Go: <c>func originIsSymbolMember(origin *symbolOriginInfo) bool</c>（completions.go:3244-3246）。</summary>
    internal static bool OriginIsSymbolMember(SymbolOriginInfo? origin) =>
        origin is not null && (origin._kind & SymbolOriginInfoKind.SymbolMember) != 0;

    /// <summary>Go: <c>func originIsNullableMember(origin *symbolOriginInfo) bool</c>（completions.go:3248-3250）。</summary>
    internal static bool OriginIsNullableMember(SymbolOriginInfo? origin) =>
        origin is not null && (origin._kind & SymbolOriginInfoKind.Nullable) != 0;

    /// <summary>Go: <c>func originIsPromise(origin *symbolOriginInfo) bool</c>（completions.go:3252-3254）。</summary>
    internal static bool OriginIsPromise(SymbolOriginInfo? origin) =>
        origin is not null && (origin._kind & SymbolOriginInfoKind.Promise) != 0;

    // ── getSourceFromOrigin（Go 3256-3266）─────────────────────────────────────

    /// <summary>Go: <c>func getSourceFromOrigin(origin *symbolOriginInfo) string</c>（completions.go:3256-3266）。</summary>
    internal static string GetSourceFromOrigin(SymbolOriginInfo? origin)
    {
        if (OriginIsThisTypeNode(origin))
        {
            return SourceThisProperty;
        }

        if (OriginIsTypeOnlyAlias(origin))
        {
            return SourceTypeOnlyAlias;
        }

        return "";
    }

    // ── getRelevantTokens（Go 3268-3281）───────────────────────────────────────

    /// <summary>Go: <c>func getRelevantTokens(position int, file *ast.SourceFile) (contextToken, previousToken *ast.Node)</c>（completions.go:3268-3281）。
    /// In a scenarion such as <c>const x = 1 * |</c>, the context and previous tokens are both <c>*</c>。
    /// In <c>const x = 1 * o|</c>, the context token is * and the previous token is <c>o</c>。
    /// <c>contextToken</c> and <c>previousToken</c> can both be nil if we are at the beginning of the file。</summary>
    internal static (Node? ContextToken, Node? PreviousToken) GetRelevantTokens(int position, SourceFile file)
    {
        var previousToken = FindPrecedingToken(file, position);
        if (previousToken is not null
            && position <= previousToken.End()
            && (IsMemberName(previousToken) || IsKeywordKind(previousToken.Kind)))
        {
            var contextToken = FindPrecedingToken(file, previousToken.Pos());
            return (contextToken, previousToken);
        }

        return (previousToken, previousToken);
    }

    // ── isValidTrigger（Go 3283-3316）──────────────────────────────────────────

    /// <summary>Go: <c>func isValidTrigger(file, triggerCharacter, contextToken, position) bool</c>（completions.go:3283-3316）。
    /// "." | '"' | "'" | "`" | "/" | "@" | "&lt;" | "#" | " " | "*"。</summary>
    internal static bool IsValidTrigger(SourceFile file, string triggerCharacter, Node? contextToken, int position)
    {
        switch (triggerCharacter)
        {
            case ".":
            case "@":
                return true;
            case "\"":
            case "'":
            case "`":
                // Only automatically bring up completions if this is an opening quote.
                return contextToken is not null
                    && IsStringLiteralOrTemplate(contextToken)
                    && position == GetStartOfNode(contextToken, file, false /*includeJSDoc*/) + 1;
            case "#":
                return contextToken is not null
                    && IsPrivateIdentifier(contextToken)
                    && GetContainingClass(contextToken) is not null;
            case "<":
                // Opening JSX tag
                return contextToken is not null
                    && contextToken.Kind == Kind.LessThanToken
                    && (!IsBinaryExpression(contextToken.Parent!) || BinaryExpressionMayBeOpenTag(contextToken.Parent!.AsBinaryExpression()!));
            case "/":
                if (contextToken is null)
                {
                    return false;
                }

                if (IsStringLiteralLike(contextToken))
                {
                    return TryGetImportFromModuleSpecifier(contextToken) is not null;
                }

                return contextToken.Kind == Kind.LessThanSlashToken && IsJsxClosingElement(contextToken.Parent!);
            case " ":
                return contextToken is not null && contextToken.Kind == Kind.ImportKeyword && contextToken.Parent!.Kind == Kind.SourceFile;
            case "*":
                return IsPotentiallyValidJSDocSnippetCompletionPosition(file, position);
            default:
                throw new Exception("Unknown trigger character: " + triggerCharacter);
        }
    }

    // ── isStringLiteralOrTemplate（Go 3318-3325）───────────────────────────────

    /// <summary>Go: <c>func isStringLiteralOrTemplate(node *ast.Node) bool</c>（completions.go:3318-3325）。</summary>
    internal static bool IsStringLiteralOrTemplate(Node node)
    {
        switch (node.Kind)
        {
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateExpression:
            case Kind.TaggedTemplateExpression:
                return true;
        }

        return false;
    }

    // ── binaryExpressionMayBeOpenTag（Go 3327-3329）────────────────────────────

    /// <summary>Go: <c>func binaryExpressionMayBeOpenTag(binaryExpression *ast.BinaryExpression) bool</c>（completions.go:3327-3329）。</summary>
    internal static bool BinaryExpressionMayBeOpenTag(BinaryExpression binaryExpression) =>
        NodeIsMissing(binaryExpression.Left);

    // ── isCheckedFile（Go 3331-3333）───────────────────────────────────────────

    /// <summary>Go: <c>func isCheckedFile(file, compilerOptions) bool</c>（completions.go:3331-3333）。</summary>
    internal static bool IsCheckedFile(SourceFile file, CompilerOptions compilerOptions) =>
        !IsSourceFileJS(file) || IsCheckJSEnabledForFile(file, compilerOptions);

    // ── isContextTokenValueLocation（Go 3335-3339）─────────────────────────────

    /// <summary>Go: <c>func isContextTokenValueLocation(contextToken *ast.Node) bool</c>（completions.go:3335-3339）。</summary>
    internal static bool IsContextTokenValueLocation(Node? contextToken) =>
        contextToken is not null
        && ((contextToken.Kind == Kind.TypeOfKeyword
                && (contextToken.Parent!.Kind == Kind.TypeQuery || IsTypeOfExpression(contextToken.Parent!)))
            || (contextToken.Kind == Kind.AssertsKeyword && contextToken.Parent!.Kind == Kind.TypePredicate));

    // ── isPossiblyTypeArgumentPosition（Go 3341-3346）──────────────────────────

    /// <summary>Go: <c>func isPossiblyTypeArgumentPosition(token, sourceFile, typeChecker) bool</c>（completions.go:3341-3346）。</summary>
    internal static bool IsPossiblyTypeArgumentPosition(Node? token, SourceFile sourceFile, CheckerType typeChecker)
    {
        var info = GetPossibleTypeArgumentsInfo(token, sourceFile);
        return info is not null
            && (IsPartOfTypeNode(info._called)
                || GetPossibleGenericSignatures(info._called, info._nTypeArguments, typeChecker).Length != 0
                || IsPossiblyTypeArgumentPosition(info._called, sourceFile, typeChecker));
    }

    // ── isContextTokenTypeLocation（Go 3348-3374）──────────────────────────────

    /// <summary>Go: <c>func isContextTokenTypeLocation(contextToken *ast.Node) bool</c>（completions.go:3348-3374）。</summary>
    internal static bool IsContextTokenTypeLocation(Node? contextToken)
    {
        if (contextToken is not null)
        {
            var parentKind = contextToken.Parent!.Kind;
            switch (contextToken.Kind)
            {
                case Kind.ColonToken:
                    return parentKind == Kind.PropertyDeclaration
                        || parentKind == Kind.PropertySignature
                        || parentKind == Kind.Parameter
                        || parentKind == Kind.VariableDeclaration
                        || IsFunctionLikeKind(parentKind);
                case Kind.EqualsToken:
                    return parentKind == Kind.TypeAliasDeclaration || parentKind == Kind.TypeParameter;
                case Kind.AsKeyword:
                    return parentKind == Kind.AsExpression;
                case Kind.LessThanToken:
                    return parentKind == Kind.TypeReference || parentKind == Kind.TypeAssertionExpression;
                case Kind.ExtendsKeyword:
                    return parentKind == Kind.TypeParameter;
                case Kind.SatisfiesKeyword:
                    return parentKind == Kind.SatisfiesExpression;
                case Kind.OpenBracketToken:
                case Kind.CommaToken:
                    return parentKind == Kind.TupleType;
            }
        }

        return false;
    }

    // ── symbolCanBeReferencedAtTypeLocation（Go 3376-3385）─────────────────────

    /// <summary>Go: <c>func symbolCanBeReferencedAtTypeLocation(symbol, typeChecker, seenModules) bool</c>（completions.go:3376-3385）。
    /// True if symbol is a type or a module containing at least one type。</summary>
    internal static bool SymbolCanBeReferencedAtTypeLocation(Symbol symbol, CheckerType typeChecker, Set<SymbolId> seenModules)
    {
        // Since an alias can be merged with a local declaration, we need to test both the alias and its target.
        // This code used to just test the result of `skipAlias`, but that would ignore any locally introduced meanings.
        return NonAliasCanBeReferencedAtTypeLocation(symbol, typeChecker, seenModules)
            || NonAliasCanBeReferencedAtTypeLocation(
                CheckerType.SkipAlias(symbol.ExportSymbol ?? symbol, typeChecker),
                typeChecker,
                seenModules);
    }

    // ── nonAliasCanBeReferencedAtTypeLocation（Go 3387-3396）───────────────────

    /// <summary>Go: <c>func nonAliasCanBeReferencedAtTypeLocation(symbol, typeChecker, seenModules) bool</c>（completions.go:3387-3396）。</summary>
    internal static bool NonAliasCanBeReferencedAtTypeLocation(Symbol symbol, CheckerType typeChecker, Set<SymbolId> seenModules) =>
        (symbol.Flags & SymbolFlags.Type) != 0 || typeChecker.IsUnknownSymbol(symbol)
        || (symbol.Flags & SymbolFlags.Module) != 0 && seenModules.AddIfAbsent(GetSymbolId(symbol))
            && Some(
                typeChecker.GetExportsOfModule(symbol),
                e => SymbolCanBeReferencedAtTypeLocation(e, typeChecker, seenModules));

    // ── getPropertiesForCompletion（Go 3398-3405）──────────────────────────────

    /// <summary>Go: <c>func getPropertiesForCompletion(t, typeChecker) []*ast.Symbol</c>（completions.go:3398-3405）。
    /// Gets all properties on a type, but if that type is a union of several types,
    /// excludes array-like types or callable/constructable types。</summary>
    internal static Symbol[] GetPropertiesForCompletion(Type t, CheckerType typeChecker)
    {
        if (t.IsUnion())
        {
            return CheckEachDefined(
                typeChecker.GetAllPossiblePropertiesOfTypes(t.Types()),
                "getAllPossiblePropertiesOfTypes() should all be defined.");
        }

        return CheckEachDefined(
            typeChecker.GetApparentProperties(t),
            "getApparentProperties() should all be defined.");
    }

    // ── getLeftMostName（Go 3407-3415）─────────────────────────────────────────

    /// <summary>Go: <c>func getLeftMostName(e *ast.Expression) *ast.IdentifierNode</c>（completions.go:3407-3415）。Given 'a.b.c', returns 'a'。</summary>
    internal static Identifier? GetLeftMostName(Node e)
    {
        if (IsIdentifier(e))
        {
            return e.AsIdentifier();
        }

        if (IsPropertyAccessExpression(e))
        {
            return GetLeftMostName(e.Expression()!);
        }

        return null;
    }

    // ── getFirstSymbolInChain（Go 3417-3434）───────────────────────────────────

    /// <summary>Go: <c>func getFirstSymbolInChain(symbol, enclosingDeclaration, typeChecker) *ast.Symbol</c>（completions.go:3417-3434）。</summary>
    internal static Symbol? GetFirstSymbolInChain(Symbol symbol, Node enclosingDeclaration, CheckerType typeChecker)
    {
        var chain = typeChecker.GetAccessibleSymbolChain(
            symbol,
            enclosingDeclaration,
            SymbolFlags.All, /*meaning*/
            false /*useOnlyExternalAliasing*/);
        if (chain is not null && chain.Length > 0)
        {
            return chain[0];
        }

        if (symbol.Parent is not null)
        {
            if (IsModuleSymbol(symbol.Parent))
            {
                return symbol;
            }

            return GetFirstSymbolInChain(symbol.Parent, enclosingDeclaration, typeChecker);
        }

        return null;
    }

    // ── isModuleSymbol（Go 3436-3438）──────────────────────────────────────────

    /// <summary>Go: <c>func isModuleSymbol(symbol *ast.Symbol) bool</c>（completions.go:3436-3438）。</summary>
    internal static bool IsModuleSymbol(Symbol symbol) =>
        Some(symbol.Declarations, decl => decl.Kind == Kind.SourceFile);

    // ── getNullableSymbolOriginInfoKind（Go 3440-3445）─────────────────────────

    /// <summary>Go: <c>func getNullableSymbolOriginInfoKind(kind, insertQuestionDot) symbolOriginInfoKind</c>（completions.go:3440-3445）。</summary>
    internal static SymbolOriginInfoKind GetNullableSymbolOriginInfoKind(SymbolOriginInfoKind kind, bool insertQuestionDot)
    {
        if (insertQuestionDot)
        {
            kind |= SymbolOriginInfoKind.Nullable;
        }

        return kind;
    }

    // ── isStaticProperty（Go 3447-3453）────────────────────────────────────────

    /// <summary>Go: <c>func isStaticProperty(symbol *ast.Symbol) bool</c>（completions.go:3447-3453）。</summary>
    internal static bool IsStaticProperty(Symbol symbol) =>
        symbol.ValueDeclaration is not null
        && (symbol.ValueDeclaration.ModifierFlags & ModifierFlags.Static) != 0
        && IsClassLike(symbol.ValueDeclaration.Parent);

    // ── getContextualTypeForConditionalExpression（Go 3455-3466）───────────────

    /// <summary>Go: <c>func getContextualTypeForConditionalExpression(conditionalExpr, position, file, typeChecker) *checker.Type</c>（completions.go:3455-3466）。
    /// getContextualTypeForConditionalExpression handles completion within a conditional expression
    /// (ternary operator) by using the parent expression to find the contextual type。</summary>
    internal static Type? GetContextualTypeForConditionalExpression(Node conditionalExpr, int position, SourceFile file, CheckerType typeChecker)
    {
        var argInfo = GetArgumentInfoForCompletions(conditionalExpr, position, file, typeChecker);
        if (argInfo is not null)
        {
            return typeChecker.GetContextualTypeForArgumentAtIndex(argInfo._invocation, argInfo._argumentIndex);
        }

        // Fall through to regular contextual type logic if not in an argument
        var contextualType = typeChecker.GetContextualType(conditionalExpr, ContextFlags.IgnoreNodeInferences);
        if (contextualType is not null)
        {
            return contextualType;
        }

        return typeChecker.GetContextualType(conditionalExpr, ContextFlags.None);
    }

    // ── getContextualType（Go 3468-3555）───────────────────────────────────────

    /// <summary>Go: <c>func getContextualType(previousToken, position, file, typeChecker) *checker.Type</c>（completions.go:3468-3555）。</summary>
    internal static Type? GetContextualType(Node? previousToken, int position, SourceFile file, CheckerType typeChecker)
    {
        var parent = previousToken!.Parent!;
        switch (previousToken!.Kind)
        {
            case Kind.Identifier:
                return GetContextualTypeFromParent(previousToken, typeChecker, ContextFlags.None);
            case Kind.EqualsToken:
                switch (parent.Kind)
                {
                    case Kind.VariableDeclaration:
                        return typeChecker.GetContextualType(parent.Initializer()!, ContextFlags.None);
                    case Kind.BinaryExpression:
                        return typeChecker.GetTypeAtLocation(parent.AsBinaryExpression()!.Left!);
                    case Kind.JsxAttribute:
                        return typeChecker.GetContextualTypeForJsxAttribute(parent);
                    default:
                        return null;
                }
            case Kind.NewKeyword:
                return typeChecker.GetContextualType(parent, ContextFlags.None);
            case Kind.CaseKeyword:
                var caseClause = IsCaseClause(parent) ? parent.AsCaseOrDefaultClause() : null;
                if (caseClause is not null)
                {
                    return GetSwitchedType(caseClause, typeChecker);
                }

                return null;
            case Kind.OpenBraceToken:
                if (IsJsxExpression(parent) && !IsJsxElement(parent.Parent!) && !IsJsxFragment(parent.Parent!))
                {
                    return typeChecker.GetContextualTypeForJsxAttribute(parent.Parent!);
                }

                return null;
            case Kind.OpenBracketToken:
            {
                // When completing after `[` in an array literal (e.g., `[/*here*/]`),
                // we should provide contextual type for the first element
                if (IsArrayLiteralExpression(parent))
                {
                    var contextualArrayType = typeChecker.GetContextualType(parent, ContextFlags.None);
                    if (contextualArrayType is not null)
                    {
                        // Get the type for the first element (index 0)
                        return typeChecker.GetContextualTypeForArrayLiteralAtPosition(contextualArrayType, parent, position);
                    }
                }

                return null;
            }

            case Kind.CloseBracketToken:
                // When completing after `]` (e.g., `[x]/*here*/`), we should not provide a contextual type
                // for the closing bracket token itself. Without this case, CloseBracketToken would fall through
                // to the default case, and if the parent is an array literal, GetContextualType would try to
                // find the token's index in the array elements (returning -1), leading to an out-of-bounds panic
                // in getContextualTypeForElementExpression.
                return null;
            case Kind.QuestionToken:
                // When completing after `?` in a ternary conditional (e.g., `foo(a ? /*here*/)`),
                // we need to look at the parent conditional expression to find the contextual type.
                if (IsConditionalExpression(parent))
                {
                    return GetContextualTypeForConditionalExpression(parent, position, file, typeChecker);
                }

                return null;
            case Kind.ColonToken:
                // When completing after `:` in a ternary conditional (e.g., `foo(a ? b : /*here*/)`),
                // we need to look at the parent conditional expression to find the contextual type.
                // Only handle this if parent is ConditionalExpression, otherwise fall through to default
                // (colons are used in other contexts like object literals, type annotations, etc.)
                if (IsConditionalExpression(parent))
                {
                    return GetContextualTypeForConditionalExpression(parent, position, file, typeChecker);
                }

                break;
            case Kind.CommaToken:
            {
                // When completing after `,` in an array literal (e.g., `[x, /*here*/]`),
                // we should provide contextual type for the element after the comma.
                if (IsArrayLiteralExpression(parent))
                {
                    var contextualArrayType = typeChecker.GetContextualType(parent, ContextFlags.None);
                    if (contextualArrayType is not null)
                    {
                        return typeChecker.GetContextualTypeForArrayLiteralAtPosition(contextualArrayType, parent, position);
                    }

                    return null;
                }

                break;
            }
        }

        // Default case: see if we're in an argument position.
        var argInfo = GetArgumentInfoForCompletions(previousToken, position, file, typeChecker);
        if (argInfo is not null)
        {
            return typeChecker.GetContextualTypeForArgumentAtIndex(argInfo._invocation, argInfo._argumentIndex);
        }

        if (IsEqualityOperatorKind(previousToken.Kind) && IsBinaryExpression(parent) && IsEqualityOperatorKind(parent.AsBinaryExpression()!.OperatorToken!.Kind))
        {
            // completion at `x ===/**/`
            return typeChecker.GetTypeAtLocation(parent.AsBinaryExpression()!.Left!);
        }

        var contextualType = typeChecker.GetContextualType(previousToken, ContextFlags.IgnoreNodeInferences);
        if (contextualType is not null)
        {
            return contextualType;
        }

        return typeChecker.GetContextualType(previousToken, ContextFlags.None);
    }

    // ── isLiteral（Go 3572-3574）───────────────────────────────────────────────

    /// <summary>Go: <c>func isLiteral(t *checker.Type) bool</c>（completions.go:3572-3574）。
    /// We disregard boolean literals for completion purposes。</summary>
    internal static bool IsLiteral(Type t) => t.IsStringLiteral() || t.IsNumberLiteral() || t.IsBigIntLiteral();

    // ── getRecommendedCompletion（Go 3576-3597）────────────────────────────────

    /// <summary>Go: <c>func getRecommendedCompletion(previousToken, contextualType, typeChecker) *ast.Symbol</c>（completions.go:3576-3597）。</summary>
    internal static Symbol? GetRecommendedCompletion(Node previousToken, Type contextualType, CheckerType typeChecker)
    {
        Type[] types;
        if (contextualType.IsUnion())
        {
            types = contextualType.Types();
        }
        else
        {
            types = [contextualType];
        }

        // For a union, return the first one with a recommended completion.
        return FirstNonNil<Type, Symbol?>(types, t =>
        {
            var symbol = t.Symbol();
            // Don't make a recommended completion for an abstract class.
            if (symbol is not null
                && (symbol.Flags & (SymbolFlags.EnumMember | SymbolFlags.Enum | SymbolFlags.Class)) != 0
                && !IsAbstractConstructorSymbol(symbol))
            {
                return GetFirstSymbolInChain(symbol, previousToken, typeChecker);
            }

            return null;
        });
    }

    // ── isAbstractConstructorSymbol（Go 3599-3605）─────────────────────────────

    /// <summary>Go: <c>func isAbstractConstructorSymbol(symbol *ast.Symbol) bool</c>（completions.go:3599-3605）。</summary>
    internal static bool IsAbstractConstructorSymbol(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            var declaration = GetClassLikeDeclarationOfSymbol(symbol);
            return declaration is not null && HasSyntacticModifier(declaration, ModifierFlags.Abstract);
        }

        return false;
    }

    // ── startsWithQuote（Go 3607-3610）─────────────────────────────────────────

    /// <summary>Go: <c>func startsWithQuote(s string) bool</c>（completions.go:3607-3610）。</summary>
    internal static bool StartsWithQuote(string s)
    {
        Rune.DecodeFromUtf16(s.AsSpan(), out var r, out _);
        return r.Value == '"' || r.Value == '\'';
    }

    // ── getClosestSymbolDeclaration（Go 3612-3642）─────────────────────────────

    /// <summary>Go: <c>func getClosestSymbolDeclaration(contextToken, location *ast.Node) *ast.Declaration</c>（completions.go:3612-3642）。</summary>
    internal static Node? GetClosestSymbolDeclaration(Node? contextToken, Node location)
    {
        if (contextToken is null)
        {
            return null;
        }

        var closestDeclaration = FindAncestorOrQuit(contextToken, node =>
        {
            if (IsFunctionBlock(node) || IsArrowFunctionBody(node) || IsBindingPattern(node))
            {
                return FindAncestorResult.Quit;
            }

            if ((IsParameterDeclaration(node) || IsTypeParameterDeclaration(node)) && !IsIndexSignatureDeclaration(node.Parent!))
            {
                return FindAncestorResult.True;
            }

            return FindAncestorResult.False;
        });

        if (closestDeclaration is null)
        {
            closestDeclaration = FindAncestorOrQuit(location, node =>
            {
                if (IsFunctionBlock(node) || IsArrowFunctionBody(node) || IsBindingPattern(node))
                {
                    return FindAncestorResult.Quit;
                }

                if (IsVariableDeclaration(node))
                {
                    return FindAncestorResult.True;
                }

                return FindAncestorResult.False;
            });
        }

        return closestDeclaration;
    }

    // ── isArrowFunctionBody（Go 3644-3649）─────────────────────────────────────

    /// <summary>Go: <c>func isArrowFunctionBody(node *ast.Node) bool</c>（completions.go:3644-3649）。</summary>
    internal static bool IsArrowFunctionBody(Node node) =>
        node.Parent is not null && IsArrowFunction(node.Parent)
            && (node.Parent.Body() == node
                // const a = () => /**/;
                || node.Kind == Kind.EqualsGreaterThanToken);

    // ── isInTypeParameterDefault（Go 3651-3667）────────────────────────────────

    /// <summary>Go: <c>func isInTypeParameterDefault(contextToken *ast.Node) bool</c>（completions.go:3651-3667）。</summary>
    internal static bool IsInTypeParameterDefault(Node? contextToken)
    {
        if (contextToken is null)
        {
            return false;
        }

        var node = contextToken;
        var parent = contextToken.Parent;
        while (parent is not null)
        {
            if (IsTypeParameterDeclaration(parent))
            {
                return parent.AsTypeParameterDeclaration()!.DefaultType == node || node.Kind == Kind.EqualsToken;
            }

            node = parent;
            parent = parent.Parent;
        }

        return false;
    }

    // ── isDeprecated（Go 3669-3672）────────────────────────────────────────────

    /// <summary>Go: <c>func isDeprecated(symbol *ast.Symbol, typeChecker *checker.Checker) bool</c>（completions.go:3669-3672）。</summary>
    internal static bool IsDeprecated(Symbol symbol, CheckerType typeChecker)
    {
        var declarations = CheckerType.SkipAlias(symbol, typeChecker).Declarations ?? [];
        return declarations.Length > 0 && Every(declarations, decl => typeChecker.IsDeprecatedDeclaration(decl));
    }

    // ── quotePropertyName（Go 3709-3718）───────────────────────────────────────

    /// <summary>Go: <c>func quotePropertyName(file, preferences, name) string</c>（completions.go:3709-3718）。</summary>
    internal static string QuotePropertyName(SourceFile file, UserPreferences preferences, string name)
    {
        Rune.DecodeFromUtf16(name.AsSpan(), out var r, out _);
        if (Rune.IsDigit(r))
        {
            return name;
        }

        return Quote(file, preferences, name);
    }

    // ── isStringAndEmptyAnonymousObjectIntersection（Go 3720-3728）─────────────

    /// <summary>Go: <c>func isStringAndEmptyAnonymousObjectIntersection(typeChecker, t) bool</c>（completions.go:3720-3728）。
    /// Checks whether type is <c>string &amp; {}</c>, which is semantically equivalent to string but
    /// is not reduced by the checker as a special case used for supporting string literal completions
    /// for string type。</summary>
    internal static bool IsStringAndEmptyAnonymousObjectIntersection(CheckerType typeChecker, Type t)
    {
        if (!t.IsIntersection())
        {
            return false;
        }

        var types = t.Types();
        return types.Length == 2
            && (AreIntersectedTypesAvoidingStringReduction(typeChecker, types[0], types[1])
                || AreIntersectedTypesAvoidingStringReduction(typeChecker, types[1], types[0]));
    }

    // ── areIntersectedTypesAvoidingStringReduction（Go 3730-3732）──────────────

    /// <summary>Go: <c>func areIntersectedTypesAvoidingStringReduction(typeChecker, t1, t2) bool</c>（completions.go:3730-3732）。</summary>
    internal static bool AreIntersectedTypesAvoidingStringReduction(CheckerType typeChecker, Type t1, Type t2) =>
        t1.IsString() && typeChecker.IsEmptyAnonymousObjectType(t2);

    // ── isNamedImportsOrExports（Go 3738-3740）─────────────────────────────────

    /// <summary>Go: <c>func isNamedImportsOrExports(node *ast.Node) bool</c>（completions.go:3738-3740）。</summary>
    internal static bool IsNamedImportsOrExports(Node node) => IsNamedImports(node) || IsNamedExports(node);

    // ── generateIdentifierForArbitraryString（Go 3742-3780）────────────────────

    /// <summary>Go: <c>func generateIdentifierForArbitraryString(text string) string</c>（completions.go:3742-3780）。</summary>
    internal static string GenerateIdentifierForArbitraryString(string text)
    {
        var needsUnderscore = false;
        var identifier = new StringBuilder();
        var size = 0;

        // Convert "(example, text)" into "_example_text_"
        for (var pos = 0; pos < text.Length; pos += size)
        {
            Rune.DecodeFromUtf16(text.AsSpan(pos), out var ch, out size);
            if (size == 0)
            {
                // 孤立代理项：Go 的 utf8.DecodeRuneInString 返回 (RuneError, 1) ⇒ 这里按 1 个 UTF-16 单元前进。
                size = 1;
                ch = Rune.ReplacementChar;
            }

            bool validChar;
            if (pos == 0)
            {
                validChar = IsIdentifierStart(ch.Value);
            }
            else
            {
                validChar = IsIdentifierPart(ch.Value);
            }

            if (size > 0 && validChar)
            {
                if (needsUnderscore)
                {
                    identifier.Append('_');
                }

                identifier.Append(ch.ToString());
                needsUnderscore = false;
            }
            else
            {
                needsUnderscore = true;
            }
        }

        if (needsUnderscore)
        {
            identifier.Append('_');
        }

        // Default to "_" if the provided text was empty
        var id = identifier.ToString();
        if (id == "")
        {
            return "_";
        }

        return id;
    }

    // ── getCompletionsSymbolKind（Go 3782-3835，Copied from vscode TS extension）──

    /// <summary>Go: <c>func getCompletionsSymbolKind(kind lsutil.ScriptElementKind) lsproto.CompletionItemKind</c>（completions.go:3782-3835）。</summary>
    internal static CompletionItemKind GetCompletionsSymbolKind(ScriptElementKind kind) => kind switch
    {
        ScriptElementKind.PrimitiveType or ScriptElementKind.Keyword => CompletionItemKind.Keyword,
        ScriptElementKind.ConstElement or ScriptElementKind.LetElement or ScriptElementKind.VariableElement
            or ScriptElementKind.LocalVariableElement or ScriptElementKind.Alias or ScriptElementKind.ParameterElement => CompletionItemKind.Variable,
        ScriptElementKind.MemberVariableElement or ScriptElementKind.MemberGetAccessorElement
            or ScriptElementKind.MemberSetAccessorElement => CompletionItemKind.Field,
        ScriptElementKind.FunctionElement or ScriptElementKind.LocalFunctionElement => CompletionItemKind.Function,
        ScriptElementKind.MemberFunctionElement or ScriptElementKind.ConstructSignatureElement
            or ScriptElementKind.CallSignatureElement or ScriptElementKind.IndexSignatureElement => CompletionItemKind.Method,
        ScriptElementKind.EnumElement => CompletionItemKind.Enum,
        ScriptElementKind.EnumMemberElement => CompletionItemKind.EnumMember,
        ScriptElementKind.ModuleElement or ScriptElementKind.ExternalModuleName => CompletionItemKind.Module,
        ScriptElementKind.ClassElement or ScriptElementKind.TypeElement => CompletionItemKind.Class,
        ScriptElementKind.InterfaceElement => CompletionItemKind.Interface,
        ScriptElementKind.Warning => CompletionItemKind.Text,
        ScriptElementKind.ScriptElement => CompletionItemKind.File,
        ScriptElementKind.Directory => CompletionItemKind.Folder,
        ScriptElementKind.String => CompletionItemKind.Constant,
        _ => CompletionItemKind.Property,
    };

    // ── CompareCompletionEntries（Go 3837-3844）────────────────────────────────

    /// <summary>Go: <c>func CompareCompletionEntries(a, b *lsproto.CompletionItem) int</c>（completions.go:3837-3844）。
    /// Editors will use the `sortText` and then fall back to `name` for sorting, but leave ties in response order.
    /// So, it's important that we sort those ties in the order we want them displayed if it matters. We don't
    /// strictly need to sort by name or SortText here since clients are going to do it anyway, but we have to
    /// do the work of comparing them so we can sort those ties appropriately。</summary>
    internal static int CompareCompletionEntries(LsProto.CompletionItem a, LsProto.CompletionItem b)
    {
        var compareStrings = TypeScript.StringUtil.StringUtil.CompareStringsCaseInsensitiveThenSensitive;
        var result = compareStrings(a.SortText!, b.SortText!);
        if (result == TypeScript.StringUtil.StringUtil.ComparisonEqual)
        {
            result = compareStrings(a.Label, b.Label);
        }

        return result;
    }

    // ── cloneItems（Go 3861-3871）──────────────────────────────────────────────

    /// <summary>Go: <c>func cloneItems(items []*lsproto.CompletionItem) []*CompletionItem</c>（completions.go:3861-3871）。
    /// Go 的 <c>itemClone := *item</c>（结构体浅拷贝）→ C# 的逐字段浅拷贝 <see cref="CloneProtocolCompletionItem"/>。</summary>
    internal static CompletionItem[]? CloneItems(LsProto.CompletionItem[]? items)
    {
        if (items is null)
        {
            return null;
        }

        var entries = new CompletionItem[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            entries[i] = new CompletionItem { _completionItem = CloneProtocolCompletionItem(items[i]) };
        }

        return entries;
    }

    /// <summary>Go 的 <c>itemClone := *item</c>（`lsproto.CompletionItem` 是 Go 结构体，浅拷贝共享内部指针）。
    /// ⚠ 生成物 `LsProto.CompletionItem` 是 sealed class ⇒ 逐字段浅拷贝实现同样的语义（引用字段仍共享）。</summary>
    private static LsProto.CompletionItem CloneProtocolCompletionItem(LsProto.CompletionItem item) => new()
    {
        Label = item.Label,
        LabelDetails = item.LabelDetails,
        Kind = item.Kind,
        Tags = item.Tags,
        Detail = item.Detail,
        Documentation = item.Documentation,
        Deprecated = item.Deprecated,
        Preselect = item.Preselect,
        SortText = item.SortText,
        FilterText = item.FilterText,
        InsertText = item.InsertText,
        InsertTextFormat = item.InsertTextFormat,
        InsertTextMode = item.InsertTextMode,
        TextEdit = item.TextEdit,
        TextEditText = item.TextEditText,
        AdditionalTextEdits = item.AdditionalTextEdits,
        CommitCharacters = item.CommitCharacters,
        Command = item.Command,
        Data = item.Data,
    };

    // ── getKeywordCompletions（Go 3873-3890）───────────────────────────────────

    /// <summary>Go: <c>func getKeywordCompletions(keywordFilter KeywordCompletionFilters, filterOutTsOnlyKeywords bool) []*CompletionItem</c>（completions.go:3873-3890）。</summary>
    internal static CompletionItem[]? GetKeywordCompletions(KeywordCompletionFilters keywordFilter, bool filterOutTsOnlyKeywords)
    {
        if (!filterOutTsOnlyKeywords)
        {
            return CloneItems(GetTypescriptKeywordCompletions(keywordFilter));
        }

        var index = (KeywordCompletionFilters)((int)keywordFilter + (int)KeywordCompletionFilters.Last + 1);
        var (cached, ok) = KeywordCompletionsCache.Load(index);
        if (ok)
        {
            return CloneItems(cached);
        }

        var result = Filter(
            GetTypescriptKeywordCompletions(keywordFilter),
            ci => !IsTypeScriptOnlyKeyword(StringToToken(ci.Label)));
        KeywordCompletionsCache.Store(index, result);
        return CloneItems(result);
    }

    // ── getTypescriptKeywordCompletions（Go 3892-3930）─────────────────────────

    /// <summary>Go: <c>func getTypescriptKeywordCompletions(keywordFilter KeywordCompletionFilters) []*lsproto.CompletionItem</c>（completions.go:3892-3930）。</summary>
    internal static LsProto.CompletionItem[] GetTypescriptKeywordCompletions(KeywordCompletionFilters keywordFilter)
    {
        var (cached, ok) = KeywordCompletionsCache.Load(keywordFilter);
        if (ok)
        {
            return cached!;
        }

        var result = Filter(AllKeywordCompletions.Value, entry =>
        {
            var kind = StringToToken(entry.Label);
            switch (keywordFilter)
            {
                case KeywordCompletionFilters.None:
                    return false;
                case KeywordCompletionFilters.All:
                    return IsFunctionLikeBodyKeyword(kind)
                        || kind == Kind.DeclareKeyword
                        || kind == Kind.ModuleKeyword
                        || kind == Kind.TypeKeyword
                        || kind == Kind.NamespaceKeyword
                        || kind == Kind.AbstractKeyword
                        || IsTypeKeyword(kind) && kind != Kind.UndefinedKeyword;
                case KeywordCompletionFilters.FunctionLikeBodyKeywords:
                    return IsFunctionLikeBodyKeyword(kind);
                case KeywordCompletionFilters.ClassElementKeywords:
                    return IsClassMemberCompletionKeyword(kind);
                case KeywordCompletionFilters.InterfaceElementKeywords:
                    return IsInterfaceOrTypeLiteralCompletionKeyword(kind);
                case KeywordCompletionFilters.ConstructorParameterKeywords:
                    return IsParameterPropertyModifier(kind);
                case KeywordCompletionFilters.TypeAssertionKeywords:
                    return IsTypeKeyword(kind) || kind == Kind.ConstKeyword;
                case KeywordCompletionFilters.TypeKeywords:
                    return IsTypeKeyword(kind);
                case KeywordCompletionFilters.TypeKeyword:
                    return kind == Kind.TypeKeyword;
                default:
                    throw new Exception($"Unknown keyword filter: {keywordFilter}");
            }
        });

        KeywordCompletionsCache.Store(keywordFilter, result);
        return result;
    }

    // ── isTypeScriptOnlyKeyword（Go 3932-3965）─────────────────────────────────

    /// <summary>Go: <c>func isTypeScriptOnlyKeyword(kind ast.Kind) bool</c>（completions.go:3932-3965）。</summary>
    internal static bool IsTypeScriptOnlyKeyword(Kind kind) => kind switch
    {
        Kind.AbstractKeyword
            or Kind.AnyKeyword
            or Kind.BigIntKeyword
            or Kind.BooleanKeyword
            or Kind.DeclareKeyword
            or Kind.EnumKeyword
            or Kind.GlobalKeyword
            or Kind.ImplementsKeyword
            or Kind.InferKeyword
            or Kind.InterfaceKeyword
            or Kind.IsKeyword
            or Kind.KeyOfKeyword
            or Kind.ModuleKeyword
            or Kind.NamespaceKeyword
            or Kind.NeverKeyword
            or Kind.NumberKeyword
            or Kind.ObjectKeyword
            or Kind.OverrideKeyword
            or Kind.PrivateKeyword
            or Kind.ProtectedKeyword
            or Kind.PublicKeyword
            or Kind.ReadonlyKeyword
            or Kind.StringKeyword
            or Kind.SymbolKeyword
            or Kind.TypeKeyword
            or Kind.UniqueKeyword
            or Kind.UnknownKeyword => true,
        _ => false,
    };

    // ── isFunctionLikeBodyKeyword（Go 3967-3975）───────────────────────────────

    /// <summary>Go: <c>func isFunctionLikeBodyKeyword(kind ast.Kind) bool</c>（completions.go:3967-3975）。</summary>
    internal static bool IsFunctionLikeBodyKeyword(Kind kind) =>
        kind == Kind.AsyncKeyword
            || kind == Kind.AwaitKeyword
            || kind == Kind.UsingKeyword
            || kind == Kind.AsKeyword
            || kind == Kind.SatisfiesKeyword
            || kind == Kind.TypeKeyword
            || !IsContextualKeyword(kind) && !IsClassMemberCompletionKeyword(kind);

    // ── isClassMemberCompletionKeyword（Go 3977-3985）──────────────────────────

    /// <summary>Go: <c>func isClassMemberCompletionKeyword(kind ast.Kind) bool</c>（completions.go:3977-3985）。</summary>
    internal static bool IsClassMemberCompletionKeyword(Kind kind) => kind switch
    {
        Kind.AbstractKeyword or Kind.AccessorKeyword or Kind.ConstructorKeyword or Kind.GetKeyword
            or Kind.SetKeyword or Kind.AsyncKeyword or Kind.DeclareKeyword or Kind.OverrideKeyword => true,
        _ => IsClassMemberModifier(kind),
    };

    // ── isInterfaceOrTypeLiteralCompletionKeyword（Go 3987-3989）───────────────

    /// <summary>Go: <c>func isInterfaceOrTypeLiteralCompletionKeyword(kind ast.Kind) bool</c>（completions.go:3987-3989）。</summary>
    internal static bool IsInterfaceOrTypeLiteralCompletionKeyword(Kind kind) => kind == Kind.ReadonlyKeyword;

    // ── isContextualKeywordInAutoImportableExpressionSpace（Go 3991-4001）──────

    /// <summary>Go: <c>func isContextualKeywordInAutoImportableExpressionSpace(keyword string) bool</c>（completions.go:3991-4001）。</summary>
    internal static bool IsContextualKeywordInAutoImportableExpressionSpace(string keyword) =>
        keyword == "abstract"
            || keyword == "async"
            || keyword == "await"
            || keyword == "declare"
            || keyword == "module"
            || keyword == "namespace"
            || keyword == "type"
            || keyword == "satisfies"
            || keyword == "as";

    // ── getContextualKeywords（Go 4003-4027）───────────────────────────────────

    /// <summary>Go: <c>func getContextualKeywords(file, contextToken, position) []*lsproto.CompletionItem</c>（completions.go:4003-4027）。</summary>
    internal static LsProto.CompletionItem[] GetContextualKeywords(SourceFile file, Node? contextToken, int position)
    {
        List<LsProto.CompletionItem> entries = [];
        // An `AssertClause` can come after an import declaration:
        //  import * from "foo" |
        //  import "foo" |
        // or after a re-export declaration that has a module specifier:
        //  export { foo } from "foo" |
        // Source: https://tc39.es/proposal-import-assertions/
        if (contextToken is not null)
        {
            var parent = contextToken.Parent!;
            var tokenLine = GetECMALineOfPosition(file, contextToken.End());
            var currentLine = GetECMALineOfPosition(file, position);
            if ((IsImportDeclaration(parent)
                    || IsExportDeclaration(parent) && parent.ModuleSpecifier() is not null)
                && contextToken == parent.ModuleSpecifier()
                && tokenLine == currentLine)
            {
                entries.Add(new LsProto.CompletionItem
                {
                    Label = TokenToString(Kind.AssertKeyword),
                    Kind = CompletionItemKind.Keyword,
                    SortText = SortTextGlobalsOrKeywords,
                });
            }
        }

        return entries.ToArray();
    }

    // ── isMemberCompletionKind（Go 4069-4073）──────────────────────────────────

    /// <summary>Go: <c>func isMemberCompletionKind(kind CompletionKind) bool</c>（completions.go:4069-4073）。</summary>
    internal static bool IsMemberCompletionKind(CompletionKind kind) =>
        kind == CompletionKind.ObjectPropertyDeclaration
            || kind == CompletionKind.MemberLike
            || kind == CompletionKind.PropertyAccess;

    // ── tryGetFunctionLikeBodyCompletionContainer（Go 4075-4092）────────────────

    /// <summary>Go: <c>func tryGetFunctionLikeBodyCompletionContainer(contextToken *ast.Node) *ast.Node</c>（completions.go:4075-4092）。</summary>
    internal static Node? TryGetFunctionLikeBodyCompletionContainer(Node? contextToken)
    {
        if (contextToken is null)
        {
            return null;
        }

        Node? prev = null;
        var container = FindAncestorOrQuit(contextToken, node =>
        {
            if (IsClassLike(node))
            {
                return FindAncestorResult.Quit;
            }

            if (IsFunctionLikeDeclaration(node) && prev == node.Body())
            {
                return FindAncestorResult.True;
            }

            prev = node;
            return FindAncestorResult.False;
        });
        return container;
    }

    // ── computeCommitCharactersAndIsNewIdentifier（Go 4094-4220）───────────────

    /// <summary>Go: <c>func computeCommitCharactersAndIsNewIdentifier(contextToken, file, position) (isNewIdentifierLocation bool, defaultCommitCharacters []string)</c>（completions.go:4094-4220）。</summary>
    internal static (bool IsNewIdentifierLocation, string[] DefaultCommitCharacters) ComputeCommitCharactersAndIsNewIdentifier(
        Node? contextToken,
        SourceFile file,
        int position)
    {
        if (contextToken is null)
        {
            return (false, AllCommitCharacters);
        }

        var containingNodeKind = contextToken.Parent!.Kind;
        var tokenKind = KeywordForNode(contextToken);
        // Previous token may have been a keyword that was converted to an identifier.
        switch (tokenKind)
        {
            case Kind.CommaToken:
                switch (containingNodeKind)
                {
                    // func( a, |
                    // new C(a, |
                    case Kind.CallExpression:
                    case Kind.NewExpression:
                    {
                        var expression = contextToken.Parent!.Expression()!;
                        // func\n(a, |
                        if (GetLineOfPosition(file, expression.End()) != GetLineOfPosition(file, position))
                        {
                            return (true, NoCommaCommitCharacters);
                        }

                        return (true, AllCommitCharacters);
                    }

                    // const x = (a, |
                    case Kind.BinaryExpression:
                        return (true, NoCommaCommitCharacters);
                    // constructor( a, | /* public, protected, private keywords are allowed here, so show completion */
                    // var x: (s: string, list|
                    // const obj = { x, |
                    case Kind.Constructor:
                    case Kind.FunctionType:
                    case Kind.ObjectLiteralExpression:
                        return (true, EmptyCommitCharacters);
                    // [a, |
                    case Kind.ArrayLiteralExpression:
                        return (true, AllCommitCharacters);
                    default:
                        return (false, AllCommitCharacters);
                }

            case Kind.OpenParenToken:
                switch (containingNodeKind)
                {
                    // func( |
                    // new C(a|
                    case Kind.CallExpression:
                    case Kind.NewExpression:
                    {
                        var expression = contextToken.Parent!.Expression()!;
                        // func\n( |
                        if (GetLineOfPosition(file, expression.End()) != GetLineOfPosition(file, position))
                        {
                            return (true, NoCommaCommitCharacters);
                        }

                        return (true, AllCommitCharacters);
                    }

                    // const x = (a|
                    case Kind.ParenthesizedExpression:
                        return (true, NoCommaCommitCharacters);
                    // constructor( |
                    // function F(pred: (a| /* this can become an arrow function, where 'a' is the argument */
                    case Kind.Constructor:
                    case Kind.ParenthesizedType:
                        return (true, EmptyCommitCharacters);
                    default:
                        return (false, AllCommitCharacters);
                }

            case Kind.OpenBracketToken:
                switch (containingNodeKind)
                {
                    // [ |
                    // [ | : string ]
                    // [ | : string ]
                    // [ |    /* this can become an index signature */
                    case Kind.ArrayLiteralExpression:
                    case Kind.IndexSignature:
                    case Kind.TupleType:
                    case Kind.ComputedPropertyName:
                        return (true, AllCommitCharacters);
                    default:
                        return (false, AllCommitCharacters);
                }

            // module |
            // namespace |
            // import |
            case Kind.ModuleKeyword:
            case Kind.NamespaceKeyword:
            case Kind.ImportKeyword:
                return (true, EmptyCommitCharacters);
            case Kind.DotToken:
                switch (containingNodeKind)
                {
                    // module A.|
                    case Kind.ModuleDeclaration:
                        return (true, EmptyCommitCharacters);
                    default:
                        return (false, AllCommitCharacters);
                }

            case Kind.OpenBraceToken:
                switch (containingNodeKind)
                {
                    // class A { |
                    // const obj = { |
                    case Kind.ClassDeclaration:
                    case Kind.ObjectLiteralExpression:
                        return (true, EmptyCommitCharacters);
                    default:
                        return (false, AllCommitCharacters);
                }

            case Kind.EqualsToken:
                switch (containingNodeKind)
                {
                    // const x = a|
                    // x = a|
                    case Kind.VariableDeclaration:
                    case Kind.BinaryExpression:
                        return (true, AllCommitCharacters);
                    default:
                        return (false, AllCommitCharacters);
                }

            case Kind.TemplateHead:
                // `aa ${|
                return (containingNodeKind == Kind.TemplateExpression, AllCommitCharacters);
            case Kind.TemplateMiddle:
                // `aa ${10} dd ${|
                return (containingNodeKind == Kind.TemplateSpan, AllCommitCharacters);
            case Kind.AsyncKeyword:
                // const obj = { async c|()
                // const obj = { async c|
                if (containingNodeKind == Kind.MethodDeclaration || containingNodeKind == Kind.ShorthandPropertyAssignment)
                {
                    return (true, EmptyCommitCharacters);
                }

                return (false, AllCommitCharacters);
            case Kind.AsteriskToken:
                // const obj = { * c|
                if (containingNodeKind == Kind.MethodDeclaration)
                {
                    return (true, EmptyCommitCharacters);
                }

                return (false, AllCommitCharacters);
        }

        if (IsClassMemberCompletionKeyword(tokenKind))
        {
            return (true, EmptyCommitCharacters);
        }

        return (false, AllCommitCharacters);
    }

    // ── keywordForNode（Go 4222-4229）──────────────────────────────────────────

    /// <summary>Go: <c>func keywordForNode(node *ast.Node) ast.Kind</c>（completions.go:4222-4229）。</summary>
    internal static Kind KeywordForNode(Node node)
    {
        if (IsIdentifier(node))
        {
            return IdentifierToKeywordKind(node.AsIdentifier()!);
        }

        return node.Kind;
    }

    // ── getScopeNode（Go 4231-4237）────────────────────────────────────────────

    /// <summary>Go: <c>func getScopeNode(initialToken, position, file) *ast.Node</c>（completions.go:4231-4237）。
    /// Finds the first node that "embraces" the position, so that one may
    /// accurately aggregate locals from the closest containing scope。</summary>
    internal static Node? GetScopeNode(Node? initialToken, int position, SourceFile file)
    {
        var scope = initialToken;
        while (scope is not null && !PositionBelongsToNode(scope, position, file))
        {
            scope = scope.Parent;
        }

        return scope;
    }

    // ── isSnippetScope（Go 4239-4250）──────────────────────────────────────────

    /// <summary>Go: <c>func isSnippetScope(scopeNode *ast.Node) bool</c>（completions.go:4239-4250）。</summary>
    internal static bool IsSnippetScope(Node scopeNode) => scopeNode.Kind switch
    {
        Kind.SourceFile or Kind.TemplateExpression or Kind.JsxExpression or Kind.Block => true,
        _ => IsStatement(scopeNode),
    };

    // ── isProbablyGlobalType（Go 4252-4268）────────────────────────────────────

    /// <summary>Go: <c>func isProbablyGlobalType(t, file, typeChecker) bool</c>（completions.go:4252-4268）。
    /// Determines if a type is exactly the same type resolved by the global 'self', 'global', or 'globalThis'。</summary>
    internal static bool IsProbablyGlobalType(Type t, SourceFile file, CheckerType typeChecker)
    {
        // The type of `self` and `window` is the same in lib.dom.d.ts, but `window` does not exist in
        // lib.webworker.d.ts, so checking against `self` is also a check against `window` when it exists.
        var selfSymbol = typeChecker.GetGlobalSymbol("self", SymbolFlags.Value, null /*diagnostic*/);
        if (selfSymbol is not null && typeChecker.GetTypeOfSymbolAtLocation(selfSymbol, file.AsNode()) == t)
        {
            return true;
        }

        var globalSymbol = typeChecker.GetGlobalSymbol("global", SymbolFlags.Value, null /*diagnostic*/);
        if (globalSymbol is not null && typeChecker.GetTypeOfSymbolAtLocation(globalSymbol, file.AsNode()) == t)
        {
            return true;
        }

        var globalThisSymbol = typeChecker.GetGlobalSymbol("globalThis", SymbolFlags.Value, null /*diagnostic*/);
        if (globalThisSymbol is not null && typeChecker.GetTypeOfSymbolAtLocation(globalThisSymbol, file.AsNode()) == t)
        {
            return true;
        }

        return false;
    }

    // ── tryGetTypeLiteralNode（Go 4270-4288）───────────────────────────────────

    /// <summary>Go: <c>func tryGetTypeLiteralNode(node *ast.Node) *ast.TypeLiteralNodeNode</c>（completions.go:4270-4288）。</summary>
    internal static TypeLiteralNode? TryGetTypeLiteralNode(Node? node)
    {
        if (node is null)
        {
            return null;
        }

        var parent = node.Parent!;
        switch (node.Kind)
        {
            case Kind.OpenBraceToken:
                if (IsTypeLiteralNode(parent))
                {
                    return parent.AsTypeLiteralNode();
                }

                break;
            case Kind.SemicolonToken:
            case Kind.CommaToken:
            case Kind.Identifier:
                if (parent.Kind == Kind.PropertySignature && IsTypeLiteralNode(parent.Parent!))
                {
                    return parent.Parent!.AsTypeLiteralNode();
                }

                break;
        }

        return null;
    }

    // ── getConstraintOfTypeArgumentProperty（Go 4290-4336）─────────────────────

    /// <summary>Go: <c>func getConstraintOfTypeArgumentProperty(node, typeChecker) *checker.Type</c>（completions.go:4290-4336）。</summary>
    internal static Type? GetConstraintOfTypeArgumentProperty(Node? node, CheckerType typeChecker)
    {
        if (node is null)
        {
            return null;
        }

        if (IsTypeNode(node))
        {
            var constraint = typeChecker.GetTypeArgumentConstraint(node);
            if (constraint is not null)
            {
                return constraint;
            }
        }

        var t = GetConstraintOfTypeArgumentProperty(node.Parent, typeChecker);
        if (t is null)
        {
            return null;
        }

        switch (node.Kind)
        {
            case Kind.PropertySignature:
            {
                // Try to get the reparsed node first - we may be in JSDoc.
                var reparsed = GetReparsedNodeForNode(node)!;
                if (reparsed.Symbol is { } symbol)
                {
                    return typeChecker.GetTypeOfPropertyOfContextualType(t, symbol.Name);
                }

                // In some cases, we won't have a corresponding symbol
                // (e.g. JSDoc types that never get re-attached) so we'll use
                // the name as declared by the property as a best-effort.
                if (TryGetTextOfPropertyName(reparsed.Name(), out var name))
                {
                    return typeChecker.GetTypeOfPropertyOfContextualType(t, name);
                }

                return null;
            }

            case Kind.ColonToken:
                if (node.Parent!.Kind == Kind.PropertySignature)
                {
                    // The cursor is at a property value location like `Foo<{ x: | }`.
                    // `t` already refers to the appropriate property type.
                    return t;
                }

                break;
            case Kind.IntersectionType:
            case Kind.TypeLiteral:
            case Kind.UnionType:
                return t;
            case Kind.OpenBracketToken:
                return typeChecker.GetElementTypeOfArrayType(t);
        }

        return null;
    }

    // ── tryGetObjectLikeCompletionContainer（Go 4338-4394）─────────────────────

    /// <summary>Go: <c>func tryGetObjectLikeCompletionContainer(contextToken, position, file) *ast.ObjectLiteralLike</c>（completions.go:4338-4394）。</summary>
    internal static Node? TryGetObjectLikeCompletionContainer(Node? contextToken, int position, SourceFile file)
    {
        if (contextToken is null)
        {
            return null;
        }

        var parent = contextToken.Parent!;
        switch (contextToken.Kind)
        {
            // const x = { |
            // const x = { a: 0, |
            case Kind.OpenBraceToken:
            case Kind.CommaToken:
                if (IsObjectLiteralExpression(parent) || IsObjectBindingPattern(parent))
                {
                    return parent;
                }

                break;
            case Kind.AsteriskToken:
                if (IsMethodDeclaration(parent) && IsObjectLiteralExpression(parent.Parent!))
                {
                    return parent.Parent;
                }

                break;
            case Kind.AsyncKeyword:
                if (IsObjectLiteralExpression(parent.Parent!))
                {
                    return parent.Parent;
                }

                break;
            case Kind.Identifier:
            {
                if (contextToken.Text() == "async" && IsShorthandPropertyAssignment(parent))
                {
                    return parent.Parent;
                }

                if (IsObjectLiteralExpression(parent.Parent!)
                    && (IsSpreadAssignment(parent)
                        || IsShorthandPropertyAssignment(parent)
                            && GetLineOfPosition(file, contextToken.End()) != GetLineOfPosition(file, position)))
                {
                    return parent.Parent;
                }

                var ancestorNode = FindAncestor(parent, IsPropertyAssignment);
                if (ancestorNode is not null && GetLastToken(ancestorNode, file) == contextToken && IsObjectLiteralExpression(ancestorNode.Parent!))
                {
                    return ancestorNode.Parent;
                }

                break;
            }

            default:
            {
                if (parent.Parent is not null && parent.Parent.Parent is not null
                    && (IsMethodDeclaration(parent.Parent)
                        || IsGetAccessorDeclaration(parent.Parent)
                        || IsSetAccessorDeclaration(parent.Parent))
                    && IsObjectLiteralExpression(parent.Parent.Parent))
                {
                    return parent.Parent.Parent;
                }

                if (IsSpreadAssignment(parent) && IsObjectLiteralExpression(parent.Parent!))
                {
                    return parent.Parent;
                }

                var ancestorNode = FindAncestor(parent, IsPropertyAssignment);
                if (contextToken.Kind != Kind.ColonToken
                    && ancestorNode is not null && GetLastToken(ancestorNode, file) == contextToken
                    && IsObjectLiteralExpression(ancestorNode.Parent!))
                {
                    return ancestorNode.Parent;
                }

                break;
            }
        }

        return null;
    }

    // ── tryGetObjectLiteralContextualType（Go 4396-4415）───────────────────────

    /// <summary>Go: <c>func tryGetObjectLiteralContextualType(node, typeChecker) *checker.Type</c>（completions.go:4396-4415）。</summary>
    internal static Type? TryGetObjectLiteralContextualType(ObjectLiteralExpression node, CheckerType typeChecker)
    {
        var t = typeChecker.GetContextualType(node, ContextFlags.None);
        if (t is not null)
        {
            return t;
        }

        var parent = WalkUpParenthesizedExpressions(node.Parent)!;
        if (IsBinaryExpression(parent)
            && parent.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken
            && node == parent.AsBinaryExpression()!.Left!)
        {
            // Object literal is assignment pattern: ({ | } = x)
            return typeChecker.GetTypeAtLocation(parent);
        }

        if (IsExpression(parent))
        {
            // f(() => (({ | })));
            return typeChecker.GetContextualType(parent, ContextFlags.None);
        }

        return null;
    }

    // ── getPropertiesForObjectExpression（Go 4417-4461）────────────────────────

    /// <summary>Go: <c>func getPropertiesForObjectExpression(contextualType, completionsType, obj, typeChecker) []*ast.Symbol</c>（completions.go:4417-4461）。</summary>
    internal static Symbol[] GetPropertiesForObjectExpression(
        Type contextualType,
        Type? completionsType,
        Node obj,
        CheckerType typeChecker)
    {
        var hasCompletionsType = completionsType is not null && completionsType != contextualType;
        Type[] types;
        if (contextualType.IsUnion())
        {
            types = contextualType.Types();
        }
        else
        {
            types = [contextualType];
        }

        var promiseFilteredContextualType = typeChecker.GetUnionType(Filter(types, memberType => typeChecker.GetPromisedTypeOfPromise(memberType) is null));

        Type t;
        if (hasCompletionsType && (completionsType!.Flags() & TypeFlags.AnyOrUnknown) == 0)
        {
            t = typeChecker.GetUnionType([promiseFilteredContextualType, completionsType]);
        }
        else
        {
            t = promiseFilteredContextualType;
        }

        // Filter out members whose only declaration is the object literal itself to avoid
        // self-fulfilling completions like:
        //
        // function f<T>(x: T) {}
        // f({ abc/**/: "" }) // `abc` is a member of `T` but only because it declares itself
        bool HasDeclarationOtherThanSelf(Symbol member)
        {
            if ((member.Declarations ?? []).Length == 0)
            {
                return true;
            }

            return Some(member.Declarations, decl => decl.Parent != obj);
        }

        var properties = GetApparentProperties(t, obj, typeChecker);
        if (t.IsClass() && ContainsNonPublicProperties(properties))
        {
            return [];
        }

        if (hasCompletionsType)
        {
            return Filter(properties, HasDeclarationOtherThanSelf);
        }

        return properties;
    }

    // ── getApparentProperties（Go 4463-4474）───────────────────────────────────

    /// <summary>Go: <c>func getApparentProperties(t, node, typeChecker) []*ast.Symbol</c>（completions.go:4463-4474）。</summary>
    internal static Symbol[] GetApparentProperties(Type t, Node node, CheckerType typeChecker)
    {
        if (!t.IsUnion())
        {
            return typeChecker.GetApparentProperties(t);
        }

        return typeChecker.GetAllPossiblePropertiesOfTypes(Filter(t.Types(), memberType =>
            !((memberType.Flags() & TypeFlags.Primitive) != 0
                || typeChecker.IsArrayLikeType(memberType)
                || typeChecker.IsTypeInvalidDueToUnionDiscriminant(memberType, node)
                || typeChecker.TypeHasCallOrConstructSignatures(memberType)
                || memberType.IsClass() && ContainsNonPublicProperties(typeChecker.GetApparentProperties(memberType)))));
    }

    // ── containsNonPublicProperties（Go 4476-4482）─────────────────────────────

    /// <summary>Go: <c>func containsNonPublicProperties(props []*ast.Symbol) bool</c>（completions.go:4476-4482）。</summary>
    internal static bool ContainsNonPublicProperties(Symbol[] props) =>
        Some(props, p => (CheckerType.GetDeclarationModifierFlagsFromSymbol(p) & ModifierFlags.NonPublicAccessibilityModifier) != 0);

    // ── filterObjectMembersList（Go 4484-4543）─────────────────────────────────

    /// <summary>Go: <c>func filterObjectMembersList(contextualMemberSymbols, existingMembers, file, position, typeChecker) (filteredMembers []*ast.Symbol, spreadMemberNames collections.Set[string])</c>（completions.go:4484-4543）。
    /// Filters out members that are already declared in the object literal or binding pattern.
    /// Also computes the set of existing members declared by spread assignment。</summary>
    internal static (Symbol[] FilteredMembers, Set<string> SpreadMemberNames) FilterObjectMembersList(
        Symbol[] contextualMemberSymbols,
        Node[] existingMembers,
        SourceFile file,
        int position,
        CheckerType typeChecker)
    {
        if (existingMembers.Length == 0)
        {
            return (contextualMemberSymbols, new Set<string>());
        }

        var membersDeclaredBySpreadAssignment = new Set<string>();
        var existingMemberNames = new Set<string>();
        foreach (var member in existingMembers)
        {
            // Ignore omitted expressions for missing members.
            if (member.Kind != Kind.PropertyAssignment
                && member.Kind != Kind.ShorthandPropertyAssignment
                && member.Kind != Kind.BindingElement
                && member.Kind != Kind.MethodDeclaration
                && member.Kind != Kind.GetAccessor
                && member.Kind != Kind.SetAccessor
                && member.Kind != Kind.SpreadAssignment)
            {
                continue;
            }

            // If this is the current item we are editing right now, do not filter it out.
            if (IsCurrentlyEditingNode(member, file, position))
            {
                continue;
            }

            var existingName = "";

            if (IsSpreadAssignment(member))
            {
                SetMemberDeclaredBySpreadAssignment(member, membersDeclaredBySpreadAssignment, typeChecker);
            }
            else if (IsBindingElement(member) && member.PropertyName() is not null)
            {
                // include only identifiers in completion list
                if (member.PropertyName()!.Kind == Kind.Identifier)
                {
                    existingName = member.PropertyName()!.Text();
                }
            }
            else
            {
                // TODO: Account for computed property name
                // NOTE: if one only performs this step when m.name is an identifier,
                // things like '__proto__' are not filtered out.
                var name = GetNameOfDeclaration(member);
                if (name is not null && IsPropertyNameLiteral(name))
                {
                    existingName = name.Text();
                }
            }

            if (existingName != "")
            {
                existingMemberNames.Add(existingName);
            }
        }

        var filteredSymbols = Filter(contextualMemberSymbols, m => !existingMemberNames.Has(m.Name));

        return (filteredSymbols, membersDeclaredBySpreadAssignment);
    }

    // ── isCurrentlyEditingNode（Go 4545-4548）──────────────────────────────────

    /// <summary>Go: <c>func isCurrentlyEditingNode(node, file, position) bool</c>（completions.go:4545-4548）。</summary>
    internal static bool IsCurrentlyEditingNode(Node node, SourceFile file, int position)
    {
        var start = GetStartOfNode(node, file, false /*includeJSDoc*/);
        return start <= position && position <= node.End();
    }

    // ── setMemberDeclaredBySpreadAssignment（Go 4550-4566）─────────────────────

    /// <summary>Go: <c>func setMemberDeclaredBySpreadAssignment(declaration, members, typeChecker)</c>（completions.go:4550-4566）。</summary>
    internal static void SetMemberDeclaredBySpreadAssignment(Node declaration, Set<string> members, CheckerType typeChecker)
    {
        var expression = declaration.Expression()!;
        var symbol = typeChecker.GetSymbolAtLocation(expression);
        Type? t = null;
        if (symbol is not null)
        {
            t = typeChecker.GetTypeOfSymbolAtLocation(symbol, expression);
        }

        Symbol[] properties = [];
        if (t is not null && (t.Flags() & TypeFlags.StructuredType) != 0)
        {
            properties = t.AsStructuredType()!.Properties();
        }

        foreach (var property in properties)
        {
            members.Add(property.Name);
        }
    }

    // ── tryGetConstructorLikeCompletionContainer（Go 4568-4586）────────────────

    /// <summary>Go: <c>func tryGetConstructorLikeCompletionContainer(contextToken) *ast.ConstructorDeclarationNode</c>（completions.go:4568-4586）。
    /// Returns the immediate owning class declaration of a context token,
    /// on the condition that one exists and that the context implies completion should be given。</summary>
    internal static ConstructorDeclaration? TryGetConstructorLikeCompletionContainer(Node? contextToken)
    {
        if (contextToken is null)
        {
            return null;
        }

        var parent = contextToken.Parent!;
        switch (contextToken.Kind)
        {
            case Kind.OpenParenToken:
            case Kind.CommaToken:
                if (IsConstructorDeclaration(parent))
                {
                    return parent.AsConstructorDeclaration();
                }

                return null;
            default:
                if (IsConstructorParameterCompletion(contextToken))
                {
                    return parent.Parent!.AsConstructorDeclaration();
                }

                break;
        }

        return null;
    }

    // ── isConstructorParameterCompletion（Go 4588-4593）────────────────────────

    /// <summary>Go: <c>func isConstructorParameterCompletion(node *ast.Node) bool</c>（completions.go:4588-4593）。</summary>
    internal static bool IsConstructorParameterCompletion(Node node) =>
        node.Parent is not null && IsParameterDeclaration(node.Parent) && IsConstructorDeclaration(node.Parent.Parent!)
            && (IsParameterPropertyModifier(node.Kind) || IsDeclarationName(node));

    // ── tryGetObjectTypeDeclarationCompletionContainer（Go 4595-4690）──────────

    /// <summary>Go: <c>func tryGetObjectTypeDeclarationCompletionContainer(file, contextToken, location, position) *ast.ObjectTypeDeclaration</c>（completions.go:4595-4690）。
    /// Returns the immediate owning class declaration of a context token,
    /// on the condition that one exists and that the context implies completion should be given。</summary>
    internal static Node? TryGetObjectTypeDeclarationCompletionContainer(
        SourceFile file,
        Node? contextToken,
        Node location,
        int position)
    {
        // class c { method() { } | method2() { } }
        switch (location.Kind)
        {
            case Kind.SyntaxList:
                if (IsObjectTypeDeclaration(location.Parent))
                {
                    return location.Parent;
                }

                return null;
            case Kind.EndOfFile:
            {
                var stmtList = location.Parent!.StatementList();
                var stmtNodes = stmtList?.Nodes ?? [];
                if (stmtList is not null && stmtNodes.Length > 0 && IsObjectTypeDeclaration(stmtNodes[^1]))
                {
                    var cls = stmtNodes[^1];
                    if (FindChildOfKind(cls, Kind.CloseBraceToken, file) is null)
                    {
                        return cls;
                    }
                }

                break;
            }

            case Kind.PrivateIdentifier:
                if (IsPropertyDeclaration(location.Parent!))
                {
                    return FindAncestor(location, IsClassLike);
                }

                break;
            case Kind.Identifier:
            {
                var originalKeywordKind = IdentifierToKeywordKind(location.AsIdentifier()!);
                if (originalKeywordKind != Kind.Unknown)
                {
                    return null;
                }

                // class c { public prop = c| }
                if (IsPropertyDeclaration(location.Parent!) && location.Parent!.Initializer() == location)
                {
                    return null;
                }

                // class c extends React.Component { a: () => 1\n compon| }
                if (IsFromObjectTypeDeclaration(location))
                {
                    return FindAncestor(location, IsObjectTypeDeclaration);
                }

                break;
            }
        }

        if (contextToken is null)
        {
            return null;
        }

        // class C { blah; constructor/**/ }
        // or
        // class C { blah \n constructor/**/ }
        if (location.Kind == Kind.ConstructorKeyword
            || (IsIdentifier(contextToken) && IsPropertyDeclaration(contextToken.Parent!) && IsClassLike(location)))
        {
            return FindAncestor(contextToken, IsClassLike);
        }

        switch (contextToken.Kind)
        {
            // class c { public prop = | /* global completions */ }
            case Kind.EqualsToken:
                return null;
            // class c {getValue(): number; | }
            // class c { method() { } | }
            case Kind.SemicolonToken:
            case Kind.CloseBraceToken:
                // class c { method() { } b| }
                if (IsFromObjectTypeDeclaration(location) && location.Parent!.Name() == location)
                {
                    return location.Parent.Parent;
                }

                if (IsObjectTypeDeclaration(location))
                {
                    return location;
                }

                return null;
            // class c { |
            // class c {getValue(): number, | }
            case Kind.OpenBraceToken:
            case Kind.CommaToken:
                if (IsObjectTypeDeclaration(contextToken.Parent))
                {
                    return contextToken.Parent;
                }

                return null;
            default:
                if (IsObjectTypeDeclaration(location))
                {
                    // class C extends React.Component { a: () => 1\n| }
                    // class C { prop = ""\n | }
                    if (GetLineOfPosition(file, contextToken.End()) != GetLineOfPosition(file, position))
                    {
                        return location;
                    }

                    Func<Kind, bool> isValidKeyword = IsClassLike(contextToken.Parent!.Parent)
                        ? IsClassMemberCompletionKeyword
                        : IsInterfaceOrTypeLiteralCompletionKeyword;

                    if (isValidKeyword(contextToken.Kind)
                        || contextToken.Kind == Kind.AsteriskToken
                        || IsIdentifier(contextToken) && isValidKeyword(IdentifierToKeywordKind(contextToken.AsIdentifier()!)))
                    {
                        return contextToken.Parent!.Parent;
                    }
                }

                return null;
        }
    }

    // ── isFromObjectTypeDeclaration（Go 4692-4694）─────────────────────────────

    /// <summary>Go: <c>func isFromObjectTypeDeclaration(node *ast.Node) bool</c>（completions.go:4692-4694）。</summary>
    internal static bool IsFromObjectTypeDeclaration(Node node) =>
        node.Parent is not null && IsClassOrTypeElement(node.Parent) && IsObjectTypeDeclaration(node.Parent.Parent);

    // ── filterClassMembersList（Go 4697-4741）──────────────────────────────────

    /// <summary>Go: <c>func filterClassMembersList(baseSymbols, existingMembers, classElementModifierFlags, file, position) []*ast.Symbol</c>（completions.go:4697-4741）。
    /// Filters out completion suggestions for class elements。</summary>
    internal static Symbol[] FilterClassMembersList(
        Symbol[] baseSymbols,
        Node[] existingMembers,
        ModifierFlags classElementModifierFlags,
        SourceFile file,
        int position)
    {
        var existingMemberNames = new Set<string>();
        foreach (var member in existingMembers)
        {
            // Ignore omitted expressions for missing members.
            if (member.Kind != Kind.PropertyDeclaration
                && member.Kind != Kind.MethodDeclaration
                && member.Kind != Kind.GetAccessor
                && member.Kind != Kind.SetAccessor)
            {
                continue;
            }

            // If this is the current item we are editing right now, do not filter it out
            if (IsCurrentlyEditingNode(member, file, position))
            {
                continue;
            }

            // Don't filter member even if the name matches if it is declared private in the list.
            if ((member.ModifierFlags & ModifierFlags.Private) != 0)
            {
                continue;
            }

            // Do not filter it out if the static presence doesn't match.
            if (IsStatic(member) != ((classElementModifierFlags & ModifierFlags.Static) != 0))
            {
                continue;
            }

            var existingName = GetPropertyNameForPropertyNameNode(member.Name());
            if (existingName != "")
            {
                existingMemberNames.Add(existingName);
            }
        }

        return Filter(baseSymbols, propertySymbol =>
            !existingMemberNames.Has(SymbolName(propertySymbol))
            && (propertySymbol.Declarations ?? []).Length > 0
            && (CheckerType.GetDeclarationModifierFlagsFromSymbol(propertySymbol) & ModifierFlags.Private) == 0
            && !(propertySymbol.ValueDeclaration is not null && IsPrivateIdentifierClassElementDeclaration(propertySymbol.ValueDeclaration)));
    }

    // ── tryGetContainingJsxElement（Go 4743-4805）──────────────────────────────

    /// <summary>Go: <c>func tryGetContainingJsxElement(contextToken, file) *ast.JsxOpeningLikeElement</c>（completions.go:4743-4805）。</summary>
    internal static Node? TryGetContainingJsxElement(Node? contextToken, SourceFile file)
    {
        if (contextToken is null)
        {
            return null;
        }

        var parent = contextToken.Parent;
        switch (contextToken.Kind)
        {
            case Kind.GreaterThanToken:
            case Kind.LessThanSlashToken:
            case Kind.SlashToken:
            case Kind.Identifier:
            case Kind.PropertyAccessExpression:
            case Kind.JsxNamespacedName:
            case Kind.JsxAttributes:
            case Kind.JsxAttribute:
            case Kind.JsxSpreadAttribute:
                if (parent is not null && (parent.Kind == Kind.JsxSelfClosingElement || parent.Kind == Kind.JsxOpeningElement))
                {
                    if (contextToken.Kind == Kind.GreaterThanToken)
                    {
                        var precedingToken = FindPrecedingToken(file, contextToken.Pos());
                        if ((parent.TypeArguments() ?? []).Length == 0
                            || precedingToken is not null && precedingToken.Kind == Kind.SlashToken)
                        {
                            return null;
                        }
                    }

                    return parent;
                }

                if (parent is not null && IsJsxNamespacedName(parent)
                    && parent.Parent is not null && (parent.Parent.Kind == Kind.JsxSelfClosingElement || parent.Parent.Kind == Kind.JsxOpeningElement))
                {
                    return parent.Parent;
                }

                if (parent is not null && parent.Kind == Kind.JsxAttribute)
                {
                    // Currently we parse JsxOpeningLikeElement as:
                    //      JsxOpeningLikeElement
                    //          attributes: JsxAttributes
                    //             properties: NodeArray<JsxAttributeLike>
                    return parent.Parent!.Parent;
                }

                break;
            // The context token is the closing } or " of an attribute, which means
            // its parent is a JsxExpression, whose parent is a JsxAttribute,
            // whose parent is a JsxOpeningLikeElement
            case Kind.StringLiteral:
                if (parent is not null && (parent.Kind == Kind.JsxAttribute || parent.Kind == Kind.JsxSpreadAttribute))
                {
                    // Currently we parse JsxOpeningLikeElement as:
                    //      JsxOpeningLikeElement
                    //          attributes: JsxAttributes
                    //             properties: NodeArray<JsxAttributeLike>
                    return parent.Parent!.Parent;
                }

                break;
            case Kind.CloseBraceToken:
                if (parent is not null && parent.Kind == Kind.JsxExpression
                    && parent.Parent is not null && parent.Parent.Kind == Kind.JsxAttribute)
                {
                    // Currently we parse JsxOpeningLikeElement as:
                    //      JsxOpeningLikeElement
                    //          attributes: JsxAttributes
                    //             properties: NodeArray<JsxAttributeLike>
                    //                  each JsxAttribute can have initializer as JsxExpression
                    return parent.Parent.Parent!.Parent;
                }

                if (parent is not null && parent.Kind == Kind.JsxSpreadAttribute)
                {
                    // Currently we parse JsxOpeningLikeElement as:
                    //      JsxOpeningLikeElement
                    //          attributes: JsxAttributes
                    //             properties: NodeArray<JsxAttributeLike>
                    return parent.Parent!.Parent;
                }

                break;
        }

        return null;
    }

    // ── filterJsxAttributes（Go 4807-4831）─────────────────────────────────────

    /// <summary>Go: <c>func filterJsxAttributes(symbols, attributes, file, position, typeChecker) (filteredMembers []*ast.Symbol, spreadMemberNames *collections.Set[string])</c>（completions.go:4807-4831）。
    /// Filters out completion suggestions from 'symbols' according to existing JSX attributes.
    /// Returns symbols to be suggested in a JSX element, barring those whose attributes
    /// do not occur at the current position and have not otherwise been typed。</summary>
    internal static (Symbol[] FilteredMembers, Set<string> SpreadMemberNames) FilterJsxAttributes(
        Symbol[] symbols,
        Node[] attributes,
        SourceFile file,
        int position,
        CheckerType typeChecker)
    {
        var existingNames = new Set<string>();
        var membersDeclaredBySpreadAssignment = new Set<string>();
        foreach (var attr in attributes)
        {
            // If this is the item we are editing right now, do not filter it out.
            if (IsCurrentlyEditingNode(attr, file, position))
            {
                continue;
            }

            if (attr.Kind == Kind.JsxAttribute)
            {
                existingNames.Add(attr.Name()!.Text());
            }
            else if (IsJsxSpreadAttribute(attr))
            {
                SetMemberDeclaredBySpreadAssignment(attr, membersDeclaredBySpreadAssignment, typeChecker);
            }
        }

        return (Filter(symbols, a => !existingNames.Has(a.Name)), membersDeclaredBySpreadAssignment);
    }

    // ── isTypeKeywordTokenOrIdentifier（Go 4833-4836）──────────────────────────

    /// <summary>Go: <c>func isTypeKeywordTokenOrIdentifier(node *ast.Node) bool</c>（completions.go:4833-4836）。</summary>
    internal static bool IsTypeKeywordTokenOrIdentifier(Node? node) =>
        IsTypeKeywordToken(node)
        || IsIdentifier(node!) && IdentifierToKeywordKind(node!.AsIdentifier()!) == Kind.TypeKeyword;
}

public sealed partial class LanguageService
{
    // ── getJSCompletionEntries（Go 4029-4055）──────────────────────────────────

    /// <summary>Go: <c>func (l *LanguageService) getJSCompletionEntries(ctx, file, position, uniqueNames, sortedEntries) []*CompletionItem</c>（completions.go:4029-4055）。</summary>
    internal CompletionItem[] GetJSCompletionEntries(
        Context ctx,
        SourceFile file,
        int position,
        Set<string> uniqueNames,
        CompletionItem[] sortedEntries)
    {
        var nameTable = file.GetNameTable();
        var result = new List<CompletionItem>(sortedEntries);
        foreach (var (name, pos) in nameTable)
        {
            // Skip identifiers produced only from the current location
            if (pos == position)
            {
                continue;
            }

            if (!uniqueNames.Has(name) && IsIdentifierText(name, LanguageVariant.Standard))
            {
                uniqueNames.Add(name);
                result.Add(new CompletionItem
                {
                    _completionItem = new LsProto.CompletionItem
                    {
                        Label = name,
                        Kind = CompletionItemKind.Text,
                        SortText = SortTextJavascriptIdentifiers,
                        CommitCharacters = [],
                    },
                });
            }
        }

        return result.ToArray();
    }

    // ── getOptionalReplacementSpan（Go 4057-4067）──────────────────────────────

    /// <summary>Go: <c>func (l *LanguageService) getOptionalReplacementSpan(location, file) *lsproto.Range</c>（completions.go:4057-4067）。</summary>
    internal Range? GetOptionalReplacementSpan(Node? location, SourceFile file)
    {
        // StringLiteralLike locations are handled separately in stringCompletions.ts
        if (location is not null && (location.Kind == Kind.Identifier || location.Kind == Kind.PrivateIdentifier))
        {
            var start = GetStartOfNode(location, file, false /*includeJSDoc*/);
            var (lspRange, fidelity) = CreateLspRangeFromBounds(start, location.End(), file);
            if (fidelity.IsExact())
            {
                return lspRange;
            }
        }

        return null;
    }

    // ── getReplacementRangeForContextToken（Go 3674-3690）──────────────────────

    /// <summary>Go: <c>func (l *LanguageService) getReplacementRangeForContextToken(file, contextToken, position) *lsproto.Range</c>（completions.go:3674-3690）。</summary>
    internal Range? GetReplacementRangeForContextToken(SourceFile file, Node? contextToken, int position)
    {
        if (contextToken is null)
        {
            return null;
        }

        // !!! ensure range is single line
        switch (contextToken.Kind)
        {
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
                return CreateRangeFromStringLiteralLikeContent(file, contextToken, position);
            default:
                var (lspRange, fidelity) = CreateLspRangeFromNode(contextToken, file);
                if (!fidelity.IsExact())
                {
                    return null;
                }

                return lspRange;
        }
    }

    // ── createRangeFromStringLiteralLikeContent（Go 3692-3707）─────────────────

    /// <summary>Go: <c>func (l *LanguageService) createRangeFromStringLiteralLikeContent(file, node, position) *lsproto.Range</c>（completions.go:3692-3707）。</summary>
    internal Range? CreateRangeFromStringLiteralLikeContent(SourceFile file, Node node, int position)
    {
        var replacementEnd = node.End() - 1;
        var nodeStart = GetStartOfNode(node, file, false /*includeJSDoc*/);
        if (IsUnterminatedLiteral(node))
        {
            // we return no replacement range only if unterminated string is empty
            if (nodeStart == replacementEnd)
            {
                return null;
            }

            replacementEnd = Math.Min(position, node.End());
        }

        var (lspRange, fidelity) = CreateLspRangeFromBounds(nodeStart + 1, replacementEnd, file);
        if (!fidelity.IsExact())
        {
            return null;
        }

        return lspRange;
    }
}
