// ported from: github.com/zeebo/xxh3（XXH3 的标量参考实现；64 位与 128 位共用同一套 secret/累加/混合代码）
//
// Go 侧用的是第三方包 `github.com/zeebo/xxh3`，C# 没有对应物：本项目**不引 NuGet**
// （System.IO.Hashing 只出现在一次性探针 artifacts/xxh3-probe 里当 oracle），这里自己写一份标量实现，
// 目标是与 xxHash 参考实现 **bit-exact**（对拍结果见 artifacts/xxh3-probe）。
//
// 两个调用面（合并前是 Tracing/Xxh3.cs 与 Xxh3/Xxh3.cs 两份实现，现已合成这一份）：
//
// 1) **XXH3-128**（contentmapper / ast / checker 的 Identity 哈希）：
//      xxh3.Uint128{Loo, Hi}         → System.UInt128（高 64 位 = Hi，低 64 位 = Lo；Ast.SourceFile.Hash 已用这个类型）
//      xxh3.Hash128([]byte)          → Hash128(ReadOnlySpan<byte>)
//      xxh3.Hash128Seed(b, seed)     → Hash128(ReadOnlySpan<byte>, ulong seed)
//      xxh3.HashString128(string)    → HashString128(string)
//      xxh3.Uint128.Bytes() [16]byte → Bytes(UInt128)（**规范大端** 16 字节：Hi 大端在前、Lo 大端在后，
//        与 Go 的 Bytes() 逐字节一致——zeebo/xxh3 utils.go 的注释是 "canonical form (big-endian encoded)"，
//        不是小端，见 `Bytes` 方法上的注释）
//
// 2) **XXH3-64**（internal/tracing/tracing.go 的 stableTraceThreadID）：
//      xxh3.New() + WriteString(...) + Sum64() → Hash64(ReadOnlySpan<byte>)
//      xxh3.HashString(s)                      → HashString64(string)
//    tracing 给"文件线程"分配稳定的线程 ID，这个 ID 会**逐字节**出现在 baseline 里
//    （tsc/testdata/baselines/reference/tsc/generateTrace/generateTrace-*.js 的 trace.json 有 `"tid":852581467`
//    这样的值），所以必须与 Go 侧逐位一致，不能换别的哈希。
//    Go 那边是流式（多次 WriteString），但 XXH3 的流式与"一次性喂完整输入"结果相同，C# 侧把各段先拼成一个
//    字符串再取 UTF-8 字节一次性哈希（Tracing.cs 的 StableTraceThreadID），结果与 Go 逐位一致。
//    校验向量（`tid` 由 `Sum64() % 1_000_000_000` + firstFileThreadID 得出，见 TracingTests 的 Xxh3 用例）：
//      "file:/home/src/tslibs/TS/Lib/lib.es2025.full.d.ts" → 2208031706851581467
//      "file:/home/src/workspaces/project/a.ts"            → 9197916062353130385
//      "file:/home/src/workspaces/project/main.ts"         → 7876163279099209852
//      "file:/home/src/workspaces/project/types.ts"        →  128579712552096334
//
// 与 Go 不一致的地方：
// 1) Go 的 string 是 UTF-8 字节序列；C# 的 string 是 UTF-16。HashString64/HashString128 按 UTF-8 编码后再哈希
//    （与 Go 对纯 UTF-8 文本的结果一致）；孤立代理项会被 UTF-8 编码替换成 U+FFFD，与 TSGO 的哨兵编码
//    （docs/architecture.md §3.1，本移植不采用）结果不同——调用点若真要哈希含孤立代理项的文本，需要先自行编码。
// 2) 未移植流式 API（Go 的 `xxh3.New()` + `Write`/`Sum64`/`Sum128`）：TSGO 里只有 internal/tracing/tracing.go
//    用流式，而它的多段写入等价于一次性哈希（见上），C# 侧已经用 Hash64 覆盖；`internal/project/
//    extendedconfigcache.go` 只是持有 `xxh3.Uint128` 字段（用 Hash128），不需要流式。
namespace TypeScript.Xxh3;

// usings 放在 file-scoped namespace 之后（与 Checker 包、Tracing.cs 同款）：编译单元级的 using
// 不参与命名空间体内的简单名解析顺序，外层命名空间的同名成员（如 `TypeScript.Encoding` 这类
// 「命名空间与类同名」的成员）会压过它；放进 namespace 声明体内则不会。
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

/// <summary>
/// XXH3（seed = 0）的标量实现：64 位与 128 位入口共用同一份 secret、累加（accumulate/scramble）与混合代码。
/// 表/常量与 xxHash 参考实现一致：<c>XXH3_kSecret</c> 192 字节、<c>XXH3_INIT_ACC</c> 8 个累加器初值、
/// prime 常量、以及 avalanche/rrmxmx 混合函数。
/// </summary>
public static class Xxh3
{
    // ---- 共享常量 ----

    private const uint _prime32_1 = 0x9E3779B1;
    private const uint _prime32_2 = 0x85EBCA77;
    private const uint _prime32_3 = 0xC2B2AE3D;
    private const ulong _prime64_1 = 0x9E3779B185EBCA87;
    private const ulong _prime64_2 = 0xC2B2AE3D27D4EB4F;
    private const ulong _prime64_3 = 0x165667B19E3779F9;
    private const ulong _prime64_4 = 0x85EBCA77C2B2AE63;
    private const ulong _prime64_5 = 0x27D4EB2F165667C5;
    private const ulong _primeMx1 = 0x165667919E3779F9;
    private const ulong _primeMx2 = 0x9FB21C651E98DF25;

    private const int _stripeLen = 64;
    private const int _secretConsumeRate = 8;
    private const int _secretSize = 192;
    private const int _accNb = 8;
    private const int _secretLastAccStart = 7;
    private const int _secretMergeAccsStart = 11;
    private const int _midsizeMax = 240;

    /// <summary>xxHash 的 <c>XXH3_SECRET_SIZE_MIN</c>（129..240 路径的末尾 32 字节用的密钥偏移基准）。</summary>
    private const int _secretSizeMin = 136;
    private const int _midsizeStartOffset = 3;
    private const int _midsizeLastOffset = 17;

    /// <summary>xxHash 的 <c>XXH3_kSecret</c>（192 字节默认密钥）。</summary>
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

    // ==========================================================================================
    // 64 位入口（Go: github.com/zeebo/xxh3 的 Hash/HashString/New().Sum64()）
    // ==========================================================================================

    /// <summary>Go: <c>h := xxh3.New(); h.WriteString(s); h.Sum64()</c>（流式与一次性结果相同，见文件头）。</summary>
    public static ulong Hash64(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        if (len <= 16)
        {
            return Len0To16_64b(input);
        }

        if (len <= 128)
        {
            return Len17To128_64b(input);
        }

        if (len <= _midsizeMax)
        {
            return Len129To240_64b(input);
        }

        return HashLong64(input);
    }

    /// <summary>Go: <c>xxh3.HashString(s string) uint64</c>（按 UTF-8 编码后哈希，见文件头）。</summary>
    public static ulong HashString64(string text)
    {
        if (text.Length == 0)
        {
            return Hash64([]);
        }

        var count = Encoding.UTF8.GetByteCount(text);
        var rented = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            var written = Encoding.UTF8.GetBytes(text, rented);
            return Hash64(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // XXH3_len_0to16_64b
    private static ulong Len0To16_64b(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        if (len > 8)
        {
            return Len9To16_64b(input);
        }

        if (len >= 4)
        {
            return Len4To8_64b(input);
        }

        if (len > 0)
        {
            return Len1To3_64b(input);
        }

        return Avalanche64(ReadLE64(_secret, 56) ^ ReadLE64(_secret, 64));
    }

    // XXH3_len_1to3_64b
    private static ulong Len1To3_64b(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var c1 = (uint)input[0];
        var c2 = (uint)input[len >> 1];
        var c3 = (uint)input[len - 1];
        var combined = (c1 << 16) | (c2 << 24) | (c3 << 0) | ((uint)len << 8);
        var bitflip = (ulong)(ReadLE32(_secret, 0) ^ ReadLE32(_secret, 4));
        var keyed = (ulong)combined ^ bitflip;
        return Avalanche64(keyed);
    }

    // XXH3_len_4to8_64b
    private static ulong Len4To8_64b(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var input1 = ReadLE32(input, 0);
        var input2 = ReadLE32(input, len - 4);
        var bitflip = ReadLE64(_secret, 8) ^ ReadLE64(_secret, 16);
        var input64 = input2 + ((ulong)input1 << 32);
        var keyed = input64 ^ bitflip;
        return Rrmxmx(keyed, (ulong)len);
    }

    // XXH3_len_9to16_64b
    private static ulong Len9To16_64b(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var bitflip1 = ReadLE64(_secret, 24) ^ ReadLE64(_secret, 32);
        var bitflip2 = ReadLE64(_secret, 40) ^ ReadLE64(_secret, 48);
        var inputLo = ReadLE64(input, 0) ^ bitflip1;
        var inputHi = ReadLE64(input, len - 8) ^ bitflip2;
        var acc = (ulong)len + BinaryPrimitives.ReverseEndianness(inputLo) + inputHi + Mul128Fold64(inputLo, inputHi);
        return Avalanche(acc);
    }

    // XXH3_len_17to128_64b
    private static ulong Len17To128_64b(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var acc = (ulong)len * _prime64_1;
        if (len > 32)
        {
            if (len > 64)
            {
                if (len > 96)
                {
                    acc += Mix16B(input, 48, _secret, 96, 0);
                    acc += Mix16B(input, len - 64, _secret, 112, 0);
                }

                acc += Mix16B(input, 32, _secret, 64, 0);
                acc += Mix16B(input, len - 48, _secret, 80, 0);
            }

            acc += Mix16B(input, 16, _secret, 32, 0);
            acc += Mix16B(input, len - 32, _secret, 48, 0);
        }

        acc += Mix16B(input, 0, _secret, 0, 0);
        acc += Mix16B(input, len - 16, _secret, 16, 0);
        return Avalanche(acc);
    }

    // XXH3_len_129to240_64b
    private static ulong Len129To240_64b(ReadOnlySpan<byte> input)
    {
        var len = input.Length;
        var nbRounds = len / 16;
        var acc = (ulong)len * _prime64_1;
        for (var i = 0; i < 8; i++)
        {
            acc += Mix16B(input, 16 * i, _secret, 16 * i, 0);
        }

        acc = Avalanche(acc);

        for (var i = 8; i < nbRounds; i++)
        {
            acc += Mix16B(input, 16 * i, _secret, (16 * (i - 8)) + _midsizeStartOffset, 0);
        }

        // 末尾 16 字节用密钥的 XXH3_SECRET_SIZE_MIN - XXH3_MIDSIZE_LASTOFFSET 处。
        acc += Mix16B(input, len - 16, _secret, _secretSizeMin - _midsizeLastOffset, 0);
        return Avalanche(acc);
    }

    // XXH3_hashLong_64b_default（seed == 0）：与 128 位共用 internal loop，只做一次 mergeAccs。
    private static ulong HashLong64(ReadOnlySpan<byte> input)
    {
        Span<ulong> acc = stackalloc ulong[_accNb];
        InitAcc(acc);
        HashLongInternalLoop(acc, input, _secret);
        return MergeAccs(acc, _secret, _secretMergeAccsStart, (ulong)input.Length * _prime64_1);
    }

    // ==========================================================================================
    // 128 位入口（Go: github.com/zeebo/xxh3 的 Hash128/Hash128Seed/HashString128/Uint128.Bytes）
    // ==========================================================================================

    /// <summary>Go: <c>xxh3.Hash128(b []byte) Uint128</c>（seed = 0）。</summary>
    public static UInt128 Hash128(ReadOnlySpan<byte> input) => Hash128(input, 0);

    /// <summary>Go: <c>xxh3.Hash128Seed(b []byte, seed uint64) Uint128</c>。</summary>
    public static UInt128 Hash128(ReadOnlySpan<byte> input, ulong seed)
    {
        var len = input.Length;
        if (len > _midsizeMax)
        {
            if (seed == 0)
            {
                return HashLong128(input, _secret);
            }

            // xxHash 对长输入 + 自定义 seed 会先由 kSecret 推出 192 字节的自定义密钥。
            Span<byte> customSecret = stackalloc byte[_secretSize];
            InitCustomSecret(customSecret, seed);
            return HashLong128(input, customSecret);
        }

        if (len > 128)
        {
            return Len129To240(input, seed);
        }

        if (len > 16)
        {
            return Len17To128(input, seed);
        }

        if (len > 8)
        {
            return Len9To16(input, seed);
        }

        if (len >= 4)
        {
            return Len4To8(input, seed);
        }

        if (len > 0)
        {
            return Len1To3(input, seed);
        }

        return Make(
            Avalanche64(((ulong)seed) ^ (ReadLE64(_secret, 64) ^ ReadLE64(_secret, 72))),
            Avalanche64(((ulong)seed) ^ (ReadLE64(_secret, 80) ^ ReadLE64(_secret, 88))));
    }

    /// <summary>Go: <c>xxh3.HashString128(s string) Uint128</c>（按 UTF-8 编码后哈希，见文件头）。</summary>
    public static UInt128 HashString128(string text)
    {
        if (text.Length == 0)
        {
            return Hash128([]);
        }

        var count = Encoding.UTF8.GetByteCount(text);
        var rented = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            var written = Encoding.UTF8.GetBytes(text, rented);
            return Hash128(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Go: <c>func (u Uint128) Bytes() [16]byte</c>（zeebo/xxh3 utils.go）——**规范大端**：
    /// <c>Hi</c> 的 8 字节大端在前，<c>Lo</c> 的 8 字节大端在后（Go 源码注释 "canonical form (big-endian encoded)"）。
    /// 调用点 <c>incremental.ComputeHash</c>（incremental/snapshot.go:32）把结果 hex 化后写进 <c>.tsbuildinfo</c>
    /// 的 <c>version</c>/<c>signature</c> 字段，所以字节序必须与 Go 一致。
    /// </summary>
    public static byte[] Bytes(UInt128 hash)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)(hash >> 64));
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), (ulong)hash);
        return bytes;
    }

    // ---- 128 位结果 ----

    // Go 的 Uint128{Lo, Hi} ↔ C# 的 UInt128（高 64 位 = Hi，低 64 位 = Lo）。
    private static UInt128 Make(ulong low64, ulong high64) =>
        ((UInt128)high64 << 64) | low64;

    // ---- 共享：混合函数 ----

    // xxHash 的 XXH3_avalanche。
    private static ulong Avalanche(ulong h)
    {
        h ^= h >> 37;
        h *= _primeMx1;
        h ^= h >> 32;
        return h;
    }

    // xxHash 的 XXH64_avalanche。
    private static ulong Avalanche64(ulong h)
    {
        h ^= h >> 33;
        h *= _prime64_2;
        h ^= h >> 29;
        h *= _prime64_3;
        h ^= h >> 32;
        return h;
    }

    // xxHash 的 XXH3_mix16B：XXH3_mul128_fold64(input_lo ^ (secret_0 + seed), input_hi ^ (secret_8 - seed))。
    // 64 位路径用 seed = 0 调用（XXH3_mix16B 在 XXH3_len_*to*_64b 里就是 secret_lo/secret_hi），是同一个式子。
    private static ulong Mix16B(ReadOnlySpan<byte> input, int inputOffset, ReadOnlySpan<byte> secret, int secretOffset, ulong seed)
    {
        var inputLo = ReadLE64(input, inputOffset);
        var inputHi = ReadLE64(input, inputOffset + 8);
        return Mul128Fold64(
            inputLo ^ (ReadLE64(secret, secretOffset) + seed),
            inputHi ^ (ReadLE64(secret, secretOffset + 8) - seed));
    }

    // xxHash 的 XXH3_mul128_fold64：128 位积的低 64 位 ^ 高 64 位。
    private static ulong Mul128Fold64(ulong left, ulong right)
    {
        var product = Mult64To128(left, right);
        return product.Low ^ product.High;
    }

    private static (ulong Low, ulong High) Mix32B((ulong Low, ulong High) acc, ReadOnlySpan<byte> input1, int offset1, ReadOnlySpan<byte> input2, int offset2, ReadOnlySpan<byte> secret, int secretOffset, ulong seed)
    {
        var low = acc.Low + Mix16B(input1, offset1, secret, secretOffset, seed);
        low ^= ReadLE64(input2, offset2) + ReadLE64(input2, offset2 + 8);
        var high = acc.High + Mix16B(input2, offset2, secret, secretOffset + 16, seed);
        high ^= ReadLE64(input1, offset1) + ReadLE64(input1, offset1 + 8);
        return (low, high);
    }

    private static ulong ReadLE64(ReadOnlySpan<byte> input, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(offset));

    private static uint ReadLE32(ReadOnlySpan<byte> input, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset));

    // XXH_mult64to128：a * b 的 128 位结果。
    // 注意 .NET 的签名是 `Math.BigMul(ulong a, ulong b, out ulong low)`，**返回值是 high**（与 Go 的 bits.Mul64
    // 正好相反：Go 返回 (hi, lo)）——第一版就是这里把高低位弄反了，只有 mul128_fold64 的路径因为 xor 可交换而侥幸通过。
    private static (ulong Low, ulong High) Mult64To128(ulong left, ulong right)
    {
        var high = Math.BigMul(left, right, out var low);
        return (low, high);
    }

    // ---- 共享：长输入路径（> 240 字节）的累加循环 ----

    private static void InitAcc(Span<ulong> acc)
    {
        // XXH3_INIT_ACC
        acc[0] = _prime32_3;
        acc[1] = _prime64_1;
        acc[2] = _prime64_2;
        acc[3] = _prime64_3;
        acc[4] = _prime64_4;
        acc[5] = _prime32_2;
        acc[6] = _prime64_5;
        acc[7] = _prime32_1;
    }

    // XXH3_hashLong_internal_loop：64 位与 128 位的长输入路径只在"如何把 acc 收尾"上不同（见 HashLong64/HashLong128）。
    private static void HashLongInternalLoop(Span<ulong> acc, ReadOnlySpan<byte> input, ReadOnlySpan<byte> secret)
    {
        var len = input.Length;
        var nbStripesPerBlock = (_secretSize - _stripeLen) / _secretConsumeRate;
        var blockLen = _stripeLen * nbStripesPerBlock;
        var nbBlocks = (len - 1) / blockLen;
        for (var n = 0; n < nbBlocks; n++)
        {
            Accumulate(acc, input, n * blockLen, nbStripesPerBlock, secret);
            ScrambleAcc(acc, secret, _secretSize - _stripeLen);
        }

        var nbStripes = ((len - 1) - (blockLen * nbBlocks)) / _stripeLen;
        Accumulate(acc, input, nbBlocks * blockLen, nbStripes, secret);
        // 最后一条 stripe 用密钥末尾的固定偏移。
        Accumulate512(acc, input, len - _stripeLen, secret, _secretSize - _stripeLen - _secretLastAccStart);
    }

    // XXH3_hashLong_128b_default / XXH3_hashLong_128b_withSecret
    private static UInt128 HashLong128(ReadOnlySpan<byte> input, ReadOnlySpan<byte> secret)
    {
        var len = input.Length;
        Span<ulong> acc = stackalloc ulong[_accNb];
        InitAcc(acc);
        HashLongInternalLoop(acc, input, secret);

        var low64 = MergeAccs(acc, secret, _secretMergeAccsStart, (ulong)len * _prime64_1);
        var high64 = MergeAccs(acc, secret, _secretSize - (_accNb * 8) - _secretMergeAccsStart, ~((ulong)len * _prime64_2));
        return Make(low64, high64);
    }

    // XXH3_initCustomSecret_scalar
    private static void InitCustomSecret(Span<byte> customSecret, ulong seed)
    {
        for (var i = 0; i < _secretSize / 16; i++)
        {
            var lo = ReadLE64(_secret, 16 * i) + seed;
            var hi = ReadLE64(_secret, (16 * i) + 8) - seed;
            BinaryPrimitives.WriteUInt64LittleEndian(customSecret.Slice(16 * i), lo);
            BinaryPrimitives.WriteUInt64LittleEndian(customSecret.Slice((16 * i) + 8), hi);
        }
    }

    private static void Accumulate(Span<ulong> acc, ReadOnlySpan<byte> input, int inputOffset, int nbStripes, ReadOnlySpan<byte> secret)
    {
        for (var n = 0; n < nbStripes; n++)
        {
            Accumulate512(acc, input, inputOffset + (n * _stripeLen), secret, n * _secretConsumeRate);
        }
    }

    // XXH3_accumulate_512_scalar（64/128 共用：lane 交换相加 + 低 32 位 × 高 32 位）
    private static void Accumulate512(Span<ulong> acc, ReadOnlySpan<byte> input, int inputOffset, ReadOnlySpan<byte> secret, int secretOffset)
    {
        for (var i = 0; i < _accNb; i++)
        {
            var dataVal = ReadLE64(input, inputOffset + (8 * i));
            var dataKey = dataVal ^ ReadLE64(secret, secretOffset + (8 * i));
            acc[i ^ 1] += dataVal; // 相邻 lane 交换相加
            acc[i] += (ulong)(uint)dataKey * (dataKey >> 32);
        }
    }

    // XXH3_scrambleAcc_scalar
    private static void ScrambleAcc(Span<ulong> acc, ReadOnlySpan<byte> secret, int secretOffset)
    {
        for (var i = 0; i < _accNb; i++)
        {
            var key64 = ReadLE64(secret, secretOffset + (8 * i));
            var acc64 = acc[i];
            acc64 ^= acc64 >> 47;
            acc64 ^= key64;
            acc64 *= _prime32_1;
            acc[i] = acc64;
        }
    }

    // XXH3_mergeAccs
    private static ulong MergeAccs(ReadOnlySpan<ulong> acc, ReadOnlySpan<byte> secret, int secretOffset, ulong start)
    {
        var result = start;
        for (var i = 0; i < 4; i++)
        {
            // XXH3_mix2Accs(acc + 2i, secret + 16i) = mul128_fold64(acc[2i] ^ LE64(secret), acc[2i+1] ^ LE64(secret+8))
            result += Mul128Fold64(
                acc[2 * i] ^ ReadLE64(secret, secretOffset + (16 * i)),
                acc[(2 * i) + 1] ^ ReadLE64(secret, secretOffset + (16 * i) + 8));
        }

        return Avalanche(result);
    }

    // ---- 128 位专用：短输入路径（64 位有另一套独立公式，见上面的 Len*_64b） ----

    // XXH3_len_1to3_128b
    private static UInt128 Len1To3(ReadOnlySpan<byte> input, ulong seed)
    {
        var len = input.Length;
        var c1 = input[0];
        var c2 = input[len >> 1];
        var c3 = input[len - 1];
        var combinedLow = ((uint)c1 << 16) | ((uint)c2 << 24) | c3 | ((uint)len << 8);
        var combinedHigh = uint.RotateLeft(BinaryPrimitives.ReverseEndianness(combinedLow), 13);
        var bitflipLow = (ulong)(ReadLE32(_secret, 0) ^ ReadLE32(_secret, 4)) + seed;
        var bitflipHigh = (ulong)(ReadLE32(_secret, 8) ^ ReadLE32(_secret, 12)) - seed;
        var keyedLow = combinedLow ^ bitflipLow;
        var keyedHigh = combinedHigh ^ bitflipHigh;
        return Make(Avalanche64(keyedLow), Avalanche64(keyedHigh));
    }

    // XXH3_len_4to8_128b
    private static UInt128 Len4To8(ReadOnlySpan<byte> input, ulong seed)
    {
        var len = input.Length;
        seed ^= (ulong)BinaryPrimitives.ReverseEndianness((uint)seed) << 32;
        var inputLo = ReadLE32(input, 0);
        var inputHi = ReadLE32(input, len - 4);
        var input64 = inputLo + ((ulong)inputHi << 32);
        var bitflip = (ReadLE64(_secret, 16) ^ ReadLE64(_secret, 24)) + seed;
        var keyed = input64 ^ bitflip;

        var m128 = Mult64To128(keyed, _prime64_1 + ((ulong)len << 2));
        m128.High += m128.Low << 1;
        m128.Low ^= m128.High >> 3;
        m128.Low ^= m128.Low >> 35;
        m128.Low *= _primeMx2;
        m128.Low ^= m128.Low >> 28;
        m128.High = Avalanche(m128.High);
        return Make(m128.Low, m128.High);
    }

    // XXH3_len_9to16_128b
    private static UInt128 Len9To16(ReadOnlySpan<byte> input, ulong seed)
    {
        var len = input.Length;
        var bitflipLow = (ReadLE64(_secret, 32) ^ ReadLE64(_secret, 40)) - seed;
        var bitflipHigh = (ReadLE64(_secret, 48) ^ ReadLE64(_secret, 56)) + seed;
        var inputLo = ReadLE64(input, 0);
        var inputHi = ReadLE64(input, len - 8);
        var m128 = Mult64To128(inputLo ^ inputHi ^ bitflipLow, _prime64_1);
        m128.Low += (ulong)(len - 1) << 54;
        inputHi ^= bitflipHigh;
        m128.High += inputHi + ((ulong)(uint)inputHi * (_prime32_2 - 1));
        m128.Low ^= BinaryPrimitives.ReverseEndianness(m128.High);

        var h128 = Mult64To128(m128.Low, _prime64_2);
        h128.High += m128.High * _prime64_2;
        return Make(Avalanche(h128.Low), Avalanche(h128.High));
    }

    // XXH3_len_17to128_128b
    private static UInt128 Len17To128(ReadOnlySpan<byte> input, ulong seed)
    {
        var len = input.Length;
        (ulong Low, ulong High) acc = ((ulong)len * _prime64_1, 0UL);
        if (len > 32)
        {
            if (len > 64)
            {
                if (len > 96)
                {
                    acc = Mix32B(acc, input, 48, input, len - 64, _secret, 96, seed);
                }

                acc = Mix32B(acc, input, 32, input, len - 48, _secret, 64, seed);
            }

            acc = Mix32B(acc, input, 16, input, len - 32, _secret, 32, seed);
        }

        acc = Mix32B(acc, input, 0, input, len - 16, _secret, 0, seed);
        return LenFinalize(acc, len, seed);
    }

    // XXH3_len_129to240_128b
    private static UInt128 Len129To240(ReadOnlySpan<byte> input, ulong seed)
    {
        var len = input.Length;
        var nbRounds = len / 32;
        (ulong Low, ulong High) acc = ((ulong)len * _prime64_1, 0UL);
        for (var i = 0; i < 4; i++)
        {
            acc = Mix32B(acc, input, 32 * i, input, (32 * i) + 16, _secret, 32 * i, seed);
        }

        acc.Low = Avalanche(acc.Low);
        acc.High = Avalanche(acc.High);
        for (var i = 4; i < nbRounds; i++)
        {
            acc = Mix32B(acc, input, 32 * i, input, (32 * i) + 16, _secret, _midsizeStartOffset + (32 * (i - 4)), seed);
        }

        // 末尾 32 字节：两个输入指针的顺序与循环里相反，且 seed 取反（0 - seed）。
        acc = Mix32B(acc, input, len - 16, input, len - 32, _secret, _secretSizeMin - 16 - _midsizeLastOffset, 0UL - seed);
        return LenFinalize(acc, len, seed);
    }

    private static UInt128 LenFinalize((ulong Low, ulong High) acc, int len, ulong seed)
    {
        var low = acc.Low + acc.High;
        var high = (acc.Low * _prime64_1) + (acc.High * _prime64_4) + (((ulong)len - seed) * _prime64_2);
        return Make(Avalanche(low), 0UL - Avalanche(high));
    }

    // ---- 64 位专用：rrmxmx（4..8 字节路径用） ----

    private static ulong Rrmxmx(ulong h64, ulong len)
    {
        h64 ^= RotateLeft(h64, 49) ^ RotateLeft(h64, 24);
        h64 *= _primeMx2;
        h64 ^= (h64 >> 35) + len;
        h64 *= _primeMx2;
        h64 ^= h64 >> 28;
        return h64;
    }

    private static ulong RotateLeft(ulong value, int shift) => (value << shift) | (value >> (64 - shift));
}
