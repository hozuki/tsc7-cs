// ported from: internal/checker/checker.go:912-1306
//   （NewChecker、createFileIndexMap、countGlobalSymbols、reportUnreliableWorker/reportUnmeasurableWorker、
//     getGlobalTypeResolver/getGlobalTypeAliasResolver/getGlobalValueSymbolResolver/getGlobalTypeSymbolResolver/
//     getGlobalTypesResolver、getGlobalTypeAliasSymbol、GetTypeAliasTypeParameters、getGlobalType、
//     getGlobalTypeDeclaration、getGlobalSymbol、initializeClosures、initializeIterationResolvers）
//
// 与 Go 的偏离（逐条）：
// 1) `NewChecker` 返回 `(*Checker, *sync.Mutex)` → C# `(Checker, Lock)`（`Lock` = System.Threading.Lock，Main 仲裁）。
// 2) `getGlobalSymbol` 与 exports.go:99 的导出 `GetGlobalSymbol` 同名 → 未导出者按
//    artifacts/checker-method-collisions.md 改名 `GetGlobalSymbolWorker`（引用它的调用点也用新名）。
// 3) `core.Memoize(func() T)` → `Core.Memoize<T>(Func<T>)`，返回 `Func<T>`（Go 的 `func() T`）。
// 4) `make(ast.SymbolTable, n)` → `new SymbolTable()`：C# 的 SymbolTable 派生自 Dictionary 但没有带容量的构造器
//    （Ast/Symbol.cs），预占容量这一点无法复刻（性能偏离，可忽略：仅在 NewChecker 发生一次）。
// 5) `core.Map(xs, c.getStringLiteralType)`（方法组传给泛型方法）在 C# 里改成 lambda：泛型实参无法从方法组推断。
// 6) `debug.Assert`/`panic` 未在本片出现；`core.IfElse(reportErrors, msg, nil)` → `IfElse(reportErrors, msg, (Message?)null)`。
// 7) `typeofNEFacts` 是 flow.go:635 的包级 var（flow 片所有，C# 名 `_typeofNEFacts`）；`newFunctionTypeMapper` 是
//    mapper.go:237 的包级函数（mapper 片所有，C# 名 `NewFunctionTypeMapper`）—— 均属跨片引用。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using System.Threading;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    public static (Checker Checker, Lock Mu) NewChecker(Program program, Tracer? tracer)
    {
        program.BindSourceFiles();

        var c = new Checker();
        c._id = (uint)Interlocked.Increment(ref _nextCheckerId);
        c._tracer = tracer;
        c._program = program;
        c._compilerOptions = program.Options();
        c._files = program.SourceFiles();
        c._fileIndexMap = CreateFileIndexMap(c._files);
        c._compareSymbols = c.CompareSymbolsWorker; // Closure optimization
        c._compareSymbolChains = c.CompareSymbolChainsWorker; // Closure optimization
        c._languageVersion = c._compilerOptions.GetEmitScriptTarget();
        c._moduleKind = c._compilerOptions.GetEmitModuleKind();
        c._moduleResolutionKind = c._compilerOptions.GetModuleResolutionKind();
        c._legacyDecorators = c._compilerOptions.ExperimentalDecorators == Tristate.TSTrue;
        c._emitStandardClassFields = c._compilerOptions.GetEmitStandardClassFields();
        c._strictNullChecks = c._compilerOptions.GetStrictOptionValue(c._compilerOptions.StrictNullChecks);
        c._strictFunctionTypes = c._compilerOptions.GetStrictOptionValue(c._compilerOptions.StrictFunctionTypes);
        c._strictBindCallApply = c._compilerOptions.GetStrictOptionValue(c._compilerOptions.StrictBindCallApply);
        c._strictPropertyInitialization = c._compilerOptions.GetStrictOptionValue(c._compilerOptions.StrictPropertyInitialization);
        c._strictBuiltinIteratorReturn = c._compilerOptions.GetStrictOptionValue(c._compilerOptions.StrictBuiltinIteratorReturn);
        c._noImplicitAny = c._compilerOptions.GetStrictOptionValue(c._compilerOptions.NoImplicitAny);
        c._noImplicitThis = c._compilerOptions.GetStrictOptionValue(c._compilerOptions.NoImplicitThis);
        c._useUnknownInCatchVariables = c._compilerOptions.GetStrictOptionValue(c._compilerOptions.UseUnknownInCatchVariables);
        c._exactOptionalPropertyTypes = c._compilerOptions.ExactOptionalPropertyTypes == Tristate.TSTrue;
        c._canCollectSymbolAliasAccessibilityData = c._compilerOptions.VerbatimModuleSyntax.IsFalseOrUnknown();
        c._arrayVariances = [VarianceFlags.Covariant];
        c._globals = new SymbolTable();
        c._evaluate = TypeScript.Evaluator.EvaluatorFunctions.NewEvaluator(c.EvaluateEntity, OuterExpressionKinds.Parentheses);
        // Go: 这些 link store 在 Go 里是 Checker 结构体的内联值（checker.go:674-700，`nodeLinks nodeLinkStore[NodeLinks]`、
        // `valueSymbolLinks symbolArenaLinkStore[ValueSymbolLinks]` …），零值即可直接使用 ⇒ Go 的 newChecker 不需要赋值；
        // C# 的 LinkStore/NodeLinkStore/SymbolLinkStore 是 class，必须显式构造（先例：NodeBuilderImpl.cs:165、EmitResolver.cs:74）。
        c._nodeLinks = new();
        c._signatureLinks = new();
        c._symbolNodeLinks = new();
        c._typeNodeLinks = new();
        c._enumMemberLinks = new();
        c._assertionLinks = new();
        c._arrayLiteralLinks = new();
        c._switchStatementLinks = new();
        c._jsxElementLinks = new();
        c._computedNameLinks = new();
        c._symbolReferenceLinks = new();
        c._valueSymbolLinks = new();
        c._mappedSymbolLinks = new();
        c._deferredSymbolLinks = new();
        c._aliasSymbolLinks = new();
        c._moduleSymbolLinks = new();
        c._lateBoundLinks = new();
        c._exportTypeLinks = new();
        c._membersAndExportsLinks = new();
        c._typeAliasLinks = new();
        c._declaredTypeLinks = new();
        c._spreadLinks = new();
        c._varianceLinks = new();
        c.ReverseMappedSymbolLinks = new();
        c._markedAssignmentSymbolLinks = new();
        c._symbolContainerLinks = new();
        c._sourceFileLinks = new();
        // Go: `factory ast.NodeFactory` 同样是零值可用的内联结构体（checker.go:674，全仓无 `c.factory = `）——
        // C# 侧 `NodeFactory` 是 class，等价的零值构造是 `new NodeFactory()`（Parser.cs:327 同一写法）；
        // `_factory.NewXxx` 在 C2.IndexConstraints/C5.Destructuring/C8.DecoratorsAndDiscriminants/Flow.Reachability 都有调用点。
        c._factory = new NodeFactory();
        c._stringLiteralTypes = new();
        c._numberLiteralTypes = new();
        c._bigintLiteralTypes = new();
        c._enumLiteralTypes = new();
        c._enumNaNLiteralTypes = new();
        c._indexedAccessTypes = new();
        c._templateLiteralTypes = new();
        c._stringMappingTypes = new();
        c._uniqueESSymbolTypes = new();
        c._thisExpandoKinds = new();
        c._thisExpandoLocations = new();
        c._subtypeReductionCache = new();
        c._cachedTypes = new();
        c._cachedSignatures = new();
        c._undefinedProperties = new();
        c._narrowedTypes = new();
        c._assignmentReducedTypes = new();
        c._discriminatedContextualTypes = new();
        c._instantiationExpressionTypes = new();
        c._substitutionTypes = new();
        c._reverseMappedCache = new();
        c._reverseHomomorphicMappedCache = new();
        c._iterationTypesCache = new();
        c._undefinedSymbol = c.NewSymbol(SymbolFlags.Property, "undefined");
        c._argumentsSymbol = c.NewSymbol(SymbolFlags.Property, "arguments");
        c._requireSymbol = c.NewSymbol(SymbolFlags.Property, "require");
        c._unknownSymbol = c.NewSymbol(SymbolFlags.Property, "unknown");
        c._unresolvedSymbols = new();
        c._errorTypes = new();
        c._moduleSymbols = new();
        c._globalThisSymbol = c.NewSymbolEx(SymbolFlags.Module, "globalThis", CheckFlags.Readonly);
        c._globalThisSymbol.Exports = c._globals;
        c._globals[c._globalThisSymbol.Name] = c._globalThisSymbol;
        c._resolveName = c.CreateNameResolver().Resolve;
        c._resolveNameForSymbolSuggestion = c.CreateNameResolverForSuggestion().Resolve;
        c._tupleTypes = new();
        c._unionTypes = new();
        c._unionOfUnionTypes = new();
        c._intersectionTypes = new();
        c._propertiesTypes = new();
        c._mergedSymbols = new();
        c._patternForType = new();
        c._contextFreeTypes = new();
        c._anyType = c.NewIntrinsicType(TypeFlags.Any, "any");
        c._autoType = c.NewIntrinsicTypeEx(TypeFlags.Any, "any", ObjectFlags.NonInferrableType);
        c._wildcardType = c.NewIntrinsicType(TypeFlags.Any, "any");
        c._blockedStringType = c.NewIntrinsicType(TypeFlags.Any, "any");
        c._errorType = c.NewIntrinsicType(TypeFlags.Any, "error");
        c._unresolvedType = c.NewIntrinsicType(TypeFlags.Any, "unresolved");
        c._nonInferrableAnyType = c.NewIntrinsicTypeEx(TypeFlags.Any, "any", ObjectFlags.ContainsWideningType);
        c._intrinsicMarkerType = c.NewIntrinsicType(TypeFlags.Any, "intrinsic");
        c._unknownType = c.NewIntrinsicType(TypeFlags.Unknown, "unknown");
        c._undefinedType = c.NewIntrinsicType(TypeFlags.Undefined, "undefined");
        c._undefinedWideningType = c.CreateWideningType(c._undefinedType);
        c._missingType = c.NewIntrinsicType(TypeFlags.Undefined, "undefined");
        c._undefinedOrMissingType = IfElse(c._exactOptionalPropertyTypes, c._missingType, c._undefinedType);
        c._optionalType = c.NewIntrinsicType(TypeFlags.Undefined, "undefined");
        c._nullType = c.NewIntrinsicType(TypeFlags.Null, "null");
        c._nullWideningType = c.CreateWideningType(c._nullType);
        c._stringType = c.NewIntrinsicType(TypeFlags.String, "string");
        c._numberType = c.NewIntrinsicType(TypeFlags.Number, "number");
        c._bigintType = c.NewIntrinsicType(TypeFlags.BigInt, "bigint");
        c._regularFalseType = c.NewLiteralType(TypeFlags.BooleanLiteral, false, null);
        c._falseType = c.NewLiteralType(TypeFlags.BooleanLiteral, false, c._regularFalseType);
        c._regularFalseType.AsLiteralType()!._freshType = c._falseType;
        c._falseType.AsLiteralType()!._freshType = c._falseType;
        c._regularTrueType = c.NewLiteralType(TypeFlags.BooleanLiteral, true, null);
        c._trueType = c.NewLiteralType(TypeFlags.BooleanLiteral, true, c._regularTrueType);
        c._regularTrueType.AsLiteralType()!._freshType = c._trueType;
        c._trueType.AsLiteralType()!._freshType = c._trueType;
        c._booleanType = c.GetUnionTypeWorker([c._regularFalseType, c._regularTrueType]);
        c._esSymbolType = c.NewIntrinsicType(TypeFlags.ESSymbol, "symbol");
        c._voidType = c.NewIntrinsicType(TypeFlags.Void, "void");
        c._neverType = c.NewIntrinsicType(TypeFlags.Never, "never");
        c._silentNeverType = c.NewIntrinsicTypeEx(TypeFlags.Never, "never", ObjectFlags.NonInferrableType);
        c._implicitNeverType = c.NewIntrinsicType(TypeFlags.Never, "never");
        c._unreachableNeverType = c.NewIntrinsicType(TypeFlags.Never, "never");
        c._nonPrimitiveType = c.NewIntrinsicType(TypeFlags.NonPrimitive, "object");
        c._stringOrNumberType = c.GetUnionTypeWorker([c._stringType, c._numberType]);
        c._stringNumberSymbolType = c.GetUnionTypeWorker([c._stringType, c._numberType, c._esSymbolType]);
        c._numberOrBigIntType = c.GetUnionTypeWorker([c._numberType, c._bigintType]);
        c._numericStringType = c.GetTemplateLiteralType(["", ""], [c._numberType]); // The `${number}` type
        c._templateConstraintType = c.GetUnionTypeWorker([c._stringType, c._numberType, c._booleanType, c._bigintType, c._nullType, c._undefinedType]);
        c._uniqueLiteralType = c.NewIntrinsicType(TypeFlags.Never, "never"); // Special `never` flagged by union reduction to behave as a literal
        c._uniqueLiteralMapper = NewFunctionTypeMapper(c.GetUniqueLiteralTypeForTypeParameter);
        c._reportUnreliableMapper = NewFunctionTypeMapper(c.ReportUnreliableWorker);
        c._reportUnmeasurableMapper = NewFunctionTypeMapper(c.ReportUnmeasurableWorker);
        c._restrictiveMapper = NewFunctionTypeMapper(c.RestrictiveMapperWorker);
        c._permissiveMapper = NewFunctionTypeMapper(c.PermissiveMapperWorker);
        c._emptyObjectType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._emptyJsxObjectType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._emptyFreshJsxObjectType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._emptyTypeLiteralType = c.NewAnonymousType(c.NewSymbol(SymbolFlags.TypeLiteral, InternalSymbolNameType), null, null, null, null);
        c._unknownEmptyObjectType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._unknownUnionType = c.CreateUnknownUnionType();
        c._emptyGenericType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._emptyGenericType.AsObjectType()!._instantiations = new();
        c._anyFunctionType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._anyFunctionType._objectFlags |= ObjectFlags.NonInferrableType;
        c._noConstraintType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._circularConstraintType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._resolvingDefaultType = c.NewAnonymousType(null! /*symbol*/, null, null, null, null);
        c._markerSuperType = c.NewTypeParameter(null);
        c._markerSubType = c.NewTypeParameter(null);
        c._markerSubType.AsTypeParameter()!._constraint = c._markerSuperType;
        c._markerOtherType = c.NewTypeParameter(null);
        c._markerSuperTypeForCheck = c.NewTypeParameter(null);
        c._markerSubTypeForCheck = c.NewTypeParameter(null);
        c._markerSubTypeForCheck.AsTypeParameter()!._constraint = c._markerSuperTypeForCheck;
        c._noTypePredicate = new TypePredicate { _kind = TypePredicateKind.Identifier, _parameterIndex = 0, _parameterName = "<<unresolved>>", _t = c._anyType };
        c._anySignature = c.NewSignature(SignatureFlags.None, null!, null!, null, null!, c._anyType, null, 0);
        c._unknownSignature = c.NewSignature(SignatureFlags.None, null!, null!, null, null!, c._errorType, null, 0);
        c._resolvingSignature = c.NewSignature(SignatureFlags.None, null!, null!, null, null!, c._anyType, null, 0);
        c._silentNeverSignature = c.NewSignature(SignatureFlags.None, null!, null!, null, null!, c._silentNeverType, null, 0);
        c._cachedArgumentsReferenced = new();
        c._enumNumberIndexInfo = new IndexInfo { _keyType = c._numberType, _valueType = c._stringType, _isReadonly = true };
        c._anyBaseTypeIndexInfo = new IndexInfo { _keyType = c._stringType, _valueType = c._anyType, _isReadonly = false };
        c._emptyStringType = c.GetStringLiteralType("");
        c._zeroType = c.GetNumberLiteralType(0);
        c._zeroBigIntType = c.GetBigIntLiteralType(default);
        c._typeofType = c.GetUnionTypeWorker(Map(TypeofNEKeys(), key => c.GetStringLiteralType(key)));
        c._flowLoopCache = new();
        c._flowNodeReachable = new();
        c._flowNodePostSuper = new();
        c._subtypeRelation = new Relation();
        c._strictSubtypeRelation = new Relation();
        c._assignableRelation = new Relation();
        c._comparableRelation = new Relation();
        c._identityRelation = new Relation();
        c._enumRelation = new();
        c._moduleImportAttributesTypes = new();
        c._getGlobalESSymbolType = c.GetGlobalTypeResolver("Symbol", 0 /*arity*/, false /*reportErrors*/);
        c._getGlobalBigIntType = c.GetGlobalTypeResolver("BigInt", 0 /*arity*/, false /*reportErrors*/);
        c._getGlobalImportMetaType = c.GetGlobalTypeResolver("ImportMeta", 0 /*arity*/, true /*reportErrors*/);
        c._getGlobalImportAttributesType = c.GetGlobalTypeResolver("ImportAttributes", 0 /*arity*/, false /*reportErrors*/);
        c._getGlobalImportAttributesTypeChecked = c.GetGlobalTypeResolver("ImportAttributes", 0 /*arity*/, true /*reportErrors*/);
        c._getGlobalNonNullableTypeAliasOrNil = c.GetGlobalTypeAliasResolver("NonNullable", 1 /*arity*/, false /*reportErrors*/);
        c._getGlobalExtractSymbol = c.GetGlobalTypeAliasResolver("Extract", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalDisposableType = c.GetGlobalTypeResolver("Disposable", 0 /*arity*/, true /*reportErrors*/);
        c._getGlobalAsyncDisposableType = c.GetGlobalTypeResolver("AsyncDisposable", 0 /*arity*/, true /*reportErrors*/);
        c._getGlobalAwaitedSymbol = c.GetGlobalTypeAliasResolver("Awaited", 1 /*arity*/, true /*reportErrors*/);
        c._getGlobalAwaitedSymbolOrNil = c.GetGlobalTypeAliasResolver("Awaited", 1 /*arity*/, false /*reportErrors*/);
        c._getGlobalNaNSymbolOrNil = c.GetGlobalValueSymbolResolver("NaN", false /*reportErrors*/);
        c._getGlobalRecordSymbol = c.GetGlobalTypeAliasResolver("Record", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalTemplateStringsArrayType = c.GetGlobalTypeResolver("TemplateStringsArray", 0 /*arity*/, true /*reportErrors*/);
        c._getGlobalESSymbolConstructorSymbolOrNil = c.GetGlobalValueSymbolResolver("Symbol", false /*reportErrors*/);
        c._getGlobalESSymbolConstructorTypeSymbolOrNil = c.GetGlobalTypeSymbolResolver("SymbolConstructor", false /*reportErrors*/);
        c._getGlobalImportCallOptionsType = c.GetGlobalTypeResolver("ImportCallOptions", 0 /*arity*/, false /*reportErrors*/);
        c._getGlobalImportCallOptionsTypeChecked = c.GetGlobalTypeResolver("ImportCallOptions", 0 /*arity*/, true /*reportErrors*/);
        c._getGlobalPromiseType = c.GetGlobalTypeResolver("Promise", 1 /*arity*/, false /*reportErrors*/);
        c._getGlobalPromiseTypeChecked = c.GetGlobalTypeResolver("Promise", 1 /*arity*/, true /*reportErrors*/);
        c._getGlobalPromiseLikeType = c.GetGlobalTypeResolver("PromiseLike", 1 /*arity*/, true /*reportErrors*/);
        c._getGlobalPromiseConstructorSymbol = c.GetGlobalValueSymbolResolver("Promise", true /*reportErrors*/);
        c._getGlobalPromiseConstructorSymbolOrNil = c.GetGlobalValueSymbolResolver("Promise", false /*reportErrors*/);
        c._getGlobalOmitSymbol = c.GetGlobalTypeAliasResolver("Omit", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalNoInferSymbolOrNil = c.GetGlobalTypeAliasResolver("NoInfer", 1 /*arity*/, false /*reportErrors*/);
        c._getGlobalIteratorType = c.GetGlobalTypeResolver("Iterator", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalIterableType = c.GetGlobalTypeResolver("Iterable", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalIterableTypeChecked = c.GetGlobalTypeResolver("Iterable", 3 /*arity*/, true /*reportErrors*/);
        c._getGlobalIterableIteratorType = c.GetGlobalTypeResolver("IterableIterator", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalIterableIteratorTypeChecked = c.GetGlobalTypeResolver("IterableIterator", 3 /*arity*/, true /*reportErrors*/);
        c._getGlobalIteratorObjectType = c.GetGlobalTypeResolver("IteratorObject", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalGeneratorType = c.GetGlobalTypeResolver("Generator", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalAsyncIteratorType = c.GetGlobalTypeResolver("AsyncIterator", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalAsyncIterableType = c.GetGlobalTypeResolver("AsyncIterable", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalAsyncIterableTypeChecked = c.GetGlobalTypeResolver("AsyncIterable", 3 /*arity*/, true /*reportErrors*/);
        c._getGlobalAsyncIterableIteratorType = c.GetGlobalTypeResolver("AsyncIterableIterator", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalAsyncIterableIteratorTypeChecked = c.GetGlobalTypeResolver("AsyncIterableIterator", 3 /*arity*/, true /*reportErrors*/);
        c._getGlobalAsyncIteratorObjectType = c.GetGlobalTypeResolver("AsyncIteratorObject", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalAsyncGeneratorType = c.GetGlobalTypeResolver("AsyncGenerator", 3 /*arity*/, false /*reportErrors*/);
        c._getGlobalIteratorYieldResultType = c.GetGlobalTypeResolver("IteratorYieldResult", 1 /*arity*/, false /*reportErrors*/);
        c._getGlobalIteratorReturnResultType = c.GetGlobalTypeResolver("IteratorReturnResult", 1 /*arity*/, false /*reportErrors*/);
        c._getGlobalTypedPropertyDescriptorType = c.GetGlobalTypeResolver("TypedPropertyDescriptor", 1 /*arity*/, true /*reportErrors*/);
        c._getGlobalClassDecoratorContextType = c.GetGlobalTypeResolver("ClassDecoratorContext", 1 /*arity*/, true /*reportErrors*/);
        c._getGlobalClassMethodDecoratorContextType = c.GetGlobalTypeResolver("ClassMethodDecoratorContext", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalClassGetterDecoratorContextType = c.GetGlobalTypeResolver("ClassGetterDecoratorContext", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalClassSetterDecoratorContextType = c.GetGlobalTypeResolver("ClassSetterDecoratorContext", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalClassAccessorDecoratorContextType = c.GetGlobalTypeResolver("ClassAccessorDecoratorContext", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalClassAccessorDecoratorTargetType = c.GetGlobalTypeResolver("ClassAccessorDecoratorTarget", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalClassAccessorDecoratorResultType = c.GetGlobalTypeResolver("ClassAccessorDecoratorResult", 2 /*arity*/, true /*reportErrors*/);
        c._getGlobalClassFieldDecoratorContextType = c.GetGlobalTypeResolver("ClassFieldDecoratorContext", 2 /*arity*/, true /*reportErrors*/);
        c.InitializeClosures();
        c.InitializeIterationResolvers();
        c.InitializeChecker();
        return (c, c._mu);
    }

    /// <summary>Go: checker.go:1129 <c>createFileIndexMap</c>（包级函数 → static 成员）。</summary>
    internal static Dictionary<SourceFile, int> CreateFileIndexMap(SourceFile[] files)
    {
        var result = new Dictionary<SourceFile, int>(files.Length);
        for (var i = 0; i < files.Length; i++)
        {
            result[files[i]] = i;
        }
        return result;
    }

    /// <summary>Go: checker.go:1137 <c>countGlobalSymbols</c>（包级函数 → static 成员）。</summary>
    internal static int CountGlobalSymbols(SourceFile[] files)
    {
        var count = 0;
        foreach (var file in files)
        {
            if (!IsExternalOrCommonJSModule(file))
            {
                count += file.Locals?.Count ?? 0;
            }
        }
        return count;
    }

    /// <summary>Go: checker.go:1147 <c>reportUnreliableWorker</c>。</summary>
    internal Type ReportUnreliableWorker(Type t)
    {
        if (ReferenceEquals(t, _markerSuperType) || ReferenceEquals(t, _markerSubType) || ReferenceEquals(t, _markerOtherType))
        {
            _reliabilityFlags |= RelationComparisonResult.ReportsUnreliable;
        }
        return t;
    }

    /// <summary>Go: checker.go:1154 <c>reportUnmeasurableWorker</c>。</summary>
    internal Type ReportUnmeasurableWorker(Type t)
    {
        if (ReferenceEquals(t, _markerSuperType) || ReferenceEquals(t, _markerSubType) || ReferenceEquals(t, _markerOtherType))
        {
            _reliabilityFlags |= RelationComparisonResult.ReportsUnmeasurable;
        }
        return t;
    }

    // Resolve to the global class or interface by the given name and arity, or emptyObjectType/emptyGenericType otherwise
    /// <summary>Go: checker.go:1162 <c>getGlobalTypeResolver</c>。</summary>
    internal Func<Type> GetGlobalTypeResolver(string name, int arity, bool reportErrors)
    {
        return Memoize(() => GetGlobalType(name, arity, reportErrors));
    }

    // Resolve to the global type alias symbol by the given name and arity, or nil otherwise
    /// <summary>Go: checker.go:1169 <c>getGlobalTypeAliasResolver</c>。</summary>
    internal Func<Symbol?> GetGlobalTypeAliasResolver(string name, int arity, bool reportErrors)
    {
        return Memoize(() => GetGlobalTypeAliasSymbol(name, arity, reportErrors));
    }

    // Resolve to the global value symbol by the given name, or nil otherwise
    /// <summary>Go: checker.go:1176 <c>getGlobalValueSymbolResolver</c>。</summary>
    internal Func<Symbol?> GetGlobalValueSymbolResolver(string name, bool reportErrors)
    {
        return Memoize(() => GetGlobalSymbolWorker(name, SymbolFlags.Value, IfElse(reportErrors, Diagnostics.Cannot_find_global_value_0, (Message?)null)));
    }

    /// <summary>Go: checker.go:1182 <c>getGlobalTypeSymbolResolver</c>。</summary>
    internal Func<Symbol?> GetGlobalTypeSymbolResolver(string name, bool reportErrors)
    {
        return Memoize(() => GetGlobalSymbolWorker(name, SymbolFlags.Type, IfElse(reportErrors, Diagnostics.Cannot_find_global_type_0, (Message?)null)));
    }

    /// <summary>Go: checker.go:1188 <c>getGlobalTypesResolver</c>。</summary>
    internal Func<Type[]> GetGlobalTypesResolver(string[] names, int arity, bool reportErrors)
    {
        return Memoize(() => Map(names, name => GetGlobalType(name, arity, reportErrors)));
    }

    /// <summary>Go: checker.go:1196 <c>getGlobalTypeAliasSymbol</c>。</summary>
    internal Symbol? GetGlobalTypeAliasSymbol(string name, int arity, bool reportErrors)
    {
        var symbol = GetGlobalSymbolWorker(name, SymbolFlags.TypeAlias, IfElse(reportErrors, Diagnostics.Cannot_find_global_type_0, (Message?)null));
        if (symbol is null)
        {
            return null;
        }
        // Resolve the declared type of the symbol. This resolves type parameters for the type alias so that we can check arity.
        GetDeclaredTypeOfSymbolWorker(symbol);
        if ((_typeAliasLinks.Get(symbol)._typeParameters?.Length ?? 0) != arity)
        {
            if (reportErrors)
            {
                var decl = Find(symbol.Declarations, IsTypeAliasDeclaration);
                Error(decl, Diagnostics.Global_type_0_must_have_1_type_parameter_s, SymbolName(symbol), arity);
            }
            return null;
        }
        return symbol;
    }

    /// <summary>Go: checker.go:1213 <c>GetTypeAliasTypeParameters</c>（Go 导出 → public）。</summary>
    public Type[] GetTypeAliasTypeParameters(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.TypeAlias) == 0)
        {
            Debug.Fail("Attempted to fetch type alias parameters for non-type-alias symbol");
        }
        GetDeclaredTypeOfSymbolWorker(symbol);
        return _typeAliasLinks.Get(symbol)._typeParameters ?? [];
    }

    /// <summary>Go: checker.go:1221 <c>getGlobalType</c>。</summary>
    internal Type GetGlobalType(string name, int arity, bool reportErrors)
    {
        var symbol = GetGlobalSymbolWorker(name, SymbolFlags.Type, IfElse(reportErrors, Diagnostics.Cannot_find_global_type_0, (Message?)null));
        if (symbol is not null)
        {
            if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
            {
                var t = GetDeclaredTypeOfSymbolWorker(symbol);
                if (t.AsInterfaceType()!.TypeParameters().Length == arity)
                {
                    return t;
                }
                if (reportErrors)
                {
                    Error(GetGlobalTypeDeclaration(symbol), Diagnostics.Global_type_0_must_have_1_type_parameter_s, SymbolName(symbol), arity);
                }
            }
            else if (reportErrors)
            {
                Error(GetGlobalTypeDeclaration(symbol), Diagnostics.Global_type_0_must_be_a_class_or_interface_type, SymbolName(symbol));
            }
        }
        if (arity != 0)
        {
            return _emptyGenericType;
        }
        return _emptyObjectType;
    }

    /// <summary>Go: checker.go:1242 <c>getGlobalTypeDeclaration</c>（包级函数 → static 成员）。</summary>
    internal static Node? GetGlobalTypeDeclaration(Symbol symbol)
    {
        foreach (var declaration in symbol.Declarations ?? [])
        {
            switch (declaration.Kind)
            {
                case Kind.ClassDeclaration:
                case Kind.InterfaceDeclaration:
                case Kind.EnumDeclaration:
                case Kind.TypeAliasDeclaration:
                    return declaration;
            }
        }
        return null;
    }

    /// <summary>Go: checker.go:1252 <c>getGlobalSymbol</c>（与 exports.go 的 GetGlobalSymbol 同名 → Worker 后缀）。</summary>
    internal Symbol? GetGlobalSymbolWorker(string name, SymbolFlags meaning, Message? diagnostic)
    {
        // Don't track references for global symbols anyway, so value if `isReference` is arbitrary
        return _resolveName(null, name, meaning, diagnostic, false /*isUse*/, false /*excludeGlobals*/);
    }

    /// <summary>Go: checker.go:1257 <c>initializeClosures</c>。</summary>
    internal void InitializeClosures()
    {
        _isPrimitiveOrObjectOrEmptyType = t => (t._flags & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 || IsEmptyAnonymousObjectType(t);
        _containsMissingType = t => ReferenceEquals(t, _missingType) || (t._flags & TypeFlags.Union) != 0 && t.Types()[0] == _missingType;
        _couldContainTypeVariables = CouldContainTypeVariablesWorker;
        _isStringIndexSignatureOnlyType = IsStringIndexSignatureOnlyTypeWorker;
        _markNodeAssignments = MarkNodeAssignmentsWorker;
        _compareTypesAssignable = CompareTypesAssignableWorker;
    }

    /// <summary>Go: checker.go:1270 <c>initializeIterationResolvers</c>。</summary>
    internal void InitializeIterationResolvers()
    {
        _syncIterationTypesResolver = new IterationTypesResolver
        {
            _iteratorSymbolName = "iterator",
            _getGlobalIteratorType = _getGlobalIteratorType,
            _getGlobalIterableType = _getGlobalIterableType,
            _getGlobalIterableTypeChecked = _getGlobalIterableTypeChecked,
            _getGlobalIterableIteratorType = _getGlobalIterableIteratorType,
            _getGlobalIterableIteratorTypeChecked = _getGlobalIterableIteratorTypeChecked,
            _getGlobalIteratorObjectType = _getGlobalIteratorObjectType,
            _getGlobalGeneratorType = _getGlobalGeneratorType,
            _getGlobalBuiltinIteratorTypes = GetGlobalTypesResolver(["ArrayIterator", "MapIterator", "SetIterator", "StringIterator"], 1, false /*reportErrors*/),
            _resolveIterationType = (t, errorNode) => t,
            _mustHaveANextMethodDiagnostic = Diagnostics.An_iterator_must_have_a_next_method,
            _mustBeAMethodDiagnostic = Diagnostics.The_0_property_of_an_iterator_must_be_a_method,
            _mustHaveAValueDiagnostic = Diagnostics.The_type_returned_by_the_0_method_of_an_iterator_must_have_a_value_property,
        };
        _asyncIterationTypesResolver = new IterationTypesResolver
        {
            _iteratorSymbolName = "asyncIterator",
            _getGlobalIteratorType = _getGlobalAsyncIteratorType,
            _getGlobalIterableType = _getGlobalAsyncIterableType,
            _getGlobalIterableTypeChecked = _getGlobalAsyncIterableTypeChecked,
            _getGlobalIterableIteratorType = _getGlobalAsyncIterableIteratorType,
            _getGlobalIterableIteratorTypeChecked = _getGlobalAsyncIterableIteratorTypeChecked,
            _getGlobalIteratorObjectType = _getGlobalAsyncIteratorObjectType,
            _getGlobalGeneratorType = _getGlobalAsyncGeneratorType,
            _getGlobalBuiltinIteratorTypes = GetGlobalTypesResolver(["ReadableStreamAsyncIterator"], 1, false /*reportErrors*/),
            _resolveIterationType = (t, errorNode) => GetAwaitedTypeEx(t, errorNode, Diagnostics.Type_of_await_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member),
            _mustHaveANextMethodDiagnostic = Diagnostics.An_async_iterator_must_have_a_next_method,
            _mustBeAMethodDiagnostic = Diagnostics.The_0_property_of_an_async_iterator_must_be_a_method,
            _mustHaveAValueDiagnostic = Diagnostics.The_type_returned_by_the_0_method_of_an_async_iterator_must_be_a_promise_for_a_type_with_a_value_property,
        };
    }

    /// <summary>Go: checker.go:1073 的 <c>slices.Sorted(maps.Keys(typeofNEFacts))</c>：按序数比较排好键（flow.go 的包级 var）。</summary>
    internal static string[] TypeofNEKeys()
    {
        var keys = new string[_typeofNEFacts.Count];
        _typeofNEFacts.Keys.CopyTo(keys, 0);
        Array.Sort(keys, StringComparer.Ordinal);
        return keys;
    }
}
