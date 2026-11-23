using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class AtomicFileTests
{
    [Fact]
    public void ConcurrentWrites_NoError_NoLeftoverTmp_LastContentIsOneOfWritten()
    {
        using var t = new TempRoot();
        Directory.CreateDirectory(t.Paths.Root);
        var f = Path.Combine(t.Paths.Root, "x.json");
        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => AtomicFile.WriteAllText(f, $"v{i}"));
        Assert.Empty(Directory.GetFiles(t.Paths.Root, "*.tmp"));
        Assert.Matches("^v\\d+$", File.ReadAllText(f));
    }

    [Fact]
    public void TargetBrieflyLocked_RetriesAndSucceeds()
    {
        // Mo phong Defender/bo lap chi muc giu file dich trong tich tac (khong cho xoa/ghi de).
        using var t = new TempRoot();
        Directory.CreateDirectory(t.Paths.Root);
        var f = Path.Combine(t.Paths.Root, "x.json");
        File.WriteAllText(f, "cu");
        var holder = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () => { await Task.Delay(60); holder.Dispose(); });
        AtomicFile.WriteAllText(f, "moi");
        release.Wait();
        Assert.Equal("moi", File.ReadAllText(f));
        Assert.Empty(Directory.GetFiles(t.Paths.Root, "*.tmp"));
    }

    [Fact]
    public void TargetLockedForever_ThrowsAfterRetries_AndCleansTmp()
    {
        using var t = new TempRoot();
        Directory.CreateDirectory(t.Paths.Root);
        var f = Path.Combine(t.Paths.Root, "x.json");
        File.WriteAllText(f, "cu");
        using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var ex = Record.Exception(() => AtomicFile.WriteAllText(f, "moi"));
            Assert.True(ex is IOException or UnauthorizedAccessException, ex?.GetType().Name);
        }
        Assert.Equal("cu", File.ReadAllText(f));
        Assert.Empty(Directory.GetFiles(t.Paths.Root, "*.tmp"));
    }
}
