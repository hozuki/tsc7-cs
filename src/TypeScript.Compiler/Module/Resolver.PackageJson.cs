// ported from: internal/module/resolver.go:1701-1975（package.json 字段读取：tsconfig/main/types/typings、
// info cache、PackageId 与 peerDependencies、条件判定、GetConditions / getNodeResolutionFeatures）
//
// 与 Go 的偏离（同 Resolver.cs 文件头；本文件特有的）：
// - Go 的 `(string, bool)` 双返回值 → `bool X(..., out string path)`（§3.2）。
// - `packagejson.Expected[T]` 在 C# 里是 class，字段直接传对象（Go 传的是字段地址 `&contents.Types`）。
// - `Expected[string].Value` 的 Go 零值是 `""`，C# 是 null → "falsy 字段" 判定写 `string.IsNullOrEmpty(field.Value)`
//   （两种语言下都只是 `Value == ""` 的零值判定）。
// - Go 的 `len(map) == 0` → `Value.Count`（Valid 为 true 时 Value 一定非 null，见 PackageJson/Expected.cs）。
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.PackageJson;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Module;

public static partial class Module
{
    /// <summary>
    /// This function is only ever called with paths written in package.json files - never
    /// module specifiers written in source files - and so it always allows the
    /// candidate to end with a TS extension (but will also try substituting a JS extension for a TS extension).
    /// </summary>
    internal static Resolved? LoadFileNameFromPackageJSONField(this ResolutionState r, Extensions extensions, string candidate, string packageJSONValue)
    {
        if (((extensions & Extensions.TypeScript) != 0 && HasImplementationTSFileExtension(candidate)) || ((extensions & Extensions.Declaration) != 0 && IsDeclarationFileName(candidate)))
        {
            if (r.TryFile(candidate, out var path))
            {
                var extension = TryExtractTSExtension(path);
                // resolvedUsingTsExtension should be true when the pattern ends with * and the
                // candidate file ends in a TS extension. This means the * matched a TS extension
                // from the module specifier. For example:
                // - import "pkg/foo.ts" with pattern "./*" -> true
                // - import "pkg/foo.ts.omg" with pattern "./*.omg" -> true (star matched .ts)
                // - import "pkg/foo" with pattern "./*.ts" -> false (extension in pattern, not specifier)
                var resolvedUsingTsExtension = packageJSONValue.EndsWith('*') && extension != "";
                return new Resolved
                {
                    Path = path,
                    Extension = extension,
                    ResolvedUsingTsExtension = resolvedUsingTsExtension,
                };
            }

            return null;
        }

        if (r._isConfigLookup && (extensions & Extensions.Json) != 0 && FileExtensionIs(candidate, ExtensionJson))
        {
            if (r.TryFile(candidate, out var jsonPath))
            {
                return new Resolved
                {
                    Path = jsonPath,
                    Extension = ExtensionJson,
                };
            }
        }

        return r.LoadModuleFromFileNoImplicitExtensions(extensions, candidate);
    }

    /// <summary>Go 的 <c>(*resolutionState).getPackageFile</c>。</summary>
    internal static bool GetPackageFile(this ResolutionState r, Extensions extensions, InfoCacheEntry packageInfo, out string packageFile)
    {
        packageFile = "";
        var contents = packageInfo.GetContents();
        if (contents is null)
        {
            return false;
        }

        if (r._isConfigLookup)
        {
            return r.GetPackageJSONPathField("tsconfig", contents.PathFields.TSConfig, packageInfo.PackageDirectory, out packageFile);
        }

        if ((extensions & Extensions.Declaration) != 0)
        {
            if (r.GetPackageJSONPathField("typings", contents.PathFields.Typings, packageInfo.PackageDirectory, out packageFile))
            {
                return true;
            }

            if (r.GetPackageJSONPathField("types", contents.PathFields.Types, packageInfo.PackageDirectory, out packageFile))
            {
                return true;
            }
        }

        if ((extensions & (Extensions.ImplementationFiles | Extensions.Declaration)) != 0)
        {
            return r.GetPackageJSONPathField("main", contents.PathFields.Main, packageInfo.PackageDirectory, out packageFile);
        }

        return false;
    }

    /// <summary>Go 的 <c>(*resolutionState).getPackageJsonInfo</c>。</summary>
    internal static InfoCacheEntry? GetPackageJsonInfo(this ResolutionState r, string packageDirectory)
    {
        var packageJsonPath = CombinePaths(packageDirectory, "package.json");

        if (r._resolver.PackageJsonCacheGet(packageJsonPath) is { } existing)
        {
            if (existing.Contents is not null)
            {
                r._tracer?.Write(File_0_exists_according_to_earlier_cached_lookups, packageJsonPath);
                return existing.WithPackageDirectory(packageDirectory);
            }

            if (existing.DirectoryExists)
            {
                r._tracer?.Write(File_0_does_not_exist_according_to_earlier_cached_lookups, packageJsonPath);
            }

            return null;
        }

        var fs = r._resolver.Host.FS();
        var directoryExists = fs.DirectoryExists(packageDirectory);
        if (directoryExists && fs.FileExists(packageJsonPath))
        {
            // Ignore error
            fs.TryReadFile(packageJsonPath, out var contents);
            var parseable = TryParsePackageJson(Encoding.UTF8.GetBytes(contents ?? ""), out var packageJsonContent);
            r._tracer?.Write(Found_package_json_at_0, packageJsonPath);
            var result = new InfoCacheEntry
            {
                PackageDirectory = packageDirectory,
                DirectoryExists = true,
                Contents = NewPackageJson(packageJsonContent, parseable),
            };
            result = r._resolver.PackageJsonCacheSet(packageJsonPath, result);
            return result.WithPackageDirectory(packageDirectory);
        }

        if (directoryExists)
        {
            r._tracer?.Write(File_0_does_not_exist, packageJsonPath);
        }

        r._resolver.PackageJsonCacheSet(packageJsonPath, new InfoCacheEntry
        {
            PackageDirectory = packageDirectory,
            DirectoryExists = directoryExists,
        });

        return null;
    }

    /// <summary>
    /// Go 的 <c>&amp;packagejson.PackageJson{Fields: content, Parseable: parseable}</c>：C# 侧
    /// `PackageJson : Fields` 是继承（Go 的内嵌结构体），没有名为 `Fields` 的可写成员，所以逐字段复制
    /// （四个 group 都是引用，等价于 Go 的内嵌结构体值拷贝）。
    /// </summary>
    private static TypeScript.PackageJson.PackageJson NewPackageJson(Fields fields, bool parseable)
    {
        // `PackageJson` 这个简单名会先绑定到命名空间 `TypeScript.PackageJson`（外层命名空间成员优先于 using），
        // 所以类型位置必须写全名（docs/architecture.md §2 的同名陷阱）。
        var result = new TypeScript.PackageJson.PackageJson
        {
            Parseable = parseable,
            HeaderFields = fields.HeaderFields,
            PathFields = fields.PathFields,
            DependencyFields = fields.DependencyFields,
            ContentMapper = fields.ContentMapper,
        };
        return result;
    }

    /// <summary>Go 的 <c>(*resolutionState).getPackageId</c>。</summary>
    internal static PackageId GetPackageId(this ResolutionState r, string resolvedFileName, InfoCacheEntry? packageInfo)
    {
        if (packageInfo?.GetContents() is { } packageJsonContent)
        {
            if (packageJsonContent.HeaderFields.Name.TryGetValue(out var name) &&
                packageJsonContent.HeaderFields.Version.TryGetValue(out var version))
            {
                var subModuleName = "";
                if (resolvedFileName.Length > packageInfo.PackageDirectory.Length)
                {
                    subModuleName = resolvedFileName[(packageInfo.PackageDirectory.Length + 1)..];
                }

                return new PackageId
                {
                    Name = name,
                    Version = version,
                    SubModuleName = subModuleName,
                    PeerDependencies = r.ReadPackageJsonPeerDependencies(packageInfo),
                };
            }
        }

        return default;
    }

    /// <summary>Go 的 <c>(*resolutionState).readPackageJsonPeerDependencies</c>。</summary>
    internal static string ReadPackageJsonPeerDependencies(this ResolutionState r, InfoCacheEntry packageJsonInfo)
    {
        if (packageJsonInfo.GetContents() is not { } packageJsonContent)
        {
            return "";
        }

        var peerDependencies = packageJsonContent.DependencyFields.PeerDependencies;
        var ok = r.ValidatePackageJSONField("peerDependencies", peerDependencies);
        if (!ok || peerDependencies.Value is not { Count: > 0 })
        {
            return "";
        }

        var peerDependencyMap = peerDependencies.Value;

        r._tracer?.Write(X_package_json_has_a_peerDependencies_field);
        var packageDirectory = r.RealPath(packageJsonInfo.PackageDirectory);
        const string nodeModulesSegment = "/node_modules";
        var nodeModulesIndex = packageDirectory.LastIndexOf(nodeModulesSegment, StringComparison.Ordinal);
        if (nodeModulesIndex == -1)
        {
            return "";
        }

        var nodeModules = packageDirectory[..(nodeModulesIndex + nodeModulesSegment.Length)] + "/";
        var names = new List<string>(peerDependencyMap.Keys);
        names.Sort(StringComparer.Ordinal);
        var builder = new StringBuilder();
        foreach (var name in names)
        {
            var peerPackageJson = r.GetPackageJsonInfo(nodeModules + name);
            if (peerPackageJson is { } peerInfo && peerInfo.GetContents() is { } peerContents)
            {
                var version = peerContents.HeaderFields.Version.Value;
                builder.Append('+');
                builder.Append(name);
                builder.Append('@');
                builder.Append(version);
                r._tracer?.Write(Found_peerDependency_0_with_1_version, name, version);
            }
            else
            {
                r._tracer?.Write(Failed_to_find_peerDependency_0, name);
            }
        }

        return builder.ToString();
    }

    /// <summary>Go 的 <c>(*resolutionState).realPath</c>。</summary>
    internal static string RealPath(this ResolutionState r, string path)
    {
        var realPath = NormalizePath(r._resolver.Host.FS().Realpath(path));
        r._tracer?.Write(Resolving_real_path_for_0_result_1, path, realPath);
        return realPath;
    }

    /// <summary>Go 的 <c>(*resolutionState).validatePackageJSONField</c>。</summary>
    internal static bool ValidatePackageJSONField(this ResolutionState r, string fieldName, TypeValidatedField field)
    {
        if (field.IsPresent())
        {
            if (field.IsValid())
            {
                return true;
            }

            r._tracer?.Write(Expected_type_of_0_field_in_package_json_to_be_1_got_2, fieldName, field.ExpectedJSONType(), field.ActualJSONType());
        }

        r._tracer?.Write(X_package_json_does_not_have_a_0_field, fieldName);
        return false;
    }

    /// <summary>Go 的 <c>(*resolutionState).getPackageJSONPathField</c>。</summary>
    internal static bool GetPackageJSONPathField(this ResolutionState r, string fieldName, Expected<string> field, string directory, out string path)
    {
        path = "";
        if (!r.ValidatePackageJSONField(fieldName, field))
        {
            return false;
        }

        if (string.IsNullOrEmpty(field.Value))
        {
            r._tracer?.Write(X_package_json_had_a_falsy_0_field, fieldName);
            return false;
        }

        path = NormalizePath(CombinePaths(directory, field.Value));
        r._tracer?.Write(X_package_json_has_0_field_1_that_references_2, fieldName, field.Value, path);
        return true;
    }

    /// <summary>Go 的 <c>(*resolutionState).conditionMatches</c>。</summary>
    internal static bool ConditionMatches(this ResolutionState r, string condition)
    {
        if (condition == "default" || System.Array.IndexOf(r._conditions, condition) >= 0)
        {
            return true;
        }

        if (System.Array.IndexOf(r._conditions, "types") < 0)
        {
            return false; // only apply versioned types conditions if the types condition is applied
        }

        return IsApplicableVersionedTypesKey(condition);
    }

    /// <summary>Go 的 <c>(*resolutionState).getTraceFunc</c>。</summary>
    internal static Action<Message, object?[]>? GetTraceFunc(this ResolutionState r)
    {
        if (r._tracer is { } tracer)
        {
            return tracer.Write;
        }

        return null;
    }

    public static string[] GetConditions(CompilerOptions options, ModuleKind resolutionMode)
    {
        var moduleResolution = options.GetModuleResolutionKind();
        if (resolutionMode == ModuleKind.None && moduleResolution == ModuleResolutionKind.Bundler)
        {
            resolutionMode = ModuleKind.ESNext;
        }

        var conditions = new List<string>(3 + (options.CustomConditions?.Length ?? 0));
        if (resolutionMode == ModuleKind.ESNext)
        {
            conditions.Add("import");
        }
        else
        {
            conditions.Add("require");
        }

        if (options.NoDtsResolution != Tristate.TSTrue)
        {
            conditions.Add("types");
        }

        if (moduleResolution != ModuleResolutionKind.Bundler)
        {
            conditions.Add("node");
        }

        return Concatenate([.. conditions], options.CustomConditions);
    }

    internal static NodeResolutionFeatures GetNodeResolutionFeatures(CompilerOptions options)
    {
        var features = NodeResolutionFeatures.None;

        switch (options.GetModuleResolutionKind())
        {
            case ModuleResolutionKind.Node16:
                features = NodeResolutionFeatures.Node16Default;
                break;
            case ModuleResolutionKind.NodeNext:
                features = NodeResolutionFeatures.NodeNextDefault;
                break;
            case ModuleResolutionKind.Bundler:
                features = NodeResolutionFeatures.BundlerDefault;
                break;
        }

        if (options.ResolvePackageJsonExports == Tristate.TSTrue)
        {
            features |= NodeResolutionFeatures.Exports;
        }
        else if (options.ResolvePackageJsonExports == Tristate.TSFalse)
        {
            features &= ~NodeResolutionFeatures.Exports;
        }

        if (options.ResolvePackageJsonImports == Tristate.TSTrue)
        {
            features |= NodeResolutionFeatures.Imports;
        }
        else if (options.ResolvePackageJsonImports == Tristate.TSFalse)
        {
            features &= ~NodeResolutionFeatures.Imports;
        }

        return features;
    }
}
