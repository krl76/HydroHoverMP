#if UNITY_EDITOR
using System.Linq;
using Features.Networking;
using FishNet.Component.Scenes;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HydroHoverMP.Editor.Networking
{
    public static class FishNetProjectSetup
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
        private const string LevelScenePath = "Assets/Scenes/Level.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/HoverCraft.prefab";

        [MenuItem("HydroHoverMP/Networking/Apply FishNet Setup")]
        public static void Apply()
        {
            ConfigurePlayerPrefab();
            ConfigureBuildSettings();
            ConfigureBootstrapScene();
            ConfigureGameplayScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[HydroHoverMP] FishNet setup applied. Open Bootstrap scene and test Host + Client.");
        }

        private static void ConfigurePlayerPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                AddIfMissing<NetworkObject>(root);
                AddIfMissing<NetworkTransform>(root);
                AddIfMissing<NetworkPlayerData>(root);
                AddIfMissing<NetworkHoverOwnerBridge>(root);
                AddIfMissing<NetworkHydroPulse>(root);
                AddIfMissing<PlayerNameplate>(root);

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureBuildSettings()
        {
            string[] required =
            {
                BootstrapScenePath,
                MainMenuScenePath,
                GameplayScenePath,
                LevelScenePath
            };

            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            EditorBuildSettings.scenes = existing
                .Concat(required
                    .Where(path => existing.All(scene => scene.path != path))
                    .Select(path => new EditorBuildSettingsScene(path, true)))
                .ToArray();
        }

        private static void ConfigureBootstrapScene()
        {
            Scene scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);

            GameObject networkObject = GameObject.Find("FishNet NetworkManager");
            if (networkObject == null)
                networkObject = new GameObject("FishNet NetworkManager");

            AddIfMissing<NetworkManager>(networkObject);

            DefaultScene defaultScene = AddIfMissing<DefaultScene>(networkObject);
            defaultScene.SetOfflineScene("MainMenu");
            defaultScene.SetOnlineScene("Gameplay");

            PlayerSpawner spawner = AddIfMissing<PlayerSpawner>(networkObject);
            NetworkObject playerPrefab = AssetDatabase
                .LoadAssetAtPath<GameObject>(PlayerPrefabPath)
                .GetComponent<NetworkObject>();
            spawner.SetPlayerPrefab(playerPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureGameplayScene()
        {
            Scene scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);

            GameObject sessionObject = GameObject.Find("Network Session");
            if (sessionObject == null)
                sessionObject = new GameObject("Network Session");

            AddIfMissing<NetworkObject>(sessionObject);
            AddIfMissing<NetworkSessionController>(sessionObject);
            AddIfMissing<NetworkRaceManager>(sessionObject);
            AddIfMissing<NetworkLevelAdditiveLoader>(sessionObject);
            AddIfMissing<NetworkGameplayStateBridge>(sessionObject);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static T AddIfMissing<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component != null) return component;

            Undo.AddComponent<T>(gameObject);
            return gameObject.GetComponent<T>();
        }
    }
}
#endif
