using System.Collections.Generic;
using DynamicCameraFOV = Features.Camera.DynamicCameraFOV;
using Infrastructure.Services.Player;
using UnityEngine;
using Zenject;

namespace Features.Networking
{
    /// <summary>
    /// Ownership presentation for a networked hovercraft: disables remote players' cameras/audio,
    /// registers the boat with the player service, auto-readies the owner, and assigns a default
    /// nickname.
    ///
    /// Physics is owned by <see cref="PredictedHoverMotor"/> (FishNet client-side prediction), so
    /// this component no longer touches the Rigidbody, the hover behaviours, or the transform — the
    /// old kinematic toggling and the "remote visual float" LateUpdate (which fought the predicted
    /// pose and caused remote boats to fly) have been removed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerData))]
    public sealed class NetworkHoverOwnerBridge : FishNet.Object.NetworkBehaviour
    {
        [SerializeField] private bool _disableChildCamerasForRemote = true;

        private IPlayerService _playerService;
        private NetworkPlayerData _playerData;
        private Behaviour[] _childCameraBehaviours;
        private bool _registeredWithPlayerService;
        private bool _registeredAsOwner;
        private int _registeredOwnerId;

        private void Awake()
        {
            _playerData = GetComponent<NetworkPlayerData>();
            CacheChildCameraBehaviours();
        }

        [Inject]
        public void Construct(IPlayerService playerService)
        {
            _playerService = playerService;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResolveServicesIfNeeded();
            ApplyOwnershipState();
        }

        public override void OnOwnershipClient(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            ApplyOwnershipState();
        }

        public override void OnStopClient()
        {
            UnregisterCurrentPlayerServiceEntry();

            base.OnStopClient();
        }

        private void ApplyOwnershipState()
        {
            bool owner = IsOwner;

            SetChildCameraBehavioursEnabled(owner);

            // The owning client auto-readies as soon as its player object is initialized
            // (= scene loaded + player spawned locally). The server only starts the countdown
            // once every connected pilot has reported ready, so the race never begins before a
            // joining client has finished loading.
            if (owner && _playerData != null)
                _playerData.SetReady(true);

            if (_playerService == null) return;

            RegisterPlayerServiceEntry(owner);

            if (owner && _playerData != null && string.IsNullOrWhiteSpace(_playerData.Nickname.Value))
                _playerData.SetNickname($"Pilot {OwnerId}");
        }

        private void CacheChildCameraBehaviours()
        {
            if (!_disableChildCamerasForRemote) return;

            List<Behaviour> behaviours = new();
            behaviours.AddRange(GetComponentsInChildren<UnityEngine.Camera>(true));
            behaviours.AddRange(GetComponentsInChildren<AudioListener>(true));
            behaviours.AddRange(GetComponentsInChildren<Cinemachine.CinemachineVirtualCamera>(true));
            behaviours.AddRange(GetComponentsInChildren<DynamicCameraFOV>(true));
            _childCameraBehaviours = behaviours.ToArray();
        }

        private void SetChildCameraBehavioursEnabled(bool enabled)
        {
            if (!_disableChildCamerasForRemote || _childCameraBehaviours == null) return;

            foreach (Behaviour behaviour in _childCameraBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = enabled;
            }
        }

        private void ResolveServicesIfNeeded()
        {
            if (_playerService != null) return;
            if (!ProjectContext.HasInstance) return;

            _playerService = ProjectContext.Instance.Container.TryResolve<IPlayerService>();
        }

        private void RegisterPlayerServiceEntry(bool owner)
        {
            int ownerId = OwnerId;
            if (_registeredWithPlayerService && _registeredAsOwner == owner && _registeredOwnerId == ownerId)
                return;

            UnregisterCurrentPlayerServiceEntry();

            if (owner)
                _playerService.RegisterLocalPlayer(gameObject);
            else
                _playerService.RegisterRemotePlayer(ownerId, gameObject);

            _registeredWithPlayerService = true;
            _registeredAsOwner = owner;
            _registeredOwnerId = ownerId;
        }

        private void UnregisterCurrentPlayerServiceEntry()
        {
            if (_playerService == null || !_registeredWithPlayerService) return;

            if (_registeredAsOwner)
                _playerService.UnregisterLocalPlayer();
            else
                _playerService.UnregisterRemotePlayer(_registeredOwnerId);

            _registeredWithPlayerService = false;
        }
    }
}
