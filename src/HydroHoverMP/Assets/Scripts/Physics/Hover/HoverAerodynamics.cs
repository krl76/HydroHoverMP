using Physics.Enviroment;
using Physics.Water;
using UnityEngine;
using Zenject;

namespace Physics.Hover
{
    [RequireComponent(typeof(Rigidbody))]
    public class HoverAerodynamics : MonoBehaviour
    {
        [Header("Air Drag (Сопротивление воздуха)")]
        [SerializeField] private float _airDensity = 1.225f;
        [SerializeField] private float _frontalArea = 2.0f; // Площадь лобового сечения (м2)
        [SerializeField] private float _dragCoefficient = 0.3f; // Cx (обтекаемость)

        [Header("Water Drag (Торможение об воду)")]
        [SerializeField] private float _waterDensity = 1000f;
        [SerializeField] private float _waterDragCoeff = 1.0f;
    
        [Header("Steering (Руление)")]
        [SerializeField] private float _rudderTorque = 5000f; // Сила рулей
        [SerializeField] private float _sideDrag = 0.5f; // Боковое сопротивление

        private Rigidbody _rb;
        private WindSystem _wind;
        private HoverCushion _cushion;
        private WaterPhysicsSystem _waterSystem;
        
        public float SteerInput { get; set; }

        [Inject]
        public void Construct(WindSystem wind, WaterPhysicsSystem water)
        {
            _wind = wind;
            _waterSystem = water;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _cushion = GetComponent<HoverCushion>();
        }

        private void FixedUpdate()
        {
            if (_wind == null) return;

            ApplyAirDrag();
            ApplyWaterDrag();
            ApplySteering();
            ApplySideDrag();
        }

        private void ApplyAirDrag()
        {
            // 1. Относительная скорость
            Vector3 relativeVelocity = _rb.linearVelocity - _wind.WindVector;
            float speed = relativeVelocity.magnitude;

            if (speed < 0.1f) return;

            // F = 0.5 * rho * Cx * A * v^2
            float forceMagnitude = 0.5f * _airDensity * _dragCoefficient * _frontalArea * speed * speed;
            
            Vector3 force = -relativeVelocity.normalized * forceMagnitude;

            _rb.AddForce(force, ForceMode.Force);
        }

        private void ApplyWaterDrag()
        {
            foreach (var point in _cushion.HoverPoints)
            {
                float waterH = _waterSystem.GetWaterHeightAt(point.position);
                
                if (point.position.y < waterH)
                {
                    Vector3 pointVelocity = _rb.GetPointVelocity(point.position);
                    float speed = pointVelocity.magnitude;

                    if (speed < 0.1f) continue;
                    
                    float pointArea = (_frontalArea * 0.25f); 

                    // Сила сопротивления: F = 0.5 * rho * Cd * A * v^2
                    float forceMag = 0.5f * _waterDensity * _waterDragCoeff * pointArea * speed * speed;
                    
                    forceMag = Mathf.Clamp(forceMag, 0, 100000f);

                    Vector3 dragForce = -pointVelocity.normalized * forceMag;
                    
                    _rb.AddForceAtPosition(dragForce, point.position, ForceMode.Force);
                }
            }
        }

        private void ApplySteering()
        {
            float speedFactor = Mathf.Clamp01(_rb.linearVelocity.magnitude / 5f);
            
            float torque = SteerInput * _rudderTorque * (0.2f + 0.8f * speedFactor);
        
            _rb.AddRelativeTorque(Vector3.up * torque, ForceMode.Force);
        }

        private void ApplySideDrag()
        {
            Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);
            
            float sideSpeed = localVel.x;
            float dragForce = -sideSpeed * _sideDrag * _rb.mass; // F = -kv

            _rb.AddRelativeForce(Vector3.right * dragForce, ForceMode.Force);
        }
    }
}