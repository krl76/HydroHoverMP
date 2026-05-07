using System.Collections.Generic;
using DynamicCameraFOV = Features.Camera.DynamicCameraFOV;
using Infrastructure.Services.Input;
using Infrastructure.Services.Player;
using Physics.Hover;
using UnityEngine;
using Zenject;

namespace Features.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerData))]
    public sealed class NetworkHoverOwnerBridge : FishNet.Object.NetworkBehaviour
    {
        [SerializeField] private HoverController _hoverController;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Behaviour[] _ownerOnlyBehaviours;
        [SerializeField] private bool _disableChildCamerasForRemote = true;

        private IPlayerService _playerService;
        private IInputService _inputService;
        private NetworkPlayerData _playerData;
        private Behaviour[] _childCameraBehaviours;
        private bool _registeredWithPlayerService;
        private bool _registeredAsOwner;
        private int _registeredOwnerId;

        private void Awake()
        {
            _hoverController ??= GetComponent<HoverController>();
            _rigidbody ??= GetComponent<Rigidbody>();
            _playerData = GetComponent<NetworkPlayerData>();
            CacheChildCameraBehaviours();
        }

        [Inject]
        public void Construct(IPlayerService playerService, IInputService inputService)
        {
            _playerService = playerService;
            _inputService = inputService;
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

            if (_hoverController != null)
            {
                _hoverController.SetInputService(owner ? _inputService : null);
                _hoverController.SetInputEnabled(owner);
            }

            if (_rigidbody != null && !IsServerInitialized)
                _rigidbody.isKinematic = !owner;

            if (_ownerOnlyBehaviours != null)
            {
                foreach (Behaviour behaviour in _ownerOnlyBehaviours)
                {
                    if (behaviour != null)
                        behaviour.enabled = owner;
                }
            }

            SetChildCameraBehavioursEnabled(owner);

            if (_playerService == null) return;

            RegisterPlayerServiceEntry(owner);

            if (owner && _playerData != null && string.IsNullOrWhiteSpace(_playerData.Nickname.Value))
                _playerData.SetNickname($"Pilot {OwnerId}");
        }

        private void ResolveServicesIfNeeded()
        {
            if (!ProjectContext.HasInstance) return;

            DiContainer container = ProjectContext.Instance.Container;
            _playerService ??= container.TryResolve<IPlayerService>();
            _inputService ??= container.TryResolve<IInputService>();
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
