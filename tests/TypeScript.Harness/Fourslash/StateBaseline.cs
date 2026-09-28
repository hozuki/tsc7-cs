// ported from: internal/fourslash/statebaseline.go:1-510
//
// 偏离清单（相对 Go 原文逐条）：
// 1. 未导出类型按 C# 习惯 PascalCase：`stateBaseline` → `StateBaseline`（共享契约已冻结此名）、
//    `openFileInfo` → `OpenFileInfo`、`diffTableOptions` → `DiffTableOptions`、`diffTable` → `DiffTable`、
//    `diffTableWriter` → `DiffTableWriter`；未导出字段一律 `internal _camelCase`（对应 Go 的包内可见性，
//    跨 partial/跨文件可读）。
// 2. 包级函数 `newStateBaseline` → `public static StateBaseline NewStateBaseline(FsWithSys)`（共享契约冻结的静态工厂；
//    Go 返回 `*stateBaseline`，C# 的 class 本身即引用语义）。
// 3. `type projectInfo = *compiler.Program` 是 Go 的类型别名 ⇒ C# 直接用 `TypeScript.Compiler.Program`（可空处写 `Program?`），
//    不额外声明别名类型。
// 4. `iter.Seq[T]` / `iter.Seq2` → `IEnumerable<T>`。`areIterSeqEqual[T ~string]` 的 `T ~string` 语义用
//    `x.ToString()` 取底层字符串（`project.ID` 与 `tspath.Path` 都覆写了 ToString，见 Project/Project.cs:82、
//    TSPath/Path.cs:41），排序/比较一律 `StringComparer.Ordinal`（Go 的字符串比较是字节序）。
// 5. `requestOrMessage` 是 Go 结构体，字段映射靠 reflect；C# 侧类必须自己实现 `IJsonMarshaler`（docs/json.md §2），
//    而 `LsProto.StructSpec` 是 Compiler 程序集的 internal（Harness 程序集不可见）。故改用仓库既有的
//    `OrderedMap<string, object?>` 表达同一对象（插入序 = 字段声明序 method → params），`json:"params,omitzero"`
//    用"params 为 null 时不 Set"表达（fourslash 的全部调用点传的都是指针，Go 的 omitzero 只在 nil 指针时省略，
//    与 `is null` 等价；见 fourslash.go:750/786/5195 的调用方）。输出走同一个 encoder + 同一缩进选项，
//    与 Go 的结构体编码逐字节相同。
// 6. `fmt.Fprintf(w, "%s%-*s %s\n", indent, keyWidth+1, key, value)`（左对齐补齐）→
//    `w.Write($"{indent}{key.PadRight(keyWidth + 1)} {value}\n")`。`%-*s` 的宽度在 Go 里按 rune 数、`len(key)` 是字节数，
//    这里宽度与长度统一用 UTF-16 `.Length`（项目 ID / 路径全 ASCII，两者无差别）。
// 7. `%v` 格式化 bool：C# 插值输出 "True"/"False"、Go 输出 "true"/"false"，故显式写成 `? "true" : "false"`，
//    保证 baseline 逐字节相同。
// 8. Go 的 `diffTable.diff` 是值语义 `collections.OrderedMap[string,string]`（零值可用）→ C# 字段用
//    `new OrderedMap<string,string>()`；`GetOrZero` 在 C# 返回 `V?`，拼接前 `?? ""`。
// 9. `io.Writer` → `TextWriter`（与 FSDiffer/其它 baseline 输出一致）；`strings.Builder` → `StringBuilder`
//    （Go 直接写 `*strings.Builder`，这里用 `StringWriter` 适配）。`strings.TrimSpace(builder.String())` →
//    `builder.ToString().Trim()`：`TsOptions.TsOptions.TrimSpace` 是 Compiler 程序集的 internal，Harness 程序集
//    不可见，仓库既有惯例同样是 `Trim()`（见 TestUtil/TsBaseline/TypeSymbolBaseline.cs:13），两者只差
//    U+001C–U+001F 这几个不可能出现在 baseline 文本里的字符；`builder.Reset()` → `builder.Clear()`。
// 10. Go 的 `*project.ConfigFileRegistry` 测试方法自带 nil 接收者检查（`if c != nil`，configfileregistry.go:168-224），
//    C# 端口没有 ⇒ 本文件对可能为 nil 的 `_serializedConfigFileRegistry` 一律用 `?.` 调用，保持 Go 语义。
// 11. map 迭代顺序：Go 无序、C# 的 Dictionary 也无序；输出顺序由 `DiffTableWriter`（键排序）/`DiffTable`（SortKeys）
//    保证，行为与 Go 一致。`ancestorDiff` 的 `sortKeys: true` 同样兜住 `Ancestors` 的 map 顺序。
// 12. `slices.Sort` → `List<T>.Sort(StringComparer.Ordinal)` / `Array.Sort(xs, StringComparer.Ordinal)`；
//    `slices.Contains` → `Array.IndexOf(xs, v) >= 0`；`slices.Equal` → `SequenceEqual`（两侧都非 null）；
//    `maps.Equal` → 私有 `MapsEqual`（nil map 视作空 map）。
// 13. `isLibFile` 是 fourslash.go:5681 的包级函数（由 FsCore 切片落在 FourslashTest 上）⇒ 本文件直接调用 `IsLibFile(...)`。
// 14. `t.Helper()` 删除；`assert.Assert(t, resultOk)` → `if (!resultOk) { t.Fatal("assert failed"); }`；
//    `f.testData.isStateBaseliningEnabled()` → `_testData!.IsStateBaseliningEnabled()`。
// 15. `f.client.Server.Session()`：C# 侧 `lsp.Server` 的 session 是只读属性 `Session`（可空，见 M8LspServer 冻结），
//    故写 `_client.Server.Session!`；`session.Snapshot()` 保持方法调用。Go 在 nil 接收者上会 panic，C#
//    的 `!` 之后同样在 nil 上抛 NullReferenceException。
// 16. Go 的指针比较（`oldProgram != program`、`oldFile != file`、`existing != entry`、`oldEntry != entry`）→
//    `!ReferenceEquals(...)`（显式，不依赖类型是否重载 `==`）。
// 17. 闭包捕获：Go 1.22+ 的 `for ... range` 变量每轮独立，C# 的 `foreach` 亦然；两者一致，无需额外拷贝。
// 18. `for key := range d.diff.Keys()` 的惰性视图 → `foreach (var key in _diff.Keys())`；`maps.Keys` + `slices.Sort`
//    → `new List<string>(_diffs.Keys)` + `Sort(StringComparer.Ordinal)`。
// 19. `tspath.ToPath` / `lsconv.FileNameToDocumentURI` 经 `using static`（仓库惯例，见文件顶部注释）写成简单名
//    `ToPath(...)` / `FileNameToDocumentURI(...)`。

using System.Text;
using TypeScript.Collections;
using TypeScript.LsProto;
using TypeScript.Project;
using TypeScript.TestUtil;
using TypeScript.TestUtil.FsBaselineUtil;
using TypeScript.Vfs.Iovfs;
// Go 的包级函数 `tspath.ToPath` / `lsconv.FileNameToDocumentURI` → 仓库惯例：`using static` + 简单名
// （`using TypeScript.TSPath;` 这种 using 会让简单名 `TSPath` 解析到**命名空间**而不是静态类，见
// Compiler/Program.Files.cs 文件头第 1 条；`using static` 无此歧义，与 Ls/*.cs 对 LsConv 的用法一致）。
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Harness.Fourslash;

/// <summary>Go: <c>type stateBaseline struct</c>。</summary>
internal sealed class StateBaseline
{
    internal StringBuilder _baseline = new();

    internal FSDiffer _fsDiffer;

    internal bool _isInitialized;

    internal Dictionary<string, TypeScript.Compiler.Program?> _serializedProjects = new();

    internal Dictionary<string, OpenFileInfo> _serializedOpenFiles = new();

    internal ConfigFileRegistry? _serializedConfigFileRegistry;

    private StateBaseline(FSDiffer fsDiffer)
    {
        _fsDiffer = fsDiffer;
    }

    /// <summary>Go: <c>func newStateBaseline(fsFromMap iovfs.FsWithSys) *stateBaseline</c>。</summary>
    public static StateBaseline NewStateBaseline(FsWithSys fsFromMap)
    {
        var stateBaseline = new StateBaseline(new FSDiffer
        {
            FS = fsFromMap,
            WrittenFiles = new SyncSet<string>(),
        });
        // Go: fmt.Fprintf(&baseline, "UseCaseSensitiveFileNames: %v\n", ...)。C# 的 bool 插值是 True/False，
        // 与 Go 的 %v（true/false）不同，故显式取小写。
        stateBaseline._baseline.Append($"UseCaseSensitiveFileNames: {(fsFromMap.UseCaseSensitiveFileNames() ? "true" : "false")}\n");
        stateBaseline._fsDiffer.BaselineFSWithDiff(new StringWriter(stateBaseline._baseline));
        return stateBaseline;
    }
}

/// <summary>Go: <c>type requestOrMessage struct</c>（以 <c>OrderedMap</c> 表达，见文件头第 5 条）。</summary>
internal sealed class RequestOrMessage
{
    internal static byte[] Marshal(Method method, object? parameters)
    {
        // Go: struct 字段声明序 method → params；`params,omitzero` ⇒ nil 时省略。
        var message = new OrderedMap<string, object?>();
        message.Set("method", method);
        if (parameters is not null)
        {
            message.Set("params", parameters);
        }

        return TypeScript.Json.Json.Marshal(message, TypeScript.Json.Json.WithIndent("  "));
    }
}

/// <summary>Go: <c>type openFileInfo struct</c>。</summary>
internal sealed class OpenFileInfo
{
    internal string DefaultProjectName = "";

    internal List<string> AllProjects = [];
}

/// <summary>Go: <c>type diffTableOptions struct</c>。</summary>
internal sealed class DiffTableOptions
{
    internal string Indent = "";

    internal bool SortKeys;
}

/// <summary>Go: <c>type diffTable struct</c>。</summary>
internal sealed class DiffTable
{
    private readonly OrderedMap<string, string> _diff = new();

    internal DiffTableOptions Options;

    internal DiffTable(DiffTableOptions options)
    {
        Options = options;
    }

    /// <summary>Go: <c>func (d *diffTable) add(key, value string)</c>。</summary>
    internal void Add(string key, string value)
    {
        _diff.Set(key, value);
    }

    /// <summary>Go: <c>func (d *diffTable) print(w io.Writer, header string)</c>。</summary>
    internal void Print(TextWriter w, string header)
    {
        var count = _diff.Size();
        if (count == 0)
        {
            return;
        }

        if (header != "")
        {
            w.Write($"{Options.Indent}{header}\n");
        }

        var diffKeys = new List<string>(count);
        var keyWidth = 0;
        var indent = Options.Indent + "  ";
        foreach (var key in _diff.Keys())
        {
            keyWidth = Math.Max(keyWidth, key.Length);
            diffKeys.Add(key);
        }

        if (Options.SortKeys)
        {
            diffKeys.Sort(StringComparer.Ordinal);
        }

        foreach (var key in diffKeys)
        {
            var value = _diff.GetOrZero(key) ?? "";
            w.Write($"{indent}{key.PadRight(keyWidth + 1)} {value}\n");
        }
    }
}

/// <summary>Go: <c>type diffTableWriter struct</c>。</summary>
internal sealed class DiffTableWriter
{
    private bool _hasChange;

    private readonly string _header;

    private readonly Dictionary<string, Action<TextWriter>> _diffs = new();

    internal DiffTableWriter(string header)
    {
        _header = header;
    }

    /// <summary>Go: <c>func (d *diffTableWriter) setHasChange()</c>。</summary>
    internal void SetHasChange()
    {
        _hasChange = true;
    }

    /// <summary>Go: <c>func (d *diffTableWriter) add(key string, fn func(io.Writer))</c>。</summary>
    internal void Add(string key, Action<TextWriter> fn)
    {
        _diffs[key] = fn;
    }

    /// <summary>Go: <c>func (d *diffTableWriter) print(w io.Writer)</c>。</summary>
    internal void Print(TextWriter w)
    {
        if (!_hasChange)
        {
            return;
        }

        w.Write($"{_header}::\n");
        var keys = new List<string>(_diffs.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            _diffs[key](w);
        }
    }
}

public sealed partial class FourslashTest
{
    /// <summary>Go: <c>func (f *FourslashTest) baselineRequestOrNotification(t *testing.T, method lsproto.Method, params any)</c>。</summary>
    internal void BaselineRequestOrNotification(IHarnessTest t, Method method, object? parameters)
    {
        if (!_testData!.IsStateBaseliningEnabled())
        {
            return;
        }

        var res = RequestOrMessage.Marshal(method, parameters);
        _stateBaseline!._baseline.Append('\n');
        _stateBaseline!._baseline.Append(Encoding.UTF8.GetString(res));
        _stateBaseline!._baseline.Append('\n');
        _stateBaseline!._isInitialized = true;
    }

    /// <summary>Go: <c>func (f *FourslashTest) baselineProjectsAfterNotification(t *testing.T, fileName string)</c>。</summary>
    internal void BaselineProjectsAfterNotification(IHarnessTest t, string fileName)
    {
        if (!_testData!.IsStateBaseliningEnabled())
        {
            return;
        }

        // Do hover so we have snapshot to check things on!!
        var (_, _, resultOk) = _client.SendRequest(t, TextDocumentHoverInfo, new HoverParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = FileNameToDocumentURI(fileName),
            },
            Position = new Position
            {
                Line = 0,
                Character = 0,
            },
        });
        if (!resultOk)
        {
            t.Fatal("assert failed");
        }

        BaselineState(t);
    }

    /// <summary>Go: <c>func (f *FourslashTest) baselineState(t *testing.T)</c>。</summary>
    internal void BaselineState(IHarnessTest t)
    {
        if (!_testData!.IsStateBaseliningEnabled())
        {
            return;
        }

        var serialized = SerializedState(t);
        if (serialized != "")
        {
            _stateBaseline!._baseline.Append('\n');
            _stateBaseline!._baseline.Append(serialized);
        }
    }

    /// <summary>Go: <c>func (f *FourslashTest) serializedState(t *testing.T) string</c>。</summary>
    internal string SerializedState(IHarnessTest t)
    {
        var builder = new StringBuilder();
        var writer = new StringWriter(builder);
        _stateBaseline!._fsDiffer.BaselineFSWithDiff(writer);
        if (builder.ToString().Trim() == "")
        {
            builder.Clear();
        }

        PrintStateDiff(t, writer);
        return builder.ToString();
    }

    /// <summary>Go: <c>func (f *FourslashTest) printStateDiff(t *testing.T, w io.Writer)</c>。</summary>
    internal void PrintStateDiff(IHarnessTest t, TextWriter w)
    {
        if (!_stateBaseline!._isInitialized)
        {
            return;
        }

        var session = _client.Server.Session!;
        var snapshot = session.Snapshot();

        PrintProjectsDiff(t, snapshot, w);
        PrintOpenFilesDiff(t, snapshot, w);
        PrintConfigFileRegistryDiff(t, snapshot, w);
    }

    /// <summary>Go: <c>func (f *FourslashTest) printProjectsDiff(t *testing.T, snapshot *project.Snapshot, w io.Writer)</c>。</summary>
    internal void PrintProjectsDiff(IHarnessTest t, TypeScript.Project.Snapshot snapshot, TextWriter w)
    {
        var currentProjects = new Dictionary<string, TypeScript.Compiler.Program?>();
        var options = new DiffTableOptions { Indent = "  " };
        var projectsDiffTable = new DiffTableWriter("Projects");

        foreach (var project in snapshot.ProjectCollection.Projects())
        {
            var program = project.GetProgram();
            TypeScript.Compiler.Program? oldProgram = null;
            var projectId = project.ID().Value;
            currentProjects[projectId] = program;
            var projectChange = "";
            if (_stateBaseline!._serializedProjects.TryGetValue(projectId, out var existing))
            {
                oldProgram = existing;
                if (!ReferenceEquals(oldProgram, program))
                {
                    projectChange = "*modified*";
                    projectsDiffTable.SetHasChange();
                }
                else
                {
                    projectChange = "";
                }
            }
            else
            {
                projectChange = "*new*";
                projectsDiffTable.SetHasChange();
            }

            projectsDiffTable.Add(projectId, w =>
            {
                w.Write($"  [{project.ID()}] {projectChange}\n");
                var subDiff = new DiffTable(options);
                if (program is not null)
                {
                    foreach (var file in program.GetSourceFiles())
                    {
                        var fileDiff = "";
                        // No need to write "*new*" for files as its obvious
                        var fileName = file.FileName();
                        if (projectChange == "*modified*")
                        {
                            if (oldProgram is null)
                            {
                                if (!IsLibFile(fileName))
                                {
                                    fileDiff = "*new*";
                                }
                            }
                            else
                            {
                                var oldFile = oldProgram.GetSourceFileByPath(file.Path());
                                if (oldFile is null)
                                {
                                    fileDiff = "*new*";
                                }
                                else if (!ReferenceEquals(oldFile, file))
                                {
                                    fileDiff = "*modified*";
                                }
                            }
                        }

                        if (fileDiff != "" || !IsLibFile(fileName))
                        {
                            subDiff.Add(fileName, fileDiff);
                        }
                    }
                }

                if (!ReferenceEquals(oldProgram, program) && oldProgram is not null)
                {
                    foreach (var file in oldProgram.GetSourceFiles())
                    {
                        if (program is null || program.GetSourceFileByPath(file.Path()) is null)
                        {
                            subDiff.Add(file.FileName(), "*deleted*");
                        }
                    }
                }

                subDiff.Print(w, "");
            });
        }

        foreach (var (projectName, info) in _stateBaseline!._serializedProjects)
        {
            if (!currentProjects.ContainsKey(projectName))
            {
                projectsDiffTable.SetHasChange();
                projectsDiffTable.Add(projectName, w =>
                {
                    w.Write($"  [{projectName}] *deleted*\n");
                    var subDiff = new DiffTable(options);
                    if (info is not null)
                    {
                        foreach (var file in info.GetSourceFiles())
                        {
                            var fileName = file.FileName();
                            if (!IsLibFile(fileName))
                            {
                                subDiff.Add(fileName, "");
                            }
                        }
                    }

                    subDiff.Print(w, "");
                });
            }
        }

        _stateBaseline!._serializedProjects = currentProjects;
        projectsDiffTable.Print(w);
    }

    /// <summary>Go: <c>func (f *FourslashTest) printOpenFilesDiff(t *testing.T, snapshot *project.Snapshot, w io.Writer)</c>。</summary>
    internal void PrintOpenFilesDiff(IHarnessTest t, TypeScript.Project.Snapshot snapshot, TextWriter w)
    {
        var currentOpenFiles = new Dictionary<string, OpenFileInfo>();
        var filesDiffTable = new DiffTableWriter("Open Files");
        var options = new DiffTableOptions { Indent = "  ", SortKeys = true };
        foreach (var fileName in _openFiles)
        {
            var path = ToPath(fileName, "/", _vfs.UseCaseSensitiveFileNames());
            var defaultProject = snapshot.ProjectCollection.GetDefaultProject(path);
            var newFileInfo = new OpenFileInfo();
            if (defaultProject is not null)
            {
                newFileInfo.DefaultProjectName = defaultProject.ID().Value;
            }

            foreach (var project in snapshot.ProjectCollection.Projects())
            {
                if (project.GetProgram() is { } program && program.GetSourceFileByPath(path) is not null)
                {
                    newFileInfo.AllProjects.Add(project.ID().Value);
                }
            }

            newFileInfo.AllProjects.Sort(StringComparer.Ordinal);
            currentOpenFiles[fileName] = newFileInfo;
            var openFileChange = "";
            OpenFileInfo? oldFileInfo = null;
            if (_stateBaseline!._serializedOpenFiles.TryGetValue(fileName, out var existing))
            {
                oldFileInfo = existing;
                if (existing.DefaultProjectName != newFileInfo.DefaultProjectName ||
                    !existing.AllProjects.SequenceEqual(newFileInfo.AllProjects))
                {
                    openFileChange = "*modified*";
                    filesDiffTable.SetHasChange();
                }
                else
                {
                    openFileChange = "";
                }
            }
            else
            {
                openFileChange = "*new*";
                filesDiffTable.SetHasChange();
            }

            filesDiffTable.Add(fileName, w =>
            {
                w.Write($"  [{fileName}] {openFileChange}\n");
                PrintSlicesWithDiffTable(
                    w,
                    "",
                    [.. newFileInfo.AllProjects],
                    () => [.. oldFileInfo!.AllProjects],
                    options,
                    openFileChange,
                    projectName => projectName == newFileInfo.DefaultProjectName);
            });
        }

        foreach (var fileName in _stateBaseline!._serializedOpenFiles.Keys)
        {
            if (!currentOpenFiles.ContainsKey(fileName))
            {
                filesDiffTable.SetHasChange();
                filesDiffTable.Add(fileName, w => w.Write($"  [{fileName}] *closed*\n"));
            }
        }

        _stateBaseline!._serializedOpenFiles = currentOpenFiles;
        filesDiffTable.Print(w);
    }

    /// <summary>Go: <c>func (f *FourslashTest) printConfigFileRegistryDiff(t *testing.T, snapshot *project.Snapshot, w io.Writer)</c>。</summary>
    internal void PrintConfigFileRegistryDiff(IHarnessTest t, TypeScript.Project.Snapshot snapshot, TextWriter w)
    {
        var configFileRegistry = snapshot.ProjectCollection.ConfigFileRegistry();

        var configDiffsTable = new DiffTableWriter("Config");
        var configFileNamesDiffsTable = new DiffTableWriter("Config File Names");

        if (ReferenceEquals(_stateBaseline!._serializedConfigFileRegistry, configFileRegistry))
        {
            return;
        }

        var options = new DiffTableOptions { Indent = "    ", SortKeys = true };
        configFileRegistry.ForEachTestConfigEntry((path, entry) =>
        {
            var configChange = "";
            var oldEntry = _stateBaseline!._serializedConfigFileRegistry?.GetTestConfigEntry(path);
            if (oldEntry is null)
            {
                configChange = "*new*";
                configDiffsTable.SetHasChange();
            }
            // Go 的 `oldEntry != entry` 是两次新分配指针的比较（GetTestConfigEntry 每次返回新对象），恒为 true；
            // C# 的 TestConfigEntry.FromEntry 同样每次新建，故用 ReferenceEquals 表达同一语义。
            else if (!ReferenceEquals(oldEntry, entry))
            {
                if (!AreIterSeqEqual(oldEntry.RetainingProjects, entry.RetainingProjects) ||
                    !AreIterSeqEqual(oldEntry.RetainingOpenFiles, entry.RetainingOpenFiles) ||
                    !AreIterSeqEqual(oldEntry.RetainingConfigs, entry.RetainingConfigs))
                {
                    configChange = "*modified*";
                    configDiffsTable.SetHasChange();
                }
            }

            configDiffsTable.Add(path.ToString(), w =>
            {
                w.Write($"  [{entry.FileName}] {configChange}\n");
                // Print the details of the config entry
                var retainingProjectsModified = "";
                var retainingOpenFilesModified = "";
                var retainingConfigsModified = "";
                if (configChange == "*modified*")
                {
                    if (!AreIterSeqEqual(entry.RetainingProjects, oldEntry!.RetainingProjects))
                    {
                        retainingProjectsModified = " *modified*";
                    }

                    if (!AreIterSeqEqual(entry.RetainingOpenFiles, oldEntry.RetainingOpenFiles))
                    {
                        retainingOpenFilesModified = " *modified*";
                    }

                    if (!AreIterSeqEqual(entry.RetainingConfigs, oldEntry.RetainingConfigs))
                    {
                        retainingConfigsModified = " *modified*";
                    }
                }

                PrintStringIterSeqWithDiffTable(w, "RetainingProjects:" + retainingProjectsModified, entry.RetainingProjects, () => oldEntry!.RetainingProjects, options, configChange);
                PrintStringIterSeqWithDiffTable(w, "RetainingOpenFiles:" + retainingOpenFilesModified, entry.RetainingOpenFiles, () => oldEntry!.RetainingOpenFiles, options, configChange);
                PrintStringIterSeqWithDiffTable(w, "RetainingConfigs:" + retainingConfigsModified, entry.RetainingConfigs, () => oldEntry!.RetainingConfigs, options, configChange);
            });
        });

        configFileRegistry.ForEachTestConfigFileNamesEntry((path, entry) =>
        {
            var configFileNamesChange = "";
            var oldEntry = _stateBaseline!._serializedConfigFileRegistry?.GetTestConfigFileNamesEntry(path);
            if (oldEntry is null)
            {
                configFileNamesChange = "*new*";
                configFileNamesDiffsTable.SetHasChange();
            }
            else if (oldEntry.NearestConfigFileName != entry.NearestConfigFileName ||
                !MapsEqual(oldEntry.Ancestors, entry.Ancestors))
            {
                configFileNamesChange = "*modified*";
                configFileNamesDiffsTable.SetHasChange();
            }

            configFileNamesDiffsTable.Add(path.ToString(), w =>
            {
                w.Write($"  [{path}] {configFileNamesChange}\n");
                var nearestConfigFileNameModified = "";
                var ancestorDiffModified = "";
                if (configFileNamesChange == "*modified*")
                {
                    if (oldEntry!.NearestConfigFileName != entry.NearestConfigFileName)
                    {
                        nearestConfigFileNameModified = " *modified*";
                    }

                    if (!MapsEqual(oldEntry.Ancestors, entry.Ancestors))
                    {
                        ancestorDiffModified = " *modified*";
                    }
                }

                w.Write($"    NearestConfigFileName: {entry.NearestConfigFileName}{nearestConfigFileNameModified}\n");
                var ancestorDiff = new DiffTable(options);
                foreach (var (config, ancestorOfConfig) in Entries(entry.Ancestors))
                {
                    var ancestorChange = "";
                    if (configFileNamesChange == "*modified*")
                    {
                        if (oldEntry!.Ancestors is { } oldAncestors && oldAncestors.TryGetValue(config, out var oldConfigFileName))
                        {
                            if (oldConfigFileName != ancestorOfConfig)
                            {
                                ancestorChange = "*modified*";
                            }
                        }
                        else
                        {
                            ancestorChange = "*new*";
                        }
                    }

                    ancestorDiff.Add(config, $"{ancestorOfConfig} {ancestorChange}");
                }

                if (configFileNamesChange == "*modified*")
                {
                    foreach (var (ancestorPath, oldConfigFileName) in Entries(oldEntry!.Ancestors))
                    {
                        if (entry.Ancestors is null || !entry.Ancestors.ContainsKey(ancestorPath))
                        {
                            ancestorDiff.Add(ancestorPath, oldConfigFileName + " *deleted*");
                        }
                    }
                }

                ancestorDiff.Print(w, "Ancestors:" + ancestorDiffModified);
            });
        });

        _stateBaseline!._serializedConfigFileRegistry?.ForEachTestConfigEntry((path, entry) =>
        {
            if (configFileRegistry.GetTestConfigEntry(path) is null)
            {
                configDiffsTable.SetHasChange();
                configDiffsTable.Add(path.ToString(), w => w.Write($"  [{entry.FileName}] *deleted*\n"));
            }
        });
        _stateBaseline!._serializedConfigFileRegistry?.ForEachTestConfigFileNamesEntry((path, entry) =>
        {
            if (configFileRegistry.GetTestConfigFileNamesEntry(path) is null)
            {
                configFileNamesDiffsTable.SetHasChange();
                configFileNamesDiffsTable.Add(path.ToString(), w => w.Write($"  [{path}] *deleted*\n"));
            }
        });
        _stateBaseline!._serializedConfigFileRegistry = configFileRegistry;
        configDiffsTable.Print(w);
        configFileNamesDiffsTable.Print(w);
    }

    /// <summary>Go: <c>func areIterSeqEqual[T ~string](a, b iter.Seq[T]) bool</c>。</summary>
    private static bool AreIterSeqEqual<T>(IEnumerable<T> a, IEnumerable<T> b)
    {
        var aSlice = a.Select(static x => x!.ToString() ?? "").ToArray();
        var bSlice = b.Select(static x => x!.ToString() ?? "").ToArray();
        Array.Sort(aSlice, StringComparer.Ordinal);
        Array.Sort(bSlice, StringComparer.Ordinal);
        return aSlice.SequenceEqual(bSlice, StringComparer.Ordinal);
    }

    /// <summary>
    /// Go: <c>func printSlicesWithDiffTable(w io.Writer, header string, newSlice []string, getOldSlice func() []string,
    /// options diffTableOptions, topChange string, isDefault func(entry string) bool)</c>。
    /// </summary>
    private static void PrintSlicesWithDiffTable(TextWriter w, string header, string[] newSlice, Func<string[]> getOldSlice, DiffTableOptions options, string topChange, Func<string, bool>? isDefault)
    {
        string[]? oldSlice = null;
        if (topChange == "*modified*")
        {
            oldSlice = getOldSlice();
        }

        var table = new DiffTable(options);
        foreach (var entry in newSlice)
        {
            var entryChange = "";
            if (isDefault is not null && isDefault(entry))
            {
                entryChange = "(default) ";
            }

            if (topChange == "*modified*" && Array.IndexOf(oldSlice!, entry) < 0)
            {
                entryChange = "*new*";
            }

            table.Add(entry, entryChange);
        }

        if (topChange == "*modified*")
        {
            foreach (var entry in oldSlice!)
            {
                if (Array.IndexOf(newSlice, entry) < 0)
                {
                    table.Add(entry, "*deleted*");
                }
            }
        }

        table.Print(w, header);
    }

    /// <summary>Go: <c>func sliceFromIterSeqString[T ~string](seq iter.Seq[T]) []string</c>。</summary>
    private static string[] SliceFromIterSeqString<T>(IEnumerable<T> seq)
    {
        var result = new List<string>();
        foreach (var path in seq)
        {
            result.Add(path!.ToString() ?? "");
        }

        result.Sort(StringComparer.Ordinal);
        return [.. result];
    }

    /// <summary>
    /// Go: <c>func printStringIterSeqWithDiffTable[T ~string](w io.Writer, header string, newIterSeq iter.Seq[T],
    /// getOldIterSeq func() iter.Seq[T], options diffTableOptions, topChange string)</c>。
    /// </summary>
    private static void PrintStringIterSeqWithDiffTable<T>(TextWriter w, string header, IEnumerable<T> newIterSeq, Func<IEnumerable<T>> getOldIterSeq, DiffTableOptions options, string topChange)
    {
        PrintSlicesWithDiffTable(
            w,
            header,
            SliceFromIterSeqString(newIterSeq),
            () => SliceFromIterSeqString(getOldIterSeq()),
            options,
            topChange,
            null);
    }

    /// <summary>Go: <c>maps.Equal</c>（nil map 视作空 map，与空 map 相等）。</summary>
    private static bool MapsEqual(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        var left = a ?? new Dictionary<string, string>();
        var right = b ?? new Dictionary<string, string>();
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Go: 遍历 nil map 不迭代（`for k, v := range m`）。</summary>
    private static IEnumerable<KeyValuePair<string, string>> Entries(Dictionary<string, string>? map)
    {
        return map ?? Enumerable.Empty<KeyValuePair<string, string>>();
    }
}
