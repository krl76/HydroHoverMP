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
        void StopConnection();
        void RefreshStatus();
    }
}
