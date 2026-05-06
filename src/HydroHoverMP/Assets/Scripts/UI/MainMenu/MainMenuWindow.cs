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

        [Header("Existing menu buttons")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _leaderboardButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _exitButton;

        [Header("Network launch panel")]
        [SerializeField] private RectTransform _networkPanelRoot;
        [SerializeField] private TMP_InputField _nicknameInput;
        [SerializeField] private TMP_InputField _addressInput;
        [SerializeField] private Button _serverButton;
        [SerializeField] private Button _stopButton;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private TextMeshProUGUI _hintText;

        private IWindowService _windowService;
        private INetworkConnectionService _connectionService;
        private TMP_FontAsset _fontAsset;
        private float _nextRefreshTime;

        [Inject]
        public void Construct(
            IWindowService windowService,
            INetworkConnectionService connectionService)
        {
            _windowService = windowService;
            _connectionService = connectionService;
        }

        private void Start()
        {
            CacheFont();
            EnsureNetworkPanel();
            ConfigureExistingButtons();
            SubscribeConnectionEvents();
            LoadSavedNetworkPreferences();
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
            if (_connectionService != null)
            {
                _connectionService.OnStatusChanged -= OnConnectionStatusChanged;
                _connectionService.OnClientCountChanged -= OnClientCountChanged;
                _connectionService.OnConnectionFailed -= OnConnectionFailed;
            }
        }

        private void ConfigureExistingButtons()
        {
            SetButtonLabel(_playButton, "Host");
            SetButtonLabel(_leaderboardButton, "Client");
            SetButtonLabel(_settingsButton, "Settings");
            SetButtonLabel(_exitButton, "Exit");

            if (_playButton != null)
                _playButton.onClick.AddListener(StartHostFromMenu);

            if (_leaderboardButton != null)
                _leaderboardButton.onClick.AddListener(StartClientFromMenu);

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
        }

        private void StartHostFromMenu()
        {
            if (_connectionService == null)
            {
                SetStatus("Network service is not ready. Check ProjectContext/GlobalInstaller.", true);
                return;
            }

            SaveNetworkPreferences();
            SetStatus("Starting Host... FishNet will load Gameplay when the server scene is ready.", false);

            if (!_connectionService.StartHost(DefaultPort))
                RefreshConnectionUi();
        }

        private void StartClientFromMenu()
        {
            if (_connectionService == null)
            {
                SetStatus("Network service is not ready. Check ProjectContext/GlobalInstaller.", true);
                return;
            }

            SaveNetworkPreferences();
            string address = _addressInput != null ? _addressInput.text : NetworkPlayerPreferences.GetAddress();
            SetStatus($"Connecting to {NormalizeAddress(address)}:{DefaultPort}...", false);

            if (!_connectionService.StartClient(address, DefaultPort))
                RefreshConnectionUi();
        }

        private void StartServerFromMenu()
        {
            if (_connectionService == null)
            {
                SetStatus("Network service is not ready. Check ProjectContext/GlobalInstaller.", true);
                return;
            }

            SaveNetworkPreferences();
            SetStatus($"Starting dedicated-style server on port {DefaultPort}...", false);

            if (!_connectionService.StartServer(DefaultPort))
                RefreshConnectionUi();
        }

        private void StopConnectionFromMenu()
        {
            _connectionService?.StopConnection();
            RefreshConnectionUi();
        }

        private void SaveNetworkPreferences()
        {
            if (_nicknameInput != null)
                NetworkPlayerPreferences.SetNickname(_nicknameInput.text);

            if (_addressInput != null)
                NetworkPlayerPreferences.SetAddress(_addressInput.text);
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
            SetStatus(message, true);
            RefreshConnectionUi();
        }

        private void RefreshConnectionUi()
        {
            if (_connectionService == null)
            {
                SetStatus("NetworkConnectionService is unavailable.", true);
                SetConnectionButtonsInteractable(false);
                return;
            }

            NetworkConnectionStatus status = _connectionService.Status;
            bool canStart = status is NetworkConnectionStatus.Offline or NetworkConnectionStatus.Failed;
            bool canStop = status != NetworkConnectionStatus.Offline;

            SetConnectionButtonsInteractable(canStart);

            if (_serverButton != null)
                _serverButton.interactable = canStart;

            if (_stopButton != null)
                _stopButton.interactable = canStop;

            string address = _addressInput != null ? NormalizeAddress(_addressInput.text) : NetworkPlayerPreferences.GetAddress();
            string statusLine = status switch
            {
                NetworkConnectionStatus.Offline => "Offline. Choose Host or Client to start a multiplayer session.",
                NetworkConnectionStatus.StartingHost => "Starting Host... loading shared Gameplay scene.",
                NetworkConnectionStatus.StartingClient => $"Connecting to {address}:{DefaultPort}...",
                NetworkConnectionStatus.StartingServer => $"Starting server on port {DefaultPort}...",
                NetworkConnectionStatus.HostStarted => $"Host started. Clients on server: {_connectionService.ConnectedClientCount}.",
                NetworkConnectionStatus.ClientStarted => "Client connected. Waiting for server scene/lobby.",
                NetworkConnectionStatus.ServerStarted => $"Server started on port {DefaultPort}. Clients: {_connectionService.ConnectedClientCount}.",
                NetworkConnectionStatus.Stopping => "Stopping network connection...",
                NetworkConnectionStatus.Failed => "Connection failed. Check address, port and FishNet setup.",
                _ => status.ToString()
            };

            SetStatus(statusLine, status == NetworkConnectionStatus.Failed);

            if (_hintText != null)
            {
                _hintText.text =
                    "MainMenu is the primary launch path.\n" +
                    "Host creates a lobby; Client joins by address.\n" +
                    "Nickname is applied to your spawned hovercraft in the network HUD.";
            }
        }

        private void SetConnectionButtonsInteractable(bool interactable)
        {
            if (_playButton != null)
                _playButton.interactable = interactable;

            if (_leaderboardButton != null)
                _leaderboardButton.interactable = interactable;
        }

        private void SetStatus(string message, bool error)
        {
            if (_statusText == null) return;

            _statusText.text = message;
            _statusText.color = error ? new Color(1f, 0.35f, 0.25f) : Color.white;
        }

        private void Settings()
        {
            _windowService.Open(WindowID.Settings);
            _windowService.Close(WindowID.MainMenu);
        }

        private void EnsureNetworkPanel()
        {
            if (_networkPanelRoot == null)
            {
                Transform existing = transform.Find("NetworkConnectionPanel");
                _networkPanelRoot = existing != null
                    ? existing as RectTransform
                    : CreatePanelRoot();
            }

            _nicknameInput ??= FindDirectChildComponent<TMP_InputField>(_networkPanelRoot, "NicknameInput");
            _addressInput ??= FindDirectChildComponent<TMP_InputField>(_networkPanelRoot, "AddressInput");
            _serverButton ??= FindDirectChildComponent<Button>(_networkPanelRoot, "ServerButton");
            _stopButton ??= FindDirectChildComponent<Button>(_networkPanelRoot, "StopButton");
            _statusText ??= FindDirectChildComponent<TextMeshProUGUI>(_networkPanelRoot, "StatusText");
            _hintText ??= FindDirectChildComponent<TextMeshProUGUI>(_networkPanelRoot, "HintText");

            VerticalLayoutGroup layout = _networkPanelRoot.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = _networkPanelRoot.gameObject.AddComponent<VerticalLayoutGroup>();

            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            if (_networkPanelRoot.GetComponent<Image>() == null)
            {
                Image image = _networkPanelRoot.gameObject.AddComponent<Image>();
                image.color = new Color(0.02f, 0.09f, 0.14f, 0.78f);
            }

            EnsureGeneratedPanelContent();
        }

        private void EnsureGeneratedPanelContent()
        {
            if (_networkPanelRoot == null) return;

            if (_networkPanelRoot.Find("NetworkHeader") == null)
                CreateText(_networkPanelRoot, "NetworkHeader", "MULTIPLAYER LAUNCH", 28, TextAlignmentOptions.Center);

            if (_nicknameInput == null)
                _nicknameInput = CreateInputRow(_networkPanelRoot, "NicknameRow", "Nickname", "Pilot", "NicknameInput");

            if (_addressInput == null)
                _addressInput = CreateInputRow(_networkPanelRoot, "AddressRow", "Address", "localhost", "AddressInput");

            if (_statusText == null)
                _statusText = CreateText(_networkPanelRoot, "StatusText", "Offline", 20, TextAlignmentOptions.Left);

            if (_hintText == null)
                _hintText = CreateText(_networkPanelRoot, "HintText", string.Empty, 18, TextAlignmentOptions.Left);

            if (_serverButton == null || _stopButton == null)
            {
                Transform buttonRow = _networkPanelRoot.Find("NetworkButtonsRow");
                if (buttonRow == null)
                    buttonRow = CreateHorizontalRow(_networkPanelRoot, "NetworkButtonsRow", 48f);

                _serverButton ??= CreateButton(buttonRow, "ServerButton", "Server");
                _stopButton ??= CreateButton(buttonRow, "StopButton", "Stop");
            }

            _serverButton.onClick.RemoveListener(StartServerFromMenu);
            _serverButton.onClick.AddListener(StartServerFromMenu);

            _stopButton.onClick.RemoveListener(StopConnectionFromMenu);
            _stopButton.onClick.AddListener(StopConnectionFromMenu);
        }

        private RectTransform CreatePanelRoot()
        {
            GameObject panelObject = new("NetworkConnectionPanel");
            panelObject.transform.SetParent(transform, false);

            RectTransform rect = panelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(360f, -40f);
            rect.sizeDelta = new Vector2(560f, 430f);
            return rect;
        }

        private TMP_InputField CreateInputRow(Transform parent, string rowName, string label, string defaultValue, string inputName)
        {
            Transform row = CreateHorizontalRow(parent, rowName, 54f);
            CreateText(row, $"{rowName}_Label", label, 20, TextAlignmentOptions.Left, new Vector2(150f, 44f));
            return CreateInput(row, inputName, defaultValue);
        }

        private Transform CreateHorizontalRow(Transform parent, string name, float preferredHeight)
        {
            GameObject rowObject = new(name);
            rowObject.transform.SetParent(parent, false);

            RectTransform rect = rowObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, preferredHeight);

            HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            LayoutElement layoutElement = rowObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight;

            return rowObject.transform;
        }

        private TMP_InputField CreateInput(Transform parent, string name, string defaultValue)
        {
            GameObject root = new(name);
            root.transform.SetParent(parent, false);

            RectTransform rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(320f, 44f);

            Image background = root.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.92f);

            TMP_InputField input = root.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.text = defaultValue;
            input.lineType = TMP_InputField.LineType.SingleLine;

            GameObject textArea = new("Text Area");
            textArea.transform.SetParent(root.transform, false);
            RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(12f, 6f);
            textAreaRect.offsetMax = new Vector2(-12f, -6f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = CreateText(textArea.transform, "Placeholder", defaultValue, 20, TextAlignmentOptions.Left);
            placeholder.color = new Color(0f, 0.17f, 0.29f, 0.45f);

            TextMeshProUGUI text = CreateText(textArea.transform, "Text", string.Empty, 20, TextAlignmentOptions.Left);
            text.color = new Color(0f, 0.17f, 0.29f, 1f);

            input.textViewport = textAreaRect;
            input.placeholder = placeholder;
            input.textComponent = text;

            LayoutElement layoutElement = root.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 44f;
            layoutElement.flexibleWidth = 1f;

            return input;
        }

        private Button CreateButton(Transform parent, string name, string label)
        {
            GameObject root = new(name);
            root.transform.SetParent(parent, false);

            RectTransform rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(180f, 46f);

            Image image = root.AddComponent<Image>();
            image.color = new Color(0.4f, 0.9f, 1f, 0.92f);

            Button button = root.AddComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI text = CreateText(root.transform, "Label", label, 22, TextAlignmentOptions.Center);
            text.color = new Color(0f, 0.17f, 0.29f, 1f);

            LayoutElement layoutElement = root.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 46f;
            layoutElement.flexibleWidth = 1f;

            return button;
        }

        private TextMeshProUGUI CreateText(
            Transform parent,
            string name,
            string text,
            int fontSize,
            TextAlignmentOptions alignment,
            Vector2? preferredSize = null)
        {
            GameObject textObject = new(name);
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            label.color = Color.white;
            if (_fontAsset != null)
                label.font = _fontAsset;

            LayoutElement layoutElement = textObject.AddComponent<LayoutElement>();
            if (preferredSize.HasValue)
            {
                layoutElement.preferredWidth = preferredSize.Value.x;
                layoutElement.preferredHeight = preferredSize.Value.y;
            }
            else
            {
                layoutElement.preferredHeight = fontSize + 14f;
            }

            return label;
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

        private static T FindDirectChildComponent<T>(Transform root, string childName) where T : Component
        {
            if (root == null) return null;

            Transform direct = root.Find(childName);
            if (direct != null && direct.TryGetComponent(out T directComponent))
                return directComponent;

            T[] components = root.GetComponentsInChildren<T>(true);
            foreach (T component in components)
            {
                if (component != null && component.name == childName)
                    return component;
            }

            return null;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;

            TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
                text.text = label;
        }

        private static string NormalizeAddress(string address)
        {
            return string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
        }
    }
}
