# Steam Connect Toolkit

**Tác giả:** Quynh Nguyen · **Giấy phép:** MIT · **Phiên bản:** 1.0.3 · *(tên cũ: Steam DNS Switcher)*

Ứng dụng Windows 10/11 giúp **kiểm tra kết nối tới Steam**, **đổi DNS (IPv4 + IPv6)** sang các DNS công cộng và **thu hồi về nguyên trạng**, cùng **kết nối VPN** bằng VPN client có sẵn của Windows khi đổi DNS vẫn chưa đủ.

- Một file `SteamConnectToolkit.exe` duy nhất (~350 KB), chạy trên .NET Framework 4.8 có sẵn trong Windows 10/11. **Không cần cài runtime hay driver.**
- Cần quyền **Administrator** ngay khi khởi động (Windows hỏi UAC) vì app phải đổi DNS của card mạng.

## Tải và chạy

1. Tải bản mới nhất tại trang phát hành: <https://github.com/QuynhND/steam-connect-toolkit/releases>
   - `SteamConnectToolkit.exe`: file chạy lẻ (portable).
   - `SteamConnectToolkit-x.y.z-portable.zip`: file chạy kèm tài liệu hướng dẫn.
   - `SHA256SUMS.txt`: mã băm để đối chiếu nếu muốn (PowerShell: `Get-FileHash .\SteamConnectToolkit.exe`).
2. Chạy `SteamConnectToolkit.exe` → Windows hỏi UAC → chọn **Yes**. File chưa ký số nên SmartScreen có thể cảnh báo ở lần chạy đầu: bấm *More info → Run anyway*.
3. Nếu chạy không có quyền admin, app báo và đề nghị khởi động lại với quyền Administrator.

Dữ liệu của app nằm trong `C:\ProgramData\SteamConnectToolkit\`. Dữ liệu của phiên bản cũ (`SteamDnsSwitcher`), kể cả bản sao lưu DNS gốc, được tự động chuyển sang ở lần chạy đầu.

## Giao diện

1. **Bảng điều khiển**: thông báo, lệnh đang chạy, thanh tiến trình, nhật ký; các nút 1-4 và chọn nhanh; bảng card mạng (tích chọn card sẽ đổi DNS, DNS hiện tại); kết quả kiểm tra; nút **Hướng dẫn**.
2. **Lịch sử thay đổi**: mọi lần áp dụng / thu hồi DNS, kiểm tra Steam, thao tác VPN; xuất CSV.
3. **Danh sách DNS**: kiểm tra tất cả, tự động tìm DNS công cộng mới theo quốc gia, lọc DNS lỗi, báo cáo; thêm DNS thủ công; bật/tắt/xoá.
4. **VPN**: cấu hình VPN Windows, VPN Gate miễn phí, kết nối và kiểm tra Steam qua VPN.
5. **Cài đặt**: hướng dẫn lần đầu, giữ hay khôi phục khi thoát, thời gian chờ, VPN, cập nhật từ xa, phản hồi, dữ liệu và nhật ký, giới thiệu.

## Hướng dẫn cho người dùng lần đầu

Lần đầu mở app, một hướng dẫn 7 bước hiện lên (khoảng 2 phút đọc): giới thiệu, kiểm tra Steam, đổi DNS, thu hồi và an toàn, VPN, danh sách DNS, cài đặt. Khi đọc tới bước nào, cửa sổ chính tự chuyển sang tab tương ứng để bạn nhìn thấy các nút được nhắc tới. Có thể dùng phím ← → để chuyển trang.

- Ô **"Hiện hướng dẫn này mỗi khi mở app"** ở cuối hộp thoại: bỏ tích để không hiện lại.
- Tab **Cài đặt → Hướng dẫn sử dụng**: bật/tắt lại bất cứ lúc nào, hoặc bấm **"Xem hướng dẫn ngay"**.
- Nút **"❔ Hướng dẫn"** ở góc trên phải Bảng điều khiển mở lại hướng dẫn.

## Cách dùng nhanh

1. Bấm **1. Kiểm tra kết nối Steam hiện tại**. Mỗi tên miền hiện DNS OK/LỖI, IP, HTTPS OK/LỖI, độ trễ, chứng chỉ.
2. Nếu lỗi: bấm **⚡ Chọn nhanh DNS tốt nhất và áp dụng ngay**, hoặc chọn DNS trong hộp chọn rồi bấm **2. Áp dụng DNS đã chọn**.
3. App tự kiểm tra lại. Sau khi đổi DNS, nếu Steam đang mở hãy **khởi động lại Steam**.
4. Nếu mọi DNS đều "DNS OK, HTTPS lỗi" (nhà mạng chặn ở tầng SNI/IP): sang tab **VPN**, dùng **VPN Gate** hoặc thêm cấu hình VPN của bạn, rồi bấm **⚡ Kết nối VPN và kiểm tra Steam**.
5. Muốn trả về nguyên trạng DNS: nút **4. Thu hồi – khôi phục DNS gốc**. Muốn thoát VPN: **Ngắt kết nối**.

Mặc định chỉ card mạng đang kết nối có gateway (Ethernet / Wi-Fi) được tích chọn; card ảo (Hyper-V, VMware, VPN...) không được chọn sẵn.

## Các chức năng

| # | Chức năng | Mô tả |
|---|-----------|-------|
| 1 | Kiểm tra kết nối Steam | 5 tên miền `store.steampowered.com`, `steamcommunity.com`, `api.steampowered.com`, `login.steampowered.com`, `help.steampowered.com`. Mỗi tên miền kiểm tra **DNS** (phân giải, độ trễ) và **HTTPS** (kết nối cổng 443, bắt tay TLS, xác thực chứng chỉ, phản hồi HTTP). |
| 2 | Đổi DNS | Google, Cloudflare, Quad9, Quad9 Unfiltered, AdGuard, Verisign, OpenDNS, CleanBrowsing Security, Control D Malware Blocking, DNS4EU Protective, UltraDNS (Neustar) và nhiều DNS bổ sung. **Đổi cả IPv4 và IPv6**. DNS không có IPv6 → DNS IPv6 của card được xoá để Windows không dùng DNS IPv6 của nhà mạng. |
| 3 | Kiểm tra lại | Tự động kiểm tra lại Steam ngay sau khi đổi, hoặc bấm nút 3. |
| 4 | Thu hồi | DNS gốc của từng card được sao lưu **trước thay đổi đầu tiên** (tĩnh hay DHCP, địa chỉ). Nút 4 khôi phục nguyên trạng, nhận diện card theo mã định danh nên vẫn đúng khi card đổi tên. |
| ⚡ | Chọn nhanh | Đo toàn bộ DNS đang bật với 5 tên miền Steam, xếp hạng (số tên miền HTTPS OK → DNS OK → độ trễ) rồi áp dụng DNS tốt nhất ngay, kiểm tra lại sau đó. |
| VPN | Kết nối VPN | Liệt kê / thêm / xoá cấu hình VPN tích hợp của Windows (IKEv2, L2TP/IPsec, SSTP, PPTP), kết nối / ngắt, xem IP công cộng trước và sau, kiểm tra Steam qua VPN. Mật khẩu không lộ trên dòng lệnh; nếu chọn "nhớ mật khẩu" thì được mã hoá theo tài khoản Windows của bạn. |

### Tab Danh sách DNS

- **Kiểm tra tất cả**: đo mọi DNS đang bật với 5 tên miền Steam, kết quả hiện ngay trong bảng.
- **Tìm DNS công cộng**: tải danh sách DNS công cộng theo mã quốc gia (nguồn public-dns.info), lọc những DNS trả lời đúng và nhanh, thêm vào danh sách.
- **Lọc DNS lỗi**: DNS không đạt bị tự động tắt (có thể bật lại); **Báo cáo** tóm tắt kết quả.
- **Thêm DNS thủ công**: nhập tên, IPv4 chính/phụ, IPv6 (nếu có). Địa chỉ thiếu octet (ví dụ `8.8.8`) bị từ chối.
- Bật/tắt từng DNS để quyết định DNS nào tham gia "Chọn nhanh"; xoá DNS tự thêm.

## VPN

### VPN miễn phí: VPN Gate

Tab VPN có mục **VPN Gate** (vpngate.net, dự án học thuật của Đại học Tsukuba, Nhật Bản): hàng trăm máy chủ VPN công cộng miễn phí, không cần tài khoản (tên đăng nhập và mật khẩu đều là `vpn`, L2TP pre-shared key `vpn`).

- **Tìm máy chủ VPN Gate**: tải danh sách, lọc máy chủ hỗ trợ giao thức Windows (SSTP, L2TP/IPsec), đo kết nối tới 30 máy chủ đầu, xếp hạng: tới được → có SSTP → quốc gia gần Việt Nam (JP, KR, TH, SG, HK, TW...) → máy chủ chính thức `public-vpn-*` (★) → điểm → độ trễ.
- **Tạo cấu hình, kết nối và kiểm tra Steam**: tạo cấu hình VPN Windows cho máy chủ đã chọn, kết nối bằng vpn/vpn, lấy IP công cộng rồi kiểm tra 5 tên miền Steam.
- **SSTP** được ưu tiên: chạy trên cổng 443 (gần như không bị firewall chặn), chứng chỉ hợp lệ sẵn, không vướng lỗi NAT. Đã kiểm thử thật từ Việt Nam: kết nối trong khoảng 2 giây, Steam truy cập được qua VPN.
- **L2TP/IPsec** là lựa chọn dự phòng; nếu máy nằm sau router NAT, app đề nghị đặt khoá registry cần thiết (Cài đặt → VPN; cần khởi động lại Windows để chắc chắn).
- VPN Gate chỉ chuyển lưu lượng **IPv4**. Nếu máy có IPv6, lưu lượng IPv6 vẫn đi thẳng qua nhà mạng; app hiển thị cả hai địa chỉ và cảnh báo. Muốn ẩn hoàn toàn thì tắt IPv6 trên card mạng trước khi nối.
- Máy chủ VPN Gate do tình nguyện viên vận hành và **ghi log khoảng 2 tuần** theo chính sách chống lạm dụng của dự án: chỉ nên dùng để truy cập Steam, không đăng nhập dịch vụ nhạy cảm qua VPN này.

### Dùng VPN khác với app

App dùng VPN client có sẵn của Windows, nên mức tương thích như sau (mức 1-2 không cần cài thêm gì):

| Mức | Loại | Ví dụ | Dùng với app |
|-----|------|-------|--------------|
| 1 – Dùng ngay | VPN công cộng SSTP / L2TP, không cần tài khoản | **VPN Gate** (SSTP ưu tiên, L2TP dự phòng) | Tab VPN → VPN Gate, một nút |
| 2 – Cần tài khoản, không cần phần mềm | Nhà cung cấp có hướng dẫn cài thủ công IKEv2 / L2TP / SSTP với tên đăng nhập + mật khẩu | hide.me, Windscribe (IKEv2), PrivadoVPN, gói trả phí NordVPN/Surfshark IKEv2, máy chủ riêng (SoftEther, StrongSwan, RRAS) | **Thêm cấu hình VPN…** rồi nhập tài khoản; IKEv2 thường phải cài chứng chỉ CA của nhà cung cấp trước |
| 3 – Cần phần mềm riêng | WireGuard, OpenVPN, ứng dụng riêng của nhà cung cấp (ProtonVPN Free, Cloudflare WARP, Mullvad...) | ProtonVPN, WARP, Mullvad | Không điều khiển được từ app; sau khi bật bằng phần mềm riêng vẫn dùng được nút kiểm tra Steam |

Thứ tự nên thử khi Steam bị chặn: đổi DNS (nhanh, không ảnh hưởng tốc độ) → VPN Gate SSTP máy chủ gần (JP/KR/TH) → VPN có tài khoản IKEv2 (ổn định, nhanh hơn) → phần mềm VPN riêng.

### Lưu ý khi dùng VPN

- Chỉ hỗ trợ các giao thức Windows có sẵn: **IKEv2**, **L2TP/IPsec** (pre-shared key), **SSTP** (cổng 443, khó bị chặn), **PPTP** (cũ, kém an toàn). WireGuard/OpenVPN cần phần mềm riêng.
- Nếu nhà cung cấp yêu cầu chứng chỉ CA (IKEv2), cài chứng chỉ trước khi kết nối.
- Không bật **split tunneling** nếu muốn Steam đi qua VPN.
- Khi VPN kết nối, Windows thường dùng DNS của VPN; card VPN cũng xuất hiện trong bảng card mạng và có thể đổi DNS nếu cần.
- Mã lỗi hay gặp được giải thích ngay trong nhật ký: 691 sai tài khoản, 800/809 không tới được máy chủ hoặc cổng bị chặn, 868 không phân giải được tên máy chủ, 789 sai pre-shared key, 13801 chứng chỉ không tin cậy.

## Tab Cài đặt

| Nhóm | Nội dung |
|------|----------|
| Hướng dẫn sử dụng | Bật/tắt hướng dẫn lần đầu; xem lại ngay. |
| Khi khởi động | Tự động kiểm tra kết nối Steam khi mở app. |
| Khi thoát app | Giữ hay tự khôi phục thay đổi DNS/VPN (đồng bộ với ô trên Bảng điều khiển). |
| Kiểm tra DNS và Steam | Thời gian chờ DNS/HTTPS, số DNS đo song song, tự tắt DNS lỗi, mã quốc gia và số DNS tải tối đa khi tìm DNS công cộng. |
| VPN | Ưu tiên SSTP cho VPN Gate; trạng thái và nút đặt khoá registry L2TP sau NAT; xoá tài khoản VPN đã lưu trong app. |
| Cập nhật từ xa | Tự kiểm tra bản mới khi mở app (mỗi ngày một lần), kiểm tra bản mới, tải và cài đặt. Địa chỉ manifest có sẵn, có thể đổi nếu tác giả cung cấp địa chỉ khác. |
| Phản hồi và báo lỗi | Tạo issue GitHub với nội dung điền sẵn đã ẩn thông tin cá nhân; xem trước / sao chép; xem issue đã có. |
| Dữ liệu và nhật ký | Mở thư mục dữ liệu, mở `app.log`, **xuất/nhập cấu hình** (không chứa mật khẩu; nhập có kiểm tra hợp lệ và giữ tài khoản VPN hiện có), **xuất gói chẩn đoán** (zip gồm thông tin hệ thống, trạng thái mạng, nhật ký, lịch sử, cấu hình đã lược mật khẩu) để gửi khi cần hỗ trợ; đặt lại cài đặt mặc định (không đụng DNS, tài khoản, lịch sử, sao lưu). |
| Giới thiệu | Tên, phiên bản, tác giả, giấy phép, mô tả; phiên bản Windows/.NET, quyền admin, đường dẫn file chạy và dữ liệu; mở README/LICENSE, trang dự án và trang phát hành; sao chép thông tin hệ thống. |

## An toàn: sao lưu và khôi phục

Nguyên tắc: **khi app đóng, cấu hình ban đầu được khôi phục đầy đủ**, không để DNS hay VPN bị thay đổi ngầm mà bạn không biết.

| Tình huống | App làm gì |
|------------|-----------|
| Trước thay đổi đầu tiên | Ghi DNS gốc của từng card (tĩnh/DHCP, địa chỉ) vào `original-dns.json`; chỉ xoá khi khôi phục thành công. |
| Áp dụng DNS | Sau khi đổi, app đọc lại cấu hình Windows để xác minh; nếu lệnh lỗi hoặc không khớp thì **hoàn tác ngay**, không để card ở trạng thái dở dang. |
| Nút 4. Thu hồi | Ngắt VPN do app kết nối → trả DNS gốc → xoá cấu hình VPN Gate do app tạo → làm mới bộ đệm DNS. |
| Khi thoát app | Nếu còn thay đổi: hỏi **Khôi phục rồi thoát** (mặc định) / **Giữ thay đổi** / **Huỷ**. Hộp kiểm "Giữ thay đổi sau khi thoát app" tắt hỏi và giữ luôn. Khi Windows tắt máy: khôi phục ngay, không hỏi. |
| App bị crash hoặc End task | Một tiến trình bảo vệ chạy kèm từ lúc khởi động sẽ tự khôi phục DNS/VPN và ghi lịch sử "Khôi phục tự động". |
| Mở app lần sau | Nếu phiên trước không kết thúc sạch hoặc còn dữ liệu của phiên bản cũ: hỏi khôi phục ngay. Nếu phiên trước chủ động giữ thay đổi: nhắc trong nhật ký và thanh trạng thái. |
| Card mạng không còn / đổi tên | Card đổi tên vẫn khôi phục đúng. Card bị tắt hoặc tháo: bỏ qua, giữ bản sao lưu, hỏi có xoá bản sao lưu không. |

Lưu ý: nếu máy **đã đặt sẵn DNS công cộng** (ví dụ Google tĩnh) trước khi dùng app, "thu hồi" trả về đúng cấu hình đó chứ không phải DNS của nhà mạng; nhật ký sẽ nhắc điều này.

## Nâng cấp phiên bản

- **Trong app (khuyên dùng)**: Cài đặt → **Cập nhật từ xa** → *Kiểm tra bản mới* → *Tải và cài đặt*. App tải gói từ trang phát hành, xác minh SHA-256, khôi phục DNS/VPN như khi thoát bình thường, thay file chương trình (bản cũ giữ ở `SteamConnectToolkit.exe.bak`) rồi tự mở lại. Nếu tải lỗi hoặc mã băm không khớp, app huỷ và không đụng tới bản đang dùng.
- **Bằng tay**: đóng app, tải file mới từ trang phát hành, đổi tên file cũ thành `.bak`, chép file mới vào cùng chỗ rồi chạy.
- **Quay lại bản cũ**: đóng app, xoá file mới, đổi `SteamConnectToolkit.exe.bak` lại thành `SteamConnectToolkit.exe`. Dữ liệu trong `ProgramData` không bị mất; cấu hình của bản mới vẫn đọc được bởi bản cũ.
- Bản 1.0.0 – 1.0.2 không tự thấy bản mới: tải tay từ trang phát hành, hoặc dán địa chỉ `https://github.com/QuynhND/steam-connect-toolkit/releases/latest/download/latest.json` vào Cài đặt → Cập nhật từ xa.

## Phản hồi và báo lỗi

Cài đặt → **Phản hồi và báo lỗi**: nhập mô tả, bấm *Tạo issue trên GitHub* → trình duyệt mở trang tạo issue với tiêu đề và nội dung điền sẵn (mô tả, thông tin hệ thống, 20 dòng nhật ký gần nhất). Nội dung đã lược bỏ địa chỉ IP (trừ DNS công cộng), tên máy, tên người dùng, đường dẫn cá nhân và không bao giờ chứa mật khẩu; bấm *Xem trước / sao chép* để kiểm tra trước khi gửi. Việc gửi do bạn thực hiện trong trình duyệt bằng tài khoản GitHub của mình. Có thể gửi kèm **gói chẩn đoán** (Cài đặt → Dữ liệu và nhật ký) khi cần hỗ trợ sâu hơn.

## Dữ liệu

Lưu tại `C:\ProgramData\SteamConnectToolkit\`:

| File | Nội dung |
|------|----------|
| `config.json` | Danh sách DNS (bật/tắt, kết quả gần nhất, DNS tự thêm), tuỳ chọn, tài khoản VPN đã lưu (mật khẩu đã mã hoá) |
| `original-dns.json` | DNS gốc của từng card đã đổi (xoá khi thu hồi thành công) |
| `session.json` | Trạng thái phiên làm việc hiện tại (dùng để khôi phục khi app dừng đột ngột) |
| `history.json` | Lịch sử thay đổi |
| `app.log` | Nhật ký chi tiết |
| `updates\` | Gói cập nhật đã tải (tự dọn sau 7 ngày) |

## Sự cố thường gặp

| Hiện tượng | Cách xử lý |
|------------|-----------|
| Mọi DNS đều "DNS OK, HTTPS lỗi" | Nhà mạng chặn ở tầng kết nối, đổi DNS không giúp được. Dùng tab VPN (VPN Gate SSTP). |
| "Chưa chọn card mạng" | Tích chọn card Ethernet/Wi-Fi đang dùng trong bảng card mạng trên Bảng điều khiển. |
| Đổi DNS xong Steam vẫn lỗi | Khởi động lại Steam để Steam dùng DNS mới; bấm nút 3 để kiểm tra lại. |
| VPN kết nối nhưng IP công cộng không đổi | Cấu hình VPN đang bật split tunneling, hoặc máy chủ không chuyển tiếp lưu lượng. Thử máy chủ VPN Gate khác. |
| VPN Gate L2TP báo lỗi 809 | Máy sau router NAT: Cài đặt → VPN → đặt khoá registry L2TP, khởi động lại Windows; hoặc chọn máy chủ SSTP. |
| Sau khi app dừng đột ngột DNS/VPN còn ở trạng thái cũ | Mở lại app: app hỏi khôi phục ngay. Hoặc bấm nút **4. Thu hồi**. Bản sao lưu luôn được giữ cho tới khi khôi phục thành công. |
| "Không kiểm tra được bản mới" | Kiểm tra mạng hoặc DNS đang bị chặn: đổi DNS bằng chính app rồi thử lại. |
| "SHA-256 không khớp" khi cập nhật | File tải về bị thay đổi hoặc tải lỗi; app không cài. Thử lại sau, hoặc tải tay từ trang phát hành. |
| Windows hỏi UAC hai lần | Bình thường khi chạy bộ cài tay rồi mở app; nâng cấp từ trong app chỉ hỏi một lần. |

## Giấy phép

MIT License © 2026 Quynh Nguyen. Xem file `LICENSE`.
