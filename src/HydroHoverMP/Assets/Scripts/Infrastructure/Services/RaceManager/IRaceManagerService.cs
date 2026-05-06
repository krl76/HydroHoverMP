using System;
using System.Collections.Generic;
using Features.Trigger;
using UnityEngine;

namespace Infrastructure.Services.RaceManager
{
    public interface IRaceManagerService
    {
        float CurrentTime { get; }
        int CurrentCheckpointIndex { get; }
        int TotalCheckpoints { get; }
        Vector3? NextCheckpointPosition { get; }
        bool IsRaceActive { get; }
        
        event Action OnRaceStarted;
        event Action OnRaceFinished;
        event Action<int> OnCheckpointPassed;
        event Action OnWrongCheckpoint;
        
        void RegisterTrack(List<CheckpointTrigger> checkpoints);
        void StartRace();
        void FinishRace();
    }
}