// FsWatch 测试的共享辅助（对应 Go 的 testutil_test.go / watcher_test.go 里的 newTmpDir、makeDirSymlink）。
//
// 移植范围（`internal/fswatch/*_test.go`，共 3,085 + 若干行）：
//   - 已移植（确定性）：`eventlist_test.go`（全部 7 个）、`walkdir_test.go`（8 个，native/generic 各跑一遍）、
//     `fallback_test.go`（5 个）、`watcher_test.go` 里的纯逻辑用例（TestPathComparer / TestPathComparerUnicodeAlignment /
//     TestPathComparerExactKeys / TestRebasePath / TestIsInDirectoryOrSelf / TestFileCallbackCaseSensitivity /
//     TestSubscribeMissingDirError / TestSubscribeNotADirError / TestSubscribeRejectsNilCallback /
//     TestSubscribeRejectsRelativePath / TestSubscribeUnsubscribeIdempotent / TestWatchFileCannotWatchRoot /
//     TestDefaultBackendMatchesPlatform / TestUnavailableBackendReturnsError / TestBackendRunReturnsStartError /
//     TestDirWatchErrorImplementsError / TestFileCallbackForwardsErrAlongsideEvents）。
//   - **未移植**：`watcher_test.go` 里依赖"写文件 → 等事件到达"时序的用例（它们靠 time.Sleep + 事件投递，
//     属于 flaky 范畴；行为由 `artifacts/fswatch-probe` 的实时冒烟程序覆盖），以及 `testutil_test.go` 的
//     retryT/runWithRetry 框架（只为那些 flaky 用例的 macOS 重试而存在）。
//   - **未移植（平台专用，win32 上无法运行）**：`fanotify_linux_test.go`、`fsevents_darwin_nfd_test.go`、
//     `fsevents_darwin_shared_test.go`、`fsevents_darwin_ffi_arm64_test.go`。
//
// 说明：
// - Go 的 `t.Skip`/`t.Skipf` 在 xunit 2.x 里没有运行时等价物 → 这些辅助用**返回值**表达"环境不具备"，
//   调用方提前返回（在注释里标注对应的 Go 调用点）。
// - Go 的 `newTmpDir` 会 `filepath.EvalSymlinks` 把临时目录里的符号链接解析掉（macOS 的 /var → /private/var），
//   这样断言里的路径与 watcher 报的路径一致 → C# 用已移植的 `TypeScript.NativePath.NativePath.Realpath`。
using System.Diagnostics;

namespace TypeScript.Tests;

internal static class FsWatchTestSupport
{
    /// <summary>Go: <c>newTmpDir(t testingT)</c>（解析符号链接后的临时目录）。</summary>
    internal static string NewTmpDir() => NewTmpDir("fswatch-");

    internal static string NewTmpDir(string prefix)
    {
        var dir = Directory.CreateTempSubdirectory(prefix).FullName;
        return TypeScript.NativePath.NativePath.Realpath(dir) ?? dir;
    }

    /// <summary>
    /// Go: <c>makeDirSymlink(t, target, link)</c>。Windows 上先用 <c>cmd /c mklink /J</c>（junction，
    /// 不需要 SeCreateSymbolicLinkPrivilege），再退到 <c>Directory.CreateSymbolicLink</c>。
    /// 返回 false = Go 的 <c>t.Skipf("directory symlink support is not available")</c>。
    /// </summary>
    internal static bool TryMakeDirSymlink(string target, string link)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try
            {
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                if (p.ExitCode == 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // 落到下面的 CreateSymbolicLink。
            }
        }

        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
