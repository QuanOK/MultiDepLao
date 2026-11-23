# Giới hạn đã biết

Đo trên Windows 11, Zalo PC 26.11.x. Ký hiệu: [V] đã kiểm bằng thử nghiệm, [S] dự đoán/chưa kiểm đủ.

## Zalo PC
- [V] **Gọi thoại/video chỉ ở MỘT tài khoản tại một thời điểm** (Zalo dùng kênh gọi chung toàn máy). Các tài khoản khác
  bấm gọi không phản hồi, cuộc gọi đổ về điện thoại. Đổi tài khoản gọi: chuột phải → "Chuyển quyền gọi…" (khởi động lại 2
  tài khoản).
- [V] **Công cụ chụp màn hình của Zalo chỉ ở MỘT tài khoản**: chuột phải → "Dùng chụp màn hình ở tài khoản này". Phím tắt
  Alt+Ctrl+S chỉ chạy ở tài khoản giữ quyền gọi. Thay thế: Win+Shift+S của Windows.
- [V] RAM khoảng 1,2–1,6 GB mỗi tài khoản đã đăng nhập. Máy 16 GB chạy thoải mái khoảng 5–6 tài khoản.
- [V] Cửa sổ Zalo được "dán" lên vùng hiển thị (không nhúng thật, vì nhúng làm mất gõ phím): có thể lệch trong tích tắc
  khi kéo/đổi kích thước nhanh. Nút "–" trên thanh tiêu đề Zalo không có tác dụng khi đang gắn; không kéo được mép cửa sổ
  Zalo (kéo mép trái để đổi độ rộng thanh bên).
- [V] Cửa sổ của tài khoản không được chọn nằm ngoài màn hình. Nếu app bị tắt đột ngột, cửa sổ đó nằm ngoài màn hình
  tới lần mở app sau.
- [V] Vùng hiển thị cần cao tối thiểu ~680 px (cửa sổ app ~732 px); màn hình thấp có thể chật.
- [V] Mở lại app sau khi thu nhỏ đôi khi nghe tiếng "ting" của Windows hoặc vùng Zalo chớp nền xám rất ngắn.
- [V] Zalo không báo tin của hội thoại đang mở. Mẹo: trước khi chuyển tài khoản, để tài khoản đó ở hội thoại ít dùng.
- [V] Có tài khoản Zalo không bao giờ hiện popup tin nhắn trên PC dù đã bật thông báo (do phía Zalo). Tài khoản đó vẫn có
  chấm tin chưa đọc trên thanh bên, nhưng bấm thông báo chỉ chuyển đúng tài khoản, không mở đúng cuộc trò chuyện.
- [V] Chấm tin chưa đọc dựa trên huy hiệu của Zalo (số 1–5 hoặc "5+"), trễ tới ~5 s; 30–70 s đầu sau khi mở Zalo chưa có.
  Nếu Zalo đổi giao diện, chấm có thể ngừng chạy; tắt bằng `"unreadBadge": false` trong `config.json`.
- [S] "Thông báo kín" (Cài đặt): trễ ~4–6 s, ~30 s đầu sau khi tài khoản chạy không báo; nên tắt thông báo của Zalo trong
  Windows để không bị báo trùng.
- [S] Zalo tự cập nhật: app theo dõi bản cài gốc, tạo bản sao mới ở nền và đánh dấu ⟳ tài khoản chạy bản cũ.
- [V] Từ 0.4.8 app chỉ chạy Zalo.exe có chữ ký số hợp lệ của VNG. Nếu Zalo đổi tên nhà phát hành trên chữ ký, tài khoản
  PC sẽ báo lỗi cho tới khi app được cập nhật.

## Zalo Web (WebView2)
- [V] Không gọi thoại/video, không có công cụ chụp màn hình của Zalo.
- [V] Đăng nhập bằng mã QR; tắt/mở app vẫn còn đăng nhập. Dùng song song Zalo PC và Web cùng tài khoản không bị đăng xuất.
- [V] RAM: mỗi tài khoản Web thêm ~0,45 GB (tài khoản PC ~1 GB).
- [V] Tin chưa đọc lấy từ biểu tượng trang (favicon), tối đa "5+"; máy khác font/DPI có thể chỉ hiện "Có tin mới".
- [V] Từ 0.4.8 trang chính chỉ được ở miền của Zalo (`zalo.me`, `zaloapp.com`); link ra ngoài mở bằng trình duyệt mặc định.
- [S] Lịch sử tin cũ có thể không đầy đủ như Zalo PC. Đổi loại PC ↔ Web sau khi tạo: chưa hỗ trợ.

## Bảo mật và khóa
- Khóa bằng mật khẩu chỉ **khóa giao diện**: không mã hóa dữ liệu trên đĩa; người có quyền vào tài khoản Windows của bạn
  vẫn gỡ được khóa. Biểu tượng Zalo ở khay hệ thống vẫn còn khi khóa (do Zalo tạo), và popup của chính Zalo có thể hiện xem
  trước tin nếu bạn bật trong Zalo.
- Bản phát hành **chưa ký số**: SmartScreen cảnh báo lần đầu chạy. Chỉ tải từ
  [trang phát hành chính thức](https://github.com/QuanOK/MultiDepLao/releases) và đối chiếu SHA-256. Xem [SECURITY.md](../SECURITY.md).

## Khác
- Chỉ Windows 10/11 x64. Chỉ chạy một Multi DépLào tại một thời điểm.
- Bản portable trong thư mục không ghi được (Program Files) thì báo lỗi và thoát.
- Lần đầu mở sau khi giải nén/cài có thể mất 20–30 s (Windows quét exe lớn); các lần sau ~1–2 s.
- Giao diện English: một số lý do lỗi kỹ thuật vẫn hiện tiếng Việt không dấu; log luôn tiếng Việt không dấu.
