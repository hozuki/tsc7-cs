// ported from: internal/transformers/declarations/util.go
//
// Go 的包级函数（无接收者）落在静态类 Declarations（docs/architecture.md §2）。
namespace TypeScript.Transformers.Declarations;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

/// <summary>
/// Go 的 package declarations 的包级函数/类型宿主（Go 的包级函数在 C# 里落成静态类成员，见 docs/architecture.md §2）。
/// </summary>
public static partial class Declarations
{
    internal static bool NeedsScopeMarker(Node result)
    {
        return !IsAnyImportOrReExport(result) && !IsExportAssignment(result) && !HasSyntacticModifier(result, ModifierFlags.Export) && !IsAmbientModule(result);
    }

    internal static bool CanHaveLiteralInitializer(DeclarationEmitHost host, Node node)
    {
        switch (node.Kind)
        {
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
                return host.GetEffectiveDeclarationFlags(node, ModifierFlags.Private) == 0;
            case Kind.Parameter:
            case Kind.VariableDeclaration:
                return true;
        }
        return false;
    }

    internal static bool CanProduceDiagnostics(Node node)
    {
        return IsVariableDeclaration(node) ||
            IsPropertyDeclaration(node) ||
            IsPropertySignatureDeclaration(node) ||
            IsBindingElement(node) ||
            IsSetAccessorDeclaration(node) ||
            IsGetAccessorDeclaration(node) ||
            IsConstructSignatureDeclaration(node) ||
            IsCallSignatureDeclaration(node) ||
            IsMethodDeclaration(node) ||
            IsMethodSignatureDeclaration(node) ||
            IsFunctionDeclaration(node) ||
            IsParameterDeclaration(node) ||
            IsTypeParameterDeclaration(node) ||
            IsExpressionWithTypeArguments(node) ||
            IsImportEqualsDeclaration(node) ||
            IsTypeAliasDeclaration(node) ||
            IsJSTypeAliasDeclaration(node) ||
            IsConstructorDeclaration(node) ||
            IsIndexSignatureDeclaration(node) ||
            IsPropertyAccessExpression(node) ||
            IsElementAccessExpression(node) ||
            IsBinaryExpression(node) ||
            IsCallExpression(node); // || // !!! TODO: JSDoc support
        /* IsJSDocTypeAlias(node); */
    }

    internal static bool CanReuseModifierNodes(Node[] nodes)
    {
        foreach (var node in nodes)
        {
            if (IsModifier(node) && (node.Flags & NodeFlags.Reparsed) != 0)
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsDeclarationAndNotVisible(EmitContext emitContext, EmitResolver resolver, Node node)
    {
        node = emitContext.ParseNode(node)!;
        switch (node.Kind)
        {
            case Kind.FunctionDeclaration:
            case Kind.ModuleDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.ClassDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.EnumDeclaration:
                return !resolver.IsDeclarationVisible(node);
            // The following should be doing their own visibility checks based on filtering their members
            case Kind.VariableDeclaration:
                return !GetBindingNameVisible(resolver, node);
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
            case Kind.ExportDeclaration:
            case Kind.ExportAssignment:
                return false;
            case Kind.ClassStaticBlockDeclaration:
                return true;
        }
        return false;
    }

    internal static bool GetBindingNameVisible(EmitResolver resolver, Node elem)
    {
        if (IsOmittedExpression(elem))
        {
            return false;
        }
        // TODO: parseArrayBindingElement _never_ parses out an OmittedExpression anymore, instead producing a nameless binding element
        // Audit if OmittedExpression should be removed
        if (elem.Name() == null)
        {
            return false;
        }
        if (IsBindingPattern(elem.Name()))
        {
            // If any child binding pattern element has been marked visible (usually by collect linked aliases), then this is visible
            foreach (var childElement in elem.Name()!.Elements() ?? [])
            {
                if (GetBindingNameVisible(resolver, childElement))
                {
                    return true;
                }
            }
            return false;
        }
        else
        {
            return resolver.IsDeclarationVisible(elem);
        }
    }

    internal static bool IsEnclosingDeclaration(Node node)
    {
        return IsSourceFile(node) ||
            IsTypeAliasDeclaration(node) ||
            IsJSTypeAliasDeclaration(node) ||
            IsModuleDeclaration(node) ||
            IsClassDeclaration(node) ||
            IsInterfaceDeclaration(node) ||
            IsFunctionLike(node) ||
            IsIndexSignatureDeclaration(node) ||
            IsMappedTypeNode(node) ||
            IsVariableDeclaration(node);
    }

    internal static bool IsAlwaysType(Node node)
    {
        if (node.Kind == Kind.InterfaceDeclaration)
        {
            return true;
        }
        return false;
    }

    internal static ModifierFlags MaskModifierFlags(Node node, ModifierFlags modifierMask, ModifierFlags modifierAdditions)
    {
        var flags = (GetCombinedModifierFlags(node) & modifierMask) | modifierAdditions;
        if ((flags & ModifierFlags.Default) != 0 && (flags & ModifierFlags.Export) == 0)
        {
            // A non-exported default is a nonsequitor - we usually try to remove all export modifiers
            // from statements in ambient declarations; but a default export must retain its export modifier to be syntactically valid
            flags ^= ModifierFlags.Export;
        }
        if ((flags & ModifierFlags.Default) != 0 && (flags & ModifierFlags.Ambient) != 0)
        {
            flags ^= ModifierFlags.Ambient; // `declare` is never required alongside `default` (and would be an error if printed)
        }
        return flags;
    }

    internal static Node UnwrapParenthesizedExpression(Node o)
    {
        while (o.Kind == Kind.ParenthesizedExpression)
        {
            o = o.Expression()!;
        }
        return o;
    }

    internal static bool IsPrivateMethodTypeParameter(DeclarationEmitHost host, TypeParameterDeclaration node)
    {
        return node.Parent!.Kind == Kind.MethodDeclaration && host.GetEffectiveDeclarationFlags(node.Parent!, ModifierFlags.Private) != 0;
    }

    // Returns true if expando properties should be emitted for this function.
    // Properties are emitted if any overload in the symbol has a body (implementation).
    internal static bool ShouldEmitFunctionProperties(FunctionDeclaration input)
    {
        if (input.Body != null)
        {
            return true;
        }
        return !Every(input.Symbol!.Declarations, decl => !IsFunctionDeclaration(decl) || decl.AsFunctionDeclaration()!.Body == null);
    }

    internal static Node? GetEffectiveBaseTypeNode(Node node)
    {
        var baseType = GetClassExtendsHeritageElement(node);
        // !!! TODO: JSDoc support
        // if (baseType && isInJSFile(node)) {
        //     // Prefer an @augments tag because it may have type parameters.
        //     const tag = getJSDocAugmentsTag(node);
        //     if (tag) {
        //         return tag.class;
        //     }
        // }
        return baseType;
    }

    internal static bool IsScopeMarker(Node node)
    {
        return IsExportAssignment(node) || IsExportDeclaration(node);
    }

    internal static bool HasScopeMarker(NodeList? statements)
    {
        if (statements == null)
        {
            return false;
        }
        return Some(statements.Nodes, IsScopeMarker);
    }
}
