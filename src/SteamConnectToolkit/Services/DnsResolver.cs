using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SteamConnectToolkit.Services
{
    public enum DnsRecordType
    {
        A = 1,
        AAAA = 28
    }

    public class DnsQueryResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int Rcode { get; set; }
        public double ElapsedMs { get; set; }
        public bool Truncated { get; set; }
        public List<IPAddress> Addresses { get; set; }
        public List<string> Cnames { get; set; }

        public DnsQueryResult()
        {
            Addresses = new List<IPAddress>();
            Cnames = new List<string>();
        }
    }

    /// <summary>
    /// Trình phân giải DNS tối giản qua UDP/53, cho phép hỏi trực tiếp một máy chủ DNS cụ thể
    /// (.NET Framework không có API hỏi DNS theo máy chủ chỉ định).
    /// </summary>
    public static class DnsResolver
    {
        private static readonly Random Rng = new Random();
        private static readonly object RngLock = new object();

        public static DnsQueryResult Query(IPAddress server, string host, DnsRecordType type, int timeoutMs)
        {
            var result = new DnsQueryResult();
            ushort id;
            lock (RngLock) { id = (ushort)Rng.Next(1, 65535); }

            byte[] packet;
            try { packet = BuildQuery(id, host, type); }
            catch (Exception ex)
            {
                result.Error = "Tên miền không hợp lệ: " + ex.Message;
                return result;
            }

            var sw = Stopwatch.StartNew();
            UdpClient udp = null;
            try
            {
                udp = new UdpClient(server.AddressFamily);
                udp.Client.ReceiveTimeout = timeoutMs;
                udp.Client.SendTimeout = timeoutMs;
                udp.Connect(server, 53);
                udp.Send(packet, packet.Length);

                var remote = new IPEndPoint(server.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                byte[] data;
                while (true)
                {
                    data = udp.Receive(ref remote);
                    if (data.Length >= 2 && ((data[0] << 8) | data[1]) == id) break;
                    int left = timeoutMs - (int)sw.ElapsedMilliseconds;
                    if (left <= 0)
                    {
                        result.Error = "Hết thời gian chờ (" + timeoutMs + " ms)";
                        result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                        return result;
                    }
                    udp.Client.ReceiveTimeout = left;
                }
                sw.Stop();
                result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                Parse(data, result);
            }
            catch (SocketException ex)
            {
                result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                result.Error = ex.SocketErrorCode == SocketError.TimedOut
                    ? "Hết thời gian chờ (" + timeoutMs + " ms)"
                    : "Lỗi mạng: " + ex.SocketErrorCode;
            }
            catch (Exception ex)
            {
                result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                if (udp != null) udp.Close();
            }
            return result;
        }

        private static byte[] BuildQuery(ushort id, string host, DnsRecordType type)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host rỗng");
            host = host.Trim().TrimEnd('.');
            var ms = new System.IO.MemoryStream();
            // Header
            ms.WriteByte((byte)(id >> 8));
            ms.WriteByte((byte)(id & 0xFF));
            ms.WriteByte(0x01); ms.WriteByte(0x00); // Flags: RD
            ms.WriteByte(0x00); ms.WriteByte(0x01); // QDCOUNT = 1
            ms.WriteByte(0x00); ms.WriteByte(0x00); // ANCOUNT
            ms.WriteByte(0x00); ms.WriteByte(0x00); // NSCOUNT
            ms.WriteByte(0x00); ms.WriteByte(0x00); // ARCOUNT
            // Question
            foreach (var label in host.Split('.'))
            {
                var bytes = Encoding.ASCII.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63) throw new ArgumentException("nhãn không hợp lệ: " + label);
                ms.WriteByte((byte)bytes.Length);
                ms.Write(bytes, 0, bytes.Length);
            }
            ms.WriteByte(0x00);
            ms.WriteByte(0x00); ms.WriteByte((byte)type); // QTYPE
            ms.WriteByte(0x00); ms.WriteByte(0x01);       // QCLASS IN
            return ms.ToArray();
        }

        private static void Parse(byte[] data, DnsQueryResult result)
        {
            if (data.Length < 12)
            {
                result.Error = "Phản hồi DNS quá ngắn";
                return;
            }
            int flags = (data[2] << 8) | data[3];
            bool isResponse = (flags & 0x8000) != 0;
            result.Truncated = (flags & 0x0200) != 0;
            result.Rcode = flags & 0x000F;
            int qdCount = (data[4] << 8) | data[5];
            int anCount = (data[6] << 8) | data[7];

            if (!isResponse)
            {
                result.Error = "Phản hồi không hợp lệ (không phải response)";
                return;
            }
            if (result.Rcode != 0)
            {
                result.Error = "DNS trả về " + RcodeName(result.Rcode);
                return;
            }

            try
            {
                int offset = 12;
                for (int i = 0; i < qdCount; i++)
                {
                    ReadName(data, ref offset);
                    offset += 4;
                }
                for (int i = 0; i < anCount; i++)
                {
                    ReadName(data, ref offset);
                    if (offset + 10 > data.Length) break;
                    int type = (data[offset] << 8) | data[offset + 1];
                    int rdLen = (data[offset + 8] << 8) | data[offset + 9];
                    offset += 10;
                    int rdataOffset = offset;
                    if (rdataOffset + rdLen > data.Length) break;

                    if (type == 1 && rdLen == 4)
                    {
                        var b = new byte[4];
                        Array.Copy(data, rdataOffset, b, 0, 4);
                        result.Addresses.Add(new IPAddress(b));
                    }
                    else if (type == 28 && rdLen == 16)
                    {
                        var b = new byte[16];
                        Array.Copy(data, rdataOffset, b, 0, 16);
                        result.Addresses.Add(new IPAddress(b));
                    }
                    else if (type == 5)
                    {
                        int tmp = rdataOffset;
                        result.Cnames.Add(ReadName(data, ref tmp));
                    }
                    offset = rdataOffset + rdLen;
                }
            }
            catch (Exception ex)
            {
                result.Error = "Không đọc được phản hồi DNS: " + ex.Message;
                return;
            }

            if (result.Addresses.Count == 0)
            {
                result.Error = result.Truncated ? "Phản hồi bị cắt (TC)" : "Không có bản ghi địa chỉ";
                return;
            }
            result.Success = true;
        }

        private static string ReadName(byte[] data, ref int offset)
        {
            var sb = new StringBuilder();
            int pos = offset;
            int endOffset = -1;
            int jumps = 0;
            while (true)
            {
                if (pos >= data.Length) throw new IndexOutOfRangeException("name vượt quá gói");
                int len = data[pos];
                if (len == 0)
                {
                    pos++;
                    break;
                }
                if ((len & 0xC0) == 0xC0)
                {
                    if (pos + 1 >= data.Length) throw new IndexOutOfRangeException("pointer hỏng");
                    int ptr = ((len & 0x3F) << 8) | data[pos + 1];
                    if (endOffset < 0) endOffset = pos + 2;
                    pos = ptr;
                    if (++jumps > 32) throw new InvalidOperationException("vòng lặp pointer");
                    continue;
                }
                pos++;
                if (pos + len > data.Length) throw new IndexOutOfRangeException("label vượt quá gói");
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(data, pos, len));
                pos += len;
            }
            offset = endOffset >= 0 ? endOffset : pos;
            return sb.ToString();
        }

        private static string RcodeName(int rcode)
        {
            switch (rcode)
            {
                case 1: return "FORMERR";
                case 2: return "SERVFAIL";
                case 3: return "NXDOMAIN (tên miền không tồn tại)";
                case 4: return "NOTIMP";
                case 5: return "REFUSED (từ chối)";
                default: return "RCODE " + rcode;
            }
        }
    }
}
