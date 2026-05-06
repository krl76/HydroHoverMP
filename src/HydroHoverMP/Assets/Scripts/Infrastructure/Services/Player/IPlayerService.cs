using Physics.Hover;
using UnityEngine;

namespace Infrastructure.Services.Player
{
    public interface IPlayerService
    {
        Transform Transform { get; }
        
        bool IsPlayerCreated { get; }
        
        void RegisterPlayer(GameObject playerInstance);
        void UnregisterPlayer();
    }
}