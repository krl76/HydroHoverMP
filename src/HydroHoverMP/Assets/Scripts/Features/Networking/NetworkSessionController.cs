using System.Collections.Generic;
using System.IO;
using System.Linq;
using Data.Leaderbords;
using Infrastructure.Services.Network;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Newtonsoft.Json;
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


    [System.Serializable]
    public struct NetworkLeaderboardRecord
    {
        public float Time;
        public long Date;
        public string Nickname;

        public NetworkLeaderboardRecord(float time, long date, string nickname)
        {
            Time = time;
            Date = date;
            Nickname = nickname;
        }

        public Record ToRecord()
        {
            return new Record
            {
                Time = Time,
                Date = Date,
                PlayerName = Nickname
            };
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

    public enum PostRaceVote : byte
    {
        None = 0,
        Restart = 1,
        Menu = 2
    }

    [DisallowMultipleComponent]
    public sealed class NetworkSessionController : NetworkBehaviour
    {
        [SerializeField] private int _minimumPlayers = 2;
        [SerializeField] private float _countdownSeconds = 3f;
        [SerializeField] private string _dedicatedLeaderboardFileName = "dedicated_leaderboard.json";

        private readonly Dictionary<int, NetworkPlayerData> _players = new();
        private float _countdownEndsAt;

        public static NetworkSessionController Instance { get; private set; }

        public readonly SyncVar<SessionPhase> Phase = new(SessionPhase.Lobby);
        public readonly SyncVar<int> ConnectedPlayers = new(0);
        public readonly SyncVar<int> ReadyPlayers = new(0);
        public readonly SyncVar<float> CountdownRemaining = new(0f);
        public readonly SyncList<NetworkRaceResult> Results = new();
        public readonly SyncList<NetworkLeaderboardRecord> DedicatedLeaderboardRecords = new();

        // Post-race "Race again" / "Main menu" choice, keyed by owner ClientId. A strict
        // majority of connected racers voting Restart returns everyone to the lobby.
        public readonly SyncDictionary<int, PostRaceVote> PostRaceVotes = new();

        public IReadOnlyCollection<NetworkPlayerData> Players => _players.Values;
        public int MinimumPlayers => _minimumPlayers;

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
            Debug.Log($"[NetworkSessionController] Dedicated leaderboard file: {GetDedicatedLeaderboardPath()}");
            LoadDedicatedLeaderboardRecords();
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
            if (Phase.Value == SessionPhase.Lobby)
                ServerPlacePlayersAtSpawnPoints();
            else
                ServerPlacePlayerAtSpawnPoint(player);
            RefreshReadyState();
        }

        public void UnregisterPlayer(NetworkPlayerData player)
        {
            if (!IsServerInitialized || player == null) return;

            // Drop the player's leaderboard row entirely on disconnect. Reconnecting clients
            // get a new ClientId, so keeping a "DC" row just accumulates stale duplicates.
            RemoveResult(player);
            PostRaceVotes.Remove(player.OwnerId);
            _players.Remove(player.OwnerId);
            ConnectedPlayers.Value = _players.Count;
            HandlePlayerCountChangedAfterDisconnect();
        }

        public void RefreshReadyState()
        {
            if (!IsServerInitialized) return;

            ReadyPlayers.Value = _players.Values.Count(p => p.IsReady.Value);
            ConnectedPlayers.Value = _players.Count;

            if (Phase.Value == SessionPhase.Lobby)
                ServerTryAutoStartCountdown();
            else if (Phase.Value == SessionPhase.Countdown && !CanStartCountdown())
                ServerCancelCountdown();
        }

        private void ServerTryAutoStartCountdown()
        {
            if (!IsServerInitialized) return;
            if (Phase.Value != SessionPhase.Lobby) return;
            if (!CanStartCountdown()) return;

            ServerStartCountdown(false);
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestRestartServerRpc(NetworkConnection sender = null)
        {
            if (!CanAcceptHostSessionAction(sender)) return;
            if (Phase.Value != SessionPhase.Results) return;

            ServerReturnToLobby();
        }

        [ServerRpc(RequireOwnership = false)]
        public void SubmitPostRaceVoteServerRpc(PostRaceVote vote, NetworkConnection sender = null)
        {
            if (!IsServerInitialized) return;
            if (Phase.Value != SessionPhase.Results) return;
            if (sender == null || !_players.ContainsKey(sender.ClientId)) return;

            PostRaceVotes[sender.ClientId] = vote;
            EvaluatePostRaceVotes();
        }

        // A strict majority of still-connected racers choosing "Race again" returns the whole
        // session to the lobby (which auto-starts a fresh countdown). Players who pick "Main
        // menu" just disconnect; their vote is dropped and the threshold recomputed.
        private void EvaluatePostRaceVotes()
        {
            if (!IsServerInitialized) return;
            if (Phase.Value != SessionPhase.Results) return;

            int connected = _players.Count;
            if (connected <= 0) return;

            int restartVotes = 0;
            foreach (KeyValuePair<int, PostRaceVote> vote in PostRaceVotes)
            {
                if (vote.Value == PostRaceVote.Restart)
                    restartVotes++;
            }

            if (restartVotes * 2 > connected)
                ServerReturnToLobby();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestForceStartServerRpc(NetworkConnection sender = null)
        {
            if (!CanAcceptSessionAction(sender)) return;
            if (sender != null && sender.ClientId != 0) return;

            if (Phase.Value == SessionPhase.Lobby && ConnectedPlayers.Value >= _minimumPlayers)
                ServerStartCountdown(true);
        }

        public void ServerShowResults()
        {
            if (!IsServerInitialized) return;

            RefreshResultSnapshots();
            PostRaceVotes.Clear();
            CountdownRemaining.Value = 0f;
            Phase.Value = SessionPhase.Results;
        }

        public void ServerAddDedicatedLeaderboardRecord(float time, string nickname)
        {
            if (!IsServerInitialized)
            {
                Debug.LogWarning($"[Leaderboard] Ignored record add (server not initialized): time={time:F3}, nickname='{nickname}'.");
                return;
            }
            if (time <= 0f)
            {
                Debug.LogWarning($"[Leaderboard] Ignored record add (non-positive time {time:F3}) for nickname='{nickname}'.");
                return;
            }

            string safeNickname = string.IsNullOrWhiteSpace(nickname) ? "Pilot" : nickname.Trim();
            DedicatedLeaderboardRecords.Add(new NetworkLeaderboardRecord(time, System.DateTime.Now.Ticks, safeNickname));
            SortDedicatedLeaderboardRecords();
            Debug.Log($"[Leaderboard] Added record: nickname='{safeNickname}', time={time:F3}s. Total records: {DedicatedLeaderboardRecords.Count}.");
            SaveDedicatedLeaderboardRecords();
        }

        public List<Record> GetDedicatedLeaderboardRecords(int count)
        {
            return DedicatedLeaderboardRecords
                .Take(Mathf.Max(0, count))
                .Select(record => new Record
                {
                    Time = record.Time,
                    Date = record.Date,
                    PlayerName = record.Nickname
                })
                .ToList();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestDedicatedLeaderboardServerRpc(NetworkConnection sender = null)
        {
            if (!IsServerInitialized) return;
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

            ServerPlacePlayersAtSpawnPoints();
            Phase.Value = SessionPhase.Countdown;
            _countdownEndsAt = Time.time + _countdownSeconds;
            CountdownRemaining.Value = _countdownSeconds;
        }

        private void ServerPlacePlayersAtSpawnPoints()
        {
            if (!IsServerInitialized) return;

            NetworkSpawnPointRegistry registry = NetworkSpawnPointRegistry.Instance;
            if (registry == null || registry.Count == 0) return;

            int spawnIndex = 0;
            foreach (NetworkPlayerData player in _players.Values.OrderBy(p => p.OwnerId))
            {
                if (player == null) continue;
                if (registry.TryGetSpawn(spawnIndex, out Vector3 position, out Quaternion rotation))
                    player.ServerTeleportToSpawn(position, rotation);

                spawnIndex++;
            }
        }

        private void ServerPlacePlayerAtSpawnPoint(NetworkPlayerData player)
        {
            if (!IsServerInitialized || player == null) return;

            NetworkSpawnPointRegistry registry = NetworkSpawnPointRegistry.Instance;
            if (registry == null || registry.Count == 0) return;

            int spawnIndex = _players.Values
                .Where(registeredPlayer => registeredPlayer != null)
                .OrderBy(registeredPlayer => registeredPlayer.OwnerId)
                .TakeWhile(registeredPlayer => registeredPlayer != player)
                .Count();

            if (registry.TryGetSpawn(spawnIndex, out Vector3 position, out Quaternion rotation))
                player.ServerTeleportToSpawn(position, rotation);
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
            {
                player.ServerResetForLobby();
                // Still-connected pilots are already loaded, so keep them ready: the next
                // race auto-starts without forcing everyone to reconnect/confirm again.
                player.IsReady.Value = true;
            }

            Results.Clear();
            PostRaceVotes.Clear();
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
            // Every connected pilot must be "ready". Ready is set automatically by each
            // client once it has finished loading (see NetworkHoverOwnerBridge), so there
            // is no manual confirmation — but the countdown still waits until everyone has
            // actually loaded in, instead of starting the instant a player slot connects.
            return ConnectedPlayers.Value >= _minimumPlayers &&
                   ReadyPlayers.Value == ConnectedPlayers.Value;
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
            {
                RefreshResultSnapshots();
                EvaluatePostRaceVotes();
            }
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

        private void RemoveResult(NetworkPlayerData player)
        {
            if (player == null) return;

            int existingIndex = FindResultIndex(player.ClientId);
            if (existingIndex >= 0)
                Results.RemoveAt(existingIndex);
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

        private void SortDedicatedLeaderboardRecords()
        {
            List<NetworkLeaderboardRecord> orderedRecords = DedicatedLeaderboardRecords
                .OrderBy(record => record.Time)
                .ThenBy(record => record.Date)
                .ToList();

            DedicatedLeaderboardRecords.Clear();
            foreach (NetworkLeaderboardRecord record in orderedRecords)
                DedicatedLeaderboardRecords.Add(record);
        }

        private void LoadDedicatedLeaderboardRecords()
        {
            DedicatedLeaderboardRecords.Clear();
            string path = GetDedicatedLeaderboardPath();
            try
            {
                if (!File.Exists(path))
                {
                    Debug.Log($"[NetworkSessionController] No dedicated leaderboard file yet at '{path}'. Starting empty.");
                    return;
                }

                string json = File.ReadAllText(path);
                LeaderboardData data = JsonConvert.DeserializeObject<LeaderboardData>(json) ?? new LeaderboardData();
                IEnumerable<Record> loadedRecords = data.Records ?? new List<Record>();
                foreach (Record record in loadedRecords.OrderBy(record => record.Time).ThenBy(record => record.Date))
                    DedicatedLeaderboardRecords.Add(new NetworkLeaderboardRecord(record.Time, record.Date, record.PlayerName));

                Debug.Log($"[NetworkSessionController] Loaded {DedicatedLeaderboardRecords.Count} leaderboard record(s) from '{path}'.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[NetworkSessionController] Failed to load dedicated leaderboard from '{path}': {e}");
            }
        }

        private void SaveDedicatedLeaderboardRecords()
        {
            string path = GetDedicatedLeaderboardPath();
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                LeaderboardData data = new()
                {
                    Records = DedicatedLeaderboardRecords
                        .Select(record => record.ToRecord())
                        .OrderBy(record => record.Time)
                        .ThenBy(record => record.Date)
                        .ToList()
                };

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(path, json);
                Debug.Log($"[Leaderboard] Saved {data.Records.Count} record(s) to '{path}'.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Leaderboard] Failed to save dedicated leaderboard to '{path}': {e}");
            }
        }

        private string GetDedicatedLeaderboardPath()
        {
            string fileName = string.IsNullOrWhiteSpace(_dedicatedLeaderboardFileName)
                ? "dedicated_leaderboard.json"
                : _dedicatedLeaderboardFileName.Trim();

            return Path.Combine(ResolveDataDirectory(), fileName);
        }

        // Stable, operator-controlled location for server-persisted data. Resolution order:
        //   1) "-dataDir <path>" / "-leaderboardDir <path>" launch argument (highest priority),
        //   2) a "ServerData" folder beside the built executable (survives restarts of the same install),
        //   3) Application.persistentDataPath (Editor / fallback).
        private string ResolveDataDirectory()
        {
            string argDir = ServerEnvironment.GetCommandLineValue("-dataDir")
                            ?? ServerEnvironment.GetCommandLineValue("-leaderboardDir");
            if (!string.IsNullOrWhiteSpace(argDir))
                return argDir.Trim();

            if (!Application.isEditor)
            {
                // In a built player Application.dataPath points at "<Game>_Data"; its parent is the
                // folder holding the executable, so "<exe folder>/ServerData" sits next to the build.
                string exeDir = Directory.GetParent(Application.dataPath)?.FullName;
                if (!string.IsNullOrWhiteSpace(exeDir))
                    return Path.Combine(exeDir, "ServerData");
            }

            return Application.persistentDataPath;
        }
    }
}


