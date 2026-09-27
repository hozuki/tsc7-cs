// ported from: internal/project/ata/validatepackagename.go:1-98
//
// 与 Go 的偏离（逐条）：
// 1) Go 的包级函数 `ValidatePackageName` / `RenderPackageNameValidationFailure`（未导出）没有"它构造/返回的类型"
//    可挂（`NameValidationResult` 是枚举，C# 不允许在枚举上声明成员），故放在本文件的静态类
//    **`PackageNameValidation`** 上（C# 没有包作用域；仓内先例见 docs/architecture.md §2 —— 包级成员必须落到某个
//    类型）。方法名保持 Go 的名字（`RenderPackageNameValidationFailure` 对应 Go 未导出的
//    `renderPackageNameValidationFailure` → PascalCase）。
// 2) `len(packageName)`（Go 是**字节**长度）→ `Encoding.UTF8.GetByteCount(packageName)`。这里不是"位置"，而是
//    npm 的 214 字符上限按字节量的，故保留字节语义（非 ASCII 包名与 Go 一致地判 NameTooLong）。
// 3) `utf8.DecodeRuneInString(packageName)` 取首字符 → `Rune.DecodeFromUtf16`（§3.1：UTF-16 代码单元解码）。
//    Go 对空串返回 RuneError，但函数在长度 0 时已提前返回，故这里不需要 RuneError 分支。
// 4) `url.QueryEscape` 在 C# **没有**等价物（`Uri.EscapeDataString` 的转义集与 Go 不同：Go 还转义
//    `!*'()`、空格转 `+`、非 ASCII 按 UTF-8 逐字节 `%XX`；仓内 LsConv/Converters.cs 也记了同一条）→ 本文件手写
//    `QueryEscape`（逐字节实现 net/url 的 shouldEscape(encodeQueryComponent) 表）。孤立代理项的输入在 Go 里是
//    非法 UTF-8（按字节转义），C# 的 `Encoding.UTF8` 会替换成 U+FFFD —— 只影响非法输入，见 §3.1。
// 5) Go 的 `strings.CutPrefix`/`strings.Cut` → `StartsWith` + `IndexOf`（与 LsAutoImport 的 TryCutPrefix 同形）。
// 6) `panic(...)`（`NameOk`/未知 result 落到 `RenderPackageNameValidationFailure`）→ `Debug.Fail(...)` +
//    `throw new UnreachableException()`（`Debug.Fail` 没有 `[DoesNotReturn]`；仓内既定写法）。
// 7) `const maxPackageNameLength`（未导出）→ `internal const int MaxPackageNameLength`（跨文件/测试可见用 internal，
//    并保留 Go 的 PascalCase 名）。
using System.Text;
using UnreachableException = System.Diagnostics.UnreachableException;

namespace TypeScript.Project.Ata;

/// <summary>Go: <c>type NameValidationResult int</c> + iota 常量（导出）。</summary>
public enum NameValidationResult
{
    NameOk,
    EmptyName,
    NameTooLong,
    NameStartsWithDot,
    NameStartsWithUnderscore,
    NameContainsNonURISafeCharacters,
}

/// <summary>
/// Go: validatepackagename.go 的包级函数（见文件头第 1 条：落到本静态类上）。
/// </summary>
public static class PackageNameValidation
{
    /// <summary>Go: <c>const maxPackageNameLength = 214</c>。</summary>
    internal const int MaxPackageNameLength = 214;

    /// <summary>
    /// Go: <c>func ValidatePackageName(packageName string) (result NameValidationResult, name string, isScopeName bool)</c>。
    /// </summary>
    public static (NameValidationResult Result, string Name, bool IsScopeName) ValidatePackageName(string packageName)
    {
        return ValidatePackageNameWorker(packageName /* supportScopedPackage */, true);
    }

    // Go: func validatePackageNameWorker(packageName string, supportScopedPackage bool) (…)
    private static (NameValidationResult Result, string Name, bool IsScopeName) ValidatePackageNameWorker(
        string packageName,
        bool supportScopedPackage)
    {
        var packageNameLen = Encoding.UTF8.GetByteCount(packageName);
        if (packageNameLen == 0)
        {
            return (NameValidationResult.EmptyName, "", false);
        }

        if (packageNameLen > MaxPackageNameLength)
        {
            return (NameValidationResult.NameTooLong, "", false);
        }

        Rune.DecodeFromUtf16(packageName, out var firstRune, out _);
        var firstChar = firstRune.Value;
        if (firstChar == '.')
        {
            return (NameValidationResult.NameStartsWithDot, "", false);
        }

        if (firstChar == '_')
        {
            return (NameValidationResult.NameStartsWithUnderscore, "", false);
        }

        // check if name is scope package like: starts with @ and has one '/' in the middle
        // scoped packages are not currently supported
        if (supportScopedPackage && packageName.StartsWith('@'))
        {
            var withoutScope = packageName[1..];
            var slashIndex = withoutScope.IndexOf('/');
            if (slashIndex >= 0)
            {
                var scope = withoutScope[..slashIndex];
                var scopedPackageName = withoutScope[(slashIndex + 1)..];
                if (scope.Length > 0 && scopedPackageName.Length > 0 && !scopedPackageName.Contains('/'))
                {
                    var (scopeResult, _, _) = ValidatePackageNameWorker(scope /* supportScopedPackage */, false);
                    if (scopeResult != NameValidationResult.NameOk)
                    {
                        return (scopeResult, scope, true);
                    }

                    var (packageResult, _, _) = ValidatePackageNameWorker(scopedPackageName /* supportScopedPackage */, false);
                    if (packageResult != NameValidationResult.NameOk)
                    {
                        return (packageResult, scopedPackageName, false);
                    }

                    return (NameValidationResult.NameOk, "", false);
                }
            }
        }

        if (QueryEscape(packageName) != packageName)
        {
            return (NameValidationResult.NameContainsNonURISafeCharacters, "", false);
        }

        return (NameValidationResult.NameOk, "", false);
    }

    /// <summary>
    /// Go: <c>func renderPackageNameValidationFailure(typing string, result NameValidationResult, name string, isScopeName bool) string</c>。
    /// </summary>
    public static string RenderPackageNameValidationFailure(
        string typing,
        NameValidationResult result,
        string name,
        bool isScopeName)
    {
        var kind = isScopeName ? "Scope" : "Package";
        if (name.Length == 0)
        {
            name = typing;
        }

        switch (result)
        {
            case NameValidationResult.EmptyName:
                return $"'{typing}':: {kind} name '{name}' cannot be empty";
            case NameValidationResult.NameTooLong:
                return $"'{typing}':: {kind} name '{name}' should be less than {MaxPackageNameLength} characters";
            case NameValidationResult.NameStartsWithDot:
                return $"'{typing}':: {kind} name '{name}' cannot start with '.'";
            case NameValidationResult.NameStartsWithUnderscore:
                return $"'{typing}':: {kind} name '{name}' cannot start with '_'";
            case NameValidationResult.NameContainsNonURISafeCharacters:
                return $"'{typing}':: {kind} name '{name}' contains non URI safe characters";
            case NameValidationResult.NameOk:
                Debug.Fail("Unexpected Ok result");
                throw new UnreachableException();
            default:
                Debug.Fail("Unknown package name validation result");
                throw new UnreachableException();
        }
    }

    // Go: net/url 的 QueryEscape（见文件头第 4 条：C# 无等价物，按 net/url 的 shouldEscape(encodeQueryComponent) 手写）。
    private static string QueryEscape(string s)
    {
        var builder = new StringBuilder(s.Length);
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            if (b == ' ')
            {
                builder.Append('+');
                continue;
            }

            if (!ShouldEscapeQueryComponent((char)b))
            {
                builder.Append((char)b);
                continue;
            }

            builder.Append('%');
            builder.Append(_hexUpper[b >> 4]);
            builder.Append(_hexUpper[b & 0xF]);
        }

        return builder.ToString();
    }

    private const string _hexUpper = "0123456789ABCDEF";

    // Go: net/url.shouldEscape(c, encodeQueryComponent)
    private static bool ShouldEscapeQueryComponent(char c)
    {
        if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return false;
        }

        switch (c)
        {
            // §2.3 Unreserved characters (mark)
            case '-' or '_' or '.' or '~':
                return false;
            // §2.2 Reserved characters (reserved)：encodeQueryComponent 模式下一律转义
            case '$' or '&' or '+' or ',' or '/' or ':' or ';' or '=' or '?' or '@':
                return true;
            default:
                return true;
        }
    }
}
