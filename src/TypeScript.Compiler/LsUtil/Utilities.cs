// ported from: internal/ls/lsutil/utilities.go
//
// **本文件补齐 `internal/ls/lsutil` 的缺口**（此前只移植了 Asi.cs/Children.cs/CompletedNode.cs/FormatCodeOptions.cs）。
// 与 Go 的偏离（C# 语言特性；依据 docs/architecture.md §2/§3）：
// - 包级函数 → 同名静态类 `LsUtil` 的 `partial` 分片（§2）。
// - Go 的 `var visit func(node *ast.Node) bool` 自递归闭包 → C# 局部函数（同样按引用捕获计数器，语义一致）。
// - `core.NodeCoreModules()[name]`（Go map 取值缺键为 false）→ `NodeCoreModules.GetValueOrDefault(name)`
//   （C# 的 Dictionary 索引器缺键会抛；与 Checker.C4.ModuleSpecifiers.cs 的既有处理一致）。
// - `core.ExclusivelyPrefixedNodeCoreModules[name]`（同上）→ 同一个 GetValueOrDefault 写法。
// - `strings.HasPrefix(s, "node:")` → `StringUtil.HasPrefix(s, "node:", true /*caseSensitive*/)`（Core 里没有 HasPrefix）。
// - `[]rune(baseName)` 的逐 rune 循环 → C# 的码点迭代（UTF-16 代理对合成一个码点，§3.1）：Go 里 `[]rune(string)`
//   会把非法 UTF-8 解成 U+FFFD，C# 的 string 本身是 UTF-16（可能含孤立代理项），孤立代理项在这里按"它就是那个
//   码点值"处理（与 FsWatch/PathCompare.cs 的既有做法一致，见那里的 ToUpperCodePoint）。
// - `unicode.ToUpper(r rune) rune`（simple 映射）→ `Rune.ToUpperInvariant`（>0xFFFF）与 `char.ToUpperInvariant`
//   （BMP）的分派（§3.4 与 FsWatch/PathCompare.cs 的既有写法；Rune 表示不了孤立代理项）。
// - `(T, bool)` 形态的函数这里没有；`core.Find` 的 nil 结果对应 C# 的 `Node?`。
using System.Text;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.LsUtil;

public static partial class LsUtil
{
    /// <summary>Go 的 <c>func ProbablyUsesSemicolons(file *ast.SourceFile) bool</c>。</summary>
    public static bool ProbablyUsesSemicolons(SourceFile file)
    {
        var withSemicolon = 0;
        var withoutSemicolon = 0;
        const int nStatementsToObserve = 5;

        bool Visit(Node node)
        {
            if ((node.Flags & NodeFlags.Reparsed) != 0)
            {
                return false;
            }

            if (SyntaxRequiresTrailingSemicolonOrASI(node.Kind))
            {
                var lastToken = GetLastToken(node, file);
                if (lastToken is not null && lastToken.Kind == Kind.SemicolonToken)
                {
                    withSemicolon++;
                }
                else
                {
                    withoutSemicolon++;
                }
            }
            else if (SyntaxRequiresTrailingCommaOrSemicolonOrASI(node.Kind))
            {
                var lastToken = GetLastToken(node, file);
                if (lastToken is not null && lastToken.Kind == Kind.SemicolonToken)
                {
                    withSemicolon++;
                }
                else if (lastToken is not null && lastToken.Kind != Kind.CommaToken)
                {
                    var lastTokenLine = GetECMALineOfPosition(file, GetStartOfNode(lastToken, file, false /*includeJSDoc*/));
                    var nextTokenLine = GetECMALineOfPosition(file, SkipTrivia(file.Text(), lastToken.End()));
                    // Avoid counting missing semicolon in single-line objects:
                    // `function f(p: { x: string /*no semicolon here is insignificant*/ }) {`
                    if (lastTokenLine != nextTokenLine)
                    {
                        withoutSemicolon++;
                    }
                }
            }

            if (withSemicolon + withoutSemicolon >= nStatementsToObserve)
            {
                return true;
            }

            return node.ForEachChild(Visit);
        }

        file.ForEachChild(Visit);

        // One statement missing a semicolon isn't sufficient evidence to say the user
        // doesn't want semicolons, because they may not even be done writing that statement.
        if (withSemicolon == 0 && withoutSemicolon <= 1)
        {
            return true;
        }

        // When both kinds of observation exist, treat the file as using semicolons when the
        // ratio withSemicolon/withoutSemicolon exceeds 1/nStatementsToObserve (real arithmetic),
        // implemented as an integer inequality to avoid truncation.
        if (withoutSemicolon == 0)
        {
            return true;
        }

        return withSemicolon * nStatementsToObserve > withoutSemicolon;
    }

    /// <summary>Go 的 <c>func ShouldUseUriStyleNodeCoreModules(file *ast.SourceFile, program *compiler.Program) core.Tristate</c>。</summary>
    public static Tristate ShouldUseUriStyleNodeCoreModules(SourceFile file, Program program)
    {
        foreach (var node in file.Imports())
        {
            if (NodeCoreModules.GetValueOrDefault(node.Text()) && !ExclusivelyPrefixedNodeCoreModules.GetValueOrDefault(node.Text()))
            {
                if (HasPrefix(node.Text(), "node:", true /*caseSensitive*/))
                {
                    return Tristate.TSTrue;
                }

                return Tristate.TSFalse;
            }
        }

        return program.UsesUriStyleNodeCoreModules();
    }

    /// <summary>Go 的 <c>func QuotePreferenceFromString(str *ast.StringLiteral) QuotePreference</c>。</summary>
    public static QuotePreference QuotePreferenceFromString(StringLiteral str)
    {
        if ((str.TokenFlags & TokenFlags.SingleQuote) != 0)
        {
            return QuotePreferenceSingle;
        }

        return QuotePreferenceDouble;
    }

    /// <summary>Go 的 <c>func GetQuotePreference(sourceFile *ast.SourceFile, preferences UserPreferences) QuotePreference</c>。</summary>
    public static QuotePreference GetQuotePreference(SourceFile sourceFile, UserPreferences preferences)
    {
        if (preferences.QuotePreference != QuotePreferenceUnknown && preferences.QuotePreference != QuotePreferenceAuto)
        {
            if (preferences.QuotePreference == QuotePreferenceSingle)
            {
                return QuotePreferenceSingle;
            }

            return QuotePreferenceDouble;
        }

        // ignore synthetic import added when importHelpers: true
        var firstModuleSpecifier = Find(sourceFile.Imports(), n => IsStringLiteral(n) && !NodeIsSynthesized(n.Parent));
        if (firstModuleSpecifier is not null)
        {
            // `Find` 的谓词已确认是字符串字面量（Go 的 AsStringLiteral 在类型不符时 panic；见 Checker/NodeCopy.cs 的同类写法）。
            return QuotePreferenceFromString(firstModuleSpecifier.AsStringLiteral()!);
        }

        return QuotePreferenceDouble;
    }

    /// <summary>Go 的 <c>func ModuleSymbolToValidIdentifier(moduleSymbol *ast.Symbol, forceCapitalize bool) string</c>。</summary>
    public static string ModuleSymbolToValidIdentifier(Symbol moduleSymbol, bool forceCapitalize)
    {
        var moduleName = moduleSymbol.Name;
        if (TryGetAmbientModuleNameFromSymbolName(moduleName, out var ambientModuleName))
        {
            moduleName = ambientModuleName;
        }

        return ModuleSpecifierToValidIdentifier(moduleName, forceCapitalize);
    }

    /// <summary>Go 的 <c>func ModuleSpecifierToValidIdentifier(moduleSpecifier string, forceCapitalize bool) string</c>。</summary>
    public static string ModuleSpecifierToValidIdentifier(string moduleSpecifier, bool forceCapitalize)
    {
        var baseName = GetBaseFileName(TrimSuffix(RemoveAnyFileExtension(moduleSpecifier), "/index"));
        var res = new StringBuilder();
        var lastCharWasValid = true;
        var baseNameRunes = ToCodePoints(baseName);
        if (baseNameRunes.Length > 0 && IsIdentifierStart(baseNameRunes[0]))
        {
            AppendCodePoint(res, forceCapitalize ? ToUpperCodePoint(baseNameRunes[0]) : baseNameRunes[0]);
        }
        else
        {
            lastCharWasValid = false;
        }

        for (var i = 1; i < baseNameRunes.Length; i++)
        {
            var isValid = IsIdentifierPart(baseNameRunes[i]);
            if (isValid)
            {
                if (!lastCharWasValid)
                {
                    AppendCodePoint(res, ToUpperCodePoint(baseNameRunes[i]));
                }
                else
                {
                    AppendCodePoint(res, baseNameRunes[i]);
                }
            }

            lastCharWasValid = isValid;
        }

        // Need `"_"` to ensure result isn't empty.
        var resString = res.ToString();
        if (resString.Length != 0 && !IsNonContextualKeyword(StringToToken(resString)))
        {
            return resString;
        }

        return "_" + resString;
    }

    /// <summary>Go 的 <c>func IsNonContextualKeyword(token ast.Kind) bool</c>。</summary>
    public static bool IsNonContextualKeyword(Kind token)
    {
        return IsKeywordKind(token) && !IsContextualKeyword(token);
    }

    /// <summary>
    /// Go 的 <c>[]rune(s)</c>（utilities.go:115 的 <c>baseNameRunes</c>）：按码点切分字符串。
    /// 见文件头：孤立代理项按单个码点（其 char 值）处理。
    /// </summary>
    private static int[] ToCodePoints(string s)
    {
        var result = new List<int>(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                result.Add(char.ConvertToUtf32(c, s[i + 1]));
                i++;
            }
            else
            {
                result.Add(c);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Go 的 <c>unicode.ToUpper(r)</c>（simple 映射，§3.4）：C# 的 <c>Rune</c> 表示不了孤立代理项，
    /// 所以 BMP 走 <c>char.ToUpperInvariant</c>，超出 BMP 才用 <c>Rune</c>（与 FsWatch/PathCompare.cs 一致）。
    /// </summary>
    private static int ToUpperCodePoint(int cp) => cp > 0xFFFF
        ? Rune.ToUpperInvariant(new Rune(cp)).Value
        : char.ToUpperInvariant((char)cp);

    /// <summary>
    /// Go 的 <c>string(runes)</c> 逐码点写出（utilities.go:126 的 <c>string(res)</c>）。
    /// Go 会把非法码点写成 U+FFFD；C# 的 string 允许孤立代理项，这里原样保留（见文件头）。
    /// </summary>
    private static void AppendCodePoint(StringBuilder builder, int cp)
    {
        if (cp > 0xFFFF)
        {
            builder.Append(char.ConvertFromUtf32(cp));
        }
        else
        {
            builder.Append((char)cp);
        }
    }

    /// <summary>
    /// Go 的 <c>strings.TrimSuffix(s, suffix)</c>（utilities.go:115 的 <c>strings.TrimSuffix(…, "/index")</c>）。
    /// 与 ModuleSpecifiers/Util.cs 的 <c>TrimSuffix</c> 同形（Ordinal 语义）。
    /// </summary>
    private static string TrimSuffix(string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;
}
