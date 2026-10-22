using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    public static class HistoryService
    {
        private const int MaxEntries = 2000;
        private static readonly object Sync = new object();

        public static List<HistoryEntry> Load()
        {
            lock (Sync)
            {
                var d = JsonStore.Load<HistoryData>(AppPaths.HistoryFile);
                return d.Entries ?? new List<HistoryEntry>();
            }
        }

        public static void Add(string action, string adapter, string detail, string result)
        {
            lock (Sync)
            {
                var d = JsonStore.Load<HistoryData>(AppPaths.HistoryFile);
                if (d.Entries == null) d.Entries = new List<HistoryEntry>();
                d.Entries.Add(new HistoryEntry
                {
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Action = action,
                    Adapter = adapter,
                    Detail = detail,
                    Result = result
                });
                if (d.Entries.Count > MaxEntries) d.Entries.RemoveRange(0, d.Entries.Count - MaxEntries);
                JsonStore.Save(AppPaths.HistoryFile, d);
            }
        }

        public static void Clear()
        {
            lock (Sync) { JsonStore.Save(AppPaths.HistoryFile, new HistoryData()); }
        }

        public static void ExportCsv(string path)
        {
            var entries = Load();
            var sb = new StringBuilder();
            sb.AppendLine("Thời gian,Hành động,Card mạng,Chi tiết,Kết quả");
            foreach (var e in entries)
            {
                sb.AppendLine(string.Join(",", Csv(e.Time), Csv(e.Action), Csv(e.Adapter), Csv(e.Detail), Csv(e.Result)));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
