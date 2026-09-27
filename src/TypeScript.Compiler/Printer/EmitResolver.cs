// ported from: internal/printer/emitresolver.go
//
// Go 的类型别名（ast.XxxNode = *Node 系列）在 C# 里不存在，参数/返回类型直接用 `Node`（docs/ast.md §2）。
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Evaluator;
using TypeScript.NodeBuilder;

namespace TypeScript.Printer;

/// <summary>Go 的 <c>type SymbolAccessibility int32</c>。</summary>
public enum SymbolAccessibility
{
    Accessible = 0,
    NotAccessible = 1,
    CannotBeNamed = 2,
    NotResolved = 3,
}

/// <summary>Go 的 <c>type SymbolAccessibilityResult struct</c>。</summary>
public sealed class SymbolAccessibilityResult
{
    public SymbolAccessibility Accessibility;

    /// <summary>aliases that need to have this symbol visible</summary>
    public Node[] AliasesToMakeVisible = [];

    /// <summary>Optional - symbol name that results in error</summary>
    public string ErrorSymbolName = "";

    /// <summary>Optional - node that results in error</summary>
    public Node? ErrorNode;

    /// <summary>Optional - If the symbol is not visible from module, module's name</summary>
    public string ErrorModuleName = "";
}

/// <summary>
/// Indicates how to serialize the name for a TypeReferenceNode when emitting decorator metadata
///
/// <c>@internal</c>
/// </summary>
public enum TypeReferenceSerializationKind
{
    /// <summary>
    /// The TypeReferenceNode could not be resolved.
    /// The type name should be emitted using a safe fallback.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The TypeReferenceNode resolves to a type with a constructor
    /// function that can be reached at runtime (e.g. a `class`
    /// declaration or a `var` declaration for the static side
    /// of a type, such as the global `Promise` type in lib.d.ts).
    /// </summary>
    TypeWithConstructSignatureAndValue = 1,

    /// <summary>The TypeReferenceNode resolves to a Void-like, Nullable, or Never type.</summary>
    VoidNullableOrNeverType = 2,

    /// <summary>The TypeReferenceNode resolves to a Number-like type.</summary>
    NumberLikeType = 3,

    /// <summary>The TypeReferenceNode resolves to a BigInt-like type.</summary>
    BigIntLikeType = 4,

    /// <summary>The TypeReferenceNode resolves to a String-like type.</summary>
    StringLikeType = 5,

    /// <summary>The TypeReferenceNode resolves to a Boolean-like type.</summary>
    BooleanType = 6,

    /// <summary>The TypeReferenceNode resolves to an Array-like type.</summary>
    ArrayLikeType = 7,

    /// <summary>The TypeReferenceNode resolves to the ESSymbol type.</summary>
    ESSymbolType = 8,

    /// <summary>The TypeReferenceNode resolved to the global Promise constructor symbol.</summary>
    Promise = 9,

    /// <summary>The TypeReferenceNode resolves to a Function type or a type with call signatures.</summary>
    TypeWithCallSignature = 10,

    /// <summary>The TypeReferenceNode resolves to any other type.</summary>
    ObjectType = 11,
}

public interface EmitResolver : ReferenceResolver
{
    bool IsReferencedAliasDeclaration(Node node);

    bool IsValueAliasDeclaration(Node node);

    bool IsTopLevelValueImportEqualsWithEntityName(Node node);

    void MarkLinkedReferencesRecursively(SourceFile file);

    SourceFile? GetExternalModuleFileFromDeclaration(Node node);

    ModifierFlags GetEffectiveDeclarationFlags(Node node, ModifierFlags flags);

    // decorator metadata
    TypeReferenceSerializationKind GetTypeReferenceSerializationKind(Node name, Node serialScope);

    // const enum inlining
    object? GetConstantValue(Node node);

    // JSX Emit
    Node GetJsxFactoryEntity(Node location);

    Node GetJsxFragmentFactoryEntity(Node location);

    /// <summary>for overriding the reference resolver behavior for generated identifiers</summary>
    void SetReferencedImportDeclaration(Node node, Node @ref);

    // declaration emit checker functionality projections
    void PrecalculateDeclarationEmitVisibility(SourceFile file);

    SymbolAccessibilityResult IsSymbolAccessible(Symbol symbol, Node enclosingDeclaration, SymbolFlags meaning, bool shouldComputeAliasToMarkVisible);

    /// <summary>previously SymbolVisibilityResult in strada - ErrorModuleName never set</summary>
    SymbolAccessibilityResult IsEntityNameVisible(Node entityName, Node enclosingDeclaration);

    bool IsExpandoFunctionDeclaration(Node node);

    bool IsExpandoFunctionDeclarationUnsafe(Node node);

    bool IsLiteralConstDeclaration(Node node);

    bool RequiresAddingImplicitUndefined(Node node, Symbol symbol, Node enclosingDeclaration);

    bool IsDeclarationVisible(Node node);

    bool IsNameResolvable(Node location, string name);

    bool IsImportRequiredByAugmentation(ImportDeclaration decl);

    bool IsDefinitelyReferenceToGlobalSymbolObject(Node node);

    bool IsImplementationOfOverload(Node node);

    Result GetEnumMemberValue(Node node);

    bool IsLateBound(Node node);

    bool IsOptionalParameter(Node node);

    bool IsThisPropertyAssignmentDeclarationRedundant(Node node);

    // isolatedDeclarations-specific declaration emit
    Symbol[] GetPropertiesOfContainerFunction(Node node);

    bool RequiresAddingImplicitUndefinedUnsafe(Node node, Symbol symbol, Node enclosingDeclaration);

    Node? GetReferencedValueDeclarationUnsafe(Identifier node);

    // Node construction for declaration emit
    Node CreateTypeOfDeclaration(EmitContext emitContext, Node declaration, Node enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker);

    Node CreateReturnTypeOfSignatureDeclaration(EmitContext emitContext, Node signatureDeclaration, Node enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker);

    Node[] CreateTypeParametersOfSignatureDeclaration(EmitContext emitContext, Node signatureDeclaration, Node enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker);

    Node? CreateLiteralConstValue(EmitContext emitContext, Node node, SymbolTracker tracker);

    Node CreateTypeOfExpression(EmitContext emitContext, Node expression, Node enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker);

    Node[] CreateLateBoundIndexSignatures(EmitContext emitContext, Node container, Node enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker);

    Node? TryJSTypeNodeToTypeNode(EmitContext emitContext, Node typeNode, Node enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker);
}
