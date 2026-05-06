using Infrastructure.Services.Input;
using UnityEngine;
using Zenject;

namespace Physics.Hover
{
    [RequireComponent(typeof(Rigidbody))]
    public class HoverController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private HoverCushion _cushion;
        [SerializeField] private HoverEngine _liftEngine;
        [SerializeField] private HoverEngine _thrustEngine;
        [SerializeField] private Transform _thrustPoint;

        [Header("Power Config")]
        [SerializeField] private float _forwardForceMultiplier = 5000f;
        
        [Header("Braking")]
        [Tooltip("Сила торможения (применяется против вектора скорости)")]
        [SerializeField] private float _brakeForce = 20f; 
        
        [Header("Aerodynamics")]
        [SerializeField] private HoverAerodynamics _aerodynamics;
        
        public HoverEngine LiftEngine => _liftEngine;
        public HoverEngine ThrustEngine => _thrustEngine;
        public Rigidbody Rb => _rb;
        public bool InputEnabled { get; private set; } = true;

        private Rigidbody _rb;
        private IInputService _input;

        [Inject]
        public void Construct(IInputService input)
        {
            _input = input;
        }

        public void SetInputService(IInputService input)
        {
            _input = input;
        }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
            if (!enabled)
            {
                if (_liftEngine) _liftEngine.SetThrottle(0f);
                if (_thrustEngine) _thrustEngine.SetThrottle(0f);
                if (_aerodynamics) _aerodynamics.SteerInput = 0f;
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_cushion == null) _cushion = GetComponent<HoverCushion>();
            
            if (_cushion != null && _cushion.CenterOfMass != null)
            {
                _rb.centerOfMass = _cushion.CenterOfMass.localPosition;
            }
        }

        private void Update()
        {
            if (!InputEnabled) return;
            ResolveInputIfNeeded();
            if (_input == null) return;
            
            float liftInput = _input.LiftInput;
            Vector2 moveInput = _input.MoveInput;
            
            float finalLift = Mathf.Clamp01(0.5f + liftInput * 0.5f);
        
            if (_liftEngine) _liftEngine.SetThrottle(finalLift);
            if (_thrustEngine) _thrustEngine.SetThrottle(moveInput.y);
            if (_aerodynamics) _aerodynamics.SteerInput = _input.MoveInput.x;
        }

        private void FixedUpdate()
        {
            if (!InputEnabled) return;

            float dt = Time.fixedDeltaTime;
            
            if (_liftEngine) _liftEngine.CalculatePhysics(dt);
            if (_thrustEngine) _thrustEngine.CalculatePhysics(dt);
            
            if (_cushion != null && _liftEngine != null)
            {
                float liftFactor = _liftEngine.CurrentRPM / _liftEngine.MaxRPM;
                _cushion.LiftEfficiency = liftFactor;
            }
            
            if (_input != null && _input.HandbrakeInput)
            {
                ApplyBrakes();
            }
            else
            {
                ApplyThrust();
            }
        }
        
        private void ApplyBrakes()
        {
            Vector3 velocity = _rb.linearVelocity;
            
            if (velocity.sqrMagnitude > 0.1f)
            {
                _rb.AddForce(-velocity.normalized * _brakeForce, ForceMode.Acceleration);
            }
        }

        private void ApplyThrust()
        {
            if (_thrustPoint != null && _thrustEngine != null)
            {
                Vector3 force = transform.forward * (_thrustEngine.CurrentTorque * _forwardForceMultiplier);
                _rb.AddForceAtPosition(force, _thrustPoint.position);
            }
        }

        public void ApplyHydroPulse(float impulseForce)
        {
            if (_rb == null) return;

            _rb.AddForce(transform.forward * impulseForce, ForceMode.Impulse);
        }

        private void ResolveInputIfNeeded()
        {
            if (_input != null || !Zenject.ProjectContext.HasInstance) return;

            _input = Zenject.ProjectContext.Instance.Container.TryResolve<IInputService>();
        }
    }
}
