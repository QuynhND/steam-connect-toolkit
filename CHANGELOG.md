# Lịch sử phiên bản

## 1.0.2 – 2026-10-22

- Sửa lỗi: sau khi áp dụng DNS, ô tích card mạng bị bỏ tích nên "Chọn nhanh" và lần áp dụng tiếp theo báo "chưa chọn card mạng".
- Sửa lỗi: cấu hình mới không có địa chỉ manifest nên nút "Kiểm tra bản mới" báo chưa cấu hình; ô để trống nay dùng địa chỉ kho GitHub mặc định.
- VPN: kiểm tra IP công cộng bằng máy chủ IPv4; hiển thị cả IPv6 và cảnh báo khi IPv6 vẫn đi thẳng qua ISP (VPN chỉ chuyển IPv4).
- Nhật ký ghi rõ từng lần kiểm tra Steam (trước/sau đổi DNS, qua VPN).

## 1.0.1 – 2026-10-22

- Thêm mục "Phản hồi và báo lỗi" trong Cài đặt: tạo issue trên GitHub với nội dung điền sẵn (đã ẩn IP, tên máy, tên người dùng; không chứa mật khẩu), xem trước và sao chép, xem issue đã có.
- Mục Giới thiệu có nút mở trang dự án và các bản phát hành trên GitHub.
- Địa chỉ manifest cập nhật mặc định trỏ về kho GitHub; gói phát hành nằm trong thư mục `releases/` của kho.
- Kho nguồn: script quét thông tin nhạy cảm, hook pre-commit, mẫu issue.

## 1.0.0 – 2026-10-21

Phiên bản đầu tiên.

- Kiểm tra 5 tên miền Steam (DNS + HTTPS với xác thực chứng chỉ).
- Đổi DNS IPv4 + IPv6 sang 11 DNS công cộng chính và 22 DNS bổ sung; chọn nhanh DNS tốt nhất; tự tìm và lọc DNS công cộng theo quốc gia.
- Sao lưu DNS gốc trước thay đổi đầu tiên; áp dụng có xác minh và tự hoàn tác khi lỗi; thu hồi toàn bộ.
- VPN tích hợp Windows (IKEv2, L2TP/IPsec, SSTP, PPTP) và VPN Gate miễn phí (SSTP/L2TP, vpn/vpn).
- Phiên làm việc: tự khôi phục DNS/VPN khi thoát (hỏi trước), guardian khôi phục khi app dừng đột ngột, kiểm tra khi khởi động.
- Hướng dẫn 7 bước cho người dùng lần đầu; tab Cài đặt với Giới thiệu, xuất/nhập cấu hình, gói chẩn đoán.
- Nâng cấp từ xa qua manifest HTTPS, xác minh SHA-256 và chữ ký số, tự thay file hoặc chạy bộ cài im lặng.
