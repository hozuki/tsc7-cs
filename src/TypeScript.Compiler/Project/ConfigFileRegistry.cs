// ported from: internal/project/configfileregistry.go:1-245
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **包级函数落点**（本仓特有，见 Project.cs 文件头第 1 条）：Go 的 `project` 包名与类型 `Project` 同名
//    ⇒ 不能建 `static class Project`。本文件的两个包级未导出函数挂到"最相关类型"上：
//      - `collectConfiguredContentMappers(commandLines)` → `ConfigFileRegistry.CollectConfiguredContentMappers(...)`
//        （它只被 `ConfigFileRegistry.contentMappers` 与 builder 的 contentMappers 调用）；
//      - `newConfigFileEntry(...)` / `newExtendedConfigFileEntry(...)` → `ConfigFileEntry.NewConfigFileEntry(...)` /
//        `ConfigFileEntry.NewExtendedConfigFileEntry(...)`（先例：`Locale.Parse`/`Locale.WithLocale`）。
//
// 2) **未导出类型按 C# 习惯命名**：`configuredContentMappers` → `internal sealed class ConfiguredContentMappers`；
//    `configFileEntry` → `internal sealed class ConfigFileEntry`；`configFileNames` → `internal sealed class ConfigFileNames`。
//    未导出字段一律 `internal _camelCase`（跨文件使用；Go 的未导出可见性在包内 == C# 的 internal）。
//    导出类型 `ConfigFileRegistry` / `TestConfigEntry` / `TestConfigFileNamesEntry` 保持 public。
//
// 3) **`map[K]struct{}` 集合 → `Collections.Set<T>`**（本仓既有集合类型）。Go 的 nil map 读等价于空集合，
//    但字段**保持可空**：Go 里 `if config.retainingProjects == nil { … = make(…) }` 的分支逐字保留，
//    由调用点（ConfigFileRegistryBuilder）创建。
//
// 4) `maps.Clone(m)`（nil → nil）→ `CloneMap`（本文件私有静态；浅拷贝等价于 `new Dictionary<K,V>(m)`）。
//
// 5) `iter.Seq[T]`（`TestConfigEntry.RetainingProjects` 等）→ `IEnumerable<T>`：Go 的 `maps.Keys` 是惰性视图，
//    C# 里直接把底层 `HashSet<T>` 作为 `IEnumerable<T>` 暴露（同样是惰性视图，只读遍历可观察等价）。
//
// 6) `slices.Sort(extensions)` → `Array.Sort(extensions, StringComparer.Ordinal)`：Go 的字符串排序是逐字节序，
//    对内容 mapper 的 ASCII 扩展名与 Ordinal 相同。
//
// 7) `mapper.Definition.Extensions` → `mapper.Extensions`：C# 里 `Mapper : Definition` 是真正的继承
//    （Go 是嵌入字段的提升），成员名相同。
//
// 8) `for path, entry := range c.configs` → `foreach (var (path, entry) in c._configs)`（元组解构）。
//
// 9) Go 的 nil 接收者分支（`if c != nil`）在 C# 的实例方法里不可达，不保留（同 Logging/LogTree.cs 文件头第 1 条）。
namespace TypeScript.Project;

using System;
using System.Collections.Generic;
using Path = TypeScript.TSPath.Path;

/// <summary>Go 的 <c>ConfigFileRegistry</c>（configfileregistry.go:16-27）。</summary>
public sealed class ConfigFileRegistry
{
    /// <summary>configs is a map of config file paths to their entries。</summary>
    internal Dictionary<Path, ConfigFileEntry> _configs = [];

    /// <summary>
    /// configFileNames is a map of open file paths to information about their ancestor config file names.
    /// It is only used as a cache during（Go 的注释如此，原文未写完）。
    /// </summary>
    internal Dictionary<Path, ConfigFileNames> _configFileNames = [];

    /// <summary>
    /// customConfigFileName is the custom config file name preference that was
    /// used when building this registry's configFileNames cache.
    /// </summary>
    internal string _customConfigFileName = "";

    internal ConfiguredContentMappers? _allConfiguredContentMappers;

    /// <summary>Go: <c>func (c *ConfigFileRegistry) contentMappers() *configuredContentMappers</c>（configfileregistry.go:46-57）。</summary>
    internal ConfiguredContentMappers ContentMappers()
    {
        if (_allConfiguredContentMappers is not null)
        {
            return _allConfiguredContentMappers;
        }

        var commandLines = new List<TsOptions.ParsedCommandLine>();
        foreach (var entry in _configs.Values)
        {
            if (entry._commandLine is not null)
            {
                commandLines.Add(entry._commandLine);
            }
        }

        return CollectConfiguredContentMappers(commandLines.ToArray());
    }

    /// <summary>
    /// Go: <c>func collectConfiguredContentMappers(commandLines []*tsoptions.ParsedCommandLine) *configuredContentMappers</c>
    /// （configfileregistry.go:33-44；见文件头第 1 条）。
    /// </summary>
    internal static ConfiguredContentMappers CollectConfiguredContentMappers(TsOptions.ParsedCommandLine[] commandLines)
    {
        var seenExtensions = new Collections.Set<string>();
        var extensions = new List<string>();
        foreach (var commandLine in commandLines)
        {
            foreach (var mapper in commandLine.ContentMappers() ?? [])
            {
                foreach (var extension in mapper.Extensions)
                {
                    if (seenExtensions.AddIfAbsent(extension))
                    {
                        extensions.Add(extension);
                    }
                }
            }
        }

        var result = extensions.ToArray();
        Array.Sort(result, StringComparer.Ordinal);
        return new ConfiguredContentMappers { _extensions = result };
    }

    /// <summary>Go: <c>func (c *ConfigFileRegistry) GetConfig(path tspath.Path) *tsoptions.ParsedCommandLine</c>。</summary>
    public TsOptions.ParsedCommandLine? GetConfig(Path path)
    {
        if (_configs.TryGetValue(path, out var entry))
        {
            return entry._commandLine;
        }

        return null;
    }

    /// <summary>Go: <c>func (c *ConfigFileRegistry) isTracked(path tspath.Path) bool</c>。</summary>
    internal bool IsTracked(Path path)
    {
        return _configs.ContainsKey(path);
    }

    /// <summary>Go: <c>func (c *ConfigFileRegistry) GetConfigFileName(path tspath.Path) string</c>。</summary>
    public string GetConfigFileName(Path path)
    {
        if (_configFileNames.TryGetValue(path, out var entry))
        {
            return entry._nearestConfigFileName;
        }

        return "";
    }

    /// <summary>Go: <c>func (c *ConfigFileRegistry) GetAncestorConfigFileName(path tspath.Path, higherThanConfig string) string</c>。</summary>
    public string GetAncestorConfigFileName(Path path, string higherThanConfig)
    {
        if (_configFileNames.TryGetValue(path, out var entry) && entry._ancestors is not null
            && entry._ancestors.TryGetValue(higherThanConfig, out var ancestor))
        {
            return ancestor;
        }

        return "";
    }

    /// <summary>clone creates a shallow copy of the configFileRegistry（Go 的未导出 <c>clone</c>）。</summary>
    internal ConfigFileRegistry Clone()
    {
        return new ConfigFileRegistry
        {
            _configs = CloneMap(_configs),
            _configFileNames = CloneMap(_configFileNames),
            _customConfigFileName = _customConfigFileName,
            _allConfiguredContentMappers = _allConfiguredContentMappers,
        };
    }

    /// <summary>Go: <c>maps.Clone(m)</c>（调用点的 map 恒非 nil，见文件头第 4 条）。</summary>
    private static Dictionary<K, V> CloneMap<K, V>(Dictionary<K, V> map) where K : notnull
    {
        return new Dictionary<K, V>(map);
    }

    // For testing

    /// <summary>Go: <c>func (c *ConfigFileRegistry) ForEachTestConfigEntry(cb func(tspath.Path, *TestConfigEntry))</c>。</summary>
    public void ForEachTestConfigEntry(Action<Path, TestConfigEntry> cb)
    {
        foreach (var (path, entry) in _configs)
        {
            cb(path, TestConfigEntry.FromEntry(entry));
        }
    }

    /// <summary>Go: <c>func (c *ConfigFileRegistry) GetTestConfigEntry(path tspath.Path) *TestConfigEntry</c>。</summary>
    public TestConfigEntry? GetTestConfigEntry(Path path)
    {
        return _configs.TryGetValue(path, out var entry) ? TestConfigEntry.FromEntry(entry) : null;
    }

    /// <summary>Go: <c>func (c *ConfigFileRegistry) ForEachTestConfigFileNamesEntry(cb func(tspath.Path, *TestConfigFileNamesEntry))</c>。</summary>
    public void ForEachTestConfigFileNamesEntry(Action<Path, TestConfigFileNamesEntry> cb)
    {
        foreach (var (path, entry) in _configFileNames)
        {
            cb(path, new TestConfigFileNamesEntry
            {
                NearestConfigFileName = entry._nearestConfigFileName,
                Ancestors = entry._ancestors,
            });
        }
    }

    /// <summary>Go: <c>func (c *ConfigFileRegistry) GetTestConfigFileNamesEntry(path tspath.Path) *TestConfigFileNamesEntry</c>。</summary>
    public TestConfigFileNamesEntry? GetTestConfigFileNamesEntry(Path path)
    {
        if (_configFileNames.TryGetValue(path, out var entry))
        {
            return new TestConfigFileNamesEntry
            {
                NearestConfigFileName = entry._nearestConfigFileName,
                Ancestors = entry._ancestors,
            };
        }

        return null;
    }
}

/// <summary>Go 的未导出 <c>type configuredContentMappers struct</c>（configfileregistry.go:29-31）。</summary>
internal sealed class ConfiguredContentMappers
{
    internal string[] _extensions = [];
}

/// <summary>Go 的未导出 <c>type configFileEntry struct</c>（configfileregistry.go:59-89）。</summary>
internal sealed class ConfigFileEntry : Dirty.Cloneable<ConfigFileEntry>
{
    internal string _fileName = "";
    internal PendingReload _pendingReload;
    internal TsOptions.ParsedCommandLine? _commandLine;

    /// <summary>
    /// retainingProjects is the set of projects that have called acquireConfig
    /// without releasing it. A config file entry may be acquired by a project
    /// either because it is the config for that project or because it is the
    /// config for a referenced project.
    /// </summary>
    internal Collections.Set<ID>? _retainingProjects;

    /// <summary>
    /// retainingOpenFiles is the set of open files that caused this config to
    /// load during project collection building. This config file may or may not
    /// end up being the config for the default project for these files, but
    /// determining the default project loaded this config as a candidate, so
    /// subsequent calls to `projectCollectionBuilder.findDefaultConfiguredProject`
    /// will use this config as part of the search, so it must be retained.
    /// </summary>
    internal Collections.Set<Path>? _retainingOpenFiles;

    /// <summary>
    /// retainingConfigs is the set of config files that extend this one. This
    /// provides a cheap reverse mapping for a project config's
    /// `commandLine.ExtendedSourceFiles()` that can be used to notify the
    /// extending projects when this config changes. An extended config file may
    /// or may not also be used directly by a project, so it's possible that
    /// when this is set, no other fields will be used.
    /// </summary>
    internal Collections.Set<Path>? _retainingConfigs;

    /// <summary>rootFilesWatch is a watch for the root files of this config file。</summary>
    internal WatchedFiles<PatternsAndIgnored>? _rootFilesWatch;

    /// <summary>
    /// Go: <c>func newConfigFileEntry(hasRelativePatternCapability bool, fileName string) *configFileEntry</c>
    /// （configfileregistry.go:91-102；见文件头第 1 条）。
    /// </summary>
    internal static ConfigFileEntry NewConfigFileEntry(bool hasRelativePatternCapability, string fileName)
    {
        return new ConfigFileEntry
        {
            _fileName = fileName,
            _pendingReload = PendingReload.PendingReloadFull,
            _rootFilesWatch = WatchedFiles<PatternsAndIgnored>.NewWatchedFiles(
                "root files for " + fileName,
                LsProto.WatchKind.Create | LsProto.WatchKind.Change | LsProto.WatchKind.Delete,
                hasRelativePatternCapability,
                Core.Core.Identity),
        };
    }

    /// <summary>
    /// Go: <c>func newExtendedConfigFileEntry(fileName string, extendingConfigPath tspath.Path) *configFileEntry</c>
    /// （configfileregistry.go:104-110；见文件头第 1 条）。
    /// </summary>
    internal static ConfigFileEntry NewExtendedConfigFileEntry(string fileName, Path extendingConfigPath)
    {
        var retainingConfigs = new Collections.Set<Path>();
        retainingConfigs.Add(extendingConfigPath);
        return new ConfigFileEntry
        {
            _fileName = fileName,
            _pendingReload = PendingReload.PendingReloadFull,
            _retainingConfigs = retainingConfigs,
        };
    }

    /// <summary>Go: <c>func (e *configFileEntry) Clone() *configFileEntry</c>（configfileregistry.go:112-124）。</summary>
    public ConfigFileEntry Clone()
    {
        return new ConfigFileEntry
        {
            _fileName = _fileName,
            _pendingReload = _pendingReload,
            _commandLine = _commandLine,
            // !!! eagerly cloning these maps makes everything more convenient,
            // but it could be avoided if needed.
            _retainingProjects = _retainingProjects?.Clone(),
            _retainingOpenFiles = _retainingOpenFiles?.Clone(),
            _retainingConfigs = _retainingConfigs?.Clone(),
            _rootFilesWatch = _rootFilesWatch,
        };
    }
}

/// <summary>For testing（Go: <c>type TestConfigEntry struct</c>，configfileregistry.go:153-158）。</summary>
public sealed class TestConfigEntry
{
    public string FileName = "";
    public IEnumerable<ID> RetainingProjects = [];
    public IEnumerable<Path> RetainingOpenFiles = [];
    public IEnumerable<Path> RetainingConfigs = [];

    /// <summary>Go 里由结构体字面量完成的部分（<c>maps.Keys(...)</c>，见文件头第 5 条）。</summary>
    internal static TestConfigEntry FromEntry(ConfigFileEntry entry)
    {
        return new TestConfigEntry
        {
            FileName = entry._fileName,
            RetainingProjects = entry._retainingProjects?.M ?? [],
            RetainingOpenFiles = entry._retainingOpenFiles?.M ?? [],
            RetainingConfigs = entry._retainingConfigs?.M ?? [],
        };
    }
}

/// <summary>For testing（Go: <c>type TestConfigFileNamesEntry struct</c>，configfileregistry.go:190-193）。</summary>
public sealed class TestConfigFileNamesEntry
{
    public string NearestConfigFileName = "";
    public Dictionary<string, string>? Ancestors;
}

/// <summary>Go 的未导出 <c>type configFileNames struct</c>（configfileregistry.go:222-235）。</summary>
internal sealed class ConfigFileNames : Dirty.Cloneable<ConfigFileNames>
{
    /// <summary>nearestConfigFileName is the file name of the nearest ancestor config file。</summary>
    internal string _nearestConfigFileName = "";

    /// <summary>
    /// ancestors is a map from one ancestor config file path to the next.
    /// For example, if `/a`, `/a/b`, and `/a/b/c` all contain config files,
    /// the fully loaded map will look like:
    ///     {
    ///         "/a/b/c/tsconfig.json": "/a/b/tsconfig.json",
    ///         "/a/b/tsconfig.json": "/a/tsconfig.json"
    ///     }
    /// </summary>
    internal Dictionary<string, string>? _ancestors;

    /// <summary>Go: <c>func (c *configFileNames) Clone() *configFileNames</c>（configfileregistry.go:237-242）。</summary>
    public ConfigFileNames Clone()
    {
        return new ConfigFileNames
        {
            _nearestConfigFileName = _nearestConfigFileName,
            _ancestors = _ancestors is null ? null : new Dictionary<string, string>(_ancestors),
        };
    }
}
