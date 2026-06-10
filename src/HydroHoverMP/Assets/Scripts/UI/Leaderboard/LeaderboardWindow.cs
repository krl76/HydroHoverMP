using System.Collections.Generic;
using Data.Leaderbords;
using Features.Networking;
using Infrastructure.Services.Leaderboard;
using Infrastructure.Services.Window;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Zenject;

namespace UI.Leaderboard
{
    public class LeaderboardWindow : MonoBehaviour
    {
        [SerializeField] private Transform _container;
        [SerializeField] private GameObject _recordItemPrefab;
        [SerializeField] private Button _closeButton;
        [SerializeField] private TextMeshProUGUI _sourceText;

        private ILeaderboardService _leaderboardService;
        private IWindowService _windowService;

        [Inject]
        public void Construct(ILeaderboardService leaderboardService, IWindowService windowService)
        {
            _leaderboardService = leaderboardService;
            _windowService = windowService;
        }

        private const float QueryTimeoutSeconds = 8f;
        private float _queryDeadline;
        private bool _awaitingRecords;

        private void Start()
        {
            _closeButton.onClick.AddListener(Close);
            _leaderboardService.OnDedicatedRecordsUpdated += OnRecordsUpdated;

            BeginLoadOrShow();
        }

        private void OnDestroy()
        {
            _leaderboardService.OnDedicatedRecordsUpdated -= OnRecordsUpdated;
            // The query connection stays open while the window is shown; always tear it down on close.
            _leaderboardService.CancelDedicatedRecordsRequest();
        }

        private void Update()
        {
            if (!_awaitingRecords) return;
            if (Time.unscaledTime < _queryDeadline) return;

            _awaitingRecords = false;
            _leaderboardService.CancelDedicatedRecordsRequest();
            RenderRecords(_leaderboardService.GetTopRecords(5), "Server unreachable — showing last known records.");
        }

        private void BeginLoadOrShow()
        {
            if (_sourceText != null)
                _sourceText.text = BuildSourceText();

            bool willQuery = _leaderboardService.IsUsingDedicatedServer
                             && NetworkSessionController.Instance == null;

            if (willQuery)
            {
                ClearContainer();
                ShowStatus("Fetching records from server…");

                if (_leaderboardService.RequestDedicatedRecords())
                {
                    // Records will arrive asynchronously via OnDedicatedRecordsUpdated; arm the timeout.
                    _awaitingRecords = true;
                    _queryDeadline = Time.unscaledTime + QueryTimeoutSeconds;
                }
                else
                {
                    // Could not start a query (no connection service / busy / connect failed):
                    // show the last-known records now instead of a false 8s "Fetching" state.
                    RenderRecords(_leaderboardService.GetTopRecords(5), "Server unreachable — showing last known records.");
                }

                return;
            }

            // Local mode, or already connected (live SyncList): render immediately.
            _leaderboardService.RequestDedicatedRecords();
            RenderRecords(_leaderboardService.GetTopRecords(5), null);
        }

        private void OnRecordsUpdated()
        {
            _awaitingRecords = false;
            RenderRecords(_leaderboardService.GetTopRecords(5), null);
        }

        private void RenderRecords(List<Record> records, string emptyHint)
        {
            ClearContainer();

            if (records.Count == 0)
            {
                ShowStatus(emptyHint ?? "No records yet.");
                return;
            }

            if (_sourceText != null)
                _sourceText.text = BuildSourceText();

            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var itemObj = Instantiate(_recordItemPrefab, _container);

                var view = itemObj.GetComponent<RecordItemView>();
                view.SetData(i + 1, record.Time, record.PlayerName);
            }
        }

        private void ShowStatus(string message)
        {
            if (_sourceText != null)
                _sourceText.text = message;
        }

        private void ClearContainer()
        {
            foreach (Transform child in _container) Destroy(child.gameObject);
        }

        private string BuildSourceText()
        {
            if (!_leaderboardService.IsUsingDedicatedServer)
                return "Leaderboard source: Local";

            return $"Leaderboard source: Dedicated server {_leaderboardService.DedicatedServerAddress}:{_leaderboardService.DedicatedServerPort}";
        }

        private void Close()
        {
            _windowService.Open(WindowID.MainMenu);
            _windowService.Close(WindowID.Leaderboard);
        }
    }
}
