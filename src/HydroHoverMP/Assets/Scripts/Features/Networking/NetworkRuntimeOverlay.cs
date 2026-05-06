using System.Linq;
using Infrastructure.Services.Network;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Zenject;

namespace Features.Networking
{
    public sealed class NetworkRuntimeOverlay : MonoBehaviour
    {
        private const ushort DefaultPort = 7770;

        private INetworkConnectionService _connectionService;
        private string _address = "localhost";
        private string _nickname = "Pilot";
        private bool _visible;
        private Vector2 _scroll;

        private void Awake()
        {
            ResolveService();
        }

        private void Update()
        {
            if (IsTogglePressed())
                _visible = !_visible;
        }

        private static bool IsTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F2);
#else
            return false;
#endif
        }

        private void OnGUI()
        {
            if (!_visible) return;

            ResolveService();

            GUILayout.BeginArea(new Rect(12, 12, 360, Screen.height - 24), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("<b>HydroHoverMP FishNet</b>");
            GUILayout.Label("F2 — show/hide network panel");

            DrawConnectionControls();
            DrawSessionControls();
            DrawPlayers();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawConnectionControls()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>Connection</b>");

            if (_connectionService == null)
            {
                GUILayout.Label("NetworkConnectionService is not available.");
                return;
            }

            _connectionService.RefreshStatus();
            GUILayout.Label($"Status: {_connectionService.Status}");
            GUILayout.Label($"Server clients: {_connectionService.ConnectedClientCount}");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", GUILayout.Width(70));
            _address = GUILayout.TextField(_address);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host"))
                _connectionService.StartHost(DefaultPort);
            if (GUILayout.Button("Client"))
                _connectionService.StartClient(_address, DefaultPort);
            if (GUILayout.Button("Server"))
                _connectionService.StartServer(DefaultPort);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Stop connection"))
                _connectionService.StopConnection();
        }

        private void DrawSessionControls()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>Lobby / Session</b>");

            NetworkSessionController session = NetworkSessionController.Instance;
            if (session == null)
            {
                GUILayout.Label("NetworkSessionController is not in the active network scene.");
                GUILayout.Label("Run HydroHoverMP/Networking/Apply FishNet Setup in Unity Editor.");
                return;
            }

            GUILayout.Label($"Phase: {session.Phase.Value}");
            GUILayout.Label($"Ready: {session.ReadyPlayers.Value}/{session.ConnectedPlayers.Value}");
            if (session.Phase.Value == SessionPhase.Countdown)
                GUILayout.Label($"Countdown: {session.CountdownRemaining.Value:0.0}");

            NetworkPlayerData localPlayer = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nickname", GUILayout.Width(70));
            _nickname = GUILayout.TextField(_nickname);
            GUILayout.EndHorizontal();

            if (localPlayer != null)
            {
                if (GUILayout.Button("Apply nickname"))
                    localPlayer.SetNickname(_nickname);

                string readyLabel = localPlayer.IsReady.Value ? "Unready" : "Ready";
                if (GUILayout.Button(readyLabel))
                    localPlayer.SetReady(!localPlayer.IsReady.Value);
            }
            else
            {
                GUILayout.Label("Waiting for local network player spawn.");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force start"))
                session.RequestForceStartServerRpc();
            if (GUILayout.Button("Restart lobby"))
                session.RequestRestartServerRpc();
            GUILayout.EndHorizontal();
        }

        private void DrawPlayers()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>Players</b>");

            NetworkPlayerData[] players = FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None);
            if (players.Length == 0)
            {
                GUILayout.Label("No network players spawned.");
                return;
            }

            foreach (NetworkPlayerData player in players.OrderBy(p => p.ClientId))
            {
                string ownerMark = player.IsOwner ? "local" : "remote";
                string finish = player.IsFinished.Value ? $" | finish {player.FinishTime.Value:0.00}s" : string.Empty;
                GUILayout.Label(
                    $"#{player.ClientId} {player.Nickname.Value} ({ownerMark}) | HP {player.HP.Value} | CP {player.CheckpointIndex.Value} | Score {player.Score.Value}{finish}");
            }
        }

        private void ResolveService()
        {
            if (_connectionService != null || !ProjectContext.HasInstance) return;

            _connectionService = ProjectContext.Instance.Container.TryResolve<INetworkConnectionService>();
        }
    }
}
