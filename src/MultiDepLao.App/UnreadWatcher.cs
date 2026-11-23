using System.Windows.Threading;
using MultiDepLao.Core;
using MultiDepLao.Win32;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.App;

/// <summary>
/// Luong nen (MTA) quet huy hieu chua doc cua cac tai khoan dang chay moi 4 giay (UI Automation, chi hinh hoc).
/// Changed duoc phat tren UI thread khi trang thai cua mot id doi. Id khong con trong danh sach -> None.
/// </summary>
public sealed class UnreadWatcher : IDisposable
{
    private const int MinMainW = 380, MinMainH = 450;
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(4);

    private readonly Func<IReadOnlyList<(string Id, int Pid)>> _accounts;
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _thread;

    public event Action<string, UnreadResult>? Changed;

    public UnreadWatcher(Func<IReadOnlyList<(string Id, int Pid)>> runningAccounts, Dispatcher ui, ILog log)
    { _accounts = runningAccounts; _ui = ui; _log = log; }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "UnreadWatcher" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Run()
    {
        using var probe = new UnreadBadgeProbe();
        var last = new Dictionary<string, (UnreadState, int?)>(StringComparer.OrdinalIgnoreCase);
        var hwnds = new Dictionary<int, IntPtr>();
        var errLogged = new HashSet<string>();
        var rawLogged = new HashSet<string>();
        while (!_stop.IsSet)
        {
            try
            {
                IReadOnlyList<(string Id, int Pid)> list;
                try { list = _ui.Invoke(_accounts); } catch { break; }   // UI thread da dong
                var alive = new HashSet<string>(list.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
                foreach (var id in last.Keys.ToList())
                    if (!alive.Contains(id)) { last.Remove(id); Report(id, new(UnreadState.None, null, null), true); }

                foreach (var (id, pid) in list)
                {
                    if (_stop.IsSet) return;
                    UnreadResult s;
                    try
                    {
                        if (!hwnds.TryGetValue(pid, out var h) || h == IntPtr.Zero || !IsWindow(h))
                        {
                            h = ZaloWindowFinder.FindMainWindow((uint)pid, MinMainW, MinMainH);
                            hwnds[pid] = h;
                        }
                        s = probe.Probe(h);
                        if (probe.LastError is { } err && errLogged.Add(id + "|" + err))
                            _log.Warn($"[{id}] chua doc: loi UIA {err}");
                    }
                    catch (Exception e)
                    {
                        s = new(UnreadState.Unknown, null, null);
                        if (errLogged.Add(id + "|" + e.GetType().Name)) _log.Warn($"[{id}] chua doc: loi {e.GetType().Name}");
                    }
                    if (s.State != UnreadState.None && s.Count == null && s.RawCodePoint is int rc && rawLogged.Add($"{id}|{rc}"))
                        _log.Info($"[{id}] ky tu huy hieu la U+{rc:X4}");
                    if (!last.TryGetValue(id, out var prev) || prev != (s.State, s.Count))
                    {
                        last[id] = (s.State, s.Count);
                        Report(id, s, false);
                    }
                }
            }
            catch (Exception e) { _log.Warn("[unread] " + e.GetType().Name); }
            _stop.Wait(Period);
        }
    }

    private void Report(string id, UnreadResult s, bool gone)
    {
        _log.Info($"[{id}] chua doc: {(s.State == UnreadState.Has ? "co (" + (s.Count?.ToString() ?? "?") + ")" : s.State == UnreadState.None ? "khong" : "chua biet")}{(gone ? " (khong con chay)" : "")}");
        try { _ui.BeginInvoke(() => Changed?.Invoke(id, s)); } catch { /* dang dong */ }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(300);
    }
}


