using System.Collections.Generic;
using Data.Leaderbords;
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

        private void Start()
        {
            _closeButton.onClick.AddListener(Close);
            _leaderboardService.RequestDedicatedRecords();
            Refresh();
        }

        private void Refresh()
        {
            if (_sourceText != null)
                _sourceText.text = BuildSourceText();

            foreach (Transform child in _container) Destroy(child.gameObject);
            
            List<Record> records = _leaderboardService.GetTopRecords(5);

            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var itemObj = Instantiate(_recordItemPrefab, _container);
                
                var view = itemObj.GetComponent<RecordItemView>();
                view.SetData(i + 1, record.Time);
            }
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
