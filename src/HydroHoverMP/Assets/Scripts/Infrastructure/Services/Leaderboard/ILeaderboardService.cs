using System.Collections.Generic;
using Data.Leaderbords;

namespace Infrastructure.Services.Leaderboard
{
    public interface ILeaderboardService
    {
        LeaderboardSourceMode SourceMode { get; }
        string DedicatedServerAddress { get; }
        ushort DedicatedServerPort { get; }
        bool IsUsingDedicatedServer { get; }

        void AddRecord(float time, string nickname);
        List<Record> GetTopRecords(int count);
        float GetBestTime();
        void RequestDedicatedRecords();
    }
}
