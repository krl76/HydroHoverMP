using System.Collections.Generic;
using System.Linq;
using Core.States.Base;
using Core.States.Core;
using Core.States.MainMenu;
using Data;
using Features.Networking;
using Infrastructure.Services.Network;
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
        private INetworkConnectionService _connectionService;
        private TextMeshProUGUI _networkResultsText;
        private TextMeshProUGUI _networkStatusText;
        private bool _voted;

        [Inject]
        public void Construct(IRaceManagerService raceService,
            GameStateMachine stateMachine,
            IWindowService windowService,
            ILeaderboardService leaderboardService,
            INetworkConnectionService connectionService)
        {
            _raceService = raceService;
            _stateMachine = stateMachine;
            _windowService = windowService;
            _leaderboardService = leaderboardService;
            _connectionService = connectionService;
        }

        private void Start()
        {
            NetworkSessionController session = NetworkSessionController.Instance;
            bool networked = session != null;

            // In multiplayer the authoritative time comes from the synced player state; the
            // local race service is inert during a networked race.
            float currentTime = networked ? GetLocalNetworkedFinishTime() : _raceService.CurrentTime;

            // Local (single-player) leaderboard only. In multiplayer the record (time + nickname)
            // is written server-side in NetworkRaceManager.ServerFinishPlayer.
            if (!networked)
                _leaderboardService.AddRecord(currentTime, NetworkPlayerPreferences.GetNickname());

            _timeText.text = $"Time: {FormatTime(currentTime)}";

            float best = _leaderboardService.GetBestTime();
            _bestTimeText.text = $"Best: {FormatTime(best)}";

            EnsureNetworkResultsPanel();
            RefreshNetworkResults();

            SetButtonLabel(_restartButton, networked ? "Race Again" : "Restart");
            SetButtonLabel(_menuButton, "Main Menu");

            _restartButton.onClick.AddListener(OnRestartClicked);
            _menuButton.onClick.AddListener(OnMenuClicked);
        }

        private void Update()
        {
            RefreshNetworkResults();
            RefreshVoteButton();
        }

        private void OnRestartClicked()
        {
            NetworkSessionController session = NetworkSessionController.Instance;
            if (session != null)
            {
                // Multiplayer: cast a "Race again" vote. The server restarts the session for
                // everyone once a majority of connected pilots agree.
                _voted = true;
                session.SubmitPostRaceVoteServerRpc(PostRaceVote.Restart);
                RefreshVoteButton();
                return;
            }

            _windowService.Close(WindowID.Finish);
            _stateMachine.Enter<LoadLevelState, string>(ScenesPaths.GAMEPLAY);
        }

        private void OnMenuClicked()
        {
            // "Main menu" is always available per-player: leave the session immediately. The
            // server drops this pilot's vote and recomputes the restart majority for the rest.
            if (NetworkSessionController.Instance != null)
                _connectionService?.StopConnection();

            _windowService.Close(WindowID.Finish);
            _stateMachine.Enter<MainMenuState>();
        }

        private void RefreshVoteButton()
        {
            if (!_voted || _restartButton == null)
                return;

            if (_restartButton.interactable)
            {
                _restartButton.interactable = false;
                SetButtonLabel(_restartButton, "Voted ✓");
            }
        }

        private float GetLocalNetworkedFinishTime()
        {
            NetworkPlayerData[] players = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None);
            foreach (NetworkPlayerData player in players)
            {
                if (player != null && player.IsOwner)
                    return player.FinishTime.Value;
            }

            return 0f;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
                return;

            TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
                text.text = label;
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
            if (session == null)
            {
                _networkResultsText.text = "No synced multiplayer finish data is visible in this scene.";
                if (_networkStatusText != null)
                    _networkStatusText.text = "Solo/local result";
                return;
            }

            if (_networkStatusText != null)
                _networkStatusText.text = session.Phase.Value == SessionPhase.Results
                    ? BuildVoteStatus(session)
                    : $"Session is {session.Phase.Value}; showing latest synced player data";

            if (session.Results.Count > 0)
            {
                _networkResultsText.text = BuildSnapshotResults(session.Results);
                return;
            }

            if (players.Length == 0)
            {
                _networkResultsText.text = "Waiting for server results or live player data to synchronize.";
                return;
            }

            List<NetworkPlayerData> orderedPlayers = players
                .Where(player => player != null)
                .OrderByDescending(player => player.IsFinished.Value)
                .ThenBy(player => player.IsFinished.Value ? player.FinishTime.Value : float.MaxValue)
                .ThenByDescending(player => player.Score.Value)
                .ThenBy(player => player.ClientId)
                .ToList();

            _networkResultsText.text = string.Join("\n", orderedPlayers.Select((player, index) => BuildResultLine(index + 1, player)));
        }

        private string BuildVoteStatus(NetworkSessionController session)
        {
            int connected = session.ConnectedPlayers.Value;
            int restartVotes = 0;
            foreach (KeyValuePair<int, PostRaceVote> vote in session.PostRaceVotes)
            {
                if (vote.Value == PostRaceVote.Restart)
                    restartVotes++;
            }

            int needed = connected / 2 + 1;
            string prompt = _voted
                ? "You voted to race again — waiting for the others."
                : "Race again, or back to the main menu?";

            return $"{prompt}   Race again: {restartVotes}/{connected} (need {needed})";
        }

        private string BuildSnapshotResults(IReadOnlyList<NetworkRaceResult> results)
        {
            List<NetworkRaceResult> orderedResults = results
                .OrderByDescending(result => result.IsFinished)
                .ThenBy(result => result.IsFinished ? result.FinishTime : float.MaxValue)
                .ThenByDescending(result => result.Score)
                .ThenByDescending(result => result.CheckpointIndex)
                .ThenBy(result => result.ClientId)
                .ToList();

            return string.Join("\n", orderedResults.Select((result, index) => BuildSnapshotResultLine(index + 1, result)));
        }

        private string BuildSnapshotResultLine(int place, NetworkRaceResult result)
        {
            string finish = result.IsFinished ? FormatTime(result.FinishTime) : "DNF";
            string state = result.IsFinished ? "Finished" : result.HP > 0 ? "In progress" : "Out";
            string disconnected = result.IsDisconnected ? " - Disconnected" : string.Empty;
            return $"{place}. {result.Nickname} - {state}{disconnected} - {finish} - Score {result.Score} - HP {result.HP}";
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
