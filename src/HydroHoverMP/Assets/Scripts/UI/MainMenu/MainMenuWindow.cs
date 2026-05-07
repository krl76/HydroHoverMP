using System.Collections.Generic;
using System.Linq;
using Features.Networking;
using Infrastructure.Services.Network;
using Infrastructure.Services.Window;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.MainMenu
{
    public class MainMenuWindow : MonoBehaviour
    {
        private const ushort DefaultPort = 7770;

        private enum ConnectionMode
        {
            None,
            Host,
            Client
        }

        [Header("Existing menu buttons")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _leaderboardButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _exitButton;

        [Header("Inline connection form")]
        [SerializeField] private TMP_InputField _nicknameInput;
        [SerializeField] private TMP_InputField _addressInput;
        [SerializeField] private TMP_InputField _portInput;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private TextMeshProUGUI _modeTitleText;

        private IWindowService _windowService;
        private INetworkConnectionService _connectionService;
        private TMP_FontAsset _fontAsset;
        private ConnectionMode _mode;
        private Vector2 _playStartPosition;
        private Vector2 _clientStartPosition;
        private Vector2 _settingsStartPosition;
        private Vector2 _exitStartPosition;
        private float _nextRefreshTime;
        private string _lastConnectionFailure;

        private RectTransform _multiplayerStatusRoot;
        private TextMeshProUGUI _sessionSummaryText;
        private TextMeshProUGUI _rosterText;
        private TextMeshProUGUI _resultsText;
        private TextMeshProUGUI _connectionHintText;

        [Inject]
        public void Construct(IWindowService windowService, INetworkConnectionService connectionService)
        {
            _windowService = windowService;
            _connectionService = connectionService;
        }

        private void Start()
        {
            CacheFont();
            CacheButtonPositions();
            EnsureInlineForm();
            EnsureMultiplayerStatusPanel();
            ConfigureButtonsForRootMenu();
            SubscribeConnectionEvents();
            LoadSavedNetworkPreferences();
            ShowRootMenu();
            RefreshConnectionUi();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime) return;

            _nextRefreshTime = Time.unscaledTime + 0.5f;
            _connectionService?.RefreshStatus();
            RefreshConnectionUi();
            RefreshMultiplayerSummary();
        }

        private void OnDestroy()
        {
            if (_connectionService == null) return;

            _connectionService.OnStatusChanged -= OnConnectionStatusChanged;
            _connectionService.OnClientCountChanged -= OnClientCountChanged;
            _connectionService.OnConnectionFailed -= OnConnectionFailed;
        }

        private void ConfigureButtonsForRootMenu()
        {
            ClearButtonListeners(_playButton);
            ClearButtonListeners(_leaderboardButton);
            ClearButtonListeners(_settingsButton);
            ClearButtonListeners(_exitButton);

            if (_playButton != null)
                _playButton.onClick.AddListener(() => ShowConnectionForm(ConnectionMode.Host));

            if (_leaderboardButton != null)
                _leaderboardButton.onClick.AddListener(() => ShowConnectionForm(ConnectionMode.Client));

            if (_settingsButton != null)
                _settingsButton.onClick.AddListener(Settings);

#if UNITY_EDITOR
            if (_exitButton != null)
                _exitButton.onClick.AddListener(EditorApplication.ExitPlaymode);
#else
            if (_exitButton != null)
                _exitButton.onClick.AddListener(Application.Quit);
#endif
        }

        private void ConfigureButtonsForForm(ConnectionMode mode)
        {
            ClearButtonListeners(_playButton);
            ClearButtonListeners(_leaderboardButton);

            if (_playButton != null)
                _playButton.onClick.AddListener(mode == ConnectionMode.Host ? StartHostFromMenu : StartClientFromMenu);

            if (_leaderboardButton != null)
                _leaderboardButton.onClick.AddListener(ShowRootMenu);
        }

        private void ShowRootMenu()
        {
            _mode = ConnectionMode.None;

            RestoreButtonPositions();
            SetButtonLabel(_playButton, "Host");
            SetButtonLabel(_leaderboardButton, "Client");
            SetButtonLabel(_settingsButton, "Settings");
            SetButtonLabel(_exitButton, "Exit");

            SetButtonActive(_playButton, true);
            SetButtonActive(_leaderboardButton, true);
            SetButtonActive(_settingsButton, true);
            SetButtonActive(_exitButton, true);
            SetFormActive(false);
            ConfigureButtonsForRootMenu();
            RefreshConnectionUi();
            RefreshMultiplayerSummary();
        }

        private void ShowConnectionForm(ConnectionMode mode)
        {
            _mode = mode;
            ConfigureButtonsForForm(mode);
            SetFormActive(true);

            SetButtonLabel(_playButton, mode == ConnectionMode.Host ? "Start Host" : "Connect");
            SetButtonLabel(_leaderboardButton, "Back");
            SetButtonActive(_settingsButton, false);
            SetButtonActive(_exitButton, false);

            MoveButton(_playButton, new Vector2(0f, -92f));
            MoveButton(_leaderboardButton, new Vector2(0f, -176f));

            if (_modeTitleText != null)
                _modeTitleText.text = mode == ConnectionMode.Host ? "HOST SESSION" : "JOIN SESSION";

            if (_addressInput != null)
                _addressInput.gameObject.SetActive(mode == ConnectionMode.Client);

            RefreshConnectionUi();
            RefreshMultiplayerSummary();
        }

        private void SubscribeConnectionEvents()
        {
            if (_connectionService == null) return;

            _connectionService.OnStatusChanged += OnConnectionStatusChanged;
            _connectionService.OnClientCountChanged += OnClientCountChanged;
            _connectionService.OnConnectionFailed += OnConnectionFailed;
        }

        private void LoadSavedNetworkPreferences()
        {
            if (_nicknameInput != null)
                _nicknameInput.text = NetworkPlayerPreferences.GetNickname();

            if (_addressInput != null)
                _addressInput.text = NetworkPlayerPreferences.GetAddress();

            if (_portInput != null)
                _portInput.text = NetworkPlayerPreferences.GetPort().ToString();
        }

        private void StartHostFromMenu()
        {
            if (!TryPrepareConnection()) return;
            if (!TryGetPortFromInput(out ushort port)) return;

            _lastConnectionFailure = null;
            SetStatus($"Starting Host on port {port}...", false);
            if (!_connectionService.StartHost(port))
                RefreshConnectionUi();
            RefreshMultiplayerSummary();
        }

        private void StartClientFromMenu()
        {
            if (!TryPrepareConnection()) return;
            if (!TryGetPortFromInput(out ushort port)) return;

            string address = _addressInput != null ? _addressInput.text : NetworkPlayerPreferences.GetAddress();
            _lastConnectionFailure = null;
            SetStatus($"Connecting to {NormalizeAddress(address)}:{port}...", false);
            if (!_connectionService.StartClient(address, port))
                RefreshConnectionUi();
            RefreshMultiplayerSummary();
        }

        private bool TryPrepareConnection()
        {
            if (_connectionService == null)
            {
                SetStatus("Network service is not ready.", true);
                return false;
            }

            SaveNetworkPreferences();
            return true;
        }

        private void SaveNetworkPreferences()
        {
            if (_nicknameInput != null)
                NetworkPlayerPreferences.SetNickname(_nicknameInput.text);

            if (_addressInput != null)
                NetworkPlayerPreferences.SetAddress(_addressInput.text);

            if (TryParsePort(_portInput != null ? _portInput.text : null, out ushort port, out _))
                NetworkPlayerPreferences.SetPort(port);
        }

        private bool TryGetPortFromInput(out ushort port)
        {
            string portText = _portInput != null ? _portInput.text : NetworkPlayerPreferences.GetPort().ToString();
            if (TryParsePort(portText, out port, out string error))
                return true;

            SetStatus(error, true);
            return false;
        }

        private void OnConnectionStatusChanged(NetworkConnectionStatus status)
        {
            RefreshConnectionUi();
            RefreshMultiplayerSummary();
        }

        private void OnClientCountChanged(int count)
        {
            RefreshConnectionUi();
            RefreshMultiplayerSummary();
        }

        private void OnConnectionFailed(string message)
        {
            _lastConnectionFailure = message;
            RefreshConnectionUi();
            RefreshMultiplayerSummary();
        }

        private void RefreshConnectionUi()
        {
            if (_connectionService == null)
            {
                SetStatus("NetworkConnectionService is unavailable.", true);
                return;
            }

            NetworkConnectionStatus status = _connectionService.Status;
            bool canStart = status is NetworkConnectionStatus.Offline or NetworkConnectionStatus.Failed;
            if (_playButton != null)
                _playButton.interactable = canStart;

            if (_mode == ConnectionMode.None)
            {
                if (_leaderboardButton != null)
                    _leaderboardButton.interactable = canStart;
                return;
            }

            if (_leaderboardButton != null)
                _leaderboardButton.interactable = true;

            string address = _addressInput != null ? NormalizeAddress(_addressInput.text) : NetworkPlayerPreferences.GetAddress();
            string port = _portInput != null && !string.IsNullOrWhiteSpace(_portInput.text)
                ? _portInput.text.Trim()
                : NetworkPlayerPreferences.GetPort().ToString();
            string statusLine = status switch
            {
                NetworkConnectionStatus.Offline => _mode == ConnectionMode.Host
                    ? "Enter nickname/port, then Start Host."
                    : "Enter nickname/address/port, then Connect.",
                NetworkConnectionStatus.StartingHost => $"Starting Host on port {port}... loading shared Gameplay scene.",
                NetworkConnectionStatus.StartingClient => $"Connecting to {address}:{port}...",
                NetworkConnectionStatus.HostStarted => $"Host started. Clients: {_connectionService.ConnectedClientCount}.",
                NetworkConnectionStatus.ClientStarted => "Client connected. Waiting for server scene/lobby.",
                NetworkConnectionStatus.Failed => string.IsNullOrWhiteSpace(_lastConnectionFailure)
                    ? "Connection failed. Check address, port and FishNet setup."
                    : _lastConnectionFailure,
                _ => status.ToString()
            };

            SetStatus(statusLine, status == NetworkConnectionStatus.Failed);
        }

        private void Settings()
        {
            _windowService.Open(WindowID.Settings);
            _windowService.Close(WindowID.MainMenu);
        }

        private void EnsureInlineForm()
        {
            _modeTitleText ??= CreateText("ConnectionModeTitle", "HOST SESSION", new Vector2(0f, 176f), new Vector2(420f, 48f), 28, TextAlignmentOptions.Center);
            _nicknameInput ??= CreateInput("NicknameInput", "Pilot", new Vector2(0f, 96f), new Vector2(330f, 48f));
            _addressInput ??= CreateInput("AddressInput", "localhost", new Vector2(0f, 30f), new Vector2(330f, 48f));
            _portInput ??= CreateInput("PortInput", DefaultPort.ToString(), new Vector2(0f, -36f), new Vector2(330f, 48f));
            _statusText ??= CreateText("ConnectionStatus", string.Empty, new Vector2(0f, -250f), new Vector2(560f, 72f), 20, TextAlignmentOptions.Center);
        }

        private TMP_InputField CreateInput(string objectName, string defaultValue, Vector2 position, Vector2 size)
        {
            GameObject root = new(objectName);
            root.transform.SetParent(transform, false);

            RectTransform rect = root.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image background = root.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.92f);

            TMP_InputField input = root.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.text = defaultValue;
            input.lineType = TMP_InputField.LineType.SingleLine;

            GameObject textArea = new("Text Area");
            textArea.transform.SetParent(root.transform, false);
            RectTransform viewport = textArea.AddComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(14f, 7f);
            viewport.offsetMax = new Vector2(-14f, -7f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = CreateChildText(textArea.transform, "Placeholder", defaultValue, 22, TextAlignmentOptions.Left);
            placeholder.color = new Color(0f, 0.17f, 0.29f, 0.45f);

            TextMeshProUGUI text = CreateChildText(textArea.transform, "Text", string.Empty, 22, TextAlignmentOptions.Left);
            text.color = new Color(0f, 0.17f, 0.29f, 1f);

            input.textViewport = viewport;
            input.placeholder = placeholder;
            input.textComponent = text;
            return input;
        }

        private TextMeshProUGUI CreateText(string objectName, string text, Vector2 position, Vector2 size, int fontSize, TextAlignmentOptions alignment)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(transform, false);

            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return ConfigureText(textObject.AddComponent<TextMeshProUGUI>(), text, fontSize, alignment, Color.white);
        }

        private TextMeshProUGUI CreateChildText(Transform parent, string objectName, string text, int fontSize, TextAlignmentOptions alignment)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return ConfigureText(textObject.AddComponent<TextMeshProUGUI>(), text, fontSize, alignment, Color.white);
        }

        private TextMeshProUGUI ConfigureText(TextMeshProUGUI label, string text, int fontSize, TextAlignmentOptions alignment, Color color)
        {
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            label.color = color;
            if (_fontAsset != null)
                label.font = _fontAsset;
            return label;
        }

        private void SetFormActive(bool active)
        {
            if (_modeTitleText != null) _modeTitleText.gameObject.SetActive(active);
            if (_nicknameInput != null) _nicknameInput.gameObject.SetActive(active);
            if (_addressInput != null) _addressInput.gameObject.SetActive(active && _mode == ConnectionMode.Client);
            if (_portInput != null) _portInput.gameObject.SetActive(active);
            if (_statusText != null) _statusText.gameObject.SetActive(active);
        }

        private void SetStatus(string message, bool error)
        {
            if (_statusText == null) return;

            _statusText.text = message;
            _statusText.color = error ? new Color(1f, 0.35f, 0.25f) : Color.white;
        }

        private void CacheFont()
        {
            TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI text in texts)
            {
                if (text != null && text.font != null)
                {
                    _fontAsset = text.font;
                    return;
                }
            }
        }

        private void CacheButtonPositions()
        {
            _playStartPosition = GetButtonPosition(_playButton);
            _clientStartPosition = GetButtonPosition(_leaderboardButton);
            _settingsStartPosition = GetButtonPosition(_settingsButton);
            _exitStartPosition = GetButtonPosition(_exitButton);
        }

        private void RestoreButtonPositions()
        {
            MoveButton(_playButton, _playStartPosition);
            MoveButton(_leaderboardButton, _clientStartPosition);
            MoveButton(_settingsButton, _settingsStartPosition);
            MoveButton(_exitButton, _exitStartPosition);
        }

        private static Vector2 GetButtonPosition(Button button)
        {
            return button != null && button.transform is RectTransform rect ? rect.anchoredPosition : Vector2.zero;
        }

        private static void MoveButton(Button button, Vector2 position)
        {
            if (button != null && button.transform is RectTransform rect)
                rect.anchoredPosition = position;
        }

        private static void SetButtonActive(Button button, bool active)
        {
            if (button != null)
                button.gameObject.SetActive(active);
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;

            TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
                text.text = label;
        }

        private static void ClearButtonListeners(Button button)
        {
            if (button != null)
                button.onClick.RemoveAllListeners();
        }

        private static string NormalizeAddress(string address)
        {
            return string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
        }

        private static bool TryParsePort(string text, out ushort port, out string error)
        {
            port = 0;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = $"Port is required. Enter a number from 1 to {ushort.MaxValue}.";
                return false;
            }

            if (!int.TryParse(text.Trim(), out int parsed))
            {
                error = $"Port '{text.Trim()}' is not numeric. Enter a number from 1 to {ushort.MaxValue}.";
                return false;
            }

            if (parsed <= 0 || parsed > ushort.MaxValue)
            {
                error = $"Port {parsed} is out of range. Enter a number from 1 to {ushort.MaxValue}.";
                return false;
            }

            port = (ushort)parsed;
            return true;
        }

        private void EnsureMultiplayerStatusPanel()
        {
            if (_multiplayerStatusRoot != null)
                return;

            GameObject root = new("MultiplayerStatusPanel");
            root.transform.SetParent(transform, false);
            _multiplayerStatusRoot = root.AddComponent<RectTransform>();
            _multiplayerStatusRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _multiplayerStatusRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _multiplayerStatusRoot.pivot = new Vector2(0.5f, 0.5f);
            _multiplayerStatusRoot.anchoredPosition = new Vector2(0f, -340f);
            _multiplayerStatusRoot.sizeDelta = new Vector2(620f, 280f);

            Image background = root.AddComponent<Image>();
            background.color = new Color(0f, 0.11f, 0.19f, 0.72f);

            CreatePanelLabel(_multiplayerStatusRoot, "MultiplayerStatusTitle", "SESSION SNAPSHOT", new Vector2(14f, 10f), new Vector2(592f, 26f), 22, TextAlignmentOptions.TopLeft, new Color(0.73f, 0.91f, 1f, 1f));
            _sessionSummaryText = CreatePanelLabel(_multiplayerStatusRoot, "SessionSummary", string.Empty, new Vector2(14f, 44f), new Vector2(592f, 42f), 19, TextAlignmentOptions.TopLeft, Color.white);
            _rosterText = CreatePanelLabel(_multiplayerStatusRoot, "SessionRoster", string.Empty, new Vector2(14f, 92f), new Vector2(592f, 118f), 18, TextAlignmentOptions.TopLeft, Color.white);
            _resultsText = CreatePanelLabel(_multiplayerStatusRoot, "SessionResults", string.Empty, new Vector2(14f, 194f), new Vector2(592f, 62f), 17, TextAlignmentOptions.TopLeft, new Color(0.85f, 0.93f, 1f, 1f));
            _connectionHintText = CreatePanelLabel(_multiplayerStatusRoot, "ConnectionHint", string.Empty, new Vector2(14f, 236f), new Vector2(592f, 32f), 16, TextAlignmentOptions.TopLeft, new Color(0.84f, 0.88f, 0.94f, 1f));
        }

        private TextMeshProUGUI CreatePanelLabel(RectTransform parent, string objectName, string text, Vector2 topLeft, Vector2 size, int fontSize, TextAlignmentOptions alignment, Color color)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            if (_fontAsset != null)
                label.font = _fontAsset;
            return label;
        }

        private void RefreshMultiplayerSummary()
        {
            EnsureMultiplayerStatusPanel();
            if (_sessionSummaryText == null)
                return;

            NetworkSessionController session = NetworkSessionController.Instance;
            List<NetworkPlayerData> players = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None)
                .Where(player => player != null)
                .OrderBy(player => player.ClientId)
                .ToList();

            string nickname = _nicknameInput != null && !string.IsNullOrWhiteSpace(_nicknameInput.text)
                ? _nicknameInput.text.Trim()
                : NetworkPlayerPreferences.GetNickname();

            _sessionSummaryText.text = session != null
                ? BuildSessionSummary(session, players.Count, nickname)
                : BuildOfflineSummary(nickname);

            _rosterText.text = BuildRoster(players);
            _resultsText.text = BuildResultsPreview(players, session);
            _connectionHintText.text = BuildConnectionHint(session, players.Count);
        }

        private string BuildSessionSummary(NetworkSessionController session, int playerCount, string nickname)
        {
            string phase = session.Phase.Value switch
            {
                SessionPhase.Lobby => "Lobby",
                SessionPhase.Countdown => $"Countdown {session.CountdownRemaining.Value:0.0}s",
                SessionPhase.Race => "Race",
                SessionPhase.Results => "Results",
                SessionPhase.Disconnected => "Disconnected",
                _ => session.Phase.Value.ToString()
            };

            string connectionStatus = _connectionService != null ? _connectionService.Status.ToString() : "Network unavailable";
            return $"Pilot {nickname} | {connectionStatus} | {phase} | Players {playerCount}/{session.ConnectedPlayers.Value} | Ready {session.ReadyPlayers.Value}/{session.ConnectedPlayers.Value}";
        }

        private string BuildOfflineSummary(string nickname)
        {
            string failure = string.IsNullOrWhiteSpace(_lastConnectionFailure) ? string.Empty : $" | Last error: {_lastConnectionFailure}";
            return $"Pilot {nickname} | {_connectionService?.Status.ToString() ?? "Offline"}{failure}";
        }

        private static string BuildRoster(IReadOnlyList<NetworkPlayerData> players)
        {
            if (players.Count == 0)
                return "Players: none yet. When the session is running, connected pilots will appear here with ready state and compact stats.";

            List<string> lines = new() { "Players:" };
            foreach (NetworkPlayerData player in players)
            {
                string owner = player.IsOwner ? " (you)" : string.Empty;
                string ready = player.IsReady.Value ? "READY" : "WAITING";
                string finish = player.IsFinished.Value ? $" | Finish {FormatTime(player.FinishTime.Value)}" : string.Empty;
                lines.Add($"• {player.Nickname.Value}{owner} — {ready} — HP {player.HP.Value} — Score {player.Score.Value} — CP {player.CheckpointIndex.Value}{finish}");
            }

            return string.Join("\n", lines);
        }

        private static string BuildResultsPreview(IReadOnlyList<NetworkPlayerData> players, NetworkSessionController session)
        {
            if (session == null)
                return "Results: connect to a session to see synchronized finish order and restart state.";

            if (session.Phase.Value != SessionPhase.Results)
                return session.Phase.Value == SessionPhase.Race
                    ? "Results: race is still active. Finish order will appear here once the server ends the session."
                    : "Results: waiting for the server to enter the results phase.";

            if (players.Count == 0)
                return "Results: no synchronized players available yet.";

            IEnumerable<NetworkPlayerData> ordered = players
                .OrderByDescending(player => player.IsFinished.Value)
                .ThenBy(player => player.IsFinished.Value ? player.FinishTime.Value : float.MaxValue)
                .ThenByDescending(player => player.Score.Value)
                .ThenByDescending(player => player.CheckpointIndex.Value)
                .ThenBy(player => player.ClientId);

            List<string> placements = new();
            int index = 1;
            foreach (NetworkPlayerData player in ordered)
            {
                string finish = player.IsFinished.Value ? FormatTime(player.FinishTime.Value) : "DNF";
                placements.Add($"{index}. {player.Nickname.Value} — {finish} — Score {player.Score.Value}");
                index++;
            }

            return string.Join("\n", placements);
        }

        private string BuildConnectionHint(NetworkSessionController session, int playerCount)
        {
            if (_connectionService == null)
                return "Network service is not available yet. Connection status will appear here once Zenject finishes wiring the menu.";

            NetworkConnectionStatus status = _connectionService.Status;
            return status switch
            {
                NetworkConnectionStatus.Failed => string.IsNullOrWhiteSpace(_lastConnectionFailure)
                    ? "Connection failed. Check address, port, and FishNet Tugboat setup."
                    : _lastConnectionFailure,
                NetworkConnectionStatus.HostStarted when session != null && session.Phase.Value == SessionPhase.Lobby && playerCount < 2
                    => "Host is live. A second player joining should appear here with nickname and ready state.",
                NetworkConnectionStatus.ClientStarted when session != null && playerCount == 0
                    => "Connected. Waiting for the server scene and local player spawn to finish syncing.",
                NetworkConnectionStatus.Offline => _mode == ConnectionMode.Client
                    ? "Client join errors will appear above and in this session snapshot."
                    : "Use Host or Client to start a session with the saved nickname and port.",
                _ => "This panel mirrors synchronized lobby, race, and results state without relying on the debug overlay."
            };
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
