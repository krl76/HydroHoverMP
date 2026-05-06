using System.Collections.Generic;
using UnityEngine;

namespace Infrastructure.Services.Player
{
    public class PlayerService : IPlayerService
    {
        private readonly Dictionary<int, Transform> _remotePlayers = new();

        public Transform Transform => LocalPlayerTransform;
        public Transform LocalPlayerTransform { get; private set; }
        public IReadOnlyDictionary<int, Transform> RemotePlayers => _remotePlayers;

        public bool IsPlayerCreated => IsLocalPlayerCreated;
        public bool IsLocalPlayerCreated => LocalPlayerTransform != null;

        public void RegisterPlayer(GameObject playerInstance)
        {
            RegisterLocalPlayer(playerInstance);
        }

        public void RegisterLocalPlayer(GameObject playerInstance)
        {
            LocalPlayerTransform = playerInstance != null ? playerInstance.transform : null;
            Debug.Log("[PlayerService] Local player registered");
        }

        public void RegisterRemotePlayer(int clientId, GameObject playerInstance)
        {
            if (playerInstance == null) return;

            _remotePlayers[clientId] = playerInstance.transform;
            Debug.Log($"[PlayerService] Remote player registered: {clientId}");
        }

        public void UnregisterPlayer()
        {
            UnregisterLocalPlayer();
        }

        public void UnregisterLocalPlayer()
        {
            LocalPlayerTransform = null;
            Debug.Log("[PlayerService] Local player unregistered");
        }

        public void UnregisterRemotePlayer(int clientId)
        {
            if (_remotePlayers.Remove(clientId))
                Debug.Log($"[PlayerService] Remote player unregistered: {clientId}");
        }

        public void ClearRemotePlayers()
        {
            _remotePlayers.Clear();
            Debug.Log("[PlayerService] Remote players cleared");
        }
    }
}
