using System;
using System.Net;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using Zenject;

namespace Infrastructure.Services.Network
{
    public sealed class NetworkConnectionService : INetworkConnectionService, IInitializable, IDisposable
    {
        private const string LocalhostAddress = "localhost";

        private NetworkManager _networkManager;
        private bool _subscribed;
        private bool _stopRequested;
        private bool _clientStartRequested;
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
            if (!ValidatePort(port)) return false;
            if (IsAlreadyStarted(NetworkConnectionStatus.HostStarted)) return true;
            if (!CanStartConnection("host")) return false;

            _stopRequested = false;
            _clientStartRequested = true;
            SetStatus(NetworkConnectionStatus.StartingHost);
            bool serverStarted = _networkManager.ServerManager.StartConnection(port);
            if (!serverStarted)
            {
                Fail($"Failed to start FishNet host server on port {port}. The port may already be in use or Tugboat may be missing.");
                return false;
            }

            bool clientStarted = _networkManager.ClientManager.StartConnection(LocalhostAddress, port);
            if (!serverStarted || !clientStarted)
            {
                if (_networkManager.IsServerStarted)
                    _networkManager.ServerManager.StopConnection(true);

                _clientStartRequested = false;
                Fail($"Failed to start FishNet host client for {LocalhostAddress}:{port}. Host startup was rolled back.");
                return false;
            }

            return true;
        }

        public bool StartClient(string address, ushort port = 7770)
        {
            if (!EnsureNetworkManager()) return false;
            if (!ValidatePort(port)) return false;
            if (IsAlreadyStarted(NetworkConnectionStatus.ClientStarted)) return true;
            if (!CanStartConnection("client")) return false;
            if (!TryNormalizeAddress(address, out string normalizedAddress)) return false;

            _stopRequested = false;
            _clientStartRequested = true;
            SetStatus(NetworkConnectionStatus.StartingClient);
            bool started = _networkManager.ClientManager.StartConnection(normalizedAddress, port);
            if (!started)
            {
                Fail($"Failed to start FishNet client for {normalizedAddress}:{port}. Check address, port and Tugboat setup.");
                return false;
            }

            return true;
        }

        public bool StartServer(ushort port = 7770)
        {
            if (!EnsureNetworkManager()) return false;
            if (!ValidatePort(port)) return false;
            if (IsAlreadyStarted(NetworkConnectionStatus.ServerStarted)) return true;
            if (!CanStartConnection("server")) return false;

            _stopRequested = false;
            SetStatus(NetworkConnectionStatus.StartingServer);
            bool started = _networkManager.ServerManager.StartConnection(port);
            if (!started)
            {
                Fail($"Failed to start FishNet server on port {port}. The port may already be in use or Tugboat may be missing.");
                return false;
            }

            return true;
        }

        public void StopConnection()
        {
            if (!TryBindNetworkManager())
            {
                SetStatus(NetworkConnectionStatus.Offline);
                OnClientCountChanged?.Invoke(0);
                return;
            }

            if (!_networkManager.IsClientStarted && !_networkManager.IsServerStarted)
            {
                RefreshStatus();
                return;
            }

            _stopRequested = true;
            _clientStartRequested = false;
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

        private bool IsAlreadyStarted(NetworkConnectionStatus targetStatus)
        {
            RefreshStatus();
            return _status == targetStatus;
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

        private bool ValidatePort(ushort port)
        {
            if (port != 0) return true;

            Fail("Port 0 is not valid for HydroHoverMP multiplayer. Use a configured Tugboat port such as 7770.");
            return false;
        }

        private bool CanStartConnection(string requestedMode)
        {
            RefreshStatus();

            if (_status is NetworkConnectionStatus.StartingHost or NetworkConnectionStatus.StartingClient or NetworkConnectionStatus.StartingServer or NetworkConnectionStatus.Stopping)
            {
                Fail($"Cannot start {requestedMode} while connection state is {_status}. Wait for the current operation to finish or stop it first.");
                return false;
            }

            if (_networkManager.IsClientStarted || _networkManager.IsServerStarted)
            {
                Fail($"Cannot start {requestedMode} while another FishNet mode is active. Stop the current connection first.");
                return false;
            }

            return true;
        }

        private bool TryNormalizeAddress(string address, out string normalizedAddress)
        {
            normalizedAddress = string.IsNullOrWhiteSpace(address) ? LocalhostAddress : address.Trim();

            if (normalizedAddress.Length == 0)
            {
                normalizedAddress = LocalhostAddress;
                return true;
            }

            if (ContainsWhitespace(normalizedAddress))
            {
                Fail($"Address '{normalizedAddress}' is invalid. Enter a host name or IP address without spaces.");
                return false;
            }

            bool validIp = IPAddress.TryParse(normalizedAddress, out _);
            bool validHost = Uri.CheckHostName(normalizedAddress) != UriHostNameType.Unknown;
            if (validIp || validHost)
                return true;

            Fail($"Address '{normalizedAddress}' is invalid. Enter a host name or IP address, without protocol or port.");
            return false;
        }

        private static bool ContainsWhitespace(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                    return true;
            }

            return false;
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
            {
                _clientStartRequested = false;
                RefreshStatus();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (_clientStartRequested && !_stopRequested)
                {
                    _clientStartRequested = false;
                    Fail("FishNet client connection stopped before it fully connected. Check address, port, host availability and Tugboat setup.");
                    return;
                }

                _clientStartRequested = false;
                RefreshStatus();
            }
            else if (args.ConnectionState == LocalConnectionState.Starting)
                SetStatus(NetworkConnectionStatus.StartingClient);
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _stopRequested = false;
                RefreshStatus();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                RefreshStatus();
                _stopRequested = false;
            }
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
