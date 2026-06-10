using System;
using System.Net;
using Core.States.Base;
using Core.States.MainMenu;
using Data;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using UnityEngine;
using Zenject;

namespace Infrastructure.Services.Network
{
    public sealed class NetworkConnectionService : INetworkConnectionService, IInitializable, IDisposable
    {
        private const string LocalhostAddress = DedicatedServerConfiguration.DefaultAddress;
        private const ushort DefaultPort = DedicatedServerConfiguration.DefaultPort;
        private const string DedicatedServerArg = "-dedicatedServer";
        private const string ServerOnlyArg = "-serverOnly";
        private const string PortArg = "-port";
        private const string ServerPortArg = "-serverPort";

        private NetworkManager _networkManager;
        private readonly DedicatedServerConfiguration _dedicatedServerConfiguration;
        private readonly GameStateMachine _stateMachine;
        private bool _subscribed;
        private bool _stopRequested;
        private bool _clientStartRequested;
        private bool _gameplayGlobalSceneLoaded;
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

        public NetworkConnectionService(DedicatedServerConfiguration dedicatedServerConfiguration = null, GameStateMachine stateMachine = null)
        {
            _dedicatedServerConfiguration = dedicatedServerConfiguration ?? new DedicatedServerConfiguration();
            _stateMachine = stateMachine;
        }

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

            Debug.Log($"[NetworkConnectionService] Server-only start requested on port {port}. Connected clients: {ConnectedClientCount}.");
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

        public ushort ResolveServerPort()
        {
            string[] args = Environment.GetCommandLineArgs();
            ushort port = ConfiguredDefaultPort;
            if (args == null) return port;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrWhiteSpace(arg)) continue;

                if (TryReadPortArg(args, i, arg, port, out ushort parsedPort, out bool consumedNext, out string portError))
                {
                    port = parsedPort;
                    if (consumedNext) i++;
                }
                else if (!string.IsNullOrWhiteSpace(portError))
                {
                    Debug.LogError($"[NetworkConnectionService] {portError} Falling back to port {port}.");
                }
            }

            return port;
        }

        private static bool TryGetCommandLineServerPort(string[] args, out ushort port, out string error)
        {
            return TryGetCommandLineServerPortWithDefault(args, DefaultPort, out port, out error);
        }

        private static bool TryGetCommandLineServerPortWithDefault(string[] args, ushort defaultPort, out ushort port, out string error)
        {
            ushort fallbackPort = defaultPort == 0 ? DefaultPort : defaultPort;
            port = fallbackPort;
            error = null;
            if (args == null || args.Length == 0) return false;

            bool serverRequested = false;
            bool invalidPortFlag = false;
            string invalidPortError = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrWhiteSpace(arg)) continue;

                if (IsDedicatedServerArg(arg))
                {
                    serverRequested = true;
                    continue;
                }

                if (!TryReadPortArg(args, i, arg, fallbackPort, out ushort parsedPort, out bool consumedNext, out string portError))
                {
                    if (!string.IsNullOrWhiteSpace(portError))
                    {
                        invalidPortFlag = true;
                        invalidPortError ??= portError;
                    }

                    continue;
                }

                port = parsedPort;
                if (consumedNext) i++;
            }

            if (!serverRequested) return false;
            if (!invalidPortFlag) return true;

            error = invalidPortError ?? $"Dedicated server command-line port is invalid. Use {PortArg} {fallbackPort} or {ServerPortArg} {fallbackPort}.";
            return false;
        }

        private ushort ConfiguredDefaultPort => _dedicatedServerConfiguration.NormalizedPort;

        private static bool IsDedicatedServerArg(string arg)
        {
            return string.Equals(arg, DedicatedServerArg, StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, ServerOnlyArg, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadPortArg(string[] args, int index, string arg, ushort defaultPort, out ushort port, out bool consumedNext, out string error)
        {
            port = defaultPort;
            consumedNext = false;
            error = null;

            if (TryReadInlinePort(arg, PortArg, defaultPort, out port, out error) || TryReadInlinePort(arg, ServerPortArg, defaultPort, out port, out error))
                return string.IsNullOrWhiteSpace(error);

            if (!string.Equals(arg, PortArg, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(arg, ServerPortArg, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int valueIndex = index + 1;
            if (valueIndex >= args.Length)
            {
                error = $"Dedicated server command-line port flag '{arg}' is missing a value. Use {arg} {defaultPort}.";
                return false;
            }

            string value = args[valueIndex];
            if (!TryParseCommandLinePort(value, out port))
            {
                error = $"Dedicated server command-line port '{value}' from '{arg}' is invalid. Enter a number from 1 to {ushort.MaxValue}.";
                return false;
            }

            consumedNext = true;
            return true;
        }

        private static bool TryReadInlinePort(string arg, string key, ushort defaultPort, out ushort port, out string error)
        {
            port = defaultPort;
            error = null;
            string prefix = key + "=";
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string value = arg.Substring(prefix.Length);
            if (TryParseCommandLinePort(value, out port)) return true;

            error = $"Dedicated server command-line port '{value}' from '{key}' is invalid. Enter a number from 1 to {ushort.MaxValue}.";
            return true;
        }

        private static bool TryParseCommandLinePort(string value, out ushort port)
        {
            port = DefaultPort;
            if (!ushort.TryParse(value, out ushort parsedPort) || parsedPort == 0)
                return false;

            port = parsedPort;
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

                // A stop we did not request means the server/host dropped us mid-session.
                bool unexpected = !_stopRequested;
                _clientStartRequested = false;
                RefreshStatus();

                if (unexpected)
                    HandleUnexpectedClientDisconnect();
            }
            else if (args.ConnectionState == LocalConnectionState.Starting)
                SetStatus(NetworkConnectionStatus.StartingClient);
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _stopRequested = false;
                LoadGameplayGlobalSceneOnce();
                if (_clientStartRequested)
                    SetStatus(NetworkConnectionStatus.StartingHost);
                else
                    RefreshStatus();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                _gameplayGlobalSceneLoaded = false;
                RefreshStatus();
                _stopRequested = false;
            }
            else if (args.ConnectionState == LocalConnectionState.Starting)
                SetStatus(_clientStartRequested ? NetworkConnectionStatus.StartingHost : NetworkConnectionStatus.StartingServer);
        }

        // Берём на себя загрузку сетевой online-сцены. Раньше это делал FishNet DefaultScene, но он же
        // перезагружал offline-сцену (Bootstrap) при смене состояния, повторно запуская весь бутстрап и
        // загружая Gameplay второй раз. DefaultScene отключён в NetworkBootstrapper, а здесь грузим Gameplay
        // как глобальную сцену ровно один раз — подключающиеся клиенты получают её автоматически.
        private void LoadGameplayGlobalSceneOnce()
        {
            if (_gameplayGlobalSceneLoaded) return;
            if (_networkManager == null || _networkManager.SceneManager == null) return;
            // Один сервер уже стартовал (callback пришёл), грузим один раз — как делал DefaultScene.
            if (!_networkManager.ServerManager.IsOnlyOneServerStarted()) return;

            _gameplayGlobalSceneLoaded = true;
            SceneLoadData sceneLoadData = new(ScenesPaths.GAMEPLAY_SCENE);
            _networkManager.SceneManager.LoadGlobalScenes(sceneLoadData);
            Debug.Log($"[NetworkConnectionService] Loading global scene '{ScenesPaths.GAMEPLAY_SCENE}' on server start.");
        }

        private void OnRemoteConnectionState(FishNet.Connection.NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            int clientCount = ConnectedClientCount;
            Debug.Log($"[NetworkConnectionService] Client {connection.ClientId} {args.ConnectionState}. Connected clients: {clientCount}.");
            OnClientCountChanged?.Invoke(clientCount);
        }

        // The server/host vanished without this client asking to leave (host returned to the
        // menu, crashed, or timed out). Send the player back to the main menu gracefully instead
        // of stranding them in an orphaned gameplay scene.
        private void HandleUnexpectedClientDisconnect()
        {
            if (ServerEnvironment.IsDedicatedServer) return;
            if (_stateMachine == null) return;
            // A host is also a server; if our own server is still up this is not a host-left event.
            if (_networkManager != null && _networkManager.IsServerStarted) return;

            Debug.Log("[NetworkConnectionService] Connection closed by the host. Returning to the main menu.");
            OnConnectionFailed?.Invoke("Host closed the session. Returning to the main menu.");
            _stateMachine.Enter<MainMenuState>();
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
