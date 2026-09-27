// ported from: internal/core/context.go
//
// C# 映射（Go 的 context.Context → TypeScript.Core.Context）：
// 本文件在 Go 侧只用 context 携带两个值：requestID(string) 与 CheckerLifetime。C# 没有"任意键值 context"，
// 所以把这两个值 + 取消一起收进一个不可变类：
//
//   Go                                     C#
//   context.Background()                   Context.Background
//   context.WithValue(ctx, requestIDKey,…) Core.WithRequestID(ctx, id)
//   ctx.Value(requestIDKey).(string)       Core.GetRequestID(ctx)
//   context.WithValue(…, checkerLifetimeKey, …) Core.WithCheckerLifetime(ctx, lifetime)
//   ctx.Value(checkerLifetimeKey)          Core.GetCheckerLifetime(ctx)
//   context.WithCancel(ctx) + ctx.Done()   Core.WithCancellation(ctx, token) + ctx.CancellationToken
//
// With* 返回新实例（对应 Go context 的派生链，不可变即线程安全）；CancellationTokenSource 由调用方持有，
// 对应 Go 里 WithCancel 拿到的 cancel 函数。Go 的 nil ctx 在这些调用点上是崩溃（Value on nil interface panic），
// C# 里是 NullReferenceException，同样不复刻成宽松行为。
//
// 需要父 agent 统一的接口：
// 1) `TypeScript.Core.Context` 这个名字是本轮定的：internal/core/semaphore.go、workgroup.go 的 ctx 参数都用它。
// 2) locale 包（M3）将来要往同一个上下文里挂 Locale（Go 的 locale.WithLocale(ctx, …) 同样是 WithValue）：
//    到那时在本类上加一个字段即可，**不要**另起一套上下文类型。
// 3) Go 的 `ctx.Deadline()`/`ctx.Value(任意键)` 这类语义目前没有调用点；真需要时先在 docs/architecture.md 登记。
namespace TypeScript.Core;

/// <summary>Go 的 <c>type CheckerLifetime int</c>（<c>CheckerLifetimeTemporary</c> 等）。</summary>
public enum CheckerLifetime
{
    Temporary = 0,
    Diagnostics = 1,
    API = 2,
}

/// <summary>Go 的 <c>context.Context</c>（本包只用到 requestID / CheckerLifetime / 取消三项）。</summary>
public sealed class Context
{
    /// <summary>Go 的 <c>context.Background()</c>：什么值都没挂。</summary>
    public static readonly Context Background = new("", CheckerLifetime.Temporary, CancellationToken.None);

    /// <summary>Go 的 <c>ctx.Value(requestIDKey)</c>；没挂过时是 Go 的 string 零值（空串）。</summary>
    public string RequestID { get; }

    /// <summary>Go 的 <c>ctx.Value(checkerLifetimeKey)</c>；没挂过时是 <see cref="CheckerLifetime.Temporary"/>。</summary>
    public CheckerLifetime CheckerLifetime { get; }

    /// <summary>Go 的 <c>ctx.Done()</c> / <c>ctx.Err()</c> 在 C# 里的对应物。</summary>
    public CancellationToken CancellationToken { get; }

    // Go 只能通过 WithValue/WithCancel 派生；C# 的派生入口是 Core 静态类里的 With*（见下）。
    // internal 是"同程序集可见"，比 Go 的包可见性宽一点，与 Collections.OrderedMap 的构造器处理一致。
    internal Context(string requestID, CheckerLifetime checkerLifetime, CancellationToken cancellationToken)
    {
        RequestID = requestID;
        CheckerLifetime = checkerLifetime;
        CancellationToken = cancellationToken;
    }
}

public static partial class Core
{
    public static Context WithRequestID(Context ctx, string id)
    {
        return new Context(id, ctx.CheckerLifetime, ctx.CancellationToken);
    }

    public static string GetRequestID(Context ctx)
    {
        return ctx.RequestID;
    }

    public static Context WithCheckerLifetime(Context ctx, CheckerLifetime lifetime)
    {
        return new Context(ctx.RequestID, lifetime, ctx.CancellationToken);
    }

    public static CheckerLifetime GetCheckerLifetime(Context ctx)
    {
        return ctx.CheckerLifetime;
    }

    /// <summary>Go 的 <c>context.WithCancel</c> 里"把取消信号挂到 ctx 上"这一半（CancellationTokenSource
    /// 由调用方持有，对应 Go 拿到的 cancel 函数）。</summary>
    public static Context WithCancellation(Context ctx, CancellationToken cancellationToken)
    {
        return new Context(ctx.RequestID, ctx.CheckerLifetime, cancellationToken);
    }
}
