namespace Bocchi.Workspace.Scanning;

/// <summary>
/// 加载器返回的结果：成功值（可空）+ 校验错误列表（永不为 <c>null</c>，可能为空）。
/// </summary>
public sealed record LoadResult<T>(T? Document, IReadOnlyList<ContentValidationError> Errors)
    where T : class
{
    /// <summary>是否成功（即 <see cref="Document"/> 非空）。</summary>
    public bool Success => Document is not null;
}

/// <summary>非泛型工厂，避免 CA1000。</summary>
public static class LoadResult
{
    /// <summary>失败结果工厂。</summary>
    public static LoadResult<T> Fail<T>(IReadOnlyList<ContentValidationError> errors) where T : class
        => new(null, errors);

    /// <summary>成功（可附带 warnings）结果工厂。</summary>
    public static LoadResult<T> Ok<T>(T document, IReadOnlyList<ContentValidationError>? warnings = null) where T : class
        => new(document, warnings ?? []);
}