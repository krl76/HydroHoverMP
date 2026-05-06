using TMPro;
using UnityEngine;

namespace UI.Leaderboard
{
    public class RecordItemView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _timeText;

        public void SetData(int rank, float time)
        {
            _rankText.text = $"#{rank}";
            _timeText.text = FormatTime(time);
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