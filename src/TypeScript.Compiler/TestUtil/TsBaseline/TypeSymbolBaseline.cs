// ported from: internal/testutil/tsbaseline/type_symbol_baseline.go
//
// 与 Go 的差异（逐条）：
// 1) `*testing.T` → `IHarnessTest`。`defer testutil.RecoverAndFail(t, msg)`（Go 的 recover 把 panic 转成 t.Fatalf）
//    → `try/catch (DebugFailureException)`：Go 的 `t.Fatalf` 是 runtime.Goexit（**不能**被 recover 捕获），
//    所以这里只捕获"panic 语义"的 `DebugFailureException`（Debug.Fail 抛的那个），不吞 TestFailureException。
// 2) `perfLines` 的"性能统计"段在 Go 里整段被注释掉（本文件顶部原样保留注释），因此 `strings.Join(perfLines, "\n")`
//    恒为 ""：C# 侧同样不产出该段，只保留 `//// [header] ////\r\n\r\n` 头。
// 3) `checker.Type` 与 `System.Type`、`printer` 包与变量同名等冲突照仓库既有别名处理（`CheckerType`、`nodePrinter`）。
// 4) Go 的 `node.Kind`/`NodeFlags` 常量 → C# 枚举成员（`Kind.Identifier`、`NodeFlags.Reparsed`）。
// 5) `bracketLineRegex` 的 `\s`：Go 的 `\s` 是 Perl 类 `[\t\n\f\r ]`（.NET 的 `\s` 是 Unicode 语义），
//    所以显式写成字符类，保证与 Go 逐字一致。
// 6) `strings.TrimSpace` → `Trim()`（与 Go 的 unicode.IsSpace 只差极少数字符；本判定用于"该行是否只是空行/括号行"，
//    两种语义在真实用例上一致）。
// 7) `result.line` 等字段名沿用 Go（PascalCase：`Line`/`SourceText`/`Symbol`/`Typ`/`Underline`）。
// 8) Go 的 `typeWriterWalker.declarationTextCache map[*ast.Node]string` → `Dictionary<Node, string>`（Node 未覆写
//    Equals/GetHashCode，所以键就是**引用标识**，与 Go 的指针键相同）。

namespace TypeScript.TestUtil.TsBaseline;

using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.TestUtil.Baseline;
using TypeScript.TestUtil.HarnessUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using CheckerType = TypeScript.Checker.Type;
using Program = TypeScript.Compiler.Program;

public static partial class TsBaseline
{
    private static readonly Regex _codeLinesRegexp = new("[\r\u2028\u2029]|\r?\n", RegexOptions.Compiled);

    // Go 的 `^\s*[{|}]\s*$`，其中 `\s` 是 Perl 类 [\t\n\f\r ]（见文件头第 5 条）。
    private static readonly Regex _bracketLineRegex = new(@"^[\t\n\f\r ]*[{|}][\t\n\f\r ]*$", RegexOptions.Compiled);

    private static readonly Regex _lineEndRegex = new(@"\r?\n", RegexOptions.Compiled);

    /// <summary>Go: <c>func DoTypeAndSymbolBaseline(...)</c>。</summary>
    public static void DoTypeAndSymbolBaseline(
        IHarnessTest t,
        string baselinePath,
        string header,
        ProgramLike program,
        TestFile[] allFiles,
        BaselineOptions opts,
        bool skipTypeBaselines,
        bool skipSymbolBaselines,
        bool hasErrorBaseline)
    {
        // The full walker simulates the types that you would get from doing a full
        // compile.  The pull walker simulates the types you get when you just do
        // a type query for a random node (like how the LS would do it).  Most of the
        // time, these will be the same.  However, occasionally, they can be different.
        // Specifically, when the compiler internally depends on symbol IDs to order
        // things, then we may see different results because symbols can be created in a
        // different order with 'pull' operations, and thus can produce slightly differing
        // output.
        //
        // For example, with a full type check, we may see a type displayed as: number | string
        // But with a pull type check, we may see it as:                        string | number
        //
        // These types are equivalent, but depend on what order the compiler observed
        // certain parts of the program.
        var fullWalker = NewTypeWriterWalker(program, hasErrorBaseline);

        t.Run("type", subTest =>
        {
            // Go: defer testutil.RecoverAndFail(t, "Panic on creating type baseline for test "+header)
            try
            {
                // !!! Remove once the type baselines print node reuse lines
                var typesOpts = CopyOptions(opts);
                typesOpts.DiffFixupOld = s =>
                {
                    var sb = new StringBuilder(s.Length);

                    var perfStats = false;
                    foreach (var rawLine in s.Split('\n'))
                    {
                        var line = rawLine;
                        if (IsTypeBaselineNodeReuseLine(line))
                        {
                            continue;
                        }

                        if (!perfStats && line.StartsWith("=== Performance Stats ===", StringComparison.Ordinal))
                        {
                            perfStats = true;
                            continue;
                        }
                        else if (perfStats)
                        {
                            if (line.StartsWith("=== ", StringComparison.Ordinal))
                            {
                                perfStats = false;
                            }
                            else
                            {
                                continue;
                            }
                        }

                        const string relativePrefixNew = "=== ";
                        const string relativePrefixOld = relativePrefixNew + "./";
                        if (line.StartsWith(relativePrefixOld, StringComparison.Ordinal))
                        {
                            line = relativePrefixNew + line[relativePrefixOld.Length..];
                        }

                        sb.Append(line);
                        sb.Append('\n');
                    }

                    return sb.ToString()[..^1];
                };

                CheckBaselines(subTest, baselinePath, allFiles, fullWalker, header, typesOpts, false /*isSymbolBaseline*/);
            }
            catch (DebugFailureException ex)
            {
                subTest.Fatal($"Panic on creating type baseline for test {header}:\n{ex.Message}\n{ex.StackTrace}");
            }
        });
        t.Run("symbol", subTest =>
        {
            // Go: defer testutil.RecoverAndFail(t, "Panic on creating symbol baseline for test "+header)
            try
            {
                CheckBaselines(subTest, baselinePath, allFiles, fullWalker, header, opts, true /*isSymbolBaseline*/);
            }
            catch (DebugFailureException ex)
            {
                subTest.Fatal($"Panic on creating symbol baseline for test {header}:\n{ex.Message}\n{ex.StackTrace}");
            }
        });
    }

    /// <summary>Go: <c>typesOpts := opts</c>（Go 的结构体取值拷贝）→ C# 侧显式复制一份。</summary>
    private static BaselineOptions CopyOptions(BaselineOptions opts) => new()
    {
        Subfolder = opts.Subfolder,
        DiffFixupOld = opts.DiffFixupOld,
        DiffFixupNew = opts.DiffFixupNew,
        SkipDiffWithOld = opts.SkipDiffWithOld,
    };

    /// <summary>Go: <c>func isTypeBaselineNodeReuseLine(line string) bool</c>。</summary>
    private static bool IsTypeBaselineNodeReuseLine(string line)
    {
        if (!line.StartsWith('>'))
        {
            return false;
        }

        // Go: line, ok := strings.CutPrefix(line, ">") 之后又 line[1:]（跳过 '>' 后面的那一个字符）
        line = line[1..];
        if (line.Length == 0)
        {
            return false;
        }

        line = line[1..].TrimStart(' ');
        if (!line.StartsWith(':'))
        {
            return false;
        }

        line = line[1..];

        foreach (var c in line)
        {
            switch (c)
            {
                case ' ' or '^' or '\r':
                    // Okay
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>Go: <c>func checkBaselines(t *testing.T, baselinePath string, allFiles []*harnessutil.TestFile, fullWalker *typeWriterWalker, header string, opts baseline.Options, isSymbolBaseline bool)</c>。</summary>
    private static void CheckBaselines(
        IHarnessTest t,
        string baselinePath,
        TestFile[] allFiles,
        TypeWriterWalker fullWalker,
        string header,
        BaselineOptions opts,
        bool isSymbolBaseline)
    {
        var fullExtension = IfElse(isSymbolBaseline, ".symbols", ".types");
        var outputFileName = _tsExtension.Replace(baselinePath, fullExtension);
        var fullBaseline = GenerateBaseline(allFiles, fullWalker, header, isSymbolBaseline);
        Baseline.Run(t, outputFileName, fullBaseline, opts);
    }

    /// <summary>Go: <c>func generateBaseline(allFiles []*harnessutil.TestFile, fullWalker *typeWriterWalker, header string, isSymbolBaseline bool) string</c>。</summary>
    private static string GenerateBaseline(
        TestFile[] allFiles,
        TypeWriterWalker fullWalker,
        string header,
        bool isSymbolBaseline)
    {
        var result = new StringBuilder();

        // !!! Perf baseline（Go 里整段被注释掉，见文件头第 2 条）
        // var perfLines []string
        // prePerformanceValues := getPerformanceBaselineValues()
        var baselines = IterateBaseline(allFiles, fullWalker, isSymbolBaseline);
        foreach (var value in baselines)
        {
            result.Append(value);
        }

        // postPerformanceValues := getPerformanceBaselineValues()

        if (!isSymbolBaseline)
        {
            // !!! Perf baselines
            // const perfStats: [name: string, reportThreshold: number, beforeValue: number, afterValue: number][] = [];
            // perfStats.push(["Strict subtype cache", 1000, prePerformanceValues.strictSubtype, postPerformanceValues.strictSubtype]);
            // perfStats.push(["Subtype cache", 1000, prePerformanceValues.subtype, postPerformanceValues.subtype]);
            // perfStats.push(["Identity cache", 1000, prePerformanceValues.identity, postPerformanceValues.identity]);
            // perfStats.push(["Assignability cache", 1000, prePerformanceValues.assignability, postPerformanceValues.assignability]);
            // perfStats.push(["Type Count", 1000, prePerformanceValues.typeCount, postPerformanceValues.typeCount]);
            // perfStats.push(["Instantiation count", 1500, prePerformanceValues.instantiation, postPerformanceValues.instantiation]);
            // perfStats.push(["Symbol count", 45000, prePerformanceValues.symbol, postPerformanceValues.symbol]);
            //
            // if (perfStats.some(([, threshold, , postValue]) => postValue >= threshold)) {
            // 	perfLines.push(`=== Performance Stats ===`);
            // 	for (const [name, threshold, preValue, postValue] of perfStats) {
            // 		if (postValue >= threshold) {
            // 			const preString = valueToString(preValue);
            // 			const postString = valueToString(postValue);
            // 			if (preString === postString) {
            // 				perfLines.push(`${name}: ${preString}`);
            // 			}
            // 			else {
            // 				perfLines.push(`${name}: ${preString} -> ${postString}`);
            // 			}
            // 		}
            // 	}
            // 	perfLines.push("");
            // 	perfLines.push("");
            // }
        }

        // Go: strings.Join(perfLines, "\n")，perfLines 恒为 nil ⇒ ""（见文件头第 2 条）。
        if (result.Length > 0)
        {
            return $"//// [{header}] ////\r\n\r\n{result}";
        }

        return Baseline.NoContent;
    }

    /// <summary>Go: <c>func iterateBaseline(allFiles []*harnessutil.TestFile, fullWalker *typeWriterWalker, isSymbolBaseline bool) []string</c>。</summary>
    private static string[] IterateBaseline(TestFile[] allFiles, TypeWriterWalker fullWalker, bool isSymbolBaseline)
    {
        var baselines = new List<string>();

        foreach (var file in allFiles)
        {
            var unitName = file.UnitName;
            var typeLines = new StringBuilder();
            typeLines.Append("=== ");
            typeLines.Append(unitName);
            typeLines.Append(" ===\r\n");
            var codeLines = _codeLinesRegexp.Split(file.Content);
            var results = isSymbolBaseline ? fullWalker.GetSymbols(unitName) : fullWalker.GetTypes(unitName);
            var lastIndexWritten = -1;
            foreach (var result in results)
            {
                if (isSymbolBaseline && result.Symbol == "")
                {
                    return [.. baselines];
                }

                if (lastIndexWritten == -1)
                {
                    typeLines.Append(string.Join("\r\n", codeLines[..(result.Line + 1)]));
                    typeLines.Append("\r\n");
                }
                else if (lastIndexWritten != result.Line)
                {
                    if (!(lastIndexWritten + 1 < codeLines.Length
                        && (_bracketLineRegex.IsMatch(codeLines[lastIndexWritten + 1])
                            || codeLines[lastIndexWritten + 1].Trim() == "")))
                    {
                        typeLines.Append("\r\n");
                    }

                    typeLines.Append(string.Join("\r\n", codeLines[(lastIndexWritten + 1)..(result.Line + 1)]));
                    typeLines.Append("\r\n");
                }

                lastIndexWritten = result.Line;
                var typeOrSymbolString = IfElse(isSymbolBaseline, result.Symbol, result.Typ);
                var lineText = _lineDelimiter.Replace(result.SourceText, "");
                typeLines.Append('>');
                typeLines.Append($"{lineText} : {typeOrSymbolString}");
                typeLines.Append("\r\n");
                if (result.Underline != "")
                {
                    typeLines.Append('>');
                    for (var i = 0; i < lineText.Length; i++)
                    {
                        typeLines.Append(' ');
                    }

                    typeLines.Append(" : ");
                    typeLines.Append(result.Underline);
                    typeLines.Append("\r\n");
                }
            }

            if (lastIndexWritten + 1 < codeLines.Length)
            {
                if (!(lastIndexWritten + 1 < codeLines.Length
                    && (_bracketLineRegex.IsMatch(codeLines[lastIndexWritten + 1])
                        || codeLines[lastIndexWritten + 1].Trim() == "")))
                {
                    typeLines.Append("\r\n");
                }

                typeLines.Append(string.Join("\r\n", codeLines[(lastIndexWritten + 1)..]));
            }

            typeLines.Append("\r\n");

            baselines.Add(RemoveTestPathPrefixes(typeLines.ToString(), false /*retainTrailingDirectorySeparator*/));
        }

        return [.. baselines];
    }

    /// <summary>Go: <c>func newTypeWriterWalker(program compiler.ProgramLike, hadErrorBaseline bool) *typeWriterWalker</c>。</summary>
    private static TypeWriterWalker NewTypeWriterWalker(ProgramLike program, bool hadErrorBaseline)
    {
        return new TypeWriterWalker(program, hadErrorBaseline);
    }

    /// <summary>Go: <c>type typeWriterWalker struct</c>。</summary>
    private sealed class TypeWriterWalker
    {
        private readonly ProgramLike _program;
        private readonly bool _hadErrorBaseline;
        private SourceFile? _currentSourceFile;
        private readonly Dictionary<Node, string> _declarationTextCache = [];

        /// <summary>Go: <c>func newTypeWriterWalker(program compiler.ProgramLike, hadErrorBaseline bool) *typeWriterWalker</c>。</summary>
        public TypeWriterWalker(ProgramLike program, bool hadErrorBaseline)
        {
            _program = program;
            _hadErrorBaseline = hadErrorBaseline;
        }

        /// <summary>Go: <c>func (walker *typeWriterWalker) getTypeCheckerForCurrentFile() (*checker.Checker, func())</c>。</summary>
        public (Checker Checker, Action Done) GetTypeCheckerForCurrentFile()
        {
            // If we don't use the right checker for the file, its contents won't be up to date
            // since the types/symbols baselines appear to depend on files having been checked.
            return ProgramOf(_program).GetTypeCheckerForFile(Context.Background, _currentSourceFile!);
        }

        /// <summary>Go: <c>func (walker *typeWriterWalker) getTypes(filename string) []*typeWriterResult</c>。</summary>
        public List<TypeWriterResult> GetTypes(string filename)
        {
            var sourceFile = _program.GetSourceFile(filename)!;
            _currentSourceFile = sourceFile;
            return VisitNode(sourceFile.AsNode(), false /*isSymbolWalk*/);
        }

        /// <summary>Go: <c>func (walker *typeWriterWalker) getSymbols(filename string) []*typeWriterResult</c>。</summary>
        public List<TypeWriterResult> GetSymbols(string filename)
        {
            var sourceFile = _program.GetSourceFile(filename)!;
            _currentSourceFile = sourceFile;
            return VisitNode(sourceFile.AsNode(), true /*isSymbolWalk*/);
        }

        /// <summary>Go: <c>func (walker *typeWriterWalker) visitNode(node *ast.Node, isSymbolWalk bool) []*typeWriterResult</c>。</summary>
        private List<TypeWriterResult> VisitNode(Node node, bool isSymbolWalk)
        {
            var nodes = ForEachASTNode(node);
            var results = new List<TypeWriterResult>();
            foreach (var n in nodes)
            {
                if (IsExpressionNode(n) || n.Kind == Kind.Identifier || IsDeclarationName(n)
                    || (IsQualifiedName(n) && IsNameOfHeritageClauseTypeReference(n) && (isSymbolWalk || IsQualifiedName(n.Parent!))))
                {
                    var result = WriteTypeOrSymbol(n, isSymbolWalk);
                    if (result is not null)
                    {
                        results.Add(result);
                    }
                }
            }

            return results;
        }

        /// <summary>Go: <c>func (walker *typeWriterWalker) writeTypeOrSymbol(node *ast.Node, isSymbolWalk bool) *typeWriterResult</c>。</summary>
        private TypeWriterResult? WriteTypeOrSymbol(Node node, bool isSymbolWalk)
        {
            var actualPos = TypeScript.Scanner.Scanner.SkipTrivia(_currentSourceFile!.Text(), node.Pos());
            var line = TypeScript.Scanner.Scanner.GetECMALineOfPosition(_currentSourceFile, actualPos);
            var sourceText = TypeScript.Scanner.Scanner.GetSourceTextOfNodeFromSourceFile(_currentSourceFile, node, false /*includeTrivia*/);
            var (fileChecker, done) = GetTypeCheckerForCurrentFile();
            try
            {
                var (ctx, putCtx) = TypeScript.Printer.Printer.GetEmitContext();
                try
                {
                    if (!isSymbolWalk)
                    {
                        // Don't try to get the type of something that's already a type.
                        // Exception for `T` in `type T = something` because that may evaluate to some interesting type.
                        if (IsPartOfTypeNode(node)
                            || ((node.Kind == Kind.AsExpression || node.Kind == Kind.SatisfiesExpression)
                                && (node.Type()!.Flags & NodeFlags.Reparsed) != 0)
                            || (IsIdentifier(node)
                                && (GetMeaningFromDeclaration(node.Parent) & SemanticMeaning.Value) == 0
                                && !(IsTypeOrJSTypeAliasDeclaration(node.Parent!) && node == node.Parent!.Name())))
                        {
                            return null;
                        }

                        if (IsOmittedExpression(node))
                        {
                            return null;
                        }

                        CheckerType? t = null;

                        // Workaround to ensure we output 'C' instead of 'typeof C' for base class expressions
                        if (IsExpressionWithTypeArgumentsInClassExtendsClause(node.Parent))
                        {
                            t = fileChecker.GetTypeAtLocation(node.Parent!);
                        }

                        if (t is null || Checker.IsTypeAny(t))
                        {
                            t = fileChecker.GetTypeAtLocation(node);
                        }

                        string typeString;

                        // var underline string
                        if (!_hadErrorBaseline
                            && Checker.IsTypeAny(t)
                            && !IsBindingElement(node.Parent!)
                            && !IsPropertyAccessOrQualifiedName(node.Parent)
                            && !IsLabelName(node)
                            && !IsGlobalScopeAugmentation(node.Parent)
                            && !IsMetaProperty(node.Parent!)
                            && !IsImportStatementName(node)
                            && !IsExportStatementName(node)
                            && !IsIntrinsicJsxTag(node, _currentSourceFile))
                        {
                            typeString = t.AsIntrinsicType()!.IntrinsicName();
                        }
                        else
                        {
                            ctx.Reset();
                            var builder = Checker.NewNodeBuilder(fileChecker, ctx);
                            const TypeFormatFlags typeFormatFlags = TypeFormatFlags.NoTruncation | TypeFormatFlags.AllowUniqueESSymbolType | TypeFormatFlags.GenerateNamesForShadowedTypeParams;
                            var typeNode = builder.TypeToTypeNode(
                                t,
                                node.Parent,
                                (Flags)((uint)typeFormatFlags & (uint)TypeFormatFlags.NodeBuilderFlagsMask) | Flags.IgnoreErrors,
                                InternalFlags.AllowUnresolvedNames,
                                null);
                            if (IsIdentifier(node) && IsTypeAliasDeclaration(node.Parent!) && node.Parent!.Name() == node && typeNode is not null && IsIdentifier(typeNode) && typeNode.Text() == node.Text())
                            {
                                // for a complex type alias `type T = ...`, showing "T : T" isn't very helpful for type tests. When the type produced is the same as
                                // the name of the type alias, recreate the type string without reusing the alias name
                                typeNode = builder.TypeToTypeNode(
                                    t,
                                    node.Parent,
                                    (Flags)((uint)(typeFormatFlags | TypeFormatFlags.InTypeAlias) & (uint)TypeFormatFlags.NodeBuilderFlagsMask) | Flags.IgnoreErrors,
                                    InternalFlags.AllowUnresolvedNames,
                                    null);
                            }

                            // !!! TODO: port underline printer, memoize
                            var writer = TypeScript.Printer.Printer.NewTextWriter("", 0);
                            var nodePrinter = TypeScript.Printer.Printer.NewPrinter(new TypeScript.Printer.PrinterOptions { RemoveComments = true }, new TypeScript.Printer.PrintHandlers(), ctx);
                            nodePrinter.Write(typeNode!, _currentSourceFile, writer, null);
                            typeString = writer.String();
                        }

                        return new TypeWriterResult
                        {
                            Line = line,
                            SourceText = sourceText,
                            Typ = typeString,

                            // underline: underline, // !!! TODO: underline
                        };
                    }

                    var symbol = fileChecker.GetSymbolAtLocation(node);
                    if (symbol is null)
                    {
                        return null;
                    }

                    var symbolString = new StringBuilder();
                    symbolString.Append("Symbol(");
                    symbolString.Append(EscapeAllInternalSymbolNames(fileChecker.SymbolToStringEx(symbol, node.Parent, SymbolFlags.None, SymbolFormatFlags.AllowAnyNodeKind)));
                    var count = 0;
                    foreach (var declaration in symbol.Declarations ?? [])
                    {
                        if (count >= 5)
                        {
                            symbolString.Append($" ... and {symbol.Declarations!.Length - count} more");
                            break;
                        }

                        count++;
                        symbolString.Append(", ");
                        if (_declarationTextCache.TryGetValue(declaration, out var declText))
                        {
                            symbolString.Append(declText);
                            continue;
                        }

                        var declSourceFile = GetSourceFileOfNode(declaration)!;
                        var (declLine, declChar) = TypeScript.Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition(declSourceFile, declaration.Pos());
                        var fileName = GetBaseFileName(declSourceFile.FileName());
                        symbolString.Append("Decl(");
                        symbolString.Append(fileName);
                        symbolString.Append(", ");
                        if (IsDefaultLibraryFile(fileName))
                        {
                            symbolString.Append("--, --)");
                        }
                        else
                        {
                            symbolString.Append($"{declLine}, {(int)declChar})");
                        }
                    }

                    symbolString.Append(')');
                    return new TypeWriterResult
                    {
                        Line = line,
                        SourceText = sourceText,
                        Symbol = symbolString.ToString(),
                    };
                }
                finally
                {
                    putCtx();
                }
            }
            finally
            {
                done();
            }
        }
    }

    /// <summary>Go: <c>func forEachASTNode(node *ast.Node) []*ast.Node</c>。</summary>
    private static List<Node> ForEachASTNode(Node node)
    {
        var result = new List<Node>();
        var work = new List<Node> { node };

        var resChildren = new List<Node>();

        bool AddChild(Node child)
        {
            resChildren.Add(child);
            return false;
        }

        while (work.Count > 0)
        {
            var elem = work[^1];
            work.RemoveAt(work.Count - 1);
            if ((elem.Flags & NodeFlags.Reparsed) == 0
                || elem.Kind == Kind.AsExpression
                || elem.Kind == Kind.SatisfiesExpression
                || ((elem.Parent!.Kind == Kind.SatisfiesExpression || elem.Parent.Kind == Kind.AsExpression) && elem == elem.Parent.Expression()))
            {
                if ((elem.Flags & NodeFlags.Reparsed) == 0 || elem.Parent!.Kind == Kind.AsExpression || elem.Parent.Kind == Kind.SatisfiesExpression)
                {
                    result.Add(elem);
                }

                elem.ForEachChild(AddChild);
                resChildren.Reverse();
                work.AddRange(resChildren);
                resChildren.Clear();
            }
        }

        return result;
    }

    /// <summary>Go: <c>type typeWriterResult struct</c>。</summary>
    private sealed class TypeWriterResult
    {
        public int Line;
        public string SourceText = "";
        public string Symbol = "";
        public string Typ = "";

        /// <summary>Go: <c>underline string // !!!</c>（Go 侧恒为 ""，见 writeTypeOrSymbol 的注释）。</summary>
        public string Underline = "";
    }

    /// <summary>Go: <c>func isImportStatementName(node *ast.Node) bool</c>。</summary>
    private static bool IsImportStatementName(Node node)
    {
        if (IsImportSpecifier(node.Parent!) && (node == node.Parent!.Name() || node == node.Parent.PropertyName()))
        {
            return true;
        }

        if (IsImportClause(node.Parent!) && node == node.Parent!.Name())
        {
            return true;
        }

        if (IsImportEqualsDeclaration(node.Parent!) && node == node.Parent!.Name())
        {
            return true;
        }

        return false;
    }

    /// <summary>Go: <c>func isExportStatementName(node *ast.Node) bool</c>。</summary>
    private static bool IsExportStatementName(Node node)
    {
        if (IsExportAssignment(node.Parent!) && node == node.Parent!.Expression())
        {
            return true;
        }

        if (IsExportSpecifier(node.Parent!) && (node == node.Parent!.Name() || node == node.Parent.PropertyName()))
        {
            return true;
        }

        return false;
    }

    /// <summary>Go: <c>func isIntrinsicJsxTag(node *ast.Node, sourceFile *ast.SourceFile) bool</c>。</summary>
    private static bool IsIntrinsicJsxTag(Node node, SourceFile sourceFile)
    {
        if (!(IsJsxOpeningElement(node.Parent!) || IsJsxClosingElement(node.Parent!) || IsJsxSelfClosingElement(node.Parent!)))
        {
            return false;
        }

        if (node.Parent!.TagName() != node)
        {
            return false;
        }

        var text = TypeScript.Scanner.Scanner.GetSourceTextOfNodeFromSourceFile(sourceFile, node, false /*includeTrivia*/);
        return TypeScript.Scanner.Scanner.IsIntrinsicJsxName(text);
    }
}
