using System.Collections;
using System.Collections.Generic;
using FishNet.Managing.Scened;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
using UnityScene = UnityEngine.SceneManagement.Scene;
using LoadSceneParameters = UnityEngine.SceneManagement.LoadSceneParameters;

namespace Features.Networking
{
    /// <summary>
    /// FishNet SceneProcessor that loads networked scenes through Addressables
    /// instead of the Build Settings scene list. Reuses DefaultSceneProcessor
    /// bookkeeping; only the load/unload mechanism is replaced.
    /// Assumes a load batch is homogeneous (all Addressable or all built-in);
    /// in this project only "Gameplay" is routed here.
    /// </summary>
    public sealed class AddressablesSceneProcessor : DefaultSceneProcessor
    {
        private const string AddressPrefix = "Scene/";

        // Allow-list of scenes that exist as Addressables. Only "Gameplay" is actually
        // routed through FishNet's networked SceneManager today; "Level"/"MainMenu" load
        // via their own loaders, and are listed here only defensively in case one is ever
        // promoted to a networked scene.
        private static readonly HashSet<string> AddressableSceneNames = new() { "Gameplay", "Level", "MainMenu" };

        private readonly List<AsyncOperationHandle<SceneInstance>> _loadingHandles = new();
        private readonly Dictionary<UnityScene, AsyncOperationHandle<SceneInstance>> _sceneHandles = new();
        private AsyncOperationHandle<SceneInstance> _currentUnloadHandle;
        private bool _loadingViaAddressables;
        private bool _unloadingViaAddressables;

        public static bool IsAddressableScene(string sceneName) => AddressableSceneNames.Contains(sceneName);

        public static string MapSceneNameToAddress(string sceneName) => AddressPrefix + sceneName;

        public override void Initialize(SceneManager manager)
        {
            base.Initialize(manager);
            UnitySceneManager.sceneUnloaded += OnSceneUnloadedExternally;
        }

        private void OnDestroy()
        {
            UnitySceneManager.sceneUnloaded -= OnSceneUnloadedExternally;
        }

        public override void LoadStart(LoadQueueData queueData)
        {
            base.LoadStart(queueData);
            _loadingHandles.Clear();
            _loadingViaAddressables = false;
        }

        public override void LoadEnd(LoadQueueData queueData)
        {
            base.LoadEnd(queueData);
            _loadingHandles.Clear();
            _loadingViaAddressables = false;
        }

        public override void UnloadStart(UnloadQueueData queueData)
        {
            base.UnloadStart(queueData);
            _unloadingViaAddressables = false;
        }

        public override void BeginLoadAsync(string sceneName, LoadSceneParameters parameters)
        {
            if (!IsAddressableScene(sceneName))
            {
                base.BeginLoadAsync(sceneName, parameters);
                return;
            }

            _loadingViaAddressables = true;
            AsyncOperationHandle<SceneInstance> handle =
                Addressables.LoadSceneAsync(MapSceneNameToAddress(sceneName), parameters, false);
            _loadingHandles.Add(handle);

            handle.Completed += op =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                    UnityEngine.Debug.LogError(
                        $"[AddressablesSceneProcessor] Failed to load '{sceneName}' (address '{MapSceneNameToAddress(sceneName)}') via Addressables.");
            };
        }

        public override void BeginUnloadAsync(UnityScene scene)
        {
            if (_sceneHandles.TryGetValue(scene, out AsyncOperationHandle<SceneInstance> handle))
            {
                _sceneHandles.Remove(scene);
                _unloadingViaAddressables = true;
                _currentUnloadHandle = Addressables.UnloadSceneAsync(handle);
            }
            else
            {
                _unloadingViaAddressables = false;
                base.BeginUnloadAsync(scene);
            }
        }

        public override bool IsPercentComplete() => GetPercentComplete() >= 0.9f;

        public override float GetPercentComplete()
        {
            if (_unloadingViaAddressables)
                return _currentUnloadHandle.IsValid() ? _currentUnloadHandle.PercentComplete : 1f;

            if (!_loadingViaAddressables)
                return base.GetPercentComplete();

            if (_loadingHandles.Count == 0) return 1f;

            float total = 0f;
            foreach (AsyncOperationHandle<SceneInstance> handle in _loadingHandles)
                total += handle.IsValid() ? handle.PercentComplete : 1f;

            return total / _loadingHandles.Count;
        }

        public override UnityScene GetLastLoadedScene()
        {
            if (!_loadingViaAddressables)
                return base.GetLastLoadedScene();

            for (int i = _loadingHandles.Count - 1; i >= 0; i--)
            {
                AsyncOperationHandle<SceneInstance> handle = _loadingHandles[i];
                if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded)
                    return handle.Result.Scene;
            }

            return default;
        }

        public override void AddLoadedScene(UnityScene scene)
        {
            base.AddLoadedScene(scene);

            if (!_loadingViaAddressables) return;

            for (int i = _loadingHandles.Count - 1; i >= 0; i--)
            {
                AsyncOperationHandle<SceneInstance> handle = _loadingHandles[i];
                if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result.Scene == scene)
                {
                    _sceneHandles[scene] = handle;
                    break;
                }
            }
        }

        public override void ActivateLoadedScenes()
        {
            if (!_loadingViaAddressables)
            {
                base.ActivateLoadedScenes();
                return;
            }

            foreach (AsyncOperationHandle<SceneInstance> handle in _loadingHandles)
            {
                if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded)
                    handle.Result.ActivateAsync();
            }
        }

        public override IEnumerator AsyncsIsDone()
        {
            if (!_loadingViaAddressables)
            {
                yield return base.AsyncsIsDone();
                yield break;
            }

            bool notDone;
            do
            {
                notDone = false;
                foreach (AsyncOperationHandle<SceneInstance> handle in _loadingHandles)
                {
                    if (!handle.IsValid()) continue;

                    // Still loading -> keep waiting.
                    if (!handle.IsDone)
                    {
                        notDone = true;
                        break;
                    }

                    // Loaded but activation not finished yet -> keep waiting. A Failed handle
                    // is already IsDone (error logged in BeginLoadAsync) and must be treated as
                    // done here, otherwise a missing/corrupt bundle would hang the load forever.
                    if (handle.Status == AsyncOperationStatus.Succeeded && !handle.Result.Scene.isLoaded)
                    {
                        notDone = true;
                        break;
                    }
                }

                yield return null;
            } while (notDone);
        }

        // Forgets the tracked handle when the scene is unloaded outside our BeginUnloadAsync
        // (e.g. a single-mode load of the offline Bootstrap scene tears Gameplay down via
        // Unity directly). Addressables already auto-releases the scene handle on scene
        // unload (default SceneReleaseMode.ReleaseSceneWhenSceneUnloaded), so we must NOT
        // release again here — releasing twice would over-decrement the ref count. We only
        // drop the stale map entry to avoid unloading an already-released handle later.
        private void OnSceneUnloadedExternally(UnityScene scene)
        {
            _sceneHandles.Remove(scene);
        }
    }
}
