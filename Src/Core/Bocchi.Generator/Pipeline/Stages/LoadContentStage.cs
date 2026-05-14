using Bocchi.Generator.Pipeline;
using Bocchi.Workspace.Scanning;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>使用 <see cref="ContentScanner"/> 扫描内容空间，填 <see cref="BuildSession.Scan"/>。</summary>
public sealed class LoadContentStage : IBuildStage
{
    private readonly ContentScanner _scanner;

    public LoadContentStage(ContentScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        _scanner = scanner;
    }

    public string Name => nameof(LoadContentStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var scan = await _scanner.ScanAsync(session.CancellationToken).ConfigureAwait(false);
        session.Scan = scan;
        session.Log(Name, BuildLogLevel.Info,
            $"扫描完成：files={scan.FilesScanned}, items={scan.ItemsLoaded}, errors={scan.Errors.Count}, warnings={scan.Warnings.Count}.");
        foreach (var warn in scan.Warnings)
        {
            session.Log(Name, BuildLogLevel.Warning, $"[{warn.Code}] {warn.RelativePath}: {warn.Message}");
        }

        return true;
    }
}
