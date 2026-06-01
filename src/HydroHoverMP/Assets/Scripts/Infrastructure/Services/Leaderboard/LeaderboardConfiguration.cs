using System;
using Infrastructure.Services.Network;

namespace Infrastructure.Services.Leaderboard
{
    [Serializable]
    public sealed class LeaderboardConfiguration
    {
        public LeaderboardSourceMode SourceMode = LeaderboardSourceMode.Local;
        public DedicatedServerConfiguration DedicatedServer = new();

        public bool UseDedicatedServer => SourceMode == LeaderboardSourceMode.DedicatedServer;

        public string NormalizedDedicatedServerAddress => (DedicatedServer ?? new DedicatedServerConfiguration()).NormalizedAddress;

        public ushort NormalizedDedicatedServerPort => (DedicatedServer ?? new DedicatedServerConfiguration()).NormalizedPort;
    }
}
