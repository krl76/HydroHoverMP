using System;
using System.Collections;
using System.Linq;
using Core.States.Base;
using Core.States.MainMenu;
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

        [Header("Network Session UI")]
        [SerializeField] private RectTransform _networkPanelRoot;
        [SerializeField] private TextMeshProUGUI _networkSessionText;
        [SerializeField] private TextMeshProUGUI _networkPlayersText;
        [SerializeField] private TMP_InputField _networkNicknameInput;
        [SerializeField] private Button _applyNicknameButton;
        [SerializeField] private Button _readyButton;
        [SerializeField] private Button _forceStartButton;
        [SerializeField] private Button _restartLobbyButton;
        [SerializeField] private Button _exitToMenuButton;

        private IPlayerService _playerService;
        private IRaceManagerService _raceManagerService;
        private INetworkConnectionService _connectionService;
        private GameStateMachine _stateMachine;
        private HoverController _hoverController;
        private NetworkPlayerData _networkPlayerData;
        
        private int _lastSpeed = -1;
        private int _lastCheckpointIndex = -1;
        private int _lastFps = -1;
        private float _nextNetworkUiRefreshTime;
        private bool _nicknameAppliedToSpawnedPlayer;

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
            EnsureNetworkSessionPanel();
            WireNetworkSessionButtons();
            StartCoroutine(UpdateGameMetrics());
            
            _raceManagerService.OnRaceStarted += UpdateRaceInfoUI;
            _raceManagerService.OnCheckpointPassed += OnCheckpointPassedHandler;
            
            if (_raceManagerService.IsRaceActive)
            {
                UpdateRaceInfoUI();
            }
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
            UpdateNetworkSessionPanel();
        }

        private void UpdatePhysicsUI()
        {
            var rb = _hoverController.Rb;
            
            float rawSpeed = rb.linearVelocity.magnitude * 3.6f;
            int displaySpeed = Mathf.RoundToInt(rawSpeed);
            
            if (displaySpeed != _lastSpeed)
            {
                _speedText.text = $"{displaySpeed} km/h";
                _lastSpeed = displaySpeed;
            }
            
            float t = Mathf.InverseLerp(_minSpeed, _maxSpeed, rawSpeed);
            float angle = Mathf.Lerp(_minAngle, _maxAngle, t);
            _speedNeedle.localRotation = Quaternion.Euler(0, 0, angle);
            
            var lift = _hoverController.LiftEngine;
            var thrust = _hoverController.ThrustEngine;

            if (_liftBar) _liftBar.fillAmount = lift.CurrentRPM / lift.MaxRPM;
            if (_thrustBar) _thrustBar.fillAmount = thrust.CurrentRPM / thrust.MaxRPM;
        }

        private void UpdateRaceInfoUI()
        {
            float currentTime = _raceManagerService.CurrentTime;

            int minutes = (int)(currentTime / 60);
            int seconds = (int)(currentTime % 60);
            int milliseconds = (int)((currentTime * 100) % 100);
            
            _timerText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
            
            int currentCp = _raceManagerService.CurrentCheckpointIndex;
            if (currentCp != _lastCheckpointIndex)
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

            _timerText.text = NetworkSessionController.Instance != null
                ? NetworkSessionController.Instance.Phase.Value.ToString()
                : "Network";

            _checkpointText.text =
                $"CP {_networkPlayerData.CheckpointIndex.Value} / HP {_networkPlayerData.HP.Value} / Score {_networkPlayerData.Score.Value}";
        }


        private void EnsureNetworkSessionPanel()
        {
            if (_networkPanelRoot == null)
            {
                Transform existing = transform.Find("NetworkSessionPanel");
                _networkPanelRoot = existing != null
                    ? existing as RectTransform
                    : CreateNetworkPanelRoot();
            }

            _networkSessionText ??= FindNetworkPanelComponent<TextMeshProUGUI>("NetworkSessionText");
            _networkPlayersText ??= FindNetworkPanelComponent<TextMeshProUGUI>("NetworkPlayersText");
            _networkNicknameInput ??= FindNetworkPanelComponent<TMP_InputField>("NetworkNicknameInput");
            _applyNicknameButton ??= FindNetworkPanelComponent<Button>("ApplyNicknameButton");
            _readyButton ??= FindNetworkPanelComponent<Button>("ReadyButton");
            _forceStartButton ??= FindNetworkPanelComponent<Button>("ForceStartButton");
            _restartLobbyButton ??= FindNetworkPanelComponent<Button>("RestartLobbyButton");
            _exitToMenuButton ??= FindNetworkPanelComponent<Button>("ExitToMenuButton");

            VerticalLayoutGroup layout = _networkPanelRoot.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = _networkPanelRoot.gameObject.AddComponent<VerticalLayoutGroup>();

            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            if (_networkPanelRoot.GetComponent<Image>() == null)
            {
                Image image = _networkPanelRoot.gameObject.AddComponent<Image>();
                image.color = new Color(0.02f, 0.09f, 0.14f, 0.72f);
            }

            if (_networkPanelRoot.Find("NetworkTitle") == null)
                CreateNetworkText(_networkPanelRoot, "NetworkTitle", "SESSION", 22, TextAlignmentOptions.Center);

            _networkSessionText ??= CreateNetworkText(_networkPanelRoot, "NetworkSessionText", "Offline", 18, TextAlignmentOptions.Left);
            _networkPlayersText ??= CreateNetworkText(_networkPanelRoot, "NetworkPlayersText", "No players", 16, TextAlignmentOptions.Left, 118f);

            if (_networkNicknameInput == null)
                _networkNicknameInput = CreateNetworkInputRow(_networkPanelRoot, "NicknameRow", "Nickname", NetworkPlayerPreferences.GetNickname(), "NetworkNicknameInput");

            if (_applyNicknameButton == null || _readyButton == null)
            {
                Transform row = _networkPanelRoot.Find("PlayerActionRow") ?? CreateNetworkButtonRow(_networkPanelRoot, "PlayerActionRow");
                _applyNicknameButton ??= CreateNetworkButton(row, "ApplyNicknameButton", "Apply Name");
                _readyButton ??= CreateNetworkButton(row, "ReadyButton", "Ready");
            }

            if (_forceStartButton == null || _restartLobbyButton == null || _exitToMenuButton == null)
            {
                Transform row = _networkPanelRoot.Find("SessionActionRow") ?? CreateNetworkButtonRow(_networkPanelRoot, "SessionActionRow");
                _forceStartButton ??= CreateNetworkButton(row, "ForceStartButton", "Force Start");
                _restartLobbyButton ??= CreateNetworkButton(row, "RestartLobbyButton", "Restart Lobby");
                _exitToMenuButton ??= CreateNetworkButton(row, "ExitToMenuButton", "Exit");
            }
        }

        private void WireNetworkSessionButtons()
        {
            if (_applyNicknameButton != null)
            {
                _applyNicknameButton.onClick.RemoveListener(ApplyNicknameToLocalPlayer);
                _applyNicknameButton.onClick.AddListener(ApplyNicknameToLocalPlayer);
            }

            if (_readyButton != null)
            {
                _readyButton.onClick.RemoveListener(ToggleReadyForLocalPlayer);
                _readyButton.onClick.AddListener(ToggleReadyForLocalPlayer);
            }

            if (_forceStartButton != null)
            {
                _forceStartButton.onClick.RemoveListener(ForceStartSession);
                _forceStartButton.onClick.AddListener(ForceStartSession);
            }

            if (_restartLobbyButton != null)
            {
                _restartLobbyButton.onClick.RemoveListener(RestartNetworkLobby);
                _restartLobbyButton.onClick.AddListener(RestartNetworkLobby);
            }

            if (_exitToMenuButton != null)
            {
                _exitToMenuButton.onClick.RemoveListener(ExitNetworkSessionToMenu);
                _exitToMenuButton.onClick.AddListener(ExitNetworkSessionToMenu);
            }
        }

        private void UpdateNetworkSessionPanel()
        {
            if (Time.unscaledTime < _nextNetworkUiRefreshTime) return;
            _nextNetworkUiRefreshTime = Time.unscaledTime + 0.25f;

            NetworkSessionController session = NetworkSessionController.Instance;
            NetworkPlayerData localPlayer = GetLocalNetworkPlayer();
            bool networkActive = session != null || localPlayer != null || (_connectionService != null && (_connectionService.IsClient || _connectionService.IsServer));

            if (_networkPanelRoot != null && _networkPanelRoot.gameObject.activeSelf != networkActive)
                _networkPanelRoot.gameObject.SetActive(networkActive);

            if (!networkActive) return;

            if (localPlayer != null && !_nicknameAppliedToSpawnedPlayer)
            {
                ApplyNicknameToLocalPlayer();
                _nicknameAppliedToSpawnedPlayer = true;
            }

            RefreshNetworkSessionText(session);
            RefreshNetworkPlayersText();
            RefreshNetworkButtons(session, localPlayer);
        }

        private void RefreshNetworkSessionText(NetworkSessionController session)
        {
            if (_networkSessionText == null) return;

            if (session == null)
            {
                _networkSessionText.text = $"Connection: {_connectionService?.Status.ToString() ?? "Unknown"}\nWaiting for shared Gameplay scene...";
                return;
            }

            string countdown = session.Phase.Value == SessionPhase.Countdown
                ? $"\nCountdown: {session.CountdownRemaining.Value:0.0}s"
                : string.Empty;

            _networkSessionText.text =
                $"Phase: {session.Phase.Value}\n" +
                $"Ready: {session.ReadyPlayers.Value}/{session.ConnectedPlayers.Value}{countdown}";
        }

        private void RefreshNetworkPlayersText()
        {
            if (_networkPlayersText == null) return;

            NetworkPlayerData[] players = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None)
                .OrderByDescending(p => p.Score.Value)
                .ThenBy(p => p.ClientId)
                .ToArray();

            if (players.Length == 0)
            {
                _networkPlayersText.text = "Waiting for network players...";
                return;
            }

            _networkPlayersText.text = string.Join("\n", players.Select(player =>
            {
                string owner = player.IsOwner ? "local" : "remote";
                string ready = player.IsReady.Value ? "ready" : "not ready";
                string finish = player.IsFinished.Value ? $" | finish {player.FinishTime.Value:0.00}s" : string.Empty;
                return $"#{player.ClientId} {player.Nickname.Value} ({owner}, {ready}) | HP {player.HP.Value} | CP {player.CheckpointIndex.Value} | Score {player.Score.Value}{finish}";
            }));
        }

        private void RefreshNetworkButtons(NetworkSessionController session, NetworkPlayerData localPlayer)
        {
            bool inLobby = session != null && session.Phase.Value == SessionPhase.Lobby;
            bool inResults = session != null && session.Phase.Value == SessionPhase.Results;

            if (_applyNicknameButton != null)
                _applyNicknameButton.interactable = localPlayer != null;

            if (_readyButton != null)
            {
                _readyButton.interactable = localPlayer != null && inLobby;
                SetButtonLabel(_readyButton, localPlayer != null && localPlayer.IsReady.Value ? "Unready" : "Ready");
            }

            if (_forceStartButton != null)
                _forceStartButton.interactable = inLobby && session.ConnectedPlayers.Value > 0;

            if (_restartLobbyButton != null)
            {
                _restartLobbyButton.interactable = session != null && (inResults || session.Phase.Value == SessionPhase.Race || session.Phase.Value == SessionPhase.Countdown);
                SetButtonLabel(_restartLobbyButton, inResults ? "Restart" : "Lobby");
            }

            if (_exitToMenuButton != null)
                _exitToMenuButton.interactable = true;
        }

        private void ApplyNicknameToLocalPlayer()
        {
            NetworkPlayerData localPlayer = GetLocalNetworkPlayer();
            if (localPlayer == null) return;

            string nickname = _networkNicknameInput != null ? _networkNicknameInput.text : NetworkPlayerPreferences.GetNickname();
            NetworkPlayerPreferences.SetNickname(nickname);
            localPlayer.SetNickname(NetworkPlayerPreferences.GetNickname());
        }

        private void ToggleReadyForLocalPlayer()
        {
            NetworkPlayerData localPlayer = GetLocalNetworkPlayer();
            if (localPlayer == null) return;

            localPlayer.SetReady(!localPlayer.IsReady.Value);
        }

        private void ForceStartSession()
        {
            NetworkSessionController.Instance?.RequestForceStartServerRpc();
        }

        private void RestartNetworkLobby()
        {
            NetworkSessionController.Instance?.RequestRestartServerRpc();
        }

        private void ExitNetworkSessionToMenu()
        {
            _connectionService?.StopConnection();
            _stateMachine?.Enter<MainMenuState>();
        }

        private NetworkPlayerData GetLocalNetworkPlayer()
        {
            if (_networkPlayerData != null && _networkPlayerData.IsOwner)
                return _networkPlayerData;

            _networkPlayerData = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None)
                .FirstOrDefault(player => player.IsOwner);
            return _networkPlayerData;
        }

        private RectTransform CreateNetworkPanelRoot()
        {
            GameObject panelObject = new("NetworkSessionPanel");
            panelObject.transform.SetParent(transform, false);

            RectTransform rect = panelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(520f, 360f);
            return rect;
        }

        private TMP_InputField CreateNetworkInputRow(Transform parent, string rowName, string label, string defaultValue, string inputName)
        {
            Transform row = CreateNetworkButtonRow(parent, rowName, 44f);
            CreateNetworkText(row, $"{rowName}_Label", label, 16, TextAlignmentOptions.Left, 44f, 120f);
            return CreateNetworkInput(row, inputName, defaultValue);
        }

        private Transform CreateNetworkButtonRow(Transform parent, string rowName, float height = 42f)
        {
            GameObject rowObject = new(rowName);
            rowObject.transform.SetParent(parent, false);

            RectTransform rect = rowObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, height);

            HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            LayoutElement layoutElement = rowObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = height;

            return rowObject.transform;
        }

        private TMP_InputField CreateNetworkInput(Transform parent, string name, string defaultValue)
        {
            GameObject root = new(name);
            root.transform.SetParent(parent, false);

            RectTransform rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(280f, 38f);

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
            viewport.offsetMin = new Vector2(10f, 5f);
            viewport.offsetMax = new Vector2(-10f, -5f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = CreateNetworkText(textArea.transform, "Placeholder", defaultValue, 16, TextAlignmentOptions.Left);
            placeholder.color = new Color(0f, 0.17f, 0.29f, 0.45f);

            TextMeshProUGUI text = CreateNetworkText(textArea.transform, "Text", string.Empty, 16, TextAlignmentOptions.Left);
            text.color = new Color(0f, 0.17f, 0.29f, 1f);

            input.textViewport = viewport;
            input.placeholder = placeholder;
            input.textComponent = text;

            LayoutElement layoutElement = root.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 38f;
            layoutElement.flexibleWidth = 1f;

            return input;
        }

        private Button CreateNetworkButton(Transform parent, string name, string label)
        {
            GameObject root = new(name);
            root.transform.SetParent(parent, false);

            RectTransform rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(140f, 38f);

            Image image = root.AddComponent<Image>();
            image.color = new Color(0.4f, 0.9f, 1f, 0.92f);

            Button button = root.AddComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI text = CreateNetworkText(root.transform, "Label", label, 16, TextAlignmentOptions.Center);
            text.color = new Color(0f, 0.17f, 0.29f, 1f);

            LayoutElement layoutElement = root.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 38f;
            layoutElement.flexibleWidth = 1f;

            return button;
        }

        private TextMeshProUGUI CreateNetworkText(
            Transform parent,
            string name,
            string text,
            int fontSize,
            TextAlignmentOptions alignment,
            float preferredHeight = 0f,
            float preferredWidth = 0f)
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
            if (_timerText != null && _timerText.font != null)
                label.font = _timerText.font;

            LayoutElement layoutElement = textObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = preferredHeight > 0f ? preferredHeight : fontSize + 12f;
            if (preferredWidth > 0f)
                layoutElement.preferredWidth = preferredWidth;

            return label;
        }

        private T FindNetworkPanelComponent<T>(string objectName) where T : Component
        {
            if (_networkPanelRoot == null) return null;

            T[] components = _networkPanelRoot.GetComponentsInChildren<T>(true);
            foreach (T component in components)
            {
                if (component != null && component.name == objectName)
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
