using System.Collections.Generic;
using Data.Leaderbords;
using Infrastructure.Services.Leaderboard;
using Infrastructure.Services.Window;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.Leaderboard
{
    public class LeaderboardWindow : MonoBehaviour
    {
        [SerializeField] private Transform _container;
        [SerializeField] private GameObject _recordItemPrefab;
        [SerializeField] private Button _closeButton;

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
            Refresh();
        }

        private void Refresh()
        {
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

        private void Close()
        {
            _windowService.Open(WindowID.MainMenu);
            _windowService.Close(WindowID.Leaderboard);
        }
    }
}