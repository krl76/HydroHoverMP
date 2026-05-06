using Physics.Hover;
using UnityEngine;

namespace Infrastructure.Services.Player
{
    public class PlayerService : IPlayerService
    {
        public Transform Transform { get; private set; }
        public bool IsPlayerCreated => Transform != null;

        public void RegisterPlayer(GameObject playerInstance)
        {
            Transform = playerInstance.transform;
        
            Debug.Log("[PlayerService] Player Registered");
        }

        public void UnregisterPlayer()
        {
            Transform = null;
            Debug.Log("[PlayerService] Player Unregistered");
        }
    }
}