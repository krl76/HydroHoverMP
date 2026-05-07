using Physics.Hover;
using UnityEngine;

namespace Features.Audio
{
    public class HoverAudioController : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private AudioSource _liftSource;
        [SerializeField] private AudioSource _thrustSource;
        [SerializeField] private AudioSource _windSource;
        [SerializeField] private AudioSource _waterSource;

        [Header("Settings")]
        [SerializeField] private float _minPitch = 0.8f;
        [SerializeField] private float _maxPitch = 1.5f;
        [SerializeField] private float _waterImpactCooldown = 0.25f;

        private HoverController _controller;
        private HoverCushion _cushion;
        private float _nextWaterImpactTime;

        private void Awake()
        {
            _controller = GetComponent<HoverController>();
            _cushion = GetComponent<HoverCushion>();

            if (_cushion != null)
                _cushion.OnWaterImpact += OnWaterImpact;
        }

        private void Update()
        {
            if (_controller == null) return;
            if (_controller.LiftEngine == null || _controller.ThrustEngine == null || _controller.Rb == null) return;

            // 1. Lift (зависит от оборотов подъемного двигателя)
            float liftRatio = _controller.LiftEngine.CurrentRPM / _controller.LiftEngine.MaxRPM;
            if (_liftSource != null)
            {
                _liftSource.volume = 0.2f + liftRatio * 0.5f;
                _liftSource.pitch = Mathf.Lerp(_minPitch, _maxPitch, liftRatio);
            }

            // 2. Thrust (зависит от маршевого двигателя)
            float thrustRatio = _controller.ThrustEngine.CurrentRPM / _controller.ThrustEngine.MaxRPM;
            if (_thrustSource != null)
            {
                _thrustSource.volume = 0.3f + thrustRatio * 0.7f;
                _thrustSource.pitch = Mathf.Lerp(0.8f, 1.3f, thrustRatio);
            }

            // 3. Wind (зависит от скорости)
            float speed = _controller.Rb.linearVelocity.magnitude;
            if (_windSource != null)
                _windSource.volume = Mathf.Clamp01(speed / 40f);
        }

        private void OnDestroy()
        {
            if (_cushion != null)
                _cushion.OnWaterImpact -= OnWaterImpact;
        }

        private void OnWaterImpact(float impact)
        {
            if (_waterSource == null || Time.time < _nextWaterImpactTime) return;

            _nextWaterImpactTime = Time.time + _waterImpactCooldown;
            _waterSource.volume = Mathf.Clamp01(impact);
            _waterSource.Play();
        }
    }
}
