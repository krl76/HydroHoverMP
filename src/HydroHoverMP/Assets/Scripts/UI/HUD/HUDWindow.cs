using System.Collections;
using Features.Networking;
using Infrastructure.Services.Network;
using Infrastructure.Services.Player;
using Infrastructure.Services.RaceManager;
using Physics.Hover;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.HUD
{
    public class HUDWindow : MonoBehaviour
    {
        [Header("Texts")]
        [SerializeField] private TextMeshProUGUI _speedText;
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private TextMeshProUGUI _checkpointText;
        [SerializeField] private TextMeshProUGUI _fpsText;
        
        [Header("Speedometer")]
        [SerializeField] private RectTransform _speedNeedle;
        [SerializeField] private float _minSpeed = 0f;
        [SerializeField] private float _maxSpeed = 200f;
        [SerializeField] private float _minAngle = 135f;
        [SerializeField] private float _maxAngle = -135f;
        
        [Header("Bars")]
        [SerializeField] private Image _liftBar;
        [SerializeField] private Image _thrustBar;

        private IPlayerService _playerService;
        private IRaceManagerService _raceManagerService;
        private INetworkConnectionService _connectionService;
        private HoverController _hoverController;
        private NetworkPlayerData _networkPlayerData;
        
        private int _lastSpeed = -1;
        private int _lastCheckpointIndex = -1;
        private int _lastFps = -1;
        private float _nextNetworkUiRefreshTime;
        private bool _nicknameAppliedToSpawnedPlayer;
        private bool _readyAppliedToSpawnedPlayer;

        [Inject]
        public void Construct(
            IPlayerService playerService,
            IRaceManagerService raceManagerService,
            INetworkConnectionService connectionService)
        {
            _playerService = playerService;
            _raceManagerService = raceManagerService;
            _connectionService = connectionService;
        }

        private void Start()
        {
            RemoveGeneratedNetworkPanelIfPresent();
            StartCoroutine(UpdateGameMetrics());
            
            _raceManagerService.OnRaceStarted += UpdateRaceInfoUI;
            _raceManagerService.OnCheckpointPassed += OnCheckpointPassedHandler;
            
            if (_raceManagerService.IsRaceActive)
                UpdateRaceInfoUI();
        }

        private void Update()
        {
            if (!_playerService.IsPlayerCreated) return;
            
            if (_hoverController == null)
            {
                _hoverController = _playerService.Transform.gameObject.GetComponent<HoverController>();
                return;
            }

            UpdatePhysicsUI();
            UpdateNetworkOrRaceInfoUI();
            ApplyLocalNetworkDefaults();
        }

        private void UpdatePhysicsUI()
        {
            var rb = _hoverController.Rb;
            if (rb == null) return;
            
            float rawSpeed = rb.linearVelocity.magnitude * 3.6f;
            int displaySpeed = Mathf.RoundToInt(rawSpeed);
            
            if (displaySpeed != _lastSpeed && _speedText != null)
            {
                _speedText.text = $"{displaySpeed} km/h";
                _lastSpeed = displaySpeed;
            }
            
            if (_speedNeedle != null)
            {
                float t = Mathf.InverseLerp(_minSpeed, _maxSpeed, rawSpeed);
                float angle = Mathf.Lerp(_minAngle, _maxAngle, t);
                _speedNeedle.localRotation = Quaternion.Euler(0, 0, angle);
            }
            
            var lift = _hoverController.LiftEngine;
            var thrust = _hoverController.ThrustEngine;

            if (_liftBar != null && lift != null) _liftBar.fillAmount = lift.CurrentRPM / lift.MaxRPM;
            if (_thrustBar != null && thrust != null) _thrustBar.fillAmount = thrust.CurrentRPM / thrust.MaxRPM;
        }

        private void UpdateRaceInfoUI()
        {
            if (_raceManagerService == null) return;

            float currentTime = _raceManagerService.CurrentTime;

            int minutes = (int)(currentTime / 60);
            int seconds = (int)(currentTime % 60);
            int milliseconds = (int)((currentTime * 100) % 100);
            
            if (_timerText != null)
                _timerText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
            
            int currentCp = _raceManagerService.CurrentCheckpointIndex;
            if (currentCp != _lastCheckpointIndex && _checkpointText != null)
            {
                _checkpointText.text = $"{currentCp} / {_raceManagerService.TotalCheckpoints}";
                _lastCheckpointIndex = currentCp;
            }
        }

        private void UpdateNetworkOrRaceInfoUI()
        {
            if (_networkPlayerData == null && _playerService.IsLocalPlayerCreated)
                _networkPlayerData = _playerService.LocalPlayerTransform.GetComponent<NetworkPlayerData>();

            if (_networkPlayerData == null)
            {
                UpdateRaceInfoUI();
                return;
            }

            NetworkSessionController session = NetworkSessionController.Instance;
            if (_timerText != null)
                _timerText.text = BuildNetworkPhaseLine(session);

            if (_checkpointText != null)
                _checkpointText.text =
                    $"HP {_networkPlayerData.HP.Value} / CP {_networkPlayerData.CheckpointIndex.Value} / Score {_networkPlayerData.Score.Value}";
        }

        private string BuildNetworkPhaseLine(NetworkSessionController session)
        {
            if (session == null)
                return _connectionService != null ? _connectionService.Status.ToString() : "Network";

            return session.Phase.Value switch
            {
                SessionPhase.Lobby => $"Lobby {session.ReadyPlayers.Value}/{session.ConnectedPlayers.Value} ready",
                SessionPhase.Countdown => $"Start in {session.CountdownRemaining.Value:0.0}s",
                SessionPhase.Race => "Race",
                SessionPhase.Results => "Results",
                _ => session.Phase.Value.ToString()
            };
        }

        private void ApplyLocalNetworkDefaults()
        {
            if (Time.unscaledTime < _nextNetworkUiRefreshTime) return;
            _nextNetworkUiRefreshTime = Time.unscaledTime + 0.25f;

            NetworkPlayerData localPlayer = GetLocalNetworkPlayer();
            if (localPlayer == null) return;

            if (!_nicknameAppliedToSpawnedPlayer)
            {
                localPlayer.SetNickname(NetworkPlayerPreferences.GetNickname());
                _nicknameAppliedToSpawnedPlayer = true;
            }

            NetworkSessionController session = NetworkSessionController.Instance;
            if (!_readyAppliedToSpawnedPlayer && session != null && session.Phase.Value == SessionPhase.Lobby)
            {
                localPlayer.SetReady(true);
                _readyAppliedToSpawnedPlayer = true;
            }
        }

        private NetworkPlayerData GetLocalNetworkPlayer()
        {
            if (_networkPlayerData != null && _networkPlayerData.IsOwner)
                return _networkPlayerData;

            NetworkPlayerData[] players = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None);
            foreach (NetworkPlayerData player in players)
            {
                if (player != null && player.IsOwner)
                {
                    _networkPlayerData = player;
                    return _networkPlayerData;
                }
            }

            return null;
        }

        private void RemoveGeneratedNetworkPanelIfPresent()
        {
            Transform existing = transform.Find("NetworkSessionPanel");
            if (existing != null)
                Destroy(existing.gameObject);
        }

        private IEnumerator UpdateGameMetrics()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                int fps = Mathf.RoundToInt(1f / Time.unscaledDeltaTime);
                
                if (fps != _lastFps && _fpsText != null)
                {
                    _fpsText.text = $"FPS: {fps}";
                    _lastFps = fps;
                    _fpsText.color = fps < 30 ? Color.red : Color.green;
                }
                
                yield return wait;
            }
        }
        
        private void OnCheckpointPassedHandler(int index)
        {
            UpdateRaceInfoUI();
        }
        
        private void OnDestroy()
        {
            if (_raceManagerService != null)
            {
                _raceManagerService.OnRaceStarted -= UpdateRaceInfoUI;
                _raceManagerService.OnCheckpointPassed -= OnCheckpointPassedHandler;
            }
        }
    }
}
