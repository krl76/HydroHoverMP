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

        // Kicks off (or confirms) a dedicated-server records fetch. Returns true if records will
        // arrive asynchronously via OnDedicatedRecordsUpdated (a query is now/already in flight);
        // false if nothing was started (local mode, live session, no connection service, or the
        // query could not start) — in which case the caller should render last-known records now.
        bool RequestDedicatedRecords();

        // Raised when a dedicated-server menu query delivers fresh records (or an empty/timeout
        // result). The window re-reads GetTopRecords on this signal.
        event System.Action OnDedicatedRecordsUpdated;

        // Tears down any in-flight dedicated-server query connection (window closed/timed out).
        void CancelDedicatedRecordsRequest();
    }
}
