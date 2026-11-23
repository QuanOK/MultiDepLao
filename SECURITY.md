# Bảo mật

## Tải đúng bản chính thức
- Nguồn chính thức duy nhất: **https://github.com/QuanOK/MultiDepLao/releases**. Bản tải ở nơi khác (nhóm chat, trang
  chia sẻ file, "bản đã sửa"…) có thể đã bị chèn mã độc, kể cả khi vẫn mang tên Multi DépLào.
- Mỗi bản phát hành có mã SHA-256 ghi trên trang phát hành. Kiểm tra trước khi chạy:
  ```powershell
  Get-FileHash .\MultiDepLao-<phiên bản>-setup-win-x64.exe -Algorithm SHA256
  ```
  Mã phải trùng với mã trên trang phát hành. Không trùng → xóa file, không chạy.
- Bản phát hành hiện **chưa ký số** (Authenticode), nên Windows SmartScreen cảnh báo lần đầu chạy. Tác giả dự định ký số
  khi có điều kiện; khi đó trang phát hành sẽ ghi rõ tên nhà phát hành trên chữ ký.
- Bản sửa đổi của người khác được phép theo GPL-3.0 nhưng phải đổi tên và logo, phải công bố mã nguồn, và không được
  giới thiệu là bản của tác giả.

## App làm gì để bảo vệ bạn
- Chạy với quyền người dùng thường, không cần admin; chỉ ghi vào `%LOCALAPPDATA%\MultiDepLao` (hoặc thư mục `data` của
  bản portable) và hai mục registry HKCU (khởi động cùng Windows, đăng ký thông báo).
- Không đọc nội dung tin nhắn, không gửi dữ liệu đi đâu, không sửa hay chèn mã vào Zalo.
- Chỉ khởi chạy Zalo.exe có chữ ký số hợp lệ của VNG; bản sao Zalo bị thay sẽ được phát hiện và chép lại từ bản cài gốc.
- Chỉ tắt những tiến trình Zalo do chính app khởi chạy (kiểm tra PID, thời điểm tạo, đường dẫn exe).
- Kiểm tra dữ liệu cấu hình khi đọc (Id tài khoản, đường dẫn) để không đọc/ghi ra ngoài thư mục dữ liệu.
- Zalo Web: chỉ cấp quyền thông báo/clipboard/lưu trữ cho miền `zalo.me`; link ra ngoài mở bằng trình duyệt mặc định.
- Bộ cài WebView2 (khi máy thiếu, chỉ sau khi bạn đồng ý) được tải từ Microsoft và kiểm chữ ký Microsoft trước khi chạy.
- Mật khẩu khóa app lưu dạng băm PBKDF2-SHA256 có salt, không lưu mật khẩu gốc. Lưu ý: đây chỉ là khóa giao diện, không
  mã hóa dữ liệu (xem [docs/LIMITATIONS.md](docs/LIMITATIONS.md)).

## Báo lỗ hổng
Nếu bạn tìm thấy lỗ hổng bảo mật, vui lòng gửi email riêng cho tác giả: **okiemquan@gmail.com** (tiêu đề bắt đầu bằng
`[Bảo mật]`), kèm phiên bản, mô tả và cách tái hiện. Xin đừng mở issue công khai trước khi lỗi được vá. Đây là dự án cá
nhân, không có cam kết thời gian phản hồi, nhưng tác giả sẽ cố gắng trả lời và ghi nhận người báo (nếu bạn muốn).

Ngoài phạm vi: lỗ hổng của chính Zalo/VNG, của Windows hay WebView2 (báo cho nhà cung cấp tương ứng); các thao tác cần
sẵn quyền ghi vào tài khoản Windows của bạn (khi đó kẻ tấn công đã chạy được mã với quyền của bạn).
