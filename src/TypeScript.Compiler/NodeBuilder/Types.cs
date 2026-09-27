// ported from: internal/nodebuilder/types.go
//
// 与 Go 的差异（逐条）：
// - Go 的 `type Flags uint32` / `type InternalFlags int32` 是"底层类型 + 常量组"的位掩码（docs/architecture.md §3.3）：
//   C# 用 `[Flags] enum`，成员名 = Go 常量名去掉类型前缀（`FlagsNone` → `Flags.None`、
//   `InternalFlagsWriteComputedProps` → `InternalFlags.WriteComputedProps`），数值与 Go 逐条一致。
//   调用点照 Go 写 `Flags.NoTruncation` / `InternalFlags.AllowUnresolvedNames`。
// - Go 的 `SymbolTracker` 是导出接口，名字原样保留（与 Ast 的 `HasFileName`/`SourceFileLike` 同一约定）。
// - Go 的 `*ast.X` → `X?`；`ast.SourceFile`/`ast.Symbol`/`ast.Node` 均为引用类型。
namespace TypeScript.NodeBuilder;

using TypeScript.Ast;

// Exports interfaces and types defining the node builder - concrete implementations are on top of the checker,
// but these types and interfaces are used by the emit resolver in the printer
public interface SymbolTracker
{
    bool TrackSymbol(Symbol symbol, Node enclosingDeclaration, SymbolFlags meaning);

    void ReportInaccessibleThisError();

    void ReportPrivateInBaseOfClassExpression(string propertyName);

    void ReportInaccessibleUniqueSymbolError();

    void ReportCyclicStructureError();

    void ReportLikelyUnsafeImportRequiredError(string specifier, string symbolName);

    void ReportTruncationError();

    void ReportNonlocalAugmentation(SourceFile containingFile, Symbol parentSymbol, Symbol augmentingSymbol);

    void ReportNonSerializableProperty(string propertyName);

    void ReportInferenceFallback(Node node);

    void PushErrorFallbackNode(Node node);

    void PopErrorFallbackNode();
}

// NOTE: If modifying this enum, must modify `TypeFormatFlags` too!
[Flags]
public enum Flags : uint
{
    None = 0,

    // Options
    NoTruncation = 1 << 0,
    WriteArrayAsGenericType = 1 << 1,
    GenerateNamesForShadowedTypeParams = 1 << 2,
    UseStructuralFallback = 1 << 3,
    ForbidIndexedAccessSymbolReferences = 1 << 4,
    WriteTypeArgumentsOfSignature = 1 << 5,
    UseFullyQualifiedType = 1 << 6,
    UseOnlyExternalAliasing = 1 << 7,
    SuppressAnyReturnType = 1 << 8,
    WriteTypeParametersInQualifiedName = 1 << 9,
    MultilineObjectLiterals = 1 << 10,
    WriteClassExpressionAsTypeLiteral = 1 << 11,
    UseTypeOfFunction = 1 << 12,
    OmitParameterModifiers = 1 << 13,
    UseAliasDefinedOutsideCurrentScope = 1 << 14,
    UseSingleQuotesForStringLiteralType = 1 << 28,
    NoTypeReduction = 1 << 29,
    UseInstantiationExpressions = 1 << 30,
    OmitThisParameter = 1 << 25,
    WriteCallStyleSignature = 1 << 27,

    // Error handling
    AllowThisInObjectLiteral = 1 << 15,
    AllowQualifiedNameInPlaceOfIdentifier = 1 << 16,
    AllowAnonymousIdentifier = 1 << 17,
    AllowEmptyUnionOrIntersection = 1 << 18,
    AllowEmptyTuple = 1 << 19,
    AllowUniqueESSymbolType = 1 << 20,
    AllowEmptyIndexInfoType = 1 << 21,

    // Errors (cont.)
    AllowNodeModulesRelativePaths = 1 << 26,
    IgnoreErrors = AllowThisInObjectLiteral | AllowQualifiedNameInPlaceOfIdentifier | AllowAnonymousIdentifier |
        AllowEmptyUnionOrIntersection | AllowEmptyTuple | AllowEmptyIndexInfoType | AllowNodeModulesRelativePaths,

    // State
    InObjectTypeLiteral = 1 << 22,
    InTypeAlias = 1 << 23,
    InInitialEntityName = 1 << 24,
}

/** @internal */
[Flags]
public enum InternalFlags
{
    None = 0,
    WriteComputedProps = 1 << 0,
    NoSyntacticPrinter = 1 << 1,
    DoNotIncludeSymbolChain = 1 << 2,
    AllowUnresolvedNames = 1 << 3,
}
