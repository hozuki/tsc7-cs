// ported from: internal/ls/string_completions_test.go:1-39
//
// 与 Go 测试的差异（逐条）：
//
// 1) Go 的 `tryRemoveDirectoryPrefix(path, prefix, useCaseSensitiveFileNames) *string` → C# 的
//    `Ls.TryRemoveDirectoryPrefix(string path, string prefix, bool useCaseSensitiveFileNames)` 返回 `string?`
//    （Go 的 `*string` 表示"没有可去除的前缀"⇒ C# 的 `null`）。
// 2) Go 的 `assert.Equal(t, *actual, "x.ts")` → xunit 的 `Assert.Equal`（先判非空，等价于 Go 的 `t.Fatal` 分支）。
using TypeScript.Ls;
using Xunit;

namespace TypeScript.Tests;

public class StringCompletionsTests
{
    /// <summary>
    /// TestTryRemoveDirectoryPrefixCaseFoldingShrinksPrefix reproduces a panic that used to occur
    /// when tryRemoveDirectoryPrefix confirmed a case-insensitive directory match via
    /// GetCanonicalFileName, then sliced the raw (non-canonicalized) path using the raw byte length
    /// of prefix. Each Kelvin sign '\u212A' below case-folds to the single-byte 'k', so the raw
    /// prefix is longer in bytes (15) than path (12), even though path's canonical form is
    /// case-insensitively prefixed by prefix's canonical form. Slicing path[len(prefix):] used to
    /// panic with "slice bounds out of range [15:12]"; tryRemoveDirectoryPrefix must instead trim by
    /// rune count via tspath.TrimFilePathPrefix.
    /// </summary>
    [Fact]
    public void TryRemoveDirectoryPrefixCaseFoldingShrinksPrefix()
    {
        const string prefix = "/a/\u212A\u212A\u212A\u212A";
        const string path = "/a/kkkk/x.ts";
        var actual = TypeScript.Ls.Ls.TryRemoveDirectoryPrefix(path, prefix, false /*useCaseSensitiveFileNames*/);
        Assert.True(actual is not null, "expected a non-nil result");
        Assert.Equal("x.ts", actual);
    }
}
