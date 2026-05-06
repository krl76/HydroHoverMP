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

        private IPlayerService _playerService;
        private IInputService _inputService;
        private NetworkPlayerData _playerData;

        private void Awake()
        {
            _hoverController ??= GetComponent<HoverController>();
            _rigidbody ??= GetComponent<Rigidbody>();
            _playerData = GetComponent<NetworkPlayerData>();
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
            if (_playerService != null)
            {
                if (IsOwner)
                    _playerService.UnregisterLocalPlayer();
                else
                    _playerService.UnregisterRemotePlayer(OwnerId);
            }

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

            if (_rigidbody != null)
                _rigidbody.isKinematic = !owner;

            if (_ownerOnlyBehaviours != null)
            {
                foreach (Behaviour behaviour in _ownerOnlyBehaviours)
                {
                    if (behaviour != null)
                        behaviour.enabled = owner;
                }
            }

            if (_playerService == null) return;

            if (owner)
                _playerService.RegisterLocalPlayer(gameObject);
            else
                _playerService.RegisterRemotePlayer(OwnerId, gameObject);

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
    }
}
