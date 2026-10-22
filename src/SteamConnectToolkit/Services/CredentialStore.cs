using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>Lưu tài khoản VPN trong config; mật khẩu mã hoá bằng DPAPI theo tài khoản Windows hiện tại.</summary>
    public static class CredentialStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SteamConnectToolkit.vpn.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        /// <summary>Giải mã; trả về null nếu không giải mã được (khác tài khoản Windows, dữ liệu hỏng).</summary>
        public static string Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64)) return "";
            try
            {
                var data = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static VpnCredential Find(AppConfig cfg, string profile)
        {
            if (cfg == null || cfg.VpnCredentials == null || string.IsNullOrEmpty(profile)) return null;
            return cfg.VpnCredentials.FirstOrDefault(c => string.Equals(c.Profile, profile, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Lưu (hoặc xoá nếu rememberPassword=false và userName rỗng) tài khoản của một cấu hình VPN.</summary>
        public static void Save(AppConfig cfg, string profile, string userName, string password, bool rememberPassword)
        {
            if (cfg.VpnCredentials == null) cfg.VpnCredentials = new System.Collections.Generic.List<VpnCredential>();
            cfg.VpnCredentials.RemoveAll(c => string.Equals(c.Profile, profile, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(userName) && !rememberPassword) return;
            cfg.VpnCredentials.Add(new VpnCredential
            {
                Profile = profile,
                UserName = userName ?? "",
                PasswordProtected = rememberPassword ? Protect(password) : ""
            });
        }

        public static void Remove(AppConfig cfg, string profile)
        {
            if (cfg.VpnCredentials == null) return;
            cfg.VpnCredentials.RemoveAll(c => string.Equals(c.Profile, profile, StringComparison.OrdinalIgnoreCase));
        }
    }
}
