using System.Collections.Generic;
using System.Linq;
using Core.States.Base;
using Core.States.Core;
using Core.States.MainMenu;
using Data;
using Features.Networking;
using Infrastructure.Services.Leaderboard;
using Infrastructure.Services.RaceManager;
using Infrastructure.Services.Window;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.Finish
{
    public class FinishScreen : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _timeText;
        [SerializeField] private TextMeshProUGUI _bestTimeText;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _menuButton;

        private IRaceManagerService _raceService;
        private GameStateMachine _stateMachine;
        private IWindowService _windowService;
        private ILeaderboardService _leaderboardService;
        private TextMeshProUGUI _networkResultsText;
        private TextMeshProUGUI _networkStatusText;

        [Inject]
        public void Construct(IRaceManagerService raceService,
            GameStateMachine stateMachine, IWindowService windowService, ILeaderboardService leaderboardService)
        {
            _raceService = raceService;
            _stateMachine = stateMachine;
            _windowService = windowService;
            _leaderboardService = leaderboardService;
        }

        private void Start()
        {
            float currentTime = _raceService.CurrentTime;

            _leaderboardService.AddRecord(currentTime);

            _timeText.text = $"Time: {FormatTime(currentTime)}";

            float best = _leaderboardService.GetBestTime();
            _bestTimeText.text = $"Best: {FormatTime(best)}";

            EnsureNetworkResultsPanel();
            RefreshNetworkResults();

            _restartButton.onClick.AddListener(OnRestartClicked);
            _menuButton.onClick.AddListener(OnMenuClicked);
        }

        private void Update()
        {
            RefreshNetworkResults();
        }

        private void OnRestartClicked()
        {
            NetworkSessionController session = NetworkSessionController.Instance;
            if (session != null)
            {
                session.RequestRestartServerRpc();
                if (_networkStatusText != null)
                    _networkStatusText.text = "Restart requested. Returning synced session to lobby.";
                return;
            }

            _windowService.Close(WindowID.Finish);
            _stateMachine.Enter<LoadLevelState, string>(ScenesPaths.GAMEPLAY);
        }

        private void OnMenuClicked()
        {
            _windowService.Close(WindowID.Finish);
            _stateMachine.Enter<MainMenuState>();
        }

        private void EnsureNetworkResultsPanel()
        {
            if (_networkResultsText != null) return;

            _networkStatusText = CreateText("NetworkResultsStatus", "Synced multiplayer results", new Vector2(0f, -114f), new Vector2(620f, 32f), 19, TextAlignmentOptions.Center, new Color(0.85f, 0.95f, 1f));
            _networkResultsText = CreateText("NetworkResultsList", string.Empty, new Vector2(0f, -172f), new Vector2(680f, 220f), 18, TextAlignmentOptions.Center, Color.white);
        }

        private void RefreshNetworkResults()
        {
            if (_networkResultsText == null) return;

            NetworkSessionController session = NetworkSessionController.Instance;
            NetworkPlayerData[] players = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None);
            if (session == null || players.Length == 0)
            {
                _networkResultsText.text = "No synced multiplayer finish data is visible in this scene.";
                if (_networkStatusText != null)
                    _networkStatusText.text = "Solo/local result";
                return;
            }

            if (_networkStatusText != null)
                _networkStatusText.text = session.Phase.Value == SessionPhase.Results
                    ? "Server results synced to all clients"
                    : $"Session is {session.Phase.Value}; showing latest synced player data";

            List<NetworkPlayerData> orderedPlayers = players
                .Where(player => player != null)
                .OrderByDescending(player => player.IsFinished.Value)
                .ThenBy(player => player.IsFinished.Value ? player.FinishTime.Value : float.MaxValue)
                .ThenByDescending(player => player.Score.Value)
                .ThenBy(player => player.ClientId)
                .ToList();

            _networkResultsText.text = string.Join("\n", orderedPlayers.Select((player, index) => BuildResultLine(index + 1, player)));
        }

        private string BuildResultLine(int place, NetworkPlayerData player)
        {
            string local = player.IsOwner ? " <local>" : string.Empty;
            string finish = player.IsFinished.Value ? FormatTime(player.FinishTime.Value) : "DNF";
            string state = player.IsFinished.Value ? "Finished" : player.IsAlive ? "In progress" : "Out";
            return $"{place}. {player.Nickname.Value}{local} - {state} - {finish} - Score {player.Score.Value} - HP {player.HP.Value}";
        }

        private TextMeshProUGUI CreateText(string objectName, string text, Vector2 position, Vector2 size, int fontSize, TextAlignmentOptions alignment, Color color)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(transform, false);

            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            label.color = color;
            if (_timeText != null && _timeText.font != null)
                label.font = _timeText.font;
            return label;
        }

        private string FormatTime(float t)
        {
            int minutes = (int)(t / 60);
            int seconds = (int)(t % 60);
            int milliseconds = (int)((t * 100) % 100);
            return $"{minutes:00}:{seconds:00}.{milliseconds:00}";
        }
    }
}
