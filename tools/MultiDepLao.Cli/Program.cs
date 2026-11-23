using MultiDepLao.Core;

// Cong cu kiem tra tay. list/status/verify CHI DOC (khong ghi dia). start/stop/ensure-copy thao tac that.
var paths = new AppPaths();
var probe = new WinProcessProbe();
var gate = new SafetyGate(paths, probe);
var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var arg = args.Length > 1 ? args[1] : null;

IReadOnlyList<string> AllIds()
{
    var store = new ProfileStore(paths, new ConfigStore(paths), _ => false);
    return store.Accounts.Select(a => a.Id).Concat(store.Discover()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

string Describe(string id)
{
    var v = gate.Verify(id);
    return v.Ok ? $"{id,-12} DANG CHAY  pid={v.Instance!.Pid}  exe={Path.GetFileName(Path.GetDirectoryName(v.Instance.ExePath))}\\{Path.GetFileName(v.Instance.ExePath)}"
                : $"{id,-12} da tat     ({v.Failure}: {v.Reason})";
}

switch (cmd)
{
    case "list":
    {
        var store = new ProfileStore(paths, new ConfigStore(paths), _ => false);
        foreach (var a in store.Accounts) Console.WriteLine($"{a.Id,-12} {a.DisplayName}  [config]");
        foreach (var id in store.Discover()) Console.WriteLine($"{id,-12} (chua co trong config)");
        return 0;
    }
    case "status":
        foreach (var id in AllIds()) Console.WriteLine(Describe(id));
        return 0;
    case "verify":
        if (arg == null) { Console.Error.WriteLine("verify <id>"); return 2; }
        Console.WriteLine(Describe(arg));
        return gate.Verify(arg).Ok ? 0 : 1;
    case "start":
    {
        if (arg == null) { Console.Error.WriteLine("start <id>"); return 2; }
        var log = new FileLog(paths);
        using var mgr = new InstanceManager(paths, probe, gate, new ZaloInstall(paths, null, log), null, log);
        var st = await mgr.StartAsync(arg);
        Console.WriteLine($"{st.Id}: {st.State} pid={st.Pid} {st.Reason}");
        await Task.Delay(3500); mgr.Poll();
        st = mgr.GetStatus(arg);
        Console.WriteLine($"{st.Id}: {st.State} pid={st.Pid} {st.Reason}");
        return st.State is InstanceState.Running or InstanceState.Starting ? 0 : 1;
    }
    case "stop":
    {
        if (arg == null) { Console.Error.WriteLine("stop <id>"); return 2; }
        var log = new FileLog(paths);
        using var mgr = new InstanceManager(paths, probe, gate, null, null, log);
        var r = await mgr.StopAsync(arg);
        Console.WriteLine($"{arg}: {r.Outcome} {r.Reason}");
        return r.Outcome is StopOutcome.Stopped or StopOutcome.AlreadyStopped ? 0 : 1;
    }
    case "ensure-copy":
    {
        var log = new FileLog(paths);
        var inst = new ZaloInstall(paths, null, log);
        double last = -1;
        var prog = new Progress<double>(p => { if (p - last >= 0.1 || p >= 1) { last = p; Console.Write($"\r{p:P0}   "); } });
        var exe = await inst.EnsureNoCapCopyAsync(prog);
        Console.WriteLine($"\n{exe}");
        return 0;
    }
    default:
        Console.WriteLine("MultiDepLao.Cli: list | status | verify <id> | start <id> | stop <id> | ensure-copy");
        return 0;
}
