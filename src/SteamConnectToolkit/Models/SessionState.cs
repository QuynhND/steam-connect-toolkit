using System;
using System.Collections.Generic;

namespace SteamConnectToolkit.Models
{
    /// <summary>Cấu hình VPN do app tạo (lưu trong config để theo dõi qua nhiều phiên).</summary>
    public class VpnProfileRecord
    {
        public string Name { get; set; }
        /// <summary>true = tạo tự động (VPN Gate) → được xoá khi khôi phục; false = người dùng tự nhập → giữ lại.</summary>
        public bool Auto { get; set; }
        public string CreatedAt { get; set; }
    }

    /// <summary>
    /// Trạng thái một phiên chạy của app (session.json). Dùng để:
    /// - biết phiên trước có kết thúc bình thường không (CleanExit),
    /// - biết app đã kết nối những VPN nào để ngắt khi thoát,
    /// - cho tiến trình guardian khôi phục khi app dừng đột ngột.
    /// </summary>
    public class SessionState
    {
        public int Pid { get; set; }
        public string StartedAt { get; set; }
        public string EndedAt { get; set; }
        public bool CleanExit { get; set; }
        /// <summary>Người dùng chủ động giữ thay đổi khi thoát.</summary>
        public bool KeepOnExit { get; set; }
        public string Note { get; set; }
        public List<string> VpnConnectedByApp { get; set; }

        public SessionState()
        {
            VpnConnectedByApp = new List<string>();
        }
    }
}
