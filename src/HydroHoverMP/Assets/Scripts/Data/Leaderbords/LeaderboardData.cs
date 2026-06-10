using System;
using System.Collections.Generic;

namespace Data.Leaderbords
{
    [Serializable]
    public class LeaderboardData
    {
        public List<Record> Records = new List<Record>();
    }

    [Serializable]
    public class Record
    {
        public float Time;
        public long Date;

        // Nickname of the pilot who set this time. Legacy records saved before this field
        // existed deserialize to null/empty; the UI falls back to "Pilot" in that case.
        public string PlayerName;
    }
}