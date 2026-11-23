using System.Windows;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>
/// Dua bang chuoi <see cref="Loc"/> vao Application.Resources duoi key "S.&lt;key&gt;" (vd. S.Cmd.Start) de XAML
/// dung {DynamicResource S.Cmd.Start}; doi ngon ngu -> nap lai tai nguyen, giao dien doi ngay.
/// </summary>
public static class LocResources
{
    private static ResourceDictionary? _dict;

    public static void Attach(Application app)
    {
        if (_dict != null) return;
        _dict = new ResourceDictionary();
        Fill();
        app.Resources.MergedDictionaries.Add(_dict);
        Loc.Changed += () => Fill();
    }

    private static void Fill()
    {
        if (_dict == null) return;
        foreach (var k in Loc.Keys) _dict["S." + k] = Loc.T(k);
    }
}
