// ported from: internal/parser/references.go:1-71（外部模块引用收集；W6 分片，整文件）
//
// 与 Go 的偏离（逐条）：
// 1) 包级函数 → `partial class Parser` 上的 `internal static`（docs §2；Go 未导出的转 PascalCase）。
// 2) Go 的 `for _, node := range file.Statements.Nodes`（nil 切片可迭代 → 零轮）→ C# 必须先判空再 foreach
//    （C# 的 foreach 遇 null 抛 NRE）。凡"Go 里靠 nil 切片天然为空"的地方都按此写法（本文件与 ParserJsDoc.cs）。
// 3) Go 的 `append(file.Imports(), x)` → C# 的集合表达式 `[.. file.Imports(), x]`（Go 产生新切片并写回字段，
//    C# 的数组不可变长，语义一致）。
// 4) `map[K]bool` 的 `m[k]`（未命中给 false，docs §3.2/Scanner 文件头第 10 条）→ `ContainsKey`。
// 5) Go 的 `strings.HasPrefix(s, "node:")` → `HasPrefix(s, "node:", true)`（caseSensitive）。
// 6) Go 的 nil 解引用（`.Name().Text()`、回调实参）→ `!`：C# 抛 NRE，与 Go 的 panic 等价（本文件里这些位置
//    都有语法保证非空）。
// 7) Go 的 `ast.IsExternalModule(file)`（ast/utilities.go:1686，一行函数，C# 侧尚未移植）就地展开为
//    `file.ExternalModuleIndicator is not null`；不得在本轮给 ast 包加成员。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Parser;

internal sealed partial class Parser
{
    /// <summary>Go: <c>func collectExternalModuleReferences(file *ast.SourceFile)</c>。</summary>
    internal static void CollectExternalModuleReferences(SourceFile file)
    {
        var statements = file.Statements!.Nodes;
        if (statements is not null)
        {
            foreach (var node in statements)
            {
                CollectModuleReferences(file, node, false /*inAmbientModule*/);
            }
        }

        if ((file.Flags & NodeFlags.PossiblyContainsDynamicImport) != 0 || IsInJSFile(file.AsNode()))
        {
            ForEachDynamicImportOrRequireCall(file /*includeTypeSpaceImports*/, true, true /*requireStringLiteralLikeArgument*/, (_, moduleSpecifier) =>
            {
                SetImportsOfSourceFile(file, [.. file.Imports(), moduleSpecifier!]);
                return false;
            });
        }
    }

    /// <summary>Go: <c>func collectModuleReferences(file *ast.SourceFile, node *ast.Statement, inAmbientModule bool)</c>。</summary>
    internal static void CollectModuleReferences(SourceFile file, Node node, bool inAmbientModule)
    {
        if (IsAnyImportOrReExport(node))
        {
            var moduleNameExpr = GetExternalModuleName(node);
            // TypeScript 1.0 spec (April 2014): 12.1.6
            // An ExternalImportDeclaration in an AmbientExternalModuleDeclaration may reference other external modules
            // only through top - level external module names. Relative external module names are not permitted.
            if (moduleNameExpr is not null && IsStringLiteral(moduleNameExpr))
            {
                var moduleName = moduleNameExpr.Text();
                if (moduleName.Length != 0 && (!inAmbientModule || !IsExternalModuleNameRelative(moduleName)))
                {
                    SetImportsOfSourceFile(file, [.. file.Imports(), moduleNameExpr]);
                    // !!! removed `&& p.currentNodeModulesDepth == 0`
                    if (file.UsesUriStyleNodeCoreModules != Tristate.TSTrue && !file.IsDeclarationFile)
                    {
                        if (HasPrefix(moduleName, "node:", true) && !ExclusivelyPrefixedNodeCoreModules.ContainsKey(moduleName))
                        {
                            // Presence of `node:` prefix takes precedence over unprefixed node core modules
                            file.UsesUriStyleNodeCoreModules = Tristate.TSTrue;
                        }
                        else if (file.UsesUriStyleNodeCoreModules == Tristate.TSUnknown && UnprefixedNodeCoreModules.ContainsKey(moduleName))
                        {
                            // Avoid `unprefixedNodeCoreModules.has` for every import
                            file.UsesUriStyleNodeCoreModules = Tristate.TSFalse;
                        }
                    }
                }
            }

            return;
        }

        if (IsModuleDeclaration(node) && IsAmbientModule(node) &&
            (inAmbientModule || HasSyntacticModifier(node, ModifierFlags.Ambient) || file.IsDeclarationFile))
        {
            var nameText = node.AsModuleDeclaration()!.Name()!.Text();
            // Ambient module declarations can be interpreted as augmentations for some existing external modules.
            // This will happen in two cases:
            // - if current file is external module then module augmentation is a ambient module declaration defined in the top level scope
            // - if current file is not external module then module augmentation is an ambient module declaration with non-relative module name
            //   immediately nested in top level ambient module declaration .
            if (file.ExternalModuleIndicator is not null /* Go: ast.IsExternalModule(file) */ ||
                (inAmbientModule && !IsExternalModuleNameRelative(nameText)))
            {
                file.ModuleAugmentations = [.. file.ModuleAugmentations ?? [], node.AsModuleDeclaration()!.Name()!];
            }
            else if (!inAmbientModule)
            {
                file.AmbientModuleNames = [.. file.AmbientModuleNames ?? [], nameText];
                // An AmbientExternalModuleDeclaration declares an external module.
                // This type of declaration is permitted only in the global module.
                // The StringLiteral must specify a top - level external module name.
                // Relative external module names are not permitted
                // NOTE: body of ambient module is always a module block, if it exists
                var body = node.Body();
                if (body is not null)
                {
                    var bodyStatements = body.Statements();
                    if (bodyStatements is not null)
                    {
                        foreach (var statement in bodyStatements)
                        {
                            CollectModuleReferences(file, statement, true /*inAmbientModule*/);
                        }
                    }
                }
            }
        }
    }
}
