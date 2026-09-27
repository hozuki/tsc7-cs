// ported from: internal/printer/namegenerator_test.go
//
// 移植范围（35 → 15 个用例，1:1 逐条对应）：
//   namegenerator_test.go:13-253 的 15 个用例全部移植（它们只依赖 EmitContext + NodeFactory + NameGenerator）。
//   namegenerator_test.go:254-641 的 21 个用例（TestGeneratedNameForIdentifier1 … TestGeneratedNameForOther）**未移植**：
//   它们都调用 `binder.BindSourceFile(file)` 与 `parsetestutil.ParseTypeScript(...)`，而 C# 的
//   `TypeScript.Binder.Binder.BindSourceFile` 尚未移植（TypeScript.Binder 包目前只有占位目录）——
//   具体清单见本文件末尾的注释块。按任务约定：不 stub、不造假 binder、不标 Skip，只省略并上报。
//
// 移植映射（docs/architecture.md §5）：
//   * Go 的 `assert.Equal(t, expected, actual)` → xunit 的 `Assert.Equal(expected, actual)`；每个 Go 测试函数
//     都是一个 xunit `[Fact]`（这些用例没有 t.Run 子测试，不需要 [Theory]/[MemberData]，故也不需要
//     ITestOutputHelper 打用例名）。Go 的 t.Parallel() ↔ xunit 默认的测试类级并行。
//   * Go 的 `printer.NewEmitContext()` → `Printer.NewEmitContext()`（EmitContext.cs 里 `Printer` 的 static 工厂，
//     逐字对应 Go 的 NewEmitContext，内部用 printer 的 NodeFactory 填好 `Factory` 字段）。
//   * Go 的 `&printer.NameGenerator{Context: ec}` → `new NameGenerator { Context = ec }`
//     （C# 的 `Context` 是 public 字段，见 NameGenerator.cs）。
//   * Go 的 `ec.Factory.NewTempVariable()` 等自动生成名工厂 → C# 同名方法（`TypeScript.Printer.NodeFactory`，
//     即 Go 的 internal/printer/factory.go：NewTempVariable / NewTempVariableEx / NewLoopVariable /
//     NewLoopVariableEx / NewUniqueName / NewUniqueNameEx / NewUniquePrivateName …）。
//   * Go 的 `printer.AutoGenerateOptions{Prefix: "A", Suffix: "B"}` → `new AutoGenerateOptions { Prefix = "A", Suffix = "B" }`
//     （C# 里是 struct，字段名与 Go 一致：Flags / Prefix / Suffix）。
//   * Go 的 `printer.GeneratedIdentifierFlagsReservedInNestedScopes` → `GeneratedIdentifierFlags.ReservedInNestedScopes`
//     （枚举成员名 = Go 常量名去掉类型名前缀）。
//   * `g.PushScope(false)` / `g.PopScope(false)`：Go 的 `reuseTempVariableScope` 形参在 C# 里保留 `bool`，
//     测试里照 Go 原样传 `false`。
//   * 本文件里 Go 的注释（含 Strada 相关的 TODO，如 UniqueNameScoped 的 `foo_2` "matches Strada, but is incorrect"）
//     逐条保留：它们描述的是当前**故意**与 Strada 一致、但已知不正确的行为。
using TypeScript.Printer;
using Xunit;
// Go 的包级函数 `printer.NewEmitContext()` → C# 的 `TypeScript.Printer.Printer` 静态成员
// （docs §2：Go 包级函数 = 包级 `Printer` 的 static 成员）。这里 static using 让调用点保持 Go 的
// `printer.NewEmitContext()` 形状；不能写成 `EmitContext.NewEmitContext()`——该工厂不在 EmitContext 上。
// 注：也不能写 `Printer.NewEmitContext()`：在 `TypeScript.Tests` 里裸名 `Printer` 会先解析到命名空间
// `TypeScript.Printer`。
using static TypeScript.Printer.Printer;

namespace TypeScript.Tests;

/// <summary>Go: <c>internal/printer/namegenerator_test.go</c>（15 个可移植用例）。</summary>
public class NameGeneratorTests
{
    // Go: TestTempVariable1（namegenerator_test.go:13）
    [Fact]
    public void TempVariable1()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewTempVariable();
        var name2 = ec.Factory.NewTempVariable();

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name2);

        Assert.Equal("_a", text1);
        Assert.Equal("_b", text2);
    }

    // Go: TestTempVariable2（namegenerator_test.go:28）
    [Fact]
    public void TempVariable2()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewTempVariableEx(new AutoGenerateOptions
        {
            Prefix = "A",
            Suffix = "B",
        });
        var name2 = ec.Factory.NewTempVariableEx(new AutoGenerateOptions
        {
            Prefix = "A",
            Suffix = "B",
        });

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name2);

        Assert.Equal("A_aB", text1);
        Assert.Equal("A_bB", text2);
    }

    // Go: TestTempVariable3（namegenerator_test.go:49）
    [Fact]
    public void TempVariable3()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewTempVariable();

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name1);

        Assert.Equal("_a", text1);
        Assert.Equal("_a", text2);
    }

    // Go: TestTempVariableScoped（namegenerator_test.go:63）
    [Fact]
    public void TempVariableScoped()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewTempVariable();
        var name2 = ec.Factory.NewTempVariable();

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        g.PushScope(false);
        var text2 = g.GenerateName(name2);
        g.PopScope(false);

        Assert.Equal("_a", text1);
        Assert.Equal("_a", text2);
    }

    // Go: TestTempVariableScopedReserved（namegenerator_test.go:80）
    [Fact]
    public void TempVariableScopedReserved()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewTempVariableEx(new AutoGenerateOptions { Flags = GeneratedIdentifierFlags.ReservedInNestedScopes });
        var name2 = ec.Factory.NewTempVariable();

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        g.PushScope(false);
        var text2 = g.GenerateName(name2);
        g.PopScope(false);

        Assert.Equal("_a", text1);
        Assert.Equal("_b", text2);
    }

    // Go: TestLoopVariable1（namegenerator_test.go:97）
    [Fact]
    public void LoopVariable1()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewLoopVariable();
        var name2 = ec.Factory.NewLoopVariable();

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name2);

        Assert.Equal("_i", text1);
        Assert.Equal("_a", text2);
    }

    // Go: TestLoopVariable2（namegenerator_test.go:112）
    [Fact]
    public void LoopVariable2()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewLoopVariableEx(new AutoGenerateOptions
        {
            Prefix = "A",
            Suffix = "B",
        });
        var name2 = ec.Factory.NewLoopVariableEx(new AutoGenerateOptions
        {
            Prefix = "A",
            Suffix = "B",
        });

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name2);

        Assert.Equal("A_iB", text1);
        Assert.Equal("A_aB", text2);
    }

    // Go: TestLoopVariable3（namegenerator_test.go:133）
    [Fact]
    public void LoopVariable3()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewLoopVariable();

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name1);

        Assert.Equal("_i", text1);
        Assert.Equal("_i", text2);
    }

    // Go: TestLoopVariableScoped（namegenerator_test.go:147）
    [Fact]
    public void LoopVariableScoped()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewLoopVariable();
        var name2 = ec.Factory.NewLoopVariable();

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        g.PushScope(false);
        var text2 = g.GenerateName(name2);
        g.PopScope(false);

        Assert.Equal("_i", text1);
        Assert.Equal("_i", text2);
    }

    // Go: TestUniqueName1（namegenerator_test.go:164）
    [Fact]
    public void UniqueName1()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewUniqueName("foo");
        var name2 = ec.Factory.NewUniqueName("foo");

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name2);

        Assert.Equal("foo_1", text1);
        Assert.Equal("foo_2", text2);
    }

    // Go: TestUniqueName2（namegenerator_test.go:179）
    [Fact]
    public void UniqueName2()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewUniqueName("foo");

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name1);

        Assert.Equal("foo_1", text1);
        // Expected to be same because GenerateName goes off object identity
        Assert.Equal("foo_1", text2);
    }

    // Go: TestUniqueNameScoped（namegenerator_test.go:194）
    [Fact]
    public void UniqueNameScoped()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewUniqueName("foo");
        var name2 = ec.Factory.NewUniqueName("foo");

        var g = new NameGenerator { Context = ec };
        Assert.Equal("foo_1", g.GenerateName(name1));

        g.PushScope(false);
        Assert.Equal("foo_2", g.GenerateName(name2)); // Matches Strada, but is incorrect
        // Assert.Equal("foo_1", g.GenerateName(name2)); // TODO: Fix after Strada port is complete.
        g.PopScope(false);
    }

    // Go: TestUniquePrivateName1（namegenerator_test.go:210）
    [Fact]
    public void UniquePrivateName1()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewUniquePrivateName("#foo");
        var name2 = ec.Factory.NewUniquePrivateName("#foo");

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name2);

        Assert.Equal("#foo_1", text1);
        Assert.Equal("#foo_2", text2);
    }

    // Go: TestUniquePrivateName2（namegenerator_test.go:225）
    [Fact]
    public void UniquePrivateName2()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewUniquePrivateName("#foo");

        var g = new NameGenerator { Context = ec };
        var text1 = g.GenerateName(name1);
        var text2 = g.GenerateName(name1);

        Assert.Equal("#foo_1", text1);
        Assert.Equal("#foo_1", text2);
    }

    // Go: TestUniquePrivateNameScoped（namegenerator_test.go:239）
    [Fact]
    public void UniquePrivateNameScoped()
    {
        var ec = NewEmitContext();
        var name1 = ec.Factory.NewUniquePrivateName("#foo");
        var name2 = ec.Factory.NewUniquePrivateName("#foo");

        var g = new NameGenerator { Context = ec };
        Assert.Equal("#foo_1", g.GenerateName(name1));

        g.PushScope(false); // private names are always reserved in nested scopes
        Assert.Equal("#foo_2", g.GenerateName(name2));
        g.PopScope(false);
    }
}

// 未移植的 21 个用例（namegenerator_test.go:254-641）——全部阻塞于同一个缺口：
// `TypeScript.Binder.Binder.BindSourceFile` 未移植（src/TypeScript.Compiler/Binder/ 目前只有占位目录），
// 且它们还需要 `parsetestutil.ParseTypeScript`（C# 侧的等价入口是 Ast.ParseSourceFile，但用例的期望值
// 依赖 binder 写入的符号/节点标志，例如 namespace 是否与局部名冲突）。按任务约定不 stub、不 Skip：
//   namegenerator_test.go:254  TestGeneratedNameForIdentifier1     — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:271  TestGeneratedNameForIdentifier2     — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:291  TestGeneratedNameForIdentifier3     — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:313  TestGeneratedNameForNamespace1      — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:331  TestGeneratedNameForNamespace2      — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:349  TestGeneratedNameForNamespace3      — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:371  TestGeneratedNameForNamespace4      — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:398  TestGeneratedNameForNodeCached      — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:418  TestGeneratedNameForImport          — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:435  TestGeneratedNameForExport          — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:452  TestGeneratedNameForFunctionDeclaration1 — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:469  TestGeneratedNameForFunctionDeclaration2 — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:486  TestGeneratedNameForClassDeclaration1    — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:503  TestGeneratedNameForClassDeclaration2    — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:520  TestGeneratedNameForExportAssignment     — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:537  TestGeneratedNameForClassExpression      — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:554  TestGeneratedNameForMethod1              — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:571  TestGeneratedNameForMethod2              — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:588  TestGeneratedPrivateNameForMethod        — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:605  TestGeneratedNameForComputedPropertyName — TypeScript.Binder.Binder.BindSourceFile 未移植
//   namegenerator_test.go:622  TestGeneratedNameForOther                — TypeScript.Binder.Binder.BindSourceFile 未移植
