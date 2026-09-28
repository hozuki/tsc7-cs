// ported from: internal/execute/tsctests/readablebuildinfo.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 struct tag（`json:"x,omitzero"`）在 C# 没有反射可用（docs/json.md §2：结构体字段级映射要求类型自己
//    实现 `IJsonMarshaler`）→ 本文件逐字段手写 `MarshalJSONTo`，**字段顺序与 Go 的声明顺序逐字对应**，
//    omitzero 语义按"Go 的零值"判定（bool=false、int=0、string=""、slice/map/指针=nil）。
// 2) `json.MarshalIndent(&readable, "", "  ")` → `Json.MarshalIndent(readable, "", "  ")`；
//    `panic(...)` → `Debug.Fail(...)`。
// 3) `len(buildInfoText)`（Go 的字节长度）= `Encoding.UTF8.GetByteCount(buildInfoText)`。
// 4) Go 的 `[]*T` 元素可空（emitDiagnosticsPerFile）→ C# `List<T?>` + `!`（与 BuildInfo.cs 的同款处理）。
// 5) **未移植**：Go 给 `readableBuildInfoDiagnosticsOfFile`/`…SemanticDiagnostic`/`…FilePendingEmit`/
//    `…ResolvedRoot` 写的 `UnmarshalJSON`。它们在 Go 侧只有 `json.Unmarshal` 会用到，而 tsctests 里这些
//    可读结构**只被生产、从不被解析**（唯一消费者是 `testFs.WriteFile` 落的 `.readable.baseline.txt`）。
//    移植它们等于给死代码写反射替代物。
// 6) `core.Map`/`core.MapIndex` → `Select`/`Select((x, i) => …)`；`core.IfElse` → 三元。
namespace TypeScript.Harness.TscTests;

using global::System;
using global::System.Collections.Generic;
using global::System.Globalization;
using global::System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Execute.Incremental;
using TypeScript.Json;

using static TypeScript.Core.Core;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.Json.Json;

using BinDiag = TypeScript.Execute.Incremental.BuildInfoDiagnostic;
using BinDiagnosticsOfFile = TypeScript.Execute.Incremental.BuildInfoDiagnosticsOfFile;
using BinEmitSignature = TypeScript.Execute.Incremental.BuildInfoEmitSignature;
using BinFileInfo = TypeScript.Execute.Incremental.BuildInfoFileInfo;
using BinFilePendingEmit = TypeScript.Execute.Incremental.BuildInfoFilePendingEmit;
using BinResolvedRoot = TypeScript.Execute.Incremental.BuildInfoResolvedRoot;
using BinRoot = TypeScript.Execute.Incremental.BuildInfoRoot;
using BinSemanticDiagnostic = TypeScript.Execute.Incremental.BuildInfoSemanticDiagnostic;
using Encoder = TypeScript.Json.Encoder;

/// <summary>Go: tsctests/readablebuildinfo.go 的包级入口。</summary>
public static class ReadableBuildInfo
{
    /// <summary>Go: <c>func toReadableBuildInfo(buildInfo *incremental.BuildInfo, buildInfoText string) string</c>。</summary>
    public static string ToReadableBuildInfo(BuildInfo buildInfo, string buildInfoText)
    {
        var readable = new ReadableBuildInfoModel
        {
            BuildInfo = buildInfo,
            Version = buildInfo.Version,
            Errors = buildInfo.Errors,
            CheckPending = buildInfo.CheckPending,
            FileNames = buildInfo.FileNames,
            Options = buildInfo.Options,
            LatestChangedDtsFile = buildInfo.LatestChangedDtsFile,
            SemanticErrors = buildInfo.SemanticErrors,
            PackageJsons = buildInfo.PackageJsons,
            MissingPackageJsons = buildInfo.MissingPackageJsons,
            Size = Encoding.UTF8.GetByteCount(buildInfoText),
        };
        readable.SetFileInfos();
        readable.SetRoot();
        readable.SetFileIdsList();
        readable.SetReferencedMap();
        readable.SetChangeFileSet();
        readable.SetSemanticDiagnostics();
        readable.SetEmitDiagnostics();
        readable.SetAffectedFilesPendingEmit();
        readable.SetEmitSignatures();
        readable.SetResolvedRoot();
        return Encoding.UTF8.GetString(MarshalIndent(readable, "", "  "));
    }
}

/// <summary>Go: <c>type readableBuildInfo struct</c>（readablebuildinfo.go:16-45）。</summary>
public sealed class ReadableBuildInfoModel : IJsonMarshaler
{
    /// <summary>Go: <c>buildInfo *incremental.BuildInfo</c>（未导出，不进 JSON）。</summary>
    public BuildInfo BuildInfo = null!;

    public string Version = "";
    public bool Errors;
    public bool CheckPending;
    public List<ReadableBuildInfoRoot>? Root;
    public List<string>? PackageJsons;
    public List<string>? MissingPackageJsons;
    public List<string>? FileNames;
    public List<ReadableBuildInfoFileInfo>? FileInfos;
    public List<List<string>>? FileIdsList;
    public OrderedMap<string, object?>? Options;
    public OrderedMap<string, string[]>? ReferencedMap;
    public List<ReadableBuildInfoSemanticDiagnostic>? SemanticDiagnosticsPerFile;
    public List<ReadableBuildInfoDiagnosticsOfFile>? EmitDiagnosticsPerFile;
    public List<string>? ChangeFileSet;
    public List<ReadableBuildInfoFilePendingEmit>? AffectedFilesPendingEmit;
    public string LatestChangedDtsFile = "";
    public List<ReadableBuildInfoEmitSignature>? EmitSignatures;
    public List<ReadableBuildInfoResolvedRoot>? ResolvedRoot;
    public int Size;
    public bool SemanticErrors;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        WriteString(encoder, "version", Version);
        WriteBool(encoder, "errors", Errors);
        WriteBool(encoder, "checkPending", CheckPending);
        WriteArray(encoder, "root", Root, static (enc, item) => item.MarshalJSONTo(enc));
        WriteStringArray(encoder, "packageJsons", PackageJsons);
        WriteStringArray(encoder, "missingPackageJsons", MissingPackageJsons);
        WriteStringArray(encoder, "fileNames", FileNames);
        WriteArray(encoder, "fileInfos", FileInfos, static (enc, item) => item.MarshalJSONTo(enc));
        if (FileIdsList is not null)
        {
            encoder.WriteToken(JsonToken.String("fileIdsList"));
            encoder.WriteToken(BeginArray);
            foreach (var ids in FileIdsList)
            {
                encoder.WriteToken(BeginArray);
                foreach (var id in ids)
                {
                    encoder.WriteToken(JsonToken.String(id));
                }

                encoder.WriteToken(EndArray);
            }

            encoder.WriteToken(EndArray);
        }

        if (Options is not null)
        {
            encoder.WriteToken(JsonToken.String("options"));
            Options.MarshalJSONTo(encoder);
        }

        if (ReferencedMap is not null)
        {
            encoder.WriteToken(JsonToken.String("referencedMap"));
            ReferencedMap.MarshalJSONTo(encoder);
        }

        WriteArray(encoder, "semanticDiagnosticsPerFile", SemanticDiagnosticsPerFile, static (enc, item) => item.MarshalJSONTo(enc));
        WriteArray(encoder, "emitDiagnosticsPerFile", EmitDiagnosticsPerFile, static (enc, item) => item.MarshalJSONTo(enc));
        WriteStringArray(encoder, "changeFileSet", ChangeFileSet);
        WriteArray(encoder, "affectedFilesPendingEmit", AffectedFilesPendingEmit, static (enc, item) => item.MarshalJSONTo(enc));
        WriteString(encoder, "latestChangedDtsFile", LatestChangedDtsFile);
        WriteArray(encoder, "emitSignatures", EmitSignatures, static (enc, item) => item.MarshalJSONTo(enc));
        WriteArray(encoder, "resolvedRoot", ResolvedRoot, static (enc, item) => item.MarshalJSONTo(enc));
        if (Size != 0)
        {
            encoder.WriteToken(JsonToken.String("size"));
            encoder.WriteToken(JsonToken.Number(Size.ToString(CultureInfo.InvariantCulture)));
        }

        WriteBool(encoder, "semanticErrors", SemanticErrors);
        encoder.WriteToken(EndObject);
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) toFilePath(fileId incremental.BuildInfoFileId) string</c>。</summary>
    public string ToFilePath(BuildInfoFileId fileId) => BuildInfo.FileNames![(int)fileId - 1];

    /// <summary>Go: <c>func (r *readableBuildInfo) toFilePathSet(fileIdListId incremental.BuildInfoFileIdListId) []string</c>。</summary>
    public List<string> ToFilePathSet(BuildInfoFileIdListId fileIdListId) => FileIdsList![(int)fileIdListId - 1];

    /// <summary>Go: <c>func (r *readableBuildInfo) toReadableBuildInfoDiagnostic(diagnostics []*incremental.BuildInfoDiagnostic) []*readableBuildInfoDiagnostic</c>。</summary>
    public List<ReadableBuildInfoDiagnostic>? ToReadableBuildInfoDiagnostic(List<BinDiag>? diagnostics)
    {
        if (diagnostics is null)
        {
            return null;
        }

        var result = new List<ReadableBuildInfoDiagnostic>(diagnostics.Count);
        foreach (var d in diagnostics)
        {
            var file = "";
            if (d.File != 0)
            {
                file = ToFilePath(d.File);
            }

            result.Add(new ReadableBuildInfoDiagnostic
            {
                File = file,
                NoFile = d.NoFile,
                Pos = d.Pos,
                End = d.End,
                Code = d.Code,
                Category = d.Category,
                MessageKey = d.MessageKey.Value,
                MessageArgs = d.MessageArgs,
                MessageChain = ToReadableBuildInfoDiagnostic(d.MessageChain),
                RelatedInformation = ToReadableBuildInfoDiagnostic(d.RelatedInformation),
                ReportsUnnecessary = d.ReportsUnnecessary,
                ReportsDeprecated = d.ReportsDeprecated,
                SkippedOnNoEmit = d.SkippedOnNoEmit,
                RepopulateInfo = ToReadableBuildInfoRepopulateInfo(d.RepopulateInfo),
            });
        }

        return result;
    }

    /// <summary>Go: <c>func toReadableBuildInfoRepopulateInfo(info *incremental.BuildInfoRepopulateInfo) *readableBuildInfoRepopulateInfo</c>。</summary>
    public static ReadableBuildInfoRepopulateInfo? ToReadableBuildInfoRepopulateInfo(BuildInfoRepopulateInfo? info)
    {
        if (info is null)
        {
            return null;
        }

        return new ReadableBuildInfoRepopulateInfo
        {
            Kind = info.Kind,
            ModuleReference = info.ModuleReference,
            Mode = info.Mode,
            PackageName = info.PackageName,
        };
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) toReadableBuildInfoDiagnosticsOfFile(diagnostics *incremental.BuildInfoDiagnosticsOfFile) *readableBuildInfoDiagnosticsOfFile</c>。</summary>
    public ReadableBuildInfoDiagnosticsOfFile ToReadableBuildInfoDiagnosticsOfFile(BinDiagnosticsOfFile diagnostics)
    {
        return new ReadableBuildInfoDiagnosticsOfFile
        {
            File = ToFilePath(diagnostics.FileId),
            Diagnostics = ToReadableBuildInfoDiagnostic(diagnostics.Diagnostics),
        };
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setFileInfos()</c>。</summary>
    public void SetFileInfos()
    {
        if (BuildInfo.FileInfos is null)
        {
            return;
        }

        var list = new List<ReadableBuildInfoFileInfo>(BuildInfo.FileInfos.Count);
        for (var index = 0; index < BuildInfo.FileInfos.Count; index++)
        {
            var original = BuildInfo.FileInfos[index];
            var fileInfo = original.GetFileInfo()!;

            // Dont set original for string encoding
            BinFileInfo? originalForJson = original;
            if (original.HasSignature())
            {
                originalForJson = null;
            }

            list.Add(new ReadableBuildInfoFileInfo
            {
                FileName = ToFilePath(index + 1),
                Version = fileInfo.Version(),
                Signature = fileInfo.Signature(),
                AffectsGlobalScope = fileInfo.AffectsGlobalScope(),
                ImpliedNodeFormat = fileInfo.ImpliedNodeFormat().String(),
                Original = originalForJson,
            });
        }

        FileInfos = list;
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setRoot()</c>。</summary>
    public void SetRoot()
    {
        if (BuildInfo.Root is null)
        {
            return;
        }

        var list = new List<ReadableBuildInfoRoot>(BuildInfo.Root.Count);
        foreach (var original in BuildInfo.Root)
        {
            List<string> files;
            if (original.NonIncremental != "")
            {
                files = [original.NonIncremental];
            }
            else if (original.End == 0)
            {
                files = [ToFilePath(original.Start)];
            }
            else
            {
                files = new List<string>((int)original.End - (int)original.Start + 1);
                for (var i = (int)original.Start; i <= (int)original.End; i++)
                {
                    files.Add(ToFilePath(i));
                }
            }

            list.Add(new ReadableBuildInfoRoot
            {
                Files = files,
                Original = original,
            });
        }

        Root = list;
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setFileIdsList()</c>。</summary>
    public void SetFileIdsList()
    {
        if (BuildInfo.FileIdsList is null)
        {
            return;
        }

        var list = new List<List<string>>(BuildInfo.FileIdsList.Count);
        foreach (var ids in BuildInfo.FileIdsList)
        {
            var mapped = new List<string>(ids.Count);
            foreach (var id in ids)
            {
                mapped.Add(ToFilePath(id));
            }

            list.Add(mapped);
        }

        FileIdsList = list;
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setReferencedMap()</c>。</summary>
    public void SetReferencedMap()
    {
        if (BuildInfo.ReferencedMap is null)
        {
            return;
        }

        var map = new OrderedMap<string, string[]>();
        foreach (var entry in BuildInfo.ReferencedMap)
        {
            map.Set(ToFilePath(entry.FileId), [.. ToFilePathSet(entry.FileIdListId)]);
        }

        ReferencedMap = map;
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setChangeFileSet()</c>。</summary>
    public void SetChangeFileSet()
    {
        if (BuildInfo.ChangeFileSet is null)
        {
            return;
        }

        var list = new List<string>(BuildInfo.ChangeFileSet.Count);
        foreach (var fileId in BuildInfo.ChangeFileSet)
        {
            list.Add(ToFilePath(fileId));
        }

        ChangeFileSet = list;
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setSemanticDiagnostics()</c>。</summary>
    public void SetSemanticDiagnostics()
    {
        if (BuildInfo.SemanticDiagnosticsPerFile is null)
        {
            return;
        }

        var list = new List<ReadableBuildInfoSemanticDiagnostic>(BuildInfo.SemanticDiagnosticsPerFile.Count);
        foreach (var diagnostics in BuildInfo.SemanticDiagnosticsPerFile)
        {
            if (diagnostics.FileId != 0)
            {
                list.Add(new ReadableBuildInfoSemanticDiagnostic { File = ToFilePath(diagnostics.FileId) });
            }
            else
            {
                list.Add(new ReadableBuildInfoSemanticDiagnostic
                {
                    Diagnostics = ToReadableBuildInfoDiagnosticsOfFile(diagnostics.Diagnostics!),
                });
            }
        }

        SemanticDiagnosticsPerFile = list;
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setEmitDiagnostics()</c>。</summary>
    public void SetEmitDiagnostics()
    {
        if (BuildInfo.EmitDiagnosticsPerFile is null)
        {
            return;
        }

        var list = new List<ReadableBuildInfoDiagnosticsOfFile>(BuildInfo.EmitDiagnosticsPerFile.Count);
        foreach (var diagnostics in BuildInfo.EmitDiagnosticsPerFile)
        {
            list.Add(ToReadableBuildInfoDiagnosticsOfFile(diagnostics!));
        }

        EmitDiagnosticsPerFile = list;
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setAffectedFilesPendingEmit()</c>。</summary>
    public void SetAffectedFilesPendingEmit()
    {
        if (BuildInfo.AffectedFilesPendingEmit is null)
        {
            return;
        }

        var fullEmitKind = GetFileEmitKind(BuildInfo.GetCompilerOptions(""));
        var list = new List<ReadableBuildInfoFilePendingEmit>(BuildInfo.AffectedFilesPendingEmit.Count);
        foreach (var pendingEmit in BuildInfo.AffectedFilesPendingEmit)
        {
            var emitKind = pendingEmit.EmitKind == 0 ? fullEmitKind : pendingEmit.EmitKind;
            list.Add(new ReadableBuildInfoFilePendingEmit
            {
                File = ToFilePath(pendingEmit.FileId),
                EmitKind = ToReadableFileEmitKind(emitKind),
                Original = pendingEmit,
            });
        }

        AffectedFilesPendingEmit = list;
    }

    /// <summary>Go: <c>func toReadableFileEmitKind(fileEmitKind incremental.FileEmitKind) string</c>。</summary>
    public static string ToReadableFileEmitKind(FileEmitKind fileEmitKind)
    {
        var builder = new StringBuilder();
        void AddFlags(string flags)
        {
            if (builder.Length == 0)
            {
                builder.Append(flags);
            }
            else
            {
                builder.Append('|');
                builder.Append(flags);
            }
        }

        if (fileEmitKind != 0)
        {
            if ((fileEmitKind & FileEmitKind.Js) != 0)
            {
                AddFlags("Js");
            }

            if ((fileEmitKind & FileEmitKind.JsMap) != 0)
            {
                AddFlags("JsMap");
            }

            if ((fileEmitKind & FileEmitKind.JsInlineMap) != 0)
            {
                AddFlags("JsInlineMap");
            }

            if ((fileEmitKind & FileEmitKind.Dts) == FileEmitKind.Dts)
            {
                AddFlags("Dts");
            }
            else
            {
                if ((fileEmitKind & FileEmitKind.DtsEmit) != 0)
                {
                    AddFlags("DtsEmit");
                }

                if ((fileEmitKind & FileEmitKind.DtsErrors) != 0)
                {
                    AddFlags("DtsErrors");
                }
            }

            if ((fileEmitKind & FileEmitKind.DtsMap) != 0)
            {
                AddFlags("DtsMap");
            }
        }

        if (builder.Length != 0)
        {
            return builder.ToString();
        }

        return "None";
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setEmitSignatures()</c>。</summary>
    public void SetEmitSignatures()
    {
        if (BuildInfo.EmitSignatures is null)
        {
            return;
        }

        var list = new List<ReadableBuildInfoEmitSignature>(BuildInfo.EmitSignatures.Count);
        foreach (var signature in BuildInfo.EmitSignatures)
        {
            list.Add(new ReadableBuildInfoEmitSignature
            {
                File = ToFilePath(signature.FileId),
                Signature = signature.Signature,
                DiffersOnlyInDtsMap = signature.DiffersOnlyInDtsMap,
                DiffersInOptions = signature.DiffersInOptions,
                Original = signature,
            });
        }

        EmitSignatures = list;
    }

    /// <summary>Go: <c>func (r *readableBuildInfo) setResolvedRoot()</c>。</summary>
    public void SetResolvedRoot()
    {
        if (BuildInfo.ResolvedRoot is null)
        {
            return;
        }

        var list = new List<ReadableBuildInfoResolvedRoot>(BuildInfo.ResolvedRoot.Count);
        foreach (var original in BuildInfo.ResolvedRoot)
        {
            list.Add(new ReadableBuildInfoResolvedRoot
            {
                Resolved = ToFilePath(original.Resolved),
                Root = ToFilePath(original.Root),
            });
        }

        ResolvedRoot = list;
    }

    // ---- Go 的 `omitzero` 语义辅助（见文件头偏离 1）----

    internal static void WriteString(Encoder encoder, string name, string value)
    {
        if (value.Length != 0)
        {
            encoder.WriteToken(JsonToken.String(name));
            encoder.WriteToken(JsonToken.String(value));
        }
    }

    internal static void WriteBool(Encoder encoder, string name, bool value)
    {
        if (value)
        {
            encoder.WriteToken(JsonToken.String(name));
            encoder.WriteToken(JsonToken.Bool(true));
        }
    }

    internal static void WriteArray<T>(Encoder encoder, string name, List<T>? items, Action<Encoder, T> writeItem)
    {
        if (items is null)
        {
            return;
        }

        encoder.WriteToken(JsonToken.String(name));
        encoder.WriteToken(BeginArray);
        foreach (var item in items)
        {
            writeItem(encoder, item);
        }

        encoder.WriteToken(EndArray);
    }

    internal static void WriteStringArray(Encoder encoder, string name, List<string>? items)
    {
        if (items is null)
        {
            return;
        }

        encoder.WriteToken(JsonToken.String(name));
        encoder.WriteToken(BeginArray);
        foreach (var item in items)
        {
            encoder.WriteToken(JsonToken.String(item));
        }

        encoder.WriteToken(EndArray);
    }
}

/// <summary>Go: <c>type readableBuildInfoRoot struct</c>。</summary>
public sealed class ReadableBuildInfoRoot : IJsonMarshaler
{
    public List<string>? Files;
    public BinRoot? Original;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        ReadableBuildInfoModel.WriteStringArray(encoder, "files", Files);
        if (Original is not null)
        {
            encoder.WriteToken(JsonToken.String("original"));
            Original.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndObject);
    }
}

/// <summary>Go: <c>type readableBuildInfoFileInfo struct</c>。</summary>
public sealed class ReadableBuildInfoFileInfo : IJsonMarshaler
{
    public string FileName = "";
    public string Version = "";
    public string Signature = "";
    public bool AffectsGlobalScope;
    public string ImpliedNodeFormat = "";
    public BinFileInfo? Original;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        ReadableBuildInfoModel.WriteString(encoder, "fileName", FileName);
        ReadableBuildInfoModel.WriteString(encoder, "version", Version);
        ReadableBuildInfoModel.WriteString(encoder, "signature", Signature);
        ReadableBuildInfoModel.WriteBool(encoder, "affectsGlobalScope", AffectsGlobalScope);
        ReadableBuildInfoModel.WriteString(encoder, "impliedNodeFormat", ImpliedNodeFormat);
        if (Original is not null)
        {
            encoder.WriteToken(JsonToken.String("original"));
            Original.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndObject);
    }
}

/// <summary>Go: <c>type readableBuildInfoDiagnostic struct</c>。</summary>
public sealed class ReadableBuildInfoDiagnostic : IJsonMarshaler
{
    public string File = "";
    public bool NoFile;
    public int Pos;
    public int End;
    public int Code;
    public Category Category;
    public string MessageKey = "";
    public List<string>? MessageArgs;
    public List<ReadableBuildInfoDiagnostic>? MessageChain;
    public List<ReadableBuildInfoDiagnostic>? RelatedInformation;
    public bool ReportsUnnecessary;
    public bool ReportsDeprecated;
    public bool SkippedOnNoEmit;
    public ReadableBuildInfoRepopulateInfo? RepopulateInfo;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        ReadableBuildInfoModel.WriteString(encoder, "file", File);
        ReadableBuildInfoModel.WriteBool(encoder, "noFile", NoFile);
        if (Pos != 0)
        {
            encoder.WriteToken(JsonToken.String("pos"));
            encoder.WriteToken(JsonToken.Number(Pos.ToString(CultureInfo.InvariantCulture)));
        }

        if (End != 0)
        {
            encoder.WriteToken(JsonToken.String("end"));
            encoder.WriteToken(JsonToken.Number(End.ToString(CultureInfo.InvariantCulture)));
        }

        if (Code != 0)
        {
            encoder.WriteToken(JsonToken.String("code"));
            encoder.WriteToken(JsonToken.Number(Code.ToString(CultureInfo.InvariantCulture)));
        }

        if (Category != 0)
        {
            encoder.WriteToken(JsonToken.String("category"));
            encoder.WriteToken(JsonToken.Number(((int)Category).ToString(CultureInfo.InvariantCulture)));
        }

        ReadableBuildInfoModel.WriteString(encoder, "messageKey", MessageKey);
        ReadableBuildInfoModel.WriteStringArray(encoder, "messageArgs", MessageArgs);
        ReadableBuildInfoModel.WriteArray(encoder, "messageChain", MessageChain, static (enc, item) => item.MarshalJSONTo(enc));
        ReadableBuildInfoModel.WriteArray(encoder, "relatedInformation", RelatedInformation, static (enc, item) => item.MarshalJSONTo(enc));
        ReadableBuildInfoModel.WriteBool(encoder, "reportsUnnecessary", ReportsUnnecessary);
        ReadableBuildInfoModel.WriteBool(encoder, "reportsDeprecated", ReportsDeprecated);
        ReadableBuildInfoModel.WriteBool(encoder, "skippedOnNoEmit", SkippedOnNoEmit);
        if (RepopulateInfo is not null)
        {
            encoder.WriteToken(JsonToken.String("repopulateInfo"));
            RepopulateInfo.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndObject);
    }
}

/// <summary>Go: <c>type readableBuildInfoRepopulateInfo struct</c>。</summary>
public sealed class ReadableBuildInfoRepopulateInfo : IJsonMarshaler
{
    public RepopulateDiagnosticKind Kind;
    public string ModuleReference = "";
    public ModuleKind Mode;
    public string PackageName = "";

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);

        // Go 的 tag 是 `json:"kind"`（没有 omitzero）→ 恒写出。
        encoder.WriteToken(JsonToken.String("kind"));
        encoder.WriteToken(JsonToken.Number(((int)Kind).ToString(CultureInfo.InvariantCulture)));
        ReadableBuildInfoModel.WriteString(encoder, "moduleReference", ModuleReference);
        if (Mode != 0)
        {
            encoder.WriteToken(JsonToken.String("mode"));
            encoder.WriteToken(JsonToken.Number(((int)Mode).ToString(CultureInfo.InvariantCulture)));
        }

        ReadableBuildInfoModel.WriteString(encoder, "packageName", PackageName);
        encoder.WriteToken(EndObject);
    }
}

/// <summary>Go: <c>type readableBuildInfoDiagnosticsOfFile struct</c>（自定义 MarshalJSON：<c>[file, diagnostics]</c>）。</summary>
public sealed class ReadableBuildInfoDiagnosticsOfFile : IJsonMarshaler
{
    public string File = "";
    public List<ReadableBuildInfoDiagnostic>? Diagnostics;

    public void MarshalJSONTo(Encoder encoder)
    {
        // Go: json.Marshal([]any{r.file, r.diagnostics})
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.String(File));
        if (Diagnostics is null)
        {
            encoder.WriteToken(Null);
        }
        else
        {
            encoder.WriteToken(BeginArray);
            foreach (var diagnostic in Diagnostics)
            {
                diagnostic.MarshalJSONTo(encoder);
            }

            encoder.WriteToken(EndArray);
        }

        encoder.WriteToken(EndArray);
    }
}

/// <summary>Go: <c>type readableBuildInfoSemanticDiagnostic struct</c>（自定义 MarshalJSON：字符串或 file+diagnostics）。</summary>
public sealed class ReadableBuildInfoSemanticDiagnostic : IJsonMarshaler
{
    /// <summary>Go: <c>file string</c>。File is not in changedSet and still doesnt have cached diagnostics。</summary>
    public string File = "";

    /// <summary>Go: <c>diagnostics *readableBuildInfoDiagnosticsOfFile</c>。</summary>
    public ReadableBuildInfoDiagnosticsOfFile? Diagnostics;

    public void MarshalJSONTo(Encoder encoder)
    {
        if (File.Length != 0)
        {
            encoder.WriteToken(JsonToken.String(File));
            return;
        }

        Diagnostics!.MarshalJSONTo(encoder);
    }
}

/// <summary>Go: <c>type readableBuildInfoFilePendingEmit struct</c>（自定义 MarshalJSON：<c>[file, emitKind, original]</c>）。</summary>
public sealed class ReadableBuildInfoFilePendingEmit : IJsonMarshaler
{
    public string File = "";
    public string EmitKind = "";
    public BinFilePendingEmit? Original;

    public void MarshalJSONTo(Encoder encoder)
    {
        // Go: json.Marshal([]any{b.file, b.emitKind, b.original})
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.String(File));
        encoder.WriteToken(JsonToken.String(EmitKind));
        if (Original is null)
        {
            encoder.WriteToken(Null);
        }
        else
        {
            Original.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndArray);
    }
}

/// <summary>Go: <c>type readableBuildInfoEmitSignature struct</c>。</summary>
public sealed class ReadableBuildInfoEmitSignature : IJsonMarshaler
{
    public string File = "";
    public string Signature = "";
    public bool DiffersOnlyInDtsMap;
    public bool DiffersInOptions;
    public BinEmitSignature? Original;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        ReadableBuildInfoModel.WriteString(encoder, "file", File);
        ReadableBuildInfoModel.WriteString(encoder, "signature", Signature);
        ReadableBuildInfoModel.WriteBool(encoder, "differsOnlyInDtsMap", DiffersOnlyInDtsMap);
        ReadableBuildInfoModel.WriteBool(encoder, "differsInOptions", DiffersInOptions);
        if (Original is not null)
        {
            encoder.WriteToken(JsonToken.String("original"));
            Original.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndObject);
    }
}

/// <summary>Go: <c>type readableBuildInfoResolvedRoot struct</c>（自定义 MarshalJSON：<c>[resolved, root]</c>）。</summary>
public sealed class ReadableBuildInfoResolvedRoot : IJsonMarshaler
{
    public string Resolved = "";
    public string Root = "";

    public void MarshalJSONTo(Encoder encoder)
    {
        // Go: json.Marshal([2]string{b.Resolved, b.Root})
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.String(Resolved));
        encoder.WriteToken(JsonToken.String(Root));
        encoder.WriteToken(EndArray);
    }
}
