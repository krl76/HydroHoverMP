using FishNet.Component.Scenes;
using FishNet.Managing;
using UnityEngine;

namespace Features.Networking
{
    public static class NetworkBootstrapper
    {
        public static NetworkManager EnsureRuntimeObjects()
        {
            NetworkManager networkManager = Object.FindFirstObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                GameObject managerObject = new("FishNet NetworkManager");
                networkManager = managerObject.AddComponent<NetworkManager>();
                Object.DontDestroyOnLoad(managerObject);
            }

            DisableDefaultSceneAutomation(networkManager);
            return networkManager;
        }

        // FishNet DefaultScene сам грузит online-сцену на старте сервера и перезагружает offline-сцену
        // (Bootstrap) в режиме Single при смене состояния подключения. В этом проекте Bootstrap в Start()
        // запускает весь бутстрап, поэтому перезагрузка offline-сцены повторно стартует сервер и грузит
        // Gameplay второй раз — отсюда дубли контекстов Zenject / EventSystem / NetworkSession.
        // Управление сетевыми сценами берём на себя (NetworkConnectionService.LoadGameplayGlobalSceneOnce),
        // а автоматику DefaultScene отключаем, удаляя компонент до старта сервера (OnDestroy отписывает
        // его обработчики). Делаем это до StartServer, поэтому используем DestroyImmediate.
        private static void DisableDefaultSceneAutomation(NetworkManager networkManager)
        {
            if (networkManager == null) return;

            DefaultScene defaultScene = networkManager.GetComponent<DefaultScene>();
            if (defaultScene != null)
                Object.DestroyImmediate(defaultScene);
        }
    }
}
