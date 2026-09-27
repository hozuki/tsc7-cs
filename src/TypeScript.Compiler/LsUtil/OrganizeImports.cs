// ported from: internal/ls/lsutil/organizeimports.go
//
// **本文件补齐 `internal/ls/lsutil` 的缺口**（此前只移植了 Asi/Children/CompletedNode/FormatCodeOptions）。
//
// 与 Go 的偏离（C# 语言特性；依据 docs/architecture.md §2/§3）：
// - 包级函数 → 同名静态类 `LsUtil` 的 `partial` 分片（§2）。
// - Go 的 `func(a, b string) int` / `func(s1, s2 *ast.Node) int` 等函数值 → `Func<string, string, int>` /
//   `Func<Node, Node, int>`。
// - **`(T, bool)` 形态 → `bool TryX(..., out T …)`**（§3.2；api-parity 的白名单规则 "§3.2 (T,bool) → TryX"）：
//   `GetOrganizeImportsStringComparerWithDetection` → `TryGetOrganizeImportsStringComparerWithDetection`、
//   `DetectModuleSpecifierCaseBySort` → `TryDetectModuleSpecifierCaseBySort`、
//   `DetectNamedImportOrganizationBySort` → `TryDetectNamedImportOrganizationBySort`（它的 bool 就是 Go 的 `found`）。
//   返回值的 bool 语义（comparer 是否可得）与 Go 里 `comparer == nil` 的情形一一对应。
//   `GetNamedImportSpecifierComparerWithDetection` 返回 `(func, core.Tristate)`（不是 bool）⇒ 保留原名、返回 C# 元组。
// - `*ast.Statement` 是 Go 的类型别名（`ast_generated.go:518 Statement = Node`），C# 里没有对应类型 ⇒ 直接用 `Node`
//   （同理 `*ast.Expression` = `Node`）。
// - `core.FirstResult(core.BinarySearchUniqueFunc(...))` → C# 直接丢弃 `TryBinarySearchUniqueFunc` 的 bool
//   （core.go:516 的 `FirstResult` 只取多返回值的第一个，见 Core/Core.cs 文件头的第 4 条）。
// - `math.MaxInt` → `int.MaxValue`；`cmp.Compare(x, y)`（有序类型的 -1/0/1）→ 本文件的 `CompareInts`。
// - `strings.Compare(a, b)` → `string.CompareOrdinal`（Go 的 bytes 序即 UTF-16 代码单元序，对合法 UTF-16 一致）。
// - `[]rune(s)` 的逐 rune 循环 → 码点迭代（复用 Utilities.cs 的 `ToCodePoints`，§3.1）；`unicode.IsUpper(r)` →
//   `Rune.IsUpper` / `char.IsUpper` 的分派（`Rune` 表示不了孤立代理项，§3.4 + FsWatch/PathCompare.cs 的既有写法）。
// - `norm.NFD.String(s)` → `string.Normalize(NormalizationForm.FormD)`；`unicode.Mn` → `UnicodeCategory.NonSpacingMark`。
//   偏离：Go 的 x/text norm 对**非法 UTF-8** 不抛，C# 的 `Normalize` 对含孤立代理项的串会抛 `ArgumentException`
//   （模块说明符里出现孤立代理项属病态输入，本仓 §3.1 也接受这一取舍）。
// - `strings.ToLower`（Go 的完整 Unicode 小写）→ `ToLowerJS`（§3.4）。
// - `getModuleSpecifierExpression` 里的 `importEquals.ModuleReference.Expression()` 等：C# 的 `AsXxx()` 返回 `T?`，
//   用 `!` 消空（Go 侧的类型断言在类型不符时 panic，见本仓 Checker/NodeCopy.cs 的同类写法）。
// - 本文件的 `GetExternalModuleName` 与 `Ast.GetExternalModuleName(Node?)` **同名**（Go 里 lsutil 的这个包级函数
//   遮蔽了 ast 包的同名函数）；C# 里类成员优先于 `using static` 导入，于是同名遮蔽的语义一致。
using System.Globalization;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.LsUtil;

/// <summary>Go 的未导出 <c>namedImportSortResult</c>（Go 返回指针 ⇒ C# 用可空的 class）。</summary>
internal sealed class NamedImportSortResult
{
    internal Func<string, string, int> _namedImportComparer = static (_, _) => 0;

    internal OrganizeImportsTypeOrder _typeOrder;

    internal bool _isSorted;
}

/// <summary>Go 的未导出 <c>caseSensitivityDetectionResult</c>。</summary>
internal sealed class CaseSensitivityDetectionResult
{
    internal Func<string, string, int>? _comparer;

    internal bool _isSorted;
}

public static partial class LsUtil
{
    // getImportKindOrder 的排序值（Go 的 const 块；Go 未导出 const ⇒ `_camelCase`，同 AstNav/Tokens.cs）。
    private const int _importKindOrderSideEffect = 0;
    private const int _importKindOrderTypeOnly = 1;
    private const int _importKindOrderNamespace = 2;
    private const int _importKindOrderDefault = 3;
    private const int _importKindOrderNamed = 4;
    private const int _importKindOrderImportEquals = 5;
    private const int _importKindOrderRequire = 6;
    private const int _importKindOrderUnknown = 7;

    /// <summary>FilterImportDeclarations filters out non-import declarations from a list of statements.</summary>
    public static Node[] FilterImportDeclarations(Node[] statements)
    {
        return Filter(statements, stmt => stmt.Kind == Kind.ImportDeclaration);
    }

    /// <summary>GetDetectionLists returns the lists of comparers and type orders to test for organize imports detection.</summary>
    public static (Func<string, string, int>[] ComparersToTest, OrganizeImportsTypeOrder[] TypeOrdersToTest) GetDetectionLists(UserPreferences preferences)
    {
        Func<string, string, int>[] comparersToTest;
        OrganizeImportsTypeOrder[] typeOrdersToTest;

        if (preferences.OrganizeImportsSort != OrganizeImportsSort.Auto)
        {
            comparersToTest = [GetOrganizeImportsPresetStringComparer(preferences.OrganizeImportsSort)];
        }
        else if (!preferences.OrganizeImportsIgnoreCase.IsUnknown())
        {
            comparersToTest = [GetOrganizeImportsStringComparer(preferences, preferences.OrganizeImportsIgnoreCase.IsTrue())];
        }
        else
        {
            comparersToTest =
            [
                GetOrganizeImportsStringComparer(preferences, true),
                GetOrganizeImportsStringComparer(preferences, false),
            ];
        }

        if (preferences.OrganizeImportsTypeOrder != OrganizeImportsTypeOrder.Auto)
        {
            typeOrdersToTest = [preferences.OrganizeImportsTypeOrder];
        }
        else
        {
            typeOrdersToTest =
            [
                OrganizeImportsTypeOrder.Last,
                OrganizeImportsTypeOrder.Inline,
                OrganizeImportsTypeOrder.First,
            ];
        }

        return (comparersToTest, typeOrdersToTest);
    }

    /// <summary>Go 的 <c>func ResolveOrganizeImportsSort(preferences UserPreferences) OrganizeImportsSort</c>。</summary>
    public static OrganizeImportsSort ResolveOrganizeImportsSort(UserPreferences preferences)
    {
        if (preferences.OrganizeImportsSort != OrganizeImportsSort.Auto)
        {
            return preferences.OrganizeImportsSort;
        }

        if (preferences.OrganizeImportsCollation == OrganizeImportsCollation.Unicode)
        {
            return preferences.OrganizeImportsIgnoreCase switch
            {
                Tristate.TSTrue => OrganizeImportsSort.NaturalIgnoreCase,
                Tristate.TSFalse => OrganizeImportsSort.Natural,
                _ => OrganizeImportsSort.Auto,
            };
        }

        return preferences.OrganizeImportsIgnoreCase switch
        {
            Tristate.TSTrue => OrganizeImportsSort.OrdinalIgnoreCase,
            Tristate.TSFalse => OrganizeImportsSort.Ordinal,
            _ => OrganizeImportsSort.Auto,
        };
    }

    private static Func<string, string, int> GetOrganizeImportsOrdinalStringComparer(bool ignoreCase)
    {
        if (ignoreCase)
        {
            return CompareStringsCaseInsensitiveEslintCompatible;
        }

        return CompareStringsCaseSensitive;
    }

    private static Func<string, string, int> GetOrganizeImportsNaturalStringComparer(bool caseSensitive)
    {
        return (a, b) => CompareOrganizeImportsNaturalStrings(a, b, caseSensitive);
    }

    private static Func<string, string, int> GetOrganizeImportsUnicodeStringComparer(bool ignoreCase, UserPreferences preferences)
    {
        var caseFirst = preferences.OrganizeImportsCaseFirst;
        var numeric = preferences.OrganizeImportsNumericCollation.IsTrue();
        var accents = !preferences.OrganizeImportsAccentCollation.IsFalse();

        return (a, b) => CompareOrganizeImportsUnicodeStrings(a, b, ignoreCase, caseFirst, numeric, accents);
    }

    private static int CompareOrganizeImportsNaturalStrings(string a, string b, bool caseSensitive)
    {
        var cmp = CompareStringsNumeric(NaturalCollationKey(a), NaturalCollationKey(b));
        if (cmp != 0)
        {
            return cmp;
        }

        if (caseSensitive)
        {
            cmp = CompareOrganizeImportsCaseUpperFirst(a, b);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return string.CompareOrdinal(a, b);
    }

    private static int CompareOrganizeImportsUnicodeStrings(string a, string b, bool ignoreCase, OrganizeImportsCaseFirst caseFirst, bool numeric, bool accents)
    {
        var cmp = CompareOrganizeImportsUnicodeKeys(NaturalCollationKey(a), NaturalCollationKey(b), numeric);
        if (cmp != 0)
        {
            return cmp;
        }

        if (accents)
        {
            cmp = CompareOrganizeImportsUnicodeKeys(ToLowerJS(a), ToLowerJS(b), numeric);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        if (!ignoreCase)
        {
            cmp = CompareOrganizeImportsCase(a, b, caseFirst);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return string.CompareOrdinal(a, b);
    }

    private static string NaturalCollationKey(string s)
    {
        return ToLowerJS(RemoveDiacritics(s));
    }

    /// <summary>Go 的 <c>removeDiacritics</c>（NFD 分解后丢掉 Mn 类码点）。</summary>
    private static string RemoveDiacritics(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        for (var i = 0; i < decomposed.Length; i++)
        {
            var c = decomposed[i];
            if (CharUnicodeInfo.GetUnicodeCategory(decomposed, i) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c);
            if (char.IsHighSurrogate(c) && i + 1 < decomposed.Length && char.IsLowSurrogate(decomposed[i + 1]))
            {
                builder.Append(decomposed[i + 1]);
                i++;
            }
        }

        return builder.ToString();
    }

    private static int CompareOrganizeImportsUnicodeKeys(string a, string b, bool numeric)
    {
        if (numeric)
        {
            return CompareStringsNumeric(a, b);
        }

        return string.CompareOrdinal(a, b);
    }

    /// <summary>
    /// Go 的 <c>compareStringsNumeric</c>：ASCII 数字串按数值比较，其余按码点逐个比较。
    /// Go 按**字节**取首位（ASCII 数字单字节），C# 按 UTF-16 代码单元取——对 ASCII 数字等价。
    /// </summary>
    private static int CompareStringsNumeric(string a, string b)
    {
        while (a.Length > 0 && b.Length > 0)
        {
            if (IsASCIIDigit(a[0]) && IsASCIIDigit(b[0]))
            {
                var aRunEnd = AsciiDigitRunEnd(a);
                var bRunEnd = AsciiDigitRunEnd(b);

                var cmp = CompareNumericText(a[..aRunEnd], b[..bRunEnd]);
                if (cmp != 0)
                {
                    return cmp;
                }

                a = a[aRunEnd..];
                b = b[bRunEnd..];
                continue;
            }

            var (aRune, aSize) = DecodeRuneInString(a);
            var (bRune, bSize) = DecodeRuneInString(b);
            if (aRune != bRune)
            {
                return CompareInts(aRune, bRune);
            }

            a = a[aSize..];
            b = b[bSize..];
        }

        return CompareInts(a.Length, b.Length);
    }

    private static bool IsASCIIDigit(char ch)
    {
        return ch >= '0' && ch <= '9';
    }

    private static int AsciiDigitRunEnd(string s)
    {
        var i = 0;
        while (i < s.Length && IsASCIIDigit(s[i]))
        {
            i++;
        }

        return i;
    }

    private static int CompareNumericText(string a, string b)
    {
        var aDigits = a.TrimStart('0');
        var bDigits = b.TrimStart('0');
        if (aDigits.Length == 0)
        {
            aDigits = "0";
        }

        if (bDigits.Length == 0)
        {
            bDigits = "0";
        }

        if (aDigits.Length != bDigits.Length)
        {
            return CompareInts(aDigits.Length, bDigits.Length);
        }

        var cmp = string.CompareOrdinal(aDigits, bDigits);
        if (cmp != 0)
        {
            return cmp;
        }

        return string.CompareOrdinal(a, b);
    }

    private static int CompareOrganizeImportsCaseUpperFirst(string a, string b)
    {
        return CompareOrganizeImportsCase(a, b, OrganizeImportsCaseFirst.Upper);
    }

    private static int CompareOrganizeImportsCase(string a, string b, OrganizeImportsCaseFirst caseFirst)
    {
        var aRunes = ToCodePoints(a);
        var bRunes = ToCodePoints(b);
        var minLen = Math.Min(aRunes.Length, bRunes.Length);

        for (var i = 0; i < minLen; i++)
        {
            var aUpper = IsUpperCodePoint(aRunes[i]);
            var bUpper = IsUpperCodePoint(bRunes[i]);
            if (aUpper != bUpper)
            {
                switch (caseFirst)
                {
                    case OrganizeImportsCaseFirst.Upper:
                        if (aUpper)
                        {
                            return -1;
                        }

                        return 1;
                    case OrganizeImportsCaseFirst.Lower:
                        if (!aUpper)
                        {
                            return -1;
                        }

                        return 1;
                    default:
                        if (aUpper)
                        {
                            return 1;
                        }

                        return -1;
                }
            }
        }

        return CompareInts(aRunes.Length, bRunes.Length);
    }

    private static Func<string, string, int> GetOrganizeImportsPresetStringComparer(OrganizeImportsSort sort)
    {
        switch (sort)
        {
            case OrganizeImportsSort.OrdinalIgnoreCase:
                return GetOrganizeImportsOrdinalStringComparer(true);
            case OrganizeImportsSort.Natural:
                return GetOrganizeImportsNaturalStringComparer(true);
            case OrganizeImportsSort.NaturalIgnoreCase:
                return GetOrganizeImportsNaturalStringComparer(false);
            default:
                return GetOrganizeImportsOrdinalStringComparer(false);
        }
    }

    private static Func<string, string, int> GetOrganizeImportsStringComparer(UserPreferences preferences, bool ignoreCase)
    {
        if (preferences.OrganizeImportsSort != OrganizeImportsSort.Auto)
        {
            return GetOrganizeImportsPresetStringComparer(preferences.OrganizeImportsSort);
        }

        if (preferences.OrganizeImportsCollation == OrganizeImportsCollation.Unicode)
        {
            return GetOrganizeImportsUnicodeStringComparer(ignoreCase, preferences);
        }

        return GetOrganizeImportsOrdinalStringComparer(ignoreCase);
    }

    private static Node? GetModuleSpecifierExpression(Node declaration)
    {
        switch (declaration.Kind)
        {
            case Kind.ImportEqualsDeclaration:
                var importEquals = declaration.AsImportEqualsDeclaration()!;
                if (importEquals.ModuleReference!.Kind == Kind.ExternalModuleReference)
                {
                    return importEquals.ModuleReference.Expression();
                }

                return null;
            case Kind.ImportDeclaration:
                return declaration.ModuleSpecifier();
            case Kind.VariableStatement:
                var declarations = declaration.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [];
                if (declarations.Length > 0)
                {
                    var initializer = declarations[0].Initializer();
                    if (initializer is not null && initializer.Kind == Kind.CallExpression)
                    {
                        var callExpr = initializer.AsCallExpression()!;
                        var arguments = callExpr.Arguments!.Nodes ?? [];
                        if (arguments.Length > 0)
                        {
                            return arguments[0];
                        }
                    }
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>GetExternalModuleName returns the module name from a module specifier expression.</summary>
    public static string GetExternalModuleName(Node? specifier)
    {
        if (specifier is not null && IsStringLiteralLike(specifier.AsNode()))
        {
            return specifier.Text();
        }

        return "";
    }

    /// <summary>CompareModuleSpecifiers compares two module specifiers using the given comparer.</summary>
    public static int CompareModuleSpecifiers(Node? m1, Node? m2, Func<string, string, int> comparer)
    {
        var name1 = GetExternalModuleName(m1);
        var name2 = GetExternalModuleName(m2);
        var cmp = CompareBooleans(name1.Length == 0, name2.Length == 0);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = CompareBooleans(IsExternalModuleNameRelative(name1), IsExternalModuleNameRelative(name2));
        if (cmp != 0)
        {
            return cmp;
        }

        return comparer(name1, name2);
    }

    private static int CompareImportKind(Node s1, Node s2)
    {
        return CompareInts(GetImportKindOrder(s1), GetImportKindOrder(s2));
    }

    /// <summary>
    /// getImportKindOrder returns the sort order for different import kinds:
    /// 1. Side-effect imports
    /// 2. Type-only imports
    /// 3. Namespace imports
    /// 4. Default imports
    /// 5. Named imports
    /// 6. ImportEqualsDeclarations
    /// 7. Require variable statements
    /// </summary>
    private static int GetImportKindOrder(Node s1)
    {
        switch (s1.Kind)
        {
            case Kind.ImportDeclaration:
                var importDecl = s1.AsImportDeclaration()!;
                if (importDecl.ImportClause is null)
                {
                    return _importKindOrderSideEffect;
                }

                var importClause = importDecl.ImportClause.AsImportClause()!;
                if (importClause.IsTypeOnly())
                {
                    return _importKindOrderTypeOnly;
                }

                if (importClause.NamedBindings is not null && importClause.NamedBindings.Kind == Kind.NamespaceImport)
                {
                    return _importKindOrderNamespace;
                }

                if (importClause.Name() is not null)
                {
                    return _importKindOrderDefault;
                }

                return _importKindOrderNamed;
            case Kind.ImportEqualsDeclaration:
                return _importKindOrderImportEquals;
            case Kind.VariableStatement:
                return _importKindOrderRequire;
            default:
                return _importKindOrderUnknown;
        }
    }

    /// <summary>CompareImportsOrRequireStatements compares two import or require statements.</summary>
    public static int CompareImportsOrRequireStatements(Node s1, Node s2, Func<string, string, int> comparer)
    {
        var cmp = CompareModuleSpecifiers(GetModuleSpecifierExpression(s1), GetModuleSpecifierExpression(s2), comparer);
        if (cmp != 0)
        {
            return cmp;
        }

        return CompareImportKind(s1, s2);
    }

    private static int CompareImportOrExportSpecifiers(Node s1, Node s2, Func<string, string, int> comparer, UserPreferences preferences)
    {
        var typeOrder = preferences.OrganizeImportsTypeOrder;

        var s1Name = s1.Name()!.Text();
        var s2Name = s2.Name()!.Text();

        switch (typeOrder)
        {
            case OrganizeImportsTypeOrder.First:
                var firstCmp = CompareBooleans(s2.IsTypeOnly(), s1.IsTypeOnly());
                if (firstCmp != 0)
                {
                    return firstCmp;
                }

                return comparer(s1Name, s2Name);
            case OrganizeImportsTypeOrder.Inline:
                return comparer(s1Name, s2Name);
            default: // OrganizeImportsTypeOrderLast
                var lastCmp = CompareBooleans(s1.IsTypeOnly(), s2.IsTypeOnly());
                if (lastCmp != 0)
                {
                    return lastCmp;
                }

                return comparer(s1Name, s2Name);
        }
    }

    /// <summary>GetNamedImportSpecifierComparer returns a comparer function for sorting import specifiers.</summary>
    public static Func<Node, Node, int> GetNamedImportSpecifierComparer(UserPreferences preferences, Func<string, string, int>? comparer)
    {
        Func<string, string, int> actualComparer;
        if (comparer is null)
        {
            var ignoreCase = false;
            if (!preferences.OrganizeImportsIgnoreCase.IsUnknown())
            {
                ignoreCase = preferences.OrganizeImportsIgnoreCase.IsTrue();
            }

            actualComparer = GetOrganizeImportsStringComparer(preferences, ignoreCase);
        }
        else
        {
            actualComparer = comparer;
        }

        return (s1, s2) => CompareImportOrExportSpecifiers(s1, s2, actualComparer, preferences);
    }

    /// <summary>GetImportSpecifierInsertionIndex returns the index at which to insert a new import specifier.</summary>
    public static int GetImportSpecifierInsertionIndex(Node[] sortedImports, Node newImport, Func<Node, Node, int> comparer)
    {
        TryBinarySearchUniqueFunc(sortedImports, (mid, value) => comparer(value, newImport), out var index);
        return index;
    }

    /// <summary>GetImportDeclarationInsertIndex returns the index at which to insert a new import declaration.</summary>
    public static int GetImportDeclarationInsertIndex(Node[] sortedImports, Node newImport, Func<Node, Node, int> comparer)
    {
        TryBinarySearchUniqueFunc(sortedImports, (mid, value) => comparer(value, newImport), out var index);
        return index;
    }

    /// <summary>
    /// GetOrganizeImportsStringComparerWithDetection returns a string comparer based on detecting the order of import
    /// statements by the module specifier（Go 的 <c>(comparer, isSorted)</c> ⇒ C# 的 <c>TryX(out …, out …)</c>）。
    /// </summary>
    public static bool TryGetOrganizeImportsStringComparerWithDetection(Node[] originalImportDecls, UserPreferences preferences, out Func<string, string, int> comparer, out bool isSorted)
    {
        return TryDetectModuleSpecifierCaseBySort([originalImportDecls], GetComparers(preferences), out comparer, out isSorted);
    }

    private static Func<string, string, int>[] GetComparers(UserPreferences preferences)
    {
        if (preferences.OrganizeImportsSort != OrganizeImportsSort.Auto || !preferences.OrganizeImportsIgnoreCase.IsUnknown())
        {
            var ignoreCase = false;
            if (!preferences.OrganizeImportsIgnoreCase.IsUnknown())
            {
                ignoreCase = preferences.OrganizeImportsIgnoreCase.IsTrue();
            }

            return [GetOrganizeImportsStringComparer(preferences, ignoreCase)];
        }

        return
        [
            GetOrganizeImportsStringComparer(preferences, true),
            GetOrganizeImportsStringComparer(preferences, false),
        ];
    }

    /// <summary>
    /// DetectNamedImportOrganizationBySort detects the order of named imports throughout the file by considering the
    /// named imports in each statement as a group.
    /// </summary>
    public static bool TryDetectNamedImportOrganizationBySort(
        Node[] originalGroups,
        Func<string, string, int>[] comparersToTest,
        OrganizeImportsTypeOrder[] typesToTest,
        out Func<string, string, int> comparer,
        out OrganizeImportsTypeOrder typeOrder)
    {
        var result = DetectNamedImportOrganizationBySort(originalGroups, comparersToTest, typesToTest);
        if (result is null)
        {
            comparer = static (_, _) => 0;
            typeOrder = OrganizeImportsTypeOrder.Last;
            return false;
        }

        comparer = result._namedImportComparer;
        typeOrder = result._typeOrder;
        return true;
    }

    private static NamedImportSortResult? DetectNamedImportOrganizationBySort(
        Node[] originalGroups,
        Func<string, string, int>[] comparersToTest,
        OrganizeImportsTypeOrder[] typesToTest)
    {
        var bothNamedImports = false;
        var importDeclsWithNamed = new List<Node>();

        foreach (var imp in originalGroups)
        {
            if (imp.AsImportDeclaration()!.ImportClause is null)
            {
                continue;
            }

            var clause = imp.AsImportDeclaration()!.ImportClause!.AsImportClause()!;
            if (clause.NamedBindings is null || clause.NamedBindings.Kind != Kind.NamedImports)
            {
                continue;
            }

            var namedImports = clause.NamedBindings.AsNamedImports()!;
            var elements = namedImports.Elements!.Nodes ?? [];
            if (elements.Length == 0)
            {
                continue;
            }

            if (!bothNamedImports)
            {
                var hasTypeOnly = false;
                var hasRegular = false;
                foreach (var elem in elements)
                {
                    if (elem.IsTypeOnly())
                    {
                        hasTypeOnly = true;
                    }
                    else
                    {
                        hasRegular = true;
                    }
                }

                if (hasTypeOnly && hasRegular)
                {
                    bothNamedImports = true;
                }
            }

            importDeclsWithNamed.Add(imp);
        }

        if (importDeclsWithNamed.Count == 0)
        {
            return null;
        }

        var namedImportsByDecl = new List<Node[]>(importDeclsWithNamed.Count);
        foreach (var imp in importDeclsWithNamed)
        {
            var clause = imp.AsImportDeclaration()!.ImportClause!.AsImportClause()!;
            var namedImports = clause.NamedBindings!.AsNamedImports()!;
            namedImportsByDecl.Add(namedImports.Elements!.Nodes ?? []);
        }

        if (!bothNamedImports || typesToTest.Length == 0)
        {
            var namesList = new string[namedImportsByDecl.Count][];
            for (var i = 0; i < namedImportsByDecl.Count; i++)
            {
                var imports = namedImportsByDecl[i];
                var names = new string[imports.Length];
                for (var j = 0; j < imports.Length; j++)
                {
                    names[j] = imports[j].Name()!.Text();
                }

                namesList[i] = names;
            }

            var sortState = DetectCaseSensitivityBySort(namesList, comparersToTest);
            var typeOrder = OrganizeImportsTypeOrder.Last;
            if (typesToTest.Length == 1)
            {
                typeOrder = typesToTest[0];
            }

            return new NamedImportSortResult
            {
                _namedImportComparer = sortState._comparer ?? ZeroStringComparer(),
                _typeOrder = typeOrder,
                _isSorted = sortState._isSorted,
            };
        }

        var bestDiff = new Dictionary<OrganizeImportsTypeOrder, int>
        {
            [OrganizeImportsTypeOrder.First] = int.MaxValue,
            [OrganizeImportsTypeOrder.Last] = int.MaxValue,
            [OrganizeImportsTypeOrder.Inline] = int.MaxValue,
        };
        var bestComparer = new Dictionary<OrganizeImportsTypeOrder, Func<string, string, int>>
        {
            [OrganizeImportsTypeOrder.First] = comparersToTest[0],
            [OrganizeImportsTypeOrder.Last] = comparersToTest[0],
            [OrganizeImportsTypeOrder.Inline] = comparersToTest[0],
        };

        foreach (var curComparer in comparersToTest)
        {
            var currDiff = new Dictionary<OrganizeImportsTypeOrder, int>
            {
                [OrganizeImportsTypeOrder.First] = 0,
                [OrganizeImportsTypeOrder.Last] = 0,
                [OrganizeImportsTypeOrder.Inline] = 0,
            };

            foreach (var importDecl in namedImportsByDecl)
            {
                foreach (var typeOrder in typesToTest)
                {
                    var prefs = new UserPreferences { OrganizeImportsTypeOrder = typeOrder };
                    var diff = MeasureSortedness(importDecl, (n1, n2) => CompareImportOrExportSpecifiers(n1, n2, curComparer, prefs));
                    currDiff[typeOrder] = currDiff[typeOrder] + diff;
                }
            }

            foreach (var typeOrder in typesToTest)
            {
                if (currDiff[typeOrder] < bestDiff[typeOrder])
                {
                    bestDiff[typeOrder] = currDiff[typeOrder];
                    bestComparer[typeOrder] = curComparer;
                }
            }
        }

        foreach (var bestTypeOrder in typesToTest)
        {
            var isBest = true;
            foreach (var testTypeOrder in typesToTest)
            {
                if (bestDiff[testTypeOrder] < bestDiff[bestTypeOrder])
                {
                    isBest = false;
                    break;
                }
            }

            if (isBest)
            {
                return new NamedImportSortResult
                {
                    _namedImportComparer = bestComparer[bestTypeOrder],
                    _typeOrder = bestTypeOrder,
                    _isSorted = bestDiff[bestTypeOrder] == 0,
                };
            }
        }

        return new NamedImportSortResult
        {
            _namedImportComparer = bestComparer[OrganizeImportsTypeOrder.Last],
            _typeOrder = OrganizeImportsTypeOrder.Last,
            _isSorted = bestDiff[OrganizeImportsTypeOrder.Last] == 0,
        };
    }

    /// <summary>
    /// DetectModuleSpecifierCaseBySort detects the order of module specifiers based on import statements throughout the
    /// module/file（Go 的 <c>(comparer, isSorted)</c> ⇒ C# 的 <c>TryX(out …, out …)</c>；返回 false = 无法得到 comparer，
    /// 对应 Go 里 `comparersToTest` 为空的退化情形）。
    /// </summary>
    public static bool TryDetectModuleSpecifierCaseBySort(Node[][] importDeclsByGroup, Func<string, string, int>[] comparersToTest, out Func<string, string, int> comparer, out bool isSorted)
    {
        var moduleSpecifiersByGroup = new List<string[]>(importDeclsByGroup.Length);
        foreach (var importGroup in importDeclsByGroup)
        {
            var moduleNames = new List<string>(importGroup.Length);
            foreach (var decl in importGroup)
            {
                var expr = GetModuleSpecifierExpression(decl);
                if (expr is not null)
                {
                    moduleNames.Add(GetExternalModuleName(expr));
                }
                else
                {
                    moduleNames.Add("");
                }
            }

            moduleSpecifiersByGroup.Add(moduleNames.ToArray());
        }

        var result = DetectCaseSensitivityBySort(moduleSpecifiersByGroup.ToArray(), comparersToTest);
        comparer = result._comparer ?? ZeroStringComparer();
        isSorted = result._isSorted;
        return result._comparer is not null;
    }

    private static CaseSensitivityDetectionResult DetectCaseSensitivityBySort(string[][] originalGroups, Func<string, string, int>[] comparersToTest)
    {
        Func<string, string, int>? bestComparer = null;
        var bestDiff = int.MaxValue;

        foreach (var curComparer in comparersToTest)
        {
            var diffOfCurrentComparer = 0;

            foreach (var listToSort in originalGroups)
            {
                if (listToSort.Length <= 1)
                {
                    continue;
                }

                var diff = MeasureSortedness(listToSort, curComparer);
                diffOfCurrentComparer += diff;
            }

            if (diffOfCurrentComparer < bestDiff)
            {
                bestDiff = diffOfCurrentComparer;
                bestComparer = curComparer;
            }
        }

        if (bestComparer is null && comparersToTest.Length > 0)
        {
            bestComparer = comparersToTest[0];
        }

        return new CaseSensitivityDetectionResult
        {
            _comparer = bestComparer,
            _isSorted = bestDiff == 0,
        };
    }

    private static int MeasureSortedness<T>(T[] arr, Func<T, T, int> comparer)
    {
        var i = 0;
        for (var j = 0; j < arr.Length - 1; j++)
        {
            if (comparer(arr[j], arr[j + 1]) > 0)
            {
                i++;
            }
        }

        return i;
    }

    /// <summary>
    /// GetNamedImportSpecifierComparerWithDetection returns a specifier comparer based on detecting the existing sort
    /// order within a single import statement（Go 返回 `(func(s1,s2) int, core.Tristate)`，C# 用元组）。
    /// </summary>
    public static (Func<Node, Node, int> SpecifierComparer, Tristate IsSorted) GetNamedImportSpecifierComparerWithDetection(Node importDecl, SourceFile? sourceFile, UserPreferences preferences)
    {
        var (comparersToTest, typeOrdersToTest) = GetDetectionLists(preferences);

        Node? importStmt = null;
        if (importDecl.Kind == Kind.ImportDeclaration)
        {
            importStmt = importDecl;
        }

        var specifierComparer = GetNamedImportSpecifierComparer(preferences, comparersToTest[0]);
        var isSorted = Tristate.TSUnknown;

        if ((ResolveOrganizeImportsSort(preferences) == OrganizeImportsSort.Auto || preferences.OrganizeImportsTypeOrder == OrganizeImportsTypeOrder.Auto) && importStmt is not null)
        {
            var detectFromDecl = DetectNamedImportOrganizationBySort([importStmt], comparersToTest, typeOrdersToTest);
            if (detectFromDecl is not null)
            {
                isSorted = BoolToTristate(detectFromDecl._isSorted);
                specifierComparer = GetNamedImportSpecifierComparer(
                    new UserPreferences { OrganizeImportsTypeOrder = detectFromDecl._typeOrder },
                    detectFromDecl._namedImportComparer);
            }
            else if (sourceFile is not null)
            {
                var allImports = FilterImportDeclarations(sourceFile.Statements!.Nodes ?? []);
                var detectFromFile = DetectNamedImportOrganizationBySort(allImports, comparersToTest, typeOrdersToTest);
                if (detectFromFile is not null)
                {
                    isSorted = BoolToTristate(detectFromFile._isSorted);
                    specifierComparer = GetNamedImportSpecifierComparer(
                        new UserPreferences { OrganizeImportsTypeOrder = detectFromFile._typeOrder },
                        detectFromFile._namedImportComparer);
                }
            }
        }

        return (specifierComparer, isSorted);
    }

    /// <summary>Go 的 <c>cmp.Compare</c>（有序类型的 -1/0/1 三值比较）。</summary>
    private static int CompareInts(int a, int b) => a < b ? -1 : a > b ? 1 : 0;

    /// <summary>
    /// Go 里 comparer 可以是 **nil**（`comparersToTest` 为空的退化情形）；C# 的 `Func` 不能"null 也能安全调用"，
    /// 所以这里给一个"恒相等"的兜底比较器，并用 `TryX` 的返回值告诉调用方"没检测出结果"。
    /// </summary>
    private static Func<string, string, int> ZeroStringComparer() => static (_, _) => 0;

    /// <summary>
    /// Go 的 <c>unicode.IsUpper(r)</c>（§3.4）：BMP 走 <c>char.IsUpper</c>，超出 BMP 才用 <c>Rune</c>
    /// （<c>Rune</c> 表示不了孤立代理项，与 FsWatch/PathCompare.cs 的既有写法一致）。
    /// </summary>
    private static bool IsUpperCodePoint(int cp) => cp > 0xFFFF
        ? Rune.IsUpper(new Rune(cp))
        : char.IsUpper((char)cp);

    /// <summary>Go 的 <c>utf8.DecodeRuneInString</c>：返回码点与推进的 UTF-16 代码单元数。</summary>
    private static (int Rune, int Size) DecodeRuneInString(string s)
    {
        var c = s[0];
        if (char.IsHighSurrogate(c) && s.Length > 1 && char.IsLowSurrogate(s[1]))
        {
            return (char.ConvertToUtf32(c, s[1]), 2);
        }

        return (c, 1);
    }
}
