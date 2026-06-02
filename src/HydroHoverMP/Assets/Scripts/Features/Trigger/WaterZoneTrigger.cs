using Features.Trigger.Base;
using Physics.Water;
using UnityEngine;

namespace Features.Trigger
{
    public class WaterZoneTrigger : BaseTrigger
    {
        [Tooltip("Множитель высоты волн (1 = норма, 0 = штиль, 2 = шторм)")]
        [SerializeField] private float _waveMultiplier = 2.0f;

        private WaterPhysicsSystem _waterSystem;

        public override void OnPlayerEnter(Collider other)
        {
            ResolveWaterSystemIfNeeded();
            if (_waterSystem != null)
            {
                _waterSystem.SetRoughness(_waveMultiplier);
                Debug.Log("WaveMultiplier: " + _waveMultiplier);
            }
        }

        public override void OnPlayerStay(Collider other) { }

        public override void OnPlayerExit(Collider other) { }

        // Зоны воды лежат в сцене Level, чей Zenject-контекст наследуется от контракта GameplayContext.
        // На выделенном сервере Gameplay может дублироваться, ломая прямую инъекцию WaterPhysicsSystem
        // ("multiple matches"). Поэтому DI не используем, а ищем систему в сцене — как FloatingObject.
        private void ResolveWaterSystemIfNeeded()
        {
            if (_waterSystem == null)
                _waterSystem = FindFirstObjectByType<WaterPhysicsSystem>();
        }
    }
}
