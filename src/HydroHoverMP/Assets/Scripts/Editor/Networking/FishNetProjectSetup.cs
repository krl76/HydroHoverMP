#if UNITY_EDITOR
using System.Linq;
using Features.Networking;
using FishNet.Component.Scenes;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using UI.HUD;

namespace HydroHoverMP.Editor.Networking
{
    public static class FishNetProjectSetup
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
        private const string LevelScenePath = "Assets/Scenes/Level.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player/HoverCraft.prefab";
        private const string HudPrefabPath = "Assets/Prefabs/UI/HUD.prefab";

        [MenuItem("HydroHoverMP/Networking/Apply FishNet Setup")]
        public static void Apply()
        {
            ConfigurePlayerPrefab();
            ConfigureHudPrefab();
            ConfigureBuildSettings();
            ConfigureBootstrapScene();
            ConfigureGameplayScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[HydroHoverMP] FishNet setup applied. Open Bootstrap scene and test Host + Client.");
        }

        [MenuItem("HydroHoverMP/Networking/Apply Multiplayer Start And Spawns")]
        public static void ApplyMultiplayerStartAndSpawns()
        {
            ConfigureHudPrefab();
            ConfigureGameplayScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[HydroHoverMP] Multiplayer start button and spawn points applied.");
        }

        private static void ConfigureHudPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);
            try
            {
                HUDWindow hudWindow = root.GetComponent<HUDWindow>();
                if (hudWindow == null)
                    hudWindow = root.AddComponent<HUDWindow>();

                Button startButton = EnsureHostStartButton(root.transform);
                TextMeshProUGUI pingText = EnsurePingText(root.transform);
                SerializedObject serializedHud = new(hudWindow);
                serializedHud.FindProperty("_hostStartButton").objectReferenceValue = startButton;
                serializedHud.FindProperty("_pingText").objectReferenceValue = pingText;
                serializedHud.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Button EnsureHostStartButton(Transform hudRoot)
        {
            Transform existing = hudRoot.Find("HostStartButton");
            GameObject buttonObject = existing != null ? existing.gameObject : new GameObject("HostStartButton", typeof(RectTransform));
            if (existing == null)
                buttonObject.transform.SetParent(hudRoot, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-32f, -92f);
            rect.sizeDelta = new Vector2(168f, 52f);

            Image image = buttonObject.GetComponent<Image>();
            if (image == null)
                image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.7f, 0.82f, 0.88f);
            image.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            if (button == null)
                button = buttonObject.AddComponent<Button>();

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.9f, 0.95f, 1f);
            button.colors = colors;

            Transform label = buttonObject.transform.Find("Label");
            GameObject labelObject = label != null ? label.gameObject : new GameObject("Label", typeof(RectTransform));
            if (label == null)
                labelObject.transform.SetParent(buttonObject.transform, false);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
            if (text == null)
                text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = "Start";
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;

            buttonObject.SetActive(false);
            return button;
        }

        private static TextMeshProUGUI EnsurePingText(Transform hudRoot)
        {
            Transform existing = hudRoot.Find("Ping");
            GameObject pingObject = existing != null ? existing.gameObject : new GameObject("Ping", typeof(RectTransform));
            if (existing == null)
                pingObject.transform.SetParent(hudRoot, false);

            RectTransform rect = pingObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 40f);
            rect.sizeDelta = new Vector2(220f, 32f);

            TextMeshProUGUI referenceText = hudRoot.Find("FPS")?.GetComponent<TextMeshProUGUI>()
                ?? hudRoot.Find("Timer")?.GetComponent<TextMeshProUGUI>()
                ?? hudRoot.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();

            TextMeshProUGUI text = pingObject.GetComponent<TextMeshProUGUI>();
            if (text == null)
                text = pingObject.AddComponent<TextMeshProUGUI>();

            if (referenceText != null)
            {
                text.font = referenceText.font;
                text.fontSharedMaterial = referenceText.fontSharedMaterial;
                text.fontSize = referenceText.fontSize;
                text.fontStyle = referenceText.fontStyle;
                text.enableAutoSizing = referenceText.enableAutoSizing;
                text.color = referenceText.color;
            }
            else
            {
                text.fontSize = 24f;
                text.color = Color.white;
            }

            text.text = "Ping -- ms";
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            return text;
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
            TransportManager transportManager = AddIfMissing<TransportManager>(networkObject);
            Tugboat tugboat = AddIfMissing<Tugboat>(networkObject);
            transportManager.Transport = tugboat;

            DefaultScene defaultScene = AddIfMissing<DefaultScene>(networkObject);
            defaultScene.SetOfflineScene("Bootstrap");
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
            NetworkSpawnPointRegistry spawnRegistry = AddIfMissing<NetworkSpawnPointRegistry>(sessionObject);
            AddIfMissing<NetworkLevelAdditiveLoader>(sessionObject);
            AddIfMissing<NetworkGameplayStateBridge>(sessionObject);

            AssignNetworkSpawnPoints(spawnRegistry);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void AssignNetworkSpawnPoints(NetworkSpawnPointRegistry spawnRegistry)
        {
            Transform[] spawnPoints = EnsureNetworkSpawnPoints();
            SerializedObject serializedRegistry = new(spawnRegistry);
            SerializedProperty pointsProperty = serializedRegistry.FindProperty("_spawnPoints");
            pointsProperty.arraySize = spawnPoints.Length;
            for (int i = 0; i < spawnPoints.Length; i++)
                pointsProperty.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
            serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform[] EnsureNetworkSpawnPoints()
        {
            GameObject root = GameObject.Find("Network Spawn Points");
            if (root == null)
                root = new GameObject("Network Spawn Points");

            Transform first = GameObject.Find("SpawnPoint")?.transform;
            if (first == null)
            {
                GameObject firstObject = new("SpawnPoint");
                first = firstObject.transform;
            }

            first.SetParent(root.transform, false);
            first.SetPositionAndRotation(new Vector3(-4f, 1f, 0f), Quaternion.Euler(0f, 180f, 0f));
            first.gameObject.tag = "Spawn";

            return new[]
            {
                first,
                EnsureSpawnPoint(root.transform, "NetworkSpawnPoint_02", new Vector3(4f, 1f, 0f), Quaternion.Euler(0f, 180f, 0f)),
                EnsureSpawnPoint(root.transform, "NetworkSpawnPoint_03", new Vector3(-8f, 1f, -6f), Quaternion.Euler(0f, 180f, 0f)),
                EnsureSpawnPoint(root.transform, "NetworkSpawnPoint_04", new Vector3(8f, 1f, -6f), Quaternion.Euler(0f, 180f, 0f))
            };
        }

        private static Transform EnsureSpawnPoint(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            Transform spawn = parent.Find(name);
            if (spawn == null)
            {
                GameObject spawnObject = new(name);
                spawn = spawnObject.transform;
                spawn.SetParent(parent, false);
            }

            spawn.SetPositionAndRotation(position, rotation);
            return spawn;
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
