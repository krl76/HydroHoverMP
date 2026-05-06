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
    }
}