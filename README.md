# Multi DépLào

Ứng dụng desktop Windows chạy cục bộ để dùng nhiều tài khoản Zalo PC cùng lúc trong một cửa sổ: thanh bên liệt kê tài khoản, vùng bên phải hiển thị cửa sổ Zalo của tài khoản đang chọn.

Multi DépLào chỉ tạo thư mục dữ liệu riêng cho mỗi tài khoản, khởi chạy/tắt tiến trình Zalo và quản lý cửa sổ từ bên ngoài. Không patch hay inject vào Zalo, không đọc nội dung tin nhắn, không tự gửi tin, không telemetry, không cần quyền admin.

**Tải bản chính thức:** [GitHub Releases](https://github.com/QuanOK/MultiDepLao/releases) (kèm mã SHA-256). Chỉ tải từ
trang này: bản ở nơi khác có thể đã bị sửa đổi. Xem [SECURITY.md](SECURITY.md).

## Yêu cầu
- Windows 10/11 x64.
- Đã cài Zalo PC bản thường tại `%LOCALAPPDATA%\Programs\Zalo`.
- Bản phát hành đã kèm sẵn .NET, không cần cài thêm.

## Chạy
- Bản cài đặt: chạy `MultiDepLao-<phiên bản>-setup-win-x64.exe` (cài cho user hiện tại, không cần admin).
- Bản portable: giải nén `MultiDepLao-<phiên bản>-portable-win-x64.zip` vào thư mục có quyền ghi, chạy `MultiDepLao\MultiDepLao.App.exe`.

## Dữ liệu
Nằm ở `%LOCALAPPDATA%\MultiDepLao` (ngoài thư mục ứng dụng). Nếu cạnh `MultiDepLao.App.exe` có file `portable.txt`
(bản portable) thì dữ liệu nằm ở `<thư mục exe>\data`; thư mục đó phải ghi được, không thì app báo lỗi và thoát.
Dữ liệu cũ `%LOCALAPPDATA%\MultiZalo` tự chuyển sang `MultiDepLao` khi mở bản cài đặt lần đầu (bản portable không chuyển).
- `config.json`: cấu hình.
- `profiles\`: dữ liệu riêng của từng tài khoản.
- `zalo-nocap\`: bản sao Zalo (không có ZaloCap.exe) dùng để chạy các tài khoản.
- `logs\`: nhật ký (không chứa nội dung Zalo).

Multi DépLào không đụng tới `%APPDATA%\ZaloData` (dữ liệu Zalo thường của bạn).

## Thao tác chính
- **Thêm tài khoản**: nút "Thêm tài khoản", rồi bấm "Bật" và đăng nhập trong cửa sổ Zalo.
- **Zalo PC hay Zalo Web**: khi thêm tài khoản chọn loại. Zalo Web (chạy bằng WebView2 trong app, đăng nhập bằng mã QR) nhẹ hơn khi dùng nhiều tài khoản nhưng không gọi thoại/video; có chấm tin mới và thông báo Windows. App tự hỏi cài WebView2 nếu máy thiếu.
- **Chọn tài khoản**: bấm trong thanh bên, `Ctrl+1..9` (theo thứ tự), hoặc `Ctrl+Tab`.
- **Sắp xếp**: kéo thả để đổi thứ tự. Chuột phải để đổi tên, đổi màu, xóa, ghim, tự bật khi mở app.
- **Thu gọn thanh bên**: nút `«`.
- **Thu nhỏ / khay hệ thống**: nút "–" thu nhỏ cửa sổ, vẫn giữ biểu tượng trên thanh tác vụ. Mở app với tham số `--tray` thì app chạy ẩn ở khay; mở lại bằng biểu tượng khay hoặc chạy lại exe.
- **Nút X**: tắt hết các Zalo do Multi DépLào khởi chạy rồi thoát.
- **Khóa**: đặt mật khẩu trong "Cài đặt"; khóa bằng nút Khóa hoặc `Ctrl+L`.
- **Quyền gọi điện**: chỉ một tài khoản gọi được tại một thời điểm; chuột phải "Chuyển quyền gọi sang tài khoản này" (cần khởi động lại hai instance).
- **Ngủ để tiết kiệm RAM**: chuột phải "Cho ngủ" để tắt Zalo của tài khoản; "Ghim" để không bị tự cho ngủ.
- **Gắn lại cửa sổ**: chuột phải "Gắn lại cửa sổ" khi cửa sổ Zalo bị lệch hoặc không hiện đúng.
- **Cập nhật Zalo**: Multi DépLào theo dõi bản cài gốc, tạo bản sao mới ở nền; tài khoản đang chạy bản cũ có dấu ⟳, dùng "Khởi động lại để cập nhật".
- **Có gì mới**: xem thay đổi của từng phiên bản ở Cài đặt → "Có gì mới" (hoặc trong Giới thiệu, hoặc chuột phải biểu tượng khay); tự hiện một lần sau khi cập nhật.

## Giới hạn chính
- Gọi thoại/video chỉ một tài khoản tại một thời điểm.
- Không có nút chụp màn hình của Zalo (dùng công cụ chụp của Windows).
- Mỗi tài khoản đã đăng nhập tốn khoảng 1,2–1,6 GB RAM.
- Nút "–" trên thanh tiêu đề Zalo không có tác dụng khi cửa sổ đang gắn.
- Mỗi tài khoản đang chạy vẫn có biểu tượng Zalo riêng ở khay; khóa app không ẩn được.
- Khóa bằng mật khẩu chỉ khóa giao diện, không mã hóa dữ liệu trên đĩa.
- Thông báo 🔔 chỉ hiện khi chính Zalo hiện popup.

Chi tiết: [docs/LIMITATIONS.md](docs/LIMITATIONS.md).

## Khởi động cùng Windows
Cài đặt → "Khởi động cùng Windows": ghi `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` giá trị `MultiDepLao` =
`"<exe>" --autostart` (mở cửa sổ bình thường; `--tray` cũ vẫn hiểu là mở ẩn khay và được tự đổi sang lệnh mới).
Registry là nguồn sự thật (không lưu trong config.json). Khi mở app: lệnh cũ của cùng exe → ghi lại; exe đã đăng ký không
còn tồn tại → trỏ sang exe hiện tại; exe đã đăng ký là bản KHÁC còn tồn tại → giữ nguyên. Gỡ bản cài đặt sẽ xóa giá trị
này nếu nó trỏ vào thư mục cài.

Zalo cài bình thường cũng tự mở cùng Windows thì có thể giành pipe cuộc gọi / ZaloCap trước tài khoản chủ quyền gọi.
Phiên `--autostart`: ~25 s sau khi tự bật tài khoản, nếu chủ quyền gọi chưa giữ pipe thì — không còn Zalo ngoài app → tự
khởi động lại chủ quyền gọi một lần; còn Zalo ngoài app → chỉ thông báo cách xử lý (app không tắt Zalo đó, không sửa mục
khởi động của Zalo). Cài đặt cảnh báo khi Run có giá trị `Zalo` (chỉ đọc).

## Giấy phép, tác giả, miễn trừ trách nhiệm
Tác giả: QuânOK – okiemquan@gmail.com. Copyright © 2026 QuânOK.

Multi DépLào là phần mềm tự do theo **GNU GPL-3.0** ([LICENSE](LICENSE)): bạn được dùng, sửa và phân phối lại, với điều kiện
bản phân phối lại cũng theo GPL-3.0 và kèm mã nguồn. Tên "Multi DépLào" và logo **không** thuộc phạm vi giấy phép: bản sửa
đổi phải đổi tên và logo, và không được giới thiệu là bản của tác giả.

Phần mềm được cung cấp "nguyên trạng", không kèm bảo đảm nào; tác giả không chịu trách nhiệm về thiệt hại phát sinh khi
sử dụng. Multi DépLào là phần mềm độc lập, không phải sản phẩm chính thức và không liên kết với Zalo / VNG; "Zalo" là
nhãn hiệu của chủ sở hữu tương ứng. Toàn văn: [build/MIEN-TRU-TRACH-NHIEM.txt](build/MIEN-TRU-TRACH-NHIEM.txt) (hiện
trong app ở Cài đặt → "Giới thiệu & bản quyền" và ở trang đầu bộ cài).

## Đóng góp
Dự án cá nhân, phát triển theo thời gian rảnh của tác giả. Báo lỗi / đề xuất: mở issue trên GitHub. Lỗ hổng bảo mật: gửi
email riêng (xem [SECURITY.md](SECURITY.md)). Pull request được chào đón; mọi đóng góp theo GPL-3.0. Ràng buộc thiết kế
phải giữ: không patch/inject Zalo, không gọi API nội bộ của Zalo, không đọc nội dung tin nhắn, không tự gửi tin, không
telemetry, không cần admin.

## Ủng hộ
Multi DépLào miễn phí và không quảng cáo. Nếu thấy hữu ích, bạn có thể mời tác giả một ly cà phê:
**Techcombank – O Kiem Quan – 7820 7088 88** (mã QR VietQR trong app: nút "Ủng hộ tác giả" ở đáy thanh bên, Cài đặt, hoặc menu khay;
ảnh `src/MultiDepLao.App/Assets/donate-qr.png`).

## Build từ mã nguồn
Cần .NET 10 SDK (Windows).

```powershell
dotnet build MultiDepLao.sln
dotnet test MultiDepLao.sln
powershell -File build\publish.ps1   # ra dist\MultiDepLao-<phiên bản>\ và file zip
```

Script publish tạo bản self-contained win-x64 single-file (ReadyToRun, không trimming), không cần admin.

## Đóng gói
Bản chính thức trên [trang phát hành](https://github.com/QuanOK/MultiDepLao/releases) do GitHub Actions build khi đẩy tag
`v<phiên bản>` (`.github/workflows/build.yml`). Tự đóng gói trên máy: cần thêm Inno Setup 6
(`winget install JRSoftware.InnoSetup --scope user`, không cần admin), rồi

```powershell
powershell -ExecutionPolicy Bypass -File build\package.ps1   # thêm -Force để làm lại cùng phiên bản
```

Phiên bản lấy từ `<Version>` trong `src\MultiDepLao.App\MultiDepLao.App.csproj`. Kết quả trong `dist\release-<phiên bản>\`:
bộ cài `MultiDepLao-<phiên bản>-setup-win-x64.exe`, bản portable `MultiDepLao-<phiên bản>-portable-win-x64.zip`, kèm
`HUONG-DAN.txt` và `SHA256SUMS.txt`. Script dừng nếu thư mục publish có dữ liệu cá nhân (`profiles\`, `config.json`, log...).
Bộ cài dùng tiếng Việt nếu có `build\Vietnamese.isl` (bản dịch không chính thức của Inno Setup), không thì tiếng Anh.

Bản sửa đổi phát hành lại phải đổi tên và logo (xem mục Giấy phép).
