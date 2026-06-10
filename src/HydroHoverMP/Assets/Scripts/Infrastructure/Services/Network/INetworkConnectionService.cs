using System;

namespace Infrastructure.Services.Network
{
    public interface INetworkConnectionService
    {
        NetworkConnectionStatus Status { get; }
        int ConnectedClientCount { get; }
        bool IsHost { get; }
        bool IsClient { get; }
        bool IsServer { get; }

        event Action<NetworkConnectionStatus> OnStatusChanged;
        event Action<int> OnClientCountChanged;
        event Action<string> OnConnectionFailed;

        bool StartHost(ushort port = 7770);
        bool StartClient(string address, ushort port = 7770);
        bool StartServer(ushort port = 7770);
        ushort ResolveServerPort();
        void StopConnection();
        void RefreshStatus();

        // Raised on the client when a leaderboard-only query returns records (may be empty).
        event Action<System.Collections.Generic.IReadOnlyList<Data.Leaderbords.Record>> OnLeaderboardRecordsReceived;

        // Starts a short-lived "leaderboard-only" connection to the configured dedicated server,
        // requests the top `count` records, and stays connected until CancelLeaderboardQuery.
        // Returns false if a connection/query is already active. Result arrives via
        // OnLeaderboardRecordsReceived.
        bool BeginLeaderboardQuery(int count);

        // Aborts an in-flight leaderboard query (e.g. the window closed or timed out).
        void CancelLeaderboardQuery();
    }
}
