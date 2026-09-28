// ported from: internal/ls/semantictokens.go:1-624
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级变量 `tokenTypes` / `tokenModifiers` → `Ls` 静态类的 `internal static readonly` 表（Go 里是 var，但全文件只读；
//    表元素在 Go 里是 `lsproto.SemanticTokenType`（命名 string 类型），C# 侧直接用**字面量 string**（取值逐一相同，
//    且 `ResolvedSemanticTokensClientCapabilities.TokenTypes` 本身就是 `string[]?`）。
// 2) Go 的 `type tokenType int` / `type tokenModifier int`（iota 常量组）→ `internal enum TokenType`（含**额外成员
//    `None = -1`**，对应 `tokenFromDeclarationMapping` 的 `-1` 返回值）/ `[Flags] internal enum TokenModifier`（位值 1<<i 一致）。
// 3) `type semanticToken struct` → `internal struct SemanticToken`（值语义：Go 的 `for _, token := range ...` 拿的是副本，
//    `token.file = projection` 只改副本再 append ⇒ C# 必须是 struct 才等价）+ `IEquatable<>`（Go 的 `collections.Set[semanticToken]`
//    按字段值比较；C# 的 class 默认引用比较会不等价）。字段名 `_node/_file/_tokenType/_tokenModifier`（Go 未导出字段）。
// 4) 包级未导出函数 → `Ls` 静态类的 `internal static` 成员（PascalCase）；`provideSemanticTokens*` → partial 类成员（Go 导出 ⇒ `public`）。
//    G4 的文件不用 `using static TypeScript.Ls.Ls;`，`Ls` 侧成员一律显式写 `Ls.Xxx(...)`。
// 5) `classifySymbol(...) (tokenType, bool)` → `ClassifySymbol(Symbol symbol, SemanticMeaning meaning, out TokenType tokenType)`（C# 无多值返回）。
// 6) `c.GetTypeCheckerForFile(ctx, projection)` 的 `done()` 在 Go 里是顺序调用；C# 用 `try/finally` 保证异常路径也释放（G1 的既有写法，
//    调用顺序不变：都在收集循环之后）。
// 7) `panic(fmt.Sprintf(...))` → `Debug.Fail(msg)` + `throw new UnreachableException()`（G1 在 Ls/LanguageService.cs 的同一写法；
//    禁 `using System.Diagnostics;` ⇒ 全限定）。
// 8) `lsproto.GetClientCapabilities(ctx)` 在 C# 不收 ctx（LsProto/Lsp.cs:549 的偏离说明）。
// 9) `symbol.Declarations != nil` → `is not null`；`symbol.ValueDeclaration` 是 `Node?`（Go 的指针语义）。
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Scanner.Scanner;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>Go: <c>type tokenType int</c>（semantictokens.go:52-77）。</summary>
internal enum TokenType
{
    /// <summary>Go 的 `-1`（`tokenFromDeclarationMapping` 的"无映射"返回值）——C# 的 enum 需要显式成员。</summary>
    None = -1,
    Namespace = 0,
    Class,
    Enum,
    Interface,
    Struct,
    TypeParameter,
    Type,
    Parameter,
    Variable,
    Property,
    EnumMember,
    Decorator,
    Event,
    Function,
    /// <summary>Previously called "member" in TypeScript.</summary>
    Method,
    Macro,
    Label,
    Comment,
    String,
    Keyword,
    Number,
    Regexp,
    Operator,
}

/// <summary>Go: <c>type tokenModifier int</c>（semantictokens.go:79-92，位掩码 1&lt;&lt;iota）。</summary>
[Flags]
internal enum TokenModifier
{
    Declaration = 1 << 0,
    Definition = 1 << 1,
    Readonly = 1 << 2,
    Static = 1 << 3,
    Deprecated = 1 << 4,
    Abstract = 1 << 5,
    Async = 1 << 6,
    Modification = 1 << 7,
    Documentation = 1 << 8,
    DefaultLibrary = 1 << 9,
    Local = 1 << 10,
}

/// <summary>Go: <c>type semanticToken struct { node *ast.Node; file *ast.SourceFile; tokenType tokenType; tokenModifier tokenModifier }</c>（semantictokens.go:195-200）。</summary>
internal struct SemanticToken : IEquatable<SemanticToken>
{
    internal Node? _node;
    internal SourceFile? _file;
    internal TokenType _tokenType;
    internal TokenModifier _tokenModifier;

    public bool Equals(SemanticToken other) =>
        ReferenceEquals(_node, other._node) &&
        ReferenceEquals(_file, other._file) &&
        _tokenType == other._tokenType &&
        _tokenModifier == other._tokenModifier;

    public override bool Equals(object? obj) => obj is SemanticToken other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_node, _file, _tokenType, _tokenModifier);
}

public static partial class Ls
{
    /// <summary>Go: <c>var tokenTypes = []lsproto.SemanticTokenType{...}</c>（semantictokens.go:18-42）：编码顺序即索引顺序。</summary>
    internal static readonly string[] _tokenTypes =
    [
        "namespace",
        "class",
        "enum",
        "interface",
        "struct",
        "typeParameter",
        "type",
        "parameter",
        "variable",
        "property",
        "enumMember",
        "decorator",
        "event",
        "function",
        "method",
        "macro",
        "label",
        "comment",
        "string",
        "keyword",
        "number",
        "regexp",
        "operator",
    ];

    /// <summary>Go: <c>var tokenModifiers = []lsproto.SemanticTokenModifier{...}</c>（semantictokens.go:44-56，末项是字面量 "local"）。</summary>
    internal static readonly string[] _tokenModifiers =
    [
        "declaration",
        "definition",
        "readonly",
        "static",
        "deprecated",
        "abstract",
        "async",
        "modification",
        "documentation",
        "defaultLibrary",
        "local",
    ];

    private static bool ContainsOrdinal(string[]? haystack, string needle) =>
        haystack is not null && Array.IndexOf(haystack, needle) >= 0;

    /// <summary>
    /// SemanticTokensLegend returns the legend describing the token types and modifiers.
    /// It filters the legend to only include types and modifiers that the client supports,
    /// as indicated by clientCapabilities.
    /// Go: semantictokens.go:94-110。
    /// </summary>
    public static LsProto.SemanticTokensLegend SemanticTokensLegend(ResolvedSemanticTokensClientCapabilities clientCapabilities)
    {
        List<string> types = new(_tokenTypes.Length);
        foreach (var t in _tokenTypes)
        {
            if (ContainsOrdinal(clientCapabilities.TokenTypes, t))
            {
                types.Add(t);
            }
        }

        List<string> modifiers = new(_tokenModifiers.Length);
        foreach (var m in _tokenModifiers)
        {
            if (ContainsOrdinal(clientCapabilities.TokenModifiers, m))
            {
                modifiers.Add(m);
            }
        }

        return new LsProto.SemanticTokensLegend
        {
            TokenTypes = [.. types],
            TokenModifiers = [.. modifiers],
        };
    }

    /// <summary>Go: <c>func sortSemanticTokens(tokens []semanticToken, converters *lsconv.Converters)</c>（semantictokens.go:166-182）。</summary>
    internal static void SortSemanticTokens(List<SemanticToken> tokens, Converters converters)
    {
        tokens.Sort((a, b) =>
        {
            var (aRange, _) = SemanticTokenLSPRange(a, converters);
            var (bRange, _) = SemanticTokenLSPRange(b, converters);
            var result = aRange.Start.Line.CompareTo(bRange.Start.Line);
            if (result != 0)
            {
                return result;
            }

            result = aRange.Start.Character.CompareTo(bRange.Start.Character);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(a._file!.Path().Value, b._file!.Path().Value);
            if (result != 0)
            {
                return result;
            }

            return a._node!.Pos().CompareTo(b._node!.Pos());
        });
    }

    /// <summary>Go: <c>func semanticTokenLSPRange(token semanticToken, converters *lsconv.Converters) (lsproto.Range, spanmap.Fidelity)</c>（semantictokens.go:184-187）。</summary>
    internal static (Range Range, Fidelity Fidelity) SemanticTokenLSPRange(SemanticToken token, Converters converters)
    {
        var start = GetTokenPosOfNode(token._node!, token._file!, false);
        return converters.ToLSPRangeForFeature(token._file!, new TextRange(start, token._node!.End()), Feature.SemanticTokens);
    }

    /// <summary>Go: <c>func classifySymbol(symbol *ast.Symbol, meaning ast.SemanticMeaning) (tokenType, bool)</c>（semantictokens.go:430-469）。</summary>
    internal static bool ClassifySymbol(Symbol symbol, SemanticMeaning meaning, out TokenType tokenType)
    {
        var flags = symbol.Flags;
        if ((flags & SymbolFlags.Class) != 0)
        {
            tokenType = TokenType.Class;
            return true;
        }

        if ((flags & SymbolFlags.Enum) != 0)
        {
            tokenType = TokenType.Enum;
            return true;
        }

        if ((flags & SymbolFlags.TypeAlias) != 0)
        {
            tokenType = TokenType.Type;
            return true;
        }

        if ((flags & SymbolFlags.Interface) != 0)
        {
            if ((meaning & SemanticMeaning.Type) != 0)
            {
                tokenType = TokenType.Interface;
                return true;
            }
        }

        if ((flags & SymbolFlags.TypeParameter) != 0)
        {
            tokenType = TokenType.TypeParameter;
            return true;
        }

        // Check the value declaration
        var decl = symbol.ValueDeclaration;
        if (decl is null && (symbol.Declarations?.Length ?? 0) > 0)
        {
            decl = symbol.Declarations![0];
        }

        if (decl is not null)
        {
            if (IsBindingElement(decl))
            {
                decl = GetDeclarationForBindingElement(decl);
            }

            var mapped = TokenFromDeclarationMapping(decl.Kind);
            if (mapped != TokenType.None)
            {
                tokenType = mapped;
                return true;
            }
        }

        tokenType = TokenType.None;
        return false;
    }

    /// <summary>Go: <c>func tokenFromDeclarationMapping(kind ast.Kind) tokenType</c>（semantictokens.go:471-506，无映射时返回 -1）。</summary>
    internal static TokenType TokenFromDeclarationMapping(Kind kind)
    {
        switch (kind)
        {
            case Kind.VariableDeclaration:
                return TokenType.Variable;
            case Kind.Parameter:
                return TokenType.Parameter;
            case Kind.PropertyDeclaration:
                return TokenType.Property;
            case Kind.ModuleDeclaration:
                return TokenType.Namespace;
            case Kind.EnumDeclaration:
                return TokenType.Enum;
            case Kind.EnumMember:
                return TokenType.EnumMember;
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                return TokenType.Class;
            case Kind.MethodDeclaration:
                return TokenType.Method;
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
                return TokenType.Function;
            case Kind.MethodSignature:
                return TokenType.Method;
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                return TokenType.Property;
            case Kind.PropertySignature:
                return TokenType.Property;
            case Kind.InterfaceDeclaration:
                return TokenType.Interface;
            case Kind.TypeAliasDeclaration:
                return TokenType.Type;
            case Kind.TypeParameter:
                return TokenType.TypeParameter;
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
                return TokenType.Property;
            default:
                return TokenType.None;
        }
    }

    /// <summary>Go: <c>func reclassifyByType(c *checker.Checker, node *ast.Node, tt tokenType) tokenType</c>（semantictokens.go:508-547）。</summary>
    internal static TokenType ReclassifyByType(Checker.Checker c, Node node, TokenType tt)
    {
        // Type-based reclassification for variables, properties, and parameters
        if (tt == TokenType.Variable || tt == TokenType.Property || tt == TokenType.Parameter)
        {
            var typ = c.GetTypeAtLocation(node);
            if (typ is not null)
            {
                bool Test(Func<Type, bool> condition)
                {
                    if (condition(typ))
                    {
                        return true;
                    }

                    if ((typ.Flags() & TypeFlags.Union) != 0)
                    {
                        return typ.AsUnionType()!.Types().Any(condition);
                    }

                    return false;
                }

                // Check for constructor signatures (class-like)
                if (tt != TokenType.Parameter && Test(t => c.GetSignaturesOfType(t, SignatureKind.Construct).Length > 0))
                {
                    return TokenType.Class;
                }

                // Check for call signatures (function-like)
                // Must have call signatures AND (no properties OR be used in call context)
                var hasCallSignatures = Test(t => c.GetSignaturesOfType(t, SignatureKind.Call).Length > 0);
                if (hasCallSignatures)
                {
                    var hasNoProperties = !Test(t =>
                    {
                        var objType = t.AsObjectType();
                        return objType is not null && objType.Properties().Length > 0;
                    });
                    if (hasNoProperties || IsExpressionInCallExpression(node))
                    {
                        if (tt == TokenType.Property)
                        {
                            return TokenType.Method;
                        }

                        return TokenType.Function;
                    }
                }
            }
        }

        return tt;
    }

    /// <summary>Go: <c>func isLocalDeclaration(decl *ast.Node, sourceFile *ast.SourceFile) bool</c>（semantictokens.go:549-573）。</summary>
    internal static bool IsLocalDeclaration(Node decl, SourceFile sourceFile)
    {
        if (IsBindingElement(decl))
        {
            decl = GetDeclarationForBindingElement(decl);
        }

        if (IsVariableDeclaration(decl))
        {
            var parent = decl.Parent;
            // Check if this is a catch clause parameter
            if (parent is not null && IsCatchClause(parent))
            {
                return ReferenceEquals(GetSourceFileOfNode(decl), sourceFile);
            }

            if (parent is not null && IsVariableDeclarationList(parent))
            {
                var grandparent = parent.Parent;
                if (grandparent is not null)
                {
                    var greatGrandparent = grandparent.Parent;
                    return (!IsSourceFile(greatGrandparent!) || IsCatchClause(grandparent)) &&
                        ReferenceEquals(GetSourceFileOfNode(decl), sourceFile);
                }
            }
        }
        else if (IsFunctionDeclaration(decl))
        {
            var parent = decl.Parent;
            return parent is not null && !IsSourceFile(parent) && ReferenceEquals(GetSourceFileOfNode(decl), sourceFile);
        }

        return false;
    }

    /// <summary>Go: <c>func getDeclarationForBindingElement(element *ast.Node) *ast.Node</c>（semantictokens.go:575-589）。</summary>
    internal static Node GetDeclarationForBindingElement(Node element)
    {
        while (true)
        {
            var parent = element.Parent;
            if (parent is not null && IsBindingPattern(parent))
            {
                var grandparent = parent.Parent;
                if (grandparent is not null && IsBindingElement(grandparent))
                {
                    element = grandparent;
                    continue;
                }

                return parent.Parent!;
            }

            return element;
        }
    }

    /// <summary>Go: <c>func isInImportClause(node *ast.Node) bool</c>（semantictokens.go:591-594）。</summary>
    internal static bool IsInImportClause(Node node)
    {
        var parent = node.Parent;
        return parent is not null && (IsImportClause(parent) || IsImportSpecifier(parent) || IsNamespaceImport(parent));
    }

    /// <summary>Go: <c>func isExpressionInCallExpression(node *ast.Node) bool</c>（semantictokens.go:596-601）。</summary>
    internal static bool IsExpressionInCallExpression(Node node)
    {
        while (IsRightSideOfQualifiedNameOrPropertyAccess(node))
        {
            node = node.Parent!;
        }

        var parent = node.Parent;
        return parent is not null && IsCallExpression(parent) && ReferenceEquals(parent.Expression(), node);
    }

    /// <summary>Go: <c>func isInfinityOrNaNString(text string) bool</c>（semantictokens.go:603-605）。</summary>
    internal static bool IsInfinityOrNaNString(string text)
    {
        return text == "Infinity" || text == "NaN";
    }

    /// <summary>
    /// encodeSemanticTokens encodes tokens into the LSP format using relative positioning.
    /// It filters tokens based on client capabilities, only including types and modifiers that the client supports.
    /// Go: semantictokens.go:607-624。
    /// </summary>
    internal static uint[] EncodeSemanticTokens(Context ctx, List<SemanticToken> tokens, Converters converters)
    {
        // Build mapping from server token types/modifiers to client indices
        Dictionary<TokenType, uint> typeMapping = [];
        Dictionary<string, uint> modifierMapping = [];

        var clientCapabilities = GetClientCapabilities().TextDocument.SemanticTokens;

        // Map server token types to client-supported indices
        var clientIdx = 0u;
        for (var i = 0; i < _tokenTypes.Length; i++)
        {
            var serverType = _tokenTypes[i];
            if (ContainsOrdinal(clientCapabilities.TokenTypes, serverType))
            {
                typeMapping[(TokenType)i] = clientIdx;
                clientIdx++;
            }
        }

        // Map server token modifiers to client-supported bit positions
        var clientBit = 0u;
        foreach (var serverModifier in _tokenModifiers)
        {
            if (ContainsOrdinal(clientCapabilities.TokenModifiers, serverModifier))
            {
                modifierMapping[serverModifier] = clientBit;
                clientBit++;
            }
        }

        // Each token encodes 5 uint32 values: deltaLine, deltaChar, length, tokenType, tokenModifiers
        List<uint> encoded = new(tokens.Count * 5);
        var prevLine = 0u;
        var prevChar = 0u;

        foreach (var token in tokens)
        {
            // Skip tokens with types not supported by the client
            if (!typeMapping.TryGetValue(token._tokenType, out var clientTypeIdx))
            {
                continue;
            }

            // Map modifiers to client-supported bit mask
            var clientModifierMask = 0u;
            for (var i = 0; i < _tokenModifiers.Length; i++)
            {
                if (((int)token._tokenModifier & (1 << i)) != 0)
                {
                    if (modifierMapping.TryGetValue(_tokenModifiers[i], out var clientBitIndex))
                    {
                        clientModifierMask |= 1u << (int)clientBitIndex;
                    }
                }
            }

            // Semantic tokens must describe one concrete source segment; synthesized and cross-segment
            // tokens do not identify a coherent token in the original text.
            var (lspRange, fidelity) = SemanticTokenLSPRange(token, converters);
            if (!fidelity.IsExact())
            {
                continue;
            }

            var startPos = lspRange.Start;
            var endPos = lspRange.End;

            // Length is the character difference when on the same line
            uint tokenLength;
            if (startPos.Line == endPos.Line)
            {
                tokenLength = endPos.Character - startPos.Character;
            }
            else
            {
                Debug.Fail($"semantic tokens: token spans multiple lines: start=({startPos.Line},{startPos.Character}) end=({endPos.Line},{endPos.Character}) for token at offset {token._node!.Pos()}");
                throw new System.Diagnostics.UnreachableException();
            }

            var line = startPos.Line;
            var @char = startPos.Character;

            // Multiple virtual projections can describe the same original token; LSP requires one entry per
            // start position, so retain the first after sorting.
            if (encoded.Count > 0 && line == prevLine && @char == prevChar)
            {
                continue;
            }

            if (encoded.Count > 0 && (line < prevLine || (line == prevLine && @char < prevChar)))
            {
                Debug.Fail($"semantic tokens: positions must be strictly increasing: prev=({prevLine},{prevChar}) current=({line},{@char}) for token at offset {token._node!.Pos()}");
                throw new System.Diagnostics.UnreachableException();
            }

            // Encode as: [deltaLine, deltaChar, length, tokenType, tokenModifiers]
            var deltaLine = line - prevLine;
            uint deltaChar;
            if (deltaLine == 0)
            {
                deltaChar = @char - prevChar;
            }
            else
            {
                deltaChar = @char;
            }

            encoded.Add(deltaLine);
            encoded.Add(deltaChar);
            encoded.Add(tokenLength);
            encoded.Add(clientTypeIdx);
            encoded.Add(clientModifierMask);

            prevLine = line;
            prevChar = @char;
        }

        return [.. encoded];
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideSemanticTokens(ctx context.Context, documentURI lsproto.DocumentUri) (lsproto.SemanticTokensResponse, error)</c>（semantictokens.go:112-147）。</summary>
    public (SemanticTokensOrNull Response, Exception? Error) ProvideSemanticTokens(Context ctx, DocumentUri documentURI)
    {
        var (program, file) = GetProgramAndFile(documentURI);

        var supplemental = file.SupplementalSourceFiles();
        List<SourceFile> files = new(1 + supplemental.Length) { file };
        files.AddRange(supplemental);
        List<SemanticToken> tokens = new(files.Count);
        foreach (var projection in files)
        {
            var (c, done) = program.GetTypeCheckerForFile(ctx, projection);
            try
            {
                foreach (var token in CollectSemanticTokens(ctx, c, projection, program))
                {
                    var withFile = token;
                    withFile._file = projection;
                    tokens.Add(withFile);
                }
            }
            finally
            {
                done();
            }
        }

        Ls.SortSemanticTokens(tokens, _converters!);

        if (tokens.Count == 0)
        {
            return (new SemanticTokensOrNull(), null);
        }

        // Convert to LSP format (relative encoding)
        var encoded = Ls.EncodeSemanticTokens(ctx, tokens, _converters!);

        return (new SemanticTokensOrNull
        {
            SemanticTokens = new SemanticTokens
            {
                Data = encoded,
            },
        }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideSemanticTokensRange(ctx context.Context, documentURI lsproto.DocumentUri, rng lsproto.Range) (lsproto.SemanticTokensRangeResponse, error)</c>（semantictokens.go:149-192）。</summary>
    public (SemanticTokensOrNull Response, Exception? Error) ProvideSemanticTokensRange(Context ctx, DocumentUri documentURI, Range rng)
    {
        var (program, file) = GetProgramAndFile(documentURI);

        var mappedRanges = _converters!.FromLSPRangeIntersectingForSourceFile(file, rng, Feature.SemanticTokens);
        List<SemanticToken> tokens = new(mappedRanges.Length);
        var seen = new Set<SemanticToken>();
        foreach (var mapped in mappedRanges)
        {
            var projection = mapped.Script;
            var (c, done) = program.GetTypeCheckerForFile(ctx, projection);
            try
            {
                foreach (var token in CollectSemanticTokensInRange(ctx, c, projection, program, mapped.Span.Pos(), mapped.Span.End()))
                {
                    var withFile = token;
                    withFile._file = projection;
                    if (seen.AddIfAbsent(withFile))
                    {
                        tokens.Add(withFile);
                    }
                }
            }
            finally
            {
                done();
            }
        }

        Ls.SortSemanticTokens(tokens, _converters);

        if (tokens.Count == 0)
        {
            return (new SemanticTokensOrNull(), null);
        }

        // Convert to LSP format (relative encoding)
        var encoded = Ls.EncodeSemanticTokens(ctx, tokens, _converters!);

        return (new SemanticTokensOrNull
        {
            SemanticTokens = new SemanticTokens
            {
                Data = encoded,
            },
        }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) collectSemanticTokens(ctx, c *checker.Checker, file *ast.SourceFile, program *compiler.Program) []semanticToken</c>（semantictokens.go:202-204）。</summary>
    internal List<SemanticToken> CollectSemanticTokens(Context ctx, Checker.Checker c, SourceFile file, Program program)
    {
        return CollectSemanticTokensInRange(ctx, c, file, program, file.Pos(), file.End());
    }

    /// <summary>Go: <c>func (l *LanguageService) collectSemanticTokensInRange(ctx, c, file, program, spanStart, spanEnd int) []semanticToken</c>（semantictokens.go:206-...）。</summary>
    internal List<SemanticToken> CollectSemanticTokensInRange(Context ctx, Checker.Checker c, SourceFile file, Program program, int spanStart, int spanEnd)
    {
        List<SemanticToken> tokens = [];

        var inJSXElement = false;

        bool Visit(Node node)
        {
            // Check for cancellation
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if ((node.Flags & NodeFlags.Reparsed) != 0)
            {
                return false;
            }

            var nodeEnd = node.End();
            if (node.Pos() >= spanEnd || nodeEnd <= spanStart)
            {
                return false;
            }

            var prevInJSXElement = inJSXElement;
            if (IsJsxElement(node) || IsJsxSelfClosingElement(node))
            {
                inJSXElement = true;
            }
            else if (IsJsxExpression(node))
            {
                inJSXElement = false;
            }

            if ((IsIdentifier(node) || IsPrivateIdentifier(node)) && node.Text() != "" && !inJSXElement && !Ls.IsInImportClause(node) && !Ls.IsInfinityOrNaNString(node.Text()))
            {
                var symbol = c.GetSymbolAtLocation(node);
                if (symbol is not null)
                {
                    // Resolve aliases
                    if ((symbol.Flags & SymbolFlags.Alias) != 0)
                    {
                        symbol = c.GetAliasedSymbol(symbol);
                    }

                    if (Ls.ClassifySymbol(symbol, Ls.GetMeaningFromLocation(node), out var tokenType))
                    {
                        var tokenModifier = (TokenModifier)0;

                        // Check if this is a declaration
                        var parent = node.Parent;
                        if (parent is not null)
                        {
                            var parentIsDeclaration = IsBindingElement(parent) || Ls.TokenFromDeclarationMapping(parent.Kind) == tokenType;
                            if (parentIsDeclaration && ReferenceEquals(parent.Name(), node))
                            {
                                tokenModifier |= TokenModifier.Declaration;
                            }
                        }

                        // Property declaration in constructor: reclassify parameters as properties in property access context
                        if (tokenType == TokenType.Parameter && IsRightSideOfQualifiedNameOrPropertyAccess(node))
                        {
                            tokenType = TokenType.Property;
                        }

                        // Type-based reclassification
                        tokenType = Ls.ReclassifyByType(c, node, tokenType);

                        // Get the value declaration to check modifiers
                        var decl = symbol.ValueDeclaration;
                        if (decl is not null)
                        {
                            var modifiers = GetCombinedModifierFlags(decl);
                            var nodeFlags = GetCombinedNodeFlags(decl);

                            if ((modifiers & ModifierFlags.Static) != 0)
                            {
                                tokenModifier |= TokenModifier.Static;
                            }

                            if ((modifiers & ModifierFlags.Async) != 0)
                            {
                                tokenModifier |= TokenModifier.Async;
                            }

                            if (tokenType != TokenType.Class && tokenType != TokenType.Interface)
                            {
                                if ((modifiers & ModifierFlags.Readonly) != 0 || (nodeFlags & NodeFlags.Const) != 0 || (symbol.Flags & SymbolFlags.EnumMember) != 0)
                                {
                                    tokenModifier |= TokenModifier.Readonly;
                                }
                            }

                            if ((tokenType == TokenType.Variable || tokenType == TokenType.Function) && Ls.IsLocalDeclaration(decl, file))
                            {
                                tokenModifier |= TokenModifier.Local;
                            }

                            var declSourceFile = GetSourceFileOfNode(decl);
                            if (declSourceFile is not null && program.IsSourceFileDefaultLibrary(declSourceFile.Path()))
                            {
                                tokenModifier |= TokenModifier.DefaultLibrary;
                            }
                        }
                        else if (symbol.Declarations is not null)
                        {
                            foreach (var declaration in symbol.Declarations)
                            {
                                var declSourceFile = GetSourceFileOfNode(declaration);
                                if (declSourceFile is not null && program.IsSourceFileDefaultLibrary(declSourceFile.Path()))
                                {
                                    tokenModifier |= TokenModifier.DefaultLibrary;
                                    break;
                                }
                            }
                        }

                        tokens.Add(new SemanticToken
                        {
                            _node = node,
                            _tokenType = tokenType,
                            _tokenModifier = tokenModifier,
                        });
                    }
                }
            }

            node.ForEachChild(Visit);
            inJSXElement = prevInJSXElement;
            return false;
        }

        Visit(file.AsNode());

        // Check for cancellation after collection
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            return [];
        }

        return tokens;
    }
}
