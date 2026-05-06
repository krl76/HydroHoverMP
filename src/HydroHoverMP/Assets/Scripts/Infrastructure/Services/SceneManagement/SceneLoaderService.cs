using System;
using Cysharp.Threading.Tasks;
using Infrastructure.Services.Window;
using UI.Loading;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace Infrastructure.Services.SceneManagement
{
    public class SceneLoaderService : ISceneLoaderService
    {
        private readonly IWindowService _windowService;

        public SceneLoaderService(IWindowService windowService)
        {
            _windowService = windowService;
        }

        public async void LoadScene(string sceneAddress, Action onLoaded = null)
        {
            await LoadSceneAsync(sceneAddress, onLoaded);
        }

        private async UniTask LoadSceneAsync(string sceneAddress, Action onLoaded)
        {
            var loadingWindow = await _windowService.OpenAndGet<LoadingScreenWindow>(WindowID.Loading);
            loadingWindow.UpdateProgress(0);
            
            var loadOp = Addressables.LoadSceneAsync(sceneAddress, LoadSceneMode.Single);
            
            while (!loadOp.IsDone)
            {
                loadingWindow.UpdateProgress(loadOp.PercentComplete);
                
                await UniTask.Yield();
            }
            
            loadingWindow.UpdateProgress(1f);
            
            await UniTask.Delay(500);
    
            _windowService.Close(WindowID.Loading);
            
            onLoaded?.Invoke();
        }
        
        public async void LoadSceneAdditive(string sceneAddress)
        {
            await Addressables.LoadSceneAsync(sceneAddress, LoadSceneMode.Additive).ToUniTask();
        }
    }
}