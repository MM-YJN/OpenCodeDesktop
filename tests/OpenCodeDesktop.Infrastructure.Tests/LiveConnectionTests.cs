using System.Diagnostics;
using System.Security.Cryptography;

using OpenCodeDesktop.Core.Connections;
using OpenCodeDesktop.Infrastructure.Connections;

namespace OpenCodeDesktop.Infrastructure.Tests;

/// <summary>Opt in with OPENCODE_CONNECTION_TEST_EXECUTABLE pointing to the pinned, separately downloaded CLI.</summary>
public sealed class LiveConnectionTests
{
    [Fact]
    public async Task PinnedIsolatedServersSupportAuthenticationAndAtomicSwitching()
    {
        string? executable = Environment.GetEnvironmentVariable("OPENCODE_CONNECTION_TEST_EXECUTABLE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(executable), "Opt-in: set OPENCODE_CONNECTION_TEST_EXECUTABLE to the pinned 2.1.4 CLI.");
        using (FileStream file = File.OpenRead(executable!))
        {
            byte[] hash = await SHA256.HashDataAsync(file, TestContext.Current.CancellationToken);
            Assert.Equal("B12B0613E9B4A491E5B12D90A5E9BBAE6D261FE9688C82BF73125A036963D399", Convert.ToHexString(hash));
        }

        await using IsolatedServer first = await IsolatedServer.StartAsync(executable!);
        await using IsolatedServer second = await IsolatedServer.StartAsync(executable!);
        using HttpClient client = OpenCodeConnectionService.CreateHttpClient();
        using ServerConnections connections = new(new OpenCodeConnectionService(client));
        ServerProfile local = connections.Save(null, "Local development", first.Origin)!;
        ServerProfile sandbox = connections.Save(null, "Sandbox", second.Origin)!;
        await WaitForReadyAsync(connections, local.Id, first.Password);
        Assert.Equal(local, connections.Active!.Profile);
        Assert.Equal("2.1.4", connections.Active.Version);
        await connections.ConnectAsync(sandbox.Id, "incorrect-disposable-password");
        Assert.Equal(ConnectionOutcome.AuthenticationFailed, connections.LastResult!.Outcome);
        Assert.Equal(local, connections.Active.Profile);
        await WaitForReadyAsync(connections, sandbox.Id, second.Password);
        Assert.Equal(sandbox, connections.Active.Profile);
        Assert.DoesNotContain(first.Password, connections.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Password, connections.Message, StringComparison.Ordinal);
        connections.Disconnect();
        Assert.Null(connections.Active);
    }

    internal static async Task WaitForReadyAsync(ServerConnections connections, Guid id, string password)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            await connections.ConnectAsync(id, password);
            if (connections.Active?.Profile.Id == id)
            {
                return;
            }

            Assert.Equal(ConnectionOutcome.Unavailable, connections.LastResult!.Outcome);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The isolated server did not become ready.");
    }

    internal sealed class IsolatedServer(string scratch, Process process, string origin, string password) : IAsyncDisposable
    {
        public string Origin { get; } = origin;
        public string Password { get; } = password;
        internal string WorkspaceDirectory => Path.Combine(scratch, "workspace");

        public static async Task<IsolatedServer> StartAsync(string executable)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "ocd-wi002-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(scratch, "workspace"));
            ProcessStartInfo start = new(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.Combine(scratch, "workspace"),
            };
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("OPENCODE_", StringComparison.Ordinal)).ToArray())
            {
                start.Environment.Remove(key);
            }

            foreach ((string key, string directory) in new[]
            {
                ("XDG_DATA_HOME", "data"), ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache"), ("XDG_STATE_HOME", "state"),
                ("TEMP", "tmp"), ("TMP", "tmp"), ("TMPDIR", "tmp"), ("OPENCODE_TEST_HOME", "home"), ("OPENCODE_CONFIG_DIR", "config/opencode"),
            })
            {
                string path = Path.Combine(scratch, directory);
                Directory.CreateDirectory(path);
                start.Environment[key] = path;
            }

            string password = Guid.NewGuid().ToString("N");
            start.Environment["OPENCODE_PASSWORD"] = password;
            start.Environment["OPENCODE_CONFIG_PROJECT_DISABLE"] = "1";
            start.Environment["OPENCODE_DISABLE_MODELS_FETCH"] = "1";
            foreach (string argument in new[] { "serve", "--hostname", "127.0.0.1", "--port", "0" })
            {
                start.ArgumentList.Add(argument);
            }

            Process process = Process.Start(start)!;
            IsolatedServer server = new(scratch, process, "", password);
            try
            {
                _ = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                string? line = await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
                const string prefix = "server listening on ";
                Assert.NotNull(line);
                Assert.StartsWith(prefix, line, StringComparison.Ordinal);
                string origin = line[prefix.Length..];
                Assert.True(ServerAddress.TryParse(origin, out _));
                return new(scratch, process, origin, password);
            }
            catch
            {
                await server.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            process.Dispose();
            string target = Path.GetFullPath(scratch);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("ocd-wi002-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected isolated-server cleanup path.");
            }

            foreach (string path in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(target, recursive: true);
                    break;
                }
                catch (Exception exception) when (attempt < 20 && exception is IOException or UnauthorizedAccessException)
                {
                    // Windows may release a terminated server's mapped Bun DLL shortly after process exit.
                    await Task.Delay(100, CancellationToken.None);
                }
            }
        }
    }
}
