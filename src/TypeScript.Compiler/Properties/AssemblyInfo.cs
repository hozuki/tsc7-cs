// Go 的单元测试与被测代码同属一个 package，因此可以直接用 unexported（= C# internal）成员
// （例如 internal/tspath/path_test.go 直接调用 reducePathComponents、hasRelativePathSegment）。
// C# 的测试在另一个程序集里，要保住同样的覆盖只能开放 InternalsVisibleTo —— 这是项目级设置，
// 所有 package 的测试共用，请不要在别处重复添加。
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TypeScript.Compiler.Tests")]
