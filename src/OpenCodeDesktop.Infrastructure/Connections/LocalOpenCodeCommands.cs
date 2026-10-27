using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenCodeDesktop.Infrastructure.Connections;

public sealed class LocalOpenCodeCommands(TimeSpan? deadline = null) : ILocalOpenCodeCommands
{
    private const int MaximumOutputCharacters = 16384;

    public IEnumerable<string> FindExecutables()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!Path.IsPathFullyQualified(appData))
        {
            return [];
        }

        string architecture = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => "x86" };
        string package = "cli-windows-" + architecture;
        List<string> candidates = [.. BundledCliExecutables(appData, architecture)];
        candidates.Add(Path.Combine(appData, "npm", "node_modules", "@opencode", package, "bin", "opencode.exe"));
        candidates.Add(Path.Combine(appData, "npm", "node_modules", "@opencode", "cli", "node_modules", "@opencode", package, "bin", "opencode.exe"));
        return candidates.Where(File.Exists).ToArray();
    }

    private static IEnumerable<string> BundledCliExecutables(string appData, string architecture)
    {
        string root = Path.Combine(appData, "ai.opencode.desktop", "cli");
        IEnumerable<string> directories;
        try
        {
            directories = Directory.Exists(root)
                ? Directory.EnumerateDirectories(root)
                    .Where(directory => directory.EndsWith("-" + architecture, StringComparison.OrdinalIgnoreCase))
                    .OrderDescending(StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return directories.Select(directory => Path.Combine(directory, "opencode-cli.exe"));
    }

    public Task<string?> ExecuteAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        // This boundary cannot be used for mutation, shell commands, full config dumps or arbitrary CLI arguments.
        if (!(arguments.SequenceEqual(["--version"]) || arguments.SequenceEqual(["service", "status"]) || arguments.SequenceEqual(["service", "get", "password"])))
        {
            throw new ArgumentException("Only local discovery commands are supported.", nameof(arguments));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline ?? TimeSpan.FromSeconds(5));
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = start };
        timeout.Token.ThrowIfCancellationRequested();
        if (!process.Start())
        {
            return null;
        }
        // Cancellation, including output overflow, kills only the helper process tree this call created.
        using CancellationTokenRegistration registration = timeout.Token.Register(() => ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }));
        try
        {
            return await CaptureOutputAsync(process, timeout).ConfigureAwait(false);
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            throw new OperationCanceledException(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }
    }, cancellationToken);

    private static async Task<string?> CaptureOutputAsync(Process process, CancellationTokenSource timeout)
    {
        Task<string> stdout = ReadBoundedAsync(process.StandardOutput, timeout);
        Task<string> stderr = ReadBoundedAsync(process.StandardError, timeout);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
        timeout.Token.ThrowIfCancellationRequested();
        return process.ExitCode == 0 ? await stdout.ConfigureAwait(false) : null;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationTokenSource timeout)
    {
        StringBuilder output = new();
        char[] buffer = new char[1024];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
            if (count == 0)
            {
                return output.ToString();
            }

            if (output.Length + count > MaximumOutputCharacters)
            {
                await timeout.CancelAsync().ConfigureAwait(false);
                throw new OperationCanceledException(timeout.Token);
            }
            output.Append(buffer, 0, count);
        }
    }
}
