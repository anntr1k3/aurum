using System.Diagnostics;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class DefragStorageOptimizer : IStorageOptimizer
{
    private const int MaximumOutputLength = 64 * 1024;

    public async Task<StorageOperationResult> RunAsync(
        string rootPath,
        StorageOperationKind operation,
        CancellationToken cancellationToken = default)
    {
        var volume = NormalizeVolume(rootPath);
        var executable = Path.Combine(Environment.SystemDirectory, "defrag.exe");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(volume);
        startInfo.ArgumentList.Add(operation == StorageOperationKind.Analyze ? "/A" : "/L");
        startInfo.ArgumentList.Add("/U");
        startInfo.ArgumentList.Add("/V");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows defrag utility could not be started.");
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = string.Join(
                    Environment.NewLine,
                    new[] { await outputTask, await errorTask }.Where(static value => !string.IsNullOrWhiteSpace(value)))
                .Trim();
            if (output.Length > MaximumOutputLength)
            {
                output = output[..MaximumOutputLength] + Environment.NewLine + "… вывод сокращён Aurum";
            }

            return new StorageOperationResult(operation, rootPath, process.ExitCode, output, DateTimeOffset.Now);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private static string NormalizeVolume(string rootPath)
    {
        var root = Path.GetPathRoot(rootPath);
        if (root is null || root.Length < 2 || !char.IsAsciiLetter(root[0]) || root[1] != ':')
        {
            throw new ArgumentException("Only local drive-letter volumes are supported.", nameof(rootPath));
        }

        return root[..2];
    }
}
