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
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerData))]
    public sealed class NetworkHoverOwnerBridge : FishNet.Object.NetworkBehaviour
    {
        [SerializeField] private HoverController _hoverController;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Behaviour[] _ownerOnlyBehaviours;
        [SerializeField] private bool _disableChildCamerasForRemote = true;

        [Header("Remote Visual Float (non-owner smoothing)")]
        [Tooltip("How fast the remote boat's average height above the water tracks the networked value. " +
                 "Low = the boat bobs with the LOCAL waves (smoothest); high = it follows the networked vertical motion (more exact, more jitter).")]
        [SerializeField] private float _remoteOffsetSharpness = 2f;
        [Tooltip("Vertical glide. Lower = the boat hovers more gently over swells instead of rigidly tracking the surface.")]
        [SerializeField] private float _remoteHeightSharpness = 5f;
        [SerializeField] private float _remoteTiltSharpness = 5f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the wave-surface tilt applied. A hovercraft on its cushion barely follows wave faces, so keep this well below 1.")]
        [SerializeField] private float _remoteTiltStrength = 0.5f;
        [Tooltip("Hard cap on remote boat tilt (degrees) so it never snaps to a steep wave face and looks like it's flying.")]
        [SerializeField] private float _remoteMaxTiltDegrees = 10f;
        [SerializeField] private float _remoteSampleDistance = 2.5f;
        [Tooltip("Extra lift (metres) added to the remote boat's resting height so it doesn't appear to sink. Tune until the remote waterline matches the local boat.")]
        [SerializeField] private float _remoteHeightBias = 0.25f;

        private bool _remoteVisualFloatActive;
        private bool _remoteFloatInitialized;
        private float _smoothWaterOffset;
        private float _smoothDisplayY;
        private Vector3 _smoothFloatNormal = Vector3.up;
        private WaterPhysicsSystem _remoteWaterSystem;

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
            // Client-authoritative vehicles: the server (dedicated or host) must NOT
            // simulate physics for objects it does not own. Otherwise gravity / hover /
            // aerodynamics fight the networked transform and the position the server
            // relays to other clients arrives jittery.
            ApplyPhysicsAuthority(ServerSimulatesLocally());
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            ApplyPhysicsAuthority(ServerSimulatesLocally());
        }

        // IsOwner is a client-only concept and is disallowed inside server callbacks
        // (FishNet FN0007). On the server a vehicle is simulated locally only when this
        // process is a host whose own client owns it; a dedicated server and any
        // remotely-owned boat simulate nothing and just relay the networked transform.
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

            // The owning client auto-readies as soon as its player object is initialized
            // (= scene loaded + player spawned locally). The server only starts the
            // countdown once every connected pilot has reported ready, so the race never
            // begins before a joining client has finished loading.
            if (owner && _playerData != null)
                _playerData.SetReady(true);

            if (_playerService == null) return;

            RegisterPlayerServiceEntry(owner);

            if (owner && _playerData != null && string.IsNullOrWhiteSpace(_playerData.Nickname.Value))
                _playerData.SetNickname($"Pilot {OwnerId}");
        }

        private void ApplyPhysicsAuthority(bool owner)
        {
            // Only the owning client simulates the hovercraft. Everyone else — remote
            // clients AND the server — keeps it kinematic and disables the buoyancy /
            // aerodynamics behaviours, so local physics can never fight the networked
            // transform. Runs on the server too (previously skipped via !IsServerInitialized),
            // which is what left server-side bodies simulating and relaying jitter.
            if (_rigidbody != null)
                _rigidbody.isKinematic = !owner;

            // Remote vehicles are smoothed locally on rendering clients: NetworkTransform
            // drives horizontal position + heading, while the vertical bob and tilt are
            // recomputed from the local water surface (see LateUpdate). This removes the
            // wave-driven jitter caused by streaming a high-frequency bob over the wire.
            _remoteVisualFloatActive = !owner && IsClientInitialized;
            _remoteFloatInitialized = false;

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
            if (!_remoteVisualFloatActive) return;

            ResolveRemoteWaterSystem();
            if (_remoteWaterSystem == null) return;

            Vector3 position = transform.position;
            float dt = Time.deltaTime;
            float centerHeight = _remoteWaterSystem.GetWaterHeightAt(position);

            // Average height the owner floats above the water, taken from the networked Y but
            // slowly smoothed so the high-frequency wave bob (the jitter) is filtered out.
            // No magic offset constant — this auto-calibrates to wherever the owner sits.
            float networkedOffset = position.y - centerHeight;

            float forwardHeight = _remoteWaterSystem.GetWaterHeightAt(position + transform.forward * _remoteSampleDistance);
            float rightHeight = _remoteWaterSystem.GetWaterHeightAt(position + transform.right * _remoteSampleDistance);
            Vector3 vForward = new(0f, forwardHeight - centerHeight, _remoteSampleDistance);
            Vector3 vRight = new(_remoteSampleDistance, rightHeight - centerHeight, 0f);
            Vector3 surfaceNormal = Vector3.Cross(vForward, vRight).normalized;
            if (surfaceNormal.sqrMagnitude < 0.0001f)
                surfaceNormal = Vector3.up;

            // Apply only a fraction of the surface tilt: a hovercraft rides its air cushion and
            // barely follows steep wave faces, unlike a rigid raft. Keeps the boat from "flying".
            Vector3 targetNormal = Vector3.Slerp(Vector3.up, surfaceNormal, Mathf.Clamp01(_remoteTiltStrength));

            if (!_remoteFloatInitialized)
            {
                _smoothWaterOffset = networkedOffset;
                _smoothFloatNormal = targetNormal;
                _smoothDisplayY = centerHeight + networkedOffset + _remoteHeightBias;
                _remoteFloatInitialized = true;
            }
            else
            {
                _smoothWaterOffset = Mathf.Lerp(_smoothWaterOffset, networkedOffset, 1f - Mathf.Exp(-_remoteOffsetSharpness * dt));
                _smoothFloatNormal = Vector3.Slerp(_smoothFloatNormal, targetNormal, 1f - Mathf.Exp(-_remoteTiltSharpness * dt));
            }

            // Vertical bob rides the local wave surface at the owner's average height, but glides
            // (low-passed) so the boat hovers over swells instead of rigidly tracking every wave.
            float targetY = centerHeight + _smoothWaterOffset + _remoteHeightBias;
            _smoothDisplayY = Mathf.Lerp(_smoothDisplayY, targetY, 1f - Mathf.Exp(-_remoteHeightSharpness * dt));

            // Hard cap the tilt so a steep wave can never throw the boat into a dramatic pose.
            Vector3 cappedNormal = _smoothFloatNormal;
            float tiltAngle = Vector3.Angle(Vector3.up, cappedNormal);
            if (tiltAngle > _remoteMaxTiltDegrees && tiltAngle > 0.0001f)
                cappedNormal = Vector3.Slerp(Vector3.up, cappedNormal, _remoteMaxTiltDegrees / tiltAngle);

            // Horizontal position + heading stay as NetworkTransform interpolated them.
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, cappedNormal);
            Quaternion finalRotation = tilt * Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(new Vector3(position.x, _smoothDisplayY, position.z), finalRotation);
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
