using System;
using System.Collections.Generic;

namespace SteamConnectToolkit.Services
{
    public class GuidePage
    {
        public string Title { get; set; }
        public string Body { get; set; }
        /// <summary>Tab của cửa sổ chính nên hiện khi đọc trang này (0 = Bảng điều khiển, 1 = Lịch sử, 2 = Danh sách DNS, 3 = VPN, 4 = Cài đặt).</summary>
        public int TabIndex { get; set; }
    }

    /// <summary>Nội dung hướng dẫn cho người dùng lần đầu (tách khỏi giao diện để kiểm thử được).</summary>
    public static class GuideContent
    {
        public const int Version = 1;

        public static List<GuidePage> Pages()
        {
            var p = new List<GuidePage>();

            p.Add(new GuidePage
            {
                TabIndex = 0,
                Title = "Chào mừng đến Steam Connect Toolkit",
                Body =
"Ứng dụng giúp bạn khi Steam không vào được vì mạng hoặc DNS:\n" +
"• Kiểm tra 5 tên miền Steam (DNS + HTTPS) để biết lỗi nằm ở đâu.\n" +
"• Đổi DNS (cả IPv4 và IPv6) sang DNS công cộng tốt nhất, có thu hồi về nguyên trạng.\n" +
"• Kết nối VPN bằng VPN client có sẵn của Windows khi đổi DNS vẫn chưa đủ; có sẵn danh sách VPN Gate miễn phí.\n" +
"\n" +
"An toàn là ưu tiên:\n" +
"• Trước khi đổi bất cứ thứ gì, cấu hình gốc được sao lưu.\n" +
"• Khi thoát app, mọi thay đổi được khôi phục, trừ khi bạn chủ động chọn giữ.\n" +
"• Nếu app bị tắt đột ngột, một tiến trình nhỏ chạy kèm (guardian) sẽ khôi phục giúp bạn.\n" +
"\n" +
"Hướng dẫn này có 7 bước, mất khoảng 2 phút. Bấm \"Tiếp\" để bắt đầu; có thể xem lại bất cứ lúc nào bằng nút \"Hướng dẫn\" trên Bảng điều khiển."
            });

            p.Add(new GuidePage
            {
                TabIndex = 0,
                Title = "Bước 1: Kiểm tra kết nối Steam",
                Body =
"Ở tab Bảng điều khiển, bấm \"1. Kiểm tra kết nối Steam hiện tại\".\n" +
"\n" +
"Bảng kết quả cho từng tên miền:\n" +
"• DNS: có phân giải được tên miền không và mất bao lâu.\n" +
"• Địa chỉ IP: IP mà DNS trả về.\n" +
"• HTTPS: kết nối TLS tới IP đó có hợp lệ không (phát hiện IP giả, chứng chỉ sai, cổng 443 bị chặn).\n" +
"• Dòng xanh = tốt, dòng đỏ = lỗi. Thanh thông báo phía trên tóm tắt; nhật ký bên dưới ghi chi tiết.\n" +
"\n" +
"Đọc kết quả:\n" +
"• DNS lỗi, hoặc HTTPS lỗi vì chứng chỉ sai tên → DNS của bạn đang bị can thiệp → đổi DNS (bước 2).\n" +
"• DNS OK nhưng HTTPS timeout/reset ở mọi DNS → bị chặn ở tầng kết nối → dùng VPN (bước 4)."
            });

            p.Add(new GuidePage
            {
                TabIndex = 0,
                Title = "Bước 2: Đổi DNS",
                Body =
"Cách nhanh nhất:\n" +
"• Bấm \"⚡ Chọn nhanh DNS tốt nhất và áp dụng ngay\". App đo mọi DNS đang bật với 5 tên miền Steam, chọn DNS tốt nhất, áp dụng rồi kiểm tra lại.\n" +
"\n" +
"Cách thủ công:\n" +
"• Chọn DNS trong hộp chọn → \"2. Áp dụng DNS đã chọn\" → app tự kiểm tra lại. Muốn kiểm tra thêm lần nữa, bấm nút 3.\n" +
"\n" +
"Lưu ý:\n" +
"• Chỉ card mạng được tích trong bảng \"Card mạng\" mới bị đổi DNS (mặc định: card đang kết nối, in đậm).\n" +
"• App đổi cả DNS IPv4 và IPv6; DNS không có IPv6 thì DNS IPv6 của ISP được xoá.\n" +
"• Sau khi đổi, hãy khởi động lại Steam nếu Steam đang mở.\n" +
"• App đọc lại cấu hình để xác minh; nếu lỗi giữa chừng sẽ tự hoàn tác, card không bao giờ ở trạng thái dở dang."
            });

            p.Add(new GuidePage
            {
                TabIndex = 0,
                Title = "Bước 3: Thu hồi và an toàn",
                Body =
"• \"4. Thu hồi – khôi phục DNS gốc, ngắt VPN\": trả mọi thứ về như trước khi dùng app (DNS gốc, ngắt VPN do app kết nối, xoá cấu hình VPN Gate do app tạo).\n" +
"• Dòng trạng thái phía trên nút Thu hồi luôn cho biết đang có thay đổi gì và khi thoát app sẽ khôi phục hay giữ.\n" +
"• Khi thoát app mà còn thay đổi, app hỏi: Khôi phục rồi thoát / Giữ thay đổi / Huỷ.\n" +
"• Muốn giữ DNS lâu dài sau khi thoát: tích \"Giữ thay đổi sau khi thoát app\". Đây là lựa chọn chủ động và được ghi vào lịch sử.\n" +
"• App bị crash hay bị End task: tiến trình guardian chạy kèm sẽ khôi phục trong vài giây. Lần mở sau, nếu còn gì chưa khôi phục, app sẽ hỏi bạn.\n" +
"• Tab \"Lịch sử thay đổi\" ghi lại mọi thao tác và có thể xuất CSV."
            });

            p.Add(new GuidePage
            {
                TabIndex = 3,
                Title = "Bước 4: VPN khi đổi DNS chưa đủ",
                Body =
"Tab VPN dùng VPN client có sẵn của Windows (IKEv2, L2TP/IPsec, SSTP, PPTP), không cần cài phần mềm.\n" +
"\n" +
"VPN miễn phí (VPN Gate):\n" +
"• Bấm \"Tìm máy chủ VPN Gate\" → app tải danh sách, đo và xếp hạng (máy chủ gần Việt Nam, máy chủ chính thức ★, SSTP được ưu tiên).\n" +
"• Chọn một dòng → \"⚡ Tạo cấu hình, kết nối và kiểm tra Steam\". Tài khoản vpn / vpn do dự án cung cấp.\n" +
"• VPN Gate do tình nguyện viên vận hành và có ghi log khoảng 2 tuần: chỉ dùng để chơi Steam, không đăng nhập ngân hàng hay dịch vụ nhạy cảm qua VPN này.\n" +
"\n" +
"VPN của bạn:\n" +
"• \"Thêm cấu hình VPN…\" → nhập máy chủ và giao thức (IKEv2 khuyên dùng) → nhập tài khoản → \"Kết nối\".\n" +
"• \"Ngắt kết nối\" khi không dùng nữa. Mã lỗi (691, 800, 809…) được giải thích ngay trong nhật ký."
            });

            p.Add(new GuidePage
            {
                TabIndex = 2,
                Title = "Bước 5: Danh sách DNS",
                Body =
"• \"Kiểm tra tất cả DNS\": đo toàn bộ danh sách; DNS lỗi được tự tắt (có thể bật lại).\n" +
"• \"Tự động tìm, lọc và báo cáo\": thêm bước tải DNS công cộng theo quốc gia (public-dns.info), lọc máy chủ hoạt động tốt với Steam và lập báo cáo; chọn dòng tốt rồi \"Thêm DNS đã chọn vào danh sách\".\n" +
"• \"Thêm DNS…\": nhập DNS riêng (IPv4 và IPv6).\n" +
"• Cột \"Dùng\": tích để DNS xuất hiện trong hộp chọn ở Bảng điều khiển và được xét khi \"Chọn nhanh\".\n" +
"• Cột \"Kết quả gần nhất\", \"Điểm\", \"Trễ\" giúp so sánh các DNS sau mỗi lần đo."
            });

            p.Add(new GuidePage
            {
                TabIndex = 4,
                Title = "Bước 6: Cài đặt và kết thúc",
                Body =
"Tab Cài đặt cho phép:\n" +
"• Bật/tắt hướng dẫn này khi khởi động (hoặc xem lại bất cứ lúc nào bằng nút \"Hướng dẫn\").\n" +
"• Chọn giữ hay khôi phục thay đổi khi thoát app.\n" +
"• Chỉnh thời gian chờ DNS/HTTPS, số luồng đo song song, mã quốc gia tìm DNS, ưu tiên SSTP cho VPN Gate.\n" +
"• Mở thư mục dữ liệu (cấu hình, sao lưu DNS gốc, lịch sử, nhật ký) hoặc đặt lại cài đặt mặc định.\n" +
"\n" +
"Thứ tự gợi ý khi Steam không vào được:\n" +
"• Kiểm tra (nút 1) → Chọn nhanh DNS → nếu vẫn lỗi → VPN Gate → chơi xong, bấm Thu hồi hoặc thoát app để trả về nguyên trạng.\n" +
"\n" +
"Chúc bạn chơi vui!"
            });

            return p;
        }
    }
}
