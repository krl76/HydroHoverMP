using FishNet.Connection;
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
        private ushort _nextPulseSequence;
        private ushort _lastPlayedPulseSequence;

        private void Awake()
        {
            _hoverController ??= GetComponent<HoverController>();
            _playerData = GetComponent<NetworkPlayerData>();
            _pulseVfx ??= GetComponentInChildren<ParticleSystem>(true);
            _pulseAudio ??= FindPulseAudioSource();
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
            if (!CanLocallyRequestHydroPulse()) return;
            if (Time.time < _nextLocalRequestTime) return;

            _nextLocalRequestTime = Time.time + _cooldown;
            if (!IsServerInitialized)
                _hoverController?.ApplyHydroPulse(_impulseForce);

            RequestHydroPulseServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestHydroPulseServerRpc(NetworkConnection sender = null)
        {
            if (!CanAcceptHydroPulse(sender, out string rejectionReason))
            {
                Debug.LogWarning($"[NetworkHydroPulse] Rejected pulse for owner {OwnerId}: {rejectionReason}");
                return;
            }

            _nextServerAllowedTime = Time.time + _cooldown;
            _hoverController?.ApplyHydroPulse(_impulseForce);
            _playerData.ServerAddScore(_scoreReward);

            ushort sequence = ++_nextPulseSequence;
            Vector3 position = transform.position;
            Vector3 forward = transform.forward;
            PlayHydroPulseObserversRpc(sequence, position, forward);
        }

        [ObserversRpc]
        private void PlayHydroPulseObserversRpc(ushort sequence, Vector3 position, Vector3 forward)
        {
            if (sequence == _lastPlayedPulseSequence)
                return;

            _lastPlayedPulseSequence = sequence;

            if (_pulseVfx != null)
            {
                Vector3 safeForward = forward == Vector3.zero ? transform.forward : forward;
                _pulseVfx.transform.SetPositionAndRotation(position, Quaternion.LookRotation(safeForward));
                _pulseVfx.Play(true);
            }

            if (_pulseAudio != null)
            {
                if (_pulseAudio.isPlaying)
                    _pulseAudio.Stop();

                _pulseAudio.Play();
            }
        }

        private AudioSource FindPulseAudioSource()
        {
            AudioSource[] sources = GetComponentsInChildren<AudioSource>(true);
            foreach (AudioSource source in sources)
            {
                if (source != null && !source.loop)
                    return source;
            }

            return null;
        }

        private bool CanLocallyRequestHydroPulse()
        {
            if (_playerData == null || !_playerData.IsAlive || _playerData.IsFinished.Value)
                return false;

            NetworkSessionController session = NetworkSessionController.Instance;
            return session != null && session.Phase.Value == SessionPhase.Race;
        }

        private bool CanAcceptHydroPulse(NetworkConnection sender, out string rejectionReason)
        {
            if (!IsServerInitialized)
            {
                rejectionReason = "server is not initialized";
                return false;
            }

            if (sender == null)
            {
                rejectionReason = "sender connection is unavailable";
                return false;
            }

            if (sender.ClientId != OwnerId)
            {
                rejectionReason = $"sender {sender.ClientId} is not owner {OwnerId}";
                return false;
            }

            if (_playerData == null)
            {
                rejectionReason = "player data is missing";
                return false;
            }

            if (!_playerData.IsAlive)
            {
                rejectionReason = "player is dead";
                return false;
            }

            if (_playerData.IsFinished.Value)
            {
                rejectionReason = "player already finished";
                return false;
            }

            NetworkSessionController session = NetworkSessionController.Instance;
            if (session == null)
            {
                rejectionReason = "session controller is missing";
                return false;
            }

            if (session.Phase.Value != SessionPhase.Race)
            {
                rejectionReason = $"session phase is {session.Phase.Value}";
                return false;
            }

            if (Time.time < _nextServerAllowedTime)
            {
                rejectionReason = "cooldown is active";
                return false;
            }

            rejectionReason = string.Empty;
            return true;
        }

        private void ResolveInputIfNeeded()
        {
            if (_inputService != null || !ProjectContext.HasInstance) return;

            _inputService = ProjectContext.Instance.Container.TryResolve<IInputService>();
        }
    }
}
