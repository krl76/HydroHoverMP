using UnityEngine;
using UnityEngine.UI;

namespace UI.Loading
{
    public class LoadingScreenWindow : MonoBehaviour
    {
        [SerializeField] private Slider _slider;

        public void UpdateProgress(float progress)
        {
            if (_slider != null)
            {
                _slider.value = progress;
            }
        }
    }
}