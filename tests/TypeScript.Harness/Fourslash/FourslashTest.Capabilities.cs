// ported from: internal/fourslash/fourslash.go:442-738
//
// 偏离清单（相对 Go 原文逐条）：
// 1. `ptrTrue`/`ptrFalse` 是 Go 的包级 `*bool`（共享指针）。C# 的 `bool?` 是值类型，无法共享指针，
//    故按值语义直接把每处用法落成 `true`/`false` 字面量（不再声明对应的静态字段）。
//    实际可观察行为不变：Go 里也从没有对 `*ptrTrue` 指向的 bool 写入过。
// 2. 包级 `var defaultXxxCapabilities = &lsproto.XxxClientCapabilities{...}` → `private static readonly` 字段
//    （Go 包级变量在 C# 的惯例落点，见 Checker/Flow.Keys.cs 的同类注释）。
//    引用共享语义**保留**：`GetCapabilitiesWithDefaults` 会把这同一个实例赋给多个 capabilities 对象，
//    与 Go 的包级指针一致。
// 3. `getCapabilitiesWithDefaults`（Go 包级未导出函数）→ `internal static` 方法，宿主在 `FourslashTest` partial 上。
//    同文件里 `GetDefaultCapabilities`/`GetDefaultCapabilitiesWithOptions` 是 Go 的**导出**函数 → `public static`。
// 4. `type ClientCapabilitiesOptions` 与 `GetDefaultCapabilities`/`GetDefaultCapabilitiesWithOptions` 同文件定义
//    （Go 里它们都在 fourslash.go）。
// 5. Go `capabilitiesWithDefaults = *capabilities` 是结构体浅拷贝 → C# 逐字段复制 `ClientCapabilities` 的全部
//    10 个字段，保持浅拷贝（嵌套对象仍是同一引用）语义。
// 6. `RangeLimit: new(uint32(5000))` → `RangeLimit = 5000`（`uint?` 的常量隐式转换）。
// 7. Go 原文 `defaultFoldingRangeCapabilities` 里 `// LineFoldingOnly: ptrTrue` 是注释掉的 → 同样不设置该字段。
// 8. Go 的 `defaultSemanticTokenTypes()`/`defaultSemanticTokenModifiers()` 由 FsCore 切片提供
//    （`DefaultSemanticTokenTypes`/`DefaultSemanticTokenModifiers`），本文件直接调用。
// 9. C# 集合字面量 `[...]` 对应 Go 的 `&[]T{...}`；`&[]T{...}` 上的"共享切片"语义无观察者，按值处理。

using TypeScript.LsProto;

namespace TypeScript.Harness.Fourslash;

public sealed class ClientCapabilitiesOptions
{
    public ClientCompletionItemOptions? CompletionItem;
}

public sealed partial class FourslashTest
{
    private static readonly CompletionClientCapabilities _defaultCompletionCapabilities = new()
    {
        CompletionItem = new ClientCompletionItemOptions
        {
            SnippetSupport = false,
            CommitCharactersSupport = true,
            PreselectSupport = true,
            LabelDetailsSupport = false,
            InsertReplaceSupport = true,
            DocumentationFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
        },
        CompletionList = new CompletionListCapabilities
        {
            ItemDefaults = ["commitCharacters", "editRange"],
        },
    };

    private static readonly DefinitionClientCapabilities _defaultDefinitionCapabilities = new()
    {
        LinkSupport = true,
    };

    private static readonly TypeDefinitionClientCapabilities _defaultTypeDefinitionCapabilities = new()
    {
        LinkSupport = true,
    };

    private static readonly ImplementationClientCapabilities _defaultImplementationCapabilities = new()
    {
        LinkSupport = true,
    };

    private static readonly HoverClientCapabilities _defaultHoverCapabilities = new()
    {
        ContentFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
    };

    private static readonly ExperimentalClientCapabilities _defaultExperimentalCapabilities = new()
    {
        HoverVerbosityLevel = true,
    };

    private static readonly SignatureHelpClientCapabilities _defaultSignatureHelpCapabilities = new()
    {
        SignatureInformation = new ClientSignatureInformationOptions
        {
            DocumentationFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
            ParameterInformation = new ClientSignatureParameterInformationOptions
            {
                LabelOffsetSupport = true,
            },
            ActiveParameterSupport = true,
        },
        ContextSupport = true,
    };

    private static readonly DocumentSymbolClientCapabilities _defaultDocumentSymbolCapabilities = new()
    {
        HierarchicalDocumentSymbolSupport = true,
    };

    private static readonly FoldingRangeClientCapabilities _defaultFoldingRangeCapabilities = new()
    {
        RangeLimit = 5000,
        // LineFoldingOnly: true,
        FoldingRangeKind = new ClientFoldingRangeKindOptions
        {
            ValueSet =
            [
                FoldingRangeKind.Comment,
                FoldingRangeKind.Imports,
                FoldingRangeKind.Region,
            ],
        },
        FoldingRange = new ClientFoldingRangeOptions
        {
            CollapsedText = true, // Unused by our testing, but set to exercise the code.
        },
    };

    private static readonly DiagnosticClientCapabilities _defaultDiagnosticCapabilities = new()
    {
        RelatedInformation = true,
        TagSupport = new ClientDiagnosticsTagOptions
        {
            ValueSet =
            [
                DiagnosticTag.Unnecessary,
                DiagnosticTag.Deprecated,
            ],
        },
    };

    private static readonly PublishDiagnosticsClientCapabilities _defaultPublishDiagnosticCapabilities = new()
    {
        RelatedInformation = true,
        TagSupport = new ClientDiagnosticsTagOptions
        {
            ValueSet =
            [
                DiagnosticTag.Unnecessary,
                DiagnosticTag.Deprecated,
            ],
        },
    };

    private static readonly WorkspaceEditClientCapabilities _defaultWorkspaceEditCapabilities = new()
    {
        DocumentChanges = true,
        ResourceOperations =
        [
            ResourceOperationKind.Rename,
        ],
    };

    public static ClientCapabilities GetDefaultCapabilities()
    {
        return new ClientCapabilities
        {
            General = new GeneralClientCapabilities
            {
                PositionEncodings = [PositionEncodingKind.UTF8],
            },
            Experimental = new ExperimentalClientCapabilities
            {
                HoverVerbosityLevel = true,
            },
            TextDocument = new TextDocumentClientCapabilities
            {
                Completion = new CompletionClientCapabilities
                {
                    CompletionItem = new ClientCompletionItemOptions
                    {
                        SnippetSupport = false,
                        CommitCharactersSupport = true,
                        PreselectSupport = true,
                        LabelDetailsSupport = false,
                        InsertReplaceSupport = true,
                        DocumentationFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
                    },
                    CompletionList = new CompletionListCapabilities
                    {
                        ItemDefaults = ["commitCharacters", "editRange"],
                    },
                },
                Diagnostic = new DiagnosticClientCapabilities
                {
                    RelatedInformation = true,
                    TagSupport = new ClientDiagnosticsTagOptions
                    {
                        ValueSet =
                        [
                            DiagnosticTag.Unnecessary,
                            DiagnosticTag.Deprecated,
                        ],
                    },
                },
                PublishDiagnostics = new PublishDiagnosticsClientCapabilities
                {
                    RelatedInformation = true,
                    TagSupport = new ClientDiagnosticsTagOptions
                    {
                        ValueSet =
                        [
                            DiagnosticTag.Unnecessary,
                            DiagnosticTag.Deprecated,
                        ],
                    },
                },
                Definition = new DefinitionClientCapabilities
                {
                    LinkSupport = true,
                },
                TypeDefinition = new TypeDefinitionClientCapabilities
                {
                    LinkSupport = true,
                },
                Implementation = new ImplementationClientCapabilities
                {
                    LinkSupport = true,
                },
                Hover = new HoverClientCapabilities
                {
                    ContentFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
                },
                SignatureHelp = new SignatureHelpClientCapabilities
                {
                    SignatureInformation = new ClientSignatureInformationOptions
                    {
                        DocumentationFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
                        ParameterInformation = new ClientSignatureParameterInformationOptions
                        {
                            LabelOffsetSupport = true,
                        },
                        ActiveParameterSupport = true,
                    },
                    ContextSupport = true,
                },
                DocumentSymbol = new DocumentSymbolClientCapabilities
                {
                    HierarchicalDocumentSymbolSupport = true,
                },
                FoldingRange = new FoldingRangeClientCapabilities
                {
                    RangeLimit = 5000,
                    FoldingRangeKind = new ClientFoldingRangeKindOptions
                    {
                        ValueSet =
                        [
                            FoldingRangeKind.Comment,
                            FoldingRangeKind.Imports,
                            FoldingRangeKind.Region,
                        ],
                    },
                    FoldingRange = new ClientFoldingRangeOptions
                    {
                        CollapsedText = true,
                    },
                },
            },
            Workspace = new WorkspaceClientCapabilities
            {
                Configuration = true,
                FileOperations = new FileOperationClientCapabilities
                {
                    WillRename = true,
                },
                WorkspaceEdit = new WorkspaceEditClientCapabilities
                {
                    DocumentChanges = true,
                    ResourceOperations =
                    [
                        ResourceOperationKind.Rename,
                    ],
                },
            },
        };
    }

    public static ClientCapabilities GetDefaultCapabilitiesWithOptions(ClientCapabilitiesOptions? options)
    {
        var capabilities = GetDefaultCapabilities();
        if (options is null)
        {
            return capabilities;
        }
        if (options.CompletionItem is not null)
        {
            var target = capabilities.TextDocument!.Completion!.CompletionItem!;
            var completionItemOptions = options.CompletionItem;
            if (completionItemOptions.SnippetSupport is not null)
            {
                target.SnippetSupport = completionItemOptions.SnippetSupport;
            }
            if (completionItemOptions.CommitCharactersSupport is not null)
            {
                target.CommitCharactersSupport = completionItemOptions.CommitCharactersSupport;
            }
            if (completionItemOptions.DocumentationFormat is not null)
            {
                target.DocumentationFormat = completionItemOptions.DocumentationFormat;
            }
            if (completionItemOptions.DeprecatedSupport is not null)
            {
                target.DeprecatedSupport = completionItemOptions.DeprecatedSupport;
            }
            if (completionItemOptions.PreselectSupport is not null)
            {
                target.PreselectSupport = completionItemOptions.PreselectSupport;
            }
            if (completionItemOptions.TagSupport is not null)
            {
                target.TagSupport = completionItemOptions.TagSupport;
            }
            if (completionItemOptions.InsertReplaceSupport is not null)
            {
                target.InsertReplaceSupport = completionItemOptions.InsertReplaceSupport;
            }
            if (completionItemOptions.ResolveSupport is not null)
            {
                target.ResolveSupport = completionItemOptions.ResolveSupport;
            }
            if (completionItemOptions.InsertTextModeSupport is not null)
            {
                target.InsertTextModeSupport = completionItemOptions.InsertTextModeSupport;
            }
            if (completionItemOptions.LabelDetailsSupport is not null)
            {
                target.LabelDetailsSupport = completionItemOptions.LabelDetailsSupport;
            }
        }
        return capabilities;
    }

    internal static ClientCapabilities GetCapabilitiesWithDefaults(ClientCapabilities? capabilities)
    {
        var capabilitiesWithDefaults = new ClientCapabilities();
        if (capabilities is not null)
        {
            // Go: capabilitiesWithDefaults = *capabilities（结构体浅拷贝）。
            capabilitiesWithDefaults.Workspace = capabilities.Workspace;
            capabilitiesWithDefaults.TextDocument = capabilities.TextDocument;
            capabilitiesWithDefaults.Window = capabilities.Window;
            capabilitiesWithDefaults.General = capabilities.General;
            capabilitiesWithDefaults.Experimental = capabilities.Experimental;
            capabilitiesWithDefaults.VSSupportsVisualStudioExtensions = capabilities.VSSupportsVisualStudioExtensions;
            capabilitiesWithDefaults.VSSupportedSnippetVersion = capabilities.VSSupportedSnippetVersion;
            capabilitiesWithDefaults.VSSupportsNotIncludingTextInTextDocumentDidOpen = capabilities.VSSupportsNotIncludingTextInTextDocumentDidOpen;
            capabilitiesWithDefaults.VSSupportsIconExtensions = capabilities.VSSupportsIconExtensions;
            capabilitiesWithDefaults.VSSupportsDiagnosticRequests = capabilities.VSSupportsDiagnosticRequests;
        }
        capabilitiesWithDefaults.General = new GeneralClientCapabilities
        {
            PositionEncodings = [PositionEncodingKind.UTF8],
        };
        if (capabilitiesWithDefaults.Experimental is null)
        {
            capabilitiesWithDefaults.Experimental = _defaultExperimentalCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument is null)
        {
            capabilitiesWithDefaults.TextDocument = new TextDocumentClientCapabilities();
        }
        if (capabilitiesWithDefaults.TextDocument.Completion is null)
        {
            capabilitiesWithDefaults.TextDocument.Completion = _defaultCompletionCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.Diagnostic is null)
        {
            capabilitiesWithDefaults.TextDocument.Diagnostic = _defaultDiagnosticCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.PublishDiagnostics is null)
        {
            capabilitiesWithDefaults.TextDocument.PublishDiagnostics = _defaultPublishDiagnosticCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.SemanticTokens is null)
        {
            capabilitiesWithDefaults.TextDocument.SemanticTokens = new SemanticTokensClientCapabilities
            {
                Requests = new ClientSemanticTokensRequestOptions
                {
                    Full = new BooleanOrClientSemanticTokensRequestFullDelta
                    {
                        Boolean = true,
                    },
                },
                TokenTypes = DefaultSemanticTokenTypes(),
                TokenModifiers = DefaultSemanticTokenModifiers(),
                Formats = [TokenFormat.Relative],
            };
        }
        if (capabilitiesWithDefaults.Workspace is null)
        {
            capabilitiesWithDefaults.Workspace = new WorkspaceClientCapabilities();
        }
        if (capabilitiesWithDefaults.Workspace.FileOperations is null)
        {
            capabilitiesWithDefaults.Workspace.FileOperations = new FileOperationClientCapabilities
            {
                WillRename = true,
            };
        }
        if (capabilitiesWithDefaults.Workspace.WorkspaceEdit is null)
        {
            capabilitiesWithDefaults.Workspace.WorkspaceEdit = _defaultWorkspaceEditCapabilities;
        }
        if (capabilitiesWithDefaults.Workspace.Configuration is null)
        {
            capabilitiesWithDefaults.Workspace.Configuration = true;
        }
        if (capabilitiesWithDefaults.TextDocument.Definition is null)
        {
            capabilitiesWithDefaults.TextDocument.Definition = _defaultDefinitionCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.TypeDefinition is null)
        {
            capabilitiesWithDefaults.TextDocument.TypeDefinition = _defaultTypeDefinitionCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.Implementation is null)
        {
            capabilitiesWithDefaults.TextDocument.Implementation = _defaultImplementationCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.Hover is null)
        {
            capabilitiesWithDefaults.TextDocument.Hover = _defaultHoverCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.SignatureHelp is null)
        {
            capabilitiesWithDefaults.TextDocument.SignatureHelp = _defaultSignatureHelpCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.DocumentSymbol is null)
        {
            capabilitiesWithDefaults.TextDocument.DocumentSymbol = _defaultDocumentSymbolCapabilities;
        }
        if (capabilitiesWithDefaults.TextDocument.FoldingRange is null)
        {
            capabilitiesWithDefaults.TextDocument.FoldingRange = _defaultFoldingRangeCapabilities;
        }
        return capabilitiesWithDefaults;
    }
}
