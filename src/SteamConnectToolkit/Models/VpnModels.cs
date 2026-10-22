using System;
using System.Collections.Generic;

namespace SteamConnectToolkit.Models
{
    /// <summary>Một cấu hình VPN tích hợp của Windows (đọc từ Get-VpnConnection).</summary>
    public class VpnProfile
    {
        public string Name { get; set; }
        public string Server { get; set; }
        /// <summary>Ikev2 | L2tp | Sstp | Pptp | Automatic</summary>
        public string TunnelType { get; set; }
        /// <summary>Trạng thái Windows báo: Connected | Disconnected | Connecting ...</summary>
        public string Status { get; set; }
        public string Authentication { get; set; }
        public string Encryption { get; set; }
        public bool SplitTunneling { get; set; }
        public bool RememberCredential { get; set; }
        /// <summary>true = cấu hình dùng chung cho mọi người dùng (AllUserConnection).</summary>
        public bool AllUsers { get; set; }
        /// <summary>Đã kết nối (kiểm tra qua card mạng, độc lập ngôn ngữ Windows).</summary>
        public bool IsConnected { get; set; }

        public string TunnelTypeText()
        {
            switch ((TunnelType ?? "").ToLowerInvariant())
            {
                case "ikev2": return "IKEv2";
                case "l2tp": return "L2TP/IPsec";
                case "sstp": return "SSTP";
                case "pptp": return "PPTP";
                case "automatic": return "Tự động";
                default: return string.IsNullOrEmpty(TunnelType) ? "?" : TunnelType;
            }
        }

        public string StatusText() { return IsConnected ? "Đã kết nối" : "Chưa kết nối"; }
        public string ScopeText() { return AllUsers ? "Mọi người dùng" : "Người dùng này"; }

        public override string ToString()
        {
            return Name + " (" + Server + ", " + TunnelTypeText() + ")";
        }
    }

    /// <summary>Thông tin để tạo cấu hình VPN mới.</summary>
    public class VpnProfileSpec
    {
        public string Name { get; set; }
        public string Server { get; set; }
        public string TunnelType { get; set; }
        public string L2tpPsk { get; set; }
        public bool RememberCredential { get; set; }
        public bool AllUsers { get; set; }
        public bool SplitTunneling { get; set; }

        public VpnProfileSpec()
        {
            TunnelType = "Ikev2";
            RememberCredential = true;
        }

        /// <summary>Trả về thông báo lỗi, hoặc null nếu hợp lệ.</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Tên cấu hình VPN không được để trống.";
            if (Name.Trim().Length > 256) return "Tên cấu hình quá dài (tối đa 256 ký tự).";
            foreach (char c in Name)
                if (char.IsControl(c) || c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                    return "Tên cấu hình chứa ký tự không hợp lệ: " + c;
            if (string.IsNullOrWhiteSpace(Server)) return "Địa chỉ máy chủ VPN không được để trống.";
            if (Server.Trim().IndexOfAny(new[] { ' ', '\'', '"', ';', '\r', '\n' }) >= 0) return "Địa chỉ máy chủ không hợp lệ.";
            string t = (TunnelType ?? "").ToLowerInvariant();
            if (t != "ikev2" && t != "l2tp" && t != "sstp" && t != "pptp" && t != "automatic") return "Loại giao thức không hợp lệ.";
            return null;
        }
    }

    public class VpnResult
    {
        public bool Ok { get; set; }
        public int ErrorCode { get; set; }
        public string Message { get; set; }
        public double ElapsedMs { get; set; }
    }

    /// <summary>Tài khoản VPN lưu trong config (mật khẩu đã mã hoá DPAPI).</summary>
    public class VpnCredential
    {
        public string Profile { get; set; }
        public string UserName { get; set; }
        public string PasswordProtected { get; set; }
    }
}
