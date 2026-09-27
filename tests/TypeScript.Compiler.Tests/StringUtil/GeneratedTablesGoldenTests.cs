// 生成表的数据 golden 测试。
//
// 为什么需要它：StringUtil/IdentifierParts.Generated.cs 与 SpecialCasing.Generated.cs 是
// tools/TypeScript.CodeGen 解析 TSGO 已提交的 *_generated.go 得到的（Unicode 15.1.0）。
// 生成器只保证"再跑一次结果一样"，不保证"解析出来的数据是对的"。这里把两份数据钉死：
//   * 表的 R16/R32 项数；
//   * 全部 0..0x10FFFF 码点的成员位集 SHA-256（即 UnicodeRangeTable.Contains 的完整真值表）；
//   * specialCasingMappings 的条目数与规范化文本 SHA-256。
// 期望值来自独立实现：artifacts/gen-verify/verify.mjs 用 JS 重新实现 Go 标准库 unicode.Is
// （unicode/tables.go 的 is16/is32）解析同一份 Go 源并输出同样的摘要，
// 与 artifacts/gen-verify/Program.cs（直接编译生成文件 + UnicodeRangeTable 打印摘要）逐字节对拍过。
//
// 重新计算（例如 TSGO 升级了钉住的 Unicode 版本之后）：
//   node artifacts/gen-verify/verify.mjs
//   dotnet run --project artifacts/gen-verify/GenVerify.csproj
// 两行输出必须一致，然后把新值更新到这里。数字变化本身不是 bug，但必须是"有意升级"的结果。
using System.Security.Cryptography;
using System.Text;
using TypeScript.StringUtil;
using static TypeScript.StringUtil.StringUtil;
using Xunit;

namespace TypeScript.Tests;

public class StringUtilGeneratedTablesGoldenTests
{
    private const int _maxCodePoint = 0x110000;

    private static string MembershipDigest(UnicodeRangeTable table)
    {
        var bits = new byte[_maxCodePoint >> 3];
        for (var cp = 0; cp < _maxCodePoint; cp++)
        {
            if (table.Contains(cp))
            {
                bits[cp >> 3] |= (byte)(1 << (cp & 7));
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(bits));
    }

    [Fact]
    public void IdentifierStartTableMatchesTsgoData()
    {
        Assert.Equal(357, UnicodeESNextIdentifierStart.R16.Length);
        Assert.Equal(267, UnicodeESNextIdentifierStart.R32.Length);
        Assert.Equal(6, UnicodeESNextIdentifierStart.LatinOffset);
        Assert.Equal("6a617bf5d1a7c6ee6689f586552721aa61d7709eadba735ec03494f50fd31a39", MembershipDigest(UnicodeESNextIdentifierStart));
    }

    [Fact]
    public void IdentifierPartTableMatchesTsgoData()
    {
        Assert.Equal(411, UnicodeESNextIdentifierPart.R16.Length);
        Assert.Equal(330, UnicodeESNextIdentifierPart.R32.Length);
        Assert.Equal(8, UnicodeESNextIdentifierPart.LatinOffset);
        Assert.Equal("b2126bd634e386a777da5fd9d6b7ce7a5d2224c9668858b31210280a42eda217", MembershipDigest(UnicodeESNextIdentifierPart));
    }

    [Fact]
    public void CasedRangesMatchTsgoData()
    {
        Assert.Equal(90, UnicodeCasedRanges.R16.Length);
        Assert.Equal(57, UnicodeCasedRanges.R32.Length);
        Assert.Equal("a932c7fddc5b13d2c6890252937711221e2729f1f5a201478be9292e2f5b27cb", MembershipDigest(UnicodeCasedRanges));
    }

    [Fact]
    public void CaseIgnorableRangesMatchTsgoData()
    {
        Assert.Equal(238, UnicodeCaseIgnorableRanges.R16.Length);
        Assert.Equal(139, UnicodeCaseIgnorableRanges.R32.Length);
        Assert.Equal("c0b4d8cc568e1d6eb9f7b330fee03706800ce8ca3b6ca782d35c9b52e86c2c8b", MembershipDigest(UnicodeCaseIgnorableRanges));
    }

    [Fact]
    public void SpecialCasingMappingsMatchTsgoData()
    {
        var canonical = string.Join(
            '\n',
            SpecialCasingMappings
                .OrderBy(static kv => kv.Key)
                .Select(static kv => string.Join('|', kv.Key, kv.Value.Lower, kv.Value.Upper, kv.Value.ConditionalLower, kv.Value.Condition)));

        Assert.Equal(2927, SpecialCasingMappings.Count);
        Assert.Equal("53bdbdfac185a0627772a84a9294ddab6134876ddbc11d9c9b9899c943714e1b", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    [Fact]
    public void LookupRejectsOutOfRangeCodePoints()
    {
        // Go 的 unicode.Is 对负数（reinterpret 成 uint32 的巨大值）与 > MaxRune 的输入都返回 false。
        Assert.False(UnicodeESNextIdentifierPart.Contains(-1));
        Assert.False(UnicodeESNextIdentifierPart.Contains(_maxCodePoint));
        Assert.False(UnicodeESNextIdentifierPart.Contains(0x7FFFFFFF));
        Assert.True(UnicodeESNextIdentifierPart.Contains(0x41));
        Assert.False(UnicodeESNextIdentifierPart.Contains(0x20));
    }
}
