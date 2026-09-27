// ported from: internal/core/pattern_test.go
//
// 说明：Go 的 TestPatternOverlappingMatch 用 t.Errorf 逐项报错，xunit 里逐项 Assert 等价。
// 测试方法名与 Go 逐字对应（保留 Test 前缀）；Go 的 t.Parallel() 不需要（xunit 默认并行类内串行，
// 这些断言不共享状态）。
using TypeScript.Core;
using Xunit;
using static TypeScript.Core.Core;

namespace TypeScript.Tests;

public class CorePatternTests
{
    [Fact]
    public void TestPatternOverlappingMatch()
    {
        var p = TryParsePattern("ab*ab");
        Assert.False(p.Matches("ab"), "expected 'ab' not to match 'ab*ab'");
        Assert.True(p.Matches("abXab"), "expected 'abXab' to match 'ab*ab'");
        Assert.Equal("X", p.MatchedText("abXab"));
        Assert.True(p.Matches("abab"), "expected 'abab' to match 'ab*ab'");
        Assert.Equal("", p.MatchedText("abab"));
    }
}
