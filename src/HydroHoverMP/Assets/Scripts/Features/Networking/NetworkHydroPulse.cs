using FishNet.Object;
using Infrastructure.Services.Input;
using Physics.Hover;
using UnityEngine;
using Zenject;

namespace Features.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerData))]
    public sealed class NetworkHydroPulse : NetworkBehaviour
    {
        [SerializeField] private float _cooldown = 3f;
        [SerializeField] private float _impulseForce = 35f;
        [SerializeField] private int _scoreReward = 25;
        [SerializeField] private ParticleSystem _pulseVfx;
        [SerializeField] private AudioSource _pulseAudio;
        [SerializeField] private HoverController _hoverController;

        private IInputService _inputService;
        private NetworkPlayerData _playerData;
        private float _nextLocalRequestTime;
        private float _nextServerAllowedTime;

        private void Awake()
        {
            _hoverController ??= GetComponent<HoverController>();
            _playerData = GetComponent<NetworkPlayerData>();
        }

        [Inject]
        public void Construct(IInputService inputService)
        {
            _inputService = inputService;
        }

        private void Update()
        {
            if (!IsOwner) return;
            ResolveInputIfNeeded();
            if (_inputService == null || !_inputService.HydroPulsePressed) return;
            if (Time.time < _nextLocalRequestTime) return;

            _nextLocalRequestTime = Time.time + _cooldown;
            _hoverController?.ApplyHydroPulse(_impulseForce);
            RequestHydroPulseServerRpc(transform.position, transform.forward);
        }

        [ServerRpc]
        private void RequestHydroPulseServerRpc(Vector3 position, Vector3 forward)
        {
            if (_playerData == null || !_playerData.IsAlive) return;
            if (NetworkSessionController.Instance == null ||
                NetworkSessionController.Instance.Phase.Value != SessionPhase.Race)
                return;
            if (Time.time < _nextServerAllowedTime) return;

            _nextServerAllowedTime = Time.time + _cooldown;
            _hoverController?.ApplyHydroPulse(_impulseForce);
            _playerData.ServerAddScore(_scoreReward);

            PlayHydroPulseObserversRpc(position, forward);
        }

        [ObserversRpc]
        private void PlayHydroPulseObserversRpc(Vector3 position, Vector3 forward)
        {
            if (_pulseVfx != null)
            {
                _pulseVfx.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward == Vector3.zero ? transform.forward : forward));
                _pulseVfx.Play(true);
            }

            if (_pulseAudio != null && !_pulseAudio.isPlaying)
                _pulseAudio.Play();
        }

        private void ResolveInputIfNeeded()
        {
            if (_inputService != null || !ProjectContext.HasInstance) return;

            _inputService = ProjectContext.Instance.Container.TryResolve<IInputService>();
        }
    }
}
