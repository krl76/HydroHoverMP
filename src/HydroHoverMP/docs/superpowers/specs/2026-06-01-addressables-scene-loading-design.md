# Addressables Scene Loading + Headless Server Boot — Design

**Date:** 2026-06-01
**Status:** Approved (pending spec review)
**Scope owner:** krl76

## 1. Overview

Move HydroHoverMP to a "Bootstrap-only" build: the player build ships with a single
built-in scene (`Bootstrap`), and every other scene (`MainMenu`, `Gameplay`, `Level`)
is loaded at runtime through Addressables. The blocker is that FishNet's networked
scene system loads the online scene (`Gameplay`) **by name** through Unity's
`SceneManager`, which requires the scene to be in the Build Settings list. We resolve
this with a custom FishNet `SceneProcessor` backed by Addressables.

In the same change we add a dedicated-server headless boot path (skip the client
MainMenu UI on the server) and fix the command-line server-start initialization order.

The game is **multiplayer-only** (decided during design); the legacy single-player
loading branch is treated as dead code and is out of scope to remove here.

## 2. Goals / Non-goals

### Goals
- Player build contains only `Bootstrap` in the Build Settings scene list.
- `Gameplay` (FishNet online scene) loads via Addressables through a custom
  `SceneProcessor`, on both server and clients, with parity.
- `Level` loads via Addressables (additive) on every peer, including the headless
  server, preserving track colliders / checkpoint triggers / physics.
- Dedicated server boots headless without loading the MainMenu client UI scene, while
  still loading `Gameplay` and running all server-authoritative logic.
- Command-line server auto-start no longer races NetworkManager creation.

### Non-goals (out of scope — separate tasks)
- Showing the leaderboard from the cold MainMenu (disconnected). Pre-existing
  limitation: dedicated leaderboard records are only visible while connected to a
  session. The leaderboard lives in MainMenu but has its own prefab; addressed later.
- Redirecting the Restart buttons (`FinishScreen` / `PauseWindow`) from the dead
  single-player `LoadLevelState(GAMEPLAY)` path to the server restart RPC.
- A stripped/server-only variant of the `Level` scene to cut headless load cost.
- Removing the single-player branch in `LoadLevelState`.

## 3. Background — current state

Two scene-loading systems coexist today:

| Scene | Loaded by | Mechanism |
|---|---|---|
| Bootstrap | entry scene + FishNet offline scene | Unity `SceneManager` by name (Build List) |
| Gameplay | FishNet `DefaultScene.SetOnlineScene("Gameplay")` | FishNet networked `SceneManager` → `SceneProcessor` |
| Level | `NetworkLevelAdditiveLoader` (local, per-peer, additive) | Unity `SceneManager.LoadSceneAsync("Level")` by name |
| MainMenu | `MainMenuState` (client only) | Addressables `Scene/MainMenu` (Single) |

Verified facts that shape the design:
- FishNet 4.7.2 `SceneManager` exposes `[SerializeField] SceneProcessorBase _sceneProcessor`
  with `SetSceneProcessor()`. `Awake` only auto-adds `DefaultSceneProcessor` when the
  field is null (`SceneManager.cs:416`). So a custom processor can be Inspector-wired —
  no runtime registration race.
- `DefaultSceneProcessor` loads via `UnitySceneManager.LoadSceneAsync(sceneName, params)`
  with `allowSceneActivation = false` (deferred activation). Its bookkeeping fields
  (`Scenes`, `LoadingAsyncOperations`, `CurrentAsyncOperation`) are `protected`;
  `_lastLoadedScene` is `private`.
- FishNet's `DefaultScene` loads the **online** scene through the networked
  `SceneManager` (`LoadGlobalScenes`/`LoadConnectionScenes`, `DefaultScene.cs:177`) → the
  processor. The **offline** scene (`Bootstrap`) is loaded by `UnitySceneManager` directly
  (`DefaultScene.cs:226`), bypassing the processor. ⇒ the processor only ever handles
  `Gameplay`, which is always Addressable. No general non-addressable fallback is needed
  on the happy path (a defensive fallback is still included).
- Addressables 2.8.1 supports `Addressables.LoadSceneAsync(key, LoadSceneParameters,
  activateOnLoad: false)` returning `AsyncOperationHandle<SceneInstance>`, plus
  `SceneInstance.ActivateAsync()` and `Addressables.UnloadSceneAsync(handle)` — matching
  FishNet's deferred-activation pattern.
- Addressables groups use **Local** delivery (`Local.BuildPath`/`Local.LoadPath`):
  bundles are packed into StreamingAssets and ship with the build. No CDN required on a
  VPS.
- Server runs physics in `-batchmode -nographics` (FixedUpdate/colliders run; no render),
  so server-side trigger detection works when `Level` is loaded.
- Checkpoint routing (`NetworkPlayerData.cs:69`): the physical `OnTriggerEnter` in
  `CheckpointTrigger` calls `TryPassCheckpoint`, which runs `ServerPassCheckpoint`
  directly when `IsServerInitialized`, or sends `PassCheckpointServerRpc` when `IsOwner`.
  Detection is dual-covered (server physics + owner-client RPC); the server validates the
  expected index, so duplicates are idempotent. The server therefore needs `Level` loaded
  (it has it today and will continue to).

## 4. Design

### 4.1 Target scene loading

| Scene | Loader | Source |
|---|---|---|
| Bootstrap | entry + FishNet offline (Unity direct) | Build List (the only built-in scene) |
| Gameplay | FishNet online → `AddressablesSceneProcessor` | Addressables `Scene/Gameplay` |
| Level | `NetworkLevelAdditiveLoader` (per-peer additive) | Addressables `Scene/Level` |
| MainMenu | `MainMenuState` (client only) | Addressables `Scene/MainMenu` |

### 4.2 `AddressablesSceneProcessor` (new)

`Features/Networking/AddressablesSceneProcessor.cs`, subclass of
`FishNet.Managing.Scened.DefaultSceneProcessor` (chosen over a full custom processor to
reuse FishNet's scene bookkeeping and reduce replication-integration risk).

State:
- `List<AsyncOperationHandle<SceneInstance>> _loadingHandles` — current load batch.
- `Dictionary<Scene, AsyncOperationHandle<SceneInstance>> _sceneHandles` — for unload.
- `Scene _lastLoaded` — shadows the base `private _lastLoadedScene`.

Overrides:
- `BeginLoadAsync(name, params)`: map `name → "Scene/" + name`. If the address resolves in
  the Addressables catalog → `Addressables.LoadSceneAsync(addr, params, activateOnLoad:
  false)`, push the handle to `_loadingHandles`. Otherwise (defensive) →
  `base.BeginLoadAsync(name, params)` (Unity by name). In practice only `Gameplay`
  arrives here.
- `GetPercentComplete` / `IsPercentComplete`: progress across `_loadingHandles`
  (Addressables with `activateOnLoad:false` plateaus near 0.9 until activation — same
  semantics FishNet expects from `allowSceneActivation=false`).
- `GetLastLoadedScene`: `_lastLoaded` (set from the latest handle's `Result.Scene`).
- `ActivateLoadedScenes`: for each handle `handle.Result.ActivateAsync()`.
- `AsyncsIsDone`: yield until every handle `IsDone`.
- `AddLoadedScene(scene)`: `base.AddLoadedScene(scene)` (populate base `Scenes`) +
  `_sceneHandles[scene] = <latest handle>`.
- `BeginUnloadAsync(scene)`: if `scene` in `_sceneHandles` →
  `Addressables.UnloadSceneAsync(handle)` (decrements ref-count, no leak) + remove from
  map; else `base.BeginUnloadAsync(scene)`.
- `LoadStart`/`LoadEnd`: call base, then clear `_loadingHandles` for a fresh batch.

Inherited unchanged: `GetLoadedScenes`, `GetMovedObjectsScene`, `GetFallbackActiveScene`,
`FindOrCreateScene`, `UnloadStart`.

Registration: `FishNetProjectSetup` adds the component to the `FishNet NetworkManager`
GameObject and assigns it to `SceneManager._sceneProcessor` via `SerializedObject`.

Server/client parity: both peers route `"Gameplay"` through the same processor → same
address. Requirement: identical Addressables catalog in both builds (built from the same
content).

### 4.3 `NetworkLevelAdditiveLoader` (change)

- Load via `Addressables.LoadSceneAsync("Scene/Level", LoadSceneMode.Additive)`; store the
  `AsyncOperationHandle<SceneInstance>`.
- Guard double-load: skip if the handle is already valid or
  `SceneManager.GetSceneByName("Level").isLoaded` (scene name `Level`, not the address).
- `OnDestroy` (Gameplay teardown): `Addressables.UnloadSceneAsync(handle)` to avoid
  ref-count leaks across sessions.
- Replace the `_levelSceneName` field with a serialized Addressable address field
  (default `Scene/Level`), matching the catalog.
- On a non-`Succeeded` handle: log an error (a server without `Level` = no track
  colliders — must be visible in VPS logs).
- Runs on every peer (server + clients), loading each peer's own local `Level` (not
  networked) — unchanged behavior, new mechanism.

### 4.4 Headless server boot + init-order fix

- New `Infrastructure/Services/Network/ServerEnvironment.cs`: static
  `IsDedicatedServer` = `Application.isBatchMode || args contain -dedicatedServer /
  -serverOnly`. Reuses the argument-parsing already present in `NetworkConnectionService`.
- `Core/States/Core/BootstrapState.cs` (change): branch in `Enter()` —
  `ServerEnvironment.IsDedicatedServer` → `Enter<ServerBootstrapState>()`, else
  `Enter<MainMenuState>()`. Clients are unaffected.
- New `Core/States/Core/ServerBootstrapState.cs`: `Enter()` calls
  `_connectionService.StartServer(port)` (port from command line / `DedicatedServer`
  config). FishNet `DefaultScene` then loads `Gameplay` (→ processor → Addressables). No
  UI scene, no MainMenu window.
- Init-order fix: move the command-line server auto-start **out of**
  `NetworkConnectionService.Initialize()` and **into** `ServerBootstrapState`, which runs
  from `Bootstrap.Start()` after `NetworkBootstrapper.EnsureRuntimeObjects()` — so the
  NetworkManager is guaranteed to exist. The port argument-parsing helpers stay in
  `NetworkConnectionService` (reused); only the auto-`StartServer` call leaves
  `Initialize`.
- DI: register `ServerBootstrapState` in the state factory (`StateFactory` /
  `GlobalInstaller`), like the other states.

### 4.5 Build List + Addressables config

- `EditorBuildSettings.scenes`: only `Bootstrap.unity` (index 0). Remove `MainMenu`,
  `Gameplay`, `Level`.
- `MainMenu` / `Gameplay` / `Level` stay Addressable (`Scenes` / `Default Local Group`),
  Local delivery. `Bootstrap` is not Addressable.
- `AddressableAssetSettings`: set `m_BuildAddressablesWithPlayerBuild: 1` ("Build
  Addressables content on Player Build") so bundles rebuild with every player build
  (required for both server and client builds).

### 4.6 `FishNetProjectSetup` (editor, change)

- `ConfigureBootstrapScene`: add `AddressablesSceneProcessor` to `FishNet NetworkManager`
  and wire `SceneManager._sceneProcessor` via `SerializedObject`.
- `ConfigureBuildSettings`: set the scene list to only `Bootstrap` (replacing the current
  "add four scenes" logic).
- Emit a `Debug.Log` Inspector checklist (per AGENTS.md: do not blindly guess serialized
  component data — surface a manual wiring checklist).

## 5. Error handling

- Processor `BeginLoadAsync` failure (missing catalog entry / corrupt bundle):
  `NetworkManager.LogError` + defensive fallback to `base.BeginLoadAsync` so the load
  does not hang silently.
- `ServerBootstrapState`: if `StartServer` returns false, `Debug.LogError` with the
  reason (port in use / Tugboat missing) and do not leave a dangling state.
- `NetworkLevelAdditiveLoader`: non-`Succeeded` handle → error log (visible in VPS logs).

## 6. Testing / verification (manual, Unity, two processes)

1. Run `HydroHoverMP/Networking/Apply FishNet Setup`. Inspector check: `_sceneProcessor`
   assigned on the NetworkManager; Build List = only `Bootstrap`.
2. Build Addressables + a client build + a Linux Server build.
3. Editor client + server build: server starts (`-dedicatedServer -port 7770`); logs show
   `Scene/Gameplay` loaded via the processor and `Scene/Level` via Addressables; no
   MainMenu.
4. Client connects → spawn, camera follows owner, nickname / HP / checkpoints / finish,
   in-session leaderboard.
5. Server checkpoint trigger fires (log `ServerPassCheckpoint`); finish writes
   `dedicated_leaderboard.json` on the server.
6. Client disconnect during lobby / race / results does not break the session.
7. Console free of critical errors; FPS ≥ 30 in a normal two-player test (AGENTS.md
   gates).

## 7. File manifest

**New**
- `Features/Networking/AddressablesSceneProcessor.cs`
- `Core/States/Core/ServerBootstrapState.cs`
- `Infrastructure/Services/Network/ServerEnvironment.cs`

**Changed**
- `Features/Networking/NetworkLevelAdditiveLoader.cs`
- `Core/States/Core/BootstrapState.cs`
- `Infrastructure/Services/Network/NetworkConnectionService.cs`
- `Editor/Networking/FishNetProjectSetup.cs`
- `Infrastructure/Installers/GlobalInstaller.cs` / `Infrastructure/Factories/StateFactory.cs`
  (register `ServerBootstrapState`)

**Config**
- `ProjectSettings/EditorBuildSettings.asset` (scene list → Bootstrap only)
- `Assets/AddressableAssetsData/AddressableAssetSettings.asset`
  (`m_BuildAddressablesWithPlayerBuild: 1`)

## 8. Open questions

None. (Deferred items are listed under Non-goals.)
