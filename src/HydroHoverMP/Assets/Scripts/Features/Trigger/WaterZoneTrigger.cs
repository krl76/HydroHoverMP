using Features.Trigger.Base;
using Physics.Water;
using UnityEngine;
using Zenject;

namespace Features.Trigger
{
    public class WaterZoneTrigger : BaseTrigger
    {
        [Tooltip("Множитель высоты волн (1 = норма, 0 = штиль, 2 = шторм)")]
        [SerializeField] private float _waveMultiplier = 2.0f;
        
        private WaterPhysicsSystem _waterSystem;
        
        [Inject]
        public void Construct(WaterPhysicsSystem waterSystem)
        {
            _waterSystem = waterSystem;
        }

        public override void OnPlayerEnter(Collider other)
        {
            if (_waterSystem != null)
            {
                _waterSystem.SetRoughness(_waveMultiplier);
                Debug.Log("WaveMultiplier: " + _waveMultiplier);
            }
        }

        public override void OnPlayerStay(Collider other) { }

        public override void OnPlayerExit(Collider other) { }
    }
}