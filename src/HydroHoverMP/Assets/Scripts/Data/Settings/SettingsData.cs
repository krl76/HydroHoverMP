using System;

namespace Data.Settings
{
    [Serializable]
    public class SettingsData
    {
        public bool IsMuted = false;
        public float MasterVolume = 1.0f;
        public float MusicVolume = 1.0f;
        public float SFXVolume = 1.0f;
        
        public float MouseSensitivity = 1.0f;
        
        public string InputOverridesJson; 
    }
}