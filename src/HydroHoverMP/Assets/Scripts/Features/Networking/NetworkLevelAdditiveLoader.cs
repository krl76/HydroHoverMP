using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Features.Networking
{
    public sealed class NetworkLevelAdditiveLoader : MonoBehaviour
    {
        [SerializeField] private string _levelSceneAddress = "Scene/Level";
        [SerializeField] private string _levelSceneName = "Level";

        private AsyncOperationHandle<SceneInstance> _handle;
        private bool _hasHandle;

        private IEnumerator Start()
        {
            if (string.IsNullOrWhiteSpace(_levelSceneAddress)) yield break;
            if (!string.IsNullOrWhiteSpace(_levelSceneName) &&
                SceneManager.GetSceneByName(_levelSceneName).isLoaded)
                yield break;

            AsyncOperationHandle<SceneInstance> handle =
                Addressables.LoadSceneAsync(_levelSceneAddress, LoadSceneMode.Additive);
            _handle = handle;
            _hasHandle = true;

            while (!handle.IsDone)
                yield return null;

            if (handle.Status != AsyncOperationStatus.Succeeded)
                Debug.LogError($"[NetworkLevelAdditiveLoader] Failed to load level scene '{_levelSceneAddress}' via Addressables. Server has no track colliders.");
        }

        private void OnDestroy()
        {
            if (_hasHandle && _handle.IsValid())
                Addressables.UnloadSceneAsync(_handle);
            _hasHandle = false;
        }
    }
}
