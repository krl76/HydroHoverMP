using System;
using Features.Trigger.Base;
using UnityEngine;

namespace Features.Trigger
{
    public class CheckpointTrigger : BaseTrigger
    {
        public int Index { get; set; }
        public event Action<int> OnPlayerEntered;
        private bool _isPassed = false;
        
        public override void OnPlayerEnter(Collider other)
        {
            if (_isPassed) return;
            
            Vector3 playerVel = other.attachedRigidbody.linearVelocity;
            if (Vector3.Dot(playerVel.normalized, transform.forward) > 0)
            {
                _isPassed = true;
                OnPlayerEntered?.Invoke(Index);
            }
        }
        
        public void ResetState()
        {
            _isPassed = false;
        }
        
        public override void OnPlayerStay(Collider other) { }

        public override void OnPlayerExit(Collider other) { }
    }
}