// Controlled subprocess fixture: copies of the apphost select behavior by filename, without shared environment changes.
string executable = Environment.ProcessPath!;
string mode = Path.GetFileNameWithoutExtension(executable);
await File.WriteAllTextAsync(executable + ".pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
if (mode.Contains("stall", StringComparison.Ordinal))
{
    await Task.Delay(Timeout.Infinite);
}
if (mode.Contains("stdout", StringComparison.Ordinal) || mode.Contains("stderr", StringComparison.Ordinal))
{
    TextWriter output = mode.Contains("stderr", StringComparison.Ordinal) ? Console.Error : Console.Out;
    while (true)
    {
        await output.WriteAsync(new string('x', 1024));
    }
}
if (mode.Contains("fail", StringComparison.Ordinal))
{
    return 7;
}

Console.Write("opencode v2.1.4");
return 0;
