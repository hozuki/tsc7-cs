// ported from: internal/vfs/vfsmock/wrapper_test.go
//
// **无法逐字移植的断言**：Go 的 TestWrap 用 `reflect` 遍历 `Wrap` 返回值的所有导出字段，断言每个字段
// 非零（`!fieldValue.IsZero()`），从而在"给 FSMock 加了新导出字段却忘了在 Wrap 里赋值"时失败。
// C# 测试工程启用了 AOT 分析器（禁止反射/dynamic），且 FSMock 的字段是公开字段而非属性，所以这里改成
// 显式列出 11 个 `XxxFunc` 字段逐个 `Assert.NotNull`。
// 代价：本测试不再具备 Go 版的"守住未来新增字段"能力 —— 新增导出字段时它不会自动失败（需要同步手工补断言）。
using TypeScript.Vfs.VfsMock;
using TypeScript.Vfs.VfsTest;
using Xunit;

namespace TypeScript.Tests;

public class VfsMockTests
{
    // Go: Wrap(vfstest.FromMap(map[string]string{}, true))
    [Fact]
    public void TestWrap()
    {
        var wrapper = VfsMock.Wrap(VfsTest.FromMap(new Dictionary<string, string>(), true));

        // Go: 反射遍历所有导出字段断言非零（见文件头：C# 侧退化成显式列举）。
        Assert.NotNull(wrapper.AppendFileFunc);
        Assert.NotNull(wrapper.ChtimesFunc);
        Assert.NotNull(wrapper.DirectoryExistsFunc);
        Assert.NotNull(wrapper.FileExistsFunc);
        Assert.NotNull(wrapper.GetAccessibleEntriesFunc);
        Assert.NotNull(wrapper.ReadFileFunc);
        Assert.NotNull(wrapper.RealpathFunc);
        Assert.NotNull(wrapper.RemoveFunc);
        Assert.NotNull(wrapper.StatFunc);
        Assert.NotNull(wrapper.UseCaseSensitiveFileNamesFunc);
        Assert.NotNull(wrapper.WriteFileFunc);
    }
}
