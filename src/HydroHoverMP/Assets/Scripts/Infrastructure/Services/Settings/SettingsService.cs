using System.IO;
using Data.Settings;
using Infrastructure.Services.Audio;
using Infrastructure.Services.Input;
using UnityEngine;
using Newtonsoft.Json;

namespace Infrastructure.Services.Settings
{
    public class SettingsService : ISettingsService
    {
        private const string FileName = "settings.json";
        private const float MinDb = -80f;

        private readonly string _path;
        private readonly IAudioService _audioService;
        private readonly IInputService _inputService;
        
        private SettingsData _data;

        public SettingsService(IAudioService audioService, IInputService inputService)
        {
            _audioService = audioService;
            _inputService = inputService;
            _path = Path.Combine(Application.persistentDataPath, FileName);
            Load();
        }

        public bool IsMuted
        {
            get => _data.IsMuted;
            set
            {
                _data.IsMuted = value;
                RefreshAudio();
            }
        }

        public float MasterVolume
        {
            get => _data.MasterVolume;
            set
            {
                _data.MasterVolume = value;
                RefreshAudio();
            }
        }

        public float MusicVolume
        {
            get => _data.MusicVolume;
            set
            {
                _data.MusicVolume = value;
                _audioService.SetVolume("MusicVolume", _data.IsMuted ? 0 : value); 
            }
        }

        public float SFXVolume
        {
            get => _data.SFXVolume;
            set
            {
                _data.SFXVolume = value;
                _audioService.SetVolume("SFXVolume", _data.IsMuted ? 0 : value);
            }
        }

        public float Sensitivity
        {
            get => _data.MouseSensitivity;
            set
            {
                _data.MouseSensitivity = value;
                _inputService.SensitivityMultiplier = value;
            }
        }

        private void RefreshAudio()
        {
            _audioService.SetVolume("MasterVolume", _data.IsMuted ? 0 : _data.MasterVolume);
            _audioService.SetVolume("MusicVolume", _data.IsMuted ? 0 : _data.MusicVolume);
            _audioService.SetVolume("SFXVolume", _data.IsMuted ? 0 : _data.SFXVolume);
        }

        public void Save()
        {
            _data.InputOverridesJson = _inputService.SaveBindingOverrides();
            string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            File.WriteAllText(_path, json);
        }

        public void Load()
        {
            if (File.Exists(_path))
                _data = JsonConvert.DeserializeObject<SettingsData>(File.ReadAllText(_path)) ?? new SettingsData();
            else
                _data = new SettingsData();

            ApplySettings();
        }

        private void ApplySettings()
        {
            RefreshAudio();
            Sensitivity = _data.MouseSensitivity;
            
            if (!string.IsNullOrEmpty(_data.InputOverridesJson))
            {
                _inputService.LoadBindingOverrides(_data.InputOverridesJson);
            }
        }
    }
}