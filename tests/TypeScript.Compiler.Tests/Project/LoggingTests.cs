// 对应 internal/project/logging/ 的两个 Go 测试文件：
//   - logging/logtree_test.go 在 Go 里是**空壳**（`var _ testLogger = &LogTree{}` 是编译期接口断言 +
//     空的 TestLogTree），C# 里接口一致性由 `LogTree : LogCollector` 声明本身保证，所以这里改测 LogTree
//     的**可观察行为**（渲染结构、缩进、Embed、verbose 传播）—— 这些正是 SDK/CLI 基线消费的部分。
//   - logging 包的 Logger/LogCollector 没有 Go 测试；这里覆盖它们的确定性行为（前缀、分隔符、
//     fmt.Sprint 的空格规则、NopLogger 的"带类型 nil"语义）。
//
// 时间戳的处理：LogTree 用 DateTime.Now 打点，无法断言具体值；测试把 `[HH:mm:ss.fff]` 归一化成 `[T]`
// 后比对整段文本（结构、顺序、缩进因此被完整钉住）。NewTestLogger 用固定时刻，故可以断言前缀形状。
using System.Globalization;
using System.Text.RegularExpressions;
using TypeScript.Project.Logging;
using Xunit;
using static TypeScript.Project.Logging.Logging;

namespace TypeScript.Tests;

public class ProjectLoggingTests
{
    private static string NormalizeTimestamps(string text)
    {
        return Regex.Replace(text, @"\[\d{2}:\d{2}:\d{2}\.\d{3}\]", "[T]");
    }

    [Fact]
    public void TestLogTreeStringRendersHeaderEntriesAndIndentation()
    {
        var tree = NewLogTree("root");
        tree.Logf("hello {0}", 1);
        var child = tree.Fork("child");
        child.Log("nested");
        var embedded = NewLogTree("embedded");
        embedded.Log("inner");
        tree.Embed(embedded);

        Assert.Equal(
            "======== root ========\n"
            + "[T] hello 1\n"
            + "[T] child\n"
            + "\t[T] nested\n"
            + "[T] embedded\n"
            + "\t[T] inner\n",
            NormalizeTimestamps(tree.String()));
    }

    [Fact]
    public void TestLogTreeVerbosePropagatesToForks()
    {
        var tree = NewLogTree("root");
        Assert.False(tree.IsVerbose());
        Assert.Null(tree.Verbose());

        tree.SetVerbose(true);
        Assert.True(tree.IsVerbose());
        // Go 的 Verbose() 返回接收者本身（这里是根）。
        Assert.Same(tree, tree.Verbose());

        var child = tree.Fork("child");
        Assert.True(child.IsVerbose());
        Assert.Same(child, child.Verbose());
    }

    [Fact]
    public void TestLogTreeStringOnlyOnRoot()
    {
        var tree = NewLogTree("root");
        var child = tree.Fork("child");
        // Go: panic("can only call String on root LogTree") → Debug.Fail（抛 DebugFailureException）。
        Assert.Throws<DebugFailureException>(() => child.String());
    }

    [Fact]
    public void TestTestLoggerUsesFixedTimePrefix()
    {
        var collector = NewTestLogger();
        collector.Logf("fixed {0}", 42);

        // Go: formatTime(time.Unix(1349085672, 0)) → 本地时区渲染的 [HH:mm:ss.fff]。
        var local = DateTimeOffset.FromUnixTimeSeconds(1349085672).ToLocalTime();
        var prefix = "[" + local.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "]";
        Assert.Equal(prefix + " fixed 42\n", collector.String());

        // 前缀固定：再来一条，时间戳不变（Go 的 NewTestLogger 就是这么设计的）。
        // 注意 Log 用的是 fmt.Fprintln 的规则：前缀与单个字符串之间**不**插空格（Logf 才插）。
        collector.Log("second");
        Assert.Equal(prefix + " fixed 42\n" + prefix + "second\n", collector.String());
    }

    [Fact]
    public void TestLoggerLogUsesFmtSprintSpacingRule()
    {
        var sw = new StringWriter();
        var logger = NewLogger(sw);
        logger.Log("a");
        logger.Log("a", "b");
        logger.Log(1, 2);

        // 时间戳用 DateTime.Now（跨毫秒会让三次调用拿到不同前缀），故把 `[HH:mm:ss.fff]` 归一化后再比对：
        // 这条测试要钉的是 **fmt.Sprint 的空格规则**（相邻两个操作数都不是 string 才插空格），不是时间戳。
        var normalized = Regex.Replace(sw.ToString(), @"\[\d{2}:\d{2}:\d{2}\.\d{3}\]", "[T]");
        Assert.Equal("[T]a\n[T]ab\n[T]1 2\n", normalized);
    }

    [Fact]
    public void TestNopLoggerIsNoOp()
    {
        var logger = NewNopLogger();
        logger.Log("discarded");
        logger.Logf("discarded {0}", 1);
        logger.Error("e");
        logger.Warn("w");
        logger.Info("i");

        // Go 的 NewNopLogger 返回带类型的 nil：所有方法直接 return，SetVerbose 也无效。
        logger.SetVerbose(true);
        Assert.False(logger.IsVerbose());
        Assert.Null(logger.Verbose());
    }
}
