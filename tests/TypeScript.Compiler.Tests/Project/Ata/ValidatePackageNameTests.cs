// ported from: internal/project/ata/validatepackagename_test.go
//
// 说明（与 Go 测试的差异，逐条）：
// - Go 的 `assert.Equal(t, status, ata.NameTooLong)` → xunit 的 `Assert.Equal(expected, actual)`（xunit 的参数顺序是
//   (expected, actual)，与 Go 的 gotest.tools 相反）。
// - Go 的 `t.Run(...)` 每个子测试落成一个 `[Fact]`；`t.Parallel()` 不移植（xunit 的并行是集合级的，
//   这些用例彼此独立、无共享状态）。
// - Go 调用 `ata.ValidatePackageName`（包级函数）→ C# 落到静态类 `PackageNameValidation`（见 ValidatePackageName.cs
//   文件头第 1 条）。
// - 测试里 `strings.Builder` 的"复制自身 8 次"用同样的写法（`sb.Append(sb.ToString())`）。
using System.Text;
using TypeScript.Project.Ata;
using Xunit;

namespace TypeScript.Tests;

public class ValidatePackageNameTests
{
    [Fact]
    public void NameCannotBeTooLong()
    {
        var packageName = new StringBuilder();
        packageName.Append("a");
        for (var i = 0; i < 8; i++)
        {
            packageName.Append(packageName.ToString());
        }

        var (status, _, _) = PackageNameValidation.ValidatePackageName(packageName.ToString());
        Assert.Equal(NameValidationResult.NameTooLong, status);
    }

    [Fact]
    public void PackageNameCannotStartWithDot()
    {
        var (status, _, _) = PackageNameValidation.ValidatePackageName(".foo");
        Assert.Equal(NameValidationResult.NameStartsWithDot, status);
    }

    [Fact]
    public void PackageNameCannotStartWithUnderscore()
    {
        var (status, _, _) = PackageNameValidation.ValidatePackageName("_foo");
        Assert.Equal(NameValidationResult.NameStartsWithUnderscore, status);
    }

    [Fact]
    public void PackageNonUriSafeCharactersAreNotSupported()
    {
        var (status, _, _) = PackageNameValidation.ValidatePackageName("  scope  ");
        Assert.Equal(NameValidationResult.NameContainsNonURISafeCharacters, status);
        (status, _, _) = PackageNameValidation.ValidatePackageName("; say ‘Hello from TypeScript!’ #");
        Assert.Equal(NameValidationResult.NameContainsNonURISafeCharacters, status);
        (status, _, _) = PackageNameValidation.ValidatePackageName("a/b/c");
        Assert.Equal(NameValidationResult.NameContainsNonURISafeCharacters, status);
    }

    [Fact]
    public void ScopedPackageNameIsSupported()
    {
        var (status, _, _) = PackageNameValidation.ValidatePackageName("@scope/bar");
        Assert.Equal(NameValidationResult.NameOk, status);
    }

    [Fact]
    public void ScopedNameInScopedPackageNameCannotStartWithDot()
    {
        var (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@.scope/bar");
        Assert.Equal(NameValidationResult.NameStartsWithDot, status);
        Assert.Equal(".scope", name);
        Assert.True(isScopeName);
        (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@.scope/.bar");
        Assert.Equal(NameValidationResult.NameStartsWithDot, status);
        Assert.Equal(".scope", name);
        Assert.True(isScopeName);
    }

    [Fact]
    public void ScopedNameInScopedPackageNameCannotStartWithUnderscore()
    {
        var (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@_scope/bar");
        Assert.Equal(NameValidationResult.NameStartsWithUnderscore, status);
        Assert.Equal("_scope", name);
        Assert.True(isScopeName);
        (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@_scope/_bar");
        Assert.Equal(NameValidationResult.NameStartsWithUnderscore, status);
        Assert.Equal("_scope", name);
        Assert.True(isScopeName);
    }

    [Fact]
    public void ScopeNameInScopedPackageNameWithNonUriSafeCharactersAreNotSupported()
    {
        var (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@  scope  /bar");
        Assert.Equal(NameValidationResult.NameContainsNonURISafeCharacters, status);
        Assert.Equal("  scope  ", name);
        Assert.True(isScopeName);
        (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@; say ‘Hello from TypeScript!’ #/bar");
        Assert.Equal(NameValidationResult.NameContainsNonURISafeCharacters, status);
        Assert.Equal("; say ‘Hello from TypeScript!’ #", name);
        Assert.True(isScopeName);
        (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@  scope  /  bar  ");
        Assert.Equal(NameValidationResult.NameContainsNonURISafeCharacters, status);
        Assert.Equal("  scope  ", name);
        Assert.True(isScopeName);
    }

    [Fact]
    public void PackageNameInScopedPackageNameCannotStartWithDot()
    {
        var (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@scope/.bar");
        Assert.Equal(NameValidationResult.NameStartsWithDot, status);
        Assert.Equal(".bar", name);
        Assert.False(isScopeName);
    }

    [Fact]
    public void PackageNameInScopedPackageNameCannotStartWithUnderscore()
    {
        var (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@scope/_bar");
        Assert.Equal(NameValidationResult.NameStartsWithUnderscore, status);
        Assert.Equal("_bar", name);
        Assert.False(isScopeName);
    }

    [Fact]
    public void PackageNameInScopedPackageNameWithNonUriSafeCharactersAreNotSupported()
    {
        var (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@scope/  bar  ");
        Assert.Equal(NameValidationResult.NameContainsNonURISafeCharacters, status);
        Assert.Equal("  bar  ", name);
        Assert.False(isScopeName);
        (status, name, isScopeName) = PackageNameValidation.ValidatePackageName("@scope/; say ‘Hello from TypeScript!’ #");
        Assert.Equal(NameValidationResult.NameContainsNonURISafeCharacters, status);
        Assert.Equal("; say ‘Hello from TypeScript!’ #", name);
        Assert.False(isScopeName);
    }
}
