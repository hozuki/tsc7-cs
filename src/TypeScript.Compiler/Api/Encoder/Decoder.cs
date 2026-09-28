// ported from: internal/api/encoder/decoder.go:1-393
//
// 与 Go 的偏离（逐条说明）：
// 1. 错误处理：Go 的 `(T, error)` / `errors.New` / `fmt.Errorf` 在 C# 一律走异常。DecodeSourceFile / DecodeNodes 不再有 error 返回值，
//    失败路径抛 `DebugFailureException`（对应 Go 的 panic 语义；Go 侧 session.go 会在请求边界把 err 翻成 ErrClientError）。
// 2. 常量来源：NodeOffset*、NodeSize、NodeDataType*、NodeDataTypeMask/NodeDataChildMask/NodeDataStringIndexMask、SyntaxKindNodeList、
//    HeaderOffset*、HeaderSize、ProtocolVersion 在 Go 里是与本文件同 package 的包级常量；C# 拆文件后它们由**同一命名空间**的
//    `Encoder` 静态类（Encoder.cs，编码侧切片）提供，本文件只引用不重复定义（否则 CS0101/CS0111）。
//    又：Encoder.cs 把这些（Go 里是无类型 int 常量的）偏移量统一声明为 `const uint`，因此凡是参与 int 运算/数组下标/传 int 形参的
//    地方一律显式 `(int)` 转换（否则会退化成 long 运算而编译失败）。
// 3. 类型可见性：Go 的 `type astDecoder struct` 是包私有；C# 顶层类型不能是 private，故为 `internal sealed partial class AstDecoder`
//    （`partial` 是必需的：decoder_generated.go 的方法在 Decoder.Generated.cs 里继续写同一个类），字段取 internal
//    （Go 的未导出字段 → C# 习惯的 `_camelCase`，先例 Ast/NodeFactory.cs 的 `_hooks`）。
// 4. `[]byte` → `byte[]`。Go 的 `childBuf []int`（复用切片视图）→ `List<int>`：C# 数组长度固定、无法表达"同一后备数组的复用视图"，
//    List 的 Clear+Add 与 Go 的 `childBuf[:0]`+append 语义一致；与 Go 一样，返回的是**同一个实例**，调用方不得长期保留。
// 5. Go 的 `allStringData string`（把整段 string data 转成一个 Go string，再用 `[start:end]` 做零分配**字节区间**子串）：
//    C# 的 string 是 UTF-16，无法按字节区间切片，故 GetString 解码对应字节区间（每次一次分配，没有 Go 的零分配优势）。
//    解码实现见 Decoder.DecodeUtf8：合法 UTF-8 走严格 UTF-8 快速路径（与 Go 逐码位一致）；遇到非法序列时按 WTF-8 解码 ——
//    编码侧为与 Go 字节一致，把孤立代理项写成 CESU-8 形式的 3 字节序列，这里还原为未配对的 UTF-16 代理码元，
//    从而与 Go 的字节串一一对应（再次编码即得原字节）。只有「既非合法 UTF-8 又非 WTF-8 孤立代理项」的字节才会退化成 U+FFFD，
//    而 Go 会原样保留这些字节 —— UTF-16 string 无法表示任意字节，这是不可消除的差异。
// 6. Go 的 nodeArena 预分配 + `allocNodeSlice` 返回 len=0/cap=N 的切片视图（避免每个 NodeList 一次分配）：C# 数组没有这种形状，
//    也不能共享后备存储，故 AllocNodeSlice 直接返回长度 = capacity 的 `Node[]`，`nodeArena` 字段随之取消。
//    另注：Go 里 KindSyntaxList/KindJSDocTypeLiteral 分支的 `nodes[i] = d.nodes[ci]`（见 Decoder.Generated.cs）对 len=0 的切片
//    会 panic；C# 按该分支的明显意图实现为按下标写入。NodeList 分支仍按 Go 的 append 语义只收非 nil 子节点，再截断到实际长度。
// 7. Go 的 `panic("SyntheticExpression should never be decoded")` → `throw new DebugFailureException(...)`。
// 8. 消息文本：Go 的 `%q`/`%v`/`%w` 换成 C# 字符串插值，转义与指针格式不逐一模拟（这些消息只用于诊断）。
// 9. `tspath.GetEncodedRootLength` / `tspath.NormalizePath` → `using static TypeScript.TSPath.TSPath;` 下的同名静态方法（静态类名 = TSPath）。
// 10. Go 的 `fmt.Errorf("at node %d (kind %v): %w", …)` 错误包装：C# 在 Decode 里捕获 DebugFailureException 后重抛并加同样前缀。
// 11. Go 用结构体字面量 + 事后赋值构造 astDecoder；C# 用构造函数（字段非空初始化，避免 CS8618）。
// 12. `readLE32` 在 Go 里是包级函数但只被 decoder.go 使用（encoder.go/encoder_generated.go 均未用），C# 保留在 `Decoder` 静态类中，
//    AstDecoder 内部以 `Decoder.ReadLE32(...)` 显式限定调用。
using System.Buffers.Binary;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
// `Ast` 在本文件里会解析到命名空间 TypeScript.Ast（外层命名空间成员优先于编译单元 using），
// 所以静态类 Ast 必须走别名 —— 与同包 Session.Emit.cs 的写法一致。
using AstApi = TypeScript.Ast.Ast;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Api.Encoder;

/// <summary>Go: <c>type astDecoder struct</c>（decoder.go:14-32）。从二进制编码数据重建真实 <see cref="Node"/> 对象。</summary>
internal sealed partial class AstDecoder
{
    /// <summary>Go: <c>raw []byte</c>——完整的编码数据。</summary>
    internal byte[] _raw;

    /// <summary>Go: <c>strTable uint32</c>——string offsets 区起始偏移。</summary>
    internal uint _strTable;

    /// <summary>Go: <c>strData uint32</c>——string data 区起始偏移。</summary>
    internal uint _strData;

    /// <summary>Go: <c>extData uint32</c>——extended node data 区起始偏移。</summary>
    internal uint _extData;

    /// <summary>Go: <c>nodeOff uint32</c>——nodes 区起始偏移。</summary>
    internal uint _nodeOff;

    /// <summary>Go: <c>nodeCount int</c>——节点个数。</summary>
    internal int _nodeCount;

    /// <summary>Go: <c>factory *ast.NodeFactory</c>。</summary>
    internal NodeFactory _factory;

    /// <summary>Go: <c>childBuf []int</c>——复用的子节点下标缓冲；调用方不得保留（见偏离 4）。</summary>
    internal readonly List<int> _childBuf = [];

    /// <summary>Go: <c>nodes []*ast.Node</c>——结果：每个节点下标对应的已解码节点。</summary>
    internal Node?[] _nodes = [];

    /// <summary>Go: <c>nodeLists []*ast.NodeList</c>——结果：每个节点下标对应的 NodeList（仅 NodeList 节点）。</summary>
    internal NodeList?[] _nodeLists = [];

    /// <summary>Go: <c>&amp;astDecoder{raw: …, factory: …}</c>（decoder.go:76-83）的字面量构造（见偏离 11）。</summary>
    internal AstDecoder(byte[] raw, uint strTable, uint strData, uint extData, uint nodeOff, NodeFactory factory)
    {
        _raw = raw;
        _strTable = strTable;
        _strData = strData;
        _extData = extData;
        _nodeOff = nodeOff;
        _factory = factory;
    }

    /// <summary>Go: <c>func (d *astDecoder) allocNodeSlice(capacity int) []*ast.Node</c>（decoder.go:100-105）。</summary>
    internal static Node[] AllocNodeSlice(int capacity) => new Node[capacity];

    /// <summary>Go: <c>func (d *astDecoder) nodeField(i int, field int) uint32</c>（decoder.go:107-109）。</summary>
    /// 偏离：`field` 取 <c>uint</c>（Encoder.cs 把 NodeOffset* 声明为 <c>const uint</c>，Go 那边是无类型 int 常量），内部再转 int。
    internal uint NodeField(int i, uint field) => Decoder.ReadLE32(_raw, (int)_nodeOff + i * (int)Encoder.NodeSize + (int)field);

    /// <summary>Go: <c>func (d *astDecoder) getString(idx uint32) string</c>（decoder.go:111-116）。</summary>
    /// Go 是 `d.allStringData[start:end]`——对一整块字符串数据做**字节区间**子串（零拷贝、按字节保真）。
    /// C# 的 string 是 UTF-16，无法按字节切片，所以这里解码对应字节区间（见 <see cref="Decoder.DecodeUtf8"/>）。
    internal string GetString(uint idx)
    {
        var offBase = (int)_strTable + (int)idx * 4;
        var start = Decoder.ReadLE32(_raw, offBase);
        var end = Decoder.ReadLE32(_raw, offBase + 4);
        return Decoder.DecodeUtf8(_raw, (int)(_strData + start), (int)(end - start));
    }

    /// <summary>Go: <c>func (d *astDecoder) collectChildren(i int) []int</c>（decoder.go:120-135）。</summary>
    /// 返回的是复用的 <see cref="_childBuf"/> 本身（与 Go 复用切片一致），调用方不得保留。
    internal List<int> CollectChildren(int i)
    {
        _childBuf.Clear();
        if (i + 1 >= _nodeCount)
        {
            return _childBuf;
        }

        var firstChild = i + 1;
        if (NodeField(firstChild, Encoder.NodeOffsetParent) != (uint)i)
        {
            return _childBuf;
        }

        _childBuf.Add(firstChild);
        var next = (int)NodeField(firstChild, Encoder.NodeOffsetNext);
        while (next != 0)
        {
            _childBuf.Add(next);
            next = (int)NodeField(next, Encoder.NodeOffsetNext);
        }

        return _childBuf;
    }

    /// <summary>Go: <c>func (d *astDecoder) decode() (*ast.Node, error)</c>（decoder.go:138-184）。</summary>
    internal Node Decode()
    {
        if (_nodeCount < 2)
        {
            throw new DebugFailureException("no nodes to decode");
        }

        _nodes = new Node?[_nodeCount];
        _nodeLists = new NodeList?[_nodeCount];
        // Go 在这里还预分配 nodeArena；C# 没有该形状（见文件头偏离 6）。

        // Process bottom-up so children exist before parents.
        for (var i = _nodeCount - 1; i >= 1; i--)
        {
            var kind = NodeField(i, Encoder.NodeOffsetKind);
            var pos = NodeField(i, Encoder.NodeOffsetPos);
            var end = NodeField(i, Encoder.NodeOffsetEnd);
            var data = NodeField(i, Encoder.NodeOffsetData);
            var childIndices = CollectChildren(i);

            if (kind == Encoder.SyntaxKindNodeList)
            {
                // Go: childNodes := d.allocNodeSlice(len(childIndices)); append 非 nil 子节点
                var childNodes = AllocNodeSlice(childIndices.Count);
                var childCount = 0;
                foreach (var ci in childIndices)
                {
                    var child = _nodes[ci];
                    if (child is not null)
                    {
                        childNodes[childCount++] = child;
                    }
                }

                if (childCount != childNodes.Length)
                {
                    Array.Resize(ref childNodes, childCount);
                }

                var nl = _factory.NewNodeList(childNodes);
                nl.Loc = NewTextRange((int)pos, (int)end);
                _nodeLists[i] = nl;
                continue;
            }

            Node node;
            try
            {
                node = CreateNode((Kind)kind, data, childIndices);
            }
            catch (DebugFailureException ex)
            {
                throw new DebugFailureException($"at node {i} (kind {(Kind)kind}): {ex.Message}");
            }

            node.Loc = NewTextRange((int)pos, (int)end);
            node.Flags = (NodeFlags)NodeField(i, Encoder.NodeOffsetFlags);
            if (kind == (uint)Kind.SourceFile)
            {
                node.AsSourceFile()!.IsDeclarationFile = (node.Flags & NodeFlags.Ambient) != 0;
            }

            _nodes[i] = node;
        }

        return _nodes[1]!;
    }

    /// <summary>Go: <c>func (d *astDecoder) getModifierList(ci int) *ast.ModifierList</c>（decoder.go:186-195）。</summary>
    internal ModifierList? GetModifierList(int ci)
    {
        var nl = _nodeLists[ci];
        if (nl is null)
        {
            return null;
        }

        var ml = _factory.NewModifierList(nl.Nodes);
        ml.Loc = nl.Loc;
        return ml;
    }

    /// <summary>Go: <c>func (d *astDecoder) nodeAt(ci int) *ast.Node</c>（decoder.go:224-229）。</summary>
    internal Node? NodeAt(int ci) => ci == 0 ? null : _nodes[ci];

    /// <summary>Go: <c>func (d *astDecoder) nodeListAt(ci int) *ast.NodeList</c>（decoder.go:231-236）。</summary>
    internal NodeList? NodeListAt(int ci) => ci == 0 ? null : _nodeLists[ci];

    /// <summary>Go: <c>func (d *astDecoder) modifierListAt(ci int) *ast.ModifierList</c>（decoder.go:238-243）。</summary>
    internal ModifierList? ModifierListAt(int ci) => ci == 0 ? null : GetModifierList(ci);

    /// <summary>Go: <c>func (d *astDecoder) createNode(kind ast.Kind, data uint32, childIndices []int) (*ast.Node, error)</c>（decoder.go:245-256）。</summary>
    internal Node CreateNode(Kind kind, uint data, List<int> childIndices)
    {
        var dataType = data & Encoder.NodeDataTypeMask;
        var commonData = (byte)((data >> 24) & 0x3f);

        switch (dataType)
        {
            case Encoder.NodeDataTypeString:
                return CreateStringNode(kind, data, commonData);
            case Encoder.NodeDataTypeExtendedData:
                return CreateExtendedNode(kind, data, childIndices, commonData);
            default:
                return CreateChildrenNode(kind, data, childIndices, commonData);
        }
    }

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_SourceFile(data uint32, childIndices []int, commonData uint8) (*ast.Node, error)</c>（decoder.go:259-303）。</summary>
    internal Node DecodeExtendedDataSourceFile(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);

        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var fileNameIdx = Decoder.ReadLE32(_raw, extOff + 4);
        var pathIdx = Decoder.ReadLE32(_raw, extOff + 8);
        var languageVariant = (LanguageVariant)Decoder.ReadLE32(_raw, extOff + 12);
        var scriptKind = (ScriptKind)Decoder.ReadLE32(_raw, extOff + 16);
        var text = GetString(textIdx);
        var fileName = GetString(fileNameIdx);
        var path = GetString(pathIdx);
        if (GetEncodedRootLength(fileName) == 0 || fileName != NormalizePath(fileName))
        {
            throw new DebugFailureException($"invalid source file name \"{fileName}\"");
        }

        // Recover parse options from header.
        var parseOpts = Decoder.ReadLE32(_raw, (int)Encoder.HeaderOffsetParseOptions);
        var opts = new SourceFileParseOptions
        {
            FileName = fileName,
            Path = new Path(path),
            ExternalModuleIndicatorOptions = new ExternalModuleIndicatorOptions
            {
                JSX = (parseOpts & 1) != 0,
                Force = (parseOpts & 2) != 0,
            },
        };

        // Collect children: first is statements NodeList, second is EndOfFile.
        NodeList? stmts = null;
        Node? endOfFile = null;
        foreach (var ci in childIndices)
        {
            if (NodeField(ci, Encoder.NodeOffsetKind) == Encoder.SyntaxKindNodeList)
            {
                stmts = NodeListAt(ci);
            }
            else if (_nodes[ci] is not null && _nodes[ci]!.Kind == Kind.EndOfFile)
            {
                endOfFile = _nodes[ci];
            }
        }

        if (endOfFile is null)
        {
            endOfFile = _factory.NewToken(Kind.EndOfFile);
        }

        var node = _factory.NewSourceFile(opts, text, stmts, endOfFile);
        var sourceFile = node.AsSourceFile()!;
        sourceFile.LanguageVariant = languageVariant;
        sourceFile.ScriptKind = scriptKind;
        return node;
    }

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_TemplateHead(data uint32, childIndices []int, commonData uint8) (*ast.Node, error)</c>（decoder.go:305-311）。</summary>
    internal Node DecodeExtendedDataTemplateHead(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);
        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var rawTextIdx = Decoder.ReadLE32(_raw, extOff + 4);
        var flags = Decoder.ReadLE32(_raw, extOff + 8);
        return _factory.NewTemplateHead(GetString(textIdx), GetString(rawTextIdx), (TokenFlags)flags);
    }

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_TemplateMiddle(data uint32, childIndices []int, commonData uint8) (*ast.Node, error)</c>（decoder.go:313-319）。</summary>
    internal Node DecodeExtendedDataTemplateMiddle(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);
        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var rawTextIdx = Decoder.ReadLE32(_raw, extOff + 4);
        var flags = Decoder.ReadLE32(_raw, extOff + 8);
        return _factory.NewTemplateMiddle(GetString(textIdx), GetString(rawTextIdx), (TokenFlags)flags);
    }

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_TemplateTail(data uint32, childIndices []int, commonData uint8) (*ast.Node, error)</c>（decoder.go:321-327）。</summary>
    internal Node DecodeExtendedDataTemplateTail(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);
        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var rawTextIdx = Decoder.ReadLE32(_raw, extOff + 4);
        var flags = Decoder.ReadLE32(_raw, extOff + 8);
        return _factory.NewTemplateTail(GetString(textIdx), GetString(rawTextIdx), (TokenFlags)flags);
    }

    /// <summary>Go: <c>func (d *astDecoder) singleChild(childIndices []int) *ast.Node</c>（decoder.go:329-334）。</summary>
    internal Node? SingleChild(List<int> childIndices) => childIndices.Count == 0 ? null : _nodes[childIndices[0]];

    /// <summary>Go: <c>func (d *astDecoder) singleNodeListChild(childIndices []int) *ast.NodeList</c>（decoder.go:336-341）。</summary>
    internal NodeList? SingleNodeListChild(List<int> childIndices) => childIndices.Count == 0 ? null : _nodeLists[childIndices[0]];

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_StringLiteral(data uint32, _ []int, _ uint8) (*ast.Node, error)</c>（decoder.go:360-365）。</summary>
    internal Node DecodeExtendedDataStringLiteral(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);
        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var flags = Decoder.ReadLE32(_raw, extOff + 4);
        return _factory.NewStringLiteral(GetString(textIdx), (TokenFlags)flags);
    }

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_NumericLiteral(data uint32, _ []int, _ uint8) (*ast.Node, error)</c>（decoder.go:367-372）。</summary>
    internal Node DecodeExtendedDataNumericLiteral(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);
        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var flags = Decoder.ReadLE32(_raw, extOff + 4);
        return _factory.NewNumericLiteral(GetString(textIdx), (TokenFlags)flags);
    }

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_BigIntLiteral(data uint32, _ []int, _ uint8) (*ast.Node, error)</c>（decoder.go:374-379）。</summary>
    internal Node DecodeExtendedDataBigIntLiteral(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);
        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var flags = Decoder.ReadLE32(_raw, extOff + 4);
        return _factory.NewBigIntLiteral(GetString(textIdx), (TokenFlags)flags);
    }

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_RegularExpressionLiteral(data uint32, _ []int, _ uint8) (*ast.Node, error)</c>（decoder.go:381-386）。</summary>
    internal Node DecodeExtendedDataRegularExpressionLiteral(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);
        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var flags = Decoder.ReadLE32(_raw, extOff + 4);
        return _factory.NewRegularExpressionLiteral(GetString(textIdx), (TokenFlags)flags);
    }

    /// <summary>Go: <c>func (d *astDecoder) decodeExtendedData_NoSubstitutionTemplateLiteral(data uint32, _ []int, _ uint8) (*ast.Node, error)</c>（decoder.go:388-393）。</summary>
    internal Node DecodeExtendedDataNoSubstitutionTemplateLiteral(uint data, List<int> childIndices, byte commonData)
    {
        var extOff = (int)_extData + (int)(data & Encoder.NodeDataStringIndexMask);
        var textIdx = Decoder.ReadLE32(_raw, extOff);
        var flags = Decoder.ReadLE32(_raw, extOff + 4);
        return _factory.NewNoSubstitutionTemplateLiteral(GetString(textIdx), (TokenFlags)flags);
    }
}

/// <summary>Go: <c>type childIterator struct</c>（decoder.go:197-200）。按位掩码遍历子节点的助手。</summary>
internal struct ChildIterator
{
    private readonly List<int> _indices;
    private int _pos;

    private ChildIterator(List<int> indices)
    {
        _indices = indices;
        _pos = 0;
    }

    /// <summary>Go: <c>func newChildIter(indices []int) childIterator</c>（decoder.go:202-205）。</summary>
    internal static ChildIterator NewChildIter(List<int> indices) => new(indices);

    /// <summary>Go: <c>func (it *childIterator) next() int</c>（decoder.go:207-215）。返回下一个子节点下标并前进。</summary>
    internal int Next()
    {
        if (_pos >= _indices.Count)
        {
            return 0;
        }

        var ci = _indices[_pos];
        _pos++;
        return ci;
    }

    /// <summary>Go: <c>func (it *childIterator) nextIf(mask uint8, bit uint8) int</c>（decoder.go:217-222）。掩码位为 1 时才取下一个子节点。</summary>
    internal int NextIf(byte mask, byte bit)
    {
        if ((mask & (1 << bit)) == 0)
        {
            return 0;
        }

        return Next();
    }
}

/// <summary>Go 的包级函数：<c>DecodeSourceFile</c>/<c>DecodeNodes</c>/<c>newASTDecoder</c>/<c>readLE32</c>/<c>decodeNodeCommonData_SyntheticExpression</c>（decoder.go）。</summary>
/// 注意：本类名与 <c>System.Text.Decoder</c> 同名，跨命名空间调用请写成
/// <c>TypeScript.Api.Encoder.Decoder.DecodeNodes(data)</c>（或在调用方文件里加别名），否则会解析到 System.Text.Decoder / 产生 CS0104。
public static class Decoder
{
    /// <summary>Go: <c>func DecodeSourceFile(data []byte) (*ast.SourceFile, error)</c>（decoder.go:33-42）。</summary>
    public static SourceFile DecodeSourceFile(byte[] data)
    {
        var node = DecodeNodes(data);
        if (node.Kind != Kind.SourceFile)
        {
            throw new DebugFailureException($"expected SourceFile root, got {node.Kind}");
        }

        return node.AsSourceFile()!;
    }

    /// <summary>Go: <c>func DecodeNodes(data []byte) (*ast.Node, error)</c>（decoder.go:45-50）。</summary>
    public static Node DecodeNodes(byte[] data)
    {
        var d = NewASTDecoder(data);
        return d.Decode();
    }

    /// <summary>Go: <c>func newASTDecoder(data []byte) (*astDecoder, error)</c>（decoder.go:53-96）。</summary>
    internal static AstDecoder NewASTDecoder(byte[] data)
    {
        if (data.Length < (int)Encoder.HeaderSize)
        {
            throw new DebugFailureException($"data too short for header: {data.Length} bytes");
        }

        var version = data[(int)Encoder.HeaderOffsetMetadata + 3];
        if (version != Encoder.ProtocolVersion)
        {
            throw new DebugFailureException($"unsupported protocol version {version} (expected {Encoder.ProtocolVersion})");
        }

        var strTable = ReadLE32(data, (int)Encoder.HeaderOffsetStringOffsets);
        var strData = ReadLE32(data, (int)Encoder.HeaderOffsetStringData);
        var extData = ReadLE32(data, (int)Encoder.HeaderOffsetExtendedData);
        var nodeOff = ReadLE32(data, (int)Encoder.HeaderOffsetNodes);

        var dataLen = (uint)data.Length;

        // Validate that all offsets are within the buffer.
        if (strTable > dataLen || strData > dataLen || extData > dataLen || nodeOff > dataLen)
        {
            throw new DebugFailureException($"invalid AST header offsets: offsets exceed data length ({dataLen})");
        }

        // Validate monotonic non-decreasing order of regions.
        if (!(strTable <= strData && strData <= extData && extData <= nodeOff))
        {
            throw new DebugFailureException(
                $"invalid AST header offsets: expected strTable <= strData <= extData <= nodeOff (got {strTable}, {strData}, {extData}, {nodeOff})");
        }

        var d = new AstDecoder(data, strTable, strData, extData, nodeOff, AstApi.NewNodeFactory(new NodeFactoryHooks()));

        d._nodeCount = (data.Length - (int)d._nodeOff) / (int)Encoder.NodeSize;

        // Go 在这里把整段 string data 预先转成一个 Go string（供 getString 做零分配子串）；
        // C# 的 string 是 UTF-16，做不到按 UTF-8 字节区间切片，改由 GetString 每次解码（见文件头偏离 5）。

        return d;
    }

    /// <summary>Go: <c>func readLE32(data []byte, offset int) uint32</c>（decoder.go:343-348）。越界时返回 0（与 Go 一致）。</summary>
    internal static uint ReadLE32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    // ── C# 追加的字符串解码助手（Go 侧没有对应函数：Go 靠字符串的字节区间切片天然保真）──

    /// <summary>严格 UTF-8（非法/不完整序列抛 <see cref="DecoderFallbackException"/>），用于 <see cref="DecodeUtf8"/> 的快速路径。</summary>
    internal static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// 解码 <c>[index, index+count)</c> 这段 UTF-8 字节，语义上等价于 Go 的 <c>string(data[index : index+count])</c>（按字节保真）。
    /// 快速路径：严格 UTF-8 解码 —— 对**合法** UTF-8 与 Go 逐码位一致，且与 <c>Encoding.UTF8.GetString</c> 结果相同。
    /// 回退路径：字节里有非法序列时按 WTF-8（<see cref="DecodeWtf8"/>）解码 —— 编码侧为与 Go 字节一致，把孤立代理项写成
    /// CESU-8 形式的 3 字节序列；这里还原为**未配对的 UTF-16 代理码元**，从而与 Go 的字节串一一对应（再次编码即得原字节）。
    /// </summary>
    internal static string DecodeUtf8(byte[] data, int index, int count)
    {
        if (count <= 0)
        {
            return "";
        }

        try
        {
            return StrictUtf8.GetString(data, index, count);
        }
        catch (DecoderFallbackException)
        {
            return DecodeWtf8(data, index, count);
        }
    }

    /// <summary>
    /// WTF-8 解码（仅在字节中含非法 UTF-8 时使用）。
    /// 3 字节形式的代理项（<c>ED A0..BF xx</c>）→ 还原成一个未配对代理码元（Go 保留原字节，重新编码即得同一 WTF-8）；
    /// 其它非法字节 → U+FFFD（Go 会原样保留这些字节，而 UTF-16 string 无法表示任意字节，这是不可消除的差异）。
    /// </summary>
    private static string DecodeWtf8(byte[] data, int index, int count)
    {
        var end = index + count;
        var chars = new char[count]; // 每个输入字节最多产出一个 UTF-16 码元（4 字节序列产出 2 个，也满足该上界）
        var n = 0;
        var i = index;
        while (i < end)
        {
            var b = data[i];
            if (b < 0x80)
            {
                chars[n++] = (char)b;
                i++;
                continue;
            }

            var len = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 0;
            if (len == 0 || i + len > end)
            {
                chars[n++] = '\uFFFD';
                i++;
                continue;
            }

            var cp = b & (0x7F >> len);
            var valid = true;
            for (var k = 1; k < len; k++)
            {
                var c = data[i + k];
                if ((c & 0xC0) != 0x80)
                {
                    valid = false;
                    break;
                }

                cp = (cp << 6) | (c & 0x3F);
            }

            // 3 字节编码的代理项：WTF-8 的合法形态，还原为未配对代理码元。
            if (valid && len == 3 && cp >= 0xD800 && cp <= 0xDFFF)
            {
                chars[n++] = (char)cp;
                i += len;
                continue;
            }

            if (!valid || (len == 2 && cp < 0x80) || (len == 3 && cp < 0x800) || (len == 4 && cp < 0x10000) || cp > 0x10FFFF)
            {
                chars[n++] = '\uFFFD';
                i++;
                continue;
            }

            if (cp <= 0xFFFF)
            {
                chars[n++] = (char)cp;
            }
            else
            {
                cp -= 0x10000;
                chars[n++] = (char)(0xD800 + (cp >> 10));
                chars[n++] = (char)(0xDC00 + (cp & 0x3FF));
            }

            i += len;
        }

        return new string(chars, 0, n);
    }

    /// <summary>Go: <c>func decodeNodeCommonData_SyntheticExpression(_ uint8) (any, bool)</c>（decoder.go:354-356）。</summary>
    internal static (object? TypeNode, bool IsSpread) DecodeNodeCommonDataSyntheticExpression(byte _)
    {
        throw new DebugFailureException("SyntheticExpression should never be decoded");
    }
}
