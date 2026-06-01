using FishNet.Object;
using Infrastructure.Services.Input;
using Infrastructure.Services.RaceManager;
using Physics.Hover;
using UnityEngine;
using Zenject;

namespace Features.Networking
{
    /// <summary>
    /// Owner-only recovery: pressing R teleports the local hovercraft to the last
    /// passed checkpoint (or spawn point 0 if none passed yet), upright, with velocity
    /// cleared. Movement is client-authoritative (NetworkTransform syncs the result to
    /// everyone), so this runs entirely on the owner — no server RPC needed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerData))]
    public sealed class HoverRespawnController : NetworkBehaviour
    {
        [SerializeField] private float _respawnCooldown = 1f;
        [SerializeField] private float _uprightLift = 0.5f;

        private HoverController _hoverController;
        private NetworkPlayerData _playerData;
        private IInputService _inputService;
        private IRaceManagerService _raceService;
        private float _nextRespawnTime;

        private void Awake()
        {
            _hoverController = GetComponent<HoverController>();
            _playerData = GetComponent<NetworkPlayerData>();
        }

        private void Update()
        {
            if (!IsClientInitialized || !IsOwner) return;

            ResolveServicesIfNeeded();
            if (_inputService == null || !_inputService.RespawnPressed) return;
            if (Time.time < _nextRespawnTime) return;

            // Only meaningful during an active race; ignore in lobby/countdown/results.
            NetworkSessionController session = NetworkSessionController.Instance;
            if (session != null && session.Phase.Value != SessionPhase.Race) return;

            if (TryResolveRespawnPose(out Vector3 position, out Quaternion rotation))
            {
                Respawn(position, rotation);
                _nextRespawnTime = Time.time + Mathf.Max(0f, _respawnCooldown);
            }
        }

        private bool TryResolveRespawnPose(out Vector3 position, out Quaternion rotation)
        {
            int lastCheckpoint = (_playerData != null ? _playerData.CheckpointIndex.Value : 0) - 1;

            if (lastCheckpoint >= 0 && _raceService != null &&
                _raceService.TryGetCheckpointPose(lastCheckpoint, out position, out rotation))
                return true;

            NetworkSpawnPointRegistry registry = NetworkSpawnPointRegistry.Instance;
            if (registry != null && registry.TryGetSpawn(0, out position, out rotation))
                return true;

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        private void Respawn(Vector3 position, Quaternion rotation)
        {
            Rigidbody rb = _hoverController != null ? _hoverController.Rb : GetComponent<Rigidbody>();
            Vector3 target = position + Vector3.up * Mathf.Max(0f, _uprightLift);

            if (rb != null)
            {
                rb.position = target;
                rb.rotation = rotation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                transform.SetPositionAndRotation(target, rotation);
            }
        }

        private void ResolveServicesIfNeeded()
        {
            if (_inputService != null && _raceService != null) return;
            if (!ProjectContext.HasInstance) return;

            DiContainer container = ProjectContext.Instance.Container;
            _inputService ??= container.TryResolve<IInputService>();
            _raceService ??= container.TryResolve<IRaceManagerService>();
        }
    }
}
