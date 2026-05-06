using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

namespace Features.Networking
{
    public enum SessionPhase : byte
    {
        Disconnected = 0,
        Lobby = 1,
        Countdown = 2,
        Race = 3,
        Results = 4
    }

    [DisallowMultipleComponent]
    public sealed class NetworkSessionController : NetworkBehaviour
    {
        [SerializeField] private int _minimumPlayers = 2;
        [SerializeField] private float _countdownSeconds = 3f;

        private readonly Dictionary<int, NetworkPlayerData> _players = new();
        private float _countdownEndsAt;

        public static NetworkSessionController Instance { get; private set; }

        public readonly SyncVar<SessionPhase> Phase = new(SessionPhase.Lobby);
        public readonly SyncVar<int> ConnectedPlayers = new(0);
        public readonly SyncVar<int> ReadyPlayers = new(0);
        public readonly SyncVar<float> CountdownRemaining = new(0f);

        public IReadOnlyCollection<NetworkPlayerData> Players => _players.Values;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Phase.Value = SessionPhase.Lobby;
            NetworkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        public override void OnStopServer()
        {
            if (NetworkManager != null && NetworkManager.ServerManager != null)
                NetworkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;

            _players.Clear();
            base.OnStopServer();
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            if (Phase.Value != SessionPhase.Countdown) return;

            CountdownRemaining.Value = Mathf.Max(0f, _countdownEndsAt - Time.time);
            if (CountdownRemaining.Value <= 0f)
                ServerStartRace();
        }

        public void RegisterPlayer(NetworkPlayerData player)
        {
            if (!IsServerInitialized || player == null) return;

            _players[player.OwnerId] = player;
            ConnectedPlayers.Value = _players.Count;
            RefreshReadyState();
        }

        public void UnregisterPlayer(NetworkPlayerData player)
        {
            if (!IsServerInitialized || player == null) return;

            _players.Remove(player.OwnerId);
            ConnectedPlayers.Value = _players.Count;
            RefreshReadyState();

            if (Phase.Value == SessionPhase.Race && _players.Count <= 1)
                ServerShowResults();
        }

        public void RefreshReadyState()
        {
            if (!IsServerInitialized) return;

            ReadyPlayers.Value = _players.Values.Count(p => p.IsReady.Value);
            ConnectedPlayers.Value = _players.Count;

            if (Phase.Value == SessionPhase.Lobby &&
                ConnectedPlayers.Value >= _minimumPlayers &&
                ReadyPlayers.Value == ConnectedPlayers.Value)
            {
                ServerStartCountdown();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestRestartServerRpc()
        {
            ServerReturnToLobby();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestForceStartServerRpc()
        {
            if (Phase.Value == SessionPhase.Lobby && ConnectedPlayers.Value > 0)
                ServerStartCountdown();
        }

        public void ServerShowResults()
        {
            if (!IsServerInitialized) return;

            CountdownRemaining.Value = 0f;
            Phase.Value = SessionPhase.Results;
        }

        private void ServerStartCountdown()
        {
            if (!IsServerInitialized) return;

            Phase.Value = SessionPhase.Countdown;
            _countdownEndsAt = Time.time + _countdownSeconds;
            CountdownRemaining.Value = _countdownSeconds;
        }

        private void ServerStartRace()
        {
            if (!IsServerInitialized) return;

            foreach (NetworkPlayerData player in _players.Values)
                player.ServerResetForRace();

            CountdownRemaining.Value = 0f;
            Phase.Value = SessionPhase.Race;
            NetworkRaceManager.Instance?.ServerStartRace();
        }

        private void ServerReturnToLobby()
        {
            if (!IsServerInitialized) return;

            foreach (NetworkPlayerData player in _players.Values)
                player.ServerResetForLobby();

            CountdownRemaining.Value = 0f;
            Phase.Value = SessionPhase.Lobby;
            RefreshReadyState();
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            ConnectedPlayers.Value = NetworkManager.ServerManager.Clients.Count;

            if (args.ConnectionState == RemoteConnectionState.Stopped)
                RefreshReadyState();
        }
    }
}
