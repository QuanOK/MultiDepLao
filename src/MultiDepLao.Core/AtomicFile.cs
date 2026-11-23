using System.Collections.Concurrent;

namespace MultiDepLao.Core;

/// <summary>
/// Ghi file "nguyen tu": ghi ra file tam ten rieng roi doi ten de len file dich. Dung cho config.json va lock.json.
/// Truoc day moi noi dung chung mot ten tam co dinh (".tmp") va doi ten mot lan: 2 lan ghi chong nhau thi lan nay doi ten mat
/// file tam cua lan kia, va khi Defender/bo lap chi muc dang mo file dich thi File.Move bi tu choi (UnauthorizedAccessException)
/// — gay test chap chon va co the xay ra trong app that. Nay: xep hang theo duong dan + thu lai + luon don file tam.
/// </summary>
public static class AtomicFile
{
    internal const int Attempts = 6;
    internal const int RetryDelayMs = 25;

    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static void WriteAllText(string target, string content)
    {
        lock (Locks.GetOrAdd(Path.GetFullPath(target), _ => new object()))
        {
            var tmp = $"{target}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tmp, content);
                for (int attempt = 1; ; attempt++)
                {
                    try { File.Move(tmp, target, overwrite: true); return; }
                    catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < Attempts)
                    {
                        Thread.Sleep(RetryDelayMs * attempt);   // file dich dang bi tien trinh khac mo trong tich tac
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* bo qua */ }
            }
        }
    }
}
