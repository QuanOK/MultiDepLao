namespace MultiDepLao.Win32;

/// <summary>Phat hien pipe PipeZCall* (quyen goi dien cua Zalo). Chi liet ke ten pipe, khong mo pipe.</summary>
public static class CallPipes
{
    public static bool AnyCallPipe()
    {
        try
        {
            foreach (var p in Directory.EnumerateFiles(@"\\.\pipe\"))
                if (p.Contains("PipeZCall", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    /// <summary>Doi toi khi AnyCallPipe() == present. True neu dat truoc timeout; huy bang ct thi nem OperationCanceledException.</summary>
    public static async Task<bool> WaitAsync(bool present, TimeSpan timeout, CancellationToken ct = default)
    {
        var end = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (AnyCallPipe() == present) return true;
            if (DateTime.UtcNow >= end) return false;
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
    }
}
