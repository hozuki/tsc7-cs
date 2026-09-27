// ported from: tsc/internal/ls/change/trackerimpl_test.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `lsproto.Position`/`lsproto.Range`/`lsproto.TextEdit` 是**值类型**（可用 == 比较、`Range{Start: p, End: p}`
//    是两份拷贝）；C# 侧都是 class（生成物），所以每个字段各 new 一个实例，避免 Start/End 共享同一个可变对象。
// 2) `TypeScript.LsProto.Range` 与 implicit usings 的 `System.Range` 撞名 → 异名别名（同 LsProto/LspTests.cs）。
// 3) Go 的未导出函数 `textEditsConflict` → `Tracker.TextEditsConflict`（`internal static`，测试程序集由
//    `src/TypeScript.Compiler/Properties/AssemblyInfo.cs` 的 InternalsVisibleTo 访问）。
// 4) Go 的 `t.Parallel()` 没有对应物（xunit 默认按 collection 并行）。
using TypeScript.LsChange;
using Xunit;
using Position = TypeScript.LsProto.Position;
using Range = TypeScript.LsProto.Range;
using TextEdit = TypeScript.LsProto.TextEdit;

namespace TypeScript.Tests;

/// <summary>Go: <c>package change</c> 的 <c>trackerimpl_test.go</c>。类名按仓内约定 <c>&lt;Package&gt;&lt;File&gt;Tests</c>
/// （同 <c>CollectionsOrderedMapTests</c>/<c>ConvertersTests</c>），包名取 C# 侧的 <c>LsChange</c>。</summary>
public class LsChangeTrackerImplTests
{
    [Fact]
    public void TextEditsConflictAtSameInsertionPointAcrossProjections()
    {
        var position = new Position { Line = 1, Character = 2 };
        var a = new TextEdit
        {
            Range = new Range { Start = position, End = new Position { Line = 1, Character = 2 } },
            NewText = "a",
        };
        var b = new TextEdit
        {
            Range = new Range { Start = new Position { Line = 1, Character = 2 }, End = new Position { Line = 1, Character = 2 } },
            NewText = "b",
        };

        Assert.True(Tracker.TextEditsConflict(a, b, true));
        Assert.False(Tracker.TextEditsConflict(a, b, false));
    }
}
