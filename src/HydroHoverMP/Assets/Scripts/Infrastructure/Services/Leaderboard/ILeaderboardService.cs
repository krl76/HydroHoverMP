using System.Collections.Generic;
using Data.Leaderbords;

namespace Infrastructure.Services.Leaderboard
{
    public interface ILeaderboardService
    {
        void AddRecord(float time);
        List<Record> GetTopRecords(int count);
        float GetBestTime();
    }
}