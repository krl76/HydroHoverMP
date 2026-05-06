using Infrastructure.Providers.Assets;
using UnityEngine;
using UnityEngine.Audio;

namespace Infrastructure.Services.Audio
{
    public class AudioService : IAudioService
    {
        private const string MixerPath = "Audio/MainMixer";
        private AudioMixer _mixer;
        private IAssetsAddressablesProvider _assets;

        public AudioService(IAssetsAddressablesProvider assets)
        {
            _assets = assets;
            Initialize();
        }

        private async void Initialize()
        {
            _mixer = await _assets.GetAsset<AudioMixer>(MixerPath);
        }

        public void SetVolume(string name, float sliderValue)
        {
            if (_mixer == null) return;
            
            float db = Mathf.Log10(Mathf.Max(sliderValue, 0.0001f)) * 20;
            _mixer.SetFloat(name, db);
        }

        public float GetVolume(string name)
        {
            if (_mixer != null && _mixer.GetFloat(name, out float db))
                return Mathf.Pow(10, db / 20);
            return 1f;
        }
    }
}