// ported from: internal/module/resolver_test.go
//
// 与 Go 测试的偏离（都不改变被测行为）：
// - Go 的 `vfstest.FromMap` → C# 的 `TypeScript.Vfs.VfsTest.VfsTest.FromMap`（Vfs 切片提供，同一份内存 FS）。
// - Go 的 goroutine + channel 同步 → C# 的 `Task.Run` + `SemaphoreSlim`/`ManualResetEventSlim`（超时即失败），
//   竞态用例的时序与 Go 逐条对应。
using System.Collections.Concurrent;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.Vfs.VfsTest;
using Xunit;
using static TypeScript.Module.Module;

namespace TypeScript.Tests;

public class ModuleResolverTests
{
    // Regression test for https://github.com/microsoft/TypeScript/tsc/issues/3526.
    //
    // Resolving a node_modules import with a trailing slash (e.g. `pkg/`) must
    // produce the same result as without one.
    [Fact]
    public void TestResolveModuleNameTrailingSlash()
    {
        var fs = VfsTest.FromMap(new Dictionary<string, string>
        {
            ["/repo/node_modules/pkg/package.json"] = """{"name":"pkg","main":"main.js","types":"main.d.ts"}""",
            ["/repo/node_modules/pkg/main.d.ts"] = "export const x: number;",
            ["/repo/node_modules/pkg/main.js"] = "exports.x = 1;",
            ["/repo/src/file.ts"] = "",
        }, true);
        var host = new ResolutionHostStub(fs, "/repo");
        var options = new CompilerOptions
        {
            ModuleResolution = ModuleResolutionKind.Bundler,
            Module = ModuleKind.ESNext,
            Target = ScriptTarget.ESNext,
        };
        var resolver = NewResolver(new ResolverOptions { Host = host, CompilerOptions = options });

        foreach (var name in new[] { "pkg", "pkg/" })
        {
            var (result, _) = resolver.ResolveModuleName(name, "/repo/src/file.ts", ModuleKind.ESNext, null);
            Assert.True(result is not null && result.IsResolved(), $"\"{name}\" failed to resolve");
        }
    }

    // Regression test for https://github.com/microsoft/TypeScript/tsc/issues/3526.
    //
    // Two threads resolve the same package via specifiers that differ only by a
    // trailing slash (`pkg` and `pkg/`). A blocking FS holds both at the
    // `FileExists` check for `package.json` - *after* each has confirmed a
    // `package.json` info-cache miss but *before* either has called `Set`. When
    // released, both proceed to `LoadOrStore` and one of them loses. Without the
    // fix, the loser receives the winner's `InfoCacheEntry` whose
    // `PackageDirectory` doesn't match its own `candidate` (because one spelling
    // has a trailing slash and the other doesn't), and
    // `loadNodeModuleFromDirectoryWorker`'s `ComparePaths` check skips loading
    // the package's `main`/`types`. With no `index.*` present, resolution falls
    // through to "unresolved" - the phantom TS2307 the issue describes. This
    // test deterministically fails when the fix is reverted.
    [Fact]
    public async Task TestResolveModuleNameTrailingSlashRace()
    {
        const string packageJsonPath = "/repo/node_modules/pkg/package.json";
        var files = new Dictionary<string, string>
        {
            // `types` points at a file that is not discoverable through any
            // fallback path: there is no `index.*` and no `main`. The only way
            // to resolve `pkg` (or `pkg/`) is via the package.json `types` field
            // inside `loadNodeModuleFromDirectoryWorker`, which is exactly the
            // step that the bug skips when `candidate` and
            // `packageInfo.PackageDirectory` mismatch.
            [packageJsonPath] = """{"name":"pkg","types":"./typings/index.d.ts"}""",
            ["/repo/node_modules/pkg/typings/index.d.ts"] = "export const x: number;",
            // Distinct containing files so each `ResolveModuleName` call has a
            // unique module-resolution-cache key.
            ["/repo/src/a/file.ts"] = "",
            ["/repo/src/b/file.ts"] = "",
        };
        var fs = new BlockingFileSystem(VfsTest.FromMap(files, true), packageJsonPath);
        var host = new ResolutionHostStub(fs, "/repo");
        var options = new CompilerOptions
        {
            ModuleResolution = ModuleResolutionKind.Bundler,
            Module = ModuleKind.ESNext,
            Target = ScriptTarget.ESNext,
        };
        var resolver = NewResolver(new ResolverOptions { Host = host, CompilerOptions = options });

        var results = new ConcurrentDictionary<string, bool>();
        var tasks = new[] { "pkg", "pkg/" }.Select(name =>
        {
            var containingFile = name.EndsWith('/') ? "/repo/src/b/file.ts" : "/repo/src/a/file.ts";
            return Task.Run(() =>
            {
                var (result, _) = resolver.ResolveModuleName(name, containingFile, ModuleKind.ESNext, null);
                results[containingFile] = result is not null && result.IsResolved();
            });
        }).ToArray();

        // Wait for both threads to reach the FileExists gate, guaranteeing
        // both have observed a package.json info-cache miss.
        Assert.True(fs.Arrived.Wait(FileSystemTestGates.Timeout), "first FileExists gate arrival");
        Assert.True(fs.Arrived.Wait(FileSystemTestGates.Timeout), "second FileExists gate arrival");
        fs.ReleaseGate();

        await Task.WhenAll(tasks).WaitAsync(FileSystemTestGates.Timeout);
        foreach (var (containingFile, resolved) in results)
        {
            Assert.True(resolved, $"\"{containingFile}\" failed to resolve");
        }

        Assert.Equal(2, results.Count);
    }

    // Regression test for https://github.com/microsoft/TypeScript/tsc/issues/1290.
    //
    // Two threads resolve `pkg/sub` concurrently. Both miss the package.json
    // info-cache for the root package directory. A `FlipFileExistsFileSystem`
    // forces the first thread's `FileExists` to return false (simulating the file
    // not yet being visible), so it stores a nil-Contents cache entry. The second
    // thread's `FileExists` returns true, but its `Set` call (`LoadOrStore`)
    // returns the first thread's nil-Contents entry. Without the `Exists()` guard
    // on the `typesVersions` lookup, `packageInfo.Contents.GetVersionPaths`
    // dereferences nil and panics. With the guard the nil-Contents entry is
    // safely skipped.
    [Fact]
    public async Task TestResolveSubpathNilContentsRace()
    {
        const string rootPackageJson = "/repo/node_modules/pkg/package.json";
        var files = new Dictionary<string, string>
        {
            [rootPackageJson] = """{"name":"pkg","version":"1.0.0"}""",
            ["/repo/node_modules/pkg/sub/index.d.ts"] = "export declare const sub: number;",
            ["/repo/node_modules/pkg/sub/index.js"] = "exports.sub = 1;",
            ["/repo/src/a/file.ts"] = "",
            ["/repo/src/b/file.ts"] = "",
        };
        var fs = new FlipFileExistsFileSystem(VfsTest.FromMap(files, true), rootPackageJson);
        var host = new ResolutionHostStub(fs, "/repo");
        var options = new CompilerOptions
        {
            ModuleResolution = ModuleResolutionKind.Bundler,
            Module = ModuleKind.ESNext,
            Target = ScriptTarget.ESNext,
        };
        var resolver = NewResolver(new ResolverOptions { Host = host, CompilerOptions = options });

        var results = new ConcurrentDictionary<string, bool>();
        var tasks = new[] { "/repo/src/a/file.ts", "/repo/src/b/file.ts" }.Select(containingFile => Task.Run(() =>
        {
            var (result, _) = resolver.ResolveModuleName("pkg/sub", containingFile, ModuleKind.ESNext, null);
            results[containingFile] = result is not null && result.IsResolved();
        })).ToArray();

        // Phase 1: Wait for both threads to reach FileExists for the root
        // package.json, guaranteeing both have observed a cache miss.
        Assert.True(fs.FirstArrived.Wait(FileSystemTestGates.Timeout), "first root package.json FileExists arrival");
        Assert.True(fs.SecondArrived.Wait(FileSystemTestGates.Timeout), "second root package.json FileExists arrival");

        // Phase 2: Release the first FileExists caller (returns false).
        // It enters the "file not found" branch and stores a nil-Contents entry
        // via Set - this is nearly instant (no ReadFile).
        fs.ReleaseFirstGate();

        // Phase 3: Release the second FileExists caller (returns true).
        // It proceeds to ReadFile, which we gate separately to ensure the first
        // thread's nil-Contents Set has completed.
        fs.ReleaseSecondGate();

        // Phase 4: Wait for the second thread to reach ReadFile, then release.
        // By this point the first thread has stored its nil-Contents entry.
        // The second thread's Set (LoadOrStore) will return that stale entry.
        Assert.True(fs.ReadArrived.Wait(FileSystemTestGates.Timeout), "root package.json ReadFile arrival");
        fs.ReleaseReadGate();

        await Task.WhenAll(tasks).WaitAsync(FileSystemTestGates.Timeout);
        foreach (var (containingFile, resolved) in results)
        {
            Assert.True(resolved, $"\"{containingFile}\" failed to resolve pkg/sub");
        }

        Assert.Equal(2, results.Count);
    }

    [Theory]
    [InlineData("/a/node_modules/b/lib/index.d.ts", false, "/a/node_modules/b")]
    [InlineData("/a/node_modules/@scope/b/lib/index.d.ts", false, "/a/node_modules/@scope/b")]
    [InlineData("/a/node_modules/b/lib/File", true, "/a/node_modules/b")]
    [InlineData("/a/node_modules/@scope/b/lib/File", true, "/a/node_modules/@scope/b")]
    [InlineData("/a/node_modules/b", true, "/a/node_modules/b")]
    [InlineData("/a/node_modules/@scope/b", true, "/a/node_modules/@scope/b")]
    // A bare scope directory has no package name; must not panic (https://github.com/microsoft/TypeScript/tsc/issues/4373).
    [InlineData("/a/node_modules/@scope", true, "/a/node_modules/@scope")]
    [InlineData("/a/node_modules/@types", true, "/a/node_modules/@types")]
    [InlineData("/a/src/index.ts", false, "")]
    public void TestParseNodeModuleFromPath(string path, bool isFolder, string want)
    {
        Assert.Equal(want, ParseNodeModuleFromPath(path, isFolder));
    }

    // Regression test for https://github.com/microsoft/TypeScript/tsc/issues/4478.
    //
    // While resolving a package with peerDependencies, two threads look up the
    // peer package's package.json concurrently. A `FlipFileExistsFileSystem`
    // forces the first lookup to cache a nil-Contents entry and the second lookup
    // to receive that stale entry from `Set`. The resolver must not dereference
    // the peer package.json contents unless the entry actually exists.
    [Fact]
    public async Task TestResolvePeerDependencyNilContentsRace()
    {
        const string peerPackageJson = "/repo/node_modules/peer/package.json";
        var files = new Dictionary<string, string>
        {
            ["/repo/node_modules/pkg/package.json"] = """{"name":"pkg","version":"1.0.0","types":"index.d.ts","peerDependencies":{"peer":"*"}}""",
            ["/repo/node_modules/pkg/index.d.ts"] = "export declare const x: number;",
            [peerPackageJson] = """{"name":"peer","version":"2.0.0"}""",
            ["/repo/src/a/file.ts"] = "",
            ["/repo/src/b/file.ts"] = "",
        };
        var fs = new FlipFileExistsFileSystem(VfsTest.FromMap(files, true), peerPackageJson);
        var host = new ResolutionHostStub(fs, "/repo");
        var options = new CompilerOptions
        {
            ModuleResolution = ModuleResolutionKind.Bundler,
            Module = ModuleKind.ESNext,
            Target = ScriptTarget.ESNext,
        };
        var resolver = NewResolver(new ResolverOptions { Host = host, CompilerOptions = options });

        var results = new ConcurrentDictionary<string, bool>();
        var firstCompleted = new SemaphoreSlim(0);
        var tasks = new[] { "/repo/src/a/file.ts", "/repo/src/b/file.ts" }.Select(containingFile => Task.Run(() =>
        {
            var (result, _) = resolver.ResolveModuleName("pkg", containingFile, ModuleKind.ESNext, null);
            results[containingFile] = result is not null && result.IsResolved();
            firstCompleted.Release();
        })).ToArray();

        Assert.True(fs.FirstArrived.Wait(FileSystemTestGates.Timeout), "first peer package.json FileExists arrival");
        Assert.True(fs.SecondArrived.Wait(FileSystemTestGates.Timeout), "second peer package.json FileExists arrival");
        fs.ReleaseFirstGate();

        // The first lookup must complete (and store its nil-Contents entry) before
        // the second one proceeds, so wait until a result is in.
        Assert.True(firstCompleted.Wait(FileSystemTestGates.Timeout), "first peer package.json lookup to finish");

        fs.ReleaseSecondGate();
        Assert.True(fs.ReadArrived.Wait(FileSystemTestGates.Timeout), "peer package.json ReadFile arrival");
        fs.ReleaseReadGate();

        await Task.WhenAll(tasks).WaitAsync(FileSystemTestGates.Timeout);
        foreach (var (containingFile, resolved) in results)
        {
            Assert.True(resolved, $"\"{containingFile}\" failed to resolve pkg");
        }

        Assert.Equal(2, results.Count);
    }
}
