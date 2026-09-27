// ported from: internal/checker/checker.go:10639-10811（生成代码的命名冲突检查：checkCollisionsForDeclarationName、
//   checkCollisionWithRequireExportsInGeneratedCode、checkCollisionWithGlobalObjectInGeneratedCode、
//   needCollisionCheckForIdentifier、setNodeLinksForPrivateIdentifierScope、
//   recordPotentialCollisionWithWeakMapSetInGeneratedCode、checkWeakMapSetCollision、
//   checkCollisionWithGlobalPromiseInGeneratedCode、recordPotentialCollisionWithReflectInGeneratedCode、
//   checkReflectCollision、checkClassNameCollisionWithObject）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `c.addDeferredDiagnostic(func() { … })` → `AddDeferredDiagnostic(() => …)`（Go 的闭包 → C# lambda；
//    `addDeferredDiagnostic` 的形参是 `Action`）。
// 2) `c.errorSkippedOnNoEmit(...)` → `ErrorSkippedOnNoEmit(...)`（C4 的 API，签名见 C4 广播）。
// 3) `scanner.DeclarationNameToString(name)`（Go 里参数是 *ast.Node 且可能为 nil）→ `DeclarationNameToString(name)`
//    （Ast 的签名是 `DeclarationNameToString(Node? name)`，与 Go 的 nil 容忍一致）。
// 4) `c.program.GetEmitModuleFormatOfFile(...)` → `_program.GetEmitModuleFormatOfFile(...)`。
// 5) `parent.AsSourceFile()` 在 Go 里由 `ast.IsSourceFile(parent)` 保证非 nil → C# 加 `!`。
// 6) `c.moduleKind.String()` → `_moduleKind.String()`（Core 的 `String(this ModuleKind)` 扩展方法，docs/architecture.md §3.3）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal void CheckCollisionsForDeclarationName(Node node, Node? name)
    {
        if (name == null)
        {
            return;
        }
        CheckCollisionWithRequireExportsInGeneratedCode(node, name);
        CheckCollisionWithGlobalObjectInGeneratedCode(node, name);
        CheckCollisionWithGlobalPromiseInGeneratedCode(node, name);
        RecordPotentialCollisionWithWeakMapSetInGeneratedCode(node, name);
        RecordPotentialCollisionWithReflectInGeneratedCode(node, name);
        if (IsClassLike(node))
        {
            CheckTypeNameIsReserved(name, Diagnostics.Class_name_cannot_be_0);
            if ((node.Flags & NodeFlags.Ambient) == 0)
            {
                CheckClassNameCollisionWithObject(name);
            }
        }
        else if (IsEnumDeclaration(node))
        {
            CheckTypeNameIsReserved(name, Diagnostics.Enum_name_cannot_be_0);
        }
    }

    internal void CheckCollisionWithRequireExportsInGeneratedCode(Node node, Node? name)
    {
        // No need to check for require or exports for ES6 modules and later
        if (_program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(node)!) >= ModuleKind.ES2015)
        {
            return;
        }
        if (name == null || !NeedCollisionCheckForIdentifier(node, name, "require") && !NeedCollisionCheckForIdentifier(node, name, "exports"))
        {
            return;
        }
        // Uninstantiated modules shouldnt do this check
        if (IsModuleDeclaration(node) && GetModuleInstanceState(node) != ModuleInstanceState.Instantiated)
        {
            return;
        }
        // In case of variable declaration, node.parent is variable statement so look at the variable statement's parent
        var parent = GetDeclarationContainer(node)!;
        if (IsSourceFile(parent) && IsExternalOrCommonJSModule(parent.AsSourceFile()))
        {
            // If the declaration happens to be in external module, report error that require and exports are reserved keywords
            ErrorSkippedOnNoEmit(name, Diagnostics.Duplicate_identifier_0_Compiler_reserves_name_1_in_top_level_scope_of_a_module, DeclarationNameToString(name), DeclarationNameToString(name));
        }
    }

    internal void CheckCollisionWithGlobalObjectInGeneratedCode(Node node, Node? name)
    {
        if (name == null || IsClassLike(node) || !NeedCollisionCheckForIdentifier(node, name, "Object"))
        {
            return;
        }
        // Uninstantiated modules shouldn't do this check
        if (IsModuleDeclaration(node) && GetModuleInstanceState(node) != ModuleInstanceState.Instantiated)
        {
            return;
        }
        // In case of variable declaration, node.parent is variable statement so look at the variable statement's parent
        var parent = GetDeclarationContainer(node)!;
        if (IsSourceFile(parent) && IsExternalOrCommonJSModule(parent.AsSourceFile()) && _program.GetEmitModuleFormatOfFile(parent.AsSourceFile()!) == ModuleKind.CommonJS)
        {
            // If the declaration happens to be in external module, report error that Object is a reserved identifier.
            ErrorSkippedOnNoEmit(name, Diagnostics.Duplicate_identifier_0_Compiler_reserves_name_1_in_top_level_scope_of_a_module, DeclarationNameToString(name), DeclarationNameToString(name));
        }
    }

    internal bool NeedCollisionCheckForIdentifier(Node node, Node? identifier, string name)
    {
        if (identifier != null && identifier.Text() != name)
        {
            return false;
        }
        switch (node.Kind)
        {
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.PropertyAssignment:
                // it is ok to have member named '_super', '_this', `Promise`, etc. - member access is always qualified
                return false;
        }
        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            // ambient context - no codegen impact
            return false;
        }
        if (IsImportClause(node) || IsImportEqualsDeclaration(node) || IsImportSpecifier(node))
        {
            // type-only imports do not require collision checks against runtime values.
            if (IsTypeOnlyImportOrExportDeclaration(node))
            {
                return false;
            }
        }
        var root = GetRootDeclaration(node);
        if (IsParameterDeclaration(root!) && NodeIsMissing(root!.Parent!.Body()))
        {
            // just an overload - no codegen impact
            return false;
        }
        return true;
    }

    internal void SetNodeLinksForPrivateIdentifierScope(Node node)
    {
        var name = node.Name();
        if (IsPrivateIdentifier(name!))
        {
            if (_languageVersion < LanguageFeatureMinimumTarget.PrivateNamesAndClassStaticBlocks ||
                _languageVersion < LanguageFeatureMinimumTarget.ClassAndClassElementDecorators ||
                !_compilerOptions.GetUseDefineForClassFields())
            {
                for (var lexicalScope = GetEnclosingBlockScopeContainer(node); lexicalScope != null; lexicalScope = GetEnclosingBlockScopeContainer(lexicalScope))
                {
                    _nodeLinks.Get(lexicalScope)._flags |= NodeCheckFlags.ContainsClassWithPrivateIdentifiers;
                }
            }
        }
    }

    internal void RecordPotentialCollisionWithWeakMapSetInGeneratedCode(Node node, Node? name)
    {
        if (_languageVersion <= ScriptTarget.ES2021 &&
            (NeedCollisionCheckForIdentifier(node, name!, "WeakMap") || NeedCollisionCheckForIdentifier(node, name!, "WeakSet")))
        {
            AddDeferredDiagnostic(() =>
            {
                CheckWeakMapSetCollision(node);
            });
        }
    }

    internal void CheckWeakMapSetCollision(Node node)
    {
        var enclosingBlockScope = GetEnclosingBlockScopeContainer(node);
        if (enclosingBlockScope != null && (_nodeLinks.Get(enclosingBlockScope)._flags & NodeCheckFlags.ContainsClassWithPrivateIdentifiers) != 0)
        {
            var name = node.Name();
            if (name != null && IsIdentifier(name))
            {
                ErrorSkippedOnNoEmit(node, Diagnostics.Compiler_reserves_name_0_when_emitting_private_identifier_downlevel, name.Text());
            }
        }
    }

    internal void CheckCollisionWithGlobalPromiseInGeneratedCode(Node node, Node? name)
    {
        if (name == null || _languageVersion >= ScriptTarget.ES2017 || !NeedCollisionCheckForIdentifier(node, name, "Promise"))
        {
            return;
        }
        // Uninstantiated modules shouldn't do this check
        if (IsModuleDeclaration(node) && GetModuleInstanceState(node) != ModuleInstanceState.Instantiated)
        {
            return;
        }
        // In case of variable declaration, node.parent is variable statement so look at the variable statement's parent
        var parent = GetDeclarationContainer(node)!;
        if (IsSourceFile(parent) && IsExternalOrCommonJSModule(parent.AsSourceFile()) && (parent.Flags & NodeFlags.HasAsyncFunctions) != 0)
        {
            // If the declaration happens to be in external module, report error that Promise is a reserved identifier.
            ErrorSkippedOnNoEmit(name, Diagnostics.Duplicate_identifier_0_Compiler_reserves_name_1_in_top_level_scope_of_a_module_containing_async_functions, DeclarationNameToString(name), DeclarationNameToString(name));
        }
    }

    internal void RecordPotentialCollisionWithReflectInGeneratedCode(Node node, Node? name)
    {
        if (name != null && _languageVersion <= ScriptTarget.ES2021 && NeedCollisionCheckForIdentifier(node, name, "Reflect"))
        {
            AddDeferredDiagnostic(() =>
            {
                CheckReflectCollision(node);
            });
        }
    }

    internal void CheckReflectCollision(Node node)
    {
        var hasCollision = false;
        if (IsClassExpression(node))
        {
            // ClassExpression names don't contribute to their containers, but do matter for any of their block-scoped members.
            foreach (var member in node.Members() ?? [])
            {
                if ((_nodeLinks.Get(member)._flags & NodeCheckFlags.ContainsSuperPropertyInStaticInitializer) != 0)
                {
                    hasCollision = true;
                    break;
                }
            }
        }
        else if (IsFunctionExpression(node))
        {
            // FunctionExpression names don't contribute to their containers, but do matter for their contents
            if ((_nodeLinks.Get(node)._flags & NodeCheckFlags.ContainsSuperPropertyInStaticInitializer) != 0)
            {
                hasCollision = true;
            }
        }
        else
        {
            var container = GetEnclosingBlockScopeContainer(node);
            if (container != null && (_nodeLinks.Get(container)._flags & NodeCheckFlags.ContainsSuperPropertyInStaticInitializer) != 0)
            {
                hasCollision = true;
            }
        }
        if (hasCollision)
        {
            var name = node.Name();
            if (name != null && IsIdentifier(name))
            {
                ErrorSkippedOnNoEmit(node, Diagnostics.Duplicate_identifier_0_Compiler_reserves_name_1_when_emitting_super_references_in_static_initializers, DeclarationNameToString(name), "Reflect");
            }
        }
    }

    internal void CheckClassNameCollisionWithObject(Node name)
    {
        if (name.Text() == "Object" && _program.GetEmitModuleFormatOfFile(GetSourceFileOfNode(name)!) < ModuleKind.ES2015)
        {
            Error(name, Diagnostics.Class_name_cannot_be_Object_when_targeting_ES5_and_above_with_module_0, _moduleKind.String());
        }
    }
}
