using System.IO;
using System.Windows;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>Lan chay thu hai cua app gui thong diep (dang ky theo ten) de instance dang chay hien cua so len.</summary>
internal static class SingleInstance
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    public static readonly uint ShowMessage = RegisterWindowMessage("MultiDepLao.App.ShowMainWindow");
    public static void PostShowRequest() { if (ShowMessage != 0) PostMessage(HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero); }
}

/// <summary>Adapter: Core khong tham chieu Win32.</summary>
internal sealed class CallPipeProbeAdapter : ICallPipeProbe
{
    public bool AnyCallPipe() => MultiDepLao.Win32.CallPipes.AnyCallPipe();
    public Task<bool> WaitAsync(bool present, TimeSpan timeout, CancellationToken ct) =>
        MultiDepLao.Win32.CallPipes.WaitAsync(present, timeout, ct);
}

/// <summary>Cac doi tuong dung chung, tao luc khoi dong.</summary>
public sealed class Services
{
    public required AppPaths Paths { get; init; }
    public required ILog Log { get; init; }
    public required ProfileStore Profiles { get; init; }
    public required InstanceManager Manager { get; init; }
    public required CallOwnerService CallOwner { get; init; }
    public required CaptureOwnerService CaptureOwner { get; init; }
    public required MemoryGovernor Governor { get; init; }
    public required AppLock Lock { get; init; }
    public ZaloUpdateWatcher? Updates { get; init; }
    public bool IsPortable { get; init; }
    public AutoStart AutoStart { get; init; } = new(new RegistryAutoStartStore());
    /// <summary>Tien do sao chep ban nocap (0..1); phat tren UI thread.</summary>
    public event Action<double>? CopyProgress;
    internal void RaiseCopyProgress(double p) => CopyProgress?.Invoke(p);
}

public partial class App : Application
{
    public static bool KeepRunningOnExit { get; private set; }
    /// <summary>Huy luot "tu bat" luc mo app khi nguoi dung dong app (khong sinh them Zalo.exe luc dang tat).</summary>
    public static readonly CancellationTokenSource StartupCts = new();
    private Mutex? _mutex;
    private string? _rootDir;
    private Services? _svc;
    private MainWindow? _win;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Goc du lieu phai biet truoc khi tao mutex: MULTIDEPLAO_TEST_INSTANCE chi duoc nghe o che do portable
        // (ban cai dat khong bao gio duoc chay 2 ban tren cung goc du lieu).
        var dr = DataRoot.Resolve(AppContext.BaseDirectory);
        // MULTIDEPLAO_TEST_INSTANCE=<ten>: chi de thu nghiem — ban thu (portable, du lieu rieng) chay song song ban that.
        var testInst = dr.IsPortable ? Environment.GetEnvironmentVariable("MULTIDEPLAO_TEST_INSTANCE") : null;
        var mutexName = @"Local\MultiDepLao.App.SingleInstance"
            + (string.IsNullOrWhiteSpace(testInst) ? "" : "." + new string(testInst.Where(char.IsLetterOrDigit).Take(20).ToArray()));
        _mutex = new Mutex(true, mutexName, out bool created);
        if (!created)
        {
            // Da co MultiDepLao dang chay (co the dang o khay): bao no hien len roi thoat
            if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase) && !e.Args.Contains(AutoStart.Arg, StringComparer.OrdinalIgnoreCase)) SingleInstance.PostShowRequest();
            _mutex.Dispose(); _mutex = null; Shutdown(); return;
        }
        KeepRunningOnExit = e.Args.Contains("--keep-running-on-exit", StringComparer.OrdinalIgnoreCase);

        if (dr.Error != null)
        {
            MessageBox.Show(dr.Error, AppInfo.DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
            _mutex.ReleaseMutex(); _mutex.Dispose(); _mutex = null; Shutdown(); return;
        }
        _rootDir = dr.Root;
        MigrationResult mig;
        if (DataRoot.ShouldMigrate(dr))
        {
            // Chuyen du lieu MultiZalo -> MultiDepLao: sau khi co mutex moi, truoc moi thu khac
            var newRoot = DataRoot.LocalRoot(DataRoot.AppDirName);
            var oldRoot = DataRoot.LocalRoot(DataRoot.OldAppDirName);
            if (!Directory.Exists(newRoot) && Directory.Exists(oldRoot) && Mutex.TryOpenExisting(DataMigration.OldMutexName, out var oldMutex))
            {
                oldMutex.Dispose();
                MessageBox.Show(Loc.T("Boot.QuitOld"), AppInfo.DisplayName, MessageBoxButton.OK, MessageBoxImage.Information);
                _mutex.Dispose(); _mutex = null; Shutdown(); return;
            }
            mig = DataMigration.Migrate(newRoot, oldRoot);
            _rootDir = mig.Root;
        }
        else mig = new MigrationResult(dr.Root, MigrationOutcome.NothingToDo);
        var paths = new AppPaths(mig.Root);
        // Ngon ngu theo config.json (doc som de moi hop thoai/cua so sau do dung dung ngon ngu); chua co/hong -> vi.
        try { Loc.Set(new ConfigStore(paths).Load().Language); } catch { /* giu mac dinh vi */ }
        LocResources.Attach(this);
        ILog log = new FileLog(paths);
        if (dr.IsPortable) log.Info("che do portable: " + dr.Root);
        switch (mig.Outcome)
        {
            case MigrationOutcome.Migrated: log.Info("da chuyen du lieu tu thu muc MultiZalo sang MultiDepLao"); break;
            case MigrationOutcome.BothExist: log.Warn("ca thu muc MultiZalo va MultiDepLao deu ton tai; dung MultiDepLao, khong dung thu muc cu"); break;
            case MigrationOutcome.Failed:
                log.Warn("chua chuyen duoc du lieu sang MultiDepLao, dung thu muc cu: " + mig.Reason);
                MessageBox.Show(Loc.F("Boot.MigrateFail", mig.Reason ?? ""),
                    AppInfo.DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
        }
        DispatcherUnhandledException += (_, a) => Crash(log, "dispatcher", a.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Crash(log, "domain", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { log.Error("task: " + a.Exception.GetBaseException()); a.SetObserved(); };

        log.Info($"=== MultiDepLao khoi dong (keepRunning={KeepRunningOnExit}) ===");
        var cfg = new ConfigStore(paths);
        InstanceManager? mgr = null;
        var profiles = new ProfileStore(paths, cfg, id => mgr?.IsRunning(id) ?? false);
        if (cfg.LastSanitize is { } sr)
            log.Warn($"config.json co muc khong hop le da bi bo: id sai {sr.DroppedInvalidId}, id trung {sr.DroppedDuplicate}, chu so huu bo {sr.OwnersCleared} (ban goc luu o config.json.bad)");
        var added = profiles.ImportExisting();
        if (added.Count > 0) log.Info($"nhan {added.Count} profile co san: {string.Join(", ", added.Select(a => a.Id))}");

        string? zaloRoot = null; TimeSpan? updateEvery = null;
#if DEBUG
        // Thu tay cap nhat Zalo khi chua co ban moi: tro toi thu muc chua cac Zalo-<ver> gia + chu ky kiem tra ngan
        zaloRoot = Environment.GetEnvironmentVariable("MULTIDEPLAO_ZALO_ROOT") ?? Environment.GetEnvironmentVariable("MULTIZALO_ZALO_ROOT");
        if (int.TryParse(Environment.GetEnvironmentVariable("MULTIDEPLAO_UPDATE_CHECK_SEC") ?? Environment.GetEnvironmentVariable("MULTIZALO_UPDATE_CHECK_SEC"), out var sec) && sec > 0) updateEvery = TimeSpan.FromSeconds(sec);
#endif
        var install = new ZaloInstall(paths, zaloRoot, log);
        var progress = new Progress<double>(p => _svc?.RaiseCopyProgress(p)); // tao tren UI thread
        var capOps = new MultiDepLao.Win32.WinCaptureProcessOps();
        var opts = new InstanceManagerOptions
        {
            ExeResolver = ct => install.EnsureNoCapCopyAsync(progress, ct),
            CaptureOwner = () => profiles.Config.CaptureOwnerId,
            CaptureGuard = () => CaptureOwnerService.ForeignCaptureReason(capOps, paths),
            GraceTimeout = TimeSpan.FromSeconds(1.5), // do thuc te: WM_CLOSE khong lam tai khoan da dang nhap thoat, cho lau la vo ich
        };
        var probe = new WinProcessProbe();
        mgr = new InstanceManager(paths, probe, new SafetyGate(paths, probe), install, opts, log,
            new MultiDepLao.Win32.GracefulCloser());   // thoat em bang WM_CLOSE; qua 5 giay thi InstanceManager buoc dung
        var adopted = mgr.AdoptRunning();
        if (adopted.Count > 0) log.Info($"nhan lai instance dang chay: {string.Join(", ", adopted)}");
        mgr.StartMonitoring();

        var callOwner = new CallOwnerService(mgr, profiles, new CallPipeProbeAdapter(), null, log);
        callOwner.Initialize();
        var captureOwner = new CaptureOwnerService(mgr, profiles, paths, new SafetyGate(paths, probe), capOps, install, log);
        captureOwner.Initialize();
        // Khoi dong lai chu quyen chup: neu no cung dang giu quyen goi thi di qua CallOwner de giu quyen goi (da kiem thu)
        captureOwner.RestartOverride = async (id, ct) =>
        {
            var cs = callOwner.GetState();
            if (cs.Kind != CallOwnerKind.Known || !string.Equals(cs.Id, id, StringComparison.OrdinalIgnoreCase)) return (false, null);
            var r = await callOwner.SwitchToAsync(id, null, ct, forceRestart: true);
            return (true, r.Success ? null : r.Reason);
        };

        var governor = new MemoryGovernor(mgr, profiles, callOwner, new SystemMemory(), log);
        foreach (var id in mgr.RunningOldestFirst()) governor.NoteSelected(id);
        var mgrRef = mgr;
        var updates = new ZaloUpdateWatcher(install, paths, probe,
            () => mgrRef.GetAll().Where(s => s.Pid is int).Select(s => (s.Id, s.Pid!.Value)).ToList(), progress, log);
        mgr.StateChanged += st => { _ = Task.Run(updates.OnInstancesChanged); };   // dung/chet -> don ban sao cu; chay/dung -> cap nhat danh dau
        _svc = new Services { Paths = paths, Log = log, Profiles = profiles, Manager = mgr, CallOwner = callOwner, CaptureOwner = captureOwner, Governor = governor, Lock = new AppLock(paths, log), Updates = updates, IsPortable = dr.IsPortable };
        try
        {   // exe da doi cho -> cap nhat lenh khoi dong (chi khi dang bat)
            if (Environment.ProcessPath is { } exe && _svc.AutoStart.RefreshPath(exe)) log.Info("da cap nhat duong dan khoi dong cung Windows");
        }
        catch (Exception ex) { log.Warn("khoi dong cung Windows: " + ex.GetType().Name); }
        ThemeManager.Apply(profiles.Config.Theme);
        ThemeManager.ApplyAccent(profiles.Config.Accent);
        _win = new MainWindow(_svc);
        MainWindow = _win;
        bool fromWindowsStart = e.Args.Contains(AutoStart.Arg, StringComparer.OrdinalIgnoreCase);
        if (e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) _win.StartInTray();   // lenh cu, khong con tao ra
        else _win.Show();
        updates.Start(updateEvery);
        var win = _win;
        _ = Task.Run(async () =>
        {
            try { await governor.StartAutoStartAsync(StartupCts.Token); }
            catch (OperationCanceledException) { log.Info("tu bat: dung do dong app"); return; }
            catch (Exception ex) { log.Error("tu bat khi mo app: " + ex.Message); }
            if (!fromWindowsStart) return;
            // Mo cung Windows: Zalo that cung tu mo co the gianh quyen goi/chup truoc; doi roi xet lai 1 lan
            var act = await AutoStartCallFix.RunAsync(
                () => profiles.Config.CallOwnerId, id => mgrRef.IsRunning(id), () => callOwner.GetState(),
                () => CaptureOwnerService.ForeignCaptureReason(capOps, paths) != null || ForeignZaloRunning(paths),
                id => callOwner.SwitchToAsync(id, null, default, forceRestart: true), log);
            if (act == CallFixAction.Notify && profiles.Config.CallOwnerId is { } owner)
                await win.Dispatcher.InvokeAsync(() => win.ShowCallHint(owner));
        });
    }

    /// <summary>
    /// Co Zalo.exe nao chay tu NGOAI thu muc du lieu cua app (vd. Zalo cai binh thuong)? CHI DOC duong dan exe, khong mo
    /// tien trinh de ghi/tat. Khong doc duoc duong dan -> coi la Zalo ngoai (than trong: chi thong bao, khong tu khoi dong lai).
    /// </summary>
    private static bool ForeignZaloRunning(AppPaths paths)
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("Zalo"))
        {
            using (p)
            {
                string? exe = null;
                try { exe = p.MainModule?.FileName; } catch { }
                if (exe == null || !AppPaths.IsUnder(exe, paths.Root)) return true;
            }
        }
        return false;
    }

    private void Crash(ILog log, string where, Exception? ex)
    {
        try { log.Error($"loi khong bat duoc ({where}): {ex}"); } catch { }
        // File rieng chi co loai loi + stack trace (khong du lieu Zalo) de de gui khi bao loi.
        try
        {
            var dir = _svc?.Paths.LogsDir ?? new AppPaths(_rootDir).LogsDir;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt"),
                $"{where}\r\n{ex?.GetType().FullName}\r\n{ex?.StackTrace}\r\n");
        }
        catch { }
        try { _win?.EmergencyRestore(); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _win?.EmergencyRestore(); } catch { }
        try { ThemeManager.Shutdown(); } catch { }
        try { _svc?.Manager.Dispose(); } catch { }
        try { _svc?.Updates?.Dispose(); } catch { }
        try { _svc?.Log.Info("=== MultiDepLao thoat ==="); } catch { }
        try { _mutex?.ReleaseMutex(); _mutex?.Dispose(); } catch { }
        base.OnExit(e);
    }
}


