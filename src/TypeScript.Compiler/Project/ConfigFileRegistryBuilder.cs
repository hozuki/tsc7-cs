// ported from: internal/project/configfileregistrybuilder.go:1-828
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **未导出类型按 C# 习惯命名**：`configFileRegistryBuilder` → `internal sealed class ConfigFileRegistryBuilder`；
//    `changeFileResult` → `internal sealed class ChangeFileResult`。未导出字段 → `internal _camelCase`
//    （Go 的未导出可见性是包内 == C# internal；ConfigFileRegistryBuilder 自身是 internal，命名不外泄）。
//    导出方法名照抄 PascalCase：`DidChangeCustomConfigFileName`/`DidChangeFiles`/`Cleanup`/`FS`/
//    `GetCurrentDirectory`/`GetExtendedConfig`。
//
// 2) **包级函数落点**：`contentMapperManifestPath` 与 `isConfigBaseName`(方法) 之外，Go 的包级函数
//    `newConfigFileRegistryBuilder` → `ConfigFileRegistryBuilder.NewConfigFileRegistryBuilder(...)`
//    （先例 `Locale.Parse`）。`contentMapperManifestPath` 挂在 `ConfigFileRegistryBuilder.ContentMapperManifestPath`
//    （它只与本 builder 相关）。
//
// 3) **`sync.Mutex` → `System.Threading.Lock`**（本仓统一约定），`defer Unlock` → `lock` 语句。
//
// 4) **`map[K]struct{}` → `Collections.Set<T>`**（见 ConfigFileRegistry.cs 文件头第 3 条）。Go 的
//    `core.CopyMapInto(affectedProjects, src)`（dst 为 nil 则克隆 src）→ 本文件私有静态 `CopyInto`
//    （语义逐条等价：dst 为 nil 时返回 src 的克隆，否则把 src 并入 dst；src 为 nil 时原样返回 dst）。
//    `maps.Copy(dst, src)` 的调用点已确保 dst 非 nil，直接 `.Add` 并入。
//
// 5) **`configFileEntry` 字段前缀**：`entry.commandLine` → `entry._commandLine` 等（同 ConfigFileRegistry.cs）。
//
// 6) `slices.Sort(globs)` → `Array.Sort(globs, StringComparer.Ordinal)`（Go 的字符串序 = 逐字节序）。
//
// 7) `tsoptions.GetParsedCommandLineOfConfigFilePath(...)` 是 `TsOptions.TsOptions` 的静态方法
//    ⇒ 用别名 `TSOpts = TypeScript.TsOptions.TsOptions`（同 ExtendedConfigCache.cs 文件头第 3 条）。
//
// 8) `tspath.*` 一律走 `using static TypeScript.TSPath.TSPath;` 的裸名（避免 `TSPath` 类与命名空间同名
//    带来的 CS0118）；`Path` 用别名 `Path = TypeScript.TSPath.Path`。
//
// 9) `tspath.ForEachAncestorDirectory` 在 C# 里是 `bool ForEachAncestorDirectory<T>(string, Func<string,(T,bool)>,
//    out T result)`：Go 丢弃的 bool 在这里也丢弃；但 C# 走到"没停下来"的分支时 `result` 是 `default!`（string 为 null），
//    而 Go 是 `""` ⇒ 调用点用 `result ?? ""` 归一（`ForEachAncestorDirectory` 里已有注释说明这一点）。
//
// 10) Go 的 `for path, fileName := range deletedFiles` → `foreach (var (path, fileName) in deletedFiles)`
//     （Dictionary 的元组解构）。
//
// 11) `logger.Log(...)` 在 C# 里是 `logger?.Log(...)`（Go 的 LogTree 有 nil 接收者分支，C# 实例方法没有；
//     同 Logging/LogTree.cs 文件头第 1 条）。`logger.Logf("…%d/%s…", …)` 的格式串按本仓约定改写成
//     C# 的 `{0}`（Logger.cs 文件头第 4 条）。
//
// 12) `c.FS().ReadFile(...)` → `TryReadFile(path, out contents)`（§3.2 的 (T,bool) → TryX）。
//
// 13) `commandLine.ConfigFile.SourceFile.Text()` → C# 侧 `ConfigFile` 是 `TsConfigSourceFile?`（可空），
//     调用点 `!` 断言（Go 的 `entry.commandLine.ConfigFile` 是值指针，可能为 nil —— 这里的判空分支照 Go 保留）。
//
// 14) **Go 的 nil 接收者安全方法**：`ParsedCommandLine` 的 `WildcardDirectories`/`LiteralFileNames`/
//     `ExtendedSourceFiles` 在 Go 里都判了接收者（`tsoptions/parsedcommandline.go:258-261`/`:299-304`/
//     `:386-391`：`if p == nil { return nil }`），所以 `updateRootFilesWatch`（Go :218-228）在
//     `entry.commandLine == nil`（本次重载读文件失败，见 `ReloadIfNeeded`）时**不 panic**，三处都退化为空。
//     C# 的实例方法无法在 null 上调用 ⇒ 调用点用 `commandLine?.X() ?? []` 复刻（同 Execute/Watcher.cs
//     文件头第 7 条；**不是**额外的防御性判空）。
namespace TypeScript.Project;

using System;
using System.Collections.Generic;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.Project.Dirty.Dirty;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;
using TSOpts = TypeScript.TsOptions.TsOptions;

/// <summary>
/// configFileRegistryBuilder tracks changes made on top of a previous
/// configFileRegistry, producing a new clone with `finalize()` after
/// all changes have been made.
/// </summary>
internal sealed class ConfigFileRegistryBuilder : TSOpts.ParseConfigHost, TSOpts.ExtendedConfigCache
{
    internal bool _hasRelativePatternCapability;
    internal SourceFS _fs;
    internal Func<Path, bool> _isOpenFile;
    internal ExtendedConfigCache? _extendedConfigCache;
    internal ulong _snapshotID;
    internal SessionOptions _sessionOptions;
    internal string _customConfigFileName;

    internal ConfigFileRegistry _base;
    internal Dirty.SyncMap<Path, ConfigFileEntry> _configs;
    internal Dirty.Map<Path, ConfigFileNames> _configFileNames;
    internal bool _customConfigFileNameChanged;
    internal readonly Lock _contentMappersMu = new();
    internal ConfiguredContentMappers? _allConfiguredContentMappers;

    /// <summary>
    /// Go: <c>func newConfigFileRegistryBuilder(...)</c>（configfileregistrybuilder.go:44-71；见文件头第 2 条）。
    /// </summary>
    internal static ConfigFileRegistryBuilder NewConfigFileRegistryBuilder(
        bool hasRelativePatternCapability,
        SnapshotFSBuilder fs,
        Func<Path, bool> isOpenFile,
        ConfigFileRegistry oldConfigFileRegistry,
        ExtendedConfigCache? extendedConfigCache,
        ulong snapshotID,
        SessionOptions sessionOptions,
        string customConfigFileName,
        Logging.LogTree? logger)
    {
        return new ConfigFileRegistryBuilder(
            hasRelativePatternCapability,
            fs,
            isOpenFile,
            oldConfigFileRegistry,
            extendedConfigCache,
            snapshotID,
            sessionOptions,
            customConfigFileName,
            logger);
    }

    /// <summary>Go 的结构体字面量（见文件头第 2 条）。</summary>
    internal ConfigFileRegistryBuilder(
        bool hasRelativePatternCapability,
        SnapshotFSBuilder fs,
        Func<Path, bool> isOpenFile,
        ConfigFileRegistry oldConfigFileRegistry,
        ExtendedConfigCache? extendedConfigCache,
        ulong snapshotID,
        SessionOptions sessionOptions,
        string customConfigFileName,
        Logging.LogTree? logger)
    {
        _hasRelativePatternCapability = hasRelativePatternCapability;
        _fs = SourceFS.NewSourceFS(false, fs, fs._toPath);
        _isOpenFile = isOpenFile;
        _base = oldConfigFileRegistry;
        _sessionOptions = sessionOptions;
        _extendedConfigCache = extendedConfigCache;
        _snapshotID = snapshotID;
        _customConfigFileName = customConfigFileName;
        _customConfigFileNameChanged = customConfigFileName != oldConfigFileRegistry._customConfigFileName;
        _allConfiguredContentMappers = oldConfigFileRegistry.ContentMappers();

        _configs = NewSyncMap(oldConfigFileRegistry._configs);
        _configFileNames = NewMap(oldConfigFileRegistry._configFileNames);
    }

    /// <summary>
    /// Finalize creates a new configFileRegistry based on the changes made in the builder.
    /// If no changes were made, it returns the original base registry.
    /// </summary>
    internal ConfigFileRegistry Finalize()
    {
        var changed = false;
        var newRegistry = _base;
        void EnsureCloned()
        {
            if (!changed)
            {
                newRegistry = newRegistry.Clone();
                changed = true;
            }
        }

        var (configs, changedConfigs) = _configs.Finalize();
        if (changedConfigs)
        {
            EnsureCloned();
            newRegistry._configs = configs!;
            newRegistry._allConfiguredContentMappers = ContentMappers();
        }

        var (configFileNames, changedNames) = _configFileNames.Finalize();
        if (changedNames)
        {
            EnsureCloned();
            newRegistry._configFileNames = configFileNames!;
        }

        if (_customConfigFileNameChanged)
        {
            EnsureCloned();
            newRegistry._customConfigFileName = _customConfigFileName;
        }

        return newRegistry;
    }

    /// <summary>Go: <c>func (c *configFileRegistryBuilder) contentMappers() *configuredContentMappers</c>。</summary>
    internal ConfiguredContentMappers ContentMappers()
    {
        lock (_contentMappersMu)
        {
            if (_allConfiguredContentMappers is null)
            {
                var commandLines = new List<TsOptions.ParsedCommandLine>();
                _configs.Range(entry =>
                {
                    if (entry.Value()._commandLine is { } commandLine)
                    {
                        commandLines.Add(commandLine);
                    }

                    return true;
                });
                _allConfiguredContentMappers = ConfigFileRegistry.CollectConfiguredContentMappers(commandLines.ToArray());
            }

            return _allConfiguredContentMappers;
        }
    }

    /// <summary>Go: <c>func (c *configFileRegistryBuilder) invalidateContentMappers()</c>。</summary>
    private void InvalidateContentMappers()
    {
        lock (_contentMappersMu)
        {
            _allConfiguredContentMappers = null;
        }
    }

    /// <summary>Go: <c>func (c *configFileRegistryBuilder) findOrAcquireConfigForFile(...)</c>（configfileregistrybuilder.go:113-128）。</summary>
    internal TsOptions.ParsedCommandLine? FindOrAcquireConfigForFile(
        string configFileName,
        Path configFilePath,
        Path filePath,
        ProjectLoadKind loadKind,
        Logging.LogTree? logger)
    {
        switch (loadKind)
        {
            case ProjectLoadKind.ProjectLoadKindFind:
                {
                    var (entry, ok) = _configs.Load(configFilePath);
                    if (ok)
                    {
                        return entry!.Value()._commandLine;
                    }

                    return null;
                }

            case ProjectLoadKind.ProjectLoadKindCreate:
                return AcquireConfigForFile(configFileName, configFilePath, filePath, logger);

            default:
                Debug.Fail($"unknown project load kind: {(int)loadKind}");
                return null;
        }
    }

    /// <summary>
    /// reloadIfNeeded updates the command line of the config file entry based on its
    /// pending reload state. This function should only be called from within the
    /// Change() method of a dirty map entry.
    /// </summary>
    private bool ReloadIfNeeded(ConfigFileEntry entry, string fileName, Path path, Logging.LogTree? logger)
    {
        var oldCommandLine = entry._commandLine;
        switch (entry._pendingReload)
        {
            case PendingReload.PendingReloadFileNames:
                logger?.Log("Reloading file names for config: " + fileName);
                entry._commandLine = entry._commandLine!.ReloadFileNamesOfParsedCommandLine(_fs);
                break;

            case PendingReload.PendingReloadFull:
                logger?.Log("Loading config file: " + fileName);
                // When the workspace is trusted, enable external content mappers so a config's contentMappers pass
                // the runExternalCode gate and register, as they would with the CLI flag.
                CompilerOptions? existingOptions = null;
                if (_sessionOptions.RunExternalCode)
                {
                    existingOptions = new CompilerOptions { RunExternalCode = Tristate.TSTrue };
                }

                (entry._commandLine, _) = TSOpts.GetParsedCommandLineOfConfigFilePath(fileName, path, existingOptions, null /*optionsRaw*/, this, this);
                UpdateExtendingConfigs(path, entry._commandLine, oldCommandLine);
                UpdateRootFilesWatch(fileName, entry);
                logger?.Log("Finished loading config file");
                break;

            default:
                return false;
        }

        entry._pendingReload = PendingReload.PendingReloadNone;
        return !ReferenceEquals(oldCommandLine, entry._commandLine);
    }

    private void UpdateExtendingConfigs(
        Path extendingConfigPath,
        TsOptions.ParsedCommandLine? newCommandLine,
        TsOptions.ParsedCommandLine? oldCommandLine)
    {
        var newExtendedConfigPaths = new Collections.Set<Path>();
        if (newCommandLine is not null)
        {
            foreach (var extendedConfig in newCommandLine.ExtendedSourceFiles() ?? [])
            {
                var extendedConfigPath = _fs._toPath(extendedConfig);
                newExtendedConfigPaths.Add(extendedConfigPath);
                var (entry, loaded) = _configs.LoadOrStore(extendedConfigPath, ConfigFileEntry.NewExtendedConfigFileEntry(extendedConfig, extendingConfigPath));
                if (loaded)
                {
                    entry!.ChangeIf(
                        config => !(config._retainingConfigs?.Has(extendingConfigPath) ?? false),
                        config =>
                        {
                            config._retainingConfigs ??= new Collections.Set<Path>();
                            config._retainingConfigs.Add(extendingConfigPath);
                        });
                }
            }
        }

        if (oldCommandLine is not null)
        {
            foreach (var extendedConfig in oldCommandLine.ExtendedSourceFiles() ?? [])
            {
                var extendedConfigPath = _fs._toPath(extendedConfig);
                if (newExtendedConfigPaths.Has(extendedConfigPath))
                {
                    continue;
                }

                var (entry, ok) = _configs.Load(extendedConfigPath);
                if (ok)
                {
                    entry!.ChangeIf(
                        config => config._retainingConfigs?.Has(extendingConfigPath) ?? false,
                        config => config._retainingConfigs!.Delete(extendingConfigPath));
                }
            }
        }
    }

    private void UpdateRootFilesWatch(string fileName, ConfigFileEntry entry)
    {
        if (entry._rootFilesWatch is null)
        {
            return;
        }

        HashSet<string>? ignored = null;
        var globs = new List<string>();
        var externalDirectories = new List<string>();
        var includeWorkspace = false;
        var includeTsconfigDir = false;
        var tsconfigDir = GetDirectoryPath(fileName);
        // Go: entry.commandLine 可能是 nil（本次重载读文件失败时，见 ReloadIfNeeded），而 Go 的
        // ParsedCommandLine.WildcardDirectories/LiteralFileNames/ExtendedSourceFiles 都是 nil 接收者安全的方法
        // （parsedcommandline.go:258-261/299-304/386-391：`if p == nil { return nil }`）⇒ 调用点用 `?.`/`?? []`
        // 复刻同一语义（同 Execute/Watcher.cs 文件头第 7 条）。
        var commandLine = entry._commandLine;
        var wildcardDirectories = commandLine?.WildcardDirectories();
        var comparePathsOptions = new TSPath.ComparePathsOptions(
            useCaseSensitiveFileNames: FS().UseCaseSensitiveFileNames(),
            currentDirectory: _sessionOptions.CurrentDirectory);
        if (wildcardDirectories is not null)
        {
            foreach (var dir in wildcardDirectories.Keys)
            {
                if (ContainsPath(_sessionOptions.CurrentDirectory, dir, comparePathsOptions))
                {
                    includeWorkspace = true;
                }
                else if (ContainsPath(tsconfigDir, dir, comparePathsOptions))
                {
                    includeTsconfigDir = true;
                }
                else
                {
                    externalDirectories.Add(dir);
                }
            }
        }

        foreach (var literalFileName in commandLine?.LiteralFileNames() ?? [])
        {
            if (ContainsPath(_sessionOptions.CurrentDirectory, literalFileName, comparePathsOptions))
            {
                includeWorkspace = true;
            }
            else if (ContainsPath(tsconfigDir, literalFileName, comparePathsOptions))
            {
                includeTsconfigDir = true;
            }
            else
            {
                externalDirectories.Add(GetDirectoryPath(literalFileName));
            }
        }

        if (includeWorkspace)
        {
            globs.Add(Watch.GetRecursiveGlobPattern(_sessionOptions.CurrentDirectory));
        }

        if (includeTsconfigDir)
        {
            globs.Add(Watch.GetRecursiveGlobPattern(tsconfigDir));
        }

        foreach (var extendedFileName in commandLine?.ExtendedSourceFiles() ?? [])
        {
            if (includeWorkspace && ContainsPath(_sessionOptions.CurrentDirectory, extendedFileName, comparePathsOptions))
            {
                continue;
            }

            globs.Add(extendedFileName);
        }

        if (externalDirectories.Count > 0)
        {
            var (commonParents, ignoredExternalDirs) = GetCommonParents(
                externalDirectories.ToArray(),
                Watch.MinWatchLocationDepth,
                Watch.GetPathComponentsForWatching,
                comparePathsOptions);
            foreach (var parent in commonParents)
            {
                globs.Add(Watch.GetRecursiveGlobPattern(parent));
            }

            ignored = ignoredExternalDirs;
        }

        var sortedGlobs = globs.ToArray();
        Array.Sort(sortedGlobs, StringComparer.Ordinal);
        entry._rootFilesWatch = entry._rootFilesWatch.Clone(new PatternsAndIgnored
        {
            _patternsInsideWorkspace = sortedGlobs,
            _ignored = ignored,
        });
    }

    /// <summary>
    /// acquireConfigForProject loads a config file entry from the cache, or parses it if not already
    /// cached, then adds the project (if provided) to `retainingProjects` to keep it alive
    /// in the cache. Each `acquireConfigForProject` call that passes a `project` should be accompanied
    /// by an eventual `releaseConfigForProject` call with the same project.
    /// </summary>
    internal TsOptions.ParsedCommandLine? AcquireConfigForProject(
        string fileName,
        Path path,
        Project project,
        Logging.LogTree? logger)
    {
        var (entry, _) = _configs.LoadOrStore(path, ConfigFileEntry.NewConfigFileEntry(_hasRelativePatternCapability, fileName));
        var needsRetainProject = false;
        var contentMappersChanged = false;
        entry!.ChangeIf(
            config =>
            {
                var alreadyRetaining = config._retainingProjects?.Has(project.ID()) ?? false;
                needsRetainProject = !alreadyRetaining;
                return needsRetainProject || config._pendingReload != PendingReload.PendingReloadNone;
            },
            config =>
            {
                if (needsRetainProject)
                {
                    config._retainingProjects ??= new Collections.Set<ID>();
                    config._retainingProjects.Add(project.ID());
                }

                contentMappersChanged = ReloadIfNeeded(config, fileName, path, logger);
            });
        if (contentMappersChanged)
        {
            InvalidateContentMappers();
        }

        return entry.Value()._commandLine;
    }

    /// <summary>
    /// acquireConfigForFile loads a config file entry from the cache, or parses it if not already
    /// cached, then adds the open file to `retainingOpenFiles` to keep it alive in the cache.
    /// Each `acquireConfigForFile` call that passes an `openFilePath`
    /// should be accompanied by an eventual `releaseConfigForOpenFile` call with the same open file.
    /// </summary>
    internal TsOptions.ParsedCommandLine? AcquireConfigForFile(
        string configFileName,
        Path configFilePath,
        Path filePath,
        Logging.LogTree? logger)
    {
        var (entry, _) = _configs.LoadOrStore(configFilePath, ConfigFileEntry.NewConfigFileEntry(_hasRelativePatternCapability, configFileName));
        var needsRetainOpenFile = false;
        var contentMappersChanged = false;
        entry!.ChangeIf(
            config =>
            {
                if (_isOpenFile(filePath))
                {
                    var alreadyRetaining = config._retainingOpenFiles?.Has(filePath) ?? false;
                    needsRetainOpenFile = !alreadyRetaining;
                }

                return needsRetainOpenFile || config._pendingReload != PendingReload.PendingReloadNone;
            },
            config =>
            {
                if (needsRetainOpenFile)
                {
                    config._retainingOpenFiles ??= new Collections.Set<Path>();
                    config._retainingOpenFiles.Add(filePath);
                }

                contentMappersChanged = ReloadIfNeeded(config, configFileName, configFilePath, logger);
            });
        if (contentMappersChanged)
        {
            InvalidateContentMappers();
        }

        return entry.Value()._commandLine;
    }

    /// <summary>
    /// releaseConfigForProject removes the project from the config entry. Once no projects
    /// or files are associated with the config entry, it will be removed on the next call to `cleanup`.
    /// </summary>
    internal void ReleaseConfigForProject(Path configFilePath, ID projectID)
    {
        var (entry, ok) = _configs.Load(configFilePath);
        if (ok)
        {
            entry!.ChangeIf(
                config => config._retainingProjects?.Has(projectID) ?? false,
                config => config._retainingProjects!.Delete(projectID));
        }
    }

    internal void RetainConfigForProject(Path configFilePath, ID projectID)
    {
        var (entry, ok) = _configs.Load(configFilePath);
        if (ok)
        {
            entry!.ChangeIf(
                config => !(config._retainingProjects?.Has(projectID) ?? false),
                config =>
                {
                    config._retainingProjects ??= new Collections.Set<ID>();
                    config._retainingProjects.Add(projectID);
                });
        }
    }

    /// <summary>
    /// didCloseFile removes the open file from the config entry. Once no projects
    /// or files are associated with the config entry, it will be removed on the next call to `cleanup`.
    /// </summary>
    internal void DidCloseFile(Path path)
    {
        if (IsDynamicFileName(path.Value))
        {
            return;
        }

        _configFileNames.Delete(path);
        _configs.Range(entry =>
        {
            entry.ChangeIf(
                config => config._retainingOpenFiles?.Has(path) ?? false,
                config => config._retainingOpenFiles!.Delete(path));
            return true;
        });
    }

    internal bool DidChangeCustomConfigFileName(Logging.LogTree? logger)
    {
        if (!_customConfigFileNameChanged)
        {
            return false;
        }

        _configFileNames.Clear();
        return true;
    }

    private ChangeFileResult InvalidateCache(Logging.LogTree? logger)
    {
        Collections.Set<ID>? affectedProjects = null;
        Collections.Set<Path>? affectedFiles = null;

        logger?.Log("Too many files changed; marking all configs for reload");
        _configFileNames.Range(entry =>
        {
            affectedFiles ??= new Collections.Set<Path>();
            affectedFiles.Add(entry.Key());
            return true;
        });
        _configFileNames.Clear();

        _configs.Range(entry =>
        {
            entry.Change(configEntry =>
            {
                affectedProjects = CopyInto(affectedProjects, configEntry._retainingProjects);
                if (configEntry._pendingReload != PendingReload.PendingReloadFull)
                {
                    var ok = FS().TryReadFile(configEntry._fileName, out var text);
                    if (!ok || configEntry._commandLine is null || text != configEntry._commandLine.ConfigFile!.SourceFile.Text())
                    {
                        configEntry._pendingReload = PendingReload.PendingReloadFull;
                    }
                    else
                    {
                        configEntry._pendingReload = PendingReload.PendingReloadFileNames;
                    }
                }
            });
            return true;
        });

        return new ChangeFileResult
        {
            _affectedProjects = affectedProjects,
            _affectedFiles = affectedFiles,
        };
    }

    /// <summary>Go: <c>core.CopyMapInto(dst, src)</c>（见文件头第 4 条）。</summary>
    private static Collections.Set<T>? CopyInto<T>(Collections.Set<T>? dst, Collections.Set<T>? src)
    {
        if (src is null)
        {
            return dst;
        }

        var result = dst ?? new Collections.Set<T>();
        foreach (var item in src.M)
        {
            result.Add(item);
        }

        return result;
    }

    private bool IsConfigBaseName(string baseName)
    {
        return baseName == "tsconfig.json" || baseName == "jsconfig.json"
            || (_customConfigFileName.Length != 0 && baseName == _customConfigFileName);
    }

    internal ChangeFileResult DidChangeFiles(FileChangeSummary summary, Logging.LogTree? logger)
    {
        if (summary.InvalidateAll)
        {
            return InvalidateCache(logger);
        }

        Collections.Set<ID>? affectedProjects = null;
        Collections.Set<Path>? affectedFiles = null;
        var shouldInvalidateCache = false;

        logger?.Log("Summarizing file changes");
        var hasExcessiveChanges = summary.HasExcessiveWatchEvents() && summary.IncludesWatchChangeOutsideNodeModules;
        var createdFiles = new Dictionary<Path, string>(summary.Created.Len());
        var deletedFiles = new Dictionary<Path, string>(summary.Deleted.Len());
        var createdOrDeletedConfigFiles = new HashSet<Path>();
        var createdOrChangedOrDeletedFiles = new HashSet<Path>(summary.Changed.Len() + summary.Created.Len() + summary.Deleted.Len());
        foreach (var uri in summary.Changed.M)
        {
            if (ContainsIgnoredPath(uri.Value))
            {
                continue;
            }

            var fileName = uri.FileName();
            var path = _fs._toPath(fileName);
            var baseName = GetBaseFileName(path.Value);
            if (IsConfigBaseName(baseName))
            {
                createdOrDeletedConfigFiles.Add(path);
            }

            createdOrChangedOrDeletedFiles.Add(path);
        }

        foreach (var uri in summary.Deleted.M)
        {
            if (ContainsIgnoredPath(uri.Value))
            {
                continue;
            }

            var fileName = uri.FileName();
            var path = _fs._toPath(fileName);
            deletedFiles[path] = fileName;
            var baseName = GetBaseFileName(path.Value);
            if (IsConfigBaseName(baseName))
            {
                createdOrDeletedConfigFiles.Add(path);
            }

            createdOrChangedOrDeletedFiles.Add(path);
        }

        foreach (var uri in summary.Created.M)
        {
            if (ContainsIgnoredPath(uri.Value))
            {
                continue;
            }

            var fileName = uri.FileName();
            var path = _fs._toPath(fileName);
            createdFiles[path] = fileName;
            var baseName = GetBaseFileName(path.Value);
            if (IsConfigBaseName(baseName))
            {
                createdOrDeletedConfigFiles.Add(path);
            }

            createdOrChangedOrDeletedFiles.Add(path);
        }

        // Handle closed files - this ranges over config entries and could be combined
        // with the file change handling, but a separate loop is simpler and a snapshot
        // change with both closing and watch changes seems rare.
        foreach (var uri in summary.Closed.M)
        {
            var fileName = uri.FileName();
            var path = _fs._toPath(fileName);
            DidCloseFile(path);
        }

        // Handle changes to stored config files and their content mapper package manifests.
        logger?.Log("Checking if any changed files are configuration files");
        foreach (var path in createdOrChangedOrDeletedFiles)
        {
            var (entry, ok) = _configs.Load(path);
            if (ok)
            {
                if (hasExcessiveChanges)
                {
                    return InvalidateCache(logger);
                }

                affectedProjects = CopyInto(affectedProjects, HandleConfigChange(entry!, logger));
                if (entry!.Value()._retainingConfigs is { } retainingConfigs)
                {
                    foreach (var extendingConfigPath in retainingConfigs.M)
                    {
                        var (extendingConfigEntry, extendingConfigEntryOk) = _configs.Load(extendingConfigPath);
                        if (extendingConfigEntryOk)
                        {
                            affectedProjects = CopyInto(affectedProjects, HandleConfigChange(extendingConfigEntry!, logger));
                        }
                    }
                }

                // This was a config file, so assume it's not also a root file
                createdFiles.Remove(path);
            }
            else if (GetBaseFileName(path.Value) == "package.json")
            {
                var manifestChanged = false;
                _configs.Range(configEntry =>
                {
                    if (ContentMapperManifestPath(configEntry.Value()._commandLine, _fs._toPath, path))
                    {
                        affectedProjects = CopyInto(affectedProjects, HandleConfigChange(configEntry, logger));
                        manifestChanged = true;
                    }

                    return true;
                });
                if (manifestChanged)
                {
                    InvalidateContentMappers();
                }
            }
        }

        // Handle created/deleted files named "tsconfig.json" or "jsconfig.json"
        foreach (var path in createdOrDeletedConfigFiles)
        {
            if (hasExcessiveChanges)
            {
                return InvalidateCache(logger);
            }

            var directoryPath = path.GetDirectoryPath();
            _configFileNames.Range(entry =>
            {
                if (directoryPath.ContainsPath(entry.Key()))
                {
                    affectedFiles ??= new Collections.Set<Path>();
                    affectedFiles.Add(entry.Key());
                    entry.Delete();
                }

                return true;
            });
        }

        // Handle deletions of wildcard-included root files
        foreach (var (path, fileName) in deletedFiles)
        {
            _configs.Range(entry =>
            {
                entry.ChangeIf(
                    config =>
                    {
                        if (config._pendingReload != PendingReload.PendingReloadNone || config._commandLine is null)
                        {
                            return false;
                        }

                        if (config._commandLine.FileNamesByPath().ContainsKey(path))
                        {
                            // If the file is included in FileNames() but not matched by literal "files", it must be
                            // included via wildcard, which means a reload of filenames will remove it from the list.
                            // (Files explicitly specified in "files" are always included in the ParsedCommandLine,
                            // triggering a missing root file error during program construction.)
                            return config._commandLine.GetMatchedFileSpec(fileName) == "";
                        }

                        return false;
                    },
                    config =>
                    {
                        config._pendingReload = PendingReload.PendingReloadFileNames;
                        affectedProjects ??= new Collections.Set<ID>();
                        if (config._retainingProjects is { } retainingProjects)
                        {
                            foreach (var projectID in retainingProjects.M)
                            {
                                affectedProjects.Add(projectID);
                            }
                        }

                        logger?.Logf("Root files for config {0} changed", entry.Key().Value);
                        shouldInvalidateCache = hasExcessiveChanges;
                    });
                return !shouldInvalidateCache;
            });
            if (shouldInvalidateCache)
            {
                return InvalidateCache(logger);
            }
        }

        // Handle possible root file creation
        if (createdFiles.Count > 0)
        {
            _configs.Range(entry =>
            {
                entry.ChangeIf(
                    config =>
                    {
                        if (config._commandLine is null || config._rootFilesWatch is null || config._pendingReload != PendingReload.PendingReloadNone)
                        {
                            return false;
                        }

                        logger?.Logf("Checking if any of {0} created files match root files for config {1}", createdFiles.Count, entry.Key().Value);
                        foreach (var (path, fileName) in createdFiles)
                        {
                            if (config._commandLine.PossiblyMatchesFileName(fileName))
                            {
                                return true;
                            }

                            if (config._commandLine.PossiblyMatchesDirectoryName(path) && _fs.DirectoryExists(fileName))
                            {
                                // If we got a creation event for a directory, it's probably a symlink. We don't need to
                                // test realpath here; this is enough confidence to trigger a filename reload.
                                return true;
                            }
                        }

                        return false;
                    },
                    config =>
                    {
                        config._pendingReload = PendingReload.PendingReloadFileNames;
                        affectedProjects ??= new Collections.Set<ID>();
                        if (config._retainingProjects is { } retainingProjects)
                        {
                            foreach (var projectID in retainingProjects.M)
                            {
                                affectedProjects.Add(projectID);
                            }
                        }

                        logger?.Logf("Root files for config {0} changed", entry.Key().Value);
                        shouldInvalidateCache = hasExcessiveChanges;
                    });
                return !shouldInvalidateCache;
            });
            if (shouldInvalidateCache)
            {
                return InvalidateCache(logger);
            }
        }

        return new ChangeFileResult
        {
            _affectedProjects = affectedProjects,
            _affectedFiles = affectedFiles,
        };
    }

    private Collections.Set<ID>? HandleConfigChange(Dirty.SyncMapEntry<Path, ConfigFileEntry> entry, Logging.LogTree? logger)
    {
        Collections.Set<ID>? affectedProjects = null;
        var changed = entry.ChangeIf(
            config => config._pendingReload != PendingReload.PendingReloadFull,
            config => config._pendingReload = PendingReload.PendingReloadFull);
        if (changed)
        {
            logger?.Logf("Config file {0} changed", entry.Key().Value);
            affectedProjects = entry.Value()._retainingProjects?.Clone();
        }

        return affectedProjects;
    }

    /// <summary>Go: <c>func contentMapperManifestPath(...)</c>（configfileregistrybuilder.go:656-667；见文件头第 2 条）。</summary>
    internal static bool ContentMapperManifestPath(TsOptions.ParsedCommandLine? commandLine, Func<string, Path> toPath, Path path)
    {
        if (commandLine is null)
        {
            return false;
        }

        foreach (var mapper in commandLine.ContentMappers() ?? [])
        {
            if (mapper.Package.Length != 0 && mapper.ContributionID.Length == 0 && mapper.PackageDirectory.Length != 0
                && toPath(CombinePaths(mapper.PackageDirectory, "package.json")) == path)
            {
                return true;
            }
        }

        return false;
    }

    internal string ComputeConfigFileName(string fileName, bool skipSearchInDirectoryOfFile, Logging.LogTree? logger)
    {
        var searchPath = GetDirectoryPath(fileName);
        // Prefer custom config file if provided; search ancestors with correct skip behavior.
        if (_customConfigFileName.Length != 0)
        {
            var skip = skipSearchInDirectoryOfFile;
            ForEachAncestorDirectory<string>(searchPath, directory =>
            {
                if (!skip)
                {
                    var customPath = CombinePaths(directory, _customConfigFileName);
                    if (FS().FileExists(customPath))
                    {
                        return (customPath, true);
                    }
                }

                if (directory.EndsWith("/node_modules", StringComparison.Ordinal))
                {
                    return ("", true);
                }

                skip = false;
                return ("", false);
            }, out var customResult);
            customResult ??= "";
            if (customResult.Length != 0)
            {
                logger?.Logf("computeConfigFileName:: File: {0}:: Result: {1}", fileName, customResult);
                return customResult;
            }
        }

        // When searching for ancestor of a config file, determine which config types to skip
        // in the starting directory. This matches TSServer's forEachConfigFileLocation behavior:
        // - For ancestor of tsconfig.json: skip tsconfig.json but still check jsconfig.json
        // - For ancestor of jsconfig.json: skip both tsconfig.json and jsconfig.json
        var skipTsconfig = skipSearchInDirectoryOfFile;
        var skipJsconfig = skipSearchInDirectoryOfFile && !fileName.EndsWith("/tsconfig.json", StringComparison.Ordinal);
        ForEachAncestorDirectory<string>(searchPath, directory =>
        {
            if (!skipTsconfig)
            {
                var tsconfigPath = CombinePaths(directory, "tsconfig.json");
                if (FS().FileExists(tsconfigPath))
                {
                    return (tsconfigPath, true);
                }
            }

            if (!skipJsconfig)
            {
                var jsconfigPath = CombinePaths(directory, "jsconfig.json");
                if (FS().FileExists(jsconfigPath))
                {
                    return (jsconfigPath, true);
                }
            }

            if (directory.EndsWith("/node_modules", StringComparison.Ordinal))
            {
                return ("", true);
            }

            skipTsconfig = false;
            skipJsconfig = false;
            return ("", false);
        }, out var result);
        result ??= "";
        logger?.Logf("computeConfigFileName:: File: {0}:: Result: {1}", fileName, result);
        return result;
    }

    internal string GetConfigFileNameForFile(string fileName, Path path, Logging.LogTree? logger)
    {
        if (IsDynamicFileName(fileName))
        {
            return "";
        }

        var (entry, ok) = _configFileNames.Get(path);
        if (ok)
        {
            return entry!.Value()._nearestConfigFileName;
        }

        var configName = ComputeConfigFileName(fileName, false, logger);
        if (_isOpenFile(path))
        {
            _configFileNames.Add(path, new ConfigFileNames { _nearestConfigFileName = configName });
        }

        return configName;
    }

    internal void ForEachConfigFileNameFor(Path path, Action<string> cb)
    {
        if (IsDynamicFileName(path.Value))
        {
            return;
        }

        var (entry, ok) = _configFileNames.Get(path);
        if (ok)
        {
            var configFileName = entry!.Value()._nearestConfigFileName;
            while (configFileName.Length != 0)
            {
                cb(configFileName);
                if (entry.Value()._ancestors is { } ancestors && ancestors.TryGetValue(configFileName, out var ancestorConfigName))
                {
                    configFileName = ancestorConfigName;
                }
                else
                {
                    return;
                }
            }
        }
    }

    internal string GetAncestorConfigFileName(string fileName, Path path, string configFileName, Logging.LogTree? logger)
    {
        if (IsDynamicFileName(fileName))
        {
            return "";
        }

        var (entry, ok) = _configFileNames.Get(path);
        if (!ok)
        {
            return "";
        }

        if (entry!.Value()._ancestors is { } ancestors && ancestors.TryGetValue(configFileName, out var ancestorConfigName))
        {
            return ancestorConfigName;
        }

        // Look for config in parent folders of config file
        var result = ComputeConfigFileName(configFileName, true, logger);

        if (_isOpenFile(path))
        {
            entry.Change(value =>
            {
                value._ancestors ??= [];
                value._ancestors[configFileName] = result;
            });
        }

        return result;
    }

    /// <summary>FS implements tsoptions.ParseConfigHost。</summary>
    public Vfs.FS FS()
    {
        return _fs;
    }

    /// <summary>GetCurrentDirectory implements tsoptions.ParseConfigHost。</summary>
    public string GetCurrentDirectory()
    {
        return _sessionOptions.CurrentDirectory;
    }

    /// <summary>GetExtendedConfig implements tsoptions.ExtendedConfigCache。</summary>
    public TSOpts.ExtendedConfigCacheEntry? GetExtendedConfig(
        string fileName,
        Path path,
        Path[]? resolutionStack,
        TSOpts.ParseConfigHost host)
    {
        var content = "";
        var fh = _fs.GetFileByPath(fileName, path);
        if (fh is not null)
        {
            content = fh.Content();
        }

        return _extendedConfigCache!.LoadAndAcquire(path, _snapshotID, new ExtendedConfigParseArgs
        {
            FileName = fileName,
            Content = content,
            FS = _fs._source,
            ResolutionStack = resolutionStack,
            Host = host,
            Cache = this,
        })._extendedConfigCacheEntry;
    }

    internal void Cleanup()
    {
        var changed = false;
        _configs.Range(entry =>
        {
            entry.DeleteIf(value =>
            {
                var shouldDelete = (value._retainingProjects?.Len() ?? 0) == 0
                    && (value._retainingOpenFiles?.Len() ?? 0) == 0
                    && (value._retainingConfigs?.Len() ?? 0) == 0;
                changed = changed || shouldDelete;
                return shouldDelete;
            });
            return true;
        });
        if (changed)
        {
            InvalidateContentMappers();
        }
    }
}

/// <summary>Go 的未导出 <c>type changeFileResult struct</c>（configfileregistrybuilder.go:383-403）。</summary>
internal sealed class ChangeFileResult
{
    internal Collections.Set<ID>? _affectedProjects;
    internal Collections.Set<Path>? _affectedFiles;

    internal bool IsEmpty()
    {
        return (_affectedProjects?.Len() ?? 0) == 0 && (_affectedFiles?.Len() ?? 0) == 0;
    }
}
