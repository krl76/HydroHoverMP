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

        private void OnConnectionStatusChanged(NetworkConnectionStatus status) => RefreshConnectionUi();
        private void OnClientCountChanged(int count) => RefreshConnectionUi();

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
    }
}
