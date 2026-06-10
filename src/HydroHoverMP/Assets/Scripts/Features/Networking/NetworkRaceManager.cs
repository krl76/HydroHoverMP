using System.Linq;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Features.Networking
{
    [DisallowMultipleComponent]
    public sealed class NetworkRaceManager : NetworkBehaviour
    {
        [SerializeField] private int _fallbackCheckpointCount = 1;
        [SerializeField] private int _checkpointScore = 100;
        [SerializeField] private int _finishScore = 500;
        [SerializeField] private int _wrongCheckpointDamage = 10;

        private int _totalCheckpoints;
        private float _serverRaceStartTime;

        public readonly SyncVar<uint> RaceStartTick = new(0);

        public static NetworkRaceManager Instance { get; private set; }
        public int TotalCheckpoints => Mathf.Max(_totalCheckpoints, _fallbackCheckpointCount);

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void RegisterTrack(int checkpointCount)
        {
            _totalCheckpoints = Mathf.Max(1, checkpointCount);
        }

        public void ServerStartRace()
        {
            if (!IsServerInitialized) return;

            _serverRaceStartTime = Time.time;
            RaceStartTick.Value = TimeManager != null ? TimeManager.Tick : 0u;
        }

        // Elapsed race time derived from the synchronized network tick, so every client
        // shows the same running clock during the race. Returns 0 outside the race phase.
        public float GetRaceElapsedSeconds()
        {
            if (TimeManager == null || RaceStartTick.Value == 0u) return 0f;
            if (NetworkSessionController.Instance == null ||
                NetworkSessionController.Instance.Phase.Value != SessionPhase.Race)
                return 0f;

            uint currentTick = TimeManager.Tick;
            if (currentTick <= RaceStartTick.Value) return 0f;

            return (float)((currentTick - RaceStartTick.Value) * TimeManager.TickDelta);
        }

        public void TryPassCheckpoint(NetworkPlayerData player, int checkpointIndex)
        {
            if (!IsServerInitialized || player == null) return;
            if (NetworkSessionController.Instance == null ||
                NetworkSessionController.Instance.Phase.Value != SessionPhase.Race)
                return;
            if (player.IsFinished.Value || !player.IsAlive)
                return;

            int expected = player.CheckpointIndex.Value;
            if (checkpointIndex != expected)
            {
                if (checkpointIndex > expected)
                    player.ServerApplyDamage(_wrongCheckpointDamage);
                return;
            }

            int nextCheckpoint = expected + 1;
            player.ServerSetCheckpoint(nextCheckpoint);
            player.ServerAddScore(_checkpointScore);

            if (nextCheckpoint >= TotalCheckpoints)
                ServerFinishPlayer(player);
        }

        private void ServerFinishPlayer(NetworkPlayerData player)
        {
            float finishTime = Mathf.Max(0f, Time.time - _serverRaceStartTime);
            player.ServerMarkFinished(finishTime);
            player.ServerAddScore(_finishScore);
            NetworkSessionController.Instance?.ServerAddDedicatedLeaderboardRecord(finishTime, player.Nickname.Value);

            if (NetworkSessionController.Instance != null &&
                NetworkSessionController.Instance.Players.Any() &&
                NetworkSessionController.Instance.Players.All(p => p.IsFinished.Value || !p.IsAlive))
            {
                NetworkSessionController.Instance.ServerShowResults();
            }
        }
    }
}

