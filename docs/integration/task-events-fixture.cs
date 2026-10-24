// WI-007 verifier only: drain the volatile stream while REST probes run.
using System;
using System.Net.Http;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public sealed class Wi007Events : IDisposable
{
    public readonly ConcurrentQueue<string> Lines = new();
    readonly CancellationTokenSource cancel = new(TimeSpan.FromSeconds(90));
    readonly HttpClient client = new(new HttpClientHandler { UseProxy = false });
    readonly Task pump;
    public volatile bool Connected;

    public Wi007Events(string url, string authorization)
    {
        pump = Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", authorization);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
            response.EnsureSuccessStatusCode();
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancel.Token));
            while (!cancel.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancel.Token);
                if (line == null) break;
                Lines.Enqueue(line);
                if (line.Contains("\"type\":\"server.connected\"")) Connected = true;
            }
        });
    }

    public void Dispose()
    {
        cancel.Cancel();
        try { pump.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        client.Dispose();
        cancel.Dispose();
    }
}
