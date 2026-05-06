using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Features.Networking
{
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerData : NetworkBehaviour
    {
        private const int MaxNicknameLength = 18;

        public readonly SyncVar<string> Nickname = new("Pilot");
        public readonly SyncVar<int> HP = new(100);
        public readonly SyncVar<int> Score = new(0);
        public readonly SyncVar<int> CheckpointIndex = new(0);
        public readonly SyncVar<bool> IsReady = new(false);
        public readonly SyncVar<bool> IsFinished = new(false);
        public readonly SyncVar<float> FinishTime = new(0f);

        public int ClientId => OwnerId;
        public bool IsAlive => HP.Value > 0;

        public event Action<NetworkPlayerData> OnAnyValueChanged;

        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkSessionController.Instance?.RegisterPlayer(this);
        }

        public override void OnStopServer()
        {
            NetworkSessionController.Instance?.UnregisterPlayer(this);
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Subscribe();
            NotifyChanged();
        }

        public override void OnStopClient()
        {
            Unsubscribe();
            base.OnStopClient();
        }

        public void SetNickname(string nickname)
        {
            string sanitized = SanitizeNickname(nickname);

            if (IsServerInitialized)
                ServerSetNickname(sanitized);
            else if (IsOwner)
                SetNicknameServerRpc(sanitized);
        }

        public void SetReady(bool ready)
        {
            if (IsServerInitialized)
                ServerSetReady(ready);
            else if (IsOwner)
                SetReadyServerRpc(ready);
        }

        public void TryPassCheckpoint(int checkpointIndex)
        {
            if (IsServerInitialized)
                ServerPassCheckpoint(checkpointIndex);
            else if (IsOwner)
                PassCheckpointServerRpc(checkpointIndex);
        }

        public void ServerResetForLobby()
        {
            if (!IsServerInitialized) return;

            HP.Value = 100;
            Score.Value = 0;
            CheckpointIndex.Value = 0;
            IsReady.Value = false;
            IsFinished.Value = false;
            FinishTime.Value = 0f;
        }

        public void ServerResetForRace()
        {
            if (!IsServerInitialized) return;

            HP.Value = 100;
            Score.Value = 0;
            CheckpointIndex.Value = 0;
            IsFinished.Value = false;
            FinishTime.Value = 0f;
        }

        public void ServerSetCheckpoint(int checkpointIndex)
        {
            if (!IsServerInitialized) return;
            CheckpointIndex.Value = Mathf.Max(0, checkpointIndex);
        }

        public void ServerAddScore(int amount)
        {
            if (!IsServerInitialized) return;
            Score.Value = Mathf.Max(0, Score.Value + amount);
        }

        public void ServerApplyDamage(int amount)
        {
            if (!IsServerInitialized) return;
            HP.Value = Mathf.Clamp(HP.Value - Mathf.Abs(amount), 0, 100);
        }

        public void ServerMarkFinished(float finishTime)
        {
            if (!IsServerInitialized) return;

            FinishTime.Value = Mathf.Max(0f, finishTime);
            IsFinished.Value = true;
        }

        [ServerRpc]
        private void SetNicknameServerRpc(string nickname)
        {
            ServerSetNickname(nickname);
        }

        [ServerRpc]
        private void SetReadyServerRpc(bool ready)
        {
            ServerSetReady(ready);
        }

        [ServerRpc]
        private void PassCheckpointServerRpc(int checkpointIndex)
        {
            ServerPassCheckpoint(checkpointIndex);
        }

        private void ServerSetNickname(string nickname)
        {
            Nickname.Value = SanitizeNickname(nickname);
        }

        private void ServerSetReady(bool ready)
        {
            if (NetworkSessionController.Instance != null &&
                NetworkSessionController.Instance.Phase.Value != SessionPhase.Lobby)
                return;

            IsReady.Value = ready;
            NetworkSessionController.Instance?.RefreshReadyState();
        }

        private void ServerPassCheckpoint(int checkpointIndex)
        {
            NetworkRaceManager.Instance?.TryPassCheckpoint(this, checkpointIndex);
        }

        private void Subscribe()
        {
            Nickname.OnChange += OnStringChanged;
            HP.OnChange += OnIntChanged;
            Score.OnChange += OnIntChanged;
            CheckpointIndex.OnChange += OnIntChanged;
            IsReady.OnChange += OnBoolChanged;
            IsFinished.OnChange += OnBoolChanged;
            FinishTime.OnChange += OnFloatChanged;
        }

        private void Unsubscribe()
        {
            Nickname.OnChange -= OnStringChanged;
            HP.OnChange -= OnIntChanged;
            Score.OnChange -= OnIntChanged;
            CheckpointIndex.OnChange -= OnIntChanged;
            IsReady.OnChange -= OnBoolChanged;
            IsFinished.OnChange -= OnBoolChanged;
            FinishTime.OnChange -= OnFloatChanged;
        }

        private void OnStringChanged(string prev, string next, bool asServer) => NotifyChanged();
        private void OnIntChanged(int prev, int next, bool asServer) => NotifyChanged();
        private void OnBoolChanged(bool prev, bool next, bool asServer) => NotifyChanged();
        private void OnFloatChanged(float prev, float next, bool asServer) => NotifyChanged();

        private void NotifyChanged()
        {
            OnAnyValueChanged?.Invoke(this);
        }

        private static string SanitizeNickname(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return "Pilot";

            string trimmed = nickname.Trim();
            return trimmed.Length <= MaxNicknameLength
                ? trimmed
                : trimmed[..MaxNicknameLength];
        }
    }
}
