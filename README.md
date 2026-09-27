# tsc-cs

Porting the TypeScript compiler (TSGO, the Go implementation at `D:\Source\GitHub\TypeScript-Go`) to C#, with the Go package/API structure preserved as-is.

移植规范、类型映射、里程碑见 [`docs/architecture.md`](docs/architecture.md)。

## 构建与测试

.NET 11（`global.json` 固定 SDK 版本）：

```sh
dotnet build TypeScript.slnx
dotnet test tests/TypeScript.Compiler.Tests/TypeScript.Compiler.Tests.csproj
```

## 目录

| 路径 | 内容 |
| --- | --- |
| `src/TypeScript.Compiler/` | 编译器库（每个 Go package 一个文件夹，见规范第 2 节） |
| `tests/TypeScript.Compiler.Tests/` | 复刻 Go `*_test.go` 的单元测试 |
| `tools/TypeScript.CodeGen/` | 由 TSGO 的生成输入（ast.json、Unicode 数据…）生成 C# 源码 |
