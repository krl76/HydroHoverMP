using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.States.Base;
using Core.States.MainMenu;
using Features.Networking;
using Infrastructure.Services.Network;
using Infrastructure.Services.Player;
using Infrastructure.Services.RaceManager;
using Infrastructure.Services.Window;
using Physics.Hover;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.HUD
{
    public class HUDWindow : MonoBehaviour
    {
        private static readonly Color BannerInfoColor = new(0.82f, 0.95f, 1f, 1f);
        private static readonly Color BannerErrorColor = new(1f, 0.45f, 0.35f, 1f);
        private static readonly Color PanelColor = new(0f, 0.1f, 0.18f, 0.72f);
        private static readonly Color TitleColor = new(0.73f, 0.91f, 1f, 1f);
        private static readonly Color ActionButtonColor = new(0.08f, 0.52f, 0.74f, 0.92f);

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
        private IWindowService _windowService;
        private GameStateMachine _stateMachine;
        private HoverController _hoverController;
        private NetworkPlayerData _networkPlayerData;
        private TMP_FontAsset _fontAsset;

        private RectTransform _networkRoot;
        private TextMeshProUGUI _bannerText;
        private TextMeshProUGUI _lobbyBodyText;
        private TextMeshProUGUI _scoreboardBodyText;
        private TextMeshProUGUI _resultsBodyText;
        private TextMeshProUGUI _resultsHintText;
        private Button _restartLobbyButton;
        private Button _exitSessionButton;
        private GameObject _lobbyPanel;
        private GameObject _scoreboardPanel;
        private GameObject _resultsPanel;

        private int _lastSpeed = -1;
        private int _lastCheckpointIndex = -1;
        private int _lastFps = -1;
        private int _lastObservedConnectedPlayers = -1;
        private float _nextNetworkUiRefreshTime;
        private float _bannerVisibleUntil;
        private bool _bannerIsError;
        private bool _nicknameAppliedToSpawnedPlayer;
        private bool _readyAppliedToSpawnedPlayer;
        private bool _leavingSession;

        [Inject]
        public void Construct(
            IPlayerService playerService,
            IRaceManagerService raceManagerService,
            INetworkConnectionService connectionService,
            IWindowService windowService,
            GameStateMachine stateMachine)
        {
            _playerService = playerService;
            _raceManagerService = raceManagerService;
            _connectionService = connectionService;
            _windowService = windowService;
            _stateMachine = stateMachine;
        }

        private void Start()
        {
            CacheFontAsset();
            EnsureNetworkUi();
            RemoveGeneratedNetworkPanelIfPresent();
            StartCoroutine(UpdateGameMetrics());

            if (_raceManagerService != null)
            {
                _raceManagerService.OnRaceStarted += UpdateRaceInfoUI;
                _raceManagerService.OnCheckpointPassed += OnCheckpointPassedHandler;

                if (_raceManagerService.IsRaceActive)
                    UpdateRaceInfoUI();
            }

            if (_connectionService != null)
                _connectionService.OnConnectionFailed += OnConnectionFailed;

            SetNetworkPanelsActive(false, false, false);
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
            if (_networkPlayerData == null && _playerService != null && _playerService.IsLocalPlayerCreated)
                _networkPlayerData = _playerService.LocalPlayerTransform.GetComponent<NetworkPlayerData>();

            NetworkSessionController session = NetworkSessionController.Instance;
            NetworkPlayerData localPlayer = GetLocalNetworkPlayer();
            bool hasNetworkContext = session != null || localPlayer != null ||
                                     (_connectionService != null && _connectionService.Status != NetworkConnectionStatus.Offline);

            if (!hasNetworkContext)
            {
                SetNetworkPanelsActive(false, false, false);
                UpdateRaceInfoUI();
                return;
            }

            if (_timerText != null)
                _timerText.text = BuildNetworkPhaseLine(session);

            if (_checkpointText != null)
                _checkpointText.text = BuildLocalPlayerLine(localPlayer);

            RefreshNetworkPanels(session, localPlayer);
        }

        private string BuildNetworkPhaseLine(NetworkSessionController session)
        {
            if (session == null)
                return _connectionService != null ? _connectionService.Status.ToString() : "Network";

            return session.Phase.Value switch
            {
                SessionPhase.Lobby => $"Lobby {session.ReadyPlayers.Value}/{session.ConnectedPlayers.Value} ready",
                SessionPhase.Countdown => $"Start in {session.CountdownRemaining.Value:0.0}s",
                SessionPhase.Race => "Race in progress",
                SessionPhase.Results => "Results",
                SessionPhase.Disconnected => "Disconnected",
                _ => session.Phase.Value.ToString()
            };
        }

        private string BuildLocalPlayerLine(NetworkPlayerData localPlayer)
        {
            if (localPlayer == null)
            {
                return _connectionService != null
                    ? $"Status: {_connectionService.Status}"
                    : "Waiting for local network player...";
            }

            int checkpointTotal = NetworkRaceManager.Instance != null
                ? NetworkRaceManager.Instance.TotalCheckpoints
                : Mathf.Max(1, localPlayer.CheckpointIndex.Value);

            string ready = localPlayer.IsReady.Value ? "Ready" : "Not Ready";
            string finish = localPlayer.IsFinished.Value
                ? $" | Finish {FormatTime(localPlayer.FinishTime.Value)}"
                : string.Empty;

            return $"{localPlayer.Nickname.Value} | {ready} | HP {localPlayer.HP.Value} | CP {localPlayer.CheckpointIndex.Value}/{checkpointTotal} | Score {localPlayer.Score.Value}{finish}";
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

        private void RefreshNetworkPanels(NetworkSessionController session, NetworkPlayerData localPlayer)
        {
            EnsureNetworkUi();

            List<NetworkPlayerData> players = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None)
                .Where(player => player != null)
                .OrderBy(player => player.ClientId)
                .ToList();

            TrackDisconnectBanner(session);

            bool showLobby = session != null && session.Phase.Value is SessionPhase.Lobby or SessionPhase.Countdown;
            bool showScoreboard = players.Count > 0;
            bool showResults = session != null && session.Phase.Value == SessionPhase.Results;
            SetNetworkPanelsActive(showLobby, showScoreboard, showResults);

            if (_lobbyBodyText != null)
                _lobbyBodyText.text = BuildLobbyRoster(players, session, localPlayer);

            if (_scoreboardBodyText != null)
                _scoreboardBodyText.text = BuildScoreboard(players, localPlayer);

            if (_resultsBodyText != null)
                _resultsBodyText.text = BuildResults(players, session, localPlayer);

            if (_resultsHintText != null)
            {
                _resultsHintText.text = showResults
                    ? "Restart returns everyone to the lobby. Exit stops the local session and returns to the main menu."
                    : string.Empty;
            }

            if (_restartLobbyButton != null)
                _restartLobbyButton.gameObject.SetActive(showResults && session != null);

            if (_exitSessionButton != null)
                _exitSessionButton.gameObject.SetActive(showResults || players.Count > 0 || session != null);

            RefreshBannerText(session, players.Count);
        }

        private void TrackDisconnectBanner(NetworkSessionController session)
        {
            if (session == null)
            {
                _lastObservedConnectedPlayers = -1;
                return;
            }

            int connectedPlayers = session.ConnectedPlayers.Value;
            if (_lastObservedConnectedPlayers >= 0 && connectedPlayers < _lastObservedConnectedPlayers && !_leavingSession)
            {
                ShowBanner(connectedPlayers > 0
                    ? "A remote pilot disconnected. Session data has been refreshed."
                    : "All remote pilots disconnected. You are alone in the session.", false, 6f);
            }

            _lastObservedConnectedPlayers = connectedPlayers;
        }

        private void RefreshBannerText(NetworkSessionController session, int playerCount)
        {
            if (_bannerText == null) return;

            if (Time.unscaledTime <= _bannerVisibleUntil)
            {
                _bannerText.color = _bannerIsError ? BannerErrorColor : BannerInfoColor;
                return;
            }

            string passiveLine = BuildPassiveBannerLine(session, playerCount);
            _bannerText.text = passiveLine;
            _bannerText.color = BannerInfoColor;
        }

        private string BuildPassiveBannerLine(NetworkSessionController session, int playerCount)
        {
            if (_connectionService == null)
                return "Network service unavailable.";

            return _connectionService.Status switch
            {
                NetworkConnectionStatus.StartingHost => "Preparing host session and loading gameplay scene...",
                NetworkConnectionStatus.StartingClient => "Connecting to host and waiting for the shared gameplay scene...",
                NetworkConnectionStatus.HostStarted when session != null && session.Phase.Value == SessionPhase.Lobby && playerCount < 2
                    => "Host is online. Waiting for another pilot to join the lobby.",
                NetworkConnectionStatus.ClientStarted when session != null && session.Phase.Value == SessionPhase.Lobby
                    => "Connected to host. Waiting for the lobby roster to finish synchronizing.",
                NetworkConnectionStatus.Failed => "Connection failed. Return to the main menu and try again.",
                NetworkConnectionStatus.Offline when !_leavingSession => "Connection is offline. Multiplayer data is no longer updating.",
                _ => session != null ? BuildNetworkPhaseLine(session) : $"Connection status: {_connectionService.Status}"
            };
        }

        private string BuildLobbyRoster(IReadOnlyList<NetworkPlayerData> players, NetworkSessionController session, NetworkPlayerData localPlayer)
        {
            if (players.Count == 0)
                return "Waiting for network player spawns...";

            string readinessLine = session == null
                ? "Lobby sync pending..."
                : $"{session.ReadyPlayers.Value}/{session.ConnectedPlayers.Value} ready";

            List<string> lines = new() { readinessLine, string.Empty };
            foreach (NetworkPlayerData player in players)
            {
                string you = player == localPlayer ? " (you)" : string.Empty;
                string ready = player.IsReady.Value ? "READY" : "WAITING";
                lines.Add($"• {player.Nickname.Value}{you}  —  {ready}");
            }

            return string.Join("\n", lines);
        }

        private string BuildScoreboard(IReadOnlyList<NetworkPlayerData> players, NetworkPlayerData localPlayer)
        {
            if (players.Count == 0)
                return "Scoreboard unavailable until players spawn.";

            IEnumerable<NetworkPlayerData> orderedPlayers = players
                .OrderByDescending(player => player.Score.Value)
                .ThenByDescending(player => player.CheckpointIndex.Value)
                .ThenBy(player => player.ClientId);

            List<string> lines = new() { "Pilot                         HP   Score   CP   State" };
            foreach (NetworkPlayerData player in orderedPlayers)
            {
                string marker = player == localPlayer ? "*" : " ";
                string nickname = Truncate(player.Nickname.Value, 14).PadRight(14);
                string state = player.IsFinished.Value
                    ? "FIN"
                    : player.IsAlive ? (player.IsReady.Value ? "LIVE" : "SYNC") : "OUT";
                lines.Add($"{marker} {nickname}  {player.HP.Value,3}   {player.Score.Value,5}   {player.CheckpointIndex.Value,2}   {state}");
            }

            return string.Join("\n", lines);
        }

        private string BuildResults(IReadOnlyList<NetworkPlayerData> players, NetworkSessionController session, NetworkPlayerData localPlayer)
        {
            if (session == null || session.Phase.Value != SessionPhase.Results)
                return string.Empty;

            if (session.Results.Count > 0)
                return BuildSnapshotResults(session.Results, localPlayer);

            if (players.Count == 0)
                return "Results are waiting for the server snapshot to synchronize.";

            IEnumerable<NetworkPlayerData> orderedPlayers = players
                .OrderByDescending(player => player.IsFinished.Value)
                .ThenBy(player => player.IsFinished.Value ? player.FinishTime.Value : float.MaxValue)
                .ThenByDescending(player => player.Score.Value)
                .ThenByDescending(player => player.CheckpointIndex.Value)
                .ThenBy(player => player.ClientId);

            List<string> lines = new();
            int placement = 1;
            foreach (NetworkPlayerData player in orderedPlayers)
            {
                string marker = player == localPlayer ? " (you)" : string.Empty;
                string finishState = player.IsFinished.Value
                    ? FormatTime(player.FinishTime.Value)
                    : player.IsAlive ? "DNF" : "Destroyed";
                lines.Add($"{placement}. {player.Nickname.Value}{marker}  —  {finishState}  —  Score {player.Score.Value}");
                placement++;
            }

            return string.Join("\n", lines);
        }

        private static string BuildSnapshotResults(IReadOnlyList<NetworkRaceResult> results, NetworkPlayerData localPlayer)
        {
            IEnumerable<NetworkRaceResult> orderedResults = results
                .OrderByDescending(result => result.IsFinished)
                .ThenBy(result => result.IsFinished ? result.FinishTime : float.MaxValue)
                .ThenByDescending(result => result.Score)
                .ThenByDescending(result => result.CheckpointIndex)
                .ThenBy(result => result.ClientId);

            List<string> lines = new();
            int placement = 1;
            foreach (NetworkRaceResult result in orderedResults)
            {
                string marker = localPlayer != null && result.ClientId == localPlayer.ClientId ? " (you)" : string.Empty;
                string finishState = result.IsFinished
                    ? FormatTime(result.FinishTime)
                    : result.HP > 0 ? "DNF" : "Destroyed";
                string disconnect = result.IsDisconnected ? " | disconnected" : string.Empty;
                lines.Add($"{placement}. {result.Nickname}{marker}  —  {finishState}  —  Score {result.Score}{disconnect}");
                placement++;
            }

            return string.Join("\n", lines);
        }

        private void OnRestartLobbyClicked()
        {
            NetworkSessionController session = NetworkSessionController.Instance;
            if (session == null)
            {
                ShowBanner("Restart is unavailable because the session controller is missing.", true, 6f);
                return;
            }

            session.RequestRestartServerRpc();
            ShowBanner("Restart requested. Waiting for the host to return everyone to the lobby.", false, 5f);
        }

        private void OnExitSessionClicked()
        {
            _leavingSession = true;
            _connectionService?.StopConnection();
            _windowService?.Close(WindowID.HUD);
            _stateMachine?.Enter<MainMenuState>();
        }

        private void OnConnectionFailed(string message)
        {
            ShowBanner(message, true, 8f);
        }

        private void ShowBanner(string message, bool error, float duration)
        {
            EnsureNetworkUi();
            if (_bannerText == null) return;

            _bannerText.text = message;
            _bannerText.color = error ? BannerErrorColor : BannerInfoColor;
            _bannerIsError = error;
            _bannerVisibleUntil = Time.unscaledTime + duration;
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

            if (_connectionService != null)
                _connectionService.OnConnectionFailed -= OnConnectionFailed;
        }

        private void CacheFontAsset()
        {
            _fontAsset = _speedText != null
                ? _speedText.font
                : GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(text => text != null && text.font != null)?.font;
        }

        private void EnsureNetworkUi()
        {
            if (_networkRoot != null)
                return;

            GameObject root = new("MultiplayerPresentationRoot");
            root.transform.SetParent(transform, false);
            _networkRoot = root.AddComponent<RectTransform>();
            _networkRoot.anchorMin = new Vector2(1f, 1f);
            _networkRoot.anchorMax = new Vector2(1f, 1f);
            _networkRoot.pivot = new Vector2(1f, 1f);
            _networkRoot.anchoredPosition = new Vector2(-24f, -20f);
            _networkRoot.sizeDelta = new Vector2(420f, 620f);

            _bannerText = CreateSectionText("NetworkBanner", _networkRoot, new Vector2(0f, 0f), new Vector2(420f, 56f), 20, TextAlignmentOptions.Center, BannerInfoColor);
            _lobbyPanel = CreatePanelSection("LobbyPanel", "LOBBY ROSTER", new Vector2(0f, -66f), new Vector2(420f, 156f), out _lobbyBodyText);
            _scoreboardPanel = CreatePanelSection("ScoreboardPanel", "RACE SCOREBOARD", new Vector2(0f, -236f), new Vector2(420f, 186f), out _scoreboardBodyText, true);
            _resultsPanel = CreatePanelSection("ResultsPanel", "SESSION RESULTS", new Vector2(0f, -436f), new Vector2(420f, 184f), out _resultsBodyText);

            _resultsHintText = CreateSectionText("ResultsHint", _resultsPanel.transform as RectTransform, new Vector2(14f, 122f), new Vector2(392f, 42f), 17, TextAlignmentOptions.TopLeft, new Color(0.83f, 0.9f, 0.96f, 1f));

            _restartLobbyButton = CreateActionButton(_resultsPanel.transform as RectTransform, "RestartLobbyButton", "Restart Lobby", new Vector2(16f, 144f), OnRestartLobbyClicked);
            _exitSessionButton = CreateActionButton(_resultsPanel.transform as RectTransform, "ExitSessionButton", "Exit Session", new Vector2(220f, 144f), OnExitSessionClicked);
        }

        private GameObject CreatePanelSection(string objectName, string title, Vector2 position, Vector2 size, out TextMeshProUGUI bodyText, bool monospace = false)
        {
            GameObject panel = new(objectName);
            panel.transform.SetParent(_networkRoot, false);

            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image background = panel.AddComponent<Image>();
            background.color = PanelColor;

            CreateSectionText($"{objectName}_Title", rect, new Vector2(14f, 10f), new Vector2(size.x - 28f, 28f), 19, TextAlignmentOptions.TopLeft, TitleColor).text = title;
            bodyText = CreateSectionText($"{objectName}_Body", rect, new Vector2(14f, 42f), new Vector2(size.x - 28f, size.y - 52f), monospace ? 16 : 18, TextAlignmentOptions.TopLeft, Color.white);
            bodyText.textWrappingMode = monospace ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            bodyText.overflowMode = TextOverflowModes.Overflow;
            return panel;
        }

        private TextMeshProUGUI CreateSectionText(string objectName, RectTransform parent, Vector2 topLeft, Vector2 size, int fontSize, TextAlignmentOptions alignment, Color color)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            if (_fontAsset != null)
                text.font = _fontAsset;
            return text;
        }

        private Button CreateActionButton(RectTransform parent, string objectName, string label, Vector2 topLeft, UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = new(objectName);
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = new Vector2(180f, 34f);

            Image background = buttonObject.AddComponent<Image>();
            background.color = ActionButtonColor;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            TextMeshProUGUI labelText = CreateSectionText($"{objectName}_Label", rect, Vector2.zero, rect.sizeDelta, 18, TextAlignmentOptions.Center, Color.white);
            labelText.text = label;
            return button;
        }

        private void SetNetworkPanelsActive(bool showLobby, bool showScoreboard, bool showResults)
        {
            if (_lobbyPanel != null) _lobbyPanel.SetActive(showLobby);
            if (_scoreboardPanel != null) _scoreboardPanel.SetActive(showScoreboard);
            if (_resultsPanel != null) _resultsPanel.SetActive(showResults);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;

            return value[..maxLength];
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
