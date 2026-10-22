# Steam Connect Toolkit

**Tác giả:** Quynh Nguyen · **Giấy phép:** MIT · **Phiên bản:** 1.0.3 · *(tên cũ: Steam DNS Switcher)*

Ứng dụng Windows 10/11 giúp **kiểm tra kết nối tới Steam**, **đổi DNS (IPv4 + IPv6)** sang các DNS công cộng có thu hồi về nguyên trạng, và **kết nối VPN** bằng VPN client tích hợp sẵn của Windows khi đổi DNS không đủ.

- Một file `SteamConnectToolkit.exe` duy nhất (~200 KB), chạy trên .NET Framework 4.8 có sẵn trong Windows 10/11, **không cần cài runtime hay driver**.
- Yêu cầu quyền **Administrator** ngay từ khi khởi động (UAC) vì phải đổi DNS của card mạng.

## Tính năng

| # | Chức năng | Mô tả |
|---|-----------|-------|
| a | Kiểm tra kết nối Steam | 5 tên miền `store.steampowered.com`, `steamcommunity.com`, `api.steampowered.com`, `login.steampowered.com`, `help.steampowered.com`. Mỗi tên miền kiểm tra **DNS** (phân giải, độ trễ) và **HTTPS** (TCP 443 + bắt tay TLS với SNI + xác thực chứng chỉ + phản hồi HTTP). |
| b | Đổi DNS | Google, Cloudflare, Quad9, Quad9 Unfiltered, AdGuard, Verisign, OpenDNS, CleanBrowsing Security, Control D Malware Blocking, DNS4EU Protective, UltraDNS (Neustar) và nhiều DNS bổ sung. **Đổi cả IPv4 và IPv6** bằng `netsh`. DNS không có IPv6 → DNS IPv6 của card bị xoá để Windows không dùng DNS IPv6 của ISP. |
| c | Kiểm tra lại | Tự động kiểm tra lại Steam ngay sau khi đổi, hoặc bấm nút 3. |
| d | Thu hồi | DNS gốc của từng card được sao lưu **trước thay đổi đầu tiên** (tĩnh hay DHCP, địa chỉ). Nút 4 khôi phục nguyên trạng, tìm card theo GUID nên vẫn đúng khi card đổi tên. |
| e | Chọn nhanh | Đo toàn bộ DNS đang bật với 5 tên miền Steam, xếp hạng (số tên miền HTTPS OK → DNS OK → độ trễ) rồi áp dụng DNS tốt nhất ngay, kiểm tra lại sau đó. |
| f | VPN | Liệt kê / thêm / xoá cấu hình VPN tích hợp của Windows (IKEv2, L2TP/IPsec, SSTP, PPTP), kết nối / ngắt, xem IP công cộng trước và sau, kiểm tra Steam qua VPN. Mật khẩu đi qua RAS API (không lộ trên dòng lệnh); nếu chọn "nhớ mật khẩu" thì mã hoá DPAPI theo tài khoản Windows. |

Giao diện gồm năm tab:

1. **Bảng điều khiển**: thông báo, lệnh đang chạy, thanh tiến trình, nhật ký; các nút 1-4 và chọn nhanh; bảng card mạng (tích chọn card sẽ đổi DNS, DNS hiện tại); kết quả kiểm tra; nút **Hướng dẫn**.
2. **Lịch sử thay đổi**: mọi lần áp dụng / thu hồi DNS, kiểm tra Steam, thao tác VPN; xuất CSV.
3. **Danh sách DNS**: kiểm tra tất cả, tự động tìm DNS công cộng mới (theo quốc gia, nguồn public-dns.info), lọc DNS lỗi (tự động tắt), báo cáo; thêm DNS thủ công; bật/tắt/xoá.
4. **VPN**: cấu hình VPN Windows, VPN Gate miễn phí, kết nối và kiểm tra Steam qua VPN.
5. **Cài đặt**: bật/tắt hướng dẫn lần đầu, giữ hay khôi phục khi thoát, thời gian chờ, số luồng đo, mã quốc gia, ưu tiên SSTP, thư mục dữ liệu, đặt lại mặc định.

## Hướng dẫn cho người dùng lần đầu

Lần đầu mở app, một hướng dẫn 7 bước hiện lên (khoảng 2 phút đọc): giới thiệu, kiểm tra Steam, đổi DNS, thu hồi và an toàn, VPN, danh sách DNS, cài đặt. Khi đọc tới bước nào, cửa sổ chính tự chuyển sang tab tương ứng để bạn nhìn thấy các nút được nhắc tới. Có thể dùng phím ← → để chuyển trang.

- Ô **"Hiện hướng dẫn này mỗi khi mở app"** ở cuối hộp thoại: bỏ tích để không hiện lại.
- Tab **Cài đặt → Hướng dẫn sử dụng**: bật/tắt lại bất cứ lúc nào, hoặc bấm **"Xem hướng dẫn ngay"**.
- Nút **"❔ Hướng dẫn"** ở góc trên phải Bảng điều khiển mở lại hướng dẫn.

Tuỳ chọn này lưu trong `config.json` (`ShowGuideOnStartup`).

## Phát hành, nâng cấp

- **Đóng gói**: `powershell -ExecutionPolicy Bypass -File .\release.ps1 [-Installer] [-Publish]` → `dist\release\<phiên bản>\` gồm exe lẻ, zip portable (kèm tài liệu hướng dẫn trong `docs`), bộ cài (nếu có Inno Setup), `SHA256SUMS.txt`, `CHANGELOG.md`, `latest.json` (manifest cập nhật).
- **Phát hành**: gói đặt trên [GitHub Releases](https://github.com/QuynhND/steam-connect-toolkit/releases) theo tag `v<phiên bản>`, không nằm trong kho nguồn. `tools\publish-release.ps1 -Version x.y.z` tạo Release và tải các file lên; token GitHub lấy từ biến môi trường `GITHUB_TOKEN` hoặc Git Credential Manager, không bao giờ lưu trong kho.
- **Nâng cấp từ xa**: app mặc định đọc `https://github.com/QuynhND/steam-connect-toolkit/releases/latest/download/latest.json` (GitHub tự chuyển hướng tới Release mới nhất), nên chỉ cần phát hành là mọi máy thấy bản mới. Hướng dẫn nâng cấp cho người dùng và phương án nâng cấp từ xa nằm trong thư mục `docs` của gói portable.
- Trong app: Cài đặt → **Cập nhật từ xa** → *Kiểm tra bản mới* → *Tải và cài đặt* (địa chỉ manifest có sẵn; có thể thay bằng địa chỉ `https://…/latest.json` riêng). App xác minh SHA-256 (và chữ ký số nếu cấu hình), khôi phục DNS/VPN như khi thoát, rồi tự thay file hoặc chạy bộ cài im lặng và mở lại.

## Phản hồi và báo lỗi

Cài đặt → **Phản hồi và báo lỗi**: nhập mô tả, bấm *Tạo issue trên GitHub* → trình duyệt mở trang tạo issue của kho với tiêu đề và nội dung điền sẵn (mô tả, thông tin hệ thống, 20 dòng nhật ký gần nhất). Nội dung được lược bỏ địa chỉ IP (trừ DNS công cộng), tên máy, tên người dùng, đường dẫn cá nhân và không bao giờ chứa mật khẩu; bấm *Xem trước / sao chép* để kiểm tra trước khi gửi. App không chứa token GitHub nào; việc gửi do bạn thực hiện trong trình duyệt bằng tài khoản của mình.

## Bảo mật khi đóng góp mã

- `.gitignore` loại trừ dữ liệu chạy (`config.json`, `history.json`, `session.json`, `*.log`, `updates/`), khoá và chứng chỉ (`*.pfx`, `*.key`, `*.pem`), file môi trường.
- `tools/check-secrets.ps1` quét khoá riêng, token GitHub/AWS, blob DPAPI, mật khẩu trong JSON (chặn) và email, đường dẫn cá nhân (cảnh báo). `release.ps1` chạy quét này trước khi đóng gói.
- Hook pre-commit cục bộ (không nằm trong kho): tạo `.githooks/pre-commit` gọi `tools/check-secrets.ps1` rồi `git config core.hooksPath .githooks`. Các thư mục `.githooks`, `.github`, `docs`, `releases` và thư mục công cụ AI/IDE (`.claude`, `.cursor`, `.vscode`…) đều nằm trong `.gitignore`.

## Tab Cài đặt

| Nhóm | Nội dung |
|------|----------|
| Hướng dẫn sử dụng | Bật/tắt hướng dẫn lần đầu; xem lại ngay. |
| Khi khởi động | Tự động kiểm tra kết nối Steam khi mở app. |
| Khi thoát app | Giữ hay tự khôi phục thay đổi DNS/VPN (đồng bộ với ô trên Bảng điều khiển). |
| Kiểm tra DNS và Steam | Thời gian chờ DNS/HTTPS, số DNS đo song song, tự tắt DNS lỗi, mã quốc gia và số DNS tải tối đa khi tìm DNS công cộng. |
| VPN | Ưu tiên SSTP cho VPN Gate; trạng thái và nút đặt khoá registry L2TP sau NAT; xoá tài khoản VPN đã lưu trong app. |
| Cập nhật từ xa | Địa chỉ manifest `latest.json`, tự kiểm tra khi mở app (mỗi ngày một lần), kiểm tra bản mới, tải và cài đặt (xác minh SHA-256/chữ ký số, khôi phục DNS/VPN trước khi thay file hoặc chạy bộ cài im lặng). |
| Phản hồi và báo lỗi | Tạo issue GitHub với nội dung điền sẵn đã ẩn thông tin cá nhân; xem trước / sao chép; xem issue đã có. |
| Dữ liệu và nhật ký | Mở thư mục dữ liệu, mở `app.log`, **xuất/nhập cấu hình** (JSON, không chứa mật khẩu; nhập có kiểm tra hợp lệ và giữ tài khoản VPN hiện có), **xuất gói chẩn đoán** (zip gồm thông tin hệ thống, đầu ra `netsh`/`rasdial`, nhật ký, lịch sử, phiên, sao lưu DNS gốc, cấu hình đã lược mật khẩu, danh sách VPN) để gửi khi cần hỗ trợ; đặt lại cài đặt mặc định (không đụng DNS, tài khoản, lịch sử, sao lưu). |
| Giới thiệu | Tên, phiên bản, tác giả, giấy phép, mô tả, nguồn dữ liệu; phiên bản Windows/.NET, quyền admin, đường dẫn file chạy và dữ liệu, khoá L2TP; mở README/LICENSE; sao chép thông tin hệ thống. |

## Biên dịch

Không cần Visual Studio. Windows 10/11 có sẵn `csc.exe` của .NET Framework 4.8:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Kết quả: `dist\SteamConnectToolkit.exe` (kèm icon, manifest `requireAdministrator`).

```powershell
# Biên dịch và chạy bộ kiểm thử (console, KHÔNG đổi DNS/VPN thật, không cần admin)
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SmokeTest -RunSmokeTest

# Tạo bộ cài (cần Inno Setup 6: https://jrsoftware.org/isinfo.php)
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Installer
```

Có thể mở `src\SteamConnectToolkit\SteamConnectToolkit.csproj` bằng Visual Studio 2022 hoặc `dotnet build` (SDK-style, target `net48`). Mã nguồn giữ tương thích **C# 5** để `csc.exe` có sẵn biên dịch được.

## Cài đặt / chạy

- **Portable**: chạy `SteamConnectToolkit.exe`, Windows hỏi UAC → chọn Yes. File chưa ký số nên SmartScreen có thể cảnh báo ở lần chạy đầu.
- **Bộ cài**: `dist\SteamConnectToolkit-Setup-1.0.0.exe` (Inno Setup, yêu cầu admin, kiểm tra .NET Framework 4.8, tạo lối tắt).
- Dữ liệu của phiên bản cũ trong `%ProgramData%\SteamDnsSwitcher` (kể cả bản sao lưu DNS gốc) được **tự động chuyển** sang `%ProgramData%\SteamConnectToolkit` ở lần chạy đầu.

Nếu chạy không có quyền admin, ứng dụng báo và đề nghị khởi động lại với quyền Administrator.

## Cách dùng nhanh

1. Bấm **1. Kiểm tra kết nối Steam hiện tại**. Mỗi tên miền hiện DNS OK/LỖI, IP, HTTPS OK/LỖI, độ trễ, chứng chỉ.
2. Nếu lỗi: bấm **⚡ Chọn nhanh DNS tốt nhất và áp dụng ngay**, hoặc chọn DNS trong hộp chọn rồi bấm **2. Áp dụng DNS đã chọn**.
3. Ứng dụng tự kiểm tra lại. Sau khi đổi DNS, nếu Steam đang mở hãy **khởi động lại Steam**.
4. Nếu mọi DNS đều "DNS OK, HTTPS lỗi" (ISP chặn ở tầng SNI/IP): sang tab **VPN**, thêm cấu hình (máy chủ, giao thức), nhập tài khoản, bấm **⚡ Kết nối VPN và kiểm tra Steam**.
5. Muốn trả về nguyên trạng DNS: nút **4. Thu hồi – khôi phục DNS gốc**. Muốn thoát VPN: **Ngắt kết nối**.

Mặc định chỉ card mạng đang kết nối có gateway (Ethernet / Wi-Fi) được tích chọn; card ảo (Hyper-V, VMware, VPN...) không được chọn sẵn.

## VPN miễn phí: VPN Gate

Tab VPN có mục **VPN Gate** (vpngate.net, dự án học thuật của Đại học Tsukuba, Nhật Bản): hàng trăm máy chủ VPN công cộng miễn phí, không cần tài khoản (tên đăng nhập và mật khẩu đều là `vpn`, L2TP pre-shared key `vpn`).

- **Tìm máy chủ VPN Gate**: tải danh sách, lọc máy chủ hỗ trợ giao thức Windows (SSTP, L2TP/IPsec), đo TCP tới 30 máy chủ đầu, xếp hạng: tới được → có SSTP → quốc gia gần Việt Nam (JP, KR, TH, SG, HK, TW...) → máy chủ chính thức `public-vpn-*` (★) → điểm → độ trễ.
- **Tạo cấu hình, kết nối và kiểm tra Steam**: tạo cấu hình VPN Windows cho máy chủ đã chọn, kết nối bằng vpn/vpn, lấy IP công cộng rồi kiểm tra 5 tên miền Steam.
- **SSTP** được ưu tiên: chạy trên TCP 443 (gần như không bị firewall chặn), chứng chỉ `*.opengw.net` hợp lệ sẵn, không vướng lỗi NAT. Đã kiểm thử thật từ Việt Nam tới `public-vpn-120.opengw.net`: kết nối trong ~2 giây, Steam truy cập được qua VPN.
- **L2TP/IPsec** là lựa chọn dự phòng; nếu máy nằm sau router NAT, app đề nghị đặt khoá registry `AssumeUDPEncapsulationContextOnSendRule = 2` (cần khởi động lại Windows để chắc chắn).
- Máy chủ VPN Gate do tình nguyện viên vận hành và **ghi log ~2 tuần** theo chính sách chống lạm dụng của dự án: chỉ nên dùng để truy cập Steam, không đăng nhập dịch vụ nhạy cảm qua VPN này.

## Xếp hạng VPN theo mức tương thích với app

App dùng VPN client có sẵn của Windows, nên mức tương thích được xếp như sau (không cần cài thêm gì ở mức 1-2):

| Mức | Loại | Ví dụ | Dùng với app |
|-----|------|-------|--------------|
| 1 – Dùng ngay | Windows SSTP / L2TP công cộng, không cần tài khoản | **VPN Gate** (SSTP ưu tiên, L2TP dự phòng) | Tab VPN → VPN Gate, một nút |
| 2 – Cần tài khoản, không cần phần mềm | Nhà cung cấp có hướng dẫn cài thủ công IKEv2 / L2TP / SSTP với tên đăng nhập + mật khẩu (nhiều gói miễn phí có giới hạn dung lượng) | hide.me, Windscribe (IKEv2), PrivadoVPN, các gói trả phí NordVPN/Surfshark IKEv2, máy chủ riêng (SoftEther, StrongSwan, RRAS) | "Thêm cấu hình VPN…" rồi nhập tài khoản; IKEv2 thường phải cài chứng chỉ CA của nhà cung cấp trước |
| 3 – Cần phần mềm riêng | WireGuard, OpenVPN, SoftEther client, ứng dụng riêng của nhà cung cấp (ProtonVPN Free, Cloudflare WARP, ...) | ProtonVPN, WARP, Mullvad | Không điều khiển được từ app; sau khi bật bằng phần mềm riêng vẫn có thể dùng nút kiểm tra Steam |

Thứ tự ưu tiên thực tế khi Steam bị chặn: đổi DNS (nhanh, không ảnh hưởng tốc độ) → VPN Gate SSTP máy chủ gần (JP/KR/TH) → VPN có tài khoản IKEv2 (ổn định, nhanh hơn) → phần mềm VPN riêng.

## VPN: lưu ý

- Chỉ hỗ trợ các giao thức Windows có sẵn: **IKEv2**, **L2TP/IPsec** (pre-shared key), **SSTP** (TCP 443, khó bị chặn), **PPTP** (cũ, kém an toàn). WireGuard/OpenVPN cần phần mềm riêng, không nằm trong phạm vi.
- Cần tài khoản VPN (trừ VPN Gate): nhà cung cấp có hướng dẫn "manual setup" IKEv2/L2TP, hoặc máy chủ riêng (StrongSwan, SoftEther, Windows Server RRAS...). Nếu nhà cung cấp yêu cầu chứng chỉ CA (IKEv2), cài chứng chỉ trước khi kết nối.
- Không bật **split tunneling** nếu muốn Steam đi qua VPN.
- Khi VPN kết nối, Windows thường dùng DNS của VPN; card VPN cũng xuất hiện trong bảng card mạng và có thể đổi DNS nếu cần.
- Mã lỗi hay gặp được giải thích ngay trong nhật ký (691 sai tài khoản, 800/809 không tới được máy chủ hoặc cổng bị chặn, 868 không phân giải được tên máy chủ, 789 sai PSK, 13801 chứng chỉ không tin cậy...).

## Dữ liệu

Lưu tại `%ProgramData%\SteamConnectToolkit\`:

| File | Nội dung |
|------|----------|
| `config.json` | Danh sách DNS (bật/tắt, kết quả gần nhất, DNS tự thêm), tuỳ chọn, tài khoản VPN đã lưu (mật khẩu mã hoá DPAPI) |
| `original-dns.json` | DNS gốc của từng card đã đổi (xoá khi thu hồi thành công) |
| `history.json` | Lịch sử |
| `app.log` | Nhật ký chi tiết |

## Kiểm thử

`dist\SmokeTest.exe` dùng chung mã nguồn Models/Services với ứng dụng, chạy không cần quyền admin và không đổi DNS/VPN thật:

| Lệnh | Nội dung |
|------|----------|
| `SmokeTest.exe selftest` | Bộ tự kiểm thử (~110 kiểm tra): hợp lệ hoá địa chỉ, danh mục DNS, cấu hình (file hỏng, file rỗng, địa chỉ bị sửa tay, di trú phiên bản cũ), JSON/CSV, lịch sử, card mạng, **mô phỏng áp dụng/thu hồi DNS** (netsh giả lập qua hook `DnsApplier.CommandRunner`), xếp hạng, huỷ thao tác, **VPN** (phân tích Get-VpnConnection, script Add/Remove, DPAPI, RasDial với cấu hình không tồn tại → mã 623, tạo cấu hình tạm tới 192.0.2.1 và quay số thất bại có mã lỗi, rồi xoá), và các tình huống mạng bất thường (NXDOMAIN, máy chủ không phản hồi, IP giả với chứng chỉ sai tên, cổng 443 chết, DNS phụ thay DNS chính). Mã thoát 0 = đạt, 2 = có lỗi. Thêm `--no-network` để bỏ qua phần cần internet. |
| `SmokeTest.exe google quad9 --no-discovery` | Đo thật các DNS chỉ định với 5 tên miền Steam (`all` = toàn bộ danh mục). |

Việc **áp dụng và thu hồi DNS thật** cần quyền Administrator và **kết nối VPN thật** cần tài khoản VPN, nên chỉ kiểm tra được bằng tay.

## Bảo đảm sao lưu và khôi phục

Mục tiêu: **khi app đóng, cấu hình ban đầu được khôi phục đầy đủ**, không để DNS hay VPN bị thay đổi ngầm mà người dùng không biết.

| Cơ chế | Cách hoạt động |
|--------|----------------|
| Sao lưu trước thay đổi đầu tiên | `original-dns.json` ghi DNS gốc (tĩnh/DHCP, địa chỉ, GUID card) trước khi netsh chạy; chỉ xoá khi khôi phục thành công. |
| Áp dụng DNS có giao dịch | Sau khi chạy netsh, app đọc lại registry để xác minh đúng địa chỉ đã đặt; nếu lệnh lỗi hoặc không khớp thì **hoàn tác ngay** về cấu hình gốc, không để card ở trạng thái dở dang. |
| Theo dõi phiên (`session.json`) | Ghi PID, VPN nào do app kết nối, phiên có kết thúc sạch không. Cấu hình VPN do app tạo được ghi trong `config.json` (phân biệt VPN Gate tự động / người dùng tự nhập). |
| Thu hồi toàn bộ (nút 4) | Ngắt VPN do app kết nối → trả DNS gốc (tìm card theo GUID) → xoá cấu hình VPN Gate do app tạo → flush DNS. Mọi bước idempotent. |
| Khi thoát app | Nếu còn thay đổi: hỏi **Khôi phục rồi thoát** (mặc định) / **Giữ thay đổi** / **Huỷ**. Chọn giữ được ghi vào lịch sử. Hộp kiểm "Giữ thay đổi sau khi thoát app" tắt hỏi và giữ luôn (người dùng chủ động). Khi Windows tắt: khôi phục ngay, không hỏi. |
| Guardian khi app dừng đột ngột | Lúc khởi động, app chạy kèm một bản sao ẩn `SteamConnectToolkit.exe --guard <pid>`. Nếu app bị crash hoặc bị End task mà phiên chưa đánh dấu sạch, guardian tự khôi phục DNS/VPN và ghi lịch sử "Khôi phục tự động". |
| Kiểm tra khi khởi động | Nếu phiên trước không sạch (guardian cũng thất bại) hoặc còn dữ liệu từ phiên bản cũ: hỏi khôi phục ngay. Nếu phiên trước chủ động giữ thay đổi: nhắc trong nhật ký và trạng thái. |
| Card mạng không còn | Giữ bản sao lưu, hỏi có bỏ không; không bao giờ xoá bản sao lưu khi chưa khôi phục được. |

## Các tình huống đã được xử lý

- **Máy đã đặt sẵn DNS công cộng** (ví dụ Google tĩnh): bản sao lưu ghi đúng trạng thái đó và nhật ký cảnh báo rằng "thu hồi" sẽ trả về cấu hình này chứ không phải DNS của ISP.
- **Áp dụng nhiều lần**: chỉ sao lưu một lần trước thay đổi đầu tiên.
- **Card mạng bị đổi tên**: thu hồi tìm card theo GUID. **Card bị tắt hoặc tháo**: bỏ qua, giữ bản sao lưu, hỏi có xoá bản sao lưu không.
- **DNS không có IPv6**: DNS IPv6 của card được xoá thay vì giữ DNS IPv6 của ISP.
- **Lỗi một phần khi chạy netsh**: báo riêng từng lệnh, giữ bản sao lưu để thử lại.
- **Mất mạng, DNS chết, DNS trả IP giả, cổng 443 bị chặn, chứng chỉ sai**: mỗi tên miền báo rõ loại lỗi; DNS phụ được dùng khi DNS chính không trả lời; lỗi bất ngờ ở một DNS không làm hỏng cả đợt đo.
- **Nhập địa chỉ thiếu octet** ("8.8.8"): bị từ chối (.NET mặc định chấp nhận thành 8.8.0.8).
- **Tải danh sách DNS từ internet chậm/thất bại**: timeout 20 giây, báo trong báo cáo.
- **File cấu hình/lịch sử hỏng, file bị khoá**: dùng mặc định, giữ bản `.corrupt`, không để sót file tạm; địa chỉ DNS có sẵn luôn lấy từ danh mục trong chương trình.
- **VPN**: cấu hình không tồn tại, máy chủ không phản hồi, sai tài khoản, cổng bị chặn → mã lỗi kèm gợi ý; IP công cộng không đổi sau khi kết nối → cảnh báo split tunneling; trạng thái kết nối được theo dõi qua card mạng mỗi 5 giây (độc lập ngôn ngữ Windows).
- **Dừng giữa chừng**, **màn hình DPI cao**, **chạy không có quyền admin**, **chạy hai bản cùng lúc**.

## Cấu trúc dự án

```
build.ps1                        Biên dịch bằng csc.exe (có sẵn), tuỳ chọn SmokeTest / Installer
src/SteamConnectToolkit/
  Program.cs                     Khởi động, kiểm tra Windows 10+, quyền admin, single instance
  app.manifest                   requireAdministrator, DPI aware, Windows 10/11
  Models/                        DnsProvider, AdapterDnsState, HistoryEntry, AppConfig, CheckResults, VpnModels
  Services/
    DnsResolver.cs               Truy vấn DNS UDP tới máy chủ chỉ định
    HttpsProbe.cs                TCP + TLS (SNI, xác thực chứng chỉ) + HEAD
    SteamChecker.cs              Kiểm tra 5 tên miền Steam
    ProviderTester.cs            Đo nhiều DNS song song, xếp hạng
    NetworkAdapterService.cs     Liệt kê card mạng, đọc DNS tĩnh/DHCP từ registry
    DnsApplier.cs                netsh set/add dnsservers, ipconfig /flushdns (có hook kiểm thử)
    BackupService.cs             Sao lưu / thu hồi DNS gốc
    HistoryService.cs            Lịch sử + xuất CSV
    ProviderCatalog.cs           Danh mục DNS, đọc/ghi config.json, di trú cấu hình
    DiscoveryService.cs          Tải & lọc DNS công cộng từ public-dns.info
    VpnService.cs                Get/Add/Remove-VpnConnection, RasDial, rasdial /disconnect, IP công cộng
    CredentialStore.cs           Lưu tài khoản VPN (DPAPI)
    JsonStore.cs, AppPaths.cs (di trú thư mục cũ), AdminHelper.cs
  UI/MainForm.cs, AddProviderForm.cs, AddVpnForm.cs
tests/SmokeTest/                 Console kiểm thử lõi
installer/SteamConnectToolkit.iss  Inno Setup
tools/make-icon.ps1              Tạo assets/app.ico
tools/check-secrets.ps1          Quét khoá/token/mật khẩu trước khi commit và phát hành
tools/publish-release.ps1        Tạo GitHub Release và tải gói lên (token từ GITHUB_TOKEN / Git Credential Manager)
release.ps1                      Build → kiểm thử → quét bí mật → gói portable, checksum, latest.json
```

## Giấy phép

MIT License © 2026 Quynh Nguyen. Xem file `LICENSE`.
