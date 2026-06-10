using TMPro;
using UnityEngine;

namespace UI.Leaderboard
{
    public class RecordItemView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _timeText;

        public void SetData(int rank, float time, string playerName)
        {
            _rankText.text = $"#{rank}";

            string displayName = string.IsNullOrWhiteSpace(playerName) ? "Pilot" : playerName;

            if (_nameText != null)
            {
                // Dedicated name column wired in the prefab: rank | name | time.
                _nameText.text = displayName;
                _timeText.text = FormatTime(time);
            }
            else
            {
                // Fallback for a 2-column prefab with no name label: fold the nickname into
                // the (wide) time column so the pilot is still shown next to the position.
                _timeText.text = $"{displayName}    {FormatTime(time)}";
            }
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
