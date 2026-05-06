using System;
using System.Collections.Generic;
using Features.Trigger;
using UnityEngine;

namespace Infrastructure.Services.RaceManager
{
    public class RaceManagerService : IRaceManagerService
    {
        public event Action OnRaceStarted;
        public event Action OnRaceFinished;
        public event Action<int> OnCheckpointPassed;
        public event Action OnWrongCheckpoint;

        private List<CheckpointTrigger> _checkpoints = new();
        private int _currentIndex = 0;
        private float _startTime;
        private bool _isActive;
        private float _finishTime;

        public bool IsRaceActive => _isActive;
        
        public float CurrentTime
        {
            get
            {
                if (_isActive) return Time.time - _startTime;
                return _finishTime;
            }
        }
        
        public int CurrentCheckpointIndex => _currentIndex;
        public int TotalCheckpoints => _checkpoints.Count;

        public Vector3? NextCheckpointPosition
        {
            get
            {
                if (_checkpoints == null || _checkpoints.Count == 0) return null;
                if (_currentIndex >= _checkpoints.Count) return null;
                return _checkpoints[_currentIndex].transform.position;
            }
        }

        public void RegisterTrack(List<CheckpointTrigger> checkpoints)
        {
            _checkpoints = checkpoints;
            
            for (int i = 0; i < _checkpoints.Count; i++)
            {
                var cp = _checkpoints[i];
                cp.Index = i;
                cp.OnPlayerEntered -= HandleCheckpointEnter;
                cp.OnPlayerEntered += HandleCheckpointEnter;
                cp.ResetState();
            }
        
            Debug.Log($"[RaceService] Track registered: {_checkpoints.Count} checkpoints.");
        }

        public void StartRace()
        {
            if (_checkpoints.Count == 0)
            {
                Debug.LogError("[RaceService] Cannot start race: No checkpoints!");
                return;
            }

            _currentIndex = 0;
            _finishTime = 0f;
            _startTime = Time.time;
            _isActive = true;
            
            foreach (var cp in _checkpoints) cp.ResetState();

            OnRaceStarted?.Invoke();
            Debug.Log("[RaceService] Race Started!");
        }

        public void FinishRace()
        {
            if (!_isActive) return;
    
            _finishTime = Time.time - _startTime;
            _isActive = false;
    
            OnRaceFinished?.Invoke();
            Debug.Log($"[RaceService] Finished! Final Time: {_finishTime:F2}");
        }

        private void HandleCheckpointEnter(int index)
        {
            if (!_isActive) return;

            if (index == _currentIndex)
            {
                _currentIndex++;
                OnCheckpointPassed?.Invoke(index);

                if (_currentIndex >= _checkpoints.Count)
                {
                    FinishRace();
                }
            }
            else if (index > _currentIndex)
            {
                OnWrongCheckpoint?.Invoke();
                Debug.LogWarning($"Wrong Checkpoint! Need {_currentIndex}, got {index}");
            }
        }
    }
}