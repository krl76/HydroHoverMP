using System;

namespace Infrastructure.Services.SceneManagement
{
    public interface ISceneLoaderService
    {
        void LoadScene(string sceneName, Action onLoaded = null);
        void LoadSceneAdditive(string sceneName);
    }
}