using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

namespace Features.Networking
{
    [System.Serializable]
    public struct NetworkRaceResult
    {
        public int ClientId;
        public string Nickname;
        public int HP;
        public int Score;
        public int CheckpointIndex;
        public bool IsReady;
        public bool IsFinished;
        public bool IsDisconnected;
        public float FinishTime;

        public NetworkRaceResult(NetworkPlayerData player, bool disconnected)
        {
            ClientId = player != null ? player.ClientId : -1;
            Nickname = player != null ? player.Nickname.Value : "Pilot";
            HP = player != null ? player.HP.Value : 0;
            Score = player != null ? player.Score.Value : 0;
            CheckpointIndex = player != null ? player.CheckpointIndex.Value : 0;
            IsReady = player != null && player.IsReady.Value;
            IsFinished = player != null && player.IsFinished.Value;
            IsDisconnected = disconnected;
            FinishTime = player != null ? player.FinishTime.Value : 0f;
        }
    }

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
        public readonly SyncList<NetworkRaceResult> Results = new();

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
            UpsertResult(player, false);
            ConnectedPlayers.Value = _players.Count;
            RefreshReadyState();
        }

        public void UnregisterPlayer(NetworkPlayerData player)
        {
            if (!IsServerInitialized || player == null) return;

            UpsertResult(player, true);
            _players.Remove(player.OwnerId);
            ConnectedPlayers.Value = _players.Count;
            HandlePlayerCountChangedAfterDisconnect();
        }

        public void RefreshReadyState()
        {
            if (!IsServerInitialized) return;

            ReadyPlayers.Value = _players.Values.Count(p => p.IsReady.Value);
            ConnectedPlayers.Value = _players.Count;

            if (Phase.Value == SessionPhase.Countdown && !CanStartCountdown())
                ServerCancelCountdown();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestRestartServerRpc(NetworkConnection sender = null)
        {
            if (!CanAcceptHostSessionAction(sender)) return;
            if (Phase.Value != SessionPhase.Results) return;

            ServerReturnToLobby();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestForceStartServerRpc(NetworkConnection sender = null)
        {
            if (!CanAcceptSessionAction(sender)) return;
            if (sender != null && sender.ClientId != 0) return;

            if (Phase.Value == SessionPhase.Lobby && ConnectedPlayers.Value > 0)
                ServerStartCountdown(true);
        }

        public void ServerShowResults()
        {
            if (!IsServerInitialized) return;

            RefreshResultSnapshots();
            CountdownRemaining.Value = 0f;
            Phase.Value = SessionPhase.Results;
        }

        private void ServerStartCountdown(bool forceStart)
        {
            if (!IsServerInitialized) return;
            if (forceStart)
            {
                if (_players.Count == 0) return;
            }
            else if (!CanStartCountdown())
            {
                return;
            }

            Phase.Value = SessionPhase.Countdown;
            _countdownEndsAt = Time.time + _countdownSeconds;
            CountdownRemaining.Value = _countdownSeconds;
        }

        private void ServerStartRace()
        {
            if (!IsServerInitialized) return;
            if (Phase.Value != SessionPhase.Countdown) return;
            if (_players.Count == 0) return;

            foreach (NetworkPlayerData player in _players.Values)
                player.ServerResetForRace();

            Results.Clear();
            RefreshResultSnapshots();

            CountdownRemaining.Value = 0f;
            Phase.Value = SessionPhase.Race;
            NetworkRaceManager.Instance?.ServerStartRace();
        }

        private void ServerReturnToLobby()
        {
            if (!IsServerInitialized) return;

            foreach (NetworkPlayerData player in _players.Values)
                player.ServerResetForLobby();

            Results.Clear();
            RefreshResultSnapshots();
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

        public void ServerRefreshPlayerSnapshot(NetworkPlayerData player)
        {
            if (!IsServerInitialized || player == null) return;

            UpsertResult(player, false);
        }

        private bool CanStartCountdown()
        {
            return ConnectedPlayers.Value >= _minimumPlayers &&
                   ReadyPlayers.Value == ConnectedPlayers.Value &&
                   ConnectedPlayers.Value > 0;
        }

        private void ServerCancelCountdown()
        {
            CountdownRemaining.Value = 0f;
            Phase.Value = SessionPhase.Lobby;
        }

        private void HandlePlayerCountChangedAfterDisconnect()
        {
            RefreshReadyState();

            if (Phase.Value == SessionPhase.Countdown && !CanStartCountdown())
                ServerCancelCountdown();
            else if (Phase.Value == SessionPhase.Race && _players.Count <= 1)
                ServerShowResults();
            else if (Phase.Value == SessionPhase.Results)
                RefreshResultSnapshots();
        }

        private bool CanAcceptSessionAction(NetworkConnection sender)
        {
            if (!IsServerInitialized) return false;
            return sender == null || _players.ContainsKey(sender.ClientId);
        }

        private bool CanAcceptHostSessionAction(NetworkConnection sender)
        {
            if (!CanAcceptSessionAction(sender)) return false;
            return sender == null || sender.ClientId == 0;
        }

        private void RefreshResultSnapshots()
        {
            foreach (NetworkPlayerData player in _players.Values)
                UpsertResult(player, false);
        }

        private void UpsertResult(NetworkPlayerData player, bool disconnected)
        {
            if (player == null) return;

            NetworkRaceResult snapshot = new(player, disconnected);
            int existingIndex = FindResultIndex(player.ClientId);
            if (existingIndex >= 0)
                Results[existingIndex] = snapshot;
            else
                Results.Add(snapshot);
        }

        private int FindResultIndex(int clientId)
        {
            for (int i = 0; i < Results.Count; i++)
            {
                if (Results[i].ClientId == clientId)
                    return i;
            }

            return -1;
        }
    }
}
