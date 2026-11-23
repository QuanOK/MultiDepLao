namespace MultiDepLao.Core;

/// <summary>
/// Bang chuoi giao dien vi/en. Moi muc khai bao mot lan (key, vi, en) nen hai bang luon dong bo key.
/// Key XAML: App nap thanh resource "S.&lt;key&gt;". Chuoi co tham so dung {0},{1}... (cung so luong o vi va en).
/// KHONG dich: log, ten tai khoan, ten app, email/tac gia.
/// </summary>
public static class Strings
{
    public static readonly IReadOnlyDictionary<string, string> Vi, En;

    static Strings()
    {
        var vi = new Dictionary<string, string>();
        var en = new Dictionary<string, string>();
        void A(string key, string v, string e) { vi.Add(key, v); en.Add(key, e); }

        // ---- thanh ben / the tai khoan
        A("Sidebar.Toggle", "Thu gọn / mở rộng thanh bên", "Collapse / expand sidebar");
        A("Sidebar.Add", "Thêm tài khoản", "Add account");
        A("Sidebar.AddShort", "Thêm", "Add");
        A("Header.Accounts", "Tài khoản ({0})", "Accounts ({0})");
        A("Header.Unread", " · {0} có tin mới", " · {0} unread");
        A("Tip.Unread", "Zalo của tài khoản này có tin chưa đọc", "This account's Zalo has unread messages");
        A("Tip.OldZalo", "Đang chạy bản Zalo cũ — khởi động lại để dùng bản mới", "Running an old Zalo version - restart to use the new one");
        A("Tip.Pinned", "Đã ghim (không tự cho ngủ)", "Pinned (never auto-slept)");
        A("Tip.CallOwner", "Đang giữ quyền gọi điện", "Holds the call right");
        A("Tip.CaptureOn", "Đang giữ quyền chụp màn hình của Zalo", "Holds the Zalo screen capture right");
        A("Tip.CaptureOff", "Giữ quyền chụp — khởi động lại tài khoản để dùng", "Holds the capture right - restart the account to use it");

        // ---- trang thai
        A("State.Stopped", "Ngủ", "Asleep");
        A("State.Starting", "Đang khởi động", "Starting");
        A("State.Running", "Đang chạy", "Running");
        A("State.Stopping", "Đang tắt", "Stopping");
        A("State.Error", "Lỗi", "Error");
        A("State.NoMessages", "không nhận tin", "no messages");
        A("State.UnreadCount", "{0} tin mới", "{0} new");
        A("State.UnreadSome", "Có tin mới", "New messages");

        // ---- thanh lenh
        A("Cmd.Lock", "Khóa (Ctrl+L)", "Lock (Ctrl+L)");
        A("Cmd.Settings", "Cài đặt", "Settings");
        A("Cmd.Start", "Bật", "Start");
        A("Cmd.Sleep", "Cho ngủ", "Sleep");
        A("Cmd.Restart", "Khởi động lại", "Restart");
        A("Cmd.RestartUpdate", "Khởi động lại để cập nhật", "Restart to update");
        A("Cmd.Reattach", "Gắn lại", "Reattach");
        A("Cmd.More", "Thêm thao tác", "More actions");
        A("Chip.Call", "Quyền gọi", "Call right");
        A("Chip.Cap", "Quyền chụp", "Capture");
        A("Chip.CapOff", "Chụp: cần khởi động lại", "Capture: restart needed");
        A("Chip.Old", "Bản Zalo cũ", "Old Zalo");
        A("Chip.Pin", "Ghim", "Pinned");

        // ---- menu chuot phai
        A("Menu.SwitchCall", "Chuyển quyền gọi sang tài khoản này", "Move call right to this account");
        A("Menu.CapSet", "Dùng chụp màn hình ở tài khoản này", "Use screen capture on this account");
        A("Menu.CapClear", "Bỏ quyền chụp màn hình", "Remove screen capture right");
        A("Menu.Reattach", "Gắn lại cửa sổ", "Reattach window");
        A("Menu.AutoStart", "Tự bật khi mở app", "Start when app opens");
        A("Menu.Pin", "Ghim (không tự cho ngủ)", "Pin (never auto-sleep)");
        A("Menu.Rename", "Đổi tên", "Rename");
        A("Menu.Color", "Đổi màu", "Change color");
        A("Menu.AvatarSet", "Đổi ảnh đại diện…", "Change avatar…");
        A("Menu.AvatarClear", "Bỏ ảnh đại diện", "Remove avatar");
        A("Menu.Delete", "Xóa", "Delete");

        // ---- vung phai
        A("Info.NoneTitle", "Chưa chọn tài khoản", "No account selected");
        A("Info.NoneDetail", "Chọn một tài khoản ở thanh bên hoặc thêm tài khoản mới.", "Pick an account in the sidebar or add a new one.");
        A("Info.Stopped", "Tài khoản đang ngủ (đã tắt tiến trình, không nhận tin trên PC; điện thoại vẫn nhận).\nBấm Bật để khởi động lại, khoảng 10 giây, không phải đăng nhập lại.",
            "This account is asleep (process closed, no messages on this PC; your phone still receives them).\nClick Start to wake it, about 10 seconds, no need to log in again.");
        A("Info.Starting", "Đang khởi động Zalo...", "Starting Zalo...");
        A("Info.Opening", "Đang mở Zalo…", "Opening Zalo…");
        A("Info.WaitWindow", "Đang chờ cửa sổ Zalo xuất hiện...", "Waiting for the Zalo window...");
        A("Info.Stopping", "Đang tắt...", "Stopping...");
        A("Info.Error", "Lỗi: {0}\nBấm Bật để thử lại.", "Error: {0}\nClick Start to retry.");
        A("Info.Unknown", "không rõ", "unknown");

        // ---- footer sidebar
        A("Footer.RamHead", "RAM · {0}/{1} đang chạy", "RAM · {0}/{1} running");
        A("Footer.RamHeadWeb", "RAM · PC {0}/{1} · Web {2}", "RAM · PC {0}/{1} · Web {2}");
        A("Footer.RingTip", "Tổng RAM: {0}", "Total RAM: {0}");
        A("Footer.Free", "Máy còn trống {0}", "{0} free on this PC");
        A("Footer.None", "Chưa có tài khoản nào đang chạy", "No account is running");
        A("Footer.More", "+{0} khác · {1}", "+{0} more · {1}");
        A("Footer.VersionTip", "Giới thiệu & bản quyền", "About & copyright");
        A("Footer.AboutTip", "{0} {1} – Giới thiệu", "{0} {1} – About");
        A("Footer.Copy", "Đang sao chép bản Zalo: {0}%", "Copying Zalo: {0}%");
        A("Footer.UpdateNote", "Zalo đã cập nhật {0} — khởi động lại tài khoản để dùng bản mới", "Zalo updated to {0} - restart accounts to use the new version");
        A("Footer.Slept", "Đã cho '{0}' ngủ để nhường RAM", "'{0}' put to sleep to free up RAM");
        A("Footer.CallUnknown", "Không xác định tài khoản giữ quyền gọi. Dùng \"Chuyển quyền gọi\" để đặt lại.",
            "Can't tell which account holds the call right. Use \"Move call right\" to reset it.");
        A("Footer.CallExternal", "Quyền gọi đang do Zalo ngoài {0} giữ. Thoát Zalo đó để dùng gọi điện trong {0}.",
            "The call right is held by a Zalo running outside {0}. Quit that Zalo to make calls in {0}.");

        // ---- khay
        A("Tray.Open", "Mở {0}", "Open {0}");
        A("Tray.Lock", "Khóa", "Lock");
        A("Tray.About", "Giới thiệu", "About");
        A("Tray.WhatsNew", "Có gì mới", "What's new");
        A("Tray.Donate", "Ủng hộ tác giả", "Support the author");
        A("Tray.Exit", "Thoát", "Exit");
        A("Tray.NewMsg", "{0} — có tin mới", "{0} — new message");
        A("Tray.NewMsgFrom", "{0} — có tin mới: {1}", "{0} — new message: {1}");

        // ---- man hinh khoa
        A("Lock.Title", "Multi DépLào đã khóa", "Multi DépLào is locked");
        A("Lock.Unlock", "Mở khóa", "Unlock");
        A("Lock.TooMany", "Sai quá nhiều lần. Thử lại sau {0} giây.", "Too many attempts. Try again in {0} seconds.");
        A("Lock.Wrong", "Sai mật khẩu.", "Wrong password.");

        // ---- nut chung
        A("Dlg.Yes", "Có", "Yes");
        A("Dlg.No", "Không", "No");
        A("Dlg.OK", "OK", "OK");
        A("Dlg.Agree", "Đồng ý", "OK");
        A("Dlg.Cancel", "Hủy", "Cancel");
        A("Dlg.Default", "Mặc định", "Default");
        A("Dlg.Close", "Đóng", "Close");
        A("Dlg.Save", "Lưu", "Save");

        // ---- hanh dong / hop thoai tai khoan
        A("Err.Action", "{0}: lỗi - {1}", "{0}: error - {1}");
        A("Act.ReattachFail", "Chưa gắn lại được: chưa thấy cửa sổ Zalo của tài khoản này. Thử lại sau vài giây.",
            "Couldn't reattach: this account's Zalo window wasn't found. Try again in a few seconds.");
        A("Act.LimitTitle", "Giới hạn tài khoản chạy", "Running account limit");
        A("Act.LimitMsg", "Đã đạt giới hạn {0} tài khoản chạy cùng lúc và không còn tài khoản nào có thể cho ngủ (ghim / gọi điện / đang chọn). Vẫn bật?",
            "The limit of {0} accounts running at once has been reached and no account can be put to sleep (pinned / call owner / selected). Start anyway?");
        A("Act.StopFail", "Không tắt được tài khoản '{0}': {1}", "Couldn't stop '{0}': {1}");
        A("Act.RenamePrompt", "Tên hiển thị mới cho tài khoản {0}:", "New display name for account {0}:");
        A("Act.ColorFor", "Màu cho tài khoản '{0}':", "Color for account '{0}':");
        A("Act.AvatarPick", "Chọn ảnh đại diện", "Choose an avatar");
        A("Act.AvatarFilter", "Ảnh|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff", "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff");
        A("Act.AvatarTitle", "Ảnh đại diện", "Avatar");
        A("Act.AvatarFail", "Không đọc được ảnh này. Hãy chọn một tệp ảnh khác.", "Couldn't read this image. Pick a different file.");
        A("Act.DeleteOnlyStopped", "Chỉ xóa được tài khoản đã tắt.", "Only stopped accounts can be deleted.");
        A("Act.DeleteTitle", "Xóa tài khoản", "Delete account");
        A("Act.DeleteMsg", "Xóa tài khoản '{0}' sẽ xóa toàn bộ dữ liệu của nó (đăng nhập, tin nhắn đã lưu trong profile). Gõ lại đúng tên hiển thị để xác nhận:",
            "Deleting '{0}' removes all of its data (login, messages stored in the profile). Type the exact display name to confirm:");
        A("Act.AddPrompt", "Tên hiển thị cho tài khoản mới:", "Display name for the new account:");
        A("Act.AddFail", "Không thêm được: {0}", "Couldn't add: {0}");
        A("Act.AutoStartFailTitle", "Khởi động cùng Windows", "Start with Windows");
        A("Act.AutoStartFail", "Không thể thay đổi thiết lập khởi động cùng Windows trên máy này.", "Couldn't change the Start with Windows setting on this computer.");
        A("Act.CloseTitle", "Đóng {0}", "Close {0}");
        A("Act.CloseMsg", "Đóng {0} sẽ tắt {1} tài khoản đang chạy. Tiếp tục?", "Closing {0} will stop {1} running account(s). Continue?");
        A("Act.StoppingTitle", "Đang tắt tài khoản", "Stopping accounts");
        A("Act.StoppingProgress", "Đang tắt tài khoản: {0}/{1}", "Stopping accounts: {0}/{1}");

        // ---- quyen goi
        A("Call.Title", "Chuyển quyền gọi", "Move call right");
        A("Call.Confirm", "Chuyển quyền gọi điện sang '{0}'.\n\n", "Move the call right to '{0}'.\n\n");
        A("Call.TargetRestart", "Tài khoản '{0}' sẽ được khởi động lại.\n", "'{0}' will be restarted.\n");
        A("Call.OthersRestart", "Các tài khoản sau sẽ bị tắt rồi mở lại: {0}.\n", "These accounts will be closed and reopened: {0}.\n");
        A("Call.ConfirmTail", "\nQuá trình mất khoảng 10–20 giây. Cuộc gọi đang diễn ra sẽ bị ngắt. Tiếp tục?",
            "\nThis takes about 10-20 seconds. Any ongoing call will be cut off. Continue?");
        A("Call.Busy", "Đang chuyển quyền gọi...", "Moving call right...");
        A("Call.Done", "Đã chuyển quyền gọi sang '{0}' ({1} giây).", "Call right moved to '{0}' ({1} s).");
        A("Call.Fail", "Không chuyển được quyền gọi: {0}", "Couldn't move the call right: {0}");
        A("Call.FailHint", "\nHãy thoát Zalo bên ngoài {0} rồi thử lại.", "\nQuit the Zalo running outside {0} and try again.");
        A("Call.ReasonForeign", "pipe do Zalo ngoài {0} giữ", "call pipe held by a Zalo outside {0}");
        A("Call.ReasonStartFail", "không khởi động được tài khoản đích", "couldn't start the target account");
        A("Call.ReasonNoPipe", "pipe cuộc gọi không xuất hiện sau khi khởi động", "call pipe didn't appear after start");

        // ---- quyen chup
        A("Cap.Title", "Quyền chụp màn hình", "Screen capture right");
        A("Cap.Foreign", "Đang có Zalo ngoài {0} mở — tắt Zalo đó trước.", "A Zalo running outside {0} is open - close it first.");
        A("Cap.RestartMsg", "'{0}' sẽ được khởi động lại (~30–50 giây) để bật chụp màn hình của Zalo.", "'{0}' will restart (~30-50 s) to enable Zalo screen capture.");
        A("Cap.OnStartMsg", "'{0}' sẽ có chụp màn hình khi được bật.", "'{0}' will get screen capture when started.");
        A("Cap.OldLoses", " '{0}' sẽ mất nút chụp (không cần khởi động lại).", " '{0}' will lose its capture button (no restart needed).");
        A("Cap.CallHolderNote", "\nTài khoản này đang giữ quyền gọi: các tài khoản khác đang chạy sẽ được tắt rồi mở lại để giữ quyền gọi.",
            "\nThis account holds the call right: other running accounts will be closed and reopened to keep it.");
        A("Cap.HotkeyNote", "\nLưu ý: phím tắt chụp (Alt+Ctrl+S) chỉ dùng được ở tài khoản giữ quyền gọi; ở tài khoản này hãy bấm nút chụp bằng chuột.",
            "\nNote: the capture hotkey (Alt+Ctrl+S) only works on the call-right account; on this account, click the capture button with the mouse.");
        A("Cap.Continue", "\n\nTiếp tục?", "\n\nContinue?");
        A("Cap.ClearConfirm", "Bỏ quyền chụp màn hình của '{0}'? Nút chụp sẽ mất ở tài khoản này (không cần khởi động lại).",
            "Remove the screen capture right from '{0}'? Its capture button will disappear (no restart needed).");
        A("Cap.Busy", "Đang chuyển quyền chụp màn hình...", "Moving screen capture right...");
        A("Cap.DoneOn", "Đã bật chụp màn hình ở '{0}'.", "Screen capture enabled on '{0}'.");
        A("Cap.DoneOff", "Đã bỏ quyền chụp màn hình.", "Screen capture right removed.");
        A("Cap.Fail", "Không chuyển được quyền chụp màn hình: {0}", "Couldn't move the screen capture right: {0}");
        A("Cap.Line", "Quyền chụp màn hình: {0}", "Screen capture right: {0}");
        A("Cap.None", "Chưa có", "None");

        // ---- tien trinh chuyen quyen (SwitchProgressText)
        A("Prog.TargetGeneric", "tài khoản đích", "the target account");
        A("Prog.AlreadyCall", "Tài khoản này đã giữ quyền gọi", "This account already holds the call right");
        A("Prog.StopTargetRestart", "Đang tắt {0} để khởi động lại", "Stopping {0} to restart it");
        A("Prog.StartTarget", "Đang khởi động {0}", "Starting {0}");
        A("Prog.CapRemoved", "Đã bỏ quyền chụp", "Capture right removed");
        A("Prog.NoChange", "Không có gì thay đổi", "Nothing changed");
        A("Prog.Reopen", "Đang mở lại {0}", "Reopening {0}");
        A("Prog.StopRelease", "Đang tắt {0} để nhả quyền gọi", "Stopping {0} to release the call right");
        A("Prog.ReclaimCap", "Đang thu quyền chụp của {0}", "Taking back the capture right of {0}");
        A("Prog.RestartCap", "Đang khởi động lại {0} để nhận quyền chụp", "Restarting {0} to get the capture right");
        A("Prog.WillCap", "{0} sẽ có quyền chụp khi được bật", "{0} will get the capture right when started");

        // ---- cai dat
        A("Set.Appearance", "Giao diện", "Appearance");
        A("Set.ThemeSystem", "Theo Windows", "Follow Windows");
        A("Set.ThemeLight", "Sáng", "Light");
        A("Set.ThemeDark", "Tối", "Dark");
        A("Set.Accent", "Màu chủ đạo", "Accent color");
        A("Set.AccentTeal", "Xanh lục", "Green");
        A("Set.AccentBlue", "Xanh dương", "Blue");
        A("Set.AccentViolet", "Tím", "Purple");
        A("Set.AccentAmber", "Cam đất", "Orange");
        A("Set.Language", "Ngôn ngữ / Language", "Ngôn ngữ / Language");
        A("Set.MaxTitle", "Số tài khoản chạy cùng lúc tối đa", "Max accounts running at once");
        A("Set.MaxAuto", "Tự động (hiện {0})", "Automatic (now {0})");
        A("Set.MaxManual", "Nhập số:", "Enter number:");
        A("Set.MaxDesc", "Mỗi tài khoản đã đăng nhập tốn khoảng 1,1–1,6 GB RAM. Giảm giới hạn không tắt ngay tài khoản đang chạy, chỉ áp dụng từ lần bật kế tiếp.",
            "Each logged-in account uses about 1.1-1.6 GB of RAM. Lowering the limit doesn't stop running accounts; it applies from the next start.");
        A("Set.MaxRunningNow", " (Hiện đang chạy {0}.)", " ({0} running now.)");
        A("Set.AutoSleep", "Tự cho ngủ tài khoản không dùng sau", "Auto-sleep unused accounts after");
        A("Set.Minutes", "phút", "min");
        A("Set.AutoSleepDesc", "Không áp dụng cho tài khoản ghim, tài khoản gọi điện và tài khoản đang chọn. Tài khoản ngủ không nhận tin trên PC.",
            "Doesn't apply to pinned accounts, the call-right account or the selected account. Sleeping accounts don't receive messages on this PC.");
        A("Set.Startup", "Khởi động", "Startup");
        A("Set.AutoStartWin", "Khởi động cùng Windows", "Start with Windows");
        A("Set.ZaloRunWarn", "Zalo cũng đang tự khởi động cùng Windows và có thể giành quyền gọi. Nên tắt Zalo trong Task Manager › Startup apps.",
            "Zalo also starts with Windows and may take over calls. Consider turning Zalo off in Task Manager › Startup apps.");
        A("Call.AutoFixTitle", "Quyền gọi", "Calling");
        A("Call.AutoFixMsg",
            "Zalo cài ngoài {0} cũng mở cùng Windows và đã giữ quyền gọi/chụp màn hình trước. Muốn gọi ở tài khoản {1}: thoát Zalo đó (chuột phải biểu tượng Zalo ở khay › Thoát) rồi chuột phải tài khoản › Chuyển quyền gọi sang tài khoản này. Để không lặp lại: tắt Zalo trong Task Manager › Startup apps.",
            "A Zalo installed outside {0} also starts with Windows and took calling/screen capture first. To call on account {1}: quit that Zalo (right-click the Zalo tray icon › Exit), then right-click the account › Move call right to this account. To avoid this next time: turn Zalo off in Task Manager › Startup apps.");
        A("Set.StartupDesc", "Mở Multi DépLào khi đăng nhập Windows. Tài khoản đặt 'tự bật' sẽ tự mở.",
            "Open Multi DépLào when you sign in to Windows. Accounts set to 'start when app opens' open automatically.");
        A("Set.PcNotify", "Thông báo kín cho tài khoản Zalo PC", "Private notifications for Zalo PC accounts");
        A("Set.PcNotifyDesc",
            "Multi DépLào tự báo \"<tài khoản> có tin nhắn mới\" mà không kèm nội dung tin. Nên tắt thông báo của Zalo trong Windows để không bị báo trùng.",
            "Multi DépLào shows \"<account> has new messages\" without the message text. Turn off Zalo's notifications in Windows to avoid duplicates.");
        A("Set.PcNotifyOpen", "Mở cài đặt thông báo của Windows", "Open Windows notification settings");
        A("Pc.NewMessages", "{0} có {1} tin nhắn mới", "{0} has {1} new messages");
        A("Set.AboutButton", "Giới thiệu & bản quyền", "About & copyright");

        // ---- khoa app
        A("Lk.Title", "Khóa app", "App lock");
        A("Lk.Enable", "Bật khóa", "Enable lock");
        A("Lk.OnStart", "Khóa khi mở app", "Lock when app opens");
        A("Lk.OnMinimize", "Khóa khi thu nhỏ cửa sổ", "Lock when window is minimized");
        A("Lk.IdleBefore", "Tự khóa sau", "Auto-lock after");
        A("Lk.IdleAfter", "phút không thao tác (0 = tắt)", "min idle (0 = off)");
        A("Lk.StatusSet", "Trạng thái: đã đặt mật khẩu", "Status: password set");
        A("Lk.StatusNone", "Trạng thái: chưa đặt mật khẩu", "Status: no password");
        A("Lk.Set", "Đặt mật khẩu", "Set password");
        A("Lk.Change", "Đổi mật khẩu", "Change password");
        A("Lk.Remove", "Bỏ mật khẩu", "Remove password");
        A("Lk.TooManyWrong", "Sai mật khẩu quá nhiều lần, thử lại sau {0} giây.", "Too many wrong passwords, try again in {0} seconds.");
        A("Lk.NewPw", "Mật khẩu mới (tối thiểu 6 ký tự):", "New password (at least 6 characters):");
        A("Lk.RePw", "Nhập lại mật khẩu:", "Re-enter password:");
        A("Lk.MinLen", "Mật khẩu tối thiểu 6 ký tự.", "Password must be at least 6 characters.");
        A("Lk.Mismatch", "Hai lần nhập không khớp.", "The two entries don't match.");
        A("Lk.SaveFail", "Không lưu được mật khẩu.", "Couldn't save the password.");
        A("Lk.CurPw", "Mật khẩu hiện tại:", "Current password:");
        A("Lk.NewMinLen", "Mật khẩu mới tối thiểu 6 ký tự.", "New password must be at least 6 characters.");
        A("Lk.NewMismatch", "Hai lần nhập mới không khớp.", "The new entries don't match.");
        A("Lk.ReNewPw", "Nhập lại mật khẩu mới:", "Re-enter new password:");
        A("Lk.CurWrong", "Mật khẩu hiện tại không đúng.", "Current password is wrong.");
        A("Lk.Desc", "Khóa chỉ che giao diện Multi DépLào, không mã hóa dữ liệu Zalo trên máy. Thông báo tin mới của Zalo vẫn có thể hiện khi đang khóa. " +
            "Quên mật khẩu: thoát app rồi xóa file app.lock trong thư mục dữ liệu MultiDepLao.",
            "The lock only covers the Multi DépLào window; it doesn't encrypt Zalo data on this PC. Zalo new-message notifications may still appear while locked. " +
            "Forgot the password: quit the app and delete app.lock in the MultiDepLao data folder.");

        // ---- gioi thieu
        A("About.Title", "Giới thiệu", "About");
        A("About.Heading", "Giới thiệu & bản quyền", "About & copyright");
        A("About.Version", "Phiên bản {0}", "Version {0}");
        A("About.Tagline", "Chạy nhiều tài khoản Zalo PC trong một cửa sổ.", "Run multiple Zalo PC accounts in one window.");
        A("About.Author", "Tác giả: {0} — ", "Author: {0} — ");
        A("About.PrivacyTitle", "Quyền riêng tư", "Privacy");
        A("About.Privacy", "• Không đọc nội dung tin nhắn.\n• Không gửi dữ liệu ra ngoài, không kết nối Internet.\n• Không sửa và không chèn mã vào Zalo.",
            "• Doesn't read message content.\n• Sends no data out and makes no Internet connection.\n• Doesn't modify Zalo or inject code into it.");
        A("About.DisclaimerTitle", "Miễn trừ trách nhiệm", "Disclaimer");
        A("About.Disclaimer",
            "PHẦN MỀM ĐƯỢC CUNG CẤP \"NGUYÊN TRẠNG\", KHÔNG KÈM BẤT KỲ BẢO ĐẢM NÀO, RÕ RÀNG HAY NGỤ Ý. TÁC GIẢ KHÔNG CHỊU TRÁCH NHIỆM VỀ BẤT KỲ THIỆT HẠI NÀO PHÁT SINH TỪ VIỆC SỬ DỤNG PHẦN MỀM, KỂ CẢ MẤT DỮ LIỆU HAY TÀI KHOẢN ZALO BỊ HẠN CHẾ.\n" +
            "\n" +
            "• Multi DépLào là phần mềm độc lập, không phải sản phẩm chính thức và không liên kết với Zalo / VNG.\n" +
            "• \"Zalo\" là nhãn hiệu của chủ sở hữu tương ứng.\n" +
            "• Multi DépLào chạy ứng dụng Zalo PC gốc do người dùng tự cài đặt; người dùng tự chịu trách nhiệm tuân thủ điều khoản sử dụng của Zalo.",
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. THE AUTHOR IS NOT LIABLE FOR ANY DAMAGES ARISING FROM THE USE OF THE SOFTWARE, INCLUDING DATA LOSS OR RESTRICTIONS ON ZALO ACCOUNTS.\n" +
            "\n" +
            "• Multi DépLào is independent software, not an official product of and not affiliated with Zalo / VNG.\n" +
            "• \"Zalo\" is a trademark of its respective owner.\n" +
            "• Multi DépLào runs the original Zalo PC app installed by the user; users are responsible for complying with Zalo's terms of use.");
        A("About.SourceTitle", "Mã nguồn & bản chính thức", "Source code & official builds");
        A("About.SourceNote", "Chỉ tải Multi DépLào từ trang này. Bản tải ở nơi khác có thể đã bị sửa đổi.", "Only download Multi DépLào from this page. Copies from elsewhere may have been modified.");
        A("About.LicenseLine", "Giấy phép: GNU GPL-3.0 — mã nguồn: ", "License: GNU GPL-3.0 — source code: ");
        A("About.TrademarkNote", "Tên \"Multi DépLào\" và logo không thuộc phạm vi giấy phép; bản đã chỉnh sửa phải dùng tên khác.", "The name \"Multi DépLào\" and the logo are not covered by the license; modified versions must use a different name.");
        A("About.DataMode","Dữ liệu: {0}\nChế độ: {1}", "Data: {0}\nMode: {1}");
        A("About.ModePortable", "Portable", "Portable");
        A("About.ModeInstalled", "Cài đặt", "Installed");

        // ---- ung ho tac gia
        A("Rel.Title", "Có gì mới", "What's new");
        A("Rel.Version", "Phiên bản {0} · {1}", "Version {0} · {1}");
        A("Rel.Current", " (đang dùng)", " (current)");
        A("Rel.ShowAll", "Xem tất cả phiên bản", "Show all versions");
        A("Rel.New", "Mới", "New");
        A("Rel.Changed", "Thay đổi", "Changed");
        A("Rel.Fixed", "Sửa lỗi", "Fixed");
        A("Donate.Title", "Ủng hộ tác giả", "Support the author");
        A("Donate.Text", "Multi DépLào miễn phí và không quảng cáo. Nếu thấy hữu ích, bạn có thể mời tác giả một ly cà phê bằng cách quét mã QR bằng app ngân hàng.",
            "Multi DépLào is free and ad-free. If you find it useful, you can buy the author a coffee by scanning the QR code with your banking app.");
        A("Donate.Copy", "Sao chép số tài khoản", "Copy account number");
        A("Donate.Copied", "Đã sao chép", "Copied");
        A("Donate.CopyFail", "Không sao chép được", "Could not copy");
        A("Donate.Section", "Ủng hộ", "Support");

        // ---- khoi dong / loi he thong
        A("Boot.DataRootError", "Không ghi được dữ liệu vào thư mục:\n{0}\n\n({1})\n\nChế độ portable cần thư mục có quyền ghi. Hãy giải nén vào thư mục bạn có quyền ghi, ví dụ Tài liệu hoặc Desktop, không đặt trong Program Files.",
            "Couldn't write data to the folder:\n{0}\n\n({1})\n\nPortable mode needs a writable folder. Extract it somewhere you can write to, such as Documents or Desktop, not in Program Files.");
        A("Boot.QuitOld", "Hãy thoát MultiZalo (bản cũ) rồi mở lại Multi DépLào.", "Quit MultiZalo (the old version), then reopen Multi DépLào.");
        A("Boot.MigrateFail", "Chưa chuyển được dữ liệu sang thư mục mới ({0}). App vẫn dùng thư mục cũ; sẽ thử lại lần sau.",
            "Couldn't move data to the new folder ({0}). The app keeps using the old folder and will retry next time.");

        // ---- loai tai khoan: Zalo PC / Zalo Web
        A("Kind.Pc", "Zalo PC", "Zalo PC");
        A("Kind.Web", "Zalo Web", "Zalo Web");
        A("Kind.PcShort", "PC", "PC");
        A("Kind.WebShort", "Web", "Web");
        A("Kind.PcDesc", "Đủ tính năng: gọi thoại/video, chụp màn hình. Tốn khoảng 1–1,6 GB RAM mỗi tài khoản.",
            "Full features: voice/video calls and screen capture. Uses about 1-1.6 GB of RAM per account.");
        A("Kind.WebDesc", "Nhẹ hơn khi dùng nhiều tài khoản: mỗi tài khoản Web thêm khoảng 0,5 GB RAM. Đăng nhập bằng mã QR. Không gọi thoại/video, không có công cụ chụp màn hình của Zalo.",
            "Lighter when you use many accounts: each extra Web account adds about 0.5 GB of RAM. Sign in with a QR code. No voice/video calls and no Zalo screen capture tool.");
        A("Kind.Choose", "Chọn loại tài khoản", "Choose account type");
        A("Web.InstallAsk", "Zalo Web cần thành phần WebView2 của Microsoft (miễn phí). Máy bạn chưa có. Tải và cài ngay? (cần mạng, khoảng 1–2 phút)",
            "Zalo Web needs Microsoft's WebView2 component (free). It isn't on this computer. Download and install it now? (needs internet, about 1-2 minutes)");
        A("Web.Installing", "Đang cài WebView2…", "Installing WebView2...");
        A("Web.InstallDone", "Đã cài xong WebView2.", "WebView2 is installed.");
        A("Web.InstallFail", "Không cài được WebView2: {0}", "Couldn't install WebView2: {0}");
        A("Web.FailNoNetwork", "không tải được, hãy kiểm tra kết nối mạng", "download failed, check your internet connection");
        A("Web.FailSignature", "tệp tải về không đúng chữ ký của Microsoft", "the downloaded file isn't signed by Microsoft");
        A("Web.FailExit", "trình cài đặt báo lỗi (mã {0})", "the installer reported an error (code {0})");
        A("Web.FailTimeout", "quá thời gian chờ (5 phút)", "timed out (5 minutes)");
        A("Web.FailNotDetected", "cài xong nhưng chưa nhận ra thành phần, hãy thử khởi động lại app", "installed but not detected, try restarting the app");
        A("Web.FailOther", "lỗi không xác định", "unknown error");
        A("Web.NewMessage", "{0} có tin nhắn mới", "{0} has a new message");
        A("Web.NoRuntime", "Chưa có WebView2 trên máy. Hãy thêm lại tài khoản Zalo Web để cài, hoặc cài WebView2 của Microsoft.",
            "WebView2 isn't installed. Add a Zalo Web account again to install it, or install Microsoft WebView2.");
        A("Web.InitFail", "Không mở được Zalo Web ({0}).", "Couldn't open Zalo Web ({0}).");
        A("Web.Crashed", "Zalo Web gặp sự cố ({0}). Bấm Khởi động lại để tải lại.", "Zalo Web crashed ({0}). Press Restart to reload.");
        A("Web.AddFailHint", "Chưa tạo tài khoản Zalo Web. Bạn có thể chọn Zalo PC thay thế.", "No Zalo Web account was created. You can choose Zalo PC instead.");
        A("Web.InstallTitle", "Cài WebView2", "Install WebView2");
        A("Web.InstallDetailDownload", "Đang tải bộ cài…", "Downloading the installer...");
        A("Web.InstallDetailVerify", "Đang kiểm tra chữ ký…", "Verifying the signature...");
        A("Web.InstallDetailInstall", "Đang cài đặt…", "Installing...");

        Vi = vi; En = en;
    }
}
