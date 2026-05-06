namespace Infrastructure.Services.Settings
{
    public interface ISettingsService
    {
        float MasterVolume { get; set; }
        float MusicVolume { get; set; }
        float SFXVolume { get; set; }
        bool IsMuted { get; set; }
        float Sensitivity { get; set; }
        
        void Save();
        void Load();
    }
}