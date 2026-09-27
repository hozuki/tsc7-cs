// ported from: internal/transformers/declarations/diagnostics.go
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `type GetSymbolAccessibilityDiagnostic = func(...) *SymbolAccessibilityDiagnostic` → C# 具名委托
//    `GetSymbolAccessibilityDiagnostic`（返回可空：Go 返回 nil 表示"无需报错"）。
// 2) Go 的 `SymbolAccessibilityDiagnostic` 是结构体、字段未导出 → C# class + `internal _camelCase` 字段
//    （Go 的 `info := *errorInfo` 只是拷结构体用于只读访问，C# 的引用拷贝语义等价）。
// 3) Go 的匿名函数 `func(node *ast.Node, r printer.SymbolAccessibilityResult) *diagnostics.Message` →
//    C# `Func<Node, SymbolAccessibilityResult, Message?>`。
// 4) `panic("...")` → `throw new DebugFailureException("...")`（docs/ast.md §3.1）。
// 5) `ast.GetAllAccessorDeclarationsForDeclaration(node, decls)` 的 C# 形参是 `ReadOnlySpan<Node>`（数组可直接传）。
// 6) Go 的 `node.Symbol().Declarations` → C# `node.Symbol!.Declarations`（`Symbol` 是 Ast 的属性，见 docs/ast.md §2）。
// 7) `slices.IndexFunc` 在别处；本文件用到 `ast.FindAncestorOrQuit`（回调返回 `FindAncestorResult`）。
namespace TypeScript.Transformers.Declarations;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Declarations.Declarations;

public delegate SymbolAccessibilityDiagnostic? GetSymbolAccessibilityDiagnostic(SymbolAccessibilityResult symbolAccessibilityResult);

public sealed class SymbolAccessibilityDiagnostic
{
    internal Node? _errorNode;
    internal Message _diagnosticMessage = null!;
    internal Node? _typeName;
}

public static partial class Declarations
{
    internal static GetSymbolAccessibilityDiagnostic WrapSimpleDiagnosticSelector(Node node, Func<Node, SymbolAccessibilityResult, Message?> selector)
    {
        return symbolAccessibilityResult =>
        {
            var diagnosticMessage = selector(node, symbolAccessibilityResult);
            if (diagnosticMessage == null)
            {
                return null;
            }
            return new SymbolAccessibilityDiagnostic
            {
                _errorNode = node,
                _diagnosticMessage = diagnosticMessage,
                _typeName = GetNameOfDeclaration(node),
            };
        };
    }

    internal static GetSymbolAccessibilityDiagnostic WrapNamedDiagnosticSelector(Node node, Func<Node, SymbolAccessibilityResult, Message?> selector)
    {
        return symbolAccessibilityResult =>
        {
            var diagnosticMessage = selector(node, symbolAccessibilityResult);
            if (diagnosticMessage == null)
            {
                return null;
            }
            var name = GetNameOfDeclaration(node);
            return new SymbolAccessibilityDiagnostic
            {
                _errorNode = name,
                _diagnosticMessage = diagnosticMessage,
                _typeName = name,
            };
        };
    }

    internal static GetSymbolAccessibilityDiagnostic WrapFallbackErrorDiagnosticSelector(Node node, Func<Node, SymbolAccessibilityResult, Message?> selector)
    {
        return symbolAccessibilityResult =>
        {
            var diagnosticMessage = selector(node, symbolAccessibilityResult);
            if (diagnosticMessage == null)
            {
                return null;
            }
            var errorNode = GetNameOfDeclaration(node);
            if (errorNode == null)
            {
                errorNode = node;
            }
            return new SymbolAccessibilityDiagnostic
            {
                _errorNode = errorNode,
                _diagnosticMessage = diagnosticMessage,
            };
        };
    }

    internal static Message SelectDiagnosticBasedOnModuleName(SymbolAccessibilityResult symbolAccessibilityResult, Message moduleNotNameable, Message privateModule, Message nonModule)
    {
        if (symbolAccessibilityResult.ErrorModuleName.Length > 0)
        {
            if (symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed)
            {
                return moduleNotNameable;
            }
            return privateModule;
        }
        return nonModule;
    }

    internal static Message SelectDiagnosticBasedOnModuleNameNoNameCheck(SymbolAccessibilityResult symbolAccessibilityResult, Message privateModule, Message nonModule)
    {
        if (symbolAccessibilityResult.ErrorModuleName.Length > 0)
        {
            return privateModule;
        }
        return nonModule;
    }

    internal static GetSymbolAccessibilityDiagnostic CreateGetSymbolAccessibilityDiagnosticForNodeName(Node node)
    {
        if (IsSetAccessorDeclaration(node) || IsGetAccessorDeclaration(node))
        {
            return WrapSimpleDiagnosticSelector(node, GetAccessorNameVisibilityDiagnosticMessage);
        }
        else if (IsMethodDeclaration(node) || IsMethodSignatureDeclaration(node))
        {
            return WrapSimpleDiagnosticSelector(node, GetMethodNameVisibilityDiagnosticMessage);
        }
        else
        {
            return CreateGetSymbolAccessibilityDiagnosticForNode(node);
        }
    }

    internal static Message? GetAccessorNameVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult symbolAccessibilityResult)
    {
        if (IsStatic(node))
        {
            return SelectDiagnosticBasedOnModuleName(
                symbolAccessibilityResult,
                Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2,
                Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_private_name_1
            );
        }
        else if (node.Parent!.Kind == Kind.ClassDeclaration)
        {
            return SelectDiagnosticBasedOnModuleName(
                symbolAccessibilityResult,
                Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2,
                Diagnostics.Public_property_0_of_exported_class_has_or_is_using_private_name_1
            );
        }
        else
        {
            return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                symbolAccessibilityResult,
                Diagnostics.Property_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2,
                Diagnostics.Property_0_of_exported_interface_has_or_is_using_private_name_1
            );
        }
    }

    internal static Message? GetMethodNameVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult symbolAccessibilityResult)
    {
        if (IsStatic(node))
        {
            return SelectDiagnosticBasedOnModuleName(
                symbolAccessibilityResult,
                Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_name_1_from_private_module_2,
                Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_private_name_1
            );
        }
        else if (node.Parent!.Kind == Kind.ClassDeclaration)
        {
            return SelectDiagnosticBasedOnModuleName(
                symbolAccessibilityResult,
                Diagnostics.Public_method_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                Diagnostics.Public_method_0_of_exported_class_has_or_is_using_name_1_from_private_module_2,
                Diagnostics.Public_method_0_of_exported_class_has_or_is_using_private_name_1
            );
        }
        else
        {
            return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                symbolAccessibilityResult,
                Diagnostics.Method_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2,
                Diagnostics.Method_0_of_exported_interface_has_or_is_using_private_name_1
            );
        }
    }

    internal static GetSymbolAccessibilityDiagnostic CreateGetSymbolAccessibilityDiagnosticForNode(Node node)
    {
        if (IsVariableDeclaration(node) || IsPropertyDeclaration(node) || IsPropertySignatureDeclaration(node) || IsPropertyAccessExpression(node) || IsElementAccessExpression(node) || IsBinaryExpression(node) || IsBindingElement(node) || IsConstructorDeclaration(node))
        {
            return WrapSimpleDiagnosticSelector(node, GetVariableDeclarationTypeVisibilityDiagnosticMessage);
        }
        else if (IsSetAccessorDeclaration(node) || IsGetAccessorDeclaration(node))
        {
            return WrapNamedDiagnosticSelector(node, GetAccessorDeclarationTypeVisibilityDiagnosticMessage);
        }
        else if (IsConstructSignatureDeclaration(node) || IsCallSignatureDeclaration(node) || IsMethodDeclaration(node) || IsMethodSignatureDeclaration(node) || IsFunctionDeclaration(node) || IsIndexSignatureDeclaration(node))
        {
            return WrapFallbackErrorDiagnosticSelector(node, GetReturnTypeVisibilityDiagnosticMessage);
        }
        else if (IsParameterDeclaration(node))
        {
            if (IsParameterPropertyDeclaration(node, node.Parent) && HasSyntacticModifier(node.Parent!, ModifierFlags.Private))
            {
                return WrapSimpleDiagnosticSelector(node, GetVariableDeclarationTypeVisibilityDiagnosticMessage);
            }
            return WrapSimpleDiagnosticSelector(node, GetParameterDeclarationTypeVisibilityDiagnosticMessage);
        }
        else if (IsTypeParameterDeclaration(node))
        {
            return WrapSimpleDiagnosticSelector(node, GetTypeParameterConstraintVisibilityDiagnosticMessage);
        }
        else if (IsExpressionWithTypeArguments(node))
        {
            // unique node selection behavior, inline closure
            return symbolAccessibilityResult =>
            {
                Message diagnosticMessage;
                // Heritage clause is written by user so it can always be named
                if (IsClassDeclaration(node.Parent!.Parent!))
                {
                    // Class or Interface implemented/extended is inaccessible
                    if (IsHeritageClause(node.Parent) && node.Parent!.AsHeritageClause()!.Token == Kind.ImplementsKeyword)
                    {
                        diagnosticMessage = Diagnostics.Implements_clause_of_exported_class_0_has_or_is_using_private_name_1;
                    }
                    else
                    {
                        if (node.Parent!.Parent!.Name() != null)
                        {
                            diagnosticMessage = Diagnostics.X_extends_clause_of_exported_class_0_has_or_is_using_private_name_1;
                        }
                        else
                        {
                            diagnosticMessage = Diagnostics.X_extends_clause_of_exported_class_has_or_is_using_private_name_0;
                        }
                    }
                }
                else
                {
                    // interface is inaccessible
                    diagnosticMessage = Diagnostics.X_extends_clause_of_exported_interface_0_has_or_is_using_private_name_1;
                }

                return new SymbolAccessibilityDiagnostic
                {
                    _diagnosticMessage = diagnosticMessage,
                    _errorNode = node,
                    _typeName = GetNameOfDeclaration(node.Parent!.Parent),
                };
            };
        }
        else if (IsImportEqualsDeclaration(node))
        {
            return WrapSimpleDiagnosticSelector(node, (_, _) => Diagnostics.Import_declaration_0_is_using_private_name_1);
        }
        else if (IsTypeAliasDeclaration(node) || IsJSTypeAliasDeclaration(node))
        {
            // unique node selection behavior, inline closure
            return symbolAccessibilityResult =>
            {
                var diagnosticMessage = SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Exported_type_alias_0_has_or_is_using_private_name_1_from_module_2,
                    Diagnostics.Exported_type_alias_0_has_or_is_using_private_name_1
                );
                var errorNode = node.Type();
                var typeName = node.Name();
                return new SymbolAccessibilityDiagnostic
                {
                    _errorNode = errorNode,
                    _diagnosticMessage = diagnosticMessage,
                    _typeName = typeName,
                };
            };
        }
        else if (IsCallExpression(node))
        {
            // JS object.defineProperty call
            // unique node selection behavior, inline closure
            return symbolAccessibilityResult =>
            {
                var diagnosticMessage = SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Exported_variable_0_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                    Diagnostics.Exported_variable_0_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Exported_variable_0_has_or_is_using_private_name_1
                );
                var errorNode = node.Arguments()![1];
                var typeName = node.Arguments()![1];
                return new SymbolAccessibilityDiagnostic
                {
                    _errorNode = errorNode,
                    _diagnosticMessage = diagnosticMessage,
                    _typeName = typeName,
                };
            };
        }
        else
        {
            throw new DebugFailureException("Attempted to set a declaration diagnostic context for unhandled node kind: " + node.Kind.String());
        }
    }

    internal static Message? GetVariableDeclarationTypeVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult symbolAccessibilityResult)
    {
        if (node.Kind == Kind.VariableDeclaration || node.Kind == Kind.BindingElement)
        {
            return SelectDiagnosticBasedOnModuleName(
                symbolAccessibilityResult,
                Diagnostics.Exported_variable_0_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                Diagnostics.Exported_variable_0_has_or_is_using_name_1_from_private_module_2,
                Diagnostics.Exported_variable_0_has_or_is_using_private_name_1
            );

            // This check is to ensure we don't report error on constructor parameter property as that error would be reported during parameter emit
            // The only exception here is if the constructor was marked as private. we are not emitting the constructor parameters at all.
        }
        else if (node.Kind == Kind.PropertyDeclaration || node.Kind == Kind.PropertyAccessExpression || node.Kind == Kind.ElementAccessExpression || node.Kind == Kind.BinaryExpression || node.Kind == Kind.PropertySignature ||
            (node.Kind == Kind.Parameter && HasSyntacticModifier(node.Parent!, ModifierFlags.Private)))
        {
            // TODO(jfreeman): Deal with computed properties in error reporting.
            if (IsStatic(node))
            {
                return SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                    Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_private_name_1
                );
            }
            else if (node.Parent!.Kind == Kind.ClassDeclaration || node.Kind == Kind.Parameter)
            {
                return SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                    Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Public_property_0_of_exported_class_has_or_is_using_private_name_1
                );
            }
            else
            {
                // Interfaces cannot have types that cannot be named
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Property_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Property_0_of_exported_interface_has_or_is_using_private_name_1
                );
            }
        }
        return null; // TODO: Audit behavior - should this panic? potentially silent error state in strada
    }

    internal static Message? GetAccessorDeclarationTypeVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult symbolAccessibilityResult)
    {
        if (node.Kind == Kind.SetAccessor)
        {
            // Getters can infer the return type from the returned expression, but setters cannot, so the
            // "_from_external_module_1_but_cannot_be_named" case cannot occur.
            if (IsStatic(node))
            {
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Parameter_type_of_public_static_setter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Parameter_type_of_public_static_setter_0_from_exported_class_has_or_is_using_private_name_1
                );
            }
            else
            {
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Parameter_type_of_public_setter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Parameter_type_of_public_setter_0_from_exported_class_has_or_is_using_private_name_1
                );
            }
        }
        else
        {
            if (IsStatic(node))
            {
                return SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                    Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_private_name_1
                );
            }
            else
            {
                return SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                    Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_private_name_1
                );
            }
        }
    }

    internal static Message? GetReturnTypeVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult symbolAccessibilityResult)
    {
        switch (node.Kind)
        {
            case Kind.ConstructSignature:
                // Interfaces cannot have return types that cannot be named
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Return_type_of_constructor_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1,
                    Diagnostics.Return_type_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_0
                );
            case Kind.CallSignature:
                // Interfaces cannot have return types that cannot be named
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Return_type_of_call_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1,
                    Diagnostics.Return_type_of_call_signature_from_exported_interface_has_or_is_using_private_name_0
                );
            case Kind.IndexSignature:
                // Interfaces cannot have return types that cannot be named
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Return_type_of_index_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1,
                    Diagnostics.Return_type_of_index_signature_from_exported_interface_has_or_is_using_private_name_0
                );

            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
                if (IsStatic(node))
                {
                    return SelectDiagnosticBasedOnModuleName(
                        symbolAccessibilityResult,
                        Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named,
                        Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_name_0_from_private_module_1,
                        Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_private_name_0
                    );
                }
                else if (node.Parent!.Kind == Kind.ClassDeclaration)
                {
                    return SelectDiagnosticBasedOnModuleName(
                        symbolAccessibilityResult,
                        Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named,
                        Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_name_0_from_private_module_1,
                        Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_private_name_0
                    );
                }
                else
                {
                    // Interfaces cannot have return types that cannot be named
                    return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                        symbolAccessibilityResult,
                        Diagnostics.Return_type_of_method_from_exported_interface_has_or_is_using_name_0_from_private_module_1,
                        Diagnostics.Return_type_of_method_from_exported_interface_has_or_is_using_private_name_0
                    );
                }
            case Kind.FunctionDeclaration:
                return SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Return_type_of_exported_function_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named,
                    Diagnostics.Return_type_of_exported_function_has_or_is_using_name_0_from_private_module_1,
                    Diagnostics.Return_type_of_exported_function_has_or_is_using_private_name_0
                );
            default:
                throw new DebugFailureException("This is unknown kind for signature: " + node.Kind.String());
        }
    }

    internal static Message? GetParameterDeclarationTypeVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult symbolAccessibilityResult)
    {
        switch (node.Parent!.Kind)
        {
            case Kind.Constructor:
                return SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                    Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_private_name_1
                );

            case Kind.ConstructSignature:
            case Kind.ConstructorType:
                // Interfaces cannot have parameter types that cannot be named
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_1
                );

            case Kind.CallSignature:
                // Interfaces cannot have parameter types that cannot be named
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Parameter_0_of_call_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Parameter_0_of_call_signature_from_exported_interface_has_or_is_using_private_name_1
                );

            case Kind.IndexSignature:
                // Interfaces cannot have parameter types that cannot be named
                return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                    symbolAccessibilityResult,
                    Diagnostics.Parameter_0_of_index_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Parameter_0_of_index_signature_from_exported_interface_has_or_is_using_private_name_1
                );

            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
                if (IsStatic(node.Parent))
                {
                    return SelectDiagnosticBasedOnModuleName(
                        symbolAccessibilityResult,
                        Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                        Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_name_1_from_private_module_2,
                        Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_private_name_1
                    );
                }
                else if (node.Parent!.Parent!.Kind == Kind.ClassDeclaration)
                {
                    return SelectDiagnosticBasedOnModuleName(
                        symbolAccessibilityResult,
                        Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                        Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_name_1_from_private_module_2,
                        Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_private_name_1
                    );
                }
                else
                {
                    // Interfaces cannot have parameter types that cannot be named
                    return SelectDiagnosticBasedOnModuleNameNoNameCheck(
                        symbolAccessibilityResult,
                        Diagnostics.Parameter_0_of_method_from_exported_interface_has_or_is_using_name_1_from_private_module_2,
                        Diagnostics.Parameter_0_of_method_from_exported_interface_has_or_is_using_private_name_1
                    );
                }

            case Kind.FunctionDeclaration:
            case Kind.FunctionType:
            case Kind.ArrowFunction:
            case Kind.FunctionExpression:
                return SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Parameter_0_of_exported_function_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                    Diagnostics.Parameter_0_of_exported_function_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Parameter_0_of_exported_function_has_or_is_using_private_name_1
                );
            case Kind.SetAccessor:
            case Kind.GetAccessor:
                return SelectDiagnosticBasedOnModuleName(
                    symbolAccessibilityResult,
                    Diagnostics.Parameter_0_of_accessor_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named,
                    Diagnostics.Parameter_0_of_accessor_has_or_is_using_name_1_from_private_module_2,
                    Diagnostics.Parameter_0_of_accessor_has_or_is_using_private_name_1
                );

            default:
                throw new DebugFailureException("Unknown parent for parameter: " + node.Parent!.Kind.String());
        }
    }

    internal static Message? GetTypeParameterConstraintVisibilityDiagnosticMessage(Node node, SymbolAccessibilityResult symbolAccessibilityResult)
    {
        // Type parameter constraints are named by user so we should always be able to name it
        switch (node.Parent!.Kind)
        {
            case Kind.ClassDeclaration:
                return Diagnostics.Type_parameter_0_of_exported_class_has_or_is_using_private_name_1;
            case Kind.InterfaceDeclaration:
                return Diagnostics.Type_parameter_0_of_exported_interface_has_or_is_using_private_name_1;
            case Kind.MappedType:
                return Diagnostics.Type_parameter_0_of_exported_mapped_object_type_is_using_private_name_1;
            case Kind.ConstructorType:
            case Kind.ConstructSignature:
                return Diagnostics.Type_parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_1;
            case Kind.CallSignature:
                return Diagnostics.Type_parameter_0_of_call_signature_from_exported_interface_has_or_is_using_private_name_1;
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
                if (IsStatic(node.Parent))
                {
                    return Diagnostics.Type_parameter_0_of_public_static_method_from_exported_class_has_or_is_using_private_name_1;
                }
                else if (node.Parent!.Parent!.Kind == Kind.ClassDeclaration)
                {
                    return Diagnostics.Type_parameter_0_of_public_method_from_exported_class_has_or_is_using_private_name_1;
                }
                else
                {
                    return Diagnostics.Type_parameter_0_of_method_from_exported_interface_has_or_is_using_private_name_1;
                }
            case Kind.FunctionType:
            case Kind.FunctionDeclaration:
                return Diagnostics.Type_parameter_0_of_exported_function_has_or_is_using_private_name_1;

            case Kind.InferType:
                return Diagnostics.Extends_clause_for_inferred_type_0_has_or_is_using_private_name_1;

            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                return Diagnostics.Type_parameter_0_of_exported_type_alias_has_or_is_using_private_name_1;

            default:
                throw new DebugFailureException("This is unknown parent for type parameter: " + node.Parent!.Kind.String());
        }
    }

    internal static Message? GetRelatedSuggestionByDeclarationKind(Kind kind)
    {
        switch (kind)
        {
            case Kind.ArrowFunction:
                return Diagnostics.Add_a_return_type_to_the_function_expression;
            case Kind.FunctionExpression:
                return Diagnostics.Add_a_return_type_to_the_function_expression;
            case Kind.MethodDeclaration:
                return Diagnostics.Add_a_return_type_to_the_method;
            case Kind.GetAccessor:
                return Diagnostics.Add_a_return_type_to_the_get_accessor_declaration;
            case Kind.SetAccessor:
                return Diagnostics.Add_a_type_to_parameter_of_the_set_accessor_declaration;
            case Kind.FunctionDeclaration:
                return Diagnostics.Add_a_return_type_to_the_function_declaration;
            case Kind.ConstructSignature:
                return Diagnostics.Add_a_return_type_to_the_function_declaration;
            case Kind.Parameter:
                return Diagnostics.Add_a_type_annotation_to_the_parameter_0;
            case Kind.VariableDeclaration:
                return Diagnostics.Add_a_type_annotation_to_the_variable_0;
            case Kind.PropertyDeclaration:
                return Diagnostics.Add_a_type_annotation_to_the_property_0;
            case Kind.PropertySignature:
                return Diagnostics.Add_a_type_annotation_to_the_property_0;
            case Kind.ExportAssignment:
                return Diagnostics.Move_the_expression_in_default_export_to_a_variable_and_add_a_type_annotation_to_it;
            default:
                return null;
        }
    }

    internal static Message? GetErrorByDeclarationKind(Kind kind)
    {
        switch (kind)
        {
            case Kind.FunctionExpression:
                return Diagnostics.Function_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations;
            case Kind.FunctionDeclaration:
                return Diagnostics.Function_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations;
            case Kind.ArrowFunction:
                return Diagnostics.Function_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations;
            case Kind.MethodDeclaration:
                return Diagnostics.Method_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations;
            case Kind.ConstructSignature:
                return Diagnostics.Method_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations;
            case Kind.GetAccessor:
                return Diagnostics.At_least_one_accessor_must_have_an_explicit_type_annotation_with_isolatedDeclarations;
            case Kind.SetAccessor:
                return Diagnostics.At_least_one_accessor_must_have_an_explicit_type_annotation_with_isolatedDeclarations;
            case Kind.Parameter:
                return Diagnostics.Parameter_must_have_an_explicit_type_annotation_with_isolatedDeclarations;
            case Kind.VariableDeclaration:
                return Diagnostics.Variable_must_have_an_explicit_type_annotation_with_isolatedDeclarations;
            case Kind.PropertyDeclaration:
                return Diagnostics.Property_must_have_an_explicit_type_annotation_with_isolatedDeclarations;
            case Kind.PropertySignature:
                return Diagnostics.Property_must_have_an_explicit_type_annotation_with_isolatedDeclarations;
            case Kind.ComputedPropertyName:
                return Diagnostics.Computed_property_names_on_class_or_object_literals_cannot_be_inferred_with_isolatedDeclarations;
            case Kind.SpreadAssignment:
                return Diagnostics.Objects_that_contain_spread_assignments_can_t_be_inferred_with_isolatedDeclarations;
            case Kind.ShorthandPropertyAssignment:
                return Diagnostics.Objects_that_contain_shorthand_properties_can_t_be_inferred_with_isolatedDeclarations;
            case Kind.ArrayLiteralExpression:
                return Diagnostics.Only_const_arrays_can_be_inferred_with_isolatedDeclarations;
            case Kind.ExportAssignment:
                return Diagnostics.Default_exports_can_t_be_inferred_with_isolatedDeclarations;
            case Kind.SpreadElement:
                return Diagnostics.Arrays_with_spread_elements_can_t_inferred_with_isolatedDeclarations;
            default:
                return null;
        }
    }

    internal static bool IsDeclarationEnoughForErrors(Node node)
    {
        return IsExportAssignment(node) || IsStatement(node) || IsVariableDeclaration(node) || IsPropertyDeclaration(node) || IsParameterDeclaration(node);
    }

    internal static bool IsFunctionLikeAndNotConstructor(Node node)
    {
        return IsFunctionLikeDeclaration(node) && !IsConstructorDeclaration(node);
    }

    internal static Node? FindNearestDeclaration(Node? node)
    {
        var result = FindAncestor(node, IsDeclarationEnoughForErrors);
        if (result == null)
        {
            return null;
        }
        if (IsExportAssignment(result))
        {
            return result;
        }
        if (IsReturnStatement(result))
        {
            return FindAncestor(result, IsFunctionLikeAndNotConstructor);
        }
        if (IsStatement(result))
        {
            return null;
        }
        return result;
    }

    internal static Diagnostic CreateEntityInTypeNodeError(Node node)
    {
        var diag = CreateDiagnosticForNode(node, Diagnostics.Type_containing_private_name_0_can_t_be_used_with_isolatedDeclarations, TypeScript.Scanner.Scanner.GetTextOfNode(node));
        AddParentDeclarationRelatedInfo(node, diag);
        return diag;
    }

    internal static void AddParentDeclarationRelatedInfo(Node node, Diagnostic diag)
    {
        var parentDeclaration = FindNearestDeclaration(node);
        if (parentDeclaration == null)
        {
            return;
        }
        var targetStr = "";
        if (!IsExportAssignment(parentDeclaration) && parentDeclaration.Name() != null)
        {
            targetStr = TypeScript.Scanner.Scanner.GetTextOfNode(parentDeclaration.Name()!);
        }
        diag.AddRelatedInfo(CreateDiagnosticForNode(parentDeclaration, GetRelatedSuggestionByDeclarationKind(parentDeclaration.Kind)!, targetStr));
    }

    internal static Diagnostic CreateAccessorTypeError(Node node)
    {
        var allDeclarations = GetAllAccessorDeclarationsForDeclaration(node, node.Symbol!.Declarations ?? []);
        var getAccessor = allDeclarations.GetAccessor;
        var setAccessor = allDeclarations.SetAccessor;
        var targetNode = node;
        if (IsSetAccessorDeclaration(node) && (node.Parameters()?.Length ?? 0) > 0)
        {
            targetNode = node.Parameters()![0];
        }
        var diag = CreateDiagnosticForNode(targetNode, GetErrorByDeclarationKind(node.Kind)!);
        if (setAccessor != null)
        {
            diag.AddRelatedInfo(CreateDiagnosticForNode(setAccessor, GetRelatedSuggestionByDeclarationKind(setAccessor.Kind)!));
        }
        if (getAccessor != null)
        {
            diag.AddRelatedInfo(CreateDiagnosticForNode(getAccessor, GetRelatedSuggestionByDeclarationKind(getAccessor.Kind)!));
        }
        return diag;
    }

    internal static Diagnostic CreateObjectLiteralError(Node node)
    {
        var diag = CreateDiagnosticForNode(node, GetErrorByDeclarationKind(node.Kind)!);
        AddParentDeclarationRelatedInfo(node, diag);
        return diag;
    }

    internal static Diagnostic CreateArrayLiteralError(Node node)
    {
        var diag = CreateDiagnosticForNode(node, GetErrorByDeclarationKind(node.Kind)!);
        AddParentDeclarationRelatedInfo(node, diag);
        return diag;
    }

    internal static Diagnostic CreateReturnTypeError(Node node)
    {
        var diag = CreateDiagnosticForNode(node, GetErrorByDeclarationKind(node.Kind)!);
        AddParentDeclarationRelatedInfo(node, diag);
        diag.AddRelatedInfo(CreateDiagnosticForNode(node, GetRelatedSuggestionByDeclarationKind(node.Kind)!));
        return diag;
    }

    internal static Diagnostic CreateBindingElementError(Node node)
    {
        return CreateDiagnosticForNode(node, Diagnostics.Binding_elements_with_initializers_can_t_be_exported_directly_with_isolatedDeclarations);
    }

    internal static Diagnostic CreateVariableOrPropertyError(Node node)
    {
        var diag = CreateDiagnosticForNode(node, GetErrorByDeclarationKind(node.Kind)!);
        diag.AddRelatedInfo(CreateDiagnosticForNode(node, GetRelatedSuggestionByDeclarationKind(node.Kind)!, TypeScript.Scanner.Scanner.GetTextOfNode(node.Name()!)));
        return diag;
    }

    internal static Diagnostic CreateExpressionError(Node node)
    {
        return CreateExpressionErrorEx(node, null);
    }

    internal static Diagnostic CreateClassExpressionError(Node node)
    {
        return CreateExpressionErrorEx(node, Diagnostics.Inference_from_class_expressions_is_not_supported_with_isolatedDeclarations);
    }

    internal static FindAncestorResult IsParentForIDDIagnostic(Node node)
    {
        if (IsExportAssignment(node))
        {
            return FindAncestorResult.True;
        }
        if (IsStatement(node))
        {
            return FindAncestorResult.Quit;
        }
        return ToFindAncestorResult(!IsParenthesizedExpression(node) && !IsAssertionExpression(node));
    }

    internal static Diagnostic CreateExpressionErrorEx(Node node, Message? diagnosticMessage)
    {
        var parentDeclaration = FindNearestDeclaration(node);
        if (parentDeclaration == null)
        {
            diagnosticMessage ??= Diagnostics.Expression_type_can_t_be_inferred_with_isolatedDeclarations;
            return CreateDiagnosticForNode(node, diagnosticMessage);
        }

        var targetStr = "";
        if (!IsExportAssignment(parentDeclaration) && parentDeclaration.Name() != null)
        {
            targetStr = TypeScript.Scanner.Scanner.GetTextOfNode(parentDeclaration.Name()!);
        }
        var parent = FindAncestorOrQuit(node.Parent, IsParentForIDDIagnostic);

        if (parentDeclaration == parent)
        {
            diagnosticMessage ??= GetErrorByDeclarationKind(parentDeclaration.Kind);
            var diag = CreateDiagnosticForNode(node, diagnosticMessage!);
            diag.AddRelatedInfo(CreateDiagnosticForNode(parentDeclaration, GetRelatedSuggestionByDeclarationKind(parentDeclaration.Kind)!, targetStr));
            return diag;
        }
        diagnosticMessage ??= Diagnostics.Expression_type_can_t_be_inferred_with_isolatedDeclarations;
        var diag2 = CreateDiagnosticForNode(node, diagnosticMessage);
        diag2.AddRelatedInfo(CreateDiagnosticForNode(parentDeclaration, GetRelatedSuggestionByDeclarationKind(parentDeclaration.Kind)!, targetStr));
        diag2.AddRelatedInfo(CreateDiagnosticForNode(node, Diagnostics.Add_satisfies_and_a_type_assertion_to_this_expression_satisfies_T_as_T_to_make_the_type_explicit));
        return diag2;
    }

    internal static Func<Node, Diagnostic> CreateGetIsolatedDeclarationErrors(EmitResolver resolver)
    {
        Diagnostic createParameterError(Node node)
        {
            if (IsSetAccessorDeclaration(node.Parent!))
            {
                return CreateAccessorTypeError(node.Parent!);
            }
            var addUndefined = resolver.RequiresAddingImplicitUndefinedUnsafe(node, null!, null!); // skip checker lock - node builder will already have one
            if (!addUndefined && node.Initializer() != null)
            {
                return CreateExpressionError(node.Initializer()!);
            }
            var message = GetErrorByDeclarationKind(node.Kind)!;
            if (addUndefined)
            {
                message = Diagnostics.Declaration_emit_for_this_parameter_requires_implicitly_adding_undefined_to_its_type_This_is_not_supported_with_isolatedDeclarations;
            }
            var diag = CreateDiagnosticForNode(node, message);
            var targetStr = TypeScript.Scanner.Scanner.GetTextOfNode(node.Name()!);
            diag.AddRelatedInfo(CreateDiagnosticForNode(node, GetRelatedSuggestionByDeclarationKind(node.Kind)!, targetStr));
            return diag;
        }

        return node =>
        {
            var heritageClause = FindAncestor(node, IsHeritageClause);
            if (heritageClause != null)
            {
                return CreateDiagnosticForNode(node, Diagnostics.Extends_clause_can_t_contain_an_expression_with_isolatedDeclarations);
            }
            if (IsPartOfTypeNode(node) || IsTypeQueryNode(node))
            {
                return CreateEntityInTypeNodeError(node);
            }
            if (IsEntityName(node) || IsEntityNameExpression(node))
            {
                return CreateEntityInTypeNodeError(node);
            }
            switch (node.Kind)
            {
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                    return CreateAccessorTypeError(node);
                case Kind.ComputedPropertyName:
                case Kind.ShorthandPropertyAssignment:
                case Kind.SpreadAssignment:
                    return CreateObjectLiteralError(node);
                case Kind.ArrayLiteralExpression:
                case Kind.SpreadElement:
                    return CreateArrayLiteralError(node);
                case Kind.MethodDeclaration:
                case Kind.ConstructSignature:
                case Kind.FunctionExpression:
                case Kind.ArrowFunction:
                case Kind.FunctionDeclaration:
                    return CreateReturnTypeError(node);
                case Kind.BindingElement:
                    return CreateBindingElementError(node);
                case Kind.PropertyDeclaration:
                case Kind.VariableDeclaration:
                    return CreateVariableOrPropertyError(node);
                case Kind.Parameter:
                    return createParameterError(node);
                case Kind.PropertyAssignment:
                    return CreateExpressionError(node.Initializer()!);
                case Kind.ClassExpression:
                    return CreateClassExpressionError(node);
                default:
                    return CreateExpressionError(node);
            }
        };
    }
}
