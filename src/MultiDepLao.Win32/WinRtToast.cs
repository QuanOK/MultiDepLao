using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MultiDepLao.Win32;

// Interop WinRT toi thieu cho toast (thay phep chieu Microsoft.Windows.SDK.NET/WinRT.Runtime, ~52 MB).
// COM tu khai bao bang [GeneratedComInterface] + StrategyBasedComWrappers + LibraryImport (khong reflection, san duong AOT).
// Moi IID va thu tu vtable lay tu header Windows SDK 10.0.26100.0:
//   Include\10.0.26100.0\winrt\windows.ui.notifications.h, windows.data.xml.dom.h, windows.foundation.collections.h,
//   inspectable.h (IInspectable), objidl.h (IAgileObject).
// Chi khai bao den phuong thuc cuoi cung can goi; cac o vtable truoc do giu cho dung thu tu (tham so nint).
// Moi loi -> COMException (HResult) nem ra cho noi goi.

/// <summary>Bo gui toast Windows cho mot AUMID (ToastNotificationManager.CreateToastNotifier(aumid)).</summary>
public sealed class WinRtToastNotifier
{
    private readonly IToastNotifier _notifier;
    private WinRtToastNotifier(IToastNotifier notifier) => _notifier = notifier;

    public static WinRtToastNotifier Create(string aumid)
    {
        using var id = new HString(aumid);
        return new(WinRt.ManagerStatics.CreateToastNotifierWithId(id.Handle));
    }

    /// <summary>XmlDocument.LoadXml + new ToastNotification(xml){Tag, Group, SuppressPopup} + Activated += onActivated.
    /// onActivated chay tren thread bat ky (thread pool cua WinRT).</summary>
    public static WinRtToast CreateToast(string xml, string tag, string group, bool suppressPopup, Action onActivated)
    {
        var doc = WinRt.Activate<IXmlDocument>("Windows.Data.Xml.Dom.XmlDocument");
        using (var h = new HString(xml)) ((IXmlDocumentIO)doc).LoadXml(h.Handle);
        var toast = WinRt.ToastFactory.CreateToastNotification(doc);
        var t2 = (IToastNotification2)toast;
        using (var h = new HString(tag)) t2.put_Tag(h.Handle);
        using (var h = new HString(group)) t2.put_Group(h.Handle);
        t2.put_SuppressPopup(suppressPopup ? (byte)1 : (byte)0);
        var handler = new ToastActivatedHandler(onActivated);
        toast.add_Activated(handler);
        return new WinRtToast(toast, handler);
    }

    public void Show(WinRtToast toast) => _notifier.Show(toast.Native);

    /// <summary>ToastNotificationManager.History.Remove(tag, group, aumid).</summary>
    public static void RemoveFromHistory(string tag, string group, string aumid)
    {
        using var t = new HString(tag);
        using var g = new HString(group);
        using var a = new HString(aumid);
        WinRt.ManagerStatics2.get_History().RemoveGroupedTagWithId(t.Handle, g.Handle, a.Handle);
    }

    /// <summary>ToastNotificationManager.History.GetHistory(aumid) -> (Tag, Group). Dung de kiem thu.</summary>
    public static List<(string Tag, string Group)> GetHistory(string aumid)
    {
        using var a = new HString(aumid);
        var view = ((IToastNotificationHistory2)WinRt.ManagerStatics2.get_History()).GetHistoryWithId(a.Handle);
        var list = new List<(string, string)>();
        uint n = view.get_Size();
        for (uint i = 0; i < n; i++)
        {
            var t2 = (IToastNotification2)view.GetAt(i);
            list.Add((HString.TakeString(t2.get_Tag()), HString.TakeString(t2.get_Group())));
        }
        return list;
    }
}

/// <summary>Mot ToastNotification da tao; giu tham chieu de doi tuong COM va handler Activated song.</summary>
public sealed class WinRtToast
{
    internal readonly IToastNotification Native;
    internal readonly ToastActivatedHandler Handler;
    internal WinRtToast(IToastNotification native, ToastActivatedHandler handler) { Native = native; Handler = handler; }
}

internal static partial class WinRt
{
    // windows.ui.notifications.h
    private static readonly Guid IID_IToastNotificationManagerStatics = new("50ac103f-d235-4598-bbef-98fe4d1a3ad4");
    private static readonly Guid IID_IToastNotificationManagerStatics2 = new("7ab93c52-0e48-4750-ba9d-1a4113981847");
    private static readonly Guid IID_IToastNotificationFactory = new("04124b20-82c6-4229-b109-fd9ed4662b53");

    private static readonly StrategyBasedComWrappers Cw = new();
    private static IToastNotificationManagerStatics? _statics;
    private static IToastNotificationManagerStatics2? _statics2;
    private static IToastNotificationFactory? _factory;

    internal static IToastNotificationManagerStatics ManagerStatics =>
        _statics ??= GetFactory<IToastNotificationManagerStatics>("Windows.UI.Notifications.ToastNotificationManager", IID_IToastNotificationManagerStatics);
    internal static IToastNotificationManagerStatics2 ManagerStatics2 =>
        _statics2 ??= GetFactory<IToastNotificationManagerStatics2>("Windows.UI.Notifications.ToastNotificationManager", IID_IToastNotificationManagerStatics2);
    internal static IToastNotificationFactory ToastFactory =>
        _factory ??= GetFactory<IToastNotificationFactory>("Windows.UI.Notifications.ToastNotification", IID_IToastNotificationFactory);

    private static T GetFactory<T>(string classId, Guid iid) where T : class
    {
        using var c = new HString(classId);
        Marshal.ThrowExceptionForHR(RoGetActivationFactory(c.Handle, iid, out var p));
        return Wrap<T>(p);
    }

    internal static T Activate<T>(string classId) where T : class
    {
        using var c = new HString(classId);
        Marshal.ThrowExceptionForHR(RoActivateInstance(c.Handle, out var p));
        return Wrap<T>(p);
    }

    /// <summary>Boc con tro (da AddRef, so huu) thanh RCW; RCW tu AddRef nen tra lai tham chieu cua ta.</summary>
    private static T Wrap<T>(nint p) where T : class
    {
        try { return (T)Cw.GetOrCreateObjectForComInstance(p, CreateObjectFlags.UniqueInstance); }
        finally { Marshal.Release(p); }
    }

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    [LibraryImport("combase.dll")]
    private static partial int RoActivateInstance(nint activatableClassId, out nint instance);
}

/// <summary>HSTRING so huu; Dispose -> WindowsDeleteString.</summary>
internal readonly ref partial struct HString
{
    public readonly nint Handle;
    public HString(string s) => Marshal.ThrowExceptionForHR(WindowsCreateString(s, (uint)s.Length, out Handle));
    public void Dispose() { if (Handle != 0) WindowsDeleteString(Handle); }

    /// <summary>Doc va giai phong HSTRING tra ve tu COM (ta so huu).</summary>
    public static string TakeString(nint h)
    {
        if (h == 0) return "";
        try { var p = WindowsGetStringRawBuffer(h, out var len); return Marshal.PtrToStringUni(p, (int)len); }
        finally { WindowsDeleteString(h); }
    }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string sourceString, uint length, out nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    private static partial nint WindowsGetStringRawBuffer(nint hstring, out uint length);
}

// ---- Giao dien COM (IID + vtable tu header SDK 10.0.26100.0) ----

/// <summary>inspectable.h</summary>
[GeneratedComInterface, Guid("AF86E2E0-B12D-4c6a-9C5A-D7AA65101E90")]
internal partial interface IInspectable
{
    void GetIids(out uint iidCount, out nint iids);
    nint GetRuntimeClassName();
    int GetTrustLevel();
}

/// <summary>objidl.h; danh dau handler la agile (WinRT goi tu thread bat ky, khong marshal).</summary>
[GeneratedComInterface, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")]
internal partial interface IAgileObject { }

/// <summary>windows.ui.notifications.h: IToastNotificationManagerStatics</summary>
[GeneratedComInterface, Guid("50ac103f-d235-4598-bbef-98fe4d1a3ad4")]
internal partial interface IToastNotificationManagerStatics : IInspectable
{
    nint CreateToastNotifier();
    IToastNotifier CreateToastNotifierWithId(nint applicationId);
}

/// <summary>windows.ui.notifications.h: IToastNotificationManagerStatics2</summary>
[GeneratedComInterface, Guid("7ab93c52-0e48-4750-ba9d-1a4113981847")]
internal partial interface IToastNotificationManagerStatics2 : IInspectable
{
    IToastNotificationHistory get_History();
}

/// <summary>windows.ui.notifications.h: IToastNotificationHistory</summary>
[GeneratedComInterface, Guid("5caddc63-01d3-4c97-986f-0533483fee14")]
internal partial interface IToastNotificationHistory : IInspectable
{
    void RemoveGroup(nint group);
    void RemoveGroupWithId(nint group, nint applicationId);
    void RemoveGroupedTagWithId(nint tag, nint group, nint applicationId);
}

/// <summary>windows.ui.notifications.h: IToastNotificationHistory2</summary>
[GeneratedComInterface, Guid("3bc3d253-2f31-4092-9129-8ad5abf067da")]
internal partial interface IToastNotificationHistory2 : IInspectable
{
    nint GetHistory();
    IVectorViewToastNotification GetHistoryWithId(nint applicationId);
}

/// <summary>windows.ui.notifications.h: IVectorView&lt;ToastNotification*&gt; (a819f3de-...);
/// vtable IVectorView_impl trong windows.foundation.collections.h: GetAt, get_Size, IndexOf, GetMany.</summary>
[GeneratedComInterface, Guid("a819f3de-60aa-5159-8407-f0a7fb1f6832")]
internal partial interface IVectorViewToastNotification : IInspectable
{
    IToastNotification GetAt(uint index);
    uint get_Size();
}

/// <summary>windows.ui.notifications.h: IToastNotifier</summary>
[GeneratedComInterface, Guid("75927b93-03f3-41ec-91d3-6e5bac1b38e7")]
internal partial interface IToastNotifier : IInspectable
{
    void Show(IToastNotification notification);
}

/// <summary>windows.ui.notifications.h: IToastNotificationFactory</summary>
[GeneratedComInterface, Guid("04124b20-82c6-4229-b109-fd9ed4662b53")]
internal partial interface IToastNotificationFactory : IInspectable
{
    IToastNotification CreateToastNotification(IXmlDocument content);
}

/// <summary>windows.ui.notifications.h: IToastNotification</summary>
[GeneratedComInterface, Guid("997e2675-059e-4e60-8b06-1760917c8b80")]
internal partial interface IToastNotification : IInspectable
{
    nint get_Content();
    void put_ExpirationTime(nint value);
    nint get_ExpirationTime();
    long add_Dismissed(nint handler);
    void remove_Dismissed(long token);
    long add_Activated(IToastActivatedHandler handler);
}

/// <summary>windows.ui.notifications.h: IToastNotification2 (boolean WinRT = 1 byte)</summary>
[GeneratedComInterface, Guid("9dfb9fd1-143a-490e-90bf-b9fba7132de7")]
internal partial interface IToastNotification2 : IInspectable
{
    void put_Tag(nint value);
    nint get_Tag();
    void put_Group(nint value);
    nint get_Group();
    void put_SuppressPopup(byte value);
}

/// <summary>windows.data.xml.dom.h: IXmlDocument (chi dung lam kieu tham so).</summary>
[GeneratedComInterface, Guid("f7f3a506-1e87-42d6-bcfb-b8c809fa5494")]
internal partial interface IXmlDocument : IInspectable { }

/// <summary>windows.data.xml.dom.h: IXmlDocumentIO</summary>
[GeneratedComInterface, Guid("6cd0e74e-ee65-4489-9ebf-ca43e87ba637")]
internal partial interface IXmlDocumentIO : IInspectable
{
    void LoadXml(nint xml);
}

/// <summary>windows.ui.notifications.h: __FITypedEventHandler_2_Windows__CUI__CNotifications__CToastNotification_IInspectable
/// = ITypedEventHandler&lt;ToastNotification*, IInspectable*&gt; uuid ab54de2d-97d9-5528-b6ad-105afe156530 (delegate: ke thua IUnknown).</summary>
[GeneratedComInterface, Guid("ab54de2d-97d9-5528-b6ad-105afe156530")]
internal partial interface IToastActivatedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComClass]
internal sealed partial class ToastActivatedHandler : IToastActivatedHandler, IAgileObject
{
    private readonly Action _callback;
    public ToastActivatedHandler(Action callback) => _callback = callback;
    public void Invoke(nint sender, nint args)
    {
        try { _callback(); } catch { /* khong de ngoai le ve phia WinRT */ }
    }
}
