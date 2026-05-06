# HydroHoverMP Agent Instructions

Project: Unity 6000.3.9f1 hovercraft racing game being extended with FishNet multiplayer.

## Non-negotiables

- Preserve the current architecture: Zenject DI, `GameStateMachine`, Addressables UI/prefab loading, and separated `Core`, `Infrastructure`, `UI`, `Physics`, and `Features` folders.
- Use FishNet as the networking framework. Do not switch to NGO, Photon, Mirror, or a clean-slate networking layer unless explicitly requested.
- Primary topology is Host/Client with FishNet Tugboat. Dedicated Server is a bonus phase only after the required Host/Client multiplayer flow works.
- Do not collapse logic into a monolithic `GameManager`.
- Do not send custom gameplay/UI state every frame. Use FishNet SyncTypes for state and RPCs for discrete actions.
- Server owns session phase, player registry, ready state, HP, score, checkpoints, finish results, restart decisions, and disconnect handling.
- Owner client controls only its own hovercraft. Remote hovercrafts are display-only on a client.

## Suggested agent workflow

- Inspect current scripts, prefabs, scenes, and Addressables before editing.
- Check official FishNet docs/source for API-sensitive changes.
- Implement in small vertical slices:
  1. FishNet dependency and NetworkManager scene setup.
  2. Host/Client connection service and UI.
  3. Network player spawn, ownership, and camera binding.
  4. SyncVar-backed nickname/HP/score/checkpoint UI.
  5. ServerRpc/ObserversRpc gameplay action.
  6. Lobby → Countdown → Race → Results lifecycle.
  7. Disconnect handling, performance, sound, and visual polish.
- Prefer extending existing services/states/windows over adding parallel frameworks.
- If FishNet scene/prefab setup cannot be safely edited as YAML, add scripts and an explicit Unity Inspector wiring checklist rather than guessing serialized component data.

## Verification gates

- Host and Client connect and see each other in the same gameplay scene.
- Local input affects only the local owner hovercraft.
- Camera follows only the local owner.
- Nickname, HP, score, checkpoint, ready state, and results are visible to all clients.
- A discrete action goes Client request → Server validation → Observer broadcast.
- Lobby → Countdown → Race → Results → Restart/Exit works.
- Disconnect during lobby/race/results does not break the remaining session.
- FPS counter stays at or above 30 FPS during a normal two-player test.
- Unity console has no critical errors before claiming completion.
