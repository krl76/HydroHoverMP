using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Features.Networking
{
    public sealed class NetworkLevelAdditiveLoader : MonoBehaviour
    {
        [SerializeField] private string _levelSceneName = "Level";

        private IEnumerator Start()
        {
            if (string.IsNullOrWhiteSpace(_levelSceneName)) yield break;
            if (SceneManager.GetSceneByName(_levelSceneName).isLoaded) yield break;

            AsyncOperation operation = SceneManager.LoadSceneAsync(_levelSceneName, LoadSceneMode.Additive);
            if (operation == null) yield break;

            while (!operation.isDone)
                yield return null;
        }
    }
}
