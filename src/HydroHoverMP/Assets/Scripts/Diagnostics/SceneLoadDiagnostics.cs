using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HydroHoverMP.Diagnostics
{
    /// <summary>
    /// TEMPORARY diagnostic. Logs every Unity scene load/unload AND every FishNet scene
    /// load to pin down why the Gameplay scene is loaded twice on the dedicated server.
    /// Every line is prefixed with [SceneDiag]. The scene 'handle' uniquely identifies a
    /// scene instance, so two LOADED lines with the same name but different handles == the
    /// scene was loaded twice. Delete this file once the duplicate-load trigger is found.
    /// </summary>
    public static class SceneLoadDiagnostics
    {
        private static bool _fishNetHooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += OnUnitySceneLoaded;
            SceneManager.sceneUnloaded += OnUnitySceneUnloaded;
            Debug.Log($"[SceneDiag] Installed. batchMode={Application.isBatchMode} platform={Application.platform}");
        }

        private static void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryHookFishNet();
            Debug.Log($"[SceneDiag][Unity] LOADED name='{scene.name}' mode={mode} buildIndex={scene.buildIndex} " +
                      $"handle={scene.handle} loadedCount={SceneManager.sceneCount} frame={Time.frameCount}");
        }

        private static void OnUnitySceneUnloaded(Scene scene)
        {
            Debug.Log($"[SceneDiag][Unity] UNLOADED name='{scene.name}' handle={scene.handle} " +
                      $"loadedCount={SceneManager.sceneCount} frame={Time.frameCount}");
        }

        private static void TryHookFishNet()
        {
            if (_fishNetHooked) return;

            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm == null || nm.SceneManager == null) return;

            nm.SceneManager.OnLoadStart += OnFishNetLoadStart;
            nm.SceneManager.OnLoadEnd += OnFishNetLoadEnd;
            _fishNetHooked = true;
            Debug.Log("[SceneDiag] FishNet SceneManager hooked.");
        }

        private static void OnFishNetLoadStart(FishNet.Managing.Scened.SceneLoadStartEventArgs args)
        {
            FishNet.Managing.Scened.LoadQueueData q = args.QueueData;
            string globals = q != null && q.GlobalScenes != null ? string.Join(",", q.GlobalScenes) : "";
            Debug.Log($"[SceneDiag][FishNet] LoadStart asServer={(q != null && q.AsServer)} scope={q?.ScopeType} " +
                      $"globals=[{globals}] frame={Time.frameCount}");
        }

        private static void OnFishNetLoadEnd(FishNet.Managing.Scened.SceneLoadEndEventArgs args)
        {
            string loaded = args.LoadedScenes != null
                ? string.Join(",", System.Array.ConvertAll(args.LoadedScenes, s => s.name))
                : "";
            string skipped = args.SkippedSceneNames != null ? string.Join(",", args.SkippedSceneNames) : "";
            Debug.Log($"[SceneDiag][FishNet] LoadEnd asServer={(args.QueueData != null && args.QueueData.AsServer)} " +
                      $"loaded=[{loaded}] skipped=[{skipped}] frame={Time.frameCount}");
        }
    }
}
