// ported from: internal/ls/autoimport/fix.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `Fix` 嵌入 `*lsproto.AutoImportFix`（匿名字段，字段被提升）→ C# 的 `AutoImportFix` 是 sealed class，
//    不能继承，改成组合字段 `public AutoImportFix AutoImportFix = new();`。Go 里 `f.Kind`/`f.Name`/
//    `f.ModuleSpecifier`/`f.ImportKind`/`f.UseRequire`/`f.AddAsTypeOnly`/`f.ImportIndex`/`f.UsagePosition`/
//    `f.NamespacePrefix` 的每个读写点都改写成 `f.AutoImportFix.X`。
// 2) 未导出类型 `newImportBinding`/`addToExistingImportFix`/`existingImport`/`fileSyntaxKind` → C# 习惯命名
//    `NewImportBinding`/`AddToExistingImportFix`/`ExistingImport`/`FileSyntaxKind`；`newImportBinding` 的字段在 Go 里
//    全是未导出的，按 C# 习惯给 PascalCase（该类型只在包内用），其余三个类型的字段保持 `_camelCase`。
//    iota 常量去类型前缀：`fileSyntaxKindAmbiguous` → `FileSyntaxKind.Ambiguous`。
// 3) `panic` → `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException();`（`Debug.Fail` 没有
//    `[DoesNotReturn]`）。
// 4) 包级函数（含未导出的）→ `public static partial class AutoImport` 上的 static 方法（§2）；未导出的方法名按
//    "internal + PascalCase"：`fileEdits` → `FileEdits`、`getNewImports` → `GetNewImports` …
// 5) `lsutil.X`/`ast.X`/`astnav.X`/`scanner.X`/`tspath.X`/`core.X` 的包级函数用 `using static` 后的裸名（§2）。
// 6) `*lsproto.Position` → `Position?`；Go 的 `([]*lsproto.TextEdit, string, bool)` → 具名元组
//    `(TextEdit[] Edits, string Description, bool Safe)`。
// 7) `locale.FromContext(ctx)` → `TypeScript.Locale.Locale.FromContext(ctx)`；`locale.Locale` 既是命名空间又是类型
//    ⇒ 别名 `Loc`（同 LsConv/Converters.cs）。`f.Edits` 的第一参数 `ctx context.Context` → `TypeScript.Core.Context`。
// 8) `slices.SortFunc` → `Array.Sort(…, Comparison<T>)`；`slices.Clone` → `(T[])x.Clone()`；`slices.Index` →
//    `Array.IndexOf`；`cmp.Compare`（int 型枚举）→ `int.CompareTo`。
// 9) `rune(name[0])`/`unicode.ToUpper`：Go 取的是**首字节**，C# 取**首个 UTF-16 代码单元**，走 `RuneUtil`
//    （util.go 的既有小工具，见 Util.cs 文件头第 3/4 条）；ASCII 名字下完全一致。
// 10) `ct.AsNodeFactory().NewNodeList(...)` → `ct.NodeFactory.NewNodeList(...)`：C# 的 `Tracker` 没有嵌入
//     `NodeFactory`，字段 `NodeFactory` 与 `AsNodeFactory()` 是同一个对象。
// 11) Go 的 `*ast.Declaration`/`*ast.IdentifierNode`/`*ast.Expression` 等具体指针类型在 C# 里统一是 `Node`
//     （与 `*ast.Node` 一一对应）；签名按 C# 侧实参的实际类型收紧。
// 12) Go 的 `switch { case cond: continue }` → 等价的 `if (cond) { continue; }`（C# 没有无表达式的 switch）。
// 13) `detectSyntaxIndicators` 末尾的 `return hasESM, hasCJS`：Go 的具名返回值 `hasESM` 在该分支上仍是零值
//     `false` ⇒ C# 写 `return (false, hasCJS);`。
// 14) `importClauseOrBindingPattern` 的 Go 风格"先赋值再判 nil"改成 C# 的"先判再赋值"（C# 的明确赋值分析要求），
//     行为一致。
// 15) `debug.Assert(statements != nil)`（getNewRequires 末尾）在 C# 里恒真（`List<T>` 构造后即非 null）⇒ 省略。
// 16) 照抄 Go 的 `NewBindingElement(nil, nil, NewIdentifier(name), IfElse(propertyName == "", nil,
//     NewIdentifier(propertyName)))`：Go 把 propertyName 的标识符传在第 4 个（`initializer`）位置，上游 TS 是传在
//     第 2 个（`propertyName`）位置。1:1 移植不做行为修正。
// 17) `ExportSyntax` 在 C# 里没有生成 `String()`（Go 的 stringer）⇒ `export.Syntax.String()` → `export.Syntax.ToString()`。
// 18) 包级函数在 C# 里没有 Go 那样的"包作用域"：类内调用一律写成 `AutoImport.X(...)`（例如 `Fix.Edits` 里调用
//     `FileEdits`/`AddImportType`/`AddNamespaceQualifier`/`AddToExistingImport`/`GetNewImports`/`GetNewRequires`/
//     `InsertImports`/`PromoteFromTypeOnly`/`GetModuleSpecifierText`）；`using static` 只用于别的包（LsUtil/Scanner…）。
// 19) `core.SingleElementSlice(existingFix.namedImport)`（Go 传可能为 nil 的指针，返回 nil 或单元素切片）→ C# 内联写成
//     `existingFix._namedImport is null ? [] : [existingFix._namedImport]`：`Core.SingleElementSlice<T>` 带
//     `where T : class` 约束，`T = NewImportBinding?`（可空引用类型）会触发 CS8634。
// 20) `compilerOptions.VerbatimModuleSyntax == core.TSTrue` → `== Tristate.TSTrue`（Go 的 `core.TSTrue` 是包级常量，
//     C# 里是枚举成员 `Tristate.TSTrue`，`using static TypeScript.Core.Core` 带不进来）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsChange;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.ModuleSpecifiers.ModuleSpecifiers;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;
using Loc = TypeScript.Locale.Locale;
using ModSpecPrefs = TypeScript.ModuleSpecifiers.UserPreferences;
using ResultKind = TypeScript.ModuleSpecifiers.ResultKind;

namespace TypeScript.LsAutoImport;

/// <summary>Go: <c>type newImportBinding struct</c>（fix.go:27-32）。字段在 Go 里都是未导出的 ⇒ C# 按习惯给 PascalCase
/// （该类型只在包内用）。</summary>
internal sealed class NewImportBinding
{
    public ImportKind Kind;

    public string PropertyName = "";

    public string Name = "";

    public AddAsTypeOnly AddAsTypeOnly;
}

/// <summary>Go: <c>type Fix struct</c>（fix.go:34-41）。</summary>
public sealed class Fix
{
    /// <summary>Go 的嵌入字段 <c>*lsproto.AutoImportFix</c>（见文件头第 1 条）。</summary>
    public AutoImportFix AutoImportFix = new();

    public ResultKind ModuleSpecifierKind;

    public bool IsReExport;

    public string ModuleFileName = "";

    public Node? TypeOnlyAliasDeclaration;

    /// <summary>Go: <c>func (f *Fix) Edits(ctx, file, compilerOptions, formatOptions, converters, preferences)
    /// ([]*lsproto.TextEdit, string, bool)</c>（fix.go:45-133）。</summary>
    public (TextEdit[] Edits, string Description, bool Safe) Edits(
        Context ctx,
        SourceFile file,
        CompilerOptions compilerOptions,
        FormatCodeSettings formatOptions,
        Converters converters,
        UserPreferences preferences)
    {
        var locale = Loc.FromContext(ctx);
        var tracker = Tracker.NewTracker(ctx, compilerOptions, formatOptions, converters);
        switch (AutoImportFix.Kind)
        {
            case AutoImportFixKind.UseNamespace:
            {
                var description = AutoImport.AddNamespaceQualifier(this, tracker, file, locale);
                var (edits, safe) = AutoImport.FileEdits(tracker, file);
                return (edits, description, safe);
            }

            case AutoImportFixKind.AddToExisting:
            {
                if (file.Imports().Length <= AutoImportFix.ImportIndex)
                {
                    Debug.Fail("import index out of range");
                    throw new System.Diagnostics.UnreachableException();
                }
                var existingFix = AutoImport.GetAddToExistingImportFix(file, this);
                // Go: core.SingleElementSlice(existingFix.namedImport)——nil 时是空切片。
                NewImportBinding?[] namedImportForFix = existingFix._namedImport is null ? [] : [existingFix._namedImport];
                AutoImport.AddToExistingImport(
                    tracker,
                    file,
                    existingFix._importClauseOrBindingPattern,
                    existingFix._defaultImport,
                    namedImportForFix,
                    preferences);
                var (edits, safe) = AutoImport.FileEdits(tracker, file);
                return (edits, Update_import_from_0.Localize(locale, AutoImportFix.ModuleSpecifier), safe);
            }

            case AutoImportFixKind.AddNew:
            {
                var defaultImport = IfElse(
                    AutoImportFix.ImportKind == ImportKind.Default,
                    new NewImportBinding { Name = AutoImportFix.Name, AddAsTypeOnly = AutoImportFix.AddAsTypeOnly },
                    null);
                NewImportBinding?[]? namedImports = IfElse(
                    AutoImportFix.ImportKind == ImportKind.Named,
                    new NewImportBinding?[] { new() { Name = AutoImportFix.Name, AddAsTypeOnly = AutoImportFix.AddAsTypeOnly } },
                    null);
                NewImportBinding? namespaceLikeImport = null;
                // qualification := f.qualification()
                if (AutoImportFix.ImportKind == ImportKind.Namespace || AutoImportFix.ImportKind == ImportKind.CommonJS)
                {
                    namespaceLikeImport = new NewImportBinding { Kind = AutoImportFix.ImportKind, Name = AutoImportFix.Name };
                    // if qualification != nil && qualification.namespacePref != "" {
                    // 	namespaceLikeImport.name = qualification.namespacePref
                    // }
                }

                var quotePreference = GetQuotePreference(file, preferences);
                Node[] declarations;
                if (AutoImportFix.UseRequire)
                {
                    declarations = AutoImport.GetNewRequires(tracker, AutoImportFix.ModuleSpecifier, quotePreference, defaultImport, namedImports ?? [], namespaceLikeImport, compilerOptions);
                }
                else
                {
                    declarations = AutoImport.GetNewImports(tracker, AutoImportFix.ModuleSpecifier, quotePreference, defaultImport, namedImports ?? [], namespaceLikeImport, compilerOptions, preferences);
                }

                AutoImport.InsertImports(
                    tracker,
                    file,
                    declarations,
                    /*blankLineBetween*/ true,
                    preferences);
                // if qualification != nil {
                // 	addNamespaceQualifier(tracker, file, qualification)
                // }
                var (edits, safe) = AutoImport.FileEdits(tracker, file);
                return (edits, Add_import_from_0.Localize(locale, AutoImportFix.ModuleSpecifier), safe);
            }

            case AutoImportFixKind.PromoteTypeOnly:
            {
                var promotedDeclaration = AutoImport.PromoteFromTypeOnly(tracker, TypeOnlyAliasDeclaration!, compilerOptions, file, preferences)!;
                if (promotedDeclaration.Kind == Kind.ImportSpecifier)
                {
                    var moduleSpec = AutoImport.GetModuleSpecifierText(promotedDeclaration.Parent!.Parent!);
                    var (edits, safe) = AutoImport.FileEdits(tracker, file);
                    return (edits, Remove_type_from_import_of_0_from_1.Localize(locale, AutoImportFix.Name, moduleSpec), safe);
                }
                else
                {
                    var moduleSpec = AutoImport.GetModuleSpecifierText(promotedDeclaration);
                    var (edits, safe) = AutoImport.FileEdits(tracker, file);
                    return (edits, Remove_type_from_import_declaration_from_0.Localize(locale, moduleSpec), safe);
                }
            }

            case AutoImportFixKind.JsdocTypeImport:
            {
                var description = AutoImport.AddImportType(this, file, preferences, tracker, locale);
                var (edits, safe) = AutoImport.FileEdits(tracker, file);
                return (edits, description, safe);
            }

            default:
                Debug.Fail("unimplemented fix edit");
                throw new System.Diagnostics.UnreachableException();
        }
    }
}

/// <summary>Go: <c>type addToExistingImportFix struct</c>（fix.go:43-48）。</summary>
internal sealed class AddToExistingImportFix
{
    internal Node _importClauseOrBindingPattern = null!;

    // One of `defaultImport` or `namedImports` will be present
    internal NewImportBinding? _defaultImport;

    internal NewImportBinding? _namedImport;
}

/// <summary>Go: <c>type existingImport struct</c>（fix.go:783-787）。</summary>
internal sealed class ExistingImport
{
    internal Node _node = null!;

    internal string _moduleSpecifier = "";

    internal int _index;
}

/// <summary>Go: <c>type fileSyntaxKind int</c>（fix.go:849-859）。</summary>
internal enum FileSyntaxKind : int
{
    Ambiguous = 0,
    ESM = 1,
    CJS = 2,
}

/// <summary>Go 的 <c>autoimport</c> 包（包级函数/常量放这里，见 docs/architecture.md §2）。</summary>
public static partial class AutoImport
{
    /// <summary>Go: <c>func fileEdits(tracker *change.Tracker, file *ast.SourceFile) (edits []*lsproto.TextEdit, safe bool)</c>（fix.go:137-140）。
    /// GetChanges 会丢掉所有无法忠实映射回原文的内容映射文件的编辑，所以"空结果 + safe == false"表示该 fix 无法表达、必须丢弃。</summary>
    internal static (TextEdit[] Edits, bool Safe) FileEdits(Tracker tracker, SourceFile file)
    {
        var (changes, unmappable) = tracker.GetChanges();
        return (changes.TryGetValue(file.OriginalFileName(), out var edits) ? edits : [], (unmappable?.Length ?? 0) == 0);
    }

    /// <summary>Go: <c>func addImportType(f *Fix, file *ast.SourceFile, preferences lsutil.UserPreferences, tracker *change.Tracker, locale locale.Locale) string</c>（fix.go:142-153）。</summary>
    internal static string AddImportType(Fix f, SourceFile file, UserPreferences preferences, Tracker tracker, Loc locale)
    {
        if (f.AutoImportFix.UsagePosition is null)
        {
            Debug.Fail("UsagePosition must be set for JSDoc type import fix");
            throw new System.Diagnostics.UnreachableException();
        }
        var quotePreference = GetQuotePreference(file, preferences);
        var quoteChar = "\"";
        if (quotePreference == QuotePreferenceSingle)
        {
            quoteChar = "'";
        }
        var importTypePrefix = "import(" + quoteChar + f.AutoImportFix.ModuleSpecifier + quoteChar + ").";
        tracker.InsertText(file, f.AutoImportFix.UsagePosition, importTypePrefix);
        return Change_0_to_1.Localize(locale, f.AutoImportFix.Name, importTypePrefix + f.AutoImportFix.Name);
    }

    /// <summary>Go: <c>func addNamespaceQualifier(f *Fix, tracker *change.Tracker, file *ast.SourceFile, locale locale.Locale) string</c>（fix.go:155-163）。</summary>
    internal static string AddNamespaceQualifier(Fix f, Tracker tracker, SourceFile file, Loc locale)
    {
        if (f.AutoImportFix.UsagePosition is null || f.AutoImportFix.NamespacePrefix == "")
        {
            Debug.Fail("namespace fix requires usage position and prefix");
            throw new System.Diagnostics.UnreachableException();
        }
        var qualified = f.AutoImportFix.NamespacePrefix + "." + f.AutoImportFix.Name;
        tracker.InsertText(file, f.AutoImportFix.UsagePosition, f.AutoImportFix.NamespacePrefix + ".");
        return Change_0_to_1.Localize(locale, f.AutoImportFix.Name, qualified);
    }

    /// <summary>Go: <c>func getAddToExistingImportFix(file *ast.SourceFile, fix *Fix) *addToExistingImportFix</c>（fix.go:165-200）。</summary>
    internal static AddToExistingImportFix GetAddToExistingImportFix(SourceFile file, Fix fix)
    {
        if (fix.AutoImportFix.Kind != AutoImportFixKind.AddToExisting)
        {
            Debug.Fail("expected add to existing import fix");
            throw new System.Diagnostics.UnreachableException();
        }
        var moduleSpecifier = file.Imports()[fix.AutoImportFix.ImportIndex];
        var importNode = TryGetImportFromModuleSpecifier(moduleSpecifier);
        if (importNode is null)
        {
            Debug.Fail("expected import declaration");
            throw new System.Diagnostics.UnreachableException();
        }
        Node importClauseOrBindingPattern;
        switch (importNode.Kind)
        {
            case Kind.ImportDeclaration:
            {
                var importClause = importNode.ImportClause();
                if (importClause is null)
                {
                    Debug.Fail("expected import clause");
                    throw new System.Diagnostics.UnreachableException();
                }
                importClauseOrBindingPattern = importClause;
                break;
            }
            case Kind.CallExpression:
            {
                if (!IsVariableDeclarationInitializedToRequire(importNode.Parent))
                {
                    Debug.Fail("expected require call expression to be in variable declaration");
                    throw new System.Diagnostics.UnreachableException();
                }
                var name = importNode.Parent!.Name();
                if (name is null || !IsObjectBindingPattern(name))
                {
                    Debug.Fail("expected object binding pattern in variable declaration");
                    throw new System.Diagnostics.UnreachableException();
                }
                importClauseOrBindingPattern = name;
                break;
            }
            default:
                Debug.Fail("expected import declaration or require call expression");
                throw new System.Diagnostics.UnreachableException();
        }

        var defaultImport = IfElse(
            fix.AutoImportFix.ImportKind == ImportKind.Default,
            new NewImportBinding { Kind = ImportKind.Default, Name = fix.AutoImportFix.Name, AddAsTypeOnly = fix.AutoImportFix.AddAsTypeOnly },
            null);
        var namedImport = IfElse(
            fix.AutoImportFix.ImportKind == ImportKind.Named,
            new NewImportBinding { Kind = ImportKind.Named, Name = fix.AutoImportFix.Name, AddAsTypeOnly = fix.AutoImportFix.AddAsTypeOnly },
            null);
        return new AddToExistingImportFix
        {
            _importClauseOrBindingPattern = importClauseOrBindingPattern,
            _defaultImport = defaultImport,
            _namedImport = namedImport,
        };
    }

    /// <summary>Go: <c>func addToExistingImport(ct *change.Tracker, file *ast.SourceFile, importClauseOrBindingPattern *ast.Node, defaultImport *newImportBinding, namedImports []*newImportBinding, preferences lsutil.UserPreferences)</c>（fix.go:202-325）。</summary>
    internal static void AddToExistingImport(
        Tracker ct,
        SourceFile file,
        Node importClauseOrBindingPattern,
        NewImportBinding? defaultImport,
        NewImportBinding?[] namedImports,
        UserPreferences preferences)
    {
        switch (importClauseOrBindingPattern.Kind)
        {
            case Kind.ObjectBindingPattern:
            {
                var bindingPattern = importClauseOrBindingPattern.AsBindingPattern()!;
                if (defaultImport is not null)
                {
                    AddElementToBindingPattern(ct, file, bindingPattern, defaultImport.Name, "default");
                }
                foreach (var namedImport in namedImports)
                {
                    AddElementToBindingPattern(ct, file, bindingPattern, namedImport!.Name, "");
                }
                return;
            }
            case Kind.ImportClause:
            {
                var importClause = importClauseOrBindingPattern.AsImportClause()!;

                // promoteFromTypeOnly = true if we need to promote the entire original clause from type only
                var promoteFromTypeOnly = importClause.IsTypeOnly() && Some(
                    Concatenate<NewImportBinding?>(namedImports, [defaultImport]),
                    i =>
                    {
                        if (i is null)
                        {
                            return false;
                        }
                        return i.AddAsTypeOnly == AddAsTypeOnly.NotAllowed;
                    });

                Node[] existingSpecifiers = [];
                if (importClause.NamedBindings is not null && importClause.NamedBindings.Kind == Kind.NamedImports)
                {
                    existingSpecifiers = importClause.NamedBindings.Elements() ?? [];
                }

                if (defaultImport is not null)
                {
                    Debug.Assert(importClause.Name() is null, "Cannot add a default import to an import clause that already has one");
                    ct.InsertNodeAt(
                        file,
                        new TextPos(GetStartOfNode(importClause.AsNode(), file, false)),
                        ct.NodeFactory.NewIdentifier(defaultImport.Name),
                        new NodeOptions { Suffix = ", " });
                }

                if (namedImports.Length > 0)
                {
                    var (specifierComparer, isSorted) = GetNamedImportSpecifierComparerWithDetection(importClause.Parent!, file, preferences);
                    var newSpecifiers = Map(namedImports, namedImport =>
                    {
                        Node? identifier = null;
                        if (namedImport!.PropertyName != "")
                        {
                            identifier = ct.NodeFactory.NewIdentifier(namedImport.PropertyName);
                        }
                        return ct.NodeFactory.NewImportSpecifier(
                            (!importClause.IsTypeOnly() || promoteFromTypeOnly) && ShouldUseTypeOnly(namedImport.AddAsTypeOnly, preferences),
                            identifier,
                            ct.NodeFactory.NewIdentifier(namedImport.Name));
                    });
                    Array.Sort(newSpecifiers, (x, y) => specifierComparer(x, y));
                    if (existingSpecifiers.Length > 0 && isSorted != Tristate.TSFalse)
                    {
                        // The sorting preference computed earlier may or may not have validated that these particular
                        // import specifiers are sorted. If they aren't, `getImportSpecifierInsertionIndex` will return
                        // nonsense. So if there are existing specifiers, even if we know the sorting preference, we
                        // need to ensure that the existing specifiers are sorted according to the preference in order
                        // to do a sorted insertion.

                        // If we're promoting the clause from type-only, we need to transform the existing imports
                        // before attempting to insert the new named imports (for comparison purposes only)
                        var specsToCompareAgainst = existingSpecifiers;
                        if (promoteFromTypeOnly && existingSpecifiers.Length > 0)
                        {
                            specsToCompareAgainst = Map(existingSpecifiers, e =>
                            {
                                var spec = e.AsImportSpecifier()!;
                                Node? propertyName = null;
                                if (spec.PropertyName is not null)
                                {
                                    propertyName = spec.PropertyName;
                                }
                                var syntheticSpec = ct.NodeFactory.NewImportSpecifier(
                                    true, // isTypeOnly
                                    propertyName,
                                    spec.Name());
                                return syntheticSpec;
                            });
                        }

                        foreach (var spec in newSpecifiers)
                        {
                            var insertionIndex = GetImportSpecifierInsertionIndex(specsToCompareAgainst, spec, specifierComparer);
                            ct.InsertImportSpecifierAtIndex(file, spec, importClause.NamedBindings!, insertionIndex);
                        }
                    }
                    else if (existingSpecifiers.Length > 0)
                    {
                        foreach (var spec in newSpecifiers)
                        {
                            ct.InsertNodeInListAfter(file, existingSpecifiers[^1], spec, null);
                        }
                    }
                    else
                    {
                        if (newSpecifiers.Length > 0)
                        {
                            var newNamedImports = ct.NodeFactory.NewNamedImports(ct.NodeFactory.NewNodeList(newSpecifiers));
                            if (importClause.NamedBindings is not null)
                            {
                                ct.ReplaceNode(file, importClause.NamedBindings, newNamedImports, null);
                            }
                            else
                            {
                                var name = importClause.Name();
                                if (name is null)
                                {
                                    Debug.Fail("Import clause must have either named imports or a default import");
                                    throw new System.Diagnostics.UnreachableException();
                                }
                                ct.InsertNodeAfter(file, name, newNamedImports);
                            }
                        }
                    }
                }

                if (promoteFromTypeOnly)
                {
                    // Delete the 'type' keyword from the import clause
                    var typeKeyword = GetTypeKeywordOfTypeOnlyImport(importClause, file);
                    ct.Delete(file, typeKeyword);

                    // Add 'type' modifier to existing specifiers (not newly added ones)
                    // We preserve the type-onlyness of existing specifiers regardless of whether
                    // it would make a difference in emit (user preference).
                    if (existingSpecifiers.Length > 0)
                    {
                        foreach (var specifier in existingSpecifiers)
                        {
                            if (!specifier.AsImportSpecifier()!.IsTypeOnly)
                            {
                                ct.InsertModifierBefore(file, Kind.TypeKeyword, specifier);
                            }
                        }
                    }
                }
                return;
            }
            default:
                Debug.Fail("Unsupported clause kind: " + importClauseOrBindingPattern.KindString() + " for addToExistingImport");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    /// <summary>Go: <c>func getTypeKeywordOfTypeOnlyImport(importClause *ast.ImportClause, sourceFile *ast.SourceFile) *ast.Node</c>（fix.go:327-336）。</summary>
    internal static Node GetTypeKeywordOfTypeOnlyImport(ImportClause importClause, SourceFile sourceFile)
    {
        Debug.Assert(importClause.IsTypeOnly(), "import clause must be type-only");
        // The first child of a type-only import clause is the 'type' keyword
        // import type { foo } from './bar'
        //        ^^^^
        var typeKeyword = FindChildOfKind(importClause.AsNode(), Kind.TypeKeyword, sourceFile);
        Debug.Assert(typeKeyword is not null, "type-only import clause should have a type keyword");
        return typeKeyword!;
    }

    /// <summary>Go: <c>func addElementToBindingPattern(ct *change.Tracker, file *ast.SourceFile, bindingPattern *ast.BindingPattern, name string, propertyName string)</c>（fix.go:331-344）。</summary>
    internal static void AddElementToBindingPattern(Tracker ct, SourceFile file, BindingPattern bindingPattern, string name, string propertyName)
    {
        var element = ct.NodeFactory.NewBindingElement(
            null,
            null,
            ct.NodeFactory.NewIdentifier(name),
            IfElse(propertyName == "", null, ct.NodeFactory.NewIdentifier(propertyName)));
        var nodes = bindingPattern.Elements!.Nodes!;
        if (nodes.Length > 0)
        {
            ct.InsertNodeInListAfter(file, nodes[^1], element, bindingPattern.Elements);
        }
        else
        {
            ct.ReplaceNode(file, bindingPattern.AsNode(), ct.NodeFactory.NewBindingPattern(Kind.ObjectBindingPattern, ct.NodeFactory.NewNodeList([element])), null);
        }
    }

    /// <summary>Go: <c>func getNewImports(ct *change.Tracker, moduleSpecifier string, quotePreference lsutil.QuotePreference, defaultImport *newImportBinding, namedImports []*newImportBinding, namespaceLikeImport *newImportBinding, compilerOptions *core.CompilerOptions, preferences lsutil.UserPreferences) []*ast.AnyImportSyntax</c>（fix.go:346-417）。</summary>
    internal static Node[] GetNewImports(
        Tracker ct,
        string moduleSpecifier,
        QuotePreference quotePreference,
        NewImportBinding? defaultImport,
        NewImportBinding?[] namedImports,
        NewImportBinding? namespaceLikeImport,
        CompilerOptions compilerOptions,
        UserPreferences preferences)
    {
        var tokenFlags = IfElse(quotePreference == QuotePreferenceSingle, TokenFlags.SingleQuote, TokenFlags.None);
        var moduleSpecifierStringLiteral = ct.NodeFactory.NewStringLiteral(moduleSpecifier, tokenFlags);
        List<Node> statements = [];
        if (defaultImport is not null || namedImports.Length > 0)
        {
            // `verbatimModuleSyntax` should prefer top-level `import type` -
            // even though it's not an error, it would add unnecessary runtime emit.
            var topLevelTypeOnly = ((defaultImport is null || NeedsTypeOnly(defaultImport.AddAsTypeOnly)) &&
                    Every(namedImports, i => NeedsTypeOnly(i!.AddAsTypeOnly))) ||
                ((compilerOptions.VerbatimModuleSyntax.IsTrue() || preferences.PreferTypeOnlyAutoImports.IsTrue()) &&
                    (defaultImport is null || defaultImport.AddAsTypeOnly != AddAsTypeOnly.NotAllowed) &&
                    !Some(namedImports, i => i!.AddAsTypeOnly == AddAsTypeOnly.NotAllowed));

            Node? defaultImportNode = null;
            if (defaultImport is not null)
            {
                defaultImportNode = ct.NodeFactory.NewIdentifier(defaultImport.Name);
            }

            statements.Add(MakeImport(
                ct,
                defaultImportNode,
                Map(namedImports, namedImport =>
                {
                    Node? namedImportPropertyName = null;
                    if (namedImport!.PropertyName != "")
                    {
                        namedImportPropertyName = ct.NodeFactory.NewIdentifier(namedImport.PropertyName);
                    }
                    return ct.NodeFactory.NewImportSpecifier(
                        !topLevelTypeOnly && ShouldUseTypeOnly(namedImport.AddAsTypeOnly, preferences),
                        namedImportPropertyName,
                        ct.NodeFactory.NewIdentifier(namedImport.Name));
                }),
                moduleSpecifierStringLiteral,
                topLevelTypeOnly));
        }

        if (namespaceLikeImport is not null)
        {
            Node declaration;
            if (namespaceLikeImport.Kind == ImportKind.CommonJS)
            {
                declaration = ct.NodeFactory.NewImportEqualsDeclaration(
                    /*modifiers*/ null,
                    ShouldUseTypeOnly(namespaceLikeImport.AddAsTypeOnly, preferences),
                    ct.NodeFactory.NewIdentifier(namespaceLikeImport.Name),
                    ct.NodeFactory.NewExternalModuleReference(moduleSpecifierStringLiteral));
            }
            else
            {
                declaration = ct.NodeFactory.NewImportDeclaration(
                    /*modifiers*/ null,
                    ct.NodeFactory.NewImportClause(
                        /*phaseModifier*/ IfElse(ShouldUseTypeOnly(namespaceLikeImport.AddAsTypeOnly, preferences), Kind.TypeKeyword, Kind.Unknown),
                        /*name*/ null,
                        ct.NodeFactory.NewNamespaceImport(ct.NodeFactory.NewIdentifier(namespaceLikeImport.Name))),
                    moduleSpecifierStringLiteral,
                    /*attributes*/ null);
            }
            statements.Add(declaration);
        }
        if (statements.Count == 0)
        {
            Debug.Fail("No statements to insert for new imports");
            throw new System.Diagnostics.UnreachableException();
        }
        return statements.ToArray();
    }

    /// <summary>Go: <c>func getNewRequires(changeTracker *change.Tracker, moduleSpecifier string, quotePreference lsutil.QuotePreference, defaultImport *newImportBinding, namedImports []*newImportBinding, namespaceLikeImport *newImportBinding, compilerOptions *core.CompilerOptions) []*ast.Statement</c>（fix.go:419-473）。</summary>
    internal static Node[] GetNewRequires(
        Tracker changeTracker,
        string moduleSpecifier,
        QuotePreference quotePreference,
        NewImportBinding? defaultImport,
        NewImportBinding?[] namedImports,
        NewImportBinding? namespaceLikeImport,
        CompilerOptions compilerOptions)
    {
        var quotedModuleSpecifier = changeTracker.NodeFactory.NewStringLiteral(
            moduleSpecifier,
            IfElse(quotePreference == QuotePreferenceSingle, TokenFlags.SingleQuote, TokenFlags.None));
        List<Node> statements = [];

        // const { default: foo, bar, etc } = require('./mod');
        if (defaultImport is not null || namedImports.Length > 0)
        {
            List<Node> bindingElements = [];
            foreach (var namedImport in namedImports)
            {
                Node? propertyName = null;
                if (namedImport!.PropertyName != "")
                {
                    propertyName = changeTracker.NodeFactory.NewIdentifier(namedImport.PropertyName);
                }
                bindingElements.Add(changeTracker.NodeFactory.NewBindingElement(
                    /*dotDotDotToken*/ null,
                    propertyName,
                    changeTracker.NodeFactory.NewIdentifier(namedImport.Name),
                    /*initializer*/ null));
            }
            if (defaultImport is not null)
            {
                bindingElements.Insert(0, changeTracker.NodeFactory.NewBindingElement(
                    /*dotDotDotToken*/ null,
                    changeTracker.NodeFactory.NewIdentifier("default"),
                    changeTracker.NodeFactory.NewIdentifier(defaultImport.Name),
                    /*initializer*/ null));
            }
            var declaration = CreateConstEqualsRequireDeclaration(
                changeTracker,
                changeTracker.NodeFactory.NewBindingPattern(
                    Kind.ObjectBindingPattern,
                    changeTracker.NodeFactory.NewNodeList(bindingElements.ToArray())),
                quotedModuleSpecifier);
            statements.Add(declaration);
        }

        // const foo = require('./mod');
        if (namespaceLikeImport is not null)
        {
            var declaration = CreateConstEqualsRequireDeclaration(
                changeTracker,
                changeTracker.NodeFactory.NewIdentifier(namespaceLikeImport.Name),
                quotedModuleSpecifier);
            statements.Add(declaration);
        }

        return statements.ToArray();
    }

    /// <summary>Go: <c>func createConstEqualsRequireDeclaration(changeTracker *change.Tracker, name *ast.Node, quotedModuleSpecifier *ast.Node) *ast.Statement</c>（fix.go:475-495）。</summary>
    internal static Node CreateConstEqualsRequireDeclaration(Tracker changeTracker, Node name, Node quotedModuleSpecifier)
    {
        return changeTracker.NodeFactory.NewVariableStatement(
            /*modifiers*/ null,
            changeTracker.NodeFactory.NewVariableDeclarationList(
                changeTracker.NodeFactory.NewNodeList([
                    changeTracker.NodeFactory.NewVariableDeclaration(
                        name,
                        /*exclamationToken*/ null,
                        /*type*/ null,
                        changeTracker.NodeFactory.NewCallExpression(
                            changeTracker.NodeFactory.NewIdentifier("require"),
                            /*questionDotToken*/ null,
                            /*typeArguments*/ null,
                            changeTracker.NodeFactory.NewNodeList([quotedModuleSpecifier]),
                            NodeFlags.None)),
                ]),
                NodeFlags.Const));
    }

    /// <summary>Go: <c>func insertImports(ct *change.Tracker, sourceFile *ast.SourceFile, imports []*ast.AnyImportOrRequireStatement, blankLineBetween bool, preferences lsutil.UserPreferences)</c>（fix.go:497-534）。</summary>
    internal static void InsertImports(Tracker ct, SourceFile sourceFile, Node[] imports, bool blankLineBetween, UserPreferences preferences)
    {
        Node[] existingImportStatements;

        if (imports[0].Kind == Kind.VariableStatement)
        {
            existingImportStatements = Filter(sourceFile.Statements!.Nodes, IsRequireVariableStatement);
        }
        else
        {
            existingImportStatements = Filter(sourceFile.Statements!.Nodes, IsAnyImportSyntax);
        }
        TryGetOrganizeImportsStringComparerWithDetection(existingImportStatements, preferences, out var comparer, out var isSorted);
        var sortedNewImports = (Node[])imports.Clone();
        Array.Sort(sortedNewImports, (a, b) => CompareImportsOrRequireStatements(a, b, comparer));

        if (existingImportStatements.Length > 0 && isSorted)
        {
            // Existing imports are sorted, insert each new import at the correct position
            foreach (var newImport in sortedNewImports)
            {
                var insertionIndex = GetImportDeclarationInsertIndex(existingImportStatements, newImport, (a, b) => CompareImportsOrRequireStatements(a, b, comparer));
                if (insertionIndex == 0)
                {
                    // If the first import is top-of-file, insert after the leading comment which is likely the header.
                    var leadingTriviaOption = LeadingTriviaOption.None;
                    if (existingImportStatements[0] == sourceFile.Statements.Nodes![0])
                    {
                        leadingTriviaOption = LeadingTriviaOption.Exclude;
                    }
                    ct.InsertNodeBefore(sourceFile, existingImportStatements[0], newImport, false /*blankLineBetween*/, leadingTriviaOption);
                }
                else
                {
                    var prevImport = existingImportStatements[insertionIndex - 1];
                    ct.InsertNodeAfter(sourceFile, prevImport, newImport);
                }
            }
        }
        else if (existingImportStatements.Length > 0)
        {
            ct.InsertNodesAfter(sourceFile, existingImportStatements[^1], sortedNewImports);
        }
        else
        {
            ct.InsertAtTopOfFile(sourceFile, sortedNewImports, blankLineBetween);
        }
    }

    /// <summary>Go: <c>func makeImport(ct *change.Tracker, defaultImport *ast.IdentifierNode, namedImports []*ast.Node, moduleSpecifier *ast.Expression, isTypeOnly bool) *ast.Statement</c>（fix.go:536-549）。</summary>
    internal static Node MakeImport(Tracker ct, Node? defaultImport, Node[] namedImports, Node moduleSpecifier, bool isTypeOnly)
    {
        Node? newNamedImports = null;
        if (namedImports.Length > 0)
        {
            newNamedImports = ct.NodeFactory.NewNamedImports(ct.NodeFactory.NewNodeList(namedImports));
        }
        Node? importClause = null;
        if (defaultImport is not null || newNamedImports is not null)
        {
            importClause = ct.NodeFactory.NewImportClause(IfElse(isTypeOnly, Kind.TypeKeyword, Kind.Unknown), defaultImport, newNamedImports);
        }
        return ct.NodeFactory.NewImportDeclaration(/*modifiers*/ null, importClause, moduleSpecifier, /*attributes*/ null);
    }

    /// <summary>Go: <c>func getAddAsTypeOnly(isValidTypeOnlyUseSite bool, export *Export, compilerOptions *core.CompilerOptions) lsproto.AddAsTypeOnly</c>（fix.go:621-633）。
    /// getAddAsTypeOnly determines if an import should be type-only based on usage context。
    /// （本函数在文件里排在 <see cref="GetImportKindForImportStatement"/> 之前：Go 的 fix.go 把它放在 <c>View.GetFixes</c>
    /// 之后、<c>View.tryUseExistingNamespaceImport</c> 之前，本文件把包级函数统一收在 <see cref="AutoImport"/> 里，
    /// 因此按 Go 的相对顺序落在 <c>GetFixes</c>/<c>TryUseExistingNamespaceImport</c> 之后的那一段。）</summary>
    internal static AddAsTypeOnly GetAddAsTypeOnly(bool isValidTypeOnlyUseSite, Export export, CompilerOptions compilerOptions)
    {
        if (!isValidTypeOnlyUseSite)
        {
            // Can't use a type-only import if the usage is an emitting position
            return AddAsTypeOnly.NotAllowed;
        }
        if ((compilerOptions.VerbatimModuleSyntax.IsTrue() && (export.IsTypeOnly || (export.Flags & SymbolFlags.Value) == 0)) ||
            (export.IsTypeOnly && (export.Flags & SymbolFlags.Value) != 0))
        {
            // A type-only import is required for this symbol if under verbatimModuleSyntax and it's purely a type
            return AddAsTypeOnly.Required;
        }
        return AddAsTypeOnly.Allowed;
    }

    /// <summary>Go: <c>func getNamespaceLikeImportText(declaration *ast.Node) string</c>（fix.go:673-693）。</summary>
    internal static string GetNamespaceLikeImportText(Node declaration)
    {
        switch (declaration.Kind)
        {
            case Kind.VariableDeclaration:
            {
                var name = declaration.Name();
                if (name is not null && name.Kind == Kind.Identifier)
                {
                    return name.Text();
                }
                return "";
            }
            case Kind.ImportEqualsDeclaration:
                return declaration.Name()!.Text();
            case Kind.JSDocImportTag:
            case Kind.ImportDeclaration:
            {
                var importClause = declaration.ImportClause();
                if (importClause is not null && importClause.AsImportClause()!.NamedBindings is not null && importClause.AsImportClause()!.NamedBindings!.Kind == Kind.NamespaceImport)
                {
                    return importClause.AsImportClause()!.NamedBindings!.Name()!.Text();
                }
                return "";
            }
            default:
                return "";
        }
    }

    /// <summary>Go: <c>func GetImportKindForImportStatement(importingFile *ast.SourceFile, export *Export, program *compiler.Program) lsproto.ImportKind</c>（fix.go:729-731）。</summary>
    public static ImportKind GetImportKindForImportStatement(SourceFile importingFile, Export export, TypeScript.Compiler.Program program) =>
        GetImportKind(importingFile, export, program, true /*forceImportKeyword*/);

    /// <summary>Go: <c>func getImportKind(importingFile *ast.SourceFile, export *Export, program *compiler.Program, forceImportKeyword bool) lsproto.ImportKind</c>（fix.go:733-771）。</summary>
    internal static ImportKind GetImportKind(SourceFile importingFile, Export export, TypeScript.Compiler.Program program, bool forceImportKeyword)
    {
        if (program.Options().VerbatimModuleSyntax.IsTrue() && program.GetEmitModuleFormatOfFile(importingFile) == ModuleKind.CommonJS)
        {
            return ImportKind.CommonJS;
        }
        switch (export.Syntax)
        {
            case ExportSyntax.DefaultModifier:
            case ExportSyntax.DefaultDeclaration:
                return ImportKind.Default;
            case ExportSyntax.Named:
                if (export.ExportName == InternalSymbolNameDefault)
                {
                    return ImportKind.Default;
                }
                goto case ExportSyntax.Modifier;
            case ExportSyntax.Modifier:
            case ExportSyntax.Star:
            case ExportSyntax.CommonJSExportsProperty:
                return ImportKind.Named;
            case ExportSyntax.Equals:
            case ExportSyntax.CommonJSModuleExports:
            case ExportSyntax.UMD:
                // export.Syntax will be ExportSyntaxEquals for named exports/properties of an export='s target.
                if (export.ExportName != InternalSymbolNameExportEquals)
                {
                    return ImportKind.Named;
                }
                // !!! cache this?
                foreach (var statement in importingFile.Statements!.Nodes!)
                {
                    // `import foo` parses as an ImportEqualsDeclaration even though it could be an ImportDeclaration
                    if (IsImportEqualsDeclaration(statement) && !NodeIsMissing(statement.AsImportEqualsDeclaration()!.ModuleReference))
                    {
                        return ImportKind.CommonJS;
                    }
                }
                // !!! this logic feels weird; we're basically trying to predict if shouldUseRequire is going to
                //     be true. The meaning of "default import" is different depending on whether we write it as
                //     a require or an es6 import. The latter, compiled to CJS, has interop built in that will
                //     avoid accessing .default, but if we write a require directly and call it a default import,
                //     we emit an unconditional .default access.
                if (importingFile.ExternalModuleIndicator is not null || forceImportKeyword || !IsSourceFileJS(importingFile))
                {
                    return ImportKind.Default;
                }
                return ImportKind.CommonJS;
            default:
                Debug.Fail("unhandled export syntax kind: " + export.Syntax.ToString());
                throw new System.Diagnostics.UnreachableException();
        }
    }

    /// <summary>Go: <c>func detectSyntax(file *ast.SourceFile, options *core.CompilerOptions) fileSyntaxKind</c>（fix.go:861-873）。</summary>
    internal static FileSyntaxKind DetectSyntax(SourceFile file, CompilerOptions options)
    {
        var (hasESM, hasCJS) = DetectSyntaxIndicators(file, options);
        if (hasCJS && !hasESM)
        {
            return FileSyntaxKind.CJS;
        }
        if (hasESM && !hasCJS)
        {
            return FileSyntaxKind.ESM;
        }
        return FileSyntaxKind.Ambiguous;
    }

    /// <summary>Go: <c>func detectSyntaxIndicators(file *ast.SourceFile, options *core.CompilerOptions) (hasESM bool, hasCJS bool)</c>（fix.go:875-910）。</summary>
    internal static (bool HasESM, bool HasCJS) DetectSyntaxIndicators(SourceFile file, CompilerOptions options)
    {
        var hasCJS = file.CommonJSModuleIndicator is not null;
        if (options.GetEmitModuleDetectionKind() != ModuleDetectionKind.Force)
        {
            // ExternalModuleIndicator is reliable when moduleDetection is not "force"
            var hasESM = file.ExternalModuleIndicator is not null;
            return (hasESM, hasCJS);
        }
        // Under moduleDetection "force", ExternalModuleIndicator is set to
        // file.AsNode() when there is no genuine ESM syntax, so only trust it
        // when it points to a real statement node.
        if (file.ExternalModuleIndicator is not null && file.ExternalModuleIndicator != file.AsNode())
        {
            return (true, hasCJS);
        }
        // Fall back to scanning Imports() for actual import/export declarations
        // (not require() calls or dynamic imports).
        foreach (var imp in file.Imports())
        {
            if ((imp.Flags & NodeFlags.Synthesized) != 0)
            {
                continue;
            }
            var parent = imp.Parent;
            if (parent is null)
            {
                continue;
            }
            switch (parent.Kind)
            {
                case Kind.ImportDeclaration:
                case Kind.JSImportDeclaration:
                case Kind.ExportDeclaration:
                    return (true, hasCJS);
                case Kind.ExternalModuleReference:
                    // import x = require("...") — this is ESM-ish syntax
                    return (true, hasCJS);
            }
        }
        // Go 的具名返回值 hasESM 在此分支上仍是零值 false（见文件头第 13 条）。
        return (false, hasCJS);
    }

    /// <summary>Go: <c>func needsTypeOnly(addAsTypeOnly lsproto.AddAsTypeOnly) bool</c>（fix.go:952-954）。</summary>
    internal static bool NeedsTypeOnly(AddAsTypeOnly addAsTypeOnly) => addAsTypeOnly == AddAsTypeOnly.Required;

    /// <summary>Go: <c>func shouldUseTypeOnly(addAsTypeOnly lsproto.AddAsTypeOnly, preferences lsutil.UserPreferences) bool</c>（fix.go:956-958）。</summary>
    internal static bool ShouldUseTypeOnly(AddAsTypeOnly addAsTypeOnly, UserPreferences preferences) =>
        NeedsTypeOnly(addAsTypeOnly) || (addAsTypeOnly != AddAsTypeOnly.NotAllowed && preferences.PreferTypeOnlyAutoImports.IsTrue());

    /// <summary>Go: <c>func compareFixKinds(a, b lsproto.AutoImportFixKind) int</c>（fix.go:977-979）。</summary>
    internal static int CompareFixKinds(AutoImportFixKind a, AutoImportFixKind b) => (int)a - (int)b;

    /// <summary>Go: <c>func isFixPossiblyReExportingImportingFile(fix *Fix, importingFileName string) bool</c>（fix.go:1031-1037）。</summary>
    internal static bool IsFixPossiblyReExportingImportingFile(Fix fix, string importingFileName)
    {
        if (fix.IsReExport && IsIndexFileName(fix.ModuleFileName))
        {
            var reExportDir = GetDirectoryPath(fix.ModuleFileName);
            return importingFileName.StartsWith(EnsureTrailingDirectorySeparator(reExportDir), StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>Go: <c>func isIndexFileName(fileName string) bool</c>（fix.go:1039-1050）。</summary>
    internal static bool IsIndexFileName(string fileName)
    {
        var lastSlash = fileName.LastIndexOf('/');
        if (lastSlash < 0 || fileName.Length <= lastSlash + 1)
        {
            return false;
        }
        fileName = fileName[(lastSlash + 1)..];
        switch (fileName)
        {
            case "index.js":
            case "index.jsx":
            case "index.d.ts":
            case "index.ts":
            case "index.tsx":
                return true;
        }
        return false;
    }

    /// <summary>Go: <c>func promoteFromTypeOnly(changes *change.Tracker, aliasDeclaration *ast.Declaration, compilerOptions *core.CompilerOptions, sourceFile *ast.SourceFile, preferences lsutil.UserPreferences) *ast.Declaration</c>（fix.go:1052-1127）。</summary>
    internal static Node? PromoteFromTypeOnly(
        Tracker changes,
        Node aliasDeclaration,
        CompilerOptions compilerOptions,
        SourceFile sourceFile,
        UserPreferences preferences)
    {
        // See comment in `doAddExistingFix` on constant with the same name.
        var convertExistingToTypeOnly = compilerOptions.VerbatimModuleSyntax;

        switch (aliasDeclaration.Kind)
        {
            case Kind.ImportSpecifier:
            {
                var spec = aliasDeclaration.AsImportSpecifier()!;
                if (spec.IsTypeOnly)
                {
                    if (spec.Parent is not null && spec.Parent.Kind == Kind.NamedImports)
                    {
                        var namedImportsNode = spec.Parent.AsNamedImports()!;
                        var elements = namedImportsNode.Elements!.Nodes!;
                        if (elements.Length > 1)
                        {
                            // Create a synthetic specifier with isTypeOnly=false to compute sorted position
                            Node? propertyName = null;
                            if (spec.PropertyName is not null)
                            {
                                propertyName = changes.NodeFactory.NewIdentifier(spec.PropertyName.Text());
                            }
                            var newSpecifier = changes.NodeFactory.NewImportSpecifier(
                                false, // isTypeOnly = false
                                propertyName,
                                changes.NodeFactory.NewIdentifier(spec.Name()!.Text()));
                            var (specifierComparer, _) = GetNamedImportSpecifierComparerWithDetection(
                                spec.Parent.Parent!.Parent!, // ImportDeclaration
                                sourceFile,
                                preferences);
                            var insertionIndex = GetImportSpecifierInsertionIndex(elements, newSpecifier, specifierComparer);
                            var currentIndex = Array.IndexOf(elements, aliasDeclaration);
                            if (insertionIndex != currentIndex)
                            {
                                changes.Delete(sourceFile, aliasDeclaration);
                                changes.InsertImportSpecifierAtIndex(sourceFile, newSpecifier, spec.Parent, insertionIndex);
                                return aliasDeclaration;
                            }
                        }
                        // If no re-sorting needed, just remove the 'type' keyword
                        var firstToken = GetFirstToken(aliasDeclaration, sourceFile);
                        var typeKeywordPos = GetTokenPosOfNode(firstToken!, sourceFile, false);
                        Node targetNode;
                        if (spec.PropertyName is not null)
                        {
                            targetNode = spec.PropertyName;
                        }
                        else
                        {
                            targetNode = spec.Name()!;
                        }
                        var targetPos = GetTokenPosOfNode(targetNode, sourceFile, false);
                        changes.DeleteRange(sourceFile, NewTextRange(typeKeywordPos, targetPos));
                    }
                    return aliasDeclaration;
                }
                else
                {
                    // The parent import clause is type-only
                    if (spec.Parent is null || spec.Parent.Kind != Kind.NamedImports)
                    {
                        Debug.Fail("ImportSpecifier parent must be NamedImports");
                        throw new System.Diagnostics.UnreachableException();
                    }
                    if (spec.Parent.Parent is null || spec.Parent.Parent.Kind != Kind.ImportClause)
                    {
                        Debug.Fail("NamedImports parent must be ImportClause");
                        throw new System.Diagnostics.UnreachableException();
                    }
                    PromoteImportClause(changes, spec.Parent.Parent.AsImportClause()!, compilerOptions, sourceFile, preferences, convertExistingToTypeOnly, aliasDeclaration);
                    return spec.Parent.Parent;
                }
            }

            case Kind.ImportClause:
                PromoteImportClause(changes, aliasDeclaration.AsImportClause()!, compilerOptions, sourceFile, preferences, convertExistingToTypeOnly, aliasDeclaration);
                return aliasDeclaration;

            case Kind.NamespaceImport:
                // Promote the parent import clause
                if (aliasDeclaration.Parent is null || aliasDeclaration.Parent.Kind != Kind.ImportClause)
                {
                    Debug.Fail("NamespaceImport parent must be ImportClause");
                    throw new System.Diagnostics.UnreachableException();
                }
                PromoteImportClause(changes, aliasDeclaration.Parent.AsImportClause()!, compilerOptions, sourceFile, preferences, convertExistingToTypeOnly, aliasDeclaration);
                return aliasDeclaration.Parent;

            case Kind.ImportEqualsDeclaration:
            {
                // Remove the 'type' keyword (which is the second token: 'import' 'type' name '=' ...)
                var importEqDecl = aliasDeclaration.AsImportEqualsDeclaration()!;
                // The type keyword is after 'import' and before the name
                var scan = GetScannerForSourceFile(sourceFile, importEqDecl.Pos());
                // Skip 'import' keyword to get to 'type'
                scan.Scan();
                DeleteTypeKeyword(changes, sourceFile, scan.TokenStart());
                return aliasDeclaration;
            }

            default:
                Debug.Fail("Unexpected alias declaration kind: " + aliasDeclaration.Kind);
                throw new System.Diagnostics.UnreachableException();
        }
    }

    /// <summary>Go: <c>func promoteImportClause(changes *change.Tracker, importClause *ast.ImportClause, compilerOptions *core.CompilerOptions, sourceFile *ast.SourceFile, preferences lsutil.UserPreferences, convertExistingToTypeOnly core.Tristate, aliasDeclaration *ast.Declaration)</c>（fix.go:1129-1204）。
    /// promoteImportClause removes the type keyword from an import clause。</summary>
    internal static void PromoteImportClause(
        Tracker changes,
        ImportClause importClause,
        CompilerOptions compilerOptions,
        SourceFile sourceFile,
        UserPreferences preferences,
        Tristate convertExistingToTypeOnly,
        Node? aliasDeclaration)
    {
        // Delete the 'type' keyword
        if (importClause.PhaseModifier == Kind.TypeKeyword)
        {
            DeleteTypeKeyword(changes, sourceFile, importClause.Pos());
        }

        // Handle .ts extension conversion to .js if necessary
        if (compilerOptions.AllowImportingTsExtensions.IsFalse())
        {
            var moduleSpecifier = TypeScript.Checker.Checker.TryGetModuleSpecifierFromDeclaration(importClause.Parent);
            if (moduleSpecifier is not null)
            {
                // Note: We can't check ResolvedUsingTsExtension without program, so we'll skip this optimization
                // The fix will still work, just might not change .ts to .js extensions in all cases
            }
        }

        // Handle verbatimModuleSyntax conversion
        // If convertExistingToTypeOnly is true, we need to add 'type' to other specifiers
        // in the same import declaration
        if (convertExistingToTypeOnly.IsTrue())
        {
            var namedImports = importClause.NamedBindings;
            if (namedImports is not null && namedImports.Kind == Kind.NamedImports)
            {
                var namedImportsData = namedImports.AsNamedImports()!;
                var elements = namedImportsData.Elements!.Nodes!;
                if (elements.Length > 1)
                {
                    // Check if the list is sorted and if we need to reorder
                    var (_, isSorted) = GetNamedImportSpecifierComparerWithDetection(
                        importClause.Parent!,
                        sourceFile,
                        preferences);

                    // If the alias declaration is an ImportSpecifier and the list is sorted,
                    // move it to index 0 (since it will be the only non-type-only import)
                    if (!isSorted.IsFalse() && // isSorted !== false
                        aliasDeclaration is not null &&
                        aliasDeclaration.Kind == Kind.ImportSpecifier)
                    {
                        // Find the index of the alias declaration
                        var aliasIndex = Array.IndexOf(elements, aliasDeclaration);
                        // If not already at index 0, move it there
                        if (aliasIndex > 0)
                        {
                            // Delete the specifier from its current position
                            changes.Delete(sourceFile, aliasDeclaration);
                            // Insert it at index 0
                            changes.InsertImportSpecifierAtIndex(sourceFile, aliasDeclaration, namedImports, 0);
                        }
                    }

                    // Add 'type' keyword to all other import specifiers that aren't already type-only
                    foreach (var element in elements)
                    {
                        var spec = element.AsImportSpecifier()!;
                        // Skip the specifier being promoted (if aliasDeclaration is an ImportSpecifier)
                        if (aliasDeclaration is not null && aliasDeclaration.Kind == Kind.ImportSpecifier)
                        {
                            if (element == aliasDeclaration)
                            {
                                continue;
                            }
                        }
                        // Skip if already type-only
                        if (!spec.IsTypeOnly)
                        {
                            changes.InsertModifierBefore(sourceFile, Kind.TypeKeyword, element);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Go: <c>func deleteTypeKeyword(changes *change.Tracker, sourceFile *ast.SourceFile, startPos int)</c>（fix.go:1206-1220）。
    /// deleteTypeKeyword deletes the 'type' keyword token starting at the given position, including any trailing whitespace.</summary>
    internal static void DeleteTypeKeyword(Tracker changes, SourceFile sourceFile, int startPos)
    {
        var scan = GetScannerForSourceFile(sourceFile, startPos);
        if (scan.Token() != Kind.TypeKeyword)
        {
            return;
        }
        var typeStart = scan.TokenStart();
        var typeEnd = scan.TokenEnd();
        // Skip trailing whitespace
        var text = sourceFile.Text();
        while (typeEnd < text.Length && (text[typeEnd] == ' ' || text[typeEnd] == '\t'))
        {
            typeEnd++;
        }
        changes.DeleteRange(sourceFile, NewTextRange(typeStart, typeEnd));
    }

    /// <summary>Go: <c>func getModuleSpecifierText(promotedDeclaration *ast.Node) string</c>（fix.go:1222-1240）。</summary>
    internal static string GetModuleSpecifierText(Node promotedDeclaration)
    {
        if (promotedDeclaration.Kind == Kind.ImportEqualsDeclaration)
        {
            var importEqualsDeclaration = promotedDeclaration.AsImportEqualsDeclaration()!;
            if (IsExternalModuleReference(importEqualsDeclaration.ModuleReference!))
            {
                var expr = importEqualsDeclaration.ModuleReference!.Expression();
                if (expr is not null)
                {
                    if (IsStringLiteralLike(expr))
                    {
                        return expr.Text();
                    }
                    return GetTextOfNode(expr);
                }
            }
            return GetTextOfNode(importEqualsDeclaration.ModuleReference!);
        }
        var moduleSpecifier = promotedDeclaration.Parent!.ModuleSpecifier();
        if (IsStringLiteralLike(moduleSpecifier))
        {
            return moduleSpecifier!.Text();
        }
        return GetTextOfNode(moduleSpecifier!);
    }

    /// <summary>Go: <c>func compareModuleSpecifierRelativity(a *Fix, b *Fix, preferences modulespecifiers.UserPreferences) int</c>（fix.go:1242-1249）。
    /// returns `-1` if `a` is better than `b`。</summary>
    internal static int CompareModuleSpecifierRelativity(Fix a, Fix b, ModSpecPrefs preferences)
    {
        switch (preferences.ImportModuleSpecifierPreference.Value)
        {
            case ImportModuleSpecifierPreferenceNonRelative:
            case ImportModuleSpecifierPreferenceProjectRelative:
                return CompareBooleans(
                    a.ModuleSpecifierKind == ResultKind.Relative,
                    b.ModuleSpecifierKind == ResultKind.Relative);
        }
        return 0;
    }
}

/// <summary>Go: <c>func (v *View) GetFixes(export *Export, forJSX bool, isValidTypeOnlyUseSite bool, usagePosition *lsproto.Position) []*Fix</c>（fix.go:551-619）等 view 方法（fix.go:551-950）。</summary>
public sealed partial class View
{
    /// <summary>Go: <c>func (v *View) GetFixes(export *Export, forJSX bool, isValidTypeOnlyUseSite bool, usagePosition *lsproto.Position) []*Fix</c>（fix.go:551-619）。</summary>
    internal Fix[] GetFixes(Export export, bool forJSX, bool isValidTypeOnlyUseSite, Position? usagePosition)
    {
        List<Fix>? fixes = null;
        if (TryUseExistingNamespaceImport(export, usagePosition) is { } namespaceFix)
        {
            fixes = [namespaceFix];
        }

        if (TryAddToExistingImport(export, isValidTypeOnlyUseSite) is { } fix)
        {
            return (fixes ??= []).Append(fix).ToArray();
        }

        // !!! getNewImportFromExistingSpecifier - even worth it?

        var (moduleSpecifier, moduleSpecifierKind) = GetModuleSpecifier(export, _preferences);
        if (moduleSpecifier == "")
        {
            if (fixes is not null && fixes.Count > 0)
            {
                return fixes.ToArray();
            }
            return [];
        }

        // Check if we need a JSDoc import type fix (for JS files with type-only imports)
        var isJs = HasJSFileExtension(_importingFile.FileName());
        var importedSymbolHasValueMeaning = (export.Flags & SymbolFlags.Value) != 0 || export.IsUnresolvedAlias();
        if (!importedSymbolHasValueMeaning && isJs && usagePosition is not null)
        {
            // For pure types in JS files, use JSDoc import type syntax
            fixes = [new Fix
            {
                AutoImportFix = new AutoImportFix
                {
                    Kind = AutoImportFixKind.JsdocTypeImport,
                    ModuleSpecifier = moduleSpecifier,
                    Name = export.Name(),
                    UsagePosition = usagePosition,
                },
                ModuleSpecifierKind = moduleSpecifierKind,
                IsReExport = export.Target.ModuleID != export.ModuleID,
                ModuleFileName = export.ModuleFileName,
            }];
            return fixes.ToArray();
        }

        var importKind = AutoImport.GetImportKind(_importingFile, export, _program, false /*forceImportKeyword*/);
        var addAsTypeOnly = AutoImport.GetAddAsTypeOnly(isValidTypeOnlyUseSite, export, _program.Options());

        var name = export.Name();
        var startsWithUpper = RuneUtil.IsUpperRune(name[0]);
        if (forJSX && !startsWithUpper)
        {
            if (export.IsRenameable())
            {
                name = char.ConvertFromUtf32(RuneUtil.ToUpperRune(name[0])) + name[1..];
            }
            else
            {
                return [];
            }
        }

        (fixes ??= []).Add(new Fix
        {
            AutoImportFix = new AutoImportFix
            {
                Kind = AutoImportFixKind.AddNew,
                ImportKind = importKind,
                ModuleSpecifier = moduleSpecifier,
                Name = name,
                UseRequire = ShouldUseRequire(),
                AddAsTypeOnly = addAsTypeOnly,
            },
            ModuleSpecifierKind = moduleSpecifierKind,
            IsReExport = export.Target.ModuleID != export.ModuleID,
            ModuleFileName = export.ModuleFileName,
        });
        return fixes.ToArray();
    }

    /// <summary>Go: <c>func (v *View) tryUseExistingNamespaceImport(export *Export, usagePosition *lsproto.Position) *Fix</c>（fix.go:644-671）。</summary>
    internal Fix? TryUseExistingNamespaceImport(Export export, Position? usagePosition)
    {
        if (usagePosition is null)
        {
            return null;
        }

        if (AutoImport.GetImportKind(_importingFile, export, _program, false /*forceImportKeyword*/) != ImportKind.Named)
        {
            return null;
        }

        var existingImports = GetExistingImports();
        var matchingDeclarations = existingImports.Get(export.ModuleID) ?? [];
        foreach (var existingImport in matchingDeclarations)
        {
            var namespacePrefix = AutoImport.GetNamespaceLikeImportText(existingImport._node);
            if (namespacePrefix == "" || existingImport._moduleSpecifier == "")
            {
                continue;
            }
            return new Fix
            {
                AutoImportFix = new AutoImportFix
                {
                    Kind = AutoImportFixKind.UseNamespace,
                    Name = export.Name(),
                    ModuleSpecifier = existingImport._moduleSpecifier,
                    ImportKind = ImportKind.Namespace,
                    AddAsTypeOnly = AddAsTypeOnly.Allowed,
                    ImportIndex = existingImport._index,
                    UsagePosition = usagePosition,
                    NamespacePrefix = namespacePrefix,
                },
            };
        }

        return null;
    }

    /// <summary>Go: <c>func (v *View) tryAddToExistingImport(export *Export, isValidTypeOnlyUseSite bool) *Fix</c>（fix.go:695-789）。</summary>
    internal Fix? TryAddToExistingImport(Export export, bool isValidTypeOnlyUseSite)
    {
        var existingImports = GetExistingImports();
        var matchingDeclarations = existingImports.Get(export.ModuleID) ?? [];
        if (matchingDeclarations.Count == 0)
        {
            return null;
        }

        // Can't use an es6 import for a type in JS.
        if (IsSourceFileJS(_importingFile) && (export.Flags & SymbolFlags.Value) == 0 && !Every(matchingDeclarations.ToArray(), i => IsJSDocImportTag(i._node)))
        {
            return null;
        }

        var importKind = AutoImport.GetImportKind(_importingFile, export, _program, false /*forceImportKeyword*/);
        if (importKind == ImportKind.CommonJS || importKind == ImportKind.Namespace)
        {
            return null;
        }

        var addAsTypeOnly = AutoImport.GetAddAsTypeOnly(isValidTypeOnlyUseSite, export, _program.Options());

        Fix? best = null;
        foreach (var existingImport in matchingDeclarations)
        {
            if (existingImport._node.Kind == Kind.ImportEqualsDeclaration)
            {
                continue;
            }

            if (existingImport._node.Kind == Kind.VariableDeclaration)
            {
                if ((importKind == ImportKind.Named || importKind == ImportKind.Default) && existingImport._node.Name()!.Kind == Kind.ObjectBindingPattern)
                {
                    var fix = new Fix
                    {
                        AutoImportFix = new AutoImportFix
                        {
                            Kind = AutoImportFixKind.AddToExisting,
                            Name = export.Name(),
                            ImportKind = importKind,
                            ImportIndex = existingImport._index,
                            ModuleSpecifier = existingImport._moduleSpecifier,
                            AddAsTypeOnly = addAsTypeOnly,
                        },
                    };
                    // Variable declarations are never type-only.
                    // Give preference to putting types in existing type-only imports and avoiding conversions
                    // of import statements to/from type-only.
                    if (addAsTypeOnly == AddAsTypeOnly.NotAllowed)
                    {
                        return fix;
                    }
                    if (best is null)
                    {
                        best = fix;
                    }
                }
                continue;
            }

            var importClauseNode = existingImport._node.ImportClause();
            if (importClauseNode is null || !IsStringLiteralLike(existingImport._node.ModuleSpecifier()))
            {
                // Side-effect import (no import clause) - can't add to it
                continue;
            }
            var importClause = importClauseNode.AsImportClause()!;

            var namedBindings = importClause.NamedBindings;
            // A type-only import may not have both a default and named imports, so the only way a name can
            // be added to an existing type-only import is adding a named import to existing named bindings.
            if (importClause.IsTypeOnly() && !(importKind == ImportKind.Named && namedBindings is not null))
            {
                continue;
            }

            if (importKind == ImportKind.Default && (importClause.Name() is not null ||
                // Cannot add a default import as type-only if the import already has named bindings
                (addAsTypeOnly == AddAsTypeOnly.Required && namedBindings is not null)))
            {
                continue;
            }

            // Cannot add a named import to a declaration that has a namespace import
            if (importKind == ImportKind.Named && namedBindings is not null && namedBindings.Kind == Kind.NamespaceImport)
            {
                continue;
            }

            var candidate = new Fix
            {
                AutoImportFix = new AutoImportFix
                {
                    Kind = AutoImportFixKind.AddToExisting,
                    Name = export.Name(),
                    ImportKind = importKind,
                    ImportIndex = existingImport._index,
                    ModuleSpecifier = existingImport._moduleSpecifier,
                    AddAsTypeOnly = addAsTypeOnly,
                },
            };

            var isTypeOnly = importClause.IsTypeOnly();
            // Give preference to putting types in existing type-only imports and avoiding conversions
            // of import statements to/from type-only.
            if ((addAsTypeOnly != AddAsTypeOnly.NotAllowed && isTypeOnly) ||
                (addAsTypeOnly == AddAsTypeOnly.NotAllowed && !isTypeOnly))
            {
                return candidate;
            }
            if (best is null)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Go: <c>func (v *View) getExistingImports() *collections.MultiMap[ModuleID, existingImport]</c>（fix.go:789-812）。</summary>
    internal MultiMap<ModuleID, ExistingImport> GetExistingImports()
    {
        if (_existingImports is not null)
        {
            return _existingImports;
        }

        var result = NewMultiMapWithSizeHint<ModuleID, ExistingImport>(_importingFile.Imports().Length);

        var imports = _importingFile.Imports();
        for (var i = 0; i < imports.Length; i++)
        {
            var moduleSpecifier = imports[i];
            var node = TryGetImportFromModuleSpecifier(moduleSpecifier);
            if (node is null)
            {
                Debug.Fail("error: did not expect node kind " + moduleSpecifier.Kind.String());
                throw new System.Diagnostics.UnreachableException();
            }
            else if (IsVariableDeclarationInitializedToRequire(node.Parent))
            {
                if (_checker.ResolveExternalModuleName(moduleSpecifier, null /*importAttributesType*/) is { } moduleSymbol)
                {
                    if (AutoImport.TryGetModuleIDAndFileNameOfModuleSymbol(moduleSymbol, out var moduleID, out _))
                    {
                        result.Add(moduleID, new ExistingImport { _node = node.Parent!, _moduleSpecifier = moduleSpecifier.Text(), _index = i });
                    }
                }
            }
            else if (node.Kind == Kind.ImportDeclaration || node.Kind == Kind.ImportEqualsDeclaration || node.Kind == Kind.JSDocImportTag)
            {
                if (_checker.GetSymbolAtLocation(moduleSpecifier) is { } moduleSymbol)
                {
                    if (AutoImport.TryGetModuleIDAndFileNameOfModuleSymbol(moduleSymbol, out var moduleID, out _))
                    {
                        result.Add(moduleID, new ExistingImport { _node = node, _moduleSpecifier = moduleSpecifier.Text(), _index = i });
                    }
                }
            }
        }
        _existingImports = result;
        return result;
    }

    /// <summary>Go: <c>func (v *View) shouldUseRequire() bool</c>（fix.go:814-821）。</summary>
    internal bool ShouldUseRequire()
    {
        if (_shouldUseRequireForFixes is { } cached)
        {
            return cached;
        }
        var shouldUseRequire = ComputeShouldUseRequire();
        _shouldUseRequireForFixes = shouldUseRequire;
        return shouldUseRequire;
    }

    /// <summary>Go: <c>func (v *View) computeShouldUseRequire() bool</c>（fix.go:912-950）。</summary>
    internal bool ComputeShouldUseRequire()
    {
        // 1. TypeScript files don't use require variable declarations
        if (!HasJSFileExtension(_importingFile.FileName()))
        {
            return false;
        }

        // 2. If the current source file is unambiguously CJS or ESM, go with that
        switch (AutoImport.DetectSyntax(_importingFile, _program.Options()))
        {
            case FileSyntaxKind.CJS:
                return true;
            case FileSyntaxKind.ESM:
                return false;
        }

        // 3. Use the implied node format to determine CJS vs ESM
        //    TODO: consider removing `impliedNodeFormatForEmit`
        switch (_program.GetImpliedNodeFormatForEmit(_importingFile))
        {
            case ModuleKind.CommonJS:
                return true;
            case ModuleKind.ESNext:
                return false;
        }

        // 4. If there's a tsconfig/jsconfig, use its module setting
        if (_program.Options().ConfigFilePath != "")
        {
            return _program.Options().GetEmitModuleKind() < ModuleKind.ES2015;
        }

        // 5. Match the first other JS file in the program that's unambiguously CJS or ESM
        foreach (var otherFile in _program.GetSourceFiles())
        {
            if (otherFile == _importingFile || !IsSourceFileJS(otherFile) || _program.IsSourceFileFromExternalLibrary(otherFile))
            {
                continue;
            }
            switch (AutoImport.DetectSyntax(otherFile, _program.Options()))
            {
                case FileSyntaxKind.CJS:
                    return true;
                case FileSyntaxKind.ESM:
                    return false;
            }
        }

        // 6. Literally nothing to go on
        return true;
    }

    /// <summary>Go: <c>func (v *View) CompareFixesForSorting(a, b *Fix) int</c>（fix.go:964-969）。
    /// CompareFixesForSorting returns negative if `a` is better than `b`。Sorting with this comparator will place the
    /// best fix first. After rank sorting, fixes will be sorted by arbitrary but stable criteria to ensure a
    /// deterministic order.</summary>
    internal int CompareFixesForSorting(Fix a, Fix b)
    {
        var res = CompareFixesForRanking(a, b);
        if (res != 0)
        {
            return res;
        }
        return CompareModuleSpecifiersForSorting(a, b);
    }

    /// <summary>Go: <c>func (v *View) CompareFixesForRanking(a, b *Fix) int</c>（fix.go:971-979）。
    /// CompareFixesForRanking returns negative if `a` is better than `b`。Sorting with this comparator will place the
    /// best fix first. Fixes of equal desirability will be considered equal.</summary>
    internal int CompareFixesForRanking(Fix a, Fix b)
    {
        var res = AutoImport.CompareFixKinds(a.AutoImportFix.Kind, b.AutoImportFix.Kind);
        if (res != 0)
        {
            return res;
        }
        return CompareModuleSpecifiersForRanking(a, b);
    }

    /// <summary>Go: <c>func (v *View) compareModuleSpecifiersForRanking(a, b *Fix) int</c>（fix.go:985-1011）。</summary>
    internal int CompareModuleSpecifiersForRanking(Fix a, Fix b)
    {
        var comparison = AutoImport.CompareModuleSpecifierRelativity(a, b, _preferences);
        if (comparison != 0)
        {
            return comparison;
        }
        if (a.ModuleSpecifierKind == ResultKind.Ambient && b.ModuleSpecifierKind == ResultKind.Ambient)
        {
            comparison = CompareNodeCoreModuleSpecifiers(a.AutoImportFix.ModuleSpecifier, b.AutoImportFix.ModuleSpecifier, _importingFile, _program);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        if (a.ModuleSpecifierKind == ResultKind.Relative && b.ModuleSpecifierKind == ResultKind.Relative)
        {
            comparison = CompareBooleans(
                AutoImport.IsFixPossiblyReExportingImportingFile(a, _importingFile.FileName()),
                AutoImport.IsFixPossiblyReExportingImportingFile(b, _importingFile.FileName()));
            if (comparison != 0)
            {
                return comparison;
            }
        }
        comparison = CompareNumberOfDirectorySeparators(a.AutoImportFix.ModuleSpecifier, b.AutoImportFix.ModuleSpecifier);
        if (comparison != 0)
        {
            return comparison;
        }
        return 0;
    }

    /// <summary>Go: <c>func (v *View) compareModuleSpecifiersForSorting(a, b *Fix) int</c>（fix.go:1013-1033）。</summary>
    internal int CompareModuleSpecifiersForSorting(Fix a, Fix b)
    {
        var res = CompareModuleSpecifiersForRanking(a, b);
        if (res != 0)
        {
            return res;
        }
        // Sort ./foo before ../foo for equal-length specifiers
        if (a.AutoImportFix.ModuleSpecifier.StartsWith("./", StringComparison.Ordinal) && !b.AutoImportFix.ModuleSpecifier.StartsWith("./", StringComparison.Ordinal))
        {
            return -1;
        }
        if (b.AutoImportFix.ModuleSpecifier.StartsWith("./", StringComparison.Ordinal) && !a.AutoImportFix.ModuleSpecifier.StartsWith("./", StringComparison.Ordinal))
        {
            return 1;
        }
        var comparison = string.CompareOrdinal(a.AutoImportFix.ModuleSpecifier, b.AutoImportFix.ModuleSpecifier);
        if (comparison != 0)
        {
            return comparison;
        }
        comparison = ((int)a.AutoImportFix.ImportKind).CompareTo((int)b.AutoImportFix.ImportKind);
        if (comparison != 0)
        {
            return comparison;
        }
        // !!! further tie-breakers? In practice this is only called on fixes with the same name
        return 0;
    }

    /// <summary>Go: <c>func (v *View) compareNodeCoreModuleSpecifiers(a, b string, importingFile *ast.SourceFile, program *compiler.Program) int</c>（fix.go:1035-1054）。</summary>
    internal int CompareNodeCoreModuleSpecifiers(string a, string b, SourceFile importingFile, TypeScript.Compiler.Program program)
    {
        if (a.StartsWith("node:", StringComparison.Ordinal) && !b.StartsWith("node:", StringComparison.Ordinal))
        {
            if (_shouldUseUriStyleNodeCoreModules.IsTrue())
            {
                return -1;
            }
            else if (_shouldUseUriStyleNodeCoreModules.IsFalse())
            {
                return 1;
            }
            return 0;
        }
        if (b.StartsWith("node:", StringComparison.Ordinal) && !a.StartsWith("node:", StringComparison.Ordinal))
        {
            if (_shouldUseUriStyleNodeCoreModules.IsTrue())
            {
                return 1;
            }
            else if (_shouldUseUriStyleNodeCoreModules.IsFalse())
            {
                return -1;
            }
        }
        return 0;
    }
}
