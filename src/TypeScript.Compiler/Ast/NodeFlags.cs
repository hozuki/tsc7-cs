// ported from: internal/ast/nodeflags.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.3）：
// - `type NodeFlags uint32` + 包级常量 → `enum NodeFlags : uint`：成员名 = Go 常量名去掉 `NodeFlags` 前缀，
//   数值逐条与 Go 一致（会进 baseline）。
// - Go 里 `NodeFlagsHasAggregatedChildData` 只剩注释（已废弃），这里保留同样的注释占位。
namespace TypeScript.Ast;

[Flags]
public enum NodeFlags : uint
{
    None = 0,
    Let = 1u << 0,                             // Variable declaration
    Const = 1u << 1,                           // Variable declaration
    Using = 1u << 2,                           // Variable declaration
    Reparsed = 1u << 3,                        // Node was synthesized during parsing
    Synthesized = 1u << 4,                     // Node was synthesized during transformation
    OptionalChain = 1u << 5,                   // Chained MemberExpression rooted to a pseudo-OptionalExpression
    ExportContext = 1u << 6,                   // Export context (initialized by binding)
    ContainsThis = 1u << 7,                    // Interface contains references to "this"
    HasImplicitReturn = 1u << 8,               // If function implicitly returns on one of codepaths (initialized by binding)
    HasExplicitReturn = 1u << 9,               // If function has explicit reachable return on one of codepaths (initialized by binding)
    DisallowInContext = 1u << 10,              // If node was parsed in a context where 'in-expressions' are not allowed
    YieldContext = 1u << 11,                   // If node was parsed in the 'yield' context created when parsing a generator
    DecoratorContext = 1u << 12,               // If node was parsed as part of a decorator
    AwaitContext = 1u << 13,                   // If node was parsed in the 'await' context created when parsing an async function
    DisallowConditionalTypesContext = 1u << 14, // If node was parsed in a context where conditional types are not allowed
    ThisNodeHasError = 1u << 15,               // If the parser encountered an error when parsing the code that created this node
    JavaScriptFile = 1u << 16,                 // If node was parsed in a JavaScript
    ThisNodeOrAnySubNodesHasError = 1u << 17,  // If this node or any of its children had an error
    HasAsyncFunctions = 1u << 18,              // If the file has async functions (initialized by binding)
    // HasAggregatedChildData is deprecated. Use `subtreeFacts` instead.

    // These flags will be set when the parser encounters a dynamic import expression or 'import.meta' to avoid
    // walking the tree if the flags are not set. However, these flags are just a approximation
    // (hence why it's named "PossiblyContainsDynamicImport") because once set, the flags never get cleared.
    // During editing, if a dynamic import is removed, incremental parsing will *NOT* clear this flag.
    // This means that the tree will always be traversed during module resolution, or when looking for external module indicators.
    // However, the removal operation should not occur often and in the case of the
    // removal, it is likely that users will add the import anyway.
    // The advantage of this approach is its simplicity. For the case of batch compilation,
    // we guarantee that users won't have to pay the price of walking the tree if a dynamic import isn't used.
    PossiblyContainsDynamicImport = 1u << 19,
    PossiblyContainsImportMeta = 1u << 20,

    HasJSDoc = 1u << 21,                       // If node has preceding JSDoc comment(s)
    JSDoc = 1u << 22,                          // If node was parsed inside jsdoc
    Ambient = 1u << 23,                        // If node was inside an ambient context -- a declaration file, or inside something with the `declare` modifier.
    InWithStatement = 1u << 24,                // If any ancestor of node was the `statement` of a WithStatement (not the `expression`)
    JsonFile = 1u << 25,                       // If node was parsed in a Json
    PossiblyContainsDeprecatedTag = 1u << 26,  // Set during parse if comment text contains '@deprecated'; must confirm via JSDoc lookup
    Unreachable = 1u << 27,                    // If node is unreachable according to the binder
    ReparserTransformedLiteral = 1u << 28,     // If node was transformed during parsing, making its' naive text source not match the AST

    BlockScoped = Let | Const | Using,
    Constant = Const | Using,
    AwaitUsing = Const | Using,                // Variable declaration (NOTE: on a single node these flags would otherwise be mutually exclusive)

    ReachabilityCheckFlags = HasImplicitReturn | HasExplicitReturn,
    ReachabilityAndEmitFlags = ReachabilityCheckFlags | HasAsyncFunctions,

    // Parsing context flags
    ContextFlags = DisallowInContext | DisallowConditionalTypesContext | YieldContext | DecoratorContext | AwaitContext | JavaScriptFile | InWithStatement | Ambient,

    // Exclude these flags when parsing a Type
    TypeExcludesFlags = YieldContext | AwaitContext,

    // Represents all flags that are potentially set once and
    // never cleared on SourceFiles which get re-used in between incremental parses.
    // See the comment above on `PossiblyContainsDynamicImport` and `PossiblyContainsImportMeta`.
    PermanentlySetIncrementalFlags = PossiblyContainsDynamicImport | PossiblyContainsImportMeta,

    // The following flags repurpose other NodeFlags as different meanings for Identifier nodes
    IdentifierHasExtendedUnicodeEscape = ContainsThis,      // Indicates whether the identifier contains an extended unicode escape sequence
    IdentifierIsInJSDocNamespace = HasAsyncFunctions,       // Indicates the identifier is the innermost name of a JSDoc namespace declaration

    // The following flag repurposes other NodeFlags for ModuleDeclaration nodes
    NestedNamespace = OptionalChain,                        // If ModuleDeclaration is a nested namespace (e.g. inner part of A.B.C)
}
