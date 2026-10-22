using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SteamConnectToolkit.Services
{
    /// <summary>Đọc/ghi JSON bằng JavaScriptSerializer có sẵn trong .NET Framework (không cần thư viện ngoài).</summary>
    public static class JsonStore
    {
        private static readonly object Sync = new object();

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        }

        public static T Load<T>(string path) where T : class, new()
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(path)) return new T();
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(json)) return new T();
                    var obj = CreateSerializer().Deserialize<T>(json);
                    return obj ?? new T();
                }
                catch (Exception)
                {
                    // File hỏng: giữ lại bản sao để người dùng xem, dùng mặc định.
                    try { File.Copy(path, path + ".corrupt", true); } catch (Exception) { }
                    return new T();
                }
            }
        }

        public static void Save<T>(string path, T obj)
        {
            lock (Sync)
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string json = CreateSerializer().Serialize(obj);
                string tmp = path + ".tmp";
                try
                {
                    File.WriteAllText(tmp, Pretty(json), new UTF8Encoding(false));
                    try
                    {
                        // Thay thế nguyên tử; File.Replace chịu được trường hợp file đích đang bị phần mềm khác (AV, indexer) giữ handle.
                        if (File.Exists(path)) File.Replace(tmp, path, null);
                        else File.Move(tmp, path);
                    }
                    catch (Exception)
                    {
                        // Dự phòng (ổ đĩa không hỗ trợ Replace, v.v.): ghi đè trực tiếp. Nếu vẫn lỗi thì ném ra cho nơi gọi xử lý.
                        File.Copy(tmp, path, true);
                    }
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>Định dạng JSON cho dễ đọc (JavaScriptSerializer chỉ xuất 1 dòng).</summary>
        public static string Pretty(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            var sb = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"':
                        inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        {
                            char close = c == '{' ? '}' : ']';
                            int j = i + 1;
                            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                            if (j < json.Length && json[j] == close)
                            {
                                sb.Append(c).Append(close);
                                i = j;
                            }
                            else
                            {
                                sb.Append(c);
                                indent++;
                                NewLine(sb, indent);
                            }
                            break;
                        }
                    case '}':
                    case ']':
                        indent--;
                        NewLine(sb, indent);
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        NewLine(sb, indent);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(c)) sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void NewLine(StringBuilder sb, int indent)
        {
            sb.AppendLine();
            sb.Append(' ', Math.Max(0, indent) * 2);
        }
    }
}
