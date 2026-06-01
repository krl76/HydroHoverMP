# Addressables Scene Loading + Headless Server Boot — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make HydroHoverMP a Bootstrap-only build — every non-Bootstrap scene loads through Addressables, including the FishNet networked `Gameplay` scene — and add a headless dedicated-server boot path.

**Architecture:** A custom `AddressablesSceneProcessor` (subclass of FishNet's `DefaultSceneProcessor`) swaps the load/unload mechanism for the networked scene to Addressables handles while reusing FishNet's scene bookkeeping. `Level` switches to Addressables additive. A `ServerBootstrapState` branches the boot flow so the dedicated server skips MainMenu UI, and the command-line server start moves out of `NetworkConnectionService.Initialize()` to fix init ordering. The game is multiplayer-only.

**Tech Stack:** Unity 6000.3.9f1, FishNet 4.7.2 (Tugboat), Addressables 2.8.1 (Local delivery), Zenject DI, NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-06-01-addressables-scene-loading-design.md`

---

## Conventions used by every task

- **Compile check:** in Unity, let scripts recompile after edits (focus the Editor or run an asset refresh via the unity MCP); confirm the Console shows **0 compile errors**. New `.cs` files require a Unity recompile before `dotnet build` sees them, so Unity recompile is the source of truth.
- **Run EditMode tests:** Unity → `Window → General → Test Runner → EditMode → Run All` (or run a single class via right-click). Equivalent: unity MCP "run tests" EditMode.
- **Test home:** `Assets/Tests/Editor/`, namespace `HydroHoverMP.Tests.Editor`, NUnit. Private/static methods are exercised either directly (if public) or via reflection helpers in `NetworkTestUtilities`.
- **Scene/prefab edits** are done by the editor automation in `FishNetProjectSetup` and applied **manually** via the `HydroHoverMP/Networking/Apply FishNet Setup` menu — never by hand-editing `.unity`/`.prefab` YAML (per `AGENTS.md`).

---

## File Structure

**New**
- `Assets/Scripts/Infrastructure/Services/Network/ServerEnvironment.cs` — detects dedicated-server mode.
- `Assets/Scripts/Features/Networking/AddressablesSceneProcessor.cs` — FishNet scene processor backed by Addressables.
- `Assets/Scripts/Core/States/Core/ServerBootstrapState.cs` — headless server boot state.
- `Assets/Tests/Editor/ServerEnvironmentTests.cs` — unit tests for detection.
- `Assets/Tests/Editor/AddressablesSceneProcessorTests.cs` — unit tests for name→address mapping.

**Changed**
- `Assets/Scripts/Features/Networking/NetworkLevelAdditiveLoader.cs` — Addressables additive load.
- `Assets/Scripts/Core/States/Core/BootstrapState.cs` — branch server vs client.
- `Assets/Scripts/Infrastructure/Services/Network/INetworkConnectionService.cs` — add `ResolveServerPort()`.
- `Assets/Scripts/Infrastructure/Services/Network/NetworkConnectionService.cs` — remove auto-start from `Initialize`, add `ResolveServerPort()`.
- `Assets/Scripts/Editor/Networking/FishNetProjectSetup.cs` — wire processor, Build List = Bootstrap only.
- `Assets/Tests/Editor/SceneAndPrefabSmokeTests.cs` — assert processor wired + Build List.
- `ProjectSettings/EditorBuildSettings.asset` — scene list reduced to Bootstrap (via Apply menu).
- `Assets/AddressableAssetsData/AddressableAssetSettings.asset` — build Addressables with player.

> **Note vs spec:** states are created via Zenject `IInstantiator.Instantiate<T>()` (see `StateFactory`/`GameStateMachine`), so `ServerBootstrapState` needs **no installer registration** — only constructor-injectable dependencies (already bound: `INetworkConnectionService`). The spec's "register in state factory" line is superseded.

---

## Task 1: `ServerEnvironment` — dedicated-server detection

**Files:**
- Create: `Assets/Scripts/Infrastructure/Services/Network/ServerEnvironment.cs`
- Test: `Assets/Tests/Editor/ServerEnvironmentTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/ServerEnvironmentTests.cs`:

```csharp
using Infrastructure.Services.Network;
using NUnit.Framework;

namespace HydroHoverMP.Tests.Editor
{
    public sealed class ServerEnvironmentTests
    {
        [Test]
        public void HasServerArgument_TrueForDedicatedServerFlag()
        {
            Assert.That(ServerEnvironment.HasServerArgument(new[] { "app.exe", "-dedicatedServer" }), Is.True);
        }

        [Test]
        public void HasServerArgument_TrueForServerOnlyFlagCaseInsensitive()
        {
            Assert.That(ServerEnvironment.HasServerArgument(new[] { "app.exe", "-SERVERONLY" }), Is.True);
        }

        [Test]
        public void HasServerArgument_FalseForClientArgs()
        {
            Assert.That(ServerEnvironment.HasServerArgument(new[] { "app.exe", "-port", "7770" }), Is.False);
        }

        [Test]
        public void HasServerArgument_FalseForNullOrEmpty()
        {
            Assert.That(ServerEnvironment.HasServerArgument(null), Is.False);
            Assert.That(ServerEnvironment.HasServerArgument(new string[0]), Is.False);
        }
    }
}
```

- [ ] **Step 2: Recompile + run, verify it fails**

Recompile in Unity. Run `ServerEnvironmentTests` (EditMode).
Expected: compile error / FAIL — `ServerEnvironment` does not exist.

- [ ] **Step 3: Implement `ServerEnvironment`**

Create `Assets/Scripts/Infrastructure/Services/Network/ServerEnvironment.cs`:

```csharp
using System;
using UnityEngine;

namespace Infrastructure.Services.Network
{
    /// <summary>
    /// Detects whether this process should run as a dedicated server.
    /// </summary>
    public static class ServerEnvironment
    {
        private const string DedicatedServerArg = "-dedicatedServer";
        private const string ServerOnlyArg = "-serverOnly";

        public static bool IsDedicatedServer
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return HasServerArgument(Environment.GetCommandLineArgs());
#endif
            }
        }

        /// <summary>
        /// Pure, testable check for a server launch flag in the supplied arguments.
        /// </summary>
        public static bool HasServerArgument(string[] args)
        {
            if (args == null) return false;

            foreach (string arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (string.Equals(arg, DedicatedServerArg, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, ServerOnlyArg, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

(The `using UnityEngine;` keeps room for future `Application` checks and matches the file's namespace neighbours; remove if the analyzer flags it as unused.)

- [ ] **Step 4: Recompile + run, verify it passes**

Recompile in Unity. Run `ServerEnvironmentTests`.
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Infrastructure/Services/Network/ServerEnvironment.cs Assets/Tests/Editor/ServerEnvironmentTests.cs Assets/Scripts/Infrastructure/Services/Network/ServerEnvironment.cs.meta Assets/Tests/Editor/ServerEnvironmentTests.cs.meta
git commit -m "feat(net): add ServerEnvironment dedicated-server detection"
```

(The `.meta` files are generated by Unity on recompile; include them if present.)

---

## Task 2: `AddressablesSceneProcessor`

**Files:**
- Create: `Assets/Scripts/Features/Networking/AddressablesSceneProcessor.cs`
- Test: `Assets/Tests/Editor/AddressablesSceneProcessorTests.cs`

- [ ] **Step 1: Write the failing test (pure mapping)**

Create `Assets/Tests/Editor/AddressablesSceneProcessorTests.cs`:

```csharp
using Features.Networking;
using NUnit.Framework;

namespace HydroHoverMP.Tests.Editor
{
    public sealed class AddressablesSceneProcessorTests
    {
        [Test]
        public void MapSceneNameToAddress_PrefixesWithSceneFolder()
        {
            Assert.That(AddressablesSceneProcessor.MapSceneNameToAddress("Gameplay"), Is.EqualTo("Scene/Gameplay"));
            Assert.That(AddressablesSceneProcessor.MapSceneNameToAddress("Level"), Is.EqualTo("Scene/Level"));
        }

        [Test]
        public void IsAddressableScene_TrueForKnownScenes()
        {
            Assert.That(AddressablesSceneProcessor.IsAddressableScene("Gameplay"), Is.True);
            Assert.That(AddressablesSceneProcessor.IsAddressableScene("Level"), Is.True);
            Assert.That(AddressablesSceneProcessor.IsAddressableScene("MainMenu"), Is.True);
        }

        [Test]
        public void IsAddressableScene_FalseForBootstrap()
        {
            Assert.That(AddressablesSceneProcessor.IsAddressableScene("Bootstrap"), Is.False);
        }
    }
}
```

- [ ] **Step 2: Recompile + run, verify it fails**

Expected: compile error / FAIL — `AddressablesSceneProcessor` does not exist.

- [ ] **Step 3: Implement the processor**

Create `Assets/Scripts/Features/Networking/AddressablesSceneProcessor.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

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
        private static readonly HashSet<string> AddressableSceneNames = new() { "Gameplay", "Level", "MainMenu" };

        private readonly List<AsyncOperationHandle<SceneInstance>> _loadingHandles = new();
        private readonly Dictionary<Scene, AsyncOperationHandle<SceneInstance>> _sceneHandles = new();
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
                    SceneManager.NetworkManager.LogError(
                        $"[AddressablesSceneProcessor] Failed to load '{sceneName}' (address '{MapSceneNameToAddress(sceneName)}') via Addressables.");
            };
        }

        public override void BeginUnloadAsync(Scene scene)
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

        public override Scene GetLastLoadedScene()
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

        public override void AddLoadedScene(Scene scene)
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
                    if (handle.Status != AsyncOperationStatus.Succeeded || !handle.Result.Scene.isLoaded)
                    {
                        notDone = true;
                        break;
                    }
                }

                yield return null;
            } while (notDone);
        }

        // Releases an Addressables handle when the scene is unloaded outside our
        // BeginUnloadAsync (e.g. a single-mode load of the offline Bootstrap scene
        // tears Gameplay down via Unity directly). Prevents ref-count leaks.
        private void OnSceneUnloadedExternally(Scene scene)
        {
            if (!_sceneHandles.TryGetValue(scene, out AsyncOperationHandle<SceneInstance> handle)) return;

            _sceneHandles.Remove(scene);
            if (handle.IsValid())
                Addressables.Release(handle);
        }
    }
}
```

- [ ] **Step 4: Recompile + run, verify mapping tests pass**

Run `AddressablesSceneProcessorTests`.
Expected: PASS (3 tests). Confirm Console has 0 compile errors (the full class compiles).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Features/Networking/AddressablesSceneProcessor.cs Assets/Tests/Editor/AddressablesSceneProcessorTests.cs Assets/Scripts/Features/Networking/AddressablesSceneProcessor.cs.meta Assets/Tests/Editor/AddressablesSceneProcessorTests.cs.meta
git commit -m "feat(net): add AddressablesSceneProcessor for networked scene loading"
```

> Runtime load/unload behaviour is verified in Task 7 (two-process run) — it cannot be unit-tested without built Addressables bundles and a live FishNet session.

---

## Task 3: `NetworkLevelAdditiveLoader` → Addressables

**Files:**
- Modify: `Assets/Scripts/Features/Networking/NetworkLevelAdditiveLoader.cs` (full rewrite)

- [ ] **Step 1: Rewrite the loader to use Addressables**

Replace the entire contents of `Assets/Scripts/Features/Networking/NetworkLevelAdditiveLoader.cs`:

```csharp
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

            yield return handle;

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
```

- [ ] **Step 2: Recompile, verify 0 errors**

Recompile in Unity. Expected: Console shows 0 compile errors.

- [ ] **Step 3: Re-run the existing scene smoke tests (regression)**

Run `SceneAndPrefabSmokeTests` (EditMode).
Expected: PASS — the Gameplay "Network Session" object still has `NetworkLevelAdditiveLoader` (component type unchanged).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Features/Networking/NetworkLevelAdditiveLoader.cs
git commit -m "feat(net): load Level additively via Addressables with handle release"
```

> The serialized `_levelSceneAddress` defaults to `Scene/Level`. After Task 5's Apply run, confirm in the Inspector on the Gameplay "Network Session" object that the value is `Scene/Level`.

---

## Task 4: Headless boot — service refactor + `ServerBootstrapState` + branch

**Files:**
- Modify: `Assets/Scripts/Infrastructure/Services/Network/INetworkConnectionService.cs`
- Modify: `Assets/Scripts/Infrastructure/Services/Network/NetworkConnectionService.cs`
- Create: `Assets/Scripts/Core/States/Core/ServerBootstrapState.cs`
- Modify: `Assets/Scripts/Core/States/Core/BootstrapState.cs` (full rewrite)

- [ ] **Step 1: Add `ResolveServerPort()` to the interface**

In `Assets/Scripts/Infrastructure/Services/Network/INetworkConnectionService.cs`, add this method to the interface (next to `StartServer`):

```csharp
        ushort ResolveServerPort();
```

- [ ] **Step 2: Implement `ResolveServerPort()` and remove auto-start from `Initialize`**

In `Assets/Scripts/Infrastructure/Services/Network/NetworkConnectionService.cs`:

(a) Replace the `Initialize` body so it no longer auto-starts:

```csharp
        public void Initialize()
        {
            TryBindNetworkManager();
        }
```

(b) Delete the now-unused `TryStartServerFromCommandLine()` method (the whole method, lines beginning `private void TryStartServerFromCommandLine()`). Keep `TryGetCommandLineServerPort`, `TryGetCommandLineServerPortWithDefault`, `TryReadPortArg`, `TryReadInlinePort`, `TryParseCommandLinePort` — they are reused below and by existing tests.

(c) Add this public method (place it just above `TryGetCommandLineServerPort`):

```csharp
        public ushort ResolveServerPort()
        {
            string[] args = Environment.GetCommandLineArgs();
            ushort port = ConfiguredDefaultPort;
            if (args == null) return port;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrWhiteSpace(arg)) continue;

                if (TryReadPortArg(args, i, arg, port, out ushort parsedPort, out bool consumedNext, out _))
                {
                    port = parsedPort;
                    if (consumedNext) i++;
                }
            }

            return port;
        }
```

- [ ] **Step 3: Recompile, verify 0 errors + existing tests green**

Recompile. Run `NetworkLogicTests` (EditMode).
Expected: PASS — the reflection helpers (`TryParseCommandLinePort`, `TryGetCommandLineServerPort`) still resolve; no test referenced `TryStartServerFromCommandLine`.

- [ ] **Step 4: Create `ServerBootstrapState`**

Create `Assets/Scripts/Core/States/Core/ServerBootstrapState.cs`:

```csharp
using Core.States.Base;
using Infrastructure.Services.Network;
using UnityEngine;

namespace Core.States.Core
{
    /// <summary>
    /// Boot state for the dedicated server: starts the FishNet server (which loads
    /// the Gameplay online scene via DefaultScene) without loading any client UI.
    /// </summary>
    public class ServerBootstrapState : IState
    {
        private readonly INetworkConnectionService _connectionService;

        public ServerBootstrapState(INetworkConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        public void Enter()
        {
            ushort port = _connectionService.ResolveServerPort();
            Debug.Log($"[ServerBootstrapState] Starting dedicated server on port {port}.");

            if (!_connectionService.StartServer(port))
                Debug.LogError($"[ServerBootstrapState] Failed to start dedicated server on port {port}. Check Tugboat setup and that the port is free.");
        }

        public void Exit()
        {
        }
    }
}
```

- [ ] **Step 5: Branch `BootstrapState`**

Replace the entire contents of `Assets/Scripts/Core/States/Core/BootstrapState.cs`:

```csharp
using Core.States.Base;
using Core.States.MainMenu;
using Infrastructure.Services.Network;

namespace Core.States.Core
{
    public class BootstrapState : IState
    {
        private readonly GameStateMachine _stateMachine;

        public BootstrapState(GameStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
        }

        public void Enter()
        {
            if (ServerEnvironment.IsDedicatedServer)
                _stateMachine.Enter<ServerBootstrapState>();
            else
                _stateMachine.Enter<MainMenuState>();
        }

        public void Exit()
        {
        }
    }
}
```

- [ ] **Step 6: Recompile, verify 0 errors**

Recompile. Expected: 0 compile errors. (`ServerBootstrapState` resolves through Zenject `IInstantiator`; `INetworkConnectionService` is already bound in `GlobalInstaller`.)

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Infrastructure/Services/Network/INetworkConnectionService.cs Assets/Scripts/Infrastructure/Services/Network/NetworkConnectionService.cs Assets/Scripts/Core/States/Core/ServerBootstrapState.cs Assets/Scripts/Core/States/Core/BootstrapState.cs Assets/Scripts/Core/States/Core/ServerBootstrapState.cs.meta
git commit -m "feat(boot): headless ServerBootstrapState + move server start out of Initialize"
```

---

## Task 5: Editor setup — wire processor, Build List = Bootstrap, smoke tests

**Files:**
- Modify: `Assets/Scripts/Editor/Networking/FishNetProjectSetup.cs`
- Modify: `Assets/Tests/Editor/SceneAndPrefabSmokeTests.cs`

- [ ] **Step 1: Wire the processor in `ConfigureBootstrapScene`**

In `Assets/Scripts/Editor/Networking/FishNetProjectSetup.cs`, add the namespace import at the top (with the other `using`s):

```csharp
using Features.Networking;
```

(`Features.Networking` is already imported in this file — confirm it is present; if so, skip.) Then, inside `ConfigureBootstrapScene`, after the `PlayerSpawner` block and before `EditorSceneManager.MarkSceneDirty(scene);`, add:

```csharp
            AddressablesSceneProcessor sceneProcessor = AddIfMissing<AddressablesSceneProcessor>(networkObject);
            FishNet.Managing.Scened.SceneManager fishnetSceneManager = AddIfMissing<FishNet.Managing.Scened.SceneManager>(networkObject);
            SerializedObject serializedSceneManager = new(fishnetSceneManager);
            SerializedProperty processorProperty = serializedSceneManager.FindProperty("_sceneProcessor");
            processorProperty.objectReferenceValue = sceneProcessor;
            serializedSceneManager.ApplyModifiedPropertiesWithoutUndo();
```

- [ ] **Step 2: Reduce the Build List to Bootstrap only**

In the same file, replace the whole `ConfigureBuildSettings` method body:

```csharp
        private static void ConfigureBuildSettings()
        {
            // Bootstrap-only build: every other scene loads via Addressables.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true)
            };
        }
```

- [ ] **Step 3: Add a setup confirmation log**

In `Apply()`, change the final log line to surface the Inspector checklist:

```csharp
            Debug.Log("[HydroHoverMP] FishNet setup applied. Verify in Inspector: NetworkManager > SceneManager._sceneProcessor = AddressablesSceneProcessor; Build Settings scene list = Bootstrap only. Then test Host + Client.");
```

- [ ] **Step 4: Recompile, verify 0 errors**

Recompile (editor scripts compile into Assembly-CSharp-Editor). Expected: 0 errors.

- [ ] **Step 5: Apply the setup in Unity (manual)**

Run menu `HydroHoverMP/Networking/Apply FishNet Setup`.
Then in the Inspector confirm on `FishNet NetworkManager` (Bootstrap scene): a `SceneManager` + `AddressablesSceneProcessor` component exist and `SceneManager`'s "Scene Processor" field references the `AddressablesSceneProcessor`. Confirm `File → Build Settings` (or `EditorBuildSettings`) lists only `Bootstrap`.

- [ ] **Step 6: Add smoke tests for the wiring**

In `Assets/Tests/Editor/SceneAndPrefabSmokeTests.cs`, add these usings if missing:

```csharp
using System.Linq;
using FishNet.Managing.Scened;
using Features.Networking;
```

Then add two tests inside the class:

```csharp
        [Test]
        public void BootstrapScene_NetworkManagerUsesAddressablesSceneProcessor()
        {
            Scene scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject networkObject = scene.GetRootGameObjects()
                    .FirstOrDefault(root => root.name == "FishNet NetworkManager");

                Assert.That(networkObject, Is.Not.Null, "Bootstrap should contain the FishNet network root object.");
                Assert.That(networkObject.GetComponent<AddressablesSceneProcessor>(), Is.Not.Null,
                    "NetworkManager should carry the AddressablesSceneProcessor component.");

                SceneManager sceneManager = networkObject.GetComponent<SceneManager>();
                Assert.That(sceneManager, Is.Not.Null, "NetworkManager should have a FishNet SceneManager.");
                Assert.That(sceneManager.GetSceneProcessor(), Is.InstanceOf<AddressablesSceneProcessor>(),
                    "SceneManager._sceneProcessor must be wired to AddressablesSceneProcessor.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void BuildSettings_ContainOnlyBootstrapScene()
        {
            string[] enabledScenePaths = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            Assert.That(enabledScenePaths, Is.EqualTo(new[] { "Assets/Scenes/Bootstrap.unity" }),
                "Only Bootstrap should be in the Build Settings scene list; other scenes load via Addressables.");
        }
```

Add the import for `UnityEditor` build settings if not present (the file already uses `UnityEditor`).

- [ ] **Step 7: Recompile + run, verify smoke tests pass**

Run `SceneAndPrefabSmokeTests` (EditMode).
Expected: PASS — including the two new tests (requires Step 5's Apply to have run).

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Editor/Networking/FishNetProjectSetup.cs Assets/Tests/Editor/SceneAndPrefabSmokeTests.cs Assets/Scenes/Bootstrap.unity ProjectSettings/EditorBuildSettings.asset
git commit -m "feat(editor): wire AddressablesSceneProcessor + Bootstrap-only build list"
```

(`Bootstrap.unity` and `EditorBuildSettings.asset` change as a result of running Apply in Step 5.)

---

## Task 6: Build Addressables content with the player

**Files:**
- Modify: `Assets/AddressableAssetsData/AddressableAssetSettings.asset`

- [ ] **Step 1: Set "Build Addressables on Player Build" via the Inspector (manual)**

In Unity: `Window → Asset Management → Addressables → Settings`. Set **Build Addressables on Player Build** to **Build Addressables content on Player Build**. (This flips `m_BuildAddressablesWithPlayerBuild` from `0` to `1`.)

- [ ] **Step 2: Verify the setting persisted**

Run:
```bash
grep -n "m_BuildAddressablesWithPlayerBuild" Assets/AddressableAssetsData/AddressableAssetSettings.asset
```
Expected: `m_BuildAddressablesWithPlayerBuild: 1`

- [ ] **Step 3: Commit**

```bash
git add Assets/AddressableAssetsData/AddressableAssetSettings.asset
git commit -m "chore(addressables): build content with player builds"
```

---

## Task 7: Integration verification (manual, two processes)

No code; this is the runtime UAT that the unit/smoke tests cannot cover. Record results; do not mark complete until all pass.

- [ ] **Step 1: Build content + players**

In Unity: `Addressables → Groups → Build → New Build → Default Build Script`. Then build a normal client player and a Linux Server (Dedicated Server) build (Build Profiles).

- [ ] **Step 2: Start the server**

Run the server build: `./HydroHoverMP_Server -dedicatedServer -port 7770` (Linux) / `.exe` equivalent.
Expected in the server log: `[ServerBootstrapState] Starting dedicated server on port 7770.`, FishNet loads `Scene/Gameplay` through the processor, `NetworkLevelAdditiveLoader` loads `Scene/Level`, and **no MainMenu** scene/window is created.

- [ ] **Step 3: Connect a client**

Launch the client → MainMenu → Client → Connect (address from `GlobalInstaller` = server IP, port 7770).
Expected: spawn, camera follows the local owner, nickname / HP / score / checkpoint / ready visible to all.

- [ ] **Step 4: Race + checkpoints + leaderboard**

Run a race. Expected: server-side `ServerPassCheckpoint` fires (server has Level colliders); on finish the server writes `dedicated_leaderboard.json` in its `persistentDataPath`; in-session leaderboard shows records on clients.

- [ ] **Step 5: Disconnect resilience**

Disconnect a client during lobby / race / results.
Expected: the session keeps running for remaining players; no exceptions.

- [ ] **Step 6: Console + performance gates (AGENTS.md)**

Expected: no critical errors in server or client logs; FPS ≥ 30 in a normal two-player test.

- [ ] **Step 7: Record verification outcome**

Append a short results note to `docs/superpowers/specs/2026-06-01-addressables-scene-loading-design.md` (or a sibling `…-verification.md`) and commit:

```bash
git add docs/superpowers/
git commit -m "docs: record Addressables scene loading verification results"
```

---

## Self-review notes

- **Spec coverage:** processor (Task 2), Level loader (Task 3), headless boot + init-order (Task 4), Build List + wiring (Task 5), build-with-player (Task 6), verification incl. server-side Level/checkpoints (Task 7). Out-of-scope items from the spec are not tasked, as intended.
- **Type consistency:** `MapSceneNameToAddress`/`IsAddressableScene` (Task 2) match their test (Task 2) and are not redefined elsewhere. `ResolveServerPort()` defined on interface (Task 4 Step 1) and impl (Step 2), consumed by `ServerBootstrapState` (Step 4). `GetSceneProcessor()` (used in Task 5 test) is the real FishNet API verified in `SceneManager.cs:260`.
- **Placeholders:** none — every code step contains complete code.
- **Unity reality:** runtime Addressables/FishNet behaviour is verified manually (Task 7), not faked as unit tests; pure logic (detection, mapping) and editor wiring (Build List, processor reference) are automated.
