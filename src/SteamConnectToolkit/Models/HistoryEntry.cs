using System;
using System.Collections.Generic;

namespace SteamConnectToolkit.Models
{
    public class HistoryEntry
    {
        public string Time { get; set; }
        public string Action { get; set; }
        public string Adapter { get; set; }
        public string Detail { get; set; }
        public string Result { get; set; }
    }

    public class HistoryData
    {
        public List<HistoryEntry> Entries { get; set; }

        public HistoryData()
        {
            Entries = new List<HistoryEntry>();
        }
    }
}
