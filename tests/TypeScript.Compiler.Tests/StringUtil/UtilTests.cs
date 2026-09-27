// ported from: internal/stringutil/util_test.go
//
// Go 的 `t.Run(name, ...)` 子测试 + 表驱动 → xunit 的 [Theory] + [InlineData]：用例名原样保留为第一个
// 参数，通过 ITestOutputHelper 打进测试输出（xunit 2.9.3 的 InlineData 没有 DisplayName 属性，而
// 不引用该参数会触发 xUnit1026 错误）。
using Xunit;
using Xunit.Abstractions;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Tests;

public class StringUtilUtilTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("encodes spaces as percent20", "a b", "a%20b")]
    [InlineData("preserves reserved uri characters", ";/?:@&=+$,#", ";/?:@&=+$,#")]
    [InlineData(
        "encodes brackets and unicode using utf8 bytes",
        "\u2460\u216B\u3128\u3129 U1[abc]", // ①Ⅻㄨㄩ U1[abc]
        "%E2%91%A0%E2%85%AB%E3%84%A8%E3%84%A9%20U1%5Babc%5D")]
    public void TestEncodeURI(string name, string input, string expected)
    {
        output.WriteLine(name);
        Assert.Equal(expected, EncodeURI(input));
    }
}
