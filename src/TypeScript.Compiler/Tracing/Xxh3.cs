// ported from: 无 Go 源文件 —— 复刻 github.com/zeebo/xxh3 的 XXH3-64（tracing.go 的 stableTraceThreadID 用它）。
//
// internal/tracing/tracing.go 的 stableTraceThreadID 用 `xxh3.New()` + `WriteString` + `Sum64()` 给"文件线程"
// 分配稳定的线程 ID；这个 ID 会**逐字节**出现在 baseline 里（tsc/testdata/baselines/reference/tsc/generateTrace/
// generateTrace-*.js 的 trace.json 有 `"tid":852581467` 这样的值），所以必须与 Go 侧逐位一致，不能换别的哈希。
// 本文件按 XXH3 规范（xxHash v0.8 的 XXH3_64bits，默认 secret、seed=0）实现；对应 Go 侧
// `xxh3.New().WriteString(s).Sum64()` 的单次写入语义（XXH3 的流式与一次性结果相同）。
//
// 校验向量来自上面的 baseline（`tid - 1_000_000` = `Sum64() % 1_000_000_000`）：
//   "file:/home/src/tslibs/TS/Lib/lib.es2025.full.d.ts" → 851581467
//   "file:/home/src/workspaces/project/a.ts"            → 353130385
//   "file:/home/src/workspaces/project/main.ts"         →  99209852
//   "file:/home/src/workspaces/project/types.ts"        → 552096334
// 见 tests/TypeScript.Compiler.Tests/Tracing/TracingTests.cs 的 Xxh3 用例。
//
// 这是 tracing 包内部的实现细节（Go 里 xxh3 是外部依赖，不是 TSGO 的导出 API），所以是 internal。
using System.Buffers.Binary;

namespace TypeScript.Tracing;

internal static class Xxh3
{
    private const ulong _prime64_1 = 0x9E3779B185EBCA87;
    private const ulong _prime64_2 = 0xC2B2AE3D27D4EB4F;
    private const ulong _prime64_3 = 0x165667B19E3779F9;
    private const ulong _prime64_4 = 0x85EBCA77C2B2AE63;
    private const ulong _prime64_5 = 0x27D4EB2F165667C5;
    private const ulong _prime32_1 = 0x9E3779B1;
    private const ulong _prime32_2 = 0x85EBCA77;
    private const ulong _prime32_3 = 0xC2B2AE3D;
    private const ulong _primeMx1 = 0x165667919E3779F9;
    private const ulong _primeMx2 = 0x9FB21C651E98DF25;

    private const int _stripeLen = 64;
    private const int _secretConsumeRate = 8;
    private const int _secretLastAccStart = 7;
    private const int _secretMergeAccsStart = 11;
    private const int _midsizeStartOffset = 3;
    private const int _midsizeLastOffset = 17;
    private const int _secretSizeMin = 136;

    /// <summary>XXH3 的 192 字节默认 secret（xxHash 的 <c>XXH3_kSecret</c>）。</summary>
    private static readonly byte[] _secret =
    [
        0xb8, 0xfe, 0x6c, 0x39, 0x23, 0xa4, 0x4b, 0xbe, 0x7c, 0x01, 0x81, 0x2c, 0xf7, 0x21, 0xad, 0x1c,
        0xde, 0xd4, 0x6d, 0xe9, 0x83, 0x90, 0x97, 0xdb, 0x72, 0x40, 0xa4, 0xa4, 0xb7, 0xb3, 0x67, 0x1f,
        0xcb, 0x79, 0xe6, 0x4e, 0xcc, 0xc0, 0xe5, 0x78, 0x82, 0x5a, 0xd0, 0x7d, 0xcc, 0xff, 0x72, 0x21,
        0xb8, 0x08, 0x46, 0x74, 0xf7, 0x43, 0x24, 0x8e, 0xe0, 0x35, 0x90, 0xe6, 0x81, 0x3a, 0x26, 0x4c,
        0x3c, 0x28, 0x52, 0xbb, 0x91, 0xc3, 0x00, 0xcb, 0x88, 0xd0, 0x65, 0x8b, 0x1b, 0x53, 0x2e, 0xa3,
        0x71, 0x64, 0x48, 0x97, 0xa2, 0x0d, 0xf9, 0x4e, 0x38, 0x19, 0xef, 0x46, 0xa9, 0xde, 0xac, 0xd8,
        0xa8, 0xfa, 0x76, 0x3f, 0xe3, 0x9c, 0x34, 0x3f, 0xf9, 0xdc, 0xbb, 0xc7, 0xc7, 0x0b, 0x4f, 0x1d,
        0x8a, 0x51, 0xe0, 0x4b, 0xcd, 0xb4, 0x59, 0x31, 0xc8, 0x9f, 0x7e, 0xc9, 0xd9, 0x78, 0x73, 0x64,
        0xea, 0xc5, 0xac, 0x83, 0x34, 0xd3, 0xeb, 0xc3, 0xc5, 0x81, 0xa0, 0xff, 0xfa, 0x13, 0x63, 0xeb,
        0x17, 0x0d, 0xdd, 0x51, 0xb7, 0xf0, 0xda, 0x49, 0xd3, 0x16, 0x55, 0x26, 0x29, 0xd4, 0x68, 0x9e,
        0x2b, 0x16, 0xbe, 0x58, 0x7d, 0x47, 0xa1, 0xfc, 0x8f, 0xf8, 0xb8, 0xd1, 0x7a, 0xd0, 0x31, 0xce,
        0x45, 0xcb, 0x3a, 0x8f, 0x95, 0x16, 0x04, 0x28, 0xaf, 0xd7, 0xfb, 0xca, 0xbb, 0x4b, 0x40, 0x7e,
    ];

    /// <summary>Go: <c>h := xxh3.New(); h.WriteString(s); h.Sum64()</c>。</summary>
    internal static ulong Hash64(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        if (len <= 16)
        {
            return Len0To16(input);
        }

        if (len <= 128)
        {
            return Len17To128(input);
        }

        if (len <= 240)
        {
            return Len129To240(input);
        }

        return HashLong(input);
    }

    private static ulong Len0To16(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        if (len > 8)
        {
            return Len9To16(input);
        }

        if (len >= 4)
        {
            return Len4To8(input);
        }

        if (len > 0)
        {
            return Len1To3(input);
        }

        return Avalanche64(ReadLe64(_secret, 56) ^ ReadLe64(_secret, 64));
    }

    private static ulong Len1To3(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var c1 = (uint)input[0];
        var c2 = (uint)input[len >> 1];
        var c3 = (uint)input[len - 1];
        var combined = (c1 << 16) | (c2 << 24) | (c3 << 0) | ((uint)len << 8);
        var bitflip = (ulong)(ReadLe32(_secret, 0) ^ ReadLe32(_secret, 4));
        var keyed = (ulong)combined ^ bitflip;
        return Avalanche64(keyed);
    }

    private static ulong Len4To8(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var input1 = ReadLe32(input, 0);
        var input2 = ReadLe32(input, len - 4);
        var bitflip = ReadLe64(_secret, 8) ^ ReadLe64(_secret, 16);
        var input64 = input2 + ((ulong)input1 << 32);
        var keyed = input64 ^ bitflip;
        return Rrmxmx(keyed, (ulong)len);
    }

    private static ulong Len9To16(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var bitflip1 = ReadLe64(_secret, 24) ^ ReadLe64(_secret, 32);
        var bitflip2 = ReadLe64(_secret, 40) ^ ReadLe64(_secret, 48);
        var inputLo = ReadLe64(input, 0) ^ bitflip1;
        var inputHi = ReadLe64(input, len - 8) ^ bitflip2;
        var acc = (ulong)len + BinaryPrimitives.ReverseEndianness(inputLo) + inputHi + Mul128Fold64(inputLo, inputHi);
        return Avalanche(acc);
    }

    private static ulong Len17To128(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var acc = (ulong)len * _prime64_1;
        if (len > 32)
        {
            if (len > 64)
            {
                if (len > 96)
                {
                    acc += Mix16B(input, 48, 96);
                    acc += Mix16B(input, len - 64, 112);
                }

                acc += Mix16B(input, 32, 64);
                acc += Mix16B(input, len - 48, 80);
            }

            acc += Mix16B(input, 16, 32);
            acc += Mix16B(input, len - 32, 48);
        }

        acc += Mix16B(input, 0, 0);
        acc += Mix16B(input, len - 16, 16);
        return Avalanche(acc);
    }

    private static ulong Len129To240(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var nbRounds = len / 16;
        var acc = (ulong)len * _prime64_1;
        for (var i = 0; i < 8; i++)
        {
            acc += Mix16B(input, 16 * i, 16 * i);
        }

        acc = Avalanche(acc);

        for (var i = 8; i < nbRounds; i++)
        {
            acc += Mix16B(input, 16 * i, 16 * (i - 8) + _midsizeStartOffset);
        }

        // last bytes
        acc += Mix16B(input, len - 16, _secretSizeMin - _midsizeLastOffset);
        return Avalanche(acc);
    }

    private static ulong HashLong(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var acc = new ulong[8]
        {
            _prime32_3, _prime64_1, _prime64_2, _prime64_3, _prime64_4, _prime32_2, _prime64_5, _prime32_1,
        };
        var secretSize = _secret.Length;
        var nbStripesPerBlock = (secretSize - _stripeLen) / _secretConsumeRate;
        var blockLen = _stripeLen * nbStripesPerBlock;
        var nbBlocks = (len - 1) / blockLen;
        for (var n = 0; n < nbBlocks; n++)
        {
            Accumulate(acc, input, n * blockLen, 0, nbStripesPerBlock);
            ScrambleAcc(acc, secretSize - _stripeLen);
        }

        var nbStripes = (len - 1 - (blockLen * nbBlocks)) / _stripeLen;
        Accumulate(acc, input, nbBlocks * blockLen, 0, nbStripes);

        // last stripe
        var stripe = len - _stripeLen;
        Accumulate512(acc, input, stripe, secretSize - _stripeLen - _secretLastAccStart);
        return MergeAccs(acc, _secretMergeAccsStart, (ulong)len * _prime64_1);
    }

    private static void Accumulate(ulong[] acc, ReadOnlySpan<byte> input, int inputOffset, int secretOffset, int nbStripes)
    {
        for (var n = 0; n < nbStripes; n++)
        {
            Accumulate512(acc, input, inputOffset + (n * _stripeLen), secretOffset + (n * _secretConsumeRate));
        }
    }

    // XXH3 的标量 accumulate：8 个 64 位 lane，相邻 lane 交换累加。
    private static void Accumulate512(ulong[] acc, ReadOnlySpan<byte> input, int inputOffset, int secretOffset)
    {
        for (var i = 0; i < 8; i++)
        {
            var dataVal = ReadLe64(input, inputOffset + (8 * i));
            var dataKey = dataVal ^ ReadLe64(_secret, secretOffset + (8 * i));
            acc[i ^ 1] += dataVal;
            acc[i] += (dataKey & 0xFFFFFFFF) * (dataKey >> 32);
        }
    }

    private static void ScrambleAcc(ulong[] acc, int secretOffset)
    {
        for (var i = 0; i < 8; i++)
        {
            var key64 = ReadLe64(_secret, secretOffset + (8 * i));
            var acc64 = acc[i];
            acc64 = XorShift64(acc64, 47);
            acc64 ^= key64;
            acc64 *= _prime32_1;
            acc[i] = acc64;
        }
    }

    private static ulong MergeAccs(ulong[] acc, int secretOffset, ulong start)
    {
        var result = start;
        for (var i = 0; i < 4; i++)
        {
            result += Mul128Fold64(
                acc[2 * i] ^ ReadLe64(_secret, secretOffset + (16 * i)),
                acc[(2 * i) + 1] ^ ReadLe64(_secret, secretOffset + (16 * i) + 8));
        }

        return Avalanche(result);
    }

    private static ulong Mix16B(ReadOnlySpan<byte> input, int inputOffset, int secretOffset)
    {
        return Mul128Fold64(
            ReadLe64(input, inputOffset) ^ ReadLe64(_secret, secretOffset),
            ReadLe64(input, inputOffset + 8) ^ ReadLe64(_secret, secretOffset + 8));
    }

    private static ulong Mul128Fold64(ulong lhs, ulong rhs)
    {
        var product = (UInt128)lhs * rhs;
        return (ulong)product ^ (ulong)(product >> 64);
    }

    private static ulong XorShift64(ulong value, int shift) => value ^ (value >> shift);

    private static ulong Avalanche(ulong h)
    {
        h = XorShift64(h, 37);
        h *= _primeMx1;
        return XorShift64(h, 32);
    }

    private static ulong Avalanche64(ulong h)
    {
        h ^= h >> 33;
        h *= _prime64_2;
        h ^= h >> 29;
        h *= _prime64_3;
        h ^= h >> 32;
        return h;
    }

    private static ulong Rrmxmx(ulong h64, ulong len)
    {
        h64 ^= RotateLeft(h64, 49) ^ RotateLeft(h64, 24);
        h64 *= _primeMx2;
        h64 ^= (h64 >> 35) + len;
        h64 *= _primeMx2;
        return XorShift64(h64, 28);
    }

    private static ulong RotateLeft(ulong value, int shift) => (value << shift) | (value >> (64 - shift));

    private static ulong ReadLe64(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(source[offset..]);

    private static uint ReadLe32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);
}
