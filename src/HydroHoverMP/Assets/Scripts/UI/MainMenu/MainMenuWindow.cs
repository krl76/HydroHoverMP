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
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private TextMeshProUGUI _modeTitleText;

        [Header("Network defaults")]
        [SerializeField] private string _defaultAddress = "localhost";
        [SerializeField] private int _defaultPort = DefaultPort;
        [SerializeField] private bool _preferSavedConnectionPreferences;

        private IWindowService _windowService;
        private INetworkConnectionService _connectionService;
        private ConnectionMode _mode;
        private Vector2 _playStartPosition;
        private Vector2 _clientStartPosition;
        private Vector2 _settingsStartPosition;
        private Vector2 _exitStartPosition;
        private float _nextRefreshTime;
        private string _lastConnectionFailure;

        [Inject]
        public void Construct(IWindowService windowService, INetworkConnectionService connectionService)
        {
            _windowService = windowService;
            _connectionService = connectionService;
        }

        private void Start()
        {
            CacheButtonPositions();
            ConfigureButtonsForRootMenu();
            SubscribeConnectionEvents();
            LoadSavedNetworkPreferences();
            ShowRootMenu();
            RefreshConnectionUi();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
                return;

            _nextRefreshTime = Time.unscaledTime + 0.5f;
            _connectionService?.RefreshStatus();
            RefreshConnectionUi();
        }

        private void OnDestroy()
        {
            if (_connectionService == null)
                return;

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
        }

        private void ShowConnectionForm(ConnectionMode mode)
        {
            _mode = mode;
            ConfigureButtonsForForm(mode);
            SetFormActive(true);

            SetButtonLabel(_playButton, mode == ConnectionMode.Host ? "Start" : "Connect");
            SetButtonLabel(_leaderboardButton, "Back");
            SetButtonActive(_settingsButton, false);
            SetButtonActive(_exitButton, false);

            MoveButton(_playButton, new Vector2(0f, -42f));
            MoveButton(_leaderboardButton, new Vector2(0f, -126f));

            if (_modeTitleText != null)
                _modeTitleText.text = mode == ConnectionMode.Host ? "HOST SESSION" : "JOIN SESSION";

            RefreshConnectionUi();
        }

        private void SubscribeConnectionEvents()
        {
            if (_connectionService == null)
                return;

            _connectionService.OnStatusChanged += OnConnectionStatusChanged;
            _connectionService.OnClientCountChanged += OnClientCountChanged;
            _connectionService.OnConnectionFailed += OnConnectionFailed;
        }

        private void LoadSavedNetworkPreferences()
        {
            if (_nicknameInput != null)
                _nicknameInput.text = NetworkPlayerPreferences.GetNickname();
        }

        private void StartHostFromMenu()
        {
            if (!TryPrepareConnection())
                return;

            if (!TryGetConnectionPort(out ushort port))
                return;

            _lastConnectionFailure = null;
            SetStatus("Starting Host...", false);
            if (!_connectionService.StartHost(port))
                RefreshConnectionUi();
        }

        private void StartClientFromMenu()
        {
            if (!TryPrepareConnection())
                return;

            if (!TryGetConnectionPort(out ushort port))
                return;

            string address = ResolveConnectionAddress();
            _lastConnectionFailure = null;
            SetStatus("Connecting to configured host...", false);
            if (!_connectionService.StartClient(address, port))
                RefreshConnectionUi();
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
        }

        private bool TryGetConnectionPort(out ushort port)
        {
            int configuredPort = _preferSavedConnectionPreferences ? NetworkPlayerPreferences.GetPort() : _defaultPort;
            if (configuredPort is > 0 and <= ushort.MaxValue)
            {
                port = (ushort)configuredPort;
                return true;
            }

            port = 0;
            SetStatus($"Configured port is invalid. Set a number from 1 to {ushort.MaxValue} in the MainMenu inspector.", true);
            return false;
        }

        private string ResolveConnectionAddress()
        {
            return _preferSavedConnectionPreferences
                ? NormalizeAddress(NetworkPlayerPreferences.GetAddress())
                : NormalizeAddress(_defaultAddress);
        }

        private void OnConnectionStatusChanged(NetworkConnectionStatus status)
        {
            RefreshConnectionUi();
        }

        private void OnClientCountChanged(int count)
        {
            RefreshConnectionUi();
        }

        private void OnConnectionFailed(string message)
        {
            _lastConnectionFailure = message;
            RefreshConnectionUi();
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

            string statusLine = status switch
            {
                NetworkConnectionStatus.Offline => _mode == ConnectionMode.Host
                    ? "Enter nickname, then Start Host."
                    : "Enter nickname, then Connect.",
                NetworkConnectionStatus.StartingHost => "Starting Host... loading shared Gameplay scene.",
                NetworkConnectionStatus.StartingClient => "Connecting to configured host...",
                NetworkConnectionStatus.StartingServer => "Starting server-only mode...",
                NetworkConnectionStatus.HostStarted => $"Host lobby open. Clients: {_connectionService.ConnectedClientCount}.",
                NetworkConnectionStatus.ClientStarted => "Client connected. Waiting in lobby.",
                NetworkConnectionStatus.ServerStarted => $"Server-only mode running. Clients: {_connectionService.ConnectedClientCount}.",
                NetworkConnectionStatus.Failed => string.IsNullOrWhiteSpace(_lastConnectionFailure)
                    ? "Connection failed. Check address, port and FishNet setup."
                    : _lastConnectionFailure,
                _ => status.ToString()
            };

            SetStatus(BuildStatusMessage(statusLine), status == NetworkConnectionStatus.Failed);
        }

        private string BuildStatusMessage(string baseStatus)
        {
            NetworkSessionController session = NetworkSessionController.Instance;
            if (session == null)
                return baseStatus;

            string phase = session.Phase.Value switch
            {
                SessionPhase.Lobby => $"Lobby {session.ReadyPlayers.Value}/{session.ConnectedPlayers.Value} ready",
                SessionPhase.Countdown => $"Countdown {session.CountdownRemaining.Value:0.0}s",
                SessionPhase.Race => $"Race live | Players {session.ConnectedPlayers.Value}",
                SessionPhase.Results => "Results ready",
                SessionPhase.Disconnected => "Disconnected",
                _ => session.Phase.Value.ToString()
            };

            return $"{baseStatus}\n{phase}";
        }

        private void Settings()
        {
            _windowService.Open(WindowID.Settings);
            _windowService.Close(WindowID.MainMenu);
        }

        private void SetFormActive(bool active)
        {
            if (_modeTitleText != null) _modeTitleText.gameObject.SetActive(active);
            if (_nicknameInput != null) _nicknameInput.gameObject.SetActive(active);
            if (_statusText != null) _statusText.gameObject.SetActive(active);
        }

        private void SetStatus(string message, bool error)
        {
            if (_statusText == null)
                return;

            _statusText.text = message;
            _statusText.color = error ? new Color(1f, 0.35f, 0.25f) : Color.white;
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
            if (button == null)
                return;

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

    }
}
