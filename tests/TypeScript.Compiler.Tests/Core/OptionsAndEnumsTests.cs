// 新增测试：Go 侧 core 包里只有 bfs_test.go / pattern_test.go，选项与枚举这几个文件**没有** *_test.go，
// 但它们的可观察行为会进诊断/JSON/baseline（`ModuleKind.String()` 会出现在诊断里、`ScriptTarget` 推导
// 决定 emit），所以补一组最小守门测试（枚举命名/stringer 约定见 docs/architecture.md）：
//
//   1) 枚举数值必须与 Go/TS 逐字一致（baseline 依赖）；
//   2) 5 个生成的 stringer + 2 个手写 String() 的输出必须与 Go 逐字一致（含越界格式 `ModuleKind(55)`），
//      Go 的 `x.String()` 在 C# 里是 `Core` 的扩展方法（C# enum 不能覆写 ToString，见各文件头说明）；
//   3) GetEmitScriptTarget / GetEmitModuleKind / GetModuleResolutionKind / GetEmitModuleDetectionKind /
//      GetStrictOptionValue / GetResolveJsonModule 等推导逻辑（options 解析与 emit 都依赖）；
//   4) Clone 是浅拷贝（Go 是 reflect 浅拷贝 → C# MemberwiseClone）；
//   5) LinkStore / PagedLinkStore 的按需创建与 Has/TryGet 语义；
//   6) NodeCoreModules 的键集合（require('module').builtinModules 的快照）。
using TypeScript.Collections;
using TypeScript.Core;
using Xunit;
using static TypeScript.Core.Core;

namespace TypeScript.Tests;

public class CoreOptionsAndEnumsTests
{
    [Fact]
    public void TestEnumValuesMatchGo()
    {
        Assert.Equal(0, (int)Tristate.TSUnknown);
        Assert.Equal(1, (int)Tristate.TSFalse);
        Assert.Equal(2, (int)Tristate.TSTrue);

        Assert.Equal(0, (int)ScriptKind.Unknown);
        Assert.Equal(1, (int)ScriptKind.JS);
        Assert.Equal(2, (int)ScriptKind.JSX);
        Assert.Equal(3, (int)ScriptKind.TS);
        Assert.Equal(4, (int)ScriptKind.TSX);
        Assert.Equal(6, (int)ScriptKind.JSON);

        Assert.Equal(0, (int)ScriptTarget.None);
        Assert.Equal(1, (int)ScriptTarget.ES5);
        Assert.Equal(2, (int)ScriptTarget.ES2015);
        Assert.Equal(12, (int)ScriptTarget.ES2025);
        Assert.Equal(99, (int)ScriptTarget.ESNext);
        Assert.Equal(100, (int)ScriptTarget.JSON);
        // Go 的别名常量（ScriptTargetLatest / ScriptTargetLatestStandard）；C# 里是 Core 的 const，
        // 不能做 enum 成员（重复值会污染 ToString()）。
        Assert.Equal(99, (int)ScriptTargetLatest);
        Assert.Equal(12, (int)ScriptTargetLatestStandard);

        Assert.Equal(0, (int)ModuleKind.None);
        Assert.Equal(5, (int)ModuleKind.ES2015);
        Assert.Equal(7, (int)ModuleKind.ES2022);
        Assert.Equal(99, (int)ModuleKind.ESNext);
        Assert.Equal(100, (int)ModuleKind.Node16);
        Assert.Equal(101, (int)ModuleKind.Node18);
        Assert.Equal(102, (int)ModuleKind.Node20);
        Assert.Equal(199, (int)ModuleKind.NodeNext);
        Assert.Equal(200, (int)ModuleKind.Preserve);

        Assert.Equal(3, (int)ModuleResolutionKind.Node16);
        Assert.Equal(99, (int)ModuleResolutionKind.NodeNext);
        Assert.Equal(100, (int)ModuleResolutionKind.Bundler);

        Assert.Equal(1, (int)JsxEmit.Preserve);
        Assert.Equal(2, (int)JsxEmit.React);
        Assert.Equal(3, (int)JsxEmit.ReactNative);
        Assert.Equal(4, (int)JsxEmit.ReactJSX);
        Assert.Equal(5, (int)JsxEmit.ReactJSXDev);

        Assert.Equal(1, (int)LanguageVariant.JSX);
        Assert.Equal(2, (int)NewLineKind.LF);
        Assert.Equal(3, (int)ModuleDetectionKind.Force);

        // Go: type ResolutionMode = ModuleKind 的常量
        Assert.Equal(0, (int)ResolutionModeNone);
        Assert.Equal(1, (int)ResolutionModeCommonJS);
        Assert.Equal(99, (int)ResolutionModeESM);
    }

    [Fact]
    public void TestModuleKindStringerMatchesGo()
    {
        Assert.Equal("None", ModuleKind.None.String());
        Assert.Equal("CommonJS", ModuleKind.CommonJS.String());
        Assert.Equal("AMD", ModuleKind.AMD.String());
        Assert.Equal("UMD", ModuleKind.UMD.String());
        Assert.Equal("System", ModuleKind.System.String());
        Assert.Equal("ES2015", ModuleKind.ES2015.String());
        Assert.Equal("ES2020", ModuleKind.ES2020.String());
        Assert.Equal("ES2022", ModuleKind.ES2022.String());
        Assert.Equal("ESNext", ModuleKind.ESNext.String());
        Assert.Equal("Node16", ModuleKind.Node16.String());
        Assert.Equal("Node18", ModuleKind.Node18.String());
        Assert.Equal("Node20", ModuleKind.Node20.String());
        Assert.Equal("NodeNext", ModuleKind.NodeNext.String());
        Assert.Equal("Preserve", ModuleKind.Preserve.String());

        // Go 的 stringer 对区间外的值输出 "ModuleKind(<十进制>)"（8/55/198/201 都不在 stringer 的区间里）
        Assert.Equal("ModuleKind(8)", ((ModuleKind)8).String());
        Assert.Equal("ModuleKind(55)", ((ModuleKind)55).String());
        Assert.Equal("ModuleKind(198)", ((ModuleKind)198).String());
        Assert.Equal("ModuleKind(201)", ((ModuleKind)201).String());

        // 合法值下 enum 成员名与 Go stringer 的输出一致（Main 的命名约定）
        Assert.Equal("NodeNext", ModuleKind.NodeNext.ToString());
    }

    [Fact]
    public void TestScriptTargetStringerMatchesGo()
    {
        Assert.Equal("None", ScriptTarget.None.String());
        Assert.Equal("ES5", ScriptTarget.ES5.String());
        Assert.Equal("ES2015", ScriptTarget.ES2015.String());
        Assert.Equal("ES2016", ScriptTarget.ES2016.String());
        Assert.Equal("ES2017", ScriptTarget.ES2017.String());
        Assert.Equal("ES2018", ScriptTarget.ES2018.String());
        Assert.Equal("ES2019", ScriptTarget.ES2019.String());
        Assert.Equal("ES2020", ScriptTarget.ES2020.String());
        Assert.Equal("ES2021", ScriptTarget.ES2021.String());
        Assert.Equal("ES2022", ScriptTarget.ES2022.String());
        Assert.Equal("ES2023", ScriptTarget.ES2023.String());
        Assert.Equal("ES2024", ScriptTarget.ES2024.String());
        Assert.Equal("ES2025", ScriptTarget.ES2025.String());
        Assert.Equal("ESNext", ScriptTarget.ESNext.String());
        Assert.Equal("JSON", ScriptTarget.JSON.String());

        // 别名不是 stringer 的独立分支
        Assert.Equal("ESNext", ScriptTargetLatest.String());
        Assert.Equal("ES2025", ScriptTargetLatestStandard.String());

        Assert.Equal("ScriptTarget(13)", ((ScriptTarget)13).String());
        Assert.Equal("ScriptTarget(101)", ((ScriptTarget)101).String());
    }

    [Fact]
    public void TestOtherGeneratedStringersMatchGo()
    {
        // ScriptKind 的 stringer **没有** -trimprefix，输出带 "ScriptKind" 前缀
        Assert.Equal("ScriptKindUnknown", ScriptKind.Unknown.String());
        Assert.Equal("ScriptKindJS", ScriptKind.JS.String());
        Assert.Equal("ScriptKindJSX", ScriptKind.JSX.String());
        Assert.Equal("ScriptKindTS", ScriptKind.TS.String());
        Assert.Equal("ScriptKindTSX", ScriptKind.TSX.String());
        Assert.Equal("ScriptKindJSON", ScriptKind.JSON.String());
        Assert.Equal("ScriptKind(5)", ((ScriptKind)5).String()); // 保留值
        Assert.Equal("ScriptKind(7)", ((ScriptKind)7).String()); // 保留值

        Assert.Equal("TSUnknown", Tristate.TSUnknown.String());
        Assert.Equal("TSFalse", Tristate.TSFalse.String());
        Assert.Equal("TSTrue", Tristate.TSTrue.String());
        Assert.Equal("Tristate(3)", ((Tristate)3).String());

        Assert.Equal("LanguageVariantStandard", LanguageVariant.Standard.String());
        Assert.Equal("LanguageVariantJSX", LanguageVariant.JSX.String());
        Assert.Equal("LanguageVariant(2)", ((LanguageVariant)2).String());
    }

    [Fact]
    public void TestHandWrittenStringersMatchGo()
    {
        Assert.Equal("Classic", ModuleResolutionKind.Classic.String());
        Assert.Equal("Node10", ModuleResolutionKind.Node10.String());
        Assert.Equal("Node16", ModuleResolutionKind.Node16.String());
        Assert.Equal("NodeNext", ModuleResolutionKind.NodeNext.String());
        Assert.Equal("Bundler", ModuleResolutionKind.Bundler.String());

        // JsxEmit 的 stringer 输出是小写 kebab-case（与 C# 枚举成员名不同，所以必须走 .String()）
        Assert.Equal("preserve", JsxEmit.Preserve.String());
        Assert.Equal("react", JsxEmit.React.String());
        Assert.Equal("react-native", JsxEmit.ReactNative.String());
        Assert.Equal("react-jsx", JsxEmit.ReactJSX.String());
        Assert.Equal("react-jsxdev", JsxEmit.ReactJSXDev.String());

        // Go 里这两个 String() 对零值与未知值 panic（C#: Debug.Fail 抛 DebugFailureException）
        Assert.Throws<DebugFailureException>(() => ModuleResolutionKind.Unknown.String());
        Assert.Throws<DebugFailureException>(() => ((ModuleResolutionKind)42).String());
        Assert.Throws<DebugFailureException>(() => JsxEmit.None.String());
        Assert.Throws<DebugFailureException>(() => ((JsxEmit)42).String());
    }

    [Fact]
    public void TestNewLineAndModuleKindHelpers()
    {
        Assert.Equal(NewLineKind.CRLF, GetNewLineKind("\r\n"));
        Assert.Equal(NewLineKind.LF, GetNewLineKind("\n"));
        Assert.Equal(NewLineKind.None, GetNewLineKind("\r"));
        Assert.Equal("\r\n", NewLineKind.CRLF.GetNewLineCharacter());
        Assert.Equal("\n", NewLineKind.LF.GetNewLineCharacter());
        Assert.Equal("\n", NewLineKind.None.GetNewLineCharacter());

        Assert.True(ModuleKind.ES2015.IsNonNodeESM());
        Assert.True(ModuleKind.ESNext.IsNonNodeESM());
        Assert.False(ModuleKind.Node16.IsNonNodeESM());
        Assert.False(ModuleKind.CommonJS.IsNonNodeESM());

        Assert.False(ModuleKind.Node16.SupportsImportAttributes());
        Assert.True(ModuleKind.Node18.SupportsImportAttributes());
        Assert.True(ModuleKind.NodeNext.SupportsImportAttributes());
        Assert.True(ModuleKind.Preserve.SupportsImportAttributes());
        Assert.True(ModuleKind.ESNext.SupportsImportAttributes());
        Assert.False(ModuleKind.ES2022.SupportsImportAttributes());

        // Go: var ModuleKindToModuleResolutionKind（只有两项）
        Assert.Equal(2, ModuleKindToModuleResolutionKind.Count);
        Assert.Equal(ModuleResolutionKind.Node16, ModuleKindToModuleResolutionKind[ModuleKind.Node16]);
        Assert.Equal(ModuleResolutionKind.NodeNext, ModuleKindToModuleResolutionKind[ModuleKind.NodeNext]);
    }

    [Fact]
    public void TestEmitTargetAndModuleKindDerivation()
    {
        var options = new CompilerOptions();
        Assert.Equal(ScriptTarget.None, options.Target); // Go 的零值
        Assert.Equal(ScriptTarget.ES2025, options.GetEmitScriptTarget()); // → ScriptTargetLatestStandard
        Assert.Equal(ModuleKind.ES2022, options.GetEmitModuleKind());
        Assert.Equal(ModuleResolutionKind.Bundler, options.GetModuleResolutionKind());
        Assert.Equal(ModuleDetectionKind.Auto, options.GetEmitModuleDetectionKind());

        Assert.Equal(ModuleKind.Node16, new CompilerOptions { Module = ModuleKind.Node16 }.GetEmitModuleKind());
        Assert.Equal(ModuleKind.ESNext, new CompilerOptions { Target = ScriptTarget.ESNext }.GetEmitModuleKind());
        Assert.Equal(ModuleKind.ES2020, new CompilerOptions { Target = ScriptTarget.ES2020 }.GetEmitModuleKind());
        Assert.Equal(ModuleKind.ES2015, new CompilerOptions { Target = ScriptTarget.ES2019 }.GetEmitModuleKind());
        Assert.Equal(ModuleKind.CommonJS, new CompilerOptions { Target = ScriptTarget.ES5 }.GetEmitModuleKind());

        // ModuleResolution 是 Unknown/Classic/Node10 时按 module kind 推导
        Assert.Equal(ModuleResolutionKind.Node16, new CompilerOptions { Module = ModuleKind.Node18 }.GetModuleResolutionKind());
        Assert.Equal(ModuleResolutionKind.NodeNext, new CompilerOptions { Module = ModuleKind.NodeNext }.GetModuleResolutionKind());
        Assert.Equal(ModuleResolutionKind.Bundler, new CompilerOptions { Module = ModuleKind.CommonJS }.GetModuleResolutionKind());
        Assert.Equal(ModuleResolutionKind.Bundler, new CompilerOptions { ModuleResolution = ModuleResolutionKind.Bundler }.GetModuleResolutionKind());
        Assert.Equal(ModuleResolutionKind.Node16, new CompilerOptions { ModuleResolution = ModuleResolutionKind.Classic, Module = ModuleKind.Node16 }.GetModuleResolutionKind());

        Assert.Equal(ModuleDetectionKind.Force, new CompilerOptions { Module = ModuleKind.Node16 }.GetEmitModuleDetectionKind());
        Assert.Equal(ModuleDetectionKind.Force, new CompilerOptions { Module = ModuleKind.NodeNext }.GetEmitModuleDetectionKind());
        Assert.Equal(ModuleDetectionKind.Auto, new CompilerOptions { Module = ModuleKind.ES2022 }.GetEmitModuleDetectionKind());
        Assert.Equal(ModuleDetectionKind.Legacy, new CompilerOptions { ModuleDetection = ModuleDetectionKind.Legacy }.GetEmitModuleDetectionKind());
    }

    [Fact]
    public void TestStrictAllowJsAndClassFieldDerivation()
    {
        Assert.True(new CompilerOptions { Strict = Tristate.TSUnknown }.GetStrictOptionValue(Tristate.TSUnknown));
        Assert.False(new CompilerOptions { Strict = Tristate.TSFalse }.GetStrictOptionValue(Tristate.TSUnknown));
        Assert.True(new CompilerOptions().GetStrictOptionValue(Tristate.TSTrue));
        Assert.False(new CompilerOptions().GetStrictOptionValue(Tristate.TSFalse));

        Assert.True(new CompilerOptions { CheckJs = Tristate.TSTrue }.GetAllowJS());
        Assert.False(new CompilerOptions { AllowJs = Tristate.TSFalse, CheckJs = Tristate.TSTrue }.GetAllowJS());
        Assert.True(new CompilerOptions { AllowJs = Tristate.TSTrue }.GetAllowJS());

        Assert.False(new CompilerOptions().GetJSXTransformEnabled());
        Assert.True(new CompilerOptions { Jsx = JsxEmit.React }.GetJSXTransformEnabled());
        Assert.True(new CompilerOptions { Jsx = JsxEmit.ReactJSX }.GetJSXTransformEnabled());
        Assert.True(new CompilerOptions { Jsx = JsxEmit.ReactJSXDev }.GetJSXTransformEnabled());

        Assert.True(new CompilerOptions { Target = ScriptTarget.ES2022 }.GetUseDefineForClassFields());
        Assert.False(new CompilerOptions { Target = ScriptTarget.ES2021 }.GetUseDefineForClassFields());
        Assert.True(new CompilerOptions { Target = ScriptTarget.ES2022 }.GetEmitStandardClassFields());
        Assert.False(new CompilerOptions { Target = ScriptTarget.ES2022, UseDefineForClassFields = Tristate.TSFalse }.GetEmitStandardClassFields());

        Assert.True(new CompilerOptions { Declaration = Tristate.TSTrue, DeclarationMap = Tristate.TSTrue }.GetEmitDeclarations());
        Assert.True(new CompilerOptions { Composite = Tristate.TSTrue, DeclarationMap = Tristate.TSTrue }.GetAreDeclarationMapsEnabled());
        Assert.False(new CompilerOptions { DeclarationMap = Tristate.TSTrue }.GetAreDeclarationMapsEnabled());

        Assert.True(new CompilerOptions { Composite = Tristate.TSTrue }.IsIncremental());
        Assert.False(new CompilerOptions().IsIncremental());
        Assert.True(new CompilerOptions { VerbatimModuleSyntax = Tristate.TSTrue }.GetIsolatedModules());
        Assert.True(new CompilerOptions { VerbatimModuleSyntax = Tristate.TSTrue }.ShouldPreserveConstEnums());
        Assert.True(new CompilerOptions { PreserveConstEnums = Tristate.TSTrue }.ShouldPreserveConstEnums());
    }

    [Fact]
    public void TestResolveJsonModuleAndModuleEmit()
    {
        Assert.True(new CompilerOptions().GetResolveJsonModule()); // 默认 Bundler
        Assert.True(new CompilerOptions { Module = ModuleKind.Node20 }.GetResolveJsonModule());
        Assert.True(new CompilerOptions { Module = ModuleKind.NodeNext }.GetResolveJsonModule());
        Assert.False(new CompilerOptions { Module = ModuleKind.Node16 }.GetResolveJsonModule());
        Assert.True(new CompilerOptions { ResolveJsonModule = Tristate.TSTrue, Module = ModuleKind.Node16 }.GetResolveJsonModule());
        Assert.False(new CompilerOptions { ResolveJsonModule = Tristate.TSFalse, Module = ModuleKind.Node20 }.GetResolveJsonModule());

        Assert.False(new CompilerOptions { Module = ModuleKind.System }.HasJsonModuleEmitEnabled());
        Assert.False(new CompilerOptions { Module = ModuleKind.UMD }.HasJsonModuleEmitEnabled());
        Assert.True(new CompilerOptions { Module = ModuleKind.CommonJS }.HasJsonModuleEmitEnabled());
    }

    [Fact]
    public void TestImportingTsExtensionsAndTypeRoots()
    {
        var options = new CompilerOptions();
        Assert.False(options.GetAllowImportingTsExtensions());
        Assert.False(options.AllowImportingTsExtensionsFrom("a.ts"));
        Assert.True(options.AllowImportingTsExtensionsFrom("a.d.ts")); // tspath.IsDeclarationFileName
        Assert.True(new CompilerOptions { RewriteRelativeImportExtensions = Tristate.TSTrue }.GetAllowImportingTsExtensions());
        Assert.True(new CompilerOptions { ResolvePackageJsonExports = Tristate.TSUnknown }.GetResolvePackageJsonExports());
        Assert.False(new CompilerOptions { ResolvePackageJsonExports = Tristate.TSFalse }.GetResolvePackageJsonExports());
        Assert.False(new CompilerOptions { ResolvePackageJsonImports = Tristate.TSFalse }.GetResolvePackageJsonImports());

        Assert.False(new CompilerOptions().UsesWildcardTypes());
        Assert.True(new CompilerOptions { Types = ["*"] }.UsesWildcardTypes());
        Assert.False(new CompilerOptions { Types = ["node"] }.UsesWildcardTypes());

        // TypeRoots 显式给出时直接返回、FromConfig = true（Go 的 `options.TypeRoots != nil`）
        var (roots, fromConfig) = new CompilerOptions { TypeRoots = ["D:/types"] }.GetEffectiveTypeRoots("D:/proj");
        Assert.True(fromConfig);
        Assert.Single(roots);
        Assert.Equal("D:/types", roots[0]);

        // 未给出时按 ConfigFilePath 的祖先目录推导 node_modules/@types
        var derived = new CompilerOptions { ConfigFilePath = "D:/proj/tsconfig.json" };
        var (derivedRoots, derivedFromConfig) = derived.GetEffectiveTypeRoots("D:/other");
        Assert.False(derivedFromConfig);
        Assert.Equal("D:/proj/node_modules/@types", derivedRoots[0]);
        Assert.All(derivedRoots, r => Assert.EndsWith("/node_modules/@types", r));

        // 既无配置路径也无 currentDirectory → Go 的 panic（C#: Debug.Fail）
        Assert.Throws<DebugFailureException>(() => new CompilerOptions().GetEffectiveTypeRoots(""));
    }

    [Fact]
    public void TestPathsBasePath()
    {
        Assert.Equal("", new CompilerOptions().GetPathsBasePath("D:/proj"));
        Assert.Equal("", new CompilerOptions { Paths = new OrderedMap<string, string[]>() }.GetPathsBasePath("D:/proj"));

        var options = new CompilerOptions { Paths = new OrderedMap<string, string[]>() };
        options.Paths.Set("@/*", ["src/*"]);
        Assert.Equal("D:/proj", options.GetPathsBasePath("D:/proj"));
        options.PathsBasePath = "D:/base";
        Assert.Equal("D:/base", options.GetPathsBasePath("D:/proj"));
    }

    [Fact]
    public void TestCloneIsShallowCopy()
    {
        var options = new CompilerOptions { AllowJs = Tristate.TSTrue, OutDir = "out", Types = ["a"] };
        var clone = options.Clone();

        Assert.NotSame(options, clone);
        Assert.Equal(Tristate.TSTrue, clone.AllowJs);
        Assert.Equal("out", clone.OutDir);
        Assert.Equal(Tristate.TSUnknown, clone.Incremental); // 零值字段
        Assert.Same(options.Types, clone.Types); // 浅拷贝：slice 字段共享引用

        clone.OutDir = "other";
        Assert.Equal("out", options.OutDir); // 只改了副本（Go 的 reflect 浅拷贝同理）
    }

    [Fact]
    public void TestTristateHelpersAndJson()
    {
        Assert.True(Tristate.TSTrue.IsTrue());
        Assert.False(Tristate.TSFalse.IsTrue());
        Assert.True(Tristate.TSUnknown.IsTrueOrUnknown());
        Assert.True(Tristate.TSFalse.IsFalseOrUnknown());
        Assert.True(Tristate.TSUnknown.IsUnknown());
        Assert.Equal(Tristate.TSTrue, Tristate.TSUnknown.DefaultIfUnknown(Tristate.TSTrue));
        Assert.Equal(Tristate.TSFalse, Tristate.TSFalse.DefaultIfUnknown(Tristate.TSTrue));
        Assert.Equal(Tristate.TSTrue, BoolToTristate(true));
        Assert.Equal(Tristate.TSFalse, BoolToTristate(false));

        // Go: Tristate.MarshalJSON 写 JSON 布尔（不是底层整数 0/1/2），UnmarshalJSON 只认 "true"/"false"
        Assert.Equal("true", System.Text.Encoding.UTF8.GetString(Tristate.TSTrue.MarshalJSON()));
        Assert.Equal("false", System.Text.Encoding.UTF8.GetString(Tristate.TSFalse.MarshalJSON()));
        Assert.Equal("null", System.Text.Encoding.UTF8.GetString(Tristate.TSUnknown.MarshalJSON()));

        var value = Tristate.TSFalse;
        value.UnmarshalJSON(System.Text.Encoding.UTF8.GetBytes("GARBAGE"));
        Assert.Equal(Tristate.TSUnknown, value);
        value.UnmarshalJSON(System.Text.Encoding.UTF8.GetBytes("true"));
        Assert.Equal(Tristate.TSTrue, value);
        value.UnmarshalJSON(System.Text.Encoding.UTF8.GetBytes("false"));
        Assert.Equal(Tristate.TSFalse, value);
        value.UnmarshalJSON(System.Text.Encoding.UTF8.GetBytes("null"));
        Assert.Equal(Tristate.TSUnknown, value);
    }

    [Fact]
    public void TestNodeCoreModules()
    {
        Assert.True(NodeCoreModules["fs"]);
        Assert.True(NodeCoreModules["node:fs"]);
        Assert.True(NodeCoreModules["fs/promises"]);
        Assert.True(NodeCoreModules["node:fs/promises"]);
        Assert.True(NodeCoreModules["node:test"]);
        Assert.True(NodeCoreModules["node:test/reporters"]);
        Assert.False(NodeCoreModules.ContainsKey("assert2"));
        // "quic" 只在带前缀的那张表里
        Assert.False(NodeCoreModules.ContainsKey("quic"));
        Assert.True(NodeCoreModules["node:quic"]);

        Assert.Equal(54, UnprefixedNodeCoreModules.Count);
        Assert.Equal(5, ExclusivelyPrefixedNodeCoreModules.Count);
        Assert.Equal(54 * 2 + 5, NodeCoreModules.Count);

        Assert.Equal("node", NonRelativeModuleNameForTypingCache("fs"));
        Assert.Equal("node", NonRelativeModuleNameForTypingCache("node:test"));
        Assert.Equal("react", NonRelativeModuleNameForTypingCache("react"));
    }

    [Fact]
    public void TestLinkStores()
    {
        var store = new LinkStore<string, LinkBox>();
        var first = store.Get("a");
        Assert.Same(first, store.Get("a")); // Get 只在首次创建
        Assert.True(store.Has("a"));
        Assert.False(store.Has("b"));
        Assert.Same(first, store.TryGet("a"));
        Assert.Null(store.TryGet("b"));

        var paged = new PagedLinkStore<LinkBox>();
        Assert.Null(paged.TryGet(0));
        Assert.False(paged.Has(0));
        var page0 = paged.Get(0);
        Assert.Same(page0, paged.Get(0));
        Assert.True(paged.Has(0));
        Assert.Same(page0, paged.TryGet(0));
        // 同页未创建槽位仍是 null：Get 只创建被访问的槽位
        Assert.Null(paged.TryGet(1));

        // 同页不同槽位 / 不同页不同槽位都互不别名
        Assert.NotSame(page0, paged.Get(1));
        Assert.NotSame(page0, paged.Get(256));
        Assert.NotSame(paged.Get(256), paged.Get(300)); // 300 = 页 1、槽 44

        // 页表范围内（pageIndex < 65536）与页 map 范围（>= 65536）都要能按需建页
        var deep = paged.Get(100000UL << 8);
        Assert.Same(deep, paged.Get(100000UL << 8));
        Assert.Null(paged.TryGet((100000UL << 8) + 1));
        Assert.Same(page0, paged.TryGet(0));
        Assert.NotNull(paged.TryGet(0));
        var boundary = paged.Get(65535UL << 8);
        Assert.NotSame(boundary, paged.Get((65535UL << 8) + 255));
    }

    [Fact]
    public void TestTypeAcquisitionAndProjectReferenceAndWatchOptions()
    {
        var a = new TypeAcquisition { Enable = Tristate.TSTrue, Include = ["x"] };
        var b = new TypeAcquisition { Enable = Tristate.TSTrue, Include = ["x"] };
        Assert.True(a.Equals(b));
        Assert.True(a.Equals(a));
        Assert.False(a.Equals(null));
        Assert.False(a.Equals(new TypeAcquisition { Enable = Tristate.TSTrue, Include = ["y"] }));
        // Go 的 slices.Equal：nil 与空 slice 相等
        Assert.True(new TypeAcquisition().Equals(new TypeAcquisition { Include = [], Exclude = [] }));

        Assert.Equal("D:/p/tsconfig.json", ResolveProjectReferencePath(new ProjectReference { Path = "D:/p" }));
        Assert.Equal("D:/p/tsconfig.json", ResolveProjectReferencePath(new ProjectReference { Path = "D:/p/tsconfig.json" }));
        Assert.Equal("D:/p/x.json", ResolveConfigFileNameOfProjectReference("D:/p/x.json"));

        Assert.Equal(TimeSpan.FromMilliseconds(2000), new WatchOptions().WatchInterval());
        Assert.Equal(TimeSpan.FromMilliseconds(7), new WatchOptions { Interval = 7 }.WatchInterval());

        Assert.Null(new BuildOptions().Builders);
        Assert.Equal(Tristate.TSUnknown, new BuildOptions().Dry);
        Assert.Equal("", new PluginImport().Name);
        Assert.Equal(Tristate.TSUnknown, EmptyCompilerOptions.Quiet); // 注意：TypeScript.* 命名空间下 `Core.X` 会被命名空间 TypeScript.Core 遮蔽（docs §2），要用 using static 的裸名
    }

    public sealed class LinkBox
    {
        public int Value;
    }
}
