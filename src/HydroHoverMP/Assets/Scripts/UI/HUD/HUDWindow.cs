using System.Collections;
using System.Linq;
using Core.States.Base;
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
        private static readonly Color NetworkInfoColor = new(0.82f, 0.95f, 1f, 1f);
        private static readonly Color NetworkErrorColor = new(1f, 0.45f, 0.35f, 1f);

        [Header("Texts")]
        [SerializeField] private TextMeshProUGUI _speedText;
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private TextMeshProUGUI _checkpointText;
        [SerializeField] private TextMeshProUGUI _fpsText;
        [SerializeField] private TextMeshProUGUI _pingText;

        [Header("Speedometer")]
        [SerializeField] private RectTransform _speedNeedle;
        [SerializeField] private float _minSpeed = 0f;
        [SerializeField] private float _maxSpeed = 200f;
        [SerializeField] private float _minAngle = 135f;
        [SerializeField] private float _maxAngle = -135f;

        [Header("Bars")]
        [SerializeField] private Image _liftBar;
        [SerializeField] private Image _thrustBar;

        [Header("Network")]
        [SerializeField] private Button _hostStartButton;

        private IPlayerService _playerService;
        private IRaceManagerService _raceManagerService;
        private INetworkConnectionService _connectionService;
        private GameStateMachine _stateMachine;
        private HoverController _hoverController;
        private NetworkPlayerData _networkPlayerData;

        private int _lastSpeed = -1;
        private int _lastCheckpointIndex = -1;
        private int _lastFps = -1;
        private int _lastObservedConnectedPlayers = -1;
        private float _nextNetworkUiRefreshTime;
        private float _nextPingRefreshTime;
        private float _networkMessageVisibleUntil;
        private bool _networkMessageIsError;
        private bool _nicknameAppliedToSpawnedPlayer;
        private bool _leavingSession;
        private string _networkMessage;

        [Inject]
        public void Construct(
            IPlayerService playerService,
            IRaceManagerService raceManagerService,
            INetworkConnectionService connectionService,
            GameStateMachine stateMachine)
        {
            _playerService = playerService;
            _raceManagerService = raceManagerService;
            _connectionService = connectionService;
            _stateMachine = stateMachine;
        }

        private void Start()
        {
            RemoveGeneratedNetworkPanelIfPresent();
            StartCoroutine(UpdateGameMetrics());
            RefreshPingText(force: true);

            if (_raceManagerService != null)
            {
                _raceManagerService.OnRaceStarted += UpdateRaceInfoUI;
                _raceManagerService.OnCheckpointPassed += OnCheckpointPassedHandler;

                if (_raceManagerService.IsRaceActive)
                    UpdateRaceInfoUI();
            }

            if (_connectionService != null)
                _connectionService.OnConnectionFailed += OnConnectionFailed;

            if (_hostStartButton != null)
            {
                _hostStartButton.onClick.AddListener(OnHostStartClicked);
                _hostStartButton.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (_playerService != null && _playerService.IsPlayerCreated)
            {
                if (_hoverController == null)
                {
                    _hoverController = _playerService.Transform.gameObject.GetComponent<HoverController>();
                }
                else
                {
                    UpdatePhysicsUI();
                }
            }

            UpdateNetworkOrRaceInfoUI();
            ApplyLocalNetworkDefaults();
            RefreshPingText();
        }

        private void UpdatePhysicsUI()
        {
            Rigidbody rb = _hoverController.Rb;
            if (rb == null)
                return;

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

            if (_liftBar != null && lift != null)
                _liftBar.fillAmount = lift.CurrentRPM / lift.MaxRPM;

            if (_thrustBar != null && thrust != null)
                _thrustBar.fillAmount = thrust.CurrentRPM / thrust.MaxRPM;
        }

        private void UpdateRaceInfoUI()
        {
            if (_raceManagerService == null)
                return;

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
            if (_networkPlayerData == null && _playerService != null && _playerService.IsLocalPlayerCreated)
                _networkPlayerData = _playerService.LocalPlayerTransform.GetComponent<NetworkPlayerData>();

            NetworkSessionController session = NetworkSessionController.Instance;
            NetworkPlayerData localPlayer = GetLocalNetworkPlayer();
            bool hasNetworkContext = session != null || localPlayer != null ||
                                     (_connectionService != null && _connectionService.Status != NetworkConnectionStatus.Offline);

            if (!hasNetworkContext)
            {
                UpdateRaceInfoUI();
                RestoreNetworkTextColors();
                RefreshHostStartButton(null, null);
                return;
            }

            RefreshHostStartButton(session, localPlayer);

            if (_timerText != null)
            {
                _timerText.text = HasTransientNetworkMessage()
                    ? _networkMessage
                    : BuildNetworkPhaseLine(session);
                _timerText.color = HasTransientNetworkMessage() && _networkMessageIsError ? NetworkErrorColor : Color.white;
            }

            if (_checkpointText != null)
            {
                _checkpointText.text = BuildLocalPlayerLine(localPlayer);
                _checkpointText.color = HasTransientNetworkMessage() && !_networkMessageIsError ? NetworkInfoColor : Color.white;
            }

            TrackDisconnectMessage(session);
        }

        private string BuildNetworkPhaseLine(NetworkSessionController session)
        {
            if (session == null)
                return _connectionService != null ? _connectionService.Status.ToString() : "Network";

            return session.Phase.Value switch
            {
                SessionPhase.Lobby => $"Lobby - waiting ({session.ConnectedPlayers.Value})",
                SessionPhase.Countdown => $"Start in {Mathf.Max(1, Mathf.CeilToInt(session.CountdownRemaining.Value))}",
                SessionPhase.Race => "Race live",
                SessionPhase.Results => BuildResultsSummary(session),
                SessionPhase.Disconnected => "Disconnected",
                _ => session.Phase.Value.ToString()
            };
        }

        private string BuildLocalPlayerLine(NetworkPlayerData localPlayer)
        {
            if (localPlayer == null)
                return "CP --";

            int checkpointTotal = NetworkRaceManager.Instance != null
                ? NetworkRaceManager.Instance.TotalCheckpoints
                : Mathf.Max(1, localPlayer.CheckpointIndex.Value);

            return $"CP {localPlayer.CheckpointIndex.Value}/{checkpointTotal}";
        }

        private string BuildResultsSummary(NetworkSessionController session)
        {
            NetworkPlayerData localPlayer = GetLocalNetworkPlayer();
            if (session.Results.Count > 0)
            {
                NetworkRaceResult? localResult = session.Results.FirstOrDefault(result => localPlayer != null && result.ClientId == localPlayer.ClientId);
                if (localResult.HasValue)
                {
                    NetworkRaceResult result = localResult.Value;
                    string finish = result.IsFinished ? FormatTime(result.FinishTime) : "DNF";
                    return $"Results | Score {result.Score} | {finish}";
                }

                return $"Results | {session.Results.Count} pilots ranked";
            }

            return "Results";
        }

        private void ApplyLocalNetworkDefaults()
        {
            if (Time.unscaledTime < _nextNetworkUiRefreshTime)
                return;

            _nextNetworkUiRefreshTime = Time.unscaledTime + 0.25f;

            NetworkPlayerData localPlayer = GetLocalNetworkPlayer();
            if (localPlayer == null)
                return;

            if (!_nicknameAppliedToSpawnedPlayer)
            {
                localPlayer.SetNickname(NetworkPlayerPreferences.GetNickname());
                _nicknameAppliedToSpawnedPlayer = true;
            }
        }

        private void RefreshPingText(bool force = false)
        {
            if (_pingText == null)
                return;

            if (!force && Time.unscaledTime < _nextPingRefreshTime)
                return;

            _nextPingRefreshTime = Time.unscaledTime + 1f;
            _pingText.text = BuildPingLabel();
        }

        private static string BuildPingLabel()
        {
            return "Ping -- ms";
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

        private void TrackDisconnectMessage(NetworkSessionController session)
        {
            if (session == null)
            {
                _lastObservedConnectedPlayers = -1;
                return;
            }

            int connectedPlayers = session.ConnectedPlayers.Value;
            if (_lastObservedConnectedPlayers >= 0 && connectedPlayers < _lastObservedConnectedPlayers && !_leavingSession)
            {
                ShowNetworkMessage(connectedPlayers > 0
                    ? "A remote pilot disconnected."
                    : "All remote pilots disconnected.", false, 6f);
            }

            _lastObservedConnectedPlayers = connectedPlayers;
        }

        private void OnConnectionFailed(string message)
        {
            ShowNetworkMessage(message, true, 8f);
        }

        private void ShowNetworkMessage(string message, bool error, float duration)
        {
            _networkMessage = message;
            _networkMessageIsError = error;
            _networkMessageVisibleUntil = Time.unscaledTime + duration;
        }

        private bool HasTransientNetworkMessage()
        {
            return !string.IsNullOrWhiteSpace(_networkMessage) && Time.unscaledTime <= _networkMessageVisibleUntil;
        }

        private void RestoreNetworkTextColors()
        {
            if (_timerText != null)
                _timerText.color = Color.white;

            if (_checkpointText != null)
                _checkpointText.color = Color.white;
        }

        private void RemoveGeneratedNetworkPanelIfPresent()
        {
            Transform presentationRoot = transform.Find("MultiplayerPresentationRoot");
            if (presentationRoot != null)
                Destroy(presentationRoot.gameObject);

            Transform legacyPanel = transform.Find("NetworkSessionPanel");
            if (legacyPanel != null)
                Destroy(legacyPanel.gameObject);
        }

        private IEnumerator UpdateGameMetrics()
        {
            WaitForSeconds wait = new(0.5f);
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

            if (_connectionService != null)
                _connectionService.OnConnectionFailed -= OnConnectionFailed;

            if (_hostStartButton != null)
                _hostStartButton.onClick.RemoveListener(OnHostStartClicked);
        }

        private void RefreshHostStartButton(NetworkSessionController session, NetworkPlayerData localPlayer)
        {
            if (_hostStartButton == null)
                return;

            bool canShow = session != null &&
                           localPlayer != null &&
                           localPlayer.ClientId == 0 &&
                           session.Phase.Value == SessionPhase.Lobby &&
                           session.ConnectedPlayers.Value >= session.MinimumPlayers &&
                           _connectionService != null &&
                           (_connectionService.IsHost || _connectionService.IsServer);

            if (_hostStartButton.gameObject.activeSelf != canShow)
                _hostStartButton.gameObject.SetActive(canShow);

            _hostStartButton.interactable = canShow;
        }

        private void OnHostStartClicked()
        {
            NetworkSessionController session = NetworkSessionController.Instance;
            if (session == null)
                return;

            session.RequestForceStartServerRpc();
        }

        private static string FormatTime(float timeSeconds)
        {
            int minutes = (int)(timeSeconds / 60f);
            int seconds = (int)(timeSeconds % 60f);
            int milliseconds = (int)((timeSeconds * 100f) % 100f);
            return $"{minutes:00}:{seconds:00}.{milliseconds:00}";
        }
    }
}
