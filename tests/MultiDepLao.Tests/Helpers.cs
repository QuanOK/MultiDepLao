using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public sealed class TempRoot : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mz-test-" + Guid.NewGuid().ToString("N"));
    public TempRoot() => Directory.CreateDirectory(Path);
    public string Sub(params string[] p) => System.IO.Path.Combine(new[] { Path }.Concat(p).ToArray());
    public AppPaths Paths => new(Sub("data"));
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public sealed class FakeProbe : IProcessProbe
{
    public Dictionary<int, ProcessInfo> Procs { get; } = new();
    public List<int> Killed { get; } = new();
    public ProcessInfo? Get(int pid) => Procs.TryGetValue(pid, out var p) ? p : null;
    public void KillTree(int pid) { Killed.Add(pid); Procs.Remove(pid); }
}
