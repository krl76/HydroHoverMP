using System;
using Infrastructure.Services.Player;
using Infrastructure.Services.RaceManager;
using Infrastructure.Services.SceneManagement;
using UnityEngine;
using Zenject;

namespace Features.Checkpoint
{
    public class CheckpointPointer : MonoBehaviour
    {
        private IRaceManagerService _raceManagerService;
        private IPlayerService _playerService;

        [Inject]
        public void Construct(IRaceManagerService raceManagerService, IPlayerService playerService)
        {
            _raceManagerService = raceManagerService;
            _playerService = playerService;
        }
        
        private void Update()
        {
            if(!_playerService.IsPlayerCreated) return;
            
            var target = _raceManagerService.NextCheckpointPosition;
            if (target.HasValue)
            {
                Vector3 direction = target.Value - _playerService.Transform.position;
                direction.y = 0;
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }

        private void FixedUpdate()
        {
            if(_playerService.IsPlayerCreated) transform.position = _playerService.Transform.position + Vector3.up * 1.5f;
        }
    }
}