# Dedicated Leaderboard Menu Query Implementation Plan

> **⚠️ SUPERSEDED (2026-06-11):** This query-connection approach was implemented, then reverted. Connecting from the menu pulls FishNet's connect-time machinery onto the menu client (auto-spawned player + the server's **global Gameplay scene** pushed at auth). The menu leaderboard is now served from a **client-side cache** in `LeaderboardService` instead (see commit `879b76a` and memory `leaderboard-menu-uses-client-cache`). Kept for historical context only — do not implement.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the main-menu Leaderboard window show the dedicated server's live, authoritative records by performing a short-lived "leaderboard-only" query connection that fetches records over a FishNet Broadcast — without the querying client becoming a spawned, ready lobby participant.

**Architecture:** A leaderboard-only client connects to the dedicated server, sends a `LeaderboardQueryBroadcast`, and the server replies to that connection with a `LeaderboardResultBroadcast` carrying the top records — no spawned NetworkObject required. The server marks query connections as "leaderboard-only", despawns/skips any player object for them (via `PlayerSpawner.OnSpawned` + the query handler), and excludes them from lobby/countdown counting. The client orchestration lives in `NetworkConnectionService` (single owner of the shared `NetworkManager`); `LeaderboardService` bridges it to the UI via an event; `LeaderboardWindow` shows Loading/Empty/Error/Results states and refreshes when data arrives.

**Tech Stack:** Unity, FishNet 4.7.2 (Broadcasts, PlayerSpawner), Zenject, Newtonsoft.Json, NUnit EditMode tests.

---

## Why this approach (spawn-gating technique)

FishNet's built-in `PlayerSpawner` (on the Bootstrap `FishNet NetworkManager`, `_addToDefaultScene: 1`) auto-spawns the player prefab for **every** connection on the server's `SceneManager.OnClientLoadedStartScenes` event (`PlayerSpawner.cs:93-115`). Once spawned, `NetworkHoverOwnerBridge.ApplyOwnershipState` auto-readies the owner (`NetworkHoverOwnerBridge.cs:126-127`), so a naive "connect to read the leaderboard" turns the menu viewer into a ready lobby participant — which can trigger/block the countdown for real players.

**Chosen technique (lowest blast radius to the working gameplay path):** do **not** replace `PlayerSpawner` and do **not** edit `Bootstrap.unity`. Instead, the server subscribes to `PlayerSpawner.OnSpawned` and, if the spawned player's owner is a known leaderboard-only connection, immediately despawns it. The query handler also marks the connection and despawns any already-spawned player, and excludes the connection from `ConnectedPlayers` counting. Normal players' spawn path is completely unchanged.

**Alternatives considered (NOT used):**
- *Replace PlayerSpawner with a gated spawner* — requires editing `Bootstrap.unity` (swap component + re-wire prefab/`_addToDefaultScene`); higher risk of breaking the working spawn path.
- *Explicit-join (spawn only on request)* — deterministic but changes the spawn timing for every normal player; highest regression risk on netcode that was recently stabilized.

---

## File Structure

**New files:**
- `Assets/Scripts/Features/Networking/LeaderboardBroadcasts.cs` — `LeaderboardQueryBroadcast` + `LeaderboardResultBroadcast` structs and a static `LeaderboardRecordMapper` (pure record/array mapping + top-N).
- `Assets/Tests/Editor/LeaderboardQueryTests.cs` — EditMode unit tests for the pure mapping/selection + `LeaderboardService` offline-cache behavior.

**Modified files:**
- `Assets/Scripts/Features/Networking/NetworkSessionController.cs` — register the query broadcast handler, maintain the leaderboard-only connection set, despawn/skip their player, reply with records, exclude them from `ConnectedPlayers`.
- `Assets/Scripts/Infrastructure/Services/Network/INetworkConnectionService.cs` — add the query primitive + result event.
- `Assets/Scripts/Infrastructure/Services/Network/NetworkConnectionService.cs` — implement the short-lived query connection with status suppression and cleanup.
- `Assets/Scripts/Infrastructure/Services/Leaderboard/ILeaderboardService.cs` — add `OnDedicatedRecordsUpdated` event + `CancelDedicatedRecordsRequest()`.
- `Assets/Scripts/Infrastructure/Services/Leaderboard/LeaderboardService.cs` — inject `INetworkConnectionService`, kick off the query when offline, store results, raise the event, return stored results from `GetTopRecords`.
- `Assets/Scripts/UI/Leaderboard/LeaderboardWindow.cs` — subscribe to the update event, show Loading/Empty/Error/Results, time out unreachable servers, unsubscribe on close.
- `Assets/Scripts/Infrastructure/Installers/GlobalInstaller.cs` — no new binding needed (verify `LeaderboardService` resolves `INetworkConnectionService`); see Task 6.

---

## Data shapes (used across tasks — defined in Task 1)

```csharp
// Client -> server: "give me the top Count records; treat my connection as leaderboard-only".
public struct LeaderboardQueryBroadcast : IBroadcast
{
    public int Count;
}

// Server -> client: the requested records (may be empty).
public struct LeaderboardResultBroadcast : IBroadcast
{
    public NetworkLeaderboardRecord[] Records; // NetworkLeaderboardRecord already exists in NetworkSessionController.cs
}
```

`NetworkLeaderboardRecord` (already defined in `NetworkSessionController.cs:43-66`, already SyncList-serialized so FishNet generates a serializer for it and its array) carries `float Time; long Date; string Nickname;` and `ToRecord()`.

---

### Task 1: Broadcast types + pure record mapper (TDD)

**Files:**
- Create: `Assets/Scripts/Features/Networking/LeaderboardBroadcasts.cs`
- Test: `Assets/Tests/Editor/LeaderboardQueryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/LeaderboardQueryTests.cs`:

```csharp
using System.Linq;
using Data.Leaderbords;
using Features.Networking;
using NUnit.Framework;

namespace HydroHoverMP.Tests.Editor
{
    public sealed class LeaderboardQueryTests
    {
        [Test]
        public void TakeTop_OrdersByTimeThenDate_AndCapsCount()
        {
            var source = new[]
            {
                new NetworkLeaderboardRecord(30f, 200, "C"),
                new NetworkLeaderboardRecord(10f, 100, "A"),
                new NetworkLeaderboardRecord(10f, 50, "A0"),
                new NetworkLeaderboardRecord(20f, 150, "B"),
            };

            NetworkLeaderboardRecord[] top = LeaderboardRecordMapper.TakeTop(source, 2);

            Assert.That(top.Length, Is.EqualTo(2));
            Assert.That(top[0].Nickname, Is.EqualTo("A0")); // 10f, earlier Date wins the tie
            Assert.That(top[1].Nickname, Is.EqualTo("A"));  // 10f, later Date
        }

        [Test]
        public void ToRecords_MapsFieldsAndHandlesNullArray()
        {
            var source = new[] { new NetworkLeaderboardRecord(12.5f, 999, "Pilot") };

            var records = LeaderboardRecordMapper.ToRecords(source);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].Time, Is.EqualTo(12.5f));
            Assert.That(records[0].Date, Is.EqualTo(999));
            Assert.That(records[0].PlayerName, Is.EqualTo("Pilot"));

            var empty = LeaderboardRecordMapper.ToRecords(null);
            Assert.That(empty, Is.Empty);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run the EditMode suite in Unity Test Runner (Window ▸ General ▸ Test Runner ▸ EditMode), or via MCP `run_tests` (mode `EditMode`).
Expected: FAIL — `LeaderboardRecordMapper` / `LeaderboardBroadcasts` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `Assets/Scripts/Features/Networking/LeaderboardBroadcasts.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Data.Leaderbords;
using FishNet.Broadcast;
using UnityEngine;

namespace Features.Networking
{
    // Client -> server: request the top Count records. Receiving this also flags the sender
    // connection as "leaderboard-only" so it is never treated as a racer (see NetworkSessionController).
    public struct LeaderboardQueryBroadcast : IBroadcast
    {
        public int Count;
    }

    // Server -> client: the requested records (may be empty).
    public struct LeaderboardResultBroadcast : IBroadcast
    {
        public NetworkLeaderboardRecord[] Records;
    }

    public static class LeaderboardRecordMapper
    {
        public static NetworkLeaderboardRecord[] TakeTop(IEnumerable<NetworkLeaderboardRecord> source, int count)
        {
            if (source == null) return new NetworkLeaderboardRecord[0];

            return source
                .OrderBy(record => record.Time)
                .ThenBy(record => record.Date)
                .Take(Mathf.Max(0, count))
                .ToArray();
        }

        public static List<Record> ToRecords(IEnumerable<NetworkLeaderboardRecord> source)
        {
            if (source == null) return new List<Record>();
            return source.Select(record => record.ToRecord()).ToList();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run the EditMode suite again.
Expected: PASS (`LeaderboardQueryTests` 2/2).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Features/Networking/LeaderboardBroadcasts.cs Assets/Scripts/Features/Networking/LeaderboardBroadcasts.cs.meta Assets/Tests/Editor/LeaderboardQueryTests.cs Assets/Tests/Editor/LeaderboardQueryTests.cs.meta
git commit -m "feat(net): leaderboard query/result broadcasts + record mapper"
```

---

### Task 2: Server answers the query + gates the spectator's spawn/counting

**Files:**
- Modify: `Assets/Scripts/Features/Networking/NetworkSessionController.cs`

- [ ] **Step 1: Add the leaderboard-only connection set and a top-N array helper**

Add a field near the other private fields (after `_countdownEndsAt`, around line 91):

```csharp
        private readonly HashSet<FishNet.Connection.NetworkConnection> _leaderboardOnlyConnections = new();
        private FishNet.Component.Spawning.PlayerSpawner _playerSpawner;
```

Add this method next to `GetDedicatedLeaderboardRecords` (around line 281):

```csharp
        private NetworkLeaderboardRecord[] GetDedicatedLeaderboardRecordsArray(int count)
        {
            return LeaderboardRecordMapper.TakeTop(DedicatedLeaderboardRecords, count);
        }
```

- [ ] **Step 2: Register the broadcast handler and the spawn hook on server start**

Replace the body of `OnStartServer` (currently lines 120-127) with:

```csharp
        public override void OnStartServer()
        {
            base.OnStartServer();
            Phase.Value = SessionPhase.Lobby;
            Debug.Log($"[NetworkSessionController] Dedicated leaderboard file: {GetDedicatedLeaderboardPath()}");
            LoadDedicatedLeaderboardRecords();
            NetworkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;

            // Leaderboard-only clients (the main-menu "live query") send this; they must never be
            // treated as racers, so requireAuthentication:false keeps the handshake light and the
            // reply is sent straight back to the asking connection.
            NetworkManager.ServerManager.RegisterBroadcast<LeaderboardQueryBroadcast>(OnLeaderboardQueryBroadcast, requireAuthentication: false);

            // PlayerSpawner auto-spawns a player for every connection; intercept and despawn the
            // player belonging to a leaderboard-only connection so it can't join the lobby.
            _playerSpawner = NetworkManager.gameObject.GetComponent<FishNet.Component.Spawning.PlayerSpawner>();
            if (_playerSpawner != null)
                _playerSpawner.OnSpawned += OnPlayerSpawnerSpawned;
        }
```

- [ ] **Step 3: Unhook on server stop**

Replace `OnStopServer` (currently lines 129-136) with:

```csharp
        public override void OnStopServer()
        {
            if (NetworkManager != null && NetworkManager.ServerManager != null)
            {
                NetworkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
                NetworkManager.ServerManager.UnregisterBroadcast<LeaderboardQueryBroadcast>(OnLeaderboardQueryBroadcast);
            }

            if (_playerSpawner != null)
                _playerSpawner.OnSpawned -= OnPlayerSpawnerSpawned;
            _playerSpawner = null;

            _leaderboardOnlyConnections.Clear();
            _players.Clear();
            base.OnStopServer();
        }
```

- [ ] **Step 4: Implement the query handler and the spawn interceptor**

Add these methods next to `RequestDedicatedLeaderboardServerRpc` (around line 287). Leave the now-vestigial `RequestDedicatedLeaderboardServerRpc` in place for the in-session (connected) read path; it stays a no-op.

```csharp
        private void OnLeaderboardQueryBroadcast(FishNet.Connection.NetworkConnection conn, LeaderboardQueryBroadcast msg, FishNet.Transporting.Channel channel)
        {
            if (!IsServerInitialized || conn == null) return;

            // Mark the connection so it is excluded from lobby/race logic, and despawn any player
            // PlayerSpawner already created for it (covers the race where spawn beat this message).
            _leaderboardOnlyConnections.Add(conn);
            DespawnConnectionPlayers(conn);
            ConnectedPlayers.Value = _players.Count;

            int count = Mathf.Clamp(msg.Count, 0, 100);
            NetworkManager.ServerManager.Broadcast(conn, new LeaderboardResultBroadcast
            {
                Records = GetDedicatedLeaderboardRecordsArray(count)
            }, requireAuthenticated: false);
        }

        private void OnPlayerSpawnerSpawned(NetworkObject nob)
        {
            if (nob == null) return;
            if (nob.Owner == null || !_leaderboardOnlyConnections.Contains(nob.Owner)) return;

            // A leaderboard-only connection just had a player auto-spawned; remove it immediately.
            NetworkManager.ServerManager.Despawn(nob);
        }

        private void DespawnConnectionPlayers(FishNet.Connection.NetworkConnection conn)
        {
            if (conn == null) return;

            // Copy first: despawning mutates conn.Objects.
            foreach (NetworkObject nob in conn.Objects.ToArray())
            {
                if (nob != null && nob.GetComponent<NetworkPlayerData>() != null)
                    NetworkManager.ServerManager.Despawn(nob);
            }
        }
```

(`System.Linq` is already imported at the top of the file; `conn.Objects.ToArray()` uses it.)

- [ ] **Step 5: Exclude leaderboard-only connections from player counting**

Replace `OnRemoteConnectionState` (currently lines 379-385) with:

```csharp
        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                _leaderboardOnlyConnections.Remove(connection);
                RefreshReadyState();
            }

            // Count only spawned racers, never leaderboard-only query connections.
            ConnectedPlayers.Value = _players.Count;
        }
```

(`RefreshReadyState()` already recomputes `ConnectedPlayers.Value = _players.Count` and `ReadyPlayers`; setting it here too keeps the value correct on the Started edge.)

- [ ] **Step 6: Compile-check**

In Unity, let the domain reload finish, then check the Console (or MCP `read_console`).
Expected: no compile errors. (If `NetworkObject` is not resolved, confirm `using FishNet.Object;` is present — it already is at line 7.)

- [ ] **Step 7: Run EditMode tests (regression)**

Run the EditMode suite.
Expected: PASS — Task 1 tests still green; existing `NetworkLogicTests`/`ServerEnvironmentTests` unaffected.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Features/Networking/NetworkSessionController.cs
git commit -m "feat(net): server answers leaderboard query + excludes query connections from lobby"
```

---

### Task 3: Short-lived client query connection in NetworkConnectionService

**Files:**
- Modify: `Assets/Scripts/Infrastructure/Services/Network/INetworkConnectionService.cs`
- Modify: `Assets/Scripts/Infrastructure/Services/Network/NetworkConnectionService.cs`

- [ ] **Step 1: Extend the interface**

Open `INetworkConnectionService.cs`. Add (inside the interface, alongside the existing members):

```csharp
        // Raised on the client when a leaderboard-only query returns records (may be empty).
        event System.Action<System.Collections.Generic.IReadOnlyList<Data.Leaderbords.Record>> OnLeaderboardRecordsReceived;

        // Starts a short-lived "leaderboard-only" connection to the configured dedicated server,
        // requests the top `count` records, and disconnects. Returns false if a connection/query is
        // already active. Result arrives via OnLeaderboardRecordsReceived.
        bool BeginLeaderboardQuery(int count);

        // Aborts an in-flight leaderboard query (e.g. the window closed or timed out).
        void CancelLeaderboardQuery();
```

> If `INetworkConnectionService.cs` does not already expose the members used by `MainMenuWindow`/`FinishScreen` (e.g. `Status`, `StartClient`, `StopConnection`, `OnStatusChanged`...), do NOT remove them — only add the three members above.

- [ ] **Step 2: Add query state + send on connect (NetworkConnectionService)**

In `NetworkConnectionService.cs`:

Add fields near the other private fields (after `_clientStartRequested`, around line 29):

```csharp
        private bool _leaderboardQueryActive;
        private int _pendingLeaderboardQueryCount;
```

Add the event next to the other events (after `OnConnectionFailed`, around line 44):

```csharp
        public event Action<System.Collections.Generic.IReadOnlyList<Data.Leaderbords.Record>> OnLeaderboardRecordsReceived;
```

Add the public methods (place after `StopConnection`, around line 160):

```csharp
        public bool BeginLeaderboardQuery(int count)
        {
            if (_leaderboardQueryActive) return false;
            if (!EnsureNetworkManager()) return false;

            RefreshStatus();
            if (_status is not (NetworkConnectionStatus.Offline or NetworkConnectionStatus.Failed))
                return false; // Busy hosting/connecting/already in a session.

            _leaderboardQueryActive = true;
            _pendingLeaderboardQueryCount = count;
            _stopRequested = false;
            _clientStartRequested = false;

            _networkManager.ClientManager.RegisterBroadcast<Features.Networking.LeaderboardResultBroadcast>(OnLeaderboardResultBroadcast);

            string address = _dedicatedServerConfiguration.NormalizedAddress;
            ushort port = _dedicatedServerConfiguration.NormalizedPort;
            if (!_networkManager.ClientManager.StartConnection(address, port))
            {
                _networkManager.ClientManager.UnregisterBroadcast<Features.Networking.LeaderboardResultBroadcast>(OnLeaderboardResultBroadcast);
                _leaderboardQueryActive = false;
                return false;
            }

            return true;
        }

        // Called by the UI when the leaderboard window closes or times out. This is the ONLY place
        // the query connection is stopped — never from inside the broadcast handler (re-entrancy).
        public void CancelLeaderboardQuery()
        {
            if (!_leaderboardQueryActive) return;

            _leaderboardQueryActive = false;
            if (_networkManager != null)
            {
                _networkManager.ClientManager.UnregisterBroadcast<Features.Networking.LeaderboardResultBroadcast>(OnLeaderboardResultBroadcast);
                if (_networkManager.IsClientStarted)
                {
                    _stopRequested = true;
                    _networkManager.ClientManager.StopConnection();
                }
            }

            SetStatus(NetworkConnectionStatus.Offline);
        }

        // Stays connected after delivering results so the window can keep showing live data; the
        // connection is torn down later by CancelLeaderboardQuery. We never StopConnection here
        // because this runs inside FishNet's client read loop.
        private void OnLeaderboardResultBroadcast(Features.Networking.LeaderboardResultBroadcast msg, Channel channel)
        {
            System.Collections.Generic.IReadOnlyList<Data.Leaderbords.Record> records =
                Features.Networking.LeaderboardRecordMapper.ToRecords(msg.Records);
            OnLeaderboardRecordsReceived?.Invoke(records);
        }
```

- [ ] **Step 3: Send the query when the query connection comes up, and keep it invisible to the menu**

In `OnClientConnectionState` (lines 436-462), handle the query path first. Replace the method with:

```csharp
        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (_leaderboardQueryActive)
            {
                if (args.ConnectionState == LocalConnectionState.Started)
                {
                    // Connected as a leaderboard-only client: ask for records, stay invisible to the
                    // menu status UI (do not flip status to ClientStarted).
                    _networkManager.ClientManager.Broadcast(new Features.Networking.LeaderboardQueryBroadcast
                    {
                        Count = _pendingLeaderboardQueryCount
                    });
                }
                else if (args.ConnectionState == LocalConnectionState.Stopped)
                {
                    // Server dropped us before replying: leave query mode and surface an empty
                    // result so the window can exit its loading state.
                    _leaderboardQueryActive = false;
                    if (_networkManager != null)
                        _networkManager.ClientManager.UnregisterBroadcast<Features.Networking.LeaderboardResultBroadcast>(OnLeaderboardResultBroadcast);
                    SetStatus(NetworkConnectionStatus.Offline);
                    OnLeaderboardRecordsReceived?.Invoke(new System.Collections.Generic.List<Data.Leaderbords.Record>());
                }

                return;
            }

            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _clientStartRequested = false;
                RefreshStatus();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (_clientStartRequested && !_stopRequested)
                {
                    _clientStartRequested = false;
                    Fail("FishNet client connection stopped before it fully connected. Check address, port, host availability and Tugboat setup.");
                    return;
                }

                bool unexpected = !_stopRequested;
                _clientStartRequested = false;
                RefreshStatus();

                if (unexpected)
                    HandleUnexpectedClientDisconnect();
            }
            else if (args.ConnectionState == LocalConnectionState.Starting)
                SetStatus(NetworkConnectionStatus.StartingClient);
        }
```

- [ ] **Step 4: Suppress status churn during a query**

`MainMenuWindow.Update` calls `RefreshStatus()` every 0.5s, which reads `IsClientStarted`. Guard it so a query connection does not show up as "connected". At the top of `RefreshStatus()` (line 162), add:

```csharp
            if (_leaderboardQueryActive)
            {
                SetStatus(NetworkConnectionStatus.Offline);
                return;
            }
```

Also guard `OnServerConnectionState`/`LoadGameplayGlobalSceneOnce` is server-only and unaffected (the query is client-only). No change needed there.

- [ ] **Step 5: Compile-check**

Let Unity reload; check Console / `read_console`.
Expected: no compile errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Infrastructure/Services/Network/INetworkConnectionService.cs Assets/Scripts/Infrastructure/Services/Network/NetworkConnectionService.cs
git commit -m "feat(net): short-lived leaderboard-only query connection (menu)"
```

---

### Task 4: LeaderboardService bridges the query to the UI (TDD)

**Files:**
- Modify: `Assets/Scripts/Infrastructure/Services/Leaderboard/ILeaderboardService.cs`
- Modify: `Assets/Scripts/Infrastructure/Services/Leaderboard/LeaderboardService.cs`
- Test: `Assets/Tests/Editor/LeaderboardQueryTests.cs` (add cases)

- [ ] **Step 1: Write the failing test (offline cache return + event)**

Append to `Assets/Tests/Editor/LeaderboardQueryTests.cs` (add `using` lines at top: `using System; using System.Collections.Generic; using Infrastructure.Services.Leaderboard; using Infrastructure.Services.Network;`):

```csharp
    public sealed class LeaderboardServiceQueryTests
    {
        // Minimal fake so LeaderboardService can be exercised without FishNet.
        private sealed class FakeConnectionService : INetworkConnectionService
        {
            public int BeginCalls;
            public int LastCount;
            public event Action<IReadOnlyList<Record>> OnLeaderboardRecordsReceived;

            public bool BeginLeaderboardQuery(int count)
            {
                BeginCalls++;
                LastCount = count;
                return true;
            }

            public void CancelLeaderboardQuery() { }
            public void Emit(IReadOnlyList<Record> records) => OnLeaderboardRecordsReceived?.Invoke(records);

            // The remaining INetworkConnectionService members are unused by these tests.
            // Implement them as no-ops/defaults to satisfy the interface (see real interface for the list).
            // >>> EXECUTOR: stub every other interface member here returning default/false/0 and no-op events. <<<
        }

        [Test]
        public void DedicatedMode_Offline_RequestThenResult_PopulatesGetTopRecordsAndFiresEvent()
        {
            var config = new LeaderboardConfiguration { SourceMode = LeaderboardSourceMode.DedicatedServer };
            var fake = new FakeConnectionService();
            var service = new LeaderboardService(config, fake);

            bool fired = false;
            service.OnDedicatedRecordsUpdated += () => fired = true;

            service.RequestDedicatedRecords();
            Assert.That(fake.BeginCalls, Is.EqualTo(1));

            fake.Emit(new List<Record> { new Record { Time = 5f, PlayerName = "A" } });

            Assert.That(fired, Is.True);
            var top = service.GetTopRecords(5);
            Assert.That(top.Count, Is.EqualTo(1));
            Assert.That(top[0].PlayerName, Is.EqualTo("A"));
        }
    }
```

> **EXECUTOR NOTE:** `FakeConnectionService` must implement the *full* `INetworkConnectionService`. Open the interface and stub each remaining member (`Status` ⇒ `NetworkConnectionStatus.Offline`, bool methods ⇒ `false`, `int` props ⇒ `0`, events ⇒ declared but unused, void methods ⇒ empty). This is the only "fill-in" in the plan and it is mechanical.

- [ ] **Step 2: Run test to verify it fails**

Run EditMode suite.
Expected: FAIL — `LeaderboardService` has no 2-arg constructor, no `OnDedicatedRecordsUpdated`, and does not store query results.

- [ ] **Step 3: Extend the interface**

In `ILeaderboardService.cs` add:

```csharp
        event System.Action OnDedicatedRecordsUpdated;
        void CancelDedicatedRecordsRequest();
```

- [ ] **Step 4: Implement the bridge in LeaderboardService**

Edit `LeaderboardService.cs`:

Add usings: `using System;` (top).

Add fields (after `_data`, line 16):

```csharp
        private readonly INetworkConnectionService _connectionService;
        private List<Record> _dedicatedRecords = new();
        private bool _dedicatedRequestInFlight;

        public event Action OnDedicatedRecordsUpdated;
```

Replace the constructor (lines 18-23) with one that also takes the connection service (keep the old default for non-DI callers/tests):

```csharp
        public LeaderboardService(LeaderboardConfiguration configuration = null, INetworkConnectionService connectionService = null)
        {
            _configuration = configuration ?? new LeaderboardConfiguration();
            _connectionService = connectionService;
            _path = Path.Combine(Application.persistentDataPath, FileName);
            Load();

            if (_connectionService != null)
                _connectionService.OnLeaderboardRecordsReceived += OnDedicatedRecordsReceived;
        }
```

Replace `GetTopRecords` (lines 44-60) so dedicated mode returns the connected SyncList when in-session, otherwise the last query result:

```csharp
        public List<Record> GetTopRecords(int count)
        {
            if (IsUsingDedicatedServer)
            {
                NetworkSessionController session = NetworkSessionController.Instance;
                if (session != null)
                {
                    session.RequestDedicatedLeaderboardServerRpc();
                    return session.GetDedicatedLeaderboardRecords(count);
                }

                // Disconnected (main menu): serve the most recent query result.
                return _dedicatedRecords.Take(count).ToList();
            }

            return _data.Records.Take(count).ToList();
        }
```

Replace `RequestDedicatedRecords` (lines 68-75) with the menu query kickoff:

```csharp
        public void RequestDedicatedRecords()
        {
            if (!IsUsingDedicatedServer) return;

            // Already in a live session: the connected SyncList path serves data; nothing to fetch.
            if (NetworkSessionController.Instance != null) return;
            if (_connectionService == null) return;
            if (_dedicatedRequestInFlight) return;

            _dedicatedRequestInFlight = _connectionService.BeginLeaderboardQuery(5);
        }

        public void CancelDedicatedRecordsRequest()
        {
            // Always forward: the query connection stays open after results are delivered, so the
            // window closing must tear it down even when no request is "in flight". Safe no-op if
            // there is no active query.
            _dedicatedRequestInFlight = false;
            _connectionService?.CancelLeaderboardQuery();
        }

        private void OnDedicatedRecordsReceived(System.Collections.Generic.IReadOnlyList<Record> records)
        {
            _dedicatedRequestInFlight = false;
            _dedicatedRecords = records != null ? new List<Record>(records) : new List<Record>();
            OnDedicatedRecordsUpdated?.Invoke();
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run EditMode suite.
Expected: PASS — `LeaderboardServiceQueryTests` green, Task 1 tests green.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Infrastructure/Services/Leaderboard/ILeaderboardService.cs Assets/Scripts/Infrastructure/Services/Leaderboard/LeaderboardService.cs Assets/Tests/Editor/LeaderboardQueryTests.cs
git commit -m "feat(leaderboard): query dedicated server from menu, expose update event"
```

---

### Task 5: LeaderboardWindow async UI (Loading / Results / Empty / Error)

**Files:**
- Modify: `Assets/Scripts/UI/Leaderboard/LeaderboardWindow.cs`

- [ ] **Step 1: Subscribe to updates, drive states, time out, and unsubscribe**

Replace `LeaderboardWindow.cs` body (the class members from `Start` down, lines 29-67) with:

```csharp
        private const float QueryTimeoutSeconds = 8f;
        private float _queryDeadline;
        private bool _awaitingRecords;

        private void Start()
        {
            _closeButton.onClick.AddListener(Close);
            _leaderboardService.OnDedicatedRecordsUpdated += OnRecordsUpdated;

            BeginLoadOrShow();
        }

        private void OnDestroy()
        {
            _leaderboardService.OnDedicatedRecordsUpdated -= OnRecordsUpdated;
            // The query connection stays open while the window is shown; always tear it down on close.
            _leaderboardService.CancelDedicatedRecordsRequest();
        }

        private void Update()
        {
            if (!_awaitingRecords) return;
            if (Time.unscaledTime < _queryDeadline) return;

            _awaitingRecords = false;
            _leaderboardService.CancelDedicatedRecordsRequest();
            RenderRecords(_leaderboardService.GetTopRecords(5), "Server unreachable — showing last known records.");
        }

        private void BeginLoadOrShow()
        {
            if (_sourceText != null)
                _sourceText.text = BuildSourceText();

            bool willQuery = _leaderboardService.IsUsingDedicatedServer
                             && NetworkSessionController.Instance == null;

            if (willQuery)
            {
                _awaitingRecords = true;
                _queryDeadline = Time.unscaledTime + QueryTimeoutSeconds;
                ClearContainer();
                ShowStatus("Fetching records from server…");
                _leaderboardService.RequestDedicatedRecords();
                return;
            }

            // Local mode, or already connected (live SyncList): render immediately.
            _leaderboardService.RequestDedicatedRecords();
            RenderRecords(_leaderboardService.GetTopRecords(5), null);
        }

        private void OnRecordsUpdated()
        {
            _awaitingRecords = false;
            RenderRecords(_leaderboardService.GetTopRecords(5), null);
        }

        private void RenderRecords(List<Record> records, string emptyHint)
        {
            ClearContainer();

            if (records.Count == 0)
            {
                ShowStatus(emptyHint ?? "No records yet.");
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var itemObj = Instantiate(_recordItemPrefab, _container);
                var view = itemObj.GetComponent<RecordItemView>();
                view.SetData(i + 1, record.Time, record.PlayerName);
            }
        }

        private void ShowStatus(string message)
        {
            if (_sourceText != null)
                _sourceText.text = message;
        }

        private void ClearContainer()
        {
            foreach (Transform child in _container) Destroy(child.gameObject);
        }

        private string BuildSourceText()
        {
            if (!_leaderboardService.IsUsingDedicatedServer)
                return "Leaderboard source: Local";

            return $"Leaderboard source: Dedicated server {_leaderboardService.DedicatedServerAddress}:{_leaderboardService.DedicatedServerPort}";
        }

        private void Close()
        {
            _windowService.Open(WindowID.MainMenu);
            _windowService.Close(WindowID.Leaderboard);
        }
```

Add the required usings at the top of the file if missing: `using Features.Networking;` (for `NetworkSessionController`). `System.Collections.Generic`, `Data.Leaderbords`, `UnityEngine`, `TMPro` are already imported.

- [ ] **Step 2: Compile-check**

Let Unity reload; check Console / `read_console`.
Expected: no compile errors.

- [ ] **Step 3: Wire `_sourceText` on the prefab (secondary bug fix)**

The verification pass found `_sourceText` is unassigned on `Assets/Prefabs/UI/Leaderboard.prefab`, so the status/source line never shows. In the Unity Editor: open `Leaderboard.prefab`, select the root with `LeaderboardWindow`, and drag the title/subtitle `TextMeshProUGUI` (or add a small status label under the panel) into the **Source Text** field. Save the prefab.

> If no suitable label exists, add a `TextMeshProUGUI` child named `SourceText` under the panel (anchored top, small font) and assign it. Without this, Loading/Empty/Error hints are silent (records still render correctly).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/Leaderboard/LeaderboardWindow.cs Assets/Prefabs/UI/Leaderboard.prefab
git commit -m "feat(ui): async leaderboard window with loading/empty/error states; wire source text"
```

---

### Task 6: DI wiring verification (GlobalInstaller)

**Files:**
- Modify (only if needed): `Assets/Scripts/Infrastructure/Installers/GlobalInstaller.cs`

- [ ] **Step 1: Confirm constructor injection resolves**

`GlobalInstaller.BindCoreSystems` binds `INetworkConnectionService` (line 62) and `ILeaderboardService → LeaderboardService` (line 59). Zenject resolves `LeaderboardService(LeaderboardConfiguration, INetworkConnectionService)` by injecting the bound `LeaderboardConfiguration` instance and `INetworkConnectionService` automatically — **no code change expected**.

- [ ] **Step 2: Guard binding order**

Zenject `AsSingle` bindings are resolved lazily, so the line order of `BindInstance(leaderboardConfiguration)` (line 58), `Bind<ILeaderboardService>` (line 59), and `BindInterfacesTo<NetworkConnectionService>` (line 62) does not matter for construction. Verify at runtime (Task 7) that `LeaderboardWindow.Construct` receives a non-null service and no Zenject "Unable to resolve" error appears in the Console.

- [ ] **Step 3: Commit (only if a change was required)**

```bash
git add Assets/Scripts/Infrastructure/Installers/GlobalInstaller.cs
git commit -m "chore(di): wire connection service into leaderboard service"
```

---

### Task 7: Manual PlayMode + dedicated-server verification

**Files:** none (verification only)

FishNet connection/broadcast/spawn behavior is integration-level; verify in the running app. Run from a build/editor that talks to the real dedicated server, OR run a local host as the "server".

- [ ] **Step 1: Local two-process smoke (host + client)**

1. Temporarily set `ProjectContext.prefab` GlobalInstaller `_dedicatedServerOnly = 0` to allow Host in-editor (revert after), keep `_leaderboardSourceMode = 1` (DedicatedServer).
2. Build a player (or use ParrelSync/second editor). Process A: **Host**. Process B: **Client**, connect, finish a race so a record is written.
3. On the Client, click **Main Menu** (disconnects), open **Leaderboard**.
   Expected: window shows "Fetching records from server…", then the record(s) appear within ~1s. Host Console shows the query connection connect+disconnect with **no countdown triggered** and **no player left in the world** for the query connection.

- [ ] **Step 2: Lobby-integrity check (the spectator must not block/launch a race)**

1. On the Host, have one real client connected and ready (so `ConnectedPlayers=1`, below `_minimumPlayers=2`).
2. From a third process in the **menu**, open Leaderboard repeatedly (each opens a query connection).
   Expected: the countdown never starts because of the query connection; `ConnectedPlayers` on the Host reflects only real racers; the query connections never appear as players.

- [ ] **Step 3: Real dedicated server (the reported scenario)**

1. Restore `_dedicatedServerOnly = 1`. Point at `84.21.173.65:7770` (existing config).
2. Connect as client, finish a race, click **Main Menu**, open **Leaderboard**.
   Expected: the just-completed time (and other server records) appear. This is the originally-reported bug — confirm it is fixed.

- [ ] **Step 4: Unreachable-server check**

1. With the dedicated server stopped/unreachable, open Leaderboard from the menu.
   Expected: "Fetching records from server…" then, after ~8s, "Server unreachable — showing last known records." (empty or last cache). The menu remains usable; status never shows a phantom "connected" state.

- [ ] **Step 5: Re-connect-to-play after a query**

1. After any query (Steps 1/3), from the menu click **Client ▸ Connect** to actually join.
   Expected: normal connect succeeds (the window's close called `CancelLeaderboardQuery`, which reset state and disconnected the query connection, so a real connect is not blocked).

- [ ] **Step 6: Final commit (if any prefab/config tweaks were made during verification)**

```bash
git add -A
git commit -m "test(net): verify dedicated leaderboard menu query end-to-end"
```

---

## Risk notes / things to watch during execution

- **Spawn race:** if `OnClientLoadedStartScenes` (player spawn) fires before the server processes `LeaderboardQueryBroadcast`, `OnPlayerSpawnerSpawned` despawns the player as soon as it knows the conn is leaderboard-only; the query handler also despawns existing players. Watch for a 1-frame `RegisterPlayer`/`UnregisterPlayer` flicker in the Host Console — acceptable, but confirm it does not auto-start a countdown (it should not: the freshly spawned player is not "ready" within that frame).
- **Single NetworkManager:** the query reuses the one client connection. `BeginLeaderboardQuery` refuses to run unless `Status` is Offline/Failed, so it never clobbers a live game connection. Confirm Step 5.
- **Status suppression:** `RefreshStatus` early-returns to Offline while a query is active; verify `MainMenuWindow` never shows "Client connected" during a query.
- **`requireAuthentication:false`:** chosen so the early query broadcast can't get the client kicked before the default passthrough auth completes, and the data is non-sensitive. If the project later adds an authenticator, revisit.
- **Global Gameplay scene load:** a query connection still receives the server's global Gameplay scene (FishNet pushes global scenes to all clients). It is unloaded on disconnect. This is a known cost of reusing the game server; acceptable for a menu peek.
```
