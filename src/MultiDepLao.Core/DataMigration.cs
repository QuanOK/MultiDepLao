namespace MultiDepLao.Core;

public enum MigrationOutcome { NothingToDo, Migrated, BothExist, Failed }

/// <summary>Ket qua chuyen du lieu: Root la goc ma app phai dung (moi, hoac cu neu chuyen that bai).</summary>
public sealed record MigrationResult(string Root, MigrationOutcome Outcome, string? Reason = null);

/// <summary>Chuyen %LOCALAPPDATA%\MultiZalo -> %LOCALAPPDATA%\MultiDepLao bang Directory.Move (cung o: doi ten tuc thi, khong copy).</summary>
public static class DataMigration
{
    public const string OldMutexName = @"Local\MultiZalo.App.SingleInstance";
    public const string MarkerFile = "migrated-from-MultiZalo.txt";

    public static MigrationResult Migrate(string newRoot, string oldRoot, Action<string, string>? move = null, Func<DateTime>? now = null)
    {
        move ??= Directory.Move;
        bool newExists = Directory.Exists(newRoot), oldExists = Directory.Exists(oldRoot);
        if (newExists && oldExists) return new(newRoot, MigrationOutcome.BothExist, "ca thu muc moi lan cu deu ton tai");
        if (newExists || !oldExists) return new(newRoot, MigrationOutcome.NothingToDo);
        try
        {
            move(oldRoot, newRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(oldRoot, MigrationOutcome.Failed, ex.Message);
        }
        try { File.WriteAllText(Path.Combine(newRoot, MarkerFile), (now?.Invoke() ?? DateTime.Now).ToString("o")); } catch { }
        return new(newRoot, MigrationOutcome.Migrated);
    }
}
