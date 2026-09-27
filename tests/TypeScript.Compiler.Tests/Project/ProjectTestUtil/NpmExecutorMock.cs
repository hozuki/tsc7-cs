// ported from: internal/testutil/projecttestutil/npmexecutormock_generated.go:1-86
//
// Go 侧由 github.com/matryer/moq 生成（DO NOT EDIT）；C# 侧手工移植，结构与生成物逐行对应
// （XXXFunc 字段 + XXXCalls() 访问器）。文件名不以 `.Generated.cs` 结尾 ⇒ 本文件继承项目 nullable 上下文。
//
// 与 Go 的差异（逐条）：
//
// 1) `NpmExecutor.NpmInstall(cwd, args) ([]byte, error)` → `byte[] NpmInstall(string cwd, string[] args)`
//    （Project/Ata/Ata.cs 文件头第 1 条：错误按 §3.2 抛异常，byte[] 只在成功路径上存在）。所以这里的
//    Func 字段是 `Func<string, string[], byte[]?>`。Go 的 mock 在 Func 为 nil 时返回 `(nil, nil)`；
//    C# 的接口返回类型不可空，故 nil 分支返回**空数组**（`[]`）——Ata.cs 的成功路径丢弃返回值，
//    只有 NpmInstallCalls() 的计数/参数被测试断言，空数组与 nil 无可观察差别。
// 2) Go 的匿名结构体调用记录 → C# 值元组数组；访问器返回**拷贝**（Go 返回别名切片）。
// 3) Go 的 mock 把 `args []string` 按**切片别名**存进调用记录（调用方之后改这个切片会改到记录里）；
//    C# 这里克隆一份（值元组里存数组引用的话，行为与 Go 的别名一致；克隆更安全，且 Go 的调用点
//    （ata.go 的 npmArgs.ToArray()）每次都新建切片，无可观察差别）。
using TypeScript.Project.Ata;

namespace TypeScript.Tests.ProjectTestUtil;

/// <summary>Go: <c>projecttestutil.NpmExecutorMock</c>（moq 生成的 <c>ata.NpmExecutor</c> 实现）。</summary>
public sealed class NpmExecutorMock : NpmExecutor
{
    /// <summary>Go: <c>NpmInstallFunc</c>（见文件头第 1 条：返回空数组 = Go 的 nil）。</summary>
    public Func<string, string[], byte[]?>? NpmInstallFunc;

    private readonly Lock _callsLock = new();
    private readonly List<(string Cwd, string[] Args)> _npmInstallCalls = [];

    /// <summary>Go: <c>NpmExecutorMock.NpmInstall</c>。</summary>
    public byte[] NpmInstall(string cwd, string[] args)
    {
        lock (_callsLock)
        {
            _npmInstallCalls.Add((cwd, [.. args]));
        }

        if (NpmInstallFunc is null)
        {
            // Go: var bytesOut []byte; var errOut error; return bytesOut, errOut
            return [];
        }

        return NpmInstallFunc(cwd, args) ?? [];
    }

    /// <summary>Go: <c>NpmExecutorMock.NpmInstallCalls()</c>。</summary>
    public (string Cwd, string[] Args)[] NpmInstallCalls()
    {
        lock (_callsLock)
        {
            return _npmInstallCalls.ToArray();
        }
    }
}
