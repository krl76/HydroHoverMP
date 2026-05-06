using System.Collections.Generic;
using UnityEngine;

namespace Infrastructure.Services.Player
{
    public interface IPlayerService
    {
        /// <summary>
        /// Backward-compatible alias for the local player transform.
        /// </summary>
        Transform Transform { get; }
        Transform LocalPlayerTransform { get; }
        IReadOnlyDictionary<int, Transform> RemotePlayers { get; }

        bool IsPlayerCreated { get; }
        bool IsLocalPlayerCreated { get; }

        void RegisterPlayer(GameObject playerInstance);
        void RegisterLocalPlayer(GameObject playerInstance);
        void RegisterRemotePlayer(int clientId, GameObject playerInstance);
        void UnregisterPlayer();
        void UnregisterLocalPlayer();
        void UnregisterRemotePlayer(int clientId);
        void ClearRemotePlayers();
    }
}
