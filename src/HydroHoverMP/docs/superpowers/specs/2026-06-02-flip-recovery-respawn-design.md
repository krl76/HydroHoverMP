# Flip Recovery (R → respawn at last checkpoint) + Remove Pause Restart — Design

**Date:** 2026-06-02
**Status:** Approved, implemented

## Goal
Press **R** to recover a flipped/stuck hovercraft by respawning it at the last passed
checkpoint (upright, velocity cleared). Remove the now-unwanted **Restart** button from
the Pause menu (multiplayer-only game).

## Decision
Respawn runs **entirely on the owner client**. Movement is already client-authoritative
(`NetworkTransform._clientAuthoritative = 1`, owner Rigidbody non-kinematic, remotes
kinematic), so the owner can move its own craft and the result syncs to everyone. No
server RPC is needed, and this does not lower the existing trust model (the server still
validates checkpoint passing in `NetworkRaceManager.TryPassCheckpoint`).

## Design
- **Input** (`IInputService.RespawnPressed`): polls `Keyboard.current.rKey.wasPressedThisFrame`,
  mirroring the existing `HydroPulsePressed` (Shift). No `.inputactions` asset edit.
- **Checkpoint poses** (`IRaceManagerService.TryGetCheckpointPose(index, out pos, out rot)`):
  `RaceManagerService` already stores the ordered `CheckpointTrigger` list from
  `RegisterTrack` (populated on every peer via `TrackData`). Returns the checkpoint's
  world pose by index.
- **`HoverRespawnController`** (new, owner-only `NetworkBehaviour` on the HoverCraft prefab):
  on R press, when `IsOwner` and (no session or `Phase == Race`) and past the cooldown:
  - target = checkpoint `CheckpointIndex - 1` pose if available; else
    `NetworkSpawnPointRegistry` spawn 0 (start fallback).
  - sets `HoverController.Rb` position/rotation (+`_uprightLift` up), clears
    `linearVelocity`/`angularVelocity`; `NetworkTransform` syncs to others.
  - `_respawnCooldown` (~1s) prevents spam.
- **Remove Pause Restart**: `PauseWindow` no longer wires `_restartButton` to a restart;
  the button is deactivated in `Start`, and `RestartRace()` is removed. (The dead
  single-player `LoadLevelState(GAMEPLAY)` path it used is gone from Pause.)

## Files
New: `Features/Networking/HoverRespawnController.cs` (+ on HoverCraft prefab).
Changed: `IInputService`/`InputService` (RespawnPressed), `IRaceManagerService`/
`RaceManagerService` (TryGetCheckpointPose), `UI/Pause/PauseWindow.cs` (remove Restart).

## Verification
Compiles clean; 14/14 EditMode tests; HoverCraft prefab carries the component.
Interactive R-respawn + Pause-without-Restart confirmed in play by the user.

## Out of scope
Server-authoritative respawn, rebindable R key, respawn VFX/SFX.
