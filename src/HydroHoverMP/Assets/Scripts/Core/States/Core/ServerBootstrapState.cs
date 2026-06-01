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
