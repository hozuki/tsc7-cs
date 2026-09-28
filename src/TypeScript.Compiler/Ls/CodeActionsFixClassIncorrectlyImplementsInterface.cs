// ported from: internal/ls/codeactions_fixclassincorrectlyimplementsinterface.go:1-241
//
// 与 Go 的偏离（逐条）：
// 1) 包级未导出常量/变量 → `Ls` 静态类的 `internal const` / `internal static readonly` 字段，命名沿用 Go 的
//    `_camelCase`（artifacts/ls-g1-helpers.md §0）。包级未导出函数 → 同一个 `Ls` 静态类的 `internal static`
//    方法（PascalCase，方法不带 `_`）。Go 导出变量 `FixClassIncorrectlyImplementsInterfaceProvider` → `public`。
// 2) Go 的 `codeFixProviders` 注册表与 `CodeFixProvider`/`CodeFixContext`/`CodeAction`/`CombinedCodeActions`
//    由 G7 的 `Ls/CodeActions.cs` 落地 → 本文件直接引用（不重复声明）。
// 3) Go 的 `(T, error)` → C# 的具名元组 `(T?, Exception?)`（先例 HandleCrossProject）。
// 4) `change.NewTracker(...)` → `LsChange.NewTracker(...)`（`TypeScript.LsChange` 命名空间与 `LsChange` 类型同名，
//    故写全限定静态类名 `LsChange.NewTracker`）；`change.Tracker` → `LsChange.Tracker`；
//    `change.LeadingTriviaOptionNone` 本文件未用。
// 5) `fixContext.LS.converters`（未导出字段）→ `fixContext.LS._converters`。
// 6) `fixContext.LS.getPreparedAutoImportView(...)` → `fixContext.LS.GetPreparedAutoImportView(...)`（返回
//    `(View?, Exception?)`）。
// 7) Go 的 `*lsproto.TextEdit` 切片 → `TextEdit[]`；`map[fileName][]*TextEdit` 的读取要 `TryGetValue`
//    （Go 读不存在的键得到 nil 切片）。
// 8) `collections.Set[*ast.Node]` → C# 的 `Collections.Set<Node>`（Go 的指针语义 = C# 引用相等，Set 默认相等性
//    对引用类型就是引用相等）。
// 9) `ast.SymbolTable` 是 `Dictionary<string, Symbol>` ⇒ 键缺失用 `TryGetValue`/`ContainsKey`（Go 读 nil map 得 nil）。
// 10) `GetTokenAtPosition` 在 C# 返回非空 `Node`（Go 里同样是非 nil），但 Go 源码保留了 `token == nil` 分支，
//     这里按 C# 非空返回类型去掉该分支（等价于恒不成立）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsAutoImport;
using TypeScript.LsChange;
using TypeScript.LsConv;
using TypeScript.LsProto;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.LsChange.LsChange;
using static TypeScript.Scanner.Scanner;
using Loc = TypeScript.Locale.Locale;
using CheckerType = TypeScript.Checker.Checker;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

public static partial class Ls
{
    internal const string _fixClassIncorrectlyImplementsInterfaceFixID = "fixClassIncorrectlyImplementsInterface";

    internal static readonly int[] _fixClassIncorrectlyImplementsInterfaceErrorCodes =
    [
        Class_0_incorrectly_implements_interface_1.Code(),
        Class_0_incorrectly_implements_class_1_Did_you_mean_to_extend_1_and_inherit_its_members_as_a_subclass.Code(),
    ];

    public static readonly CodeFixProvider FixClassIncorrectlyImplementsInterfaceProvider = new()
    {
        ErrorCodes = _fixClassIncorrectlyImplementsInterfaceErrorCodes,
        GetCodeActions = GetCodeActionsToFixClassIncorrectlyImplementsInterface,
        FixIds = [_fixClassIncorrectlyImplementsInterfaceFixID],
        GetAllCodeActions = GetAllCodeActionsToFixClassIncorrectlyImplementsInterface,
    };

    private static (CodeAction[]? Actions, Exception? Error) GetCodeActionsToFixClassIncorrectlyImplementsInterface(Context context, CodeFixContext fixContext)
    {
        var classDeclaration = GetClass(fixContext.SourceFile!, fixContext.Span);
        if (classDeclaration is null)
        {
            return (null, null);
        }

        var implementsTypes = GetImplementsHeritageClauseElements(classDeclaration);
        var locale = Loc.FromContext(context);

        var (typeChecker, done) = fixContext.Program!.GetTypeCheckerForFile(context, fixContext.SourceFile!);
        try
        {
            List<CodeAction>? actions = null;
            foreach (var implementedTypeNode in implementsTypes)
            {
                var changeTracker = NewTracker(context, fixContext.Program.Options(), fixContext.LS!.FormatOptions(), fixContext.LS._converters!);
                var (importAdder, err) = CreateImportAdder(context, fixContext, typeChecker);
                if (err is not null)
                {
                    return (null, err);
                }

                AddChanges(context, fixContext, changeTracker, importAdder, typeChecker, classDeclaration, implementedTypeNode);
                var changes = GetChanges(changeTracker, importAdder, fixContext.SourceFile!);
                if (changes.Length == 0)
                {
                    continue;
                }

                (actions ??= []).Add(new CodeAction
                {
                    Description = Implement_interface_0.Localize(locale, GetTextOfNode(implementedTypeNode)),
                    Changes = changes,
                    FixID = _fixClassIncorrectlyImplementsInterfaceFixID,
                    FixAllDescription = Implement_all_unimplemented_interfaces.Localize(locale),
                });
            }

            return (actions?.ToArray(), null);
        }
        finally
        {
            done();
        }
    }

    private static (CombinedCodeActions? Actions, Exception? Error) GetAllCodeActionsToFixClassIncorrectlyImplementsInterface(Context context, CodeFixContext fixContext)
    {
        var (typeChecker, done) = fixContext.Program!.GetTypeCheckerForFile(context, fixContext.SourceFile!);
        try
        {
            var changeTracker = NewTracker(context, fixContext.Program.Options(), fixContext.LS!.FormatOptions(), fixContext.LS._converters!);
            var (importAdder, err) = CreateImportAdder(context, fixContext, typeChecker);
            if (err is not null)
            {
                return (null, err);
            }

            var seenClassDeclarations = new Collections.Set<Node>();

            foreach (var diag in Ls.GetAllDiagnostics(context, fixContext.Program, fixContext.SourceFile!))
            {
                if (!Ls.IsFixableDiagnostic(diag, _fixClassIncorrectlyImplementsInterfaceErrorCodes))
                {
                    continue;
                }

                var classDeclaration = GetClass(fixContext.SourceFile!, new TextRange(diag.Pos(), diag.End()));
                if (classDeclaration is null)
                {
                    continue;
                }

                if (seenClassDeclarations.AddIfAbsent(classDeclaration))
                {
                    var implementsTypes = GetImplementsHeritageClauseElements(classDeclaration);
                    foreach (var implementedTypeNode in implementsTypes)
                    {
                        AddChanges(context, fixContext, changeTracker, importAdder, typeChecker, classDeclaration, implementedTypeNode);
                    }
                }
            }

            var changes = GetChanges(changeTracker, importAdder, fixContext.SourceFile!);
            if (changes.Length == 0)
            {
                return (null, null);
            }

            return (new CombinedCodeActions
            {
                Description = Implement_all_unimplemented_interfaces.Localize(Loc.FromContext(context)),
                Changes = changes,
            }, null);
        }
        finally
        {
            done();
        }
    }

    private static void AddChanges(
        Context context,
        CodeFixContext fixContext,
        Tracker changeTracker,
        ImportAdder? importAdder,
        CheckerType typeChecker,
        Node classDeclaration,
        Node implementedTypeNode)
    {
        var missingMemberFixer = new MissingMemberFixer(changeTracker, fixContext.Program!, typeChecker, fixContext.LS!.UserPreferences(), importAdder, Loc.FromContext(context));
        var constructor = GetConstructor(classDeclaration);
        var implementedType = typeChecker.GetTypeAtLocation(implementedTypeNode);
        var classType = typeChecker.GetTypeAtLocation(classDeclaration);

        if (typeChecker.GetNumberIndexType(classType) is null)
        {
            var member = missingMemberFixer.CreateIndexSignatureDeclarationFromType(classDeclaration, implementedType, typeChecker.GetNumberType());
            if (member is not null)
            {
                InsertInterfaceMemberNode(changeTracker, fixContext.SourceFile!, classDeclaration, constructor, member);
            }
        }

        if (typeChecker.GetStringIndexType(classType) is null)
        {
            var member = missingMemberFixer.CreateIndexSignatureDeclarationFromType(classDeclaration, implementedType, typeChecker.GetStringType());
            if (member is not null)
            {
                InsertInterfaceMemberNode(changeTracker, fixContext.SourceFile!, classDeclaration, constructor, member);
            }
        }

        var missingMembers = GetMissingMembers(typeChecker, classDeclaration, [implementedType]);
        foreach (var member in missingMembers)
        {
            var memberNodes = missingMemberFixer.CreateMemberFromSymbol(member, classDeclaration, fixContext.SourceFile!, null /*body*/, PreserveOptionalFlags.All, false /*abstract*/);
            foreach (var memberNode in memberNodes)
            {
                InsertInterfaceMemberNode(changeTracker, fixContext.SourceFile!, classDeclaration, constructor, memberNode);
            }
        }
    }

    internal static TextEdit[] GetChanges(Tracker changeTracker, ImportAdder? importAdder, SourceFile sourceFile)
    {
        var (changes, unmappable) = changeTracker.GetChanges();
        if (unmappable is { Length: > 0 })
        {
            return [];
        }

        var fileChanges = changes.TryGetValue(sourceFile.OriginalFileName(), out var fc) ? fc : [];
        if (importAdder is not null && importAdder.HasFixes())
        {
            fileChanges = [.. fileChanges, .. importAdder.Edits()];
        }

        return fileChanges;
    }

    private static void InsertInterfaceMemberNode(Tracker changeTracker, SourceFile sourceFile, Node classDeclaration, Node? constructor, Node member)
    {
        if (constructor is null)
        {
            changeTracker.InsertMemberAtStart(sourceFile, classDeclaration, member);
        }
        else
        {
            changeTracker.InsertNodeAfter(sourceFile, constructor, member);
        }
    }

    private static Node? GetClass(SourceFile sourceFile, TextRange span)
    {
        var token = GetTokenAtPosition(sourceFile, span.Pos());
        return GetContainingClass(token);
    }

    private static Node? GetConstructor(Node? classDeclaration)
    {
        if (classDeclaration is null || classDeclaration.MemberList() is null)
        {
            return null;
        }

        foreach (var member in classDeclaration.MemberList()!.Nodes ?? [])
        {
            if (member is not null && IsConstructorDeclaration(member))
            {
                return member;
            }
        }

        return null;
    }

    private static Symbol[] GetMissingMembers(CheckerType typeChecker, Node classDeclaration, Type[] implementedTypes)
    {
        var inheritedMembers = GetInheritedMembers(typeChecker, classDeclaration);
        var seenMembers = new Dictionary<string, Symbol>();

        var classMembers = classDeclaration.Symbol?.Members;

        List<Symbol>? missingMembers = null;
        foreach (var implementedType in implementedTypes)
        {
            foreach (var symbol in typeChecker.GetPropertiesOfType(implementedType))
            {
                if (symbol is null)
                {
                    continue;
                }

                if (classMembers is not null && classMembers.ContainsKey(symbol.Name))
                {
                    continue;
                }

                if (inheritedMembers.ContainsKey(symbol.Name) || seenMembers.ContainsKey(symbol.Name))
                {
                    continue;
                }

                var flags = TypeScript.Checker.Checker.GetDeclarationModifierFlagsFromSymbol(symbol);
                if ((flags & ModifierFlags.Private) == 0)
                {
                    seenMembers[symbol.Name] = symbol;
                    (missingMembers ??= []).Add(symbol);
                }
            }
        }

        return missingMembers?.ToArray() ?? [];
    }

    private static SymbolTable GetInheritedMembers(CheckerType typeChecker, Node classDeclaration)
    {
        var typeNode = GetClassExtendsHeritageElement(classDeclaration);
        if (typeNode is null)
        {
            return [];
        }

        var baseType = typeChecker.GetTypeAtLocation(typeNode);
        if (baseType is null)
        {
            return [];
        }

        var inheritedMembers = new SymbolTable();
        foreach (var symbol in typeChecker.GetPropertiesOfType(baseType))
        {
            if (symbol is null)
            {
                continue;
            }

            var flags = TypeScript.Checker.Checker.GetDeclarationModifierFlagsFromSymbol(symbol);
            if ((flags & ModifierFlags.Private) == 0)
            {
                inheritedMembers[symbol.Name] = symbol;
            }
        }

        return inheritedMembers;
    }

    private static (ImportAdder? ImportAdder, Exception? Error) CreateImportAdder(Context context, CodeFixContext fixContext, CheckerType typeChecker)
    {
        var (view, err) = fixContext.LS!.GetPreparedAutoImportView(fixContext.SourceFile!, typeChecker);
        if (err is not null)
        {
            return (null, err);
        }

        if (view is null)
        {
            return (null, null);
        }

        return (AutoImport.NewImportAdder(
            context,
            fixContext.Program!,
            typeChecker,
            fixContext.SourceFile!,
            view,
            fixContext.LS.FormatOptions(),
            fixContext.LS._converters!,
            fixContext.LS.UserPreferences()), null);
    }
}
