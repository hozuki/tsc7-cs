// ported from: internal/ls/symbols.go:1-723
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级常量 `maxLength = 150` → `Ls.MaxLength`（`internal const`）；包级类型 `DeclarationInfo` →
//    `internal sealed class DeclarationInfo`（Go 导出但字段未导出 ⇒ C# `internal` + `_camelCase` 字段）。
//    包级未导出函数 → `Ls` 静态类的 `internal static`（PascalCase）；`ProvideWorkspaceSymbols` 是 Go **导出**函数
//    ⇒ `Ls` 的 `public static`。`*LanguageService` 方法 → partial 类成员（Go 导出 ⇒ `public`）。
// 2) Go 的 `collections.Set[struct{ name string; kind SymbolKind; rng Range }]` 按**结构体值**去重（`Range` 逐字段比较）；
//    C# 的 `ValueTuple<..., Range>` 对 `Range` 是**引用比较** ⇒ 这里用 `DocumentSymbolKey`（`IEquatable<>`，`Range` 走
//    `CompareRanges == 0`）保住 Go 的语义。
// 3) Go 的 `flattenDocumentSymbols` 返回 `[]lsproto.SymbolInformation`（值切片）再逐元素取地址；C# 的
//    `SymbolInformation` 是 class ⇒ 直接返回 `List<SymbolInformation>`（引用的集合），跳过多余的取址循环。
// 4) Go 的闭包状态机（`symbols` / `expandoTargets` 的 save/restore + `startNode` 返回 `func()`）→ C# 局部函数 + 被捕获的
//    局部变量（C# 闭包按变量捕获，重新赋值对外层可见，与 Go 的变量捕获一致）+ `Action`。
// 5) `len(x)`（Go 的 **UTF-8 字节长度**）在 `cleanCallbackText`/`getUnnamedNodeLabel` 里是**真实参与判断**的
//    （`if len(name) > maxLength`），C# 的 `string.Length` 是 UTF-16 代码单元数 ⇒ 那里改用
//    `Encoding.UTF8.GetByteCount(name)`（与 Go 逐位一致）。`newDocumentSymbol` 里的 `len(truncatedText) < len(text)` 是
//    "前缀是否更短"的判断，两种口径等价 ⇒ 直接用 `.Length`。
// 6) `strings.Map` 删行符 → LINQ `Where` + `new string(...)`；`utf8.DecodeRuneInString` + `unicode.IsUpper/ToLower`
//    → `string.EnumerateRunes()` + `Rune.IsUpper/ToLowerInvariant`（逐码点，等价）。
// 7) `map[tspath.Path]*ast.SourceFile` 的遍历：Go 顺序随机，C# `Dictionary` 保持插入顺序 ⇒ 结果是**确定的**
//    （与 ProjectTestUtil 的同名偏离一致，是收紧而非语义变化）。
// 8) `slices.SortFunc`（不稳定）→ `List<T>.Sort`（同样不稳定）；`lsproto.CompareRanges` 用 `using static` 的裸名。
// 9) `nameNode`/`container` 等在 Go 里由 `computeDeclarationMap` 保证非 nil ⇒ C# 补 `!`；`sourceFile` 用 `!`。
using System.Text;
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Printer;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using TypeScript.StringUtil;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Path = TypeScript.TSPath.Path;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>
/// Go 的匿名结构体 <c>struct { name string; kind lsproto.SymbolKind; rng lsproto.Range }</c>（symbols.go:23-27）：
/// `seen` 集合的键。C# 里必须显式实现值相等（见文件头第 2 条）。
/// </summary>
internal struct DocumentSymbolKey : IEquatable<DocumentSymbolKey>
{
    internal string _name;
    internal SymbolKind _kind;
    internal Range _range;

    public bool Equals(DocumentSymbolKey other) =>
        string.Equals(_name, other._name, StringComparison.Ordinal) &&
        _kind == other._kind &&
        CompareRanges(_range, other._range) == 0;

    public override bool Equals(object? obj) => obj is DocumentSymbolKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_name, _kind, _range.Start.Line, _range.Start.Character, _range.End.Line, _range.End.Character);
}

/// <summary>Go: <c>type DeclarationInfo struct { name string; declaration *ast.Node; matchScore int }</c>（symbols.go:568-572）。</summary>
internal sealed class DeclarationInfo(string name, Node declaration, int matchScore)
{
    internal string _name = name;
    internal Node _declaration = declaration;
    internal int _matchScore = matchScore;
}

public static partial class Ls
{
    /// <summary>Go: <c>const maxLength = 150</c>（symbols.go:265）。</summary>
    internal const int MaxLength = 150;

    /// <summary>Go: <c>func flattenDocumentSymbols(docSymbols []*lsproto.DocumentSymbol, documentURI lsproto.DocumentUri) []lsproto.SymbolInformation</c>（symbols.go:52-80）。</summary>
    internal static List<SymbolInformation> FlattenDocumentSymbols(DocumentSymbol?[] docSymbols, DocumentUri documentURI)
    {
        // Flatten the hierarchy
        List<SymbolInformation> result = [];
        void Flatten(DocumentSymbol?[] symbols, string? containerName)
        {
            foreach (var symbol in symbols)
            {
                if (symbol is null)
                {
                    continue;
                }

                var info = new SymbolInformation
                {
                    Name = symbol.Name,
                    Kind = symbol.Kind,
                    Location = new Location
                    {
                        Uri = documentURI,
                        Range = symbol.Range,
                    },
                    ContainerName = containerName,
                    Tags = symbol.Tags,
                    Deprecated = symbol.Deprecated,
                };
                result.Add(info);

                // Recursively flatten children with this symbol as container
                if (symbol.Children is not null && symbol.Children.Length > 0)
                {
                    Flatten(symbol.Children, symbol.Name);
                }
            }
        }

        Flatten(docSymbols, null);
        return result;
    }

    /// <summary>Target is `f.prototype`.（symbols.go:261-267）</summary>
    internal static bool IsPrototypeExpando(Node target)
    {
        if (IsAccessExpression(target))
        {
            var accessName = GetElementOrPropertyAccessName(target);
            return accessName is not null && accessName.Text() == "prototype";
        }

        return false;
    }

    /// <summary>
    /// Merges expando symbols into their target symbols, and namespaces of same name.
    /// Modifies the input slice.
    /// Go: symbols.go:322-382。
    /// </summary>
    internal static List<DocumentSymbol?> MergeExpandos(List<DocumentSymbol?> symbols)
    {
        List<DocumentSymbol?> mergedSymbols = new(symbols.Count);
        // Collect symbols that can be an expando target.
        MultiMap<string, int> nameToExpandoTargetIndex = new();
        // Collect namespaces.
        Dictionary<string, int> nameToNamespaceIndex = [];
        for (var i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i]!;
            if (IsAnonymousName(symbol.Name))
            {
                continue;
            }

            if (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Function || symbol.Kind == SymbolKind.Variable)
            {
                nameToExpandoTargetIndex.Add(symbol.Name, i);
            }

            if (symbol.Kind == SymbolKind.Namespace)
            {
                if (!nameToNamespaceIndex.ContainsKey(symbol.Name))
                {
                    nameToNamespaceIndex[symbol.Name] = i;
                }
            }
        }

        for (var i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i]!;
            if (symbol.Children is not null)
            {
                symbol.Children = [.. MergeExpandos([.. symbol.Children])];
            }

            // Anonymous symbols never merge.
            if (IsAnonymousName(symbol.Name))
            {
                continue;
            }

            // Merge expandos.
            if (symbol.Kind == SymbolKind.Property)
            {
                var symbolsWithSameName = nameToExpandoTargetIndex.Get(symbol.Name) ?? [];
                for (var j = symbolsWithSameName.Count - 1; j >= 0; j--)
                {
                    var targetIndex = symbolsWithSameName[j];
                    var targetSymbol = symbols[targetIndex]!;
                    MergeChildren(targetSymbol, symbol);
                    // Mark this symbol as merged.
                    symbols[i] = null;
                }
            }

            // Merge namespaces.
            if (symbol.Kind == SymbolKind.Namespace)
            {
                if (nameToNamespaceIndex.TryGetValue(symbol.Name, out var targetIndex) && targetIndex != i)
                {
                    var targetSymbol = symbols[targetIndex]!;
                    MergeChildren(targetSymbol, symbol);
                    // Mark this symbol as merged.
                    symbols[i] = null;
                }
            }
        }

        foreach (var symbol in symbols)
        {
            if (symbol is not null)
            {
                mergedSymbols.Add(symbol);
            }
        }

        return mergedSymbols;
    }

    private static void MergeChildren(DocumentSymbol target, DocumentSymbol source)
    {
        if (source.Children is not null)
        {
            if (target.Children is null)
            {
                target.Children = source.Children;
            }
            else
            {
                var children = MergeExpandos([.. target.Children, .. source.Children]);
                var sorted = children.ToArray();
                Array.Sort(sorted, (a, b) => CompareRanges(a!.Range, b!.Range));
                target.Children = sorted;
            }
        }
    }

    /// <summary>See `getUnnamedNodeLabel`.（symbols.go:384-388）</summary>
    internal static bool IsAnonymousName(string name)
    {
        return name == "<function>" || name == "<class>" || name == "export=" || name == "default" ||
            name == "constructor" || name == "()" || name == "new()" || name == "[]" || name.EndsWith(") callback", StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func getTextOfName(node *ast.Node) string</c>（symbols.go:390-404）。</summary>
    internal static string GetTextOfName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.NumericLiteral:
                return node.Text();
            case Kind.StringLiteral:
                return "\"" + EscapeString(node.Text(), QuoteChar.DoubleQuote) + "\"";
            case Kind.NoSubstitutionTemplateLiteral:
                return "`" + EscapeString(node.Text(), QuoteChar.Backtick) + "`";
            case Kind.ComputedPropertyName:
                if (IsStringOrNumericLiteralLike(node.Expression()!))
                {
                    return GetTextOfName(node.Expression()!);
                }

                break;
        }

        return GetTextOfNode(node);
    }

    /// <summary>Go: <c>func getUnnamedNodeLabel(node *ast.Node) string</c>（symbols.go:406-440）。</summary>
    internal static string GetUnnamedNodeLabel(Node node)
    {
        var parent = WalkUpParenthesizedExpressions(node.Parent);
        if (parent is not null && IsExportAssignment(parent))
        {
            if (parent.AsExportAssignment()!.IsExportEquals)
            {
                return "export=";
            }

            return "default";
        }

        switch (node.Kind)
        {
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
                if ((node.ModifierFlags & ModifierFlags.Default) != 0)
                {
                    return "default";
                }

                if (IsCallExpression(node.Parent!))
                {
                    var name = GetCallExpressionName(node.Parent!.Expression()!);
                    if (name != "")
                    {
                        name = CleanCallbackText(name);
                        if (Encoding.UTF8.GetByteCount(name) > MaxLength)
                        {
                            return name + " callback";
                        }

                        var args = CleanCallbackText(GetCallExpressionLiteralArgs(node.Parent!));
                        return name + "(" + args + ") callback";
                    }
                }

                return "<function>";
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                if ((node.ModifierFlags & ModifierFlags.Default) != 0)
                {
                    return "default";
                }

                return "<class>";
            case Kind.Constructor:
                return "constructor";
            case Kind.CallSignature:
                return "()";
            case Kind.ConstructSignature:
                return "new()";
            case Kind.IndexSignature:
                return "[]";
        }

        return "";
    }

    /// <summary>Go: <c>func getCallExpressionName(node *ast.Node) string</c>（symbols.go:442-456）。</summary>
    internal static string GetCallExpressionName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
                return node.Text();
            case Kind.PropertyAccessExpression:
                var left = GetCallExpressionName(node.Expression()!);
                var right = GetCallExpressionName(node.Name()!);
                if (left != "")
                {
                    return left + "." + right;
                }

                return right;
        }

        return "";
    }

    /// <summary>Go: <c>func getCallExpressionLiteralArgs(callExpr *ast.Node) string</c>（symbols.go:458-465）。</summary>
    internal static string GetCallExpressionLiteralArgs(Node callExpr)
    {
        List<string> parts = [];
        foreach (var arg in callExpr.Arguments() ?? [])
        {
            if (IsStringLiteralLike(arg) || IsTemplateExpression(arg))
            {
                parts.Add(GetTextOfNode(arg));
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>Go: <c>func cleanCallbackText(text string) string</c>（symbols.go:467-477）。</summary>
    internal static string CleanCallbackText(string text)
    {
        var truncated = TruncateByRunes(text, MaxLength);
        if (truncated.Length < text.Length)
        {
            text = truncated + "...";
        }

        return new string(text.Where(c => !IsLineBreak(c)).ToArray());
    }

    /// <summary>Go: <c>func getInteriorModule(node *ast.Node) *ast.Node</c>（symbols.go:479-484）。</summary>
    internal static Node GetInteriorModule(Node node)
    {
        while (node.Body() is not null && IsModuleDeclaration(node.Body()!))
        {
            node = node.Body()!;
        }

        return node;
    }

    /// <summary>Go: <c>func getModuleName(node *ast.Node) string</c>（symbols.go:486-493）。</summary>
    internal static string GetModuleName(Node node)
    {
        var result = node.Name()!.Text();
        while (node.Body() is not null && IsModuleDeclaration(node.Body()!))
        {
            node = node.Body()!;
            result = result + "." + node.Name()!.Text();
        }

        return result;
    }

    /// <summary>
    /// ProvideWorkspaceSymbols（symbols.go:580-643）。
    /// </summary>
    public static (SymbolInformationsOrWorkspaceSymbolsOrNull Response, Exception? Error) ProvideWorkspaceSymbols(
        Context ctx,
        Program[] programs,
        Converters converters,
        UserPreferences preferences,
        string query)
    {
        var excludeLibrarySymbols = preferences.ExcludeLibrarySymbolsInNavTo.IsTrue();
        // Obtain set of non-declaration source files from all active programs.
        Dictionary<Path, SourceFile> sourceFiles = [];
        foreach (var program in programs)
        {
            foreach (var sourceFile in program.SourceFiles())
            {
                if ((program.HasTSFile() || !sourceFile.IsDeclarationFile) &&
                    !ShouldExcludeFile(sourceFile, program, excludeLibrarySymbols))
                {
                    sourceFiles[sourceFile.Path()] = sourceFile;
                }
            }
        }

        // Create DeclarationInfos for all declarations in the source files.
        List<DeclarationInfo> infos = [];
        foreach (var sourceFile in sourceFiles.Values)
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return (new SymbolInformationsOrWorkspaceSymbolsOrNull(), null);
            }

            var declarationMap = sourceFile.GetDeclarationMap();
            foreach (var (name, declarations) in declarationMap)
            {
                var score = GetMatchScore(name, query);
                if (score >= 0)
                {
                    foreach (var declaration in declarations)
                    {
                        infos.Add(new DeclarationInfo(name, declaration, score));
                    }
                }
            }
        }

        // Sort the DeclarationInfos and return the top 256 matches.
        infos.Sort(CompareDeclarationInfos);
        var count = Math.Min(infos.Count, 256);
        List<SymbolInformation> symbols = new(count);
        foreach (var info in infos.Take(count))
        {
            var node = info._declaration;
            var sourceFile = GetSourceFileOfNode(node)!;
            var container = GetContainerNode(info._declaration);
            string? containerName = null;
            if (container is not null)
            {
                containerName = GetDeclarationName(container);
            }

            // Use the name node's span so that VS selects just the symbol name (matching
            // the TS5 navto behaviour). GetNameOfDeclaration is always non-nil here because
            // computeDeclarationMap only adds declarations whose GetDeclarationName (string
            // form) is non-empty, which implies a name node exists.
            var nameNode = GetNameOfDeclaration(node)!;
            var nameStart = GetStartOfNode(nameNode, sourceFile, false /*includeJsDoc*/);
            var nameRange = new TextRange(nameStart, nameNode.End());
            var (location, fidelity) = converters.ToLSPLocationForFeature(sourceFile, nameRange, Feature.DocumentSymbols);
            if (!fidelity.IsSingleSegment())
            {
                // The name has no counterpart in the original text, so there is nothing to navigate to.
                continue;
            }

            var symbol = new SymbolInformation
            {
                Name = info._name,
                Kind = GetSymbolKindFromNode(info._declaration),
                Location = location,
                ContainerName = containerName,
            };
            symbols.Add(symbol);
        }

        return (new SymbolInformationsOrWorkspaceSymbolsOrNull { SymbolInformations = [.. symbols] }, null);
    }

    /// <summary>Go: <c>func shouldExcludeFile(file *ast.SourceFile, program *compiler.Program, excludeLibrarySymbols bool) bool</c>（symbols.go:645-647）。</summary>
    internal static bool ShouldExcludeFile(SourceFile file, Program program, bool excludeLibrarySymbols)
    {
        return excludeLibrarySymbols && (IsInsideNodeModules(file.FileName()) || program.IsLibFile(file));
    }

    /// <summary>Go: <c>func isInsideNodeModules(fileName string) bool</c>（symbols.go:649-651）。</summary>
    internal static bool IsInsideNodeModules(string fileName)
    {
        return fileName.Contains("/node_modules/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Return a score for matching `s` against `pattern`. In order to match, `s` must contain each of the characters in
    /// `pattern` in the same order. Upper case characters in `pattern` must match exactly, whereas lower case characters
    /// in `pattern` match either case in `s`. If `s` doesn't match, -1 is returned. Otherwise, the returned score is the
    /// number of characters in `s` that weren't matched. Thus, zero represents an exact match, and higher values represent
    /// increasingly less specific partial matches.
    /// Go: symbols.go:653-672。
    /// </summary>
    internal static int GetMatchScore(string s, string pattern)
    {
        var score = 0;
        var runes = s.EnumerateRunes().ToArray();
        var index = 0;
        foreach (var p in pattern.EnumerateRunes())
        {
            var exact = Rune.IsUpper(p);
            while (true)
            {
                if (index >= runes.Length)
                {
                    return -1;
                }

                var c = runes[index++];
                if ((exact && c == p) || (!exact && Rune.ToLowerInvariant(c) == Rune.ToLowerInvariant(p)))
                {
                    break;
                }

                score++;
            }
        }

        return score;
    }

    /// <summary>
    /// Sort DeclarationInfos by ascending match score, then ascending case insensitive name, then
    /// ascending case sensitive name, and finally by source file name and position.
    /// Go: symbols.go:674-689。
    /// </summary>
    internal static int CompareDeclarationInfos(DeclarationInfo d1, DeclarationInfo d2)
    {
        if (d1._matchScore != d2._matchScore)
        {
            return d1._matchScore - d2._matchScore;
        }

        var c = CompareStringsCaseInsensitive(d1._name, d2._name);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(d1._name, d2._name);
        if (c != 0)
        {
            return c;
        }

        var s1 = GetSourceFileOfNode(d1._declaration);
        var s2 = GetSourceFileOfNode(d2._declaration);
        if (!ReferenceEquals(s1, s2))
        {
            return string.CompareOrdinal(s1!.Path().Value, s2!.Path().Value);
        }

        return d1._declaration.Pos() - d2._declaration.Pos();
    }

    /// <summary>
    /// getSymbolKindFromNode converts an AST node to an LSP SymbolKind.
    /// Combines getNodeKind with VS Code's fromProtocolScriptElementKind.
    /// Go: symbols.go:691-723。
    /// </summary>
    internal static SymbolKind GetSymbolKindFromNode(Node node)
    {
        switch (node.Kind)
        {
            case Kind.SourceFile:
                if (IsExternalModule(node.AsSourceFile()!))
                {
                    return SymbolKind.Module;
                }

                return SymbolKind.File;
            case Kind.ModuleDeclaration:
                return SymbolKind.Namespace;
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                return SymbolKind.Class;
            case Kind.InterfaceDeclaration:
                return SymbolKind.Interface;
            case Kind.TypeAliasDeclaration:
            case Kind.JSDocTypedefTag:
            case Kind.JSDocCallbackTag:
                return SymbolKind.Class;
            case Kind.EnumDeclaration:
                return SymbolKind.Enum;
            case Kind.VariableDeclaration:
                return SymbolKind.Variable;
            case Kind.ArrowFunction:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
                return SymbolKind.Function;
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                return SymbolKind.Property;
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
                return SymbolKind.Method;
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.SpreadAssignment:
            case Kind.IndexSignature:
                return SymbolKind.Property;
            case Kind.CallSignature:
                return SymbolKind.Method;
            case Kind.ConstructSignature:
                return SymbolKind.Constructor;
            case Kind.Constructor:
            case Kind.ClassStaticBlockDeclaration:
                return SymbolKind.Constructor;
            case Kind.TypeParameter:
                return SymbolKind.TypeParameter;
            case Kind.EnumMember:
                return SymbolKind.EnumMember;
            case Kind.Parameter:
                if (HasSyntacticModifier(node, ModifierFlags.ParameterPropertyModifier))
                {
                    return SymbolKind.Property;
                }

                return SymbolKind.Variable;
            case Kind.BinaryExpression:
            case Kind.CallExpression:
                var kind = GetAssignmentDeclarationKind(node);
                switch (kind)
                {
                    case JSDeclarationKind.ThisProperty:
                    case JSDeclarationKind.Property:
                    case JSDeclarationKind.ObjectDefinePropertyValue:
                        return SymbolKind.Property;
                }

                break;
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.NumericLiteral:
                // String literals used as property names (e.g., in Object.defineProperty)
                return SymbolKind.Property;
        }

        return SymbolKind.Variable;
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideDocumentSymbols(ctx context.Context, documentURI lsproto.DocumentUri) (lsproto.DocumentSymbolResponse, error)</c>（symbols.go:19-50）。</summary>
    public (SymbolInformationsOrDocumentSymbolsOrNull Response, Exception? Error) ProvideDocumentSymbols(Context ctx, DocumentUri documentURI)
    {
        var (_, file) = GetProgramAndFile(documentURI);
        List<SourceFile> projections = [file, .. file.SupplementalSourceFiles()];
        List<DocumentSymbol?> symbols = [];
        Set<DocumentSymbolKey> seen = new();
        foreach (var projection in projections)
        {
            foreach (var symbol in GetDocumentSymbolsForChildren(ctx, projection.AsNode(), projection))
            {
                var key = new DocumentSymbolKey
                {
                    _name = symbol!.Name,
                    _kind = symbol.Kind,
                    _range = symbol.Range,
                };
                if (seen.AddIfAbsent(key))
                {
                    symbols.Add(symbol);
                }
            }
        }

        if (GetClientCapabilities().TextDocument.DocumentSymbol.HierarchicalDocumentSymbolSupport)
        {
            return (new SymbolInformationsOrDocumentSymbolsOrNull { DocumentSymbols = [.. symbols] }, null);
        }

        // Client doesn't support hierarchical document symbols, return flat SymbolInformation array
        var symbolInfos = Ls.FlattenDocumentSymbols([.. symbols], documentURI);
        return (new SymbolInformationsOrDocumentSymbolsOrNull { SymbolInformations = [.. symbolInfos] }, null);
    }

    /// <summary>getDocumentSymbolInformations converts hierarchical DocumentSymbols to a flat SymbolInformation array（symbols.go:52-...）。</summary>
    internal List<SymbolInformation> GetDocumentSymbolInformations(Context ctx, SourceFile file, DocumentUri documentURI)
    {
        // First get hierarchical symbols
        var docSymbols = GetDocumentSymbolsForChildren(ctx, file.AsNode(), file);
        return Ls.FlattenDocumentSymbols([.. docSymbols], documentURI);
    }

    /// <summary>Go: <c>func (l *LanguageService) getDocumentSymbolsForChildren(ctx, node *ast.Node, file *ast.SourceFile) []*lsproto.DocumentSymbol</c>（symbols.go:82-260）。</summary>
    internal List<DocumentSymbol?> GetDocumentSymbolsForChildren(Context ctx, Node node, SourceFile file)
    {
        List<DocumentSymbol?> symbols = [];
        Set<string> expandoTargets = new();

        void AddSymbolForNode(Node node, Node? name, List<DocumentSymbol?>? children)
        {
            if ((node.Flags & NodeFlags.Reparsed) == 0)
            {
                var symbol = NewDocumentSymbol(node, name, children);
                if (symbol is not null)
                {
                    symbols.Add(symbol);
                }
            }
        }

        List<DocumentSymbol?> GetSymbolsForChildren(Node? node)
        {
            List<DocumentSymbol?> result = [];
            if (node is not null)
            {
                var saveExpandoTargets = expandoTargets;
                expandoTargets = new Set<string>();
                var saveSymbols = symbols;
                symbols = [];
                node.ForEachChild(Visit);
                result = symbols;
                symbols = saveSymbols;
                expandoTargets = saveExpandoTargets;
            }

            return result;
        }

        Action StartNode(Node? node, Node? name)
        {
            if (node is null)
            {
                return () => { };
            }

            var saveExpandoTargets = expandoTargets;
            expandoTargets = new Set<string>();
            var saveSymbols = symbols;
            symbols = [];
            return () =>
            {
                var result = symbols;
                symbols = saveSymbols;
                expandoTargets = saveExpandoTargets;
                AddSymbolForNode(node, name, result);
            };
        }

        List<DocumentSymbol?> GetSymbolsForNode(Node? node)
        {
            List<DocumentSymbol?> result = [];
            if (node is not null)
            {
                var saveSymbols = symbols;
                symbols = [];
                Visit(node);
                result = symbols;
                symbols = saveSymbols;
            }

            return result;
        }

        bool Visit(Node node)
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return true;
            }

            if ((node.Flags & NodeFlags.Reparsed) == 0)
            {
                var jsdocs = node.JSDoc(file) ?? [];
                if (jsdocs.Length > 0)
                {
                    foreach (var jsdoc in jsdocs)
                    {
                        var tagList = jsdoc.AsJSDoc()!.Tags;
                        if (tagList is not null)
                        {
                            foreach (var tag in tagList.Nodes ?? [])
                            {
                                if (IsJSDocTypedefTag(tag) || IsJSDocCallbackTag(tag))
                                {
                                    AddSymbolForNode(tag, null /*name*/, null /*children*/);
                                }
                            }
                        }
                    }
                }
            }

            switch (node.Kind)
            {
                case Kind.ClassDeclaration:
                case Kind.ClassExpression:
                case Kind.InterfaceDeclaration:
                case Kind.EnumDeclaration:
                    if (IsClassLike(node) && GetDeclarationName(node) != "")
                    {
                        expandoTargets.Add(GetDeclarationName(node));
                    }

                    AddSymbolForNode(node, null /*name*/, GetSymbolsForChildren(node));
                    break;
                case Kind.ModuleDeclaration:
                    AddSymbolForNode(node, null /*name*/, GetSymbolsForChildren(Ls.GetInteriorModule(node)));
                    break;
                case Kind.Constructor:
                    AddSymbolForNode(node, null /*name*/, GetSymbolsForChildren(node.Body()));
                    foreach (var param in node.Parameters() ?? [])
                    {
                        if (IsParameterPropertyDeclaration(param, node))
                        {
                            AddSymbolForNode(param, null /*name*/, null /*children*/);
                        }
                    }

                    break;
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                case Kind.ArrowFunction:
                case Kind.MethodDeclaration:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                    var declName = GetDeclarationName(node);
                    if (declName != "")
                    {
                        expandoTargets.Add(declName);
                    }

                    AddSymbolForNode(node, null /*name*/, GetSymbolsForChildren(node.Body()));
                    break;
                case Kind.VariableDeclaration:
                case Kind.BindingElement:
                case Kind.PropertyAssignment:
                case Kind.PropertyDeclaration:
                    var nodeName = node.Name();
                    if (nodeName is not null)
                    {
                        if (IsBindingPattern(nodeName))
                        {
                            Visit(nodeName);
                        }
                        else
                        {
                            AddSymbolForNode(node, null /*name*/, GetSymbolsForChildren(node.Initializer()));
                        }
                    }

                    break;
                case Kind.SpreadAssignment:
                    AddSymbolForNode(node, node.Expression(), null /*children*/);
                    break;
                case Kind.MethodSignature:
                case Kind.PropertySignature:
                case Kind.CallSignature:
                case Kind.ConstructSignature:
                case Kind.IndexSignature:
                case Kind.EnumMember:
                case Kind.ShorthandPropertyAssignment:
                case Kind.TypeAliasDeclaration:
                case Kind.ImportEqualsDeclaration:
                case Kind.ExportSpecifier:
                    AddSymbolForNode(node, null /*name*/, null /*children*/);
                    break;
                case Kind.ImportClause:
                    // Handle default import case e.g.:
                    //    import d from "mod";
                    if (node.Name() is not null)
                    {
                        AddSymbolForNode(node.Name()!, node.Name(), null /*children*/);
                    }

                    // Handle named bindings in imports e.g.:
                    //    import * as NS from "mod";
                    //    import {a, b as B} from "mod";
                    var namedBindings = node.AsImportClause()!.NamedBindings;
                    if (namedBindings is not null)
                    {
                        if (namedBindings.Kind == Kind.NamespaceImport)
                        {
                            AddSymbolForNode(namedBindings, null /*name*/, null /*children*/);
                        }
                        else
                        {
                            foreach (var element in namedBindings.Elements() ?? [])
                            {
                                AddSymbolForNode(element, null /*name*/, null /*children*/);
                            }
                        }
                    }

                    break;
                case Kind.BinaryExpression:
                case Kind.CallExpression:
                    var assignmentKind = GetAssignmentDeclarationKind(node);
                    switch (assignmentKind)
                    {
                        // `module.exports = ...`` should be reparsed into a JSExportAssignment,
                        // and `exports.a = ...`` into a CommonJSExport.
                        case JSDeclarationKind.None:
                        case JSDeclarationKind.ThisProperty:
                        case JSDeclarationKind.ModuleExports:
                        case JSDeclarationKind.ExportsProperty:
                        case JSDeclarationKind.ObjectDefinePropertyExports:
                            node.ForEachChild(Visit);
                            break;
                        case JSDeclarationKind.Property:
                        case JSDeclarationKind.ObjectDefinePropertyValue:
                            Node? target;
                            Node? targetFunction;
                            Node? definition;
                            Node? propertyName;
                            // `A.b = ... ` or `A.prototype.b = ...`
                            if (IsBinaryExpression(node))
                            {
                                var binaryExpr = node.AsBinaryExpression()!;
                                target = binaryExpr.Left;
                                targetFunction = target!.Expression();
                                definition = binaryExpr.Right;
                                // `A.b` or `A.prototype.b`
                                if (IsPropertyAccessExpression(target!))
                                {
                                    propertyName = target.AsPropertyAccessExpression()!.Name();
                                }
                                else
                                {
                                    // `A["b"]` or `A.prototype["b"]`
                                    propertyName = target!.AsElementAccessExpression()!.ArgumentExpression;
                                }
                            }
                            else
                            {
                                // `Object.defineProperty(A, "b", {...})`
                                var args = node.Arguments()!;
                                targetFunction = args[0];
                                target = args[1];
                                propertyName = target;
                                definition = args[2];
                            }

                            if (Ls.IsPrototypeExpando(targetFunction!))
                            {
                                targetFunction = targetFunction!.Expression();
                                // If we see a prototype assignment, start tracking the target as an expando target.
                                if (IsIdentifier(targetFunction!))
                                {
                                    expandoTargets.Add(targetFunction!.Text());
                                }
                            }

                            if (IsIdentifier(targetFunction!) && expandoTargets.Has(targetFunction!.Text()))
                            {
                                var endNode = StartNode(node, targetFunction);
                                AddSymbolForNode(target!, propertyName, GetSymbolsForNode(definition));
                                endNode();
                            }
                            else
                            {
                                node.ForEachChild(Visit);
                            }

                            break;
                    }

                    break;
                case Kind.ExportAssignment:
                    if (node.AsExportAssignment()!.IsExportEquals)
                    {
                        AddSymbolForNode(node, null /*name*/, GetSymbolsForNode(node.Expression()));
                    }
                    else
                    {
                        node.ForEachChild(Visit);
                    }

                    break;
                default:
                    node.ForEachChild(Visit);
                    break;
            }

            return false;
        }

        node.ForEachChild(Visit);
        return Ls.MergeExpandos(symbols);
    }

    /// <summary>Go: <c>func (l *LanguageService) newDocumentSymbol(node *ast.Node, name *ast.Node, children []*lsproto.DocumentSymbol) *lsproto.DocumentSymbol</c>（symbols.go:269-320）。</summary>
    internal DocumentSymbol? NewDocumentSymbol(Node node, Node? name, List<DocumentSymbol?>? children)
    {
        var result = new DocumentSymbol();
        var file = GetSourceFileOfNode(node)!;
        var nodeStartPos = SkipTrivia(file.Text(), node.Pos());
        name ??= GetNameOfDeclaration(node);
        string text;
        int nameStartPos;
        int nameEndPos;
        if (IsModuleDeclaration(node) && !IsAmbientModule(node))
        {
            text = Ls.GetModuleName(node);
            nameStartPos = SkipTrivia(file.Text(), name!.Pos());
            nameEndPos = Ls.GetInteriorModule(node).Name()!.End();
        }
        else if (IsAnyExportAssignment(node) && node.AsExportAssignment()!.IsExportEquals)
        {
            text = "export=";
            if (!NodeIsMissing(name))
            {
                nameStartPos = SkipTrivia(file.Text(), name!.Pos());
                nameEndPos = name!.End();
            }
            else
            {
                nameStartPos = nodeStartPos;
                nameEndPos = node.End();
            }
        }
        else if (name is not null)
        {
            text = Ls.GetTextOfName(name);
            nameStartPos = Math.Max(SkipTrivia(file.Text(), name.Pos()), nodeStartPos);
            nameEndPos = Math.Max(name.End(), nodeStartPos);
        }
        else
        {
            text = Ls.GetUnnamedNodeLabel(node);
            nameStartPos = nodeStartPos;
            nameEndPos = nodeStartPos;
        }

        if (text == "")
        {
            return null;
        }

        var truncatedText = TruncateByRunes(text, Ls.MaxLength);
        if (truncatedText.Length < text.Length)
        {
            text = truncatedText + "...";
        }

        result.Name = text;
        result.Kind = Ls.GetSymbolKindFromNode(node);
        var (selectionRange, selectionFidelity) = _converters!.ToLSPRangeForFeature(file, new TextRange(nameStartPos, nameEndPos), Feature.DocumentSymbols);
        if (!selectionFidelity.IsSingleSegment())
        {
            return null;
        }

        var (symbolRange, rangeFidelity) = _converters.ToLSPRangeForFeature(file, new TextRange(nodeStartPos, node.End()), Feature.DocumentSymbols);
        if (rangeFidelity.IsNone())
        {
            symbolRange = selectionRange;
        }

        result.Range = symbolRange;
        result.SelectionRange = selectionRange;
        children ??= [];
        result.Children = [.. children];
        return result;
    }
}
