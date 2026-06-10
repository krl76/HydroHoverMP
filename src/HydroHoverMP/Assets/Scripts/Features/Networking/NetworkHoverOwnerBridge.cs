using System.Collections.Generic;
using System.Linq;
using DynamicCameraFOV = Features.Camera.DynamicCameraFOV;
using Infrastructure.Services.Input;
using Infrastructure.Services.Player;
using Physics.Hover;
using Physics.Water;
using UnityEngine;
using Zenject;

namespace Features.Networking
{
    /// <summary>
    /// Client-authoritative hovercraft ownership bridge. The OWNER simulates the hovercraft locally
    /// (full hover physics) and a FishNet NetworkTransform streams that pose to everyone else, so
    /// remote clients always see the owner's real position/heading. Non-owners keep the body
    /// kinematic and disable the local hover behaviours so local physics can never fight the
    /// networked transform.
    ///
    /// The owner's buoyancy spring is clamped (see HoverCushion), so the streamed pose is stable and
    /// remote boats no longer fly. A light vertical clamp (LateUpdate) is kept purely as a safety
    /// net: it only catches a boat shown absurdly far above/below the local water surface — it does
    /// NOT reconstruct the bob from local waves (that was the per-client divergence that made boats
    /// fly), it just bounds the networked Y.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerData))]
    public sealed class NetworkHoverOwnerBridge : FishNet.Object.NetworkBehaviour
    {
        [SerializeField] private HoverController _hoverController;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Behaviour[] _ownerOnlyBehaviours;
        [SerializeField] private bool _disableChildCamerasForRemote = true;

        [Header("Remote safety clamp (non-owner)")]
        [Tooltip("Max metres a remote boat may be shown ABOVE the local water surface before it is " +
                 "clamped. Generous so normal hover/jumps are untouched; only catches genuine flying.")]
        [SerializeField] private float _remoteMaxAir = 2.5f;
        [Tooltip("Max metres a remote boat may be shown BELOW the local water surface before clamped.")]
        [SerializeField] private float _remoteMaxSink = 0.6f;

        private IPlayerService _playerService;
        private IInputService _inputService;
        private NetworkPlayerData _playerData;
        private Behaviour[] _childCameraBehaviours;
        private bool _remoteClampActive;
        private WaterPhysicsSystem _remoteWaterSystem;
        private bool _registeredWithPlayerService;
        private bool _registeredAsOwner;
        private int _registeredOwnerId;

        private void Awake()
        {
            _hoverController ??= GetComponent<HoverController>();
            _rigidbody ??= GetComponent<Rigidbody>();
            _playerData = GetComponent<NetworkPlayerData>();
            CollectOwnerOnlyBehaviours();
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

        public override void OnStartServer()
        {
            base.OnStartServer();
            // The server simulates a vehicle locally only when it is the host and its own client
            // owns it; a dedicated server and any remotely-owned boat stay kinematic and just relay
            // the networked transform.
            ApplyPhysicsAuthority(ServerSimulatesLocally());
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            ApplyPhysicsAuthority(ServerSimulatesLocally());
        }

        // IsOwner is disallowed inside server callbacks (FishNet FN0007); on the server a vehicle is
        // simulated locally only when this process is a host whose own client owns it.
        private bool ServerSimulatesLocally()
        {
            return Owner != null && Owner.IsLocalClient;
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

            ApplyPhysicsAuthority(owner);

            SetChildCameraBehavioursEnabled(owner);

            // The owning client auto-readies once its player object is initialized; the server only
            // starts the countdown when every connected pilot has reported ready.
            if (owner && _playerData != null)
                _playerData.SetReady(true);

            if (_playerService == null) return;

            RegisterPlayerServiceEntry(owner);

            if (owner && _playerData != null && string.IsNullOrWhiteSpace(_playerData.Nickname.Value))
                _playerData.SetNickname($"Pilot {OwnerId}");
        }

        private void ApplyPhysicsAuthority(bool owner)
        {
            // Only the owning client (or a host whose own client owns it) simulates the hovercraft.
            // Everyone else keeps it kinematic so local physics can never fight the networked transform.
            if (_rigidbody != null)
                _rigidbody.isKinematic = !owner;

            _remoteClampActive = !owner && IsClientInitialized;

            if (_ownerOnlyBehaviours == null) return;

            foreach (Behaviour behaviour in _ownerOnlyBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = owner;
            }
        }

        private void CollectOwnerOnlyBehaviours()
        {
            List<Behaviour> behaviours = new();
            if (_ownerOnlyBehaviours != null)
                behaviours.AddRange(_ownerOnlyBehaviours.Where(behaviour => behaviour != null));

            AddOwnerOnlyBehaviour<HoverCushion>(behaviours);
            AddOwnerOnlyBehaviour<HoverAerodynamics>(behaviours);

            _ownerOnlyBehaviours = behaviours.Distinct().ToArray();
        }

        private void AddOwnerOnlyBehaviour<T>(List<Behaviour> behaviours) where T : Behaviour
        {
            foreach (T behaviour in GetComponentsInChildren<T>(true))
            {
                if (behaviour != null && !behaviours.Contains(behaviour))
                    behaviours.Add(behaviour);
            }
        }

        private void LateUpdate()
        {
            if (!_remoteClampActive) return;

            ResolveRemoteWaterSystem();
            if (_remoteWaterSystem == null) return;

            // Trust the networked pose; only bound the vertical so a remote boat can never be shown
            // flying high above or sunk far below the water. Normal hover stays inside the band and
            // is left exactly as NetworkTransform interpolated it.
            Vector3 position = transform.position;
            float waterHeight = _remoteWaterSystem.GetWaterHeightAt(position);
            float minY = waterHeight - _remoteMaxSink;
            float maxY = waterHeight + _remoteMaxAir;

            if (position.y < minY || position.y > maxY)
            {
                position.y = Mathf.Clamp(position.y, minY, maxY);
                transform.position = position;
            }
        }

        private void ResolveRemoteWaterSystem()
        {
            if (_remoteWaterSystem != null) return;

            _remoteWaterSystem = FindFirstObjectByType<WaterPhysicsSystem>();
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
