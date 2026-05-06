namespace Infrastructure.Services.Audio
{
    public interface IAudioService
    {
        void SetVolume(string parameterName, float value);
        float GetVolume(string parameterName);
    }
}