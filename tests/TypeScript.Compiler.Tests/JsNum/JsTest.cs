// ported from: internal/testutil/jstest/node.go（只移植 Node.js 交叉验证需要的部分）
//             + internal/jsnum/jsnum_test.go 里的公共测试助手（numberFromBits 等）
//
// Go 的 jstest.EvalNodeScript[T] 的做法：把 script.mjs + loader.mjs 写进临时目录，跑
// `node loader.mjs <args...>`，然后把 stdout 当 JSON 反序列化成 T（Go 用 encoding/json 的反射映射）。
// C# 侧：
//   * 找 node 用 ProcessStartInfo（PATH 查找由 .NET 完成，对应 Go 的 exec.LookPath）；找不到就
//     Assert.Skip（= Go 的 t.Skip("Node.js not found")）。
//   * 结果解析各写各的（JsonDocument 逐字段取值），不走 Json 包的反射映射——AOT 下不允许，而且
//     这里的结构只有 [2]uint32 与 string 两种字段。
//   * 临时目录用 Directory.CreateTempSubdirectory（= Go 的 t.TempDir()），用完删掉。
//
// Go 的 BenchmarkToInt32/BenchmarkExponentiate 与 FuzzStringJS/FuzzFromStringJS 的"真 fuzz"模式在
// xunit 里没有对应物：benchmark 未移植，fuzz 按 go test 默认的"只跑种子语料"语义移植为普通测试
// （见 StringTests.cs）。
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TypeScript.JsNum;
using Xunit;
using static TypeScript.Json.Json;

namespace TypeScript.Tests;

internal static class JsTest
{
    private const string _loaderScript = """
        import script from "./script.mjs";
        process.stdout.write(JSON.stringify(await script(...process.argv.slice(2))));
        """;

    private static readonly Lazy<bool> _nodeAvailable = new(static () =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    });

    // Go: SkipIfNoNodeJS(t) → t.Skip("Node.js not found")。
    //
    // C# 侧改成了显式失败：本仓库钉的 xunit 2.9.3 + xunit.runner.visualstudio 3.1.5 组合**不支持动态跳过**
    // （xunit 2.9 的 Xunit.Sdk.SkipException.ForSkip 在这里被报告成 FAIL，实测见报告），而"静默通过"
    // 会让没装 Node.js 的机器上这些交叉验证用例悄悄变成空跑。TSGO 自己的 CI 要求 Node.js，所以缺
    // Node.js 时明确失败、给出原因，比假通过更安全。
    internal static void SkipIfNoNodeJS()
    {
        if (!_nodeAvailable.Value)
        {
            Assert.Fail("Node.js not found (jsnum 的 Node.js 交叉验证用例需要 node 在 PATH 上)");
        }
    }

    internal static Number NumberFromBits(ulong bits)
    {
        return (Number)BitConverter.Int64BitsToDouble(unchecked((long)bits));
    }

    internal static ulong NumberToBits(Number number)
    {
        return unchecked((ulong)BitConverter.DoubleToInt64Bits((double)number));
    }

    internal static uint[] NumToUint32s(Number number)
    {
        var bits = NumberToBits(number);
        return [(uint)bits, (uint)(bits >> 32)];
    }

    internal static Number Uint32sToNum(uint[] bits)
    {
        var value = (ulong)bits[0] | (ulong)bits[1] << 32;
        return NumberFromBits(value);
    }

    // Go 的 assertEqualNumber：两边都 NaN 才算相等，其余按 IEEE 相等。
    internal static void AssertEqualNumber(Number got, Number want)
    {
        if (got.IsNaN() || want.IsNaN())
        {
            Assert.Equal(got.IsNaN(), want.IsNaN());
        }
        else
        {
            Assert.Equal(got, want);
        }
    }

    // Go 的 assertWithinOneULP：相等或在 1 ULP 之内。
    internal static void AssertWithinOneULP(Number got, Number want)
    {
        if (got.IsNaN() || want.IsNaN())
        {
            Assert.Equal(got.IsNaN(), want.IsNaN());
            return;
        }

        if (got == want)
        {
            return;
        }

        var gotBits = NumberToBits(got);
        var wantBits = NumberToBits(want);
        if (gotBits == wantBits)
        {
            return;
        }

        var ulpDistance = gotBits > wantBits ? gotBits - wantBits : wantBits - gotBits;
        Assert.True(
            ulpDistance <= 1,
            $"got {got} ({gotBits:x16}), want {want} ({wantBits:x16}) within 1 ULP (off by {ulpDistance} ULPs)");
    }

    // Go 的 evalBinaryOp：把 xs/ys 的位模式写进 input.json，交给 Node 求值，读回结果的位模式。
    internal static Number[] EvalBinaryOp(string op, Number[] xs, Number[] ys)
    {
        SkipIfNoNodeJS();
        Assert.Equal(xs.Length, ys.Length);

        var script = """
            import fs from 'fs';

            function fromBits(bits) {
            	const buffer = new ArrayBuffer(8);
            	(new Uint32Array(buffer))[0] = bits[0];
            	(new Uint32Array(buffer))[1] = bits[1];
            	return new Float64Array(buffer)[0];
            }

            function toBits(number) {
            	const buffer = new ArrayBuffer(8);
            	(new Float64Array(buffer))[0] = number;
            	return [(new Uint32Array(buffer))[0], (new Uint32Array(buffer))[1]];
            }

            export default function(inputFile) {
            	const input = JSON.parse(fs.readFileSync(inputFile, 'utf8'));
            	return input.map(({x, y}) => {
            		const a = fromBits(x);
            		const b = fromBits(y);
            		return { x, y, result: toBits(OP) };
            	});
            };
            """.Replace("OP", op);

        var input = new StringBuilder();
        input.Append('[');
        for (var i = 0; i < xs.Length; i++)
        {
            if (i > 0)
            {
                input.Append(',');
            }

            var x = NumToUint32s(xs[i]);
            var y = NumToUint32s(ys[i]);
            input.Append("{\"x\":[")
                .Append(x[0].ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(x[1].ToString(CultureInfo.InvariantCulture))
                .Append("],\"y\":[")
                .Append(y[0].ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(y[1].ToString(CultureInfo.InvariantCulture))
                .Append("]}");
        }

        input.Append(']');

        var results = RunWithInputFile(script, input.ToString(), xs.Length);
        return results;
    }

    // Go 的 evalUnaryOp。
    internal static Number[] EvalUnaryOp(string op, Number[] xs)
    {
        SkipIfNoNodeJS();

        var script = """
            import fs from 'fs';

            function fromBits(bits) {
            	const buffer = new ArrayBuffer(8);
            	(new Uint32Array(buffer))[0] = bits[0];
            	(new Uint32Array(buffer))[1] = bits[1];
            	return new Float64Array(buffer)[0];
            }

            function toBits(number) {
            	const buffer = new ArrayBuffer(8);
            	(new Float64Array(buffer))[0] = number;
            	return [(new Uint32Array(buffer))[0], (new Uint32Array(buffer))[1]];
            }

            export default function(inputFile) {
            	const input = JSON.parse(fs.readFileSync(inputFile, 'utf8'));
            	return input.map(({x}) => {
            		const a = fromBits(x);
            		return { x, result: toBits(OP) };
            	});
            };
            """.Replace("OP", op);

        var input = new StringBuilder();
        input.Append('[');
        for (var i = 0; i < xs.Length; i++)
        {
            if (i > 0)
            {
                input.Append(',');
            }

            var x = NumToUint32s(xs[i]);
            input.Append("{\"x\":[")
                .Append(x[0].ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(x[1].ToString(CultureInfo.InvariantCulture))
                .Append("]}");
        }

        input.Append(']');

        return RunWithInputFile(script, input.ToString(), xs.Length);
    }

    // Go 的 getStringResultsFromJS：返回 JS 侧对每个输入的 `"" + fromBits(bits)` 与 `+str` 的结果。
    internal static (Number Number, string Str)[] GetStringResultsFromJS((double Number, string Str)[] tests)
    {
        SkipIfNoNodeJS();

        const string script = """
            import fs from 'fs';

            function fromBits(bits) {
            	const buffer = new ArrayBuffer(8);
            	(new Uint32Array(buffer))[0] = bits[0];
            	(new Uint32Array(buffer))[1] = bits[1];
            	return new Float64Array(buffer)[0];
            }

            function toBits(number) {
            	const buffer = new ArrayBuffer(8);
            	(new Float64Array(buffer))[0] = number;
            	return [(new Uint32Array(buffer))[0], (new Uint32Array(buffer))[1]];
            }

            export default function(inputFile) {
            	const input = JSON.parse(fs.readFileSync(inputFile, 'utf8'));

            	const output = input.map((input) => ({
            		str: ""+fromBits(input.bits),
            		bits: toBits(+input.str),	
            	}));

            	return output;
            };
            """;

        var input = new StringBuilder();
        input.Append('[');
        for (var i = 0; i < tests.Length; i++)
        {
            if (i > 0)
            {
                input.Append(',');
            }

            var bits = NumToUint32s((Number)tests[i].Number);
            input.Append("{\"bits\":[")
                .Append(bits[0].ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(bits[1].ToString(CultureInfo.InvariantCulture))
                .Append("],\"str\":")
                .Append(JsonString(tests[i].Str))
                .Append('}');
        }

        input.Append(']');

        using var directory = new TempDirectory();
        var inputPath = Path.Combine(directory.Path, "input.json");
        File.WriteAllText(inputPath, input.ToString());
        var stdout = RunNodeScript(script, directory.Path, inputPath);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal(tests.Length, root.GetArrayLength());

        var output = new (Number Number, string Str)[tests.Length];
        var index = 0;
        foreach (var element in root.EnumerateArray())
        {
            var bitsElement = element.GetProperty("bits");
            var number = Uint32sToNum([bitsElement[0].GetUInt32(), bitsElement[1].GetUInt32()]);
            output[index++] = (number, element.GetProperty("str").GetString() ?? "");
        }

        return output;
    }

    // Go 用 encoding/json 序列化输入；C# 侧只需要把字符串写成 JSON 字面量，交给 Json 包（同一套转义规则）。
    private static string JsonString(string value)
    {
        return Encoding.UTF8.GetString(Marshal(value));
    }

    private static Number[] RunWithInputFile(string script, string input, int expectedCount)
    {
        using var directory = new TempDirectory();
        var inputPath = Path.Combine(directory.Path, "input.json");
        File.WriteAllText(inputPath, input);
        var stdout = RunNodeScript(script, directory.Path, inputPath);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal(expectedCount, root.GetArrayLength());

        var output = new Number[expectedCount];
        var index = 0;
        foreach (var element in root.EnumerateArray())
        {
            var result = element.GetProperty("result");
            output[index++] = Uint32sToNum([result[0].GetUInt32(), result[1].GetUInt32()]);
        }

        return output;
    }

    private static string RunNodeScript(string script, string directory, params string[] args)
    {
        File.WriteAllText(Path.Combine(directory, "script.mjs"), script);
        File.WriteAllText(Path.Combine(directory, "loader.mjs"), _loaderScript);

        var startInfo = new ProcessStartInfo("node")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(directory, "loader.mjs"));
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("failed to start node");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"failed to run node: exit code {process.ExitCode}\n{stderr}");
        }

        return stdout;
    }

    // Go 的 t.TempDir()：目录存在期间有效，Dispose 时递归删除。
    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Path = Directory.CreateTempSubdirectory("jsnum-").FullName;
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // 与 t.TempDir() 一样，清理失败不影响测试结果。
            }
        }
    }
}
