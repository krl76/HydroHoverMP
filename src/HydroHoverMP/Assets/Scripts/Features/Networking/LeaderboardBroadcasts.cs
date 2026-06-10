using System.Collections.Generic;
using System.Linq;
using Data.Leaderbords;
using FishNet.Broadcast;
using UnityEngine;

namespace Features.Networking
{
    // Client -> server: request the top Count records. Receiving this also flags the sender
    // connection as "leaderboard-only" so it is never treated as a racer (see NetworkSessionController).
    public struct LeaderboardQueryBroadcast : IBroadcast
    {
        public int Count;
    }

    // Server -> client: the requested records (may be empty).
    public struct LeaderboardResultBroadcast : IBroadcast
    {
        public NetworkLeaderboardRecord[] Records;
    }

    public static class LeaderboardRecordMapper
    {
        public static NetworkLeaderboardRecord[] TakeTop(IEnumerable<NetworkLeaderboardRecord> source, int count)
        {
            if (source == null) return new NetworkLeaderboardRecord[0];

            return source
                .OrderBy(record => record.Time)
                .ThenBy(record => record.Date)
                .Take(Mathf.Max(0, count))
                .ToArray();
        }

        public static List<Record> ToRecords(IEnumerable<NetworkLeaderboardRecord> source)
        {
            if (source == null) return new List<Record>();
            return source.Select(record => record.ToRecord()).ToList();
        }
    }
}
