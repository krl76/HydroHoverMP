using System;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using Zenject;

namespace Infrastructure.Services.Network
{
    public sealed class NetworkConnectionService : INetworkConnectionService, IInitializable, IDisposable
    {
        private NetworkManager _networkManager;
        private bool _subscribed;
        private NetworkConnectionStatus _status = NetworkConnectionStatus.Offline;

        public NetworkConnectionStatus Status => _status;
        public int ConnectedClientCount => _networkManager != null && _networkManager.ServerManager != null
            ? _networkManager.ServerManager.Clients.Count
            : 0;

        public bool IsHost => _networkManager != null && _networkManager.IsHostStarted;
        public bool IsClient => _networkManager != null && _networkManager.IsClientStarted;
        public bool IsServer => _networkManager != null && _networkManager.IsServerStarted;

        public event Action<NetworkConnectionStatus> OnStatusChanged;
        public event Action<int> OnClientCountChanged;
        public event Action<string> OnConnectionFailed;

        public void Initialize()
        {
            TryBindNetworkManager();
        }

        public void Dispose()
        {
            Unsubscribe();
        }

        public bool StartHost(ushort port = 7770)
        {
            if (!EnsureNetworkManager()) return false;

            SetStatus(NetworkConnectionStatus.StartingHost);
            bool serverStarted = _networkManager.ServerManager.StartConnection(port);
            bool clientStarted = _networkManager.ClientManager.StartConnection("localhost", port);
            if (!serverStarted || !clientStarted)
            {
                Fail("Failed to start FishNet host. Check NetworkManager, TransportManager and Tugboat setup.");
                return false;
            }

            return true;
        }

        public bool StartClient(string address, ushort port = 7770)
        {
            if (!EnsureNetworkManager()) return false;

            string normalizedAddress = string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
            SetStatus(NetworkConnectionStatus.StartingClient);
            bool started = _networkManager.ClientManager.StartConnection(normalizedAddress, port);
            if (!started)
            {
                Fail($"Failed to start FishNet client for {normalizedAddress}:{port}.");
                return false;
            }

            return true;
        }

        public bool StartServer(ushort port = 7770)
        {
            if (!EnsureNetworkManager()) return false;

            SetStatus(NetworkConnectionStatus.StartingServer);
            bool started = _networkManager.ServerManager.StartConnection(port);
            if (!started)
            {
                Fail($"Failed to start FishNet server on port {port}.");
                return false;
            }

            return true;
        }

        public void StopConnection()
        {
            if (!EnsureNetworkManager()) return;

            SetStatus(NetworkConnectionStatus.Stopping);

            if (_networkManager.IsClientStarted)
                _networkManager.ClientManager.StopConnection();

            if (_networkManager.IsServerStarted)
                _networkManager.ServerManager.StopConnection(true);

            RefreshStatus();
        }

        public void RefreshStatus()
        {
            if (!TryBindNetworkManager())
            {
                SetStatus(NetworkConnectionStatus.Offline);
                OnClientCountChanged?.Invoke(0);
                return;
            }

            if (_networkManager.IsHostStarted)
                SetStatus(NetworkConnectionStatus.HostStarted);
            else if (_networkManager.IsServerStarted)
                SetStatus(NetworkConnectionStatus.ServerStarted);
            else if (_networkManager.IsClientStarted)
                SetStatus(NetworkConnectionStatus.ClientStarted);
            else
                SetStatus(NetworkConnectionStatus.Offline);

            OnClientCountChanged?.Invoke(ConnectedClientCount);
        }

        private bool EnsureNetworkManager()
        {
            if (TryBindNetworkManager()) return true;

            Fail("FishNet NetworkManager was not found. Add a NetworkManager with TransportManager + Tugboat to the Bootstrap/MainMenu scene.");
            return false;
        }

        private bool TryBindNetworkManager()
        {
            NetworkManager manager = InstanceFinder.NetworkManager;
            if (manager == null)
                manager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();

            if (manager == null)
                return false;

            if (_networkManager == manager && _subscribed)
                return true;

            Unsubscribe();
            _networkManager = manager;
            Subscribe();
            return true;
        }

        private void Subscribe()
        {
            if (_networkManager == null || _subscribed) return;

            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_networkManager == null || !_subscribed) return;

            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            _subscribed = false;
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
                RefreshStatus();
            else if (args.ConnectionState == LocalConnectionState.Stopped)
                RefreshStatus();
            else if (args.ConnectionState == LocalConnectionState.Starting)
                SetStatus(NetworkConnectionStatus.StartingClient);
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
                RefreshStatus();
            else if (args.ConnectionState == LocalConnectionState.Stopped)
                RefreshStatus();
            else if (args.ConnectionState == LocalConnectionState.Starting)
                SetStatus(NetworkConnectionStatus.StartingServer);
        }

        private void OnRemoteConnectionState(FishNet.Connection.NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            OnClientCountChanged?.Invoke(ConnectedClientCount);
        }

        private void SetStatus(NetworkConnectionStatus next)
        {
            if (_status == next) return;

            _status = next;
            OnStatusChanged?.Invoke(_status);
        }

        private void Fail(string message)
        {
            Debug.LogError($"[NetworkConnectionService] {message}");
            SetStatus(NetworkConnectionStatus.Failed);
            OnConnectionFailed?.Invoke(message);
        }
    }
}
