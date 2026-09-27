// ported from: internal/debug/debug_test.go
using TypeScript;
using Xunit;

namespace TypeScript.Tests;

public class DebugTests
{
    private sealed class MockNode(string kind) : IKindString
    {
        public string KindString() => kind;
    }

    private sealed class MockStringer(string s)
    {
        public override string ToString() => s;
    }

    private static string AssertPanics(Action action)
    {
        var ex = Assert.Throws<DebugFailureException>(action);
        return ex.Message;
    }

    [Fact]
    public void FailEmptyReason()
        => Assert.Equal("Debug failure.", AssertPanics(() => Debug.Fail("")));

    [Fact]
    public void FailWithReason()
        => Assert.Equal("Debug failure. something went wrong", AssertPanics(() => Debug.Fail("something went wrong")));

    [Fact]
    public void FailBadSyntaxKindNoMessage()
        => Assert.Equal(
            "Debug failure. Unexpected node.\nNode FooNode was unexpected.",
            AssertPanics(() => Debug.FailBadSyntaxKind(new MockNode("FooNode"))));

    [Fact]
    public void FailBadSyntaxKindWithMessage()
        => Assert.Equal(
            "Debug failure. custom message\nNode BarNode was unexpected.",
            AssertPanics(() => Debug.FailBadSyntaxKind(new MockNode("BarNode"), "custom message")));

    [Fact]
    public void AssertNeverDefaultMessageKindString()
        => Assert.Equal("Debug failure. Illegal value: TestNode", AssertPanics(() => Debug.AssertNever(new MockNode("TestNode"))));

    [Fact]
    public void AssertNeverCustomMessageKindString()
        => Assert.Equal("Debug failure. bad value: TestNode", AssertPanics(() => Debug.AssertNever(new MockNode("TestNode"), "bad value:")));

    [Fact]
    public void AssertNeverStringer()
        => Assert.Equal("Debug failure. Illegal value: hello", AssertPanics(() => Debug.AssertNever(new MockStringer("hello"))));

    [Fact]
    public void AssertNeverFallback()
        => Assert.Equal("Debug failure. Illegal value: 42", AssertPanics(() => Debug.AssertNever(42)));

    [Fact]
    public void AssertNeverNil()
        => Assert.Equal("Debug failure. Illegal value: <nil>", AssertPanics(() => Debug.AssertNever(null)));

    [Fact]
    public void AssertNeverMultipleMessageParts()
        // fmt.Sprint 只在相邻两个操作数都不是 string 时插空格。
        => Assert.Equal("Debug failure. a1b 0", AssertPanics(() => Debug.AssertNever(0, "a", 1, "b")));

    [Fact]
    public void AssertNeverMultipleNonStringMessageParts()
        => Assert.Equal("Debug failure. 1 2 0", AssertPanics(() => Debug.AssertNever(0, 1, 2)));

    [Fact]
    public void AssertTrue()
        => Debug.Assert(true);

    [Fact]
    public void AssertTrueWithMessage()
        => Debug.Assert(true, "this should not trigger");

    [Fact]
    public void AssertFalseNoMessage()
        => Assert.Equal("Debug failure. False expression.", AssertPanics(() => Debug.Assert(false)));

    [Fact]
    public void AssertFalseWithMessage()
        => Assert.Equal("Debug failure. False expression: expected x > 0", AssertPanics(() => Debug.Assert(false, "expected x > 0")));
}
