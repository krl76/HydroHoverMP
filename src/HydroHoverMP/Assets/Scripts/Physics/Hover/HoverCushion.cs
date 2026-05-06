using System;
using Physics.Water;
using UnityEngine;
using Zenject;

public class HoverCushion : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private Transform _centerOfMass;
    [SerializeField] private Transform[] _hoverPoints;

    [Header("Settings")]
    [Tooltip("Желаемая высота полета над водой")]
    [SerializeField] private float _hoverHeight = 0.8f;
    
    [Tooltip("Жесткость пружины")]
    [SerializeField] private float _springForce = 20000f;
    
    [Tooltip("Сила гашения колебаний")]
    [SerializeField] private float _damperForce = 2000f;
    
    public Transform CenterOfMass => _centerOfMass;
    public Transform[] HoverPoints => _hoverPoints;
    
    public float LiftEfficiency { get; set; } = 1.0f;
    public event Action<float> OnWaterImpact;
    
    
    private Rigidbody _rb;
    private WaterPhysicsSystem _waterSystem;

    [Inject]
    public void Construct(WaterPhysicsSystem waterSystem)
    {
        _waterSystem = waterSystem;
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        
        if (_centerOfMass != null)
        {
            _rb.centerOfMass = _centerOfMass.localPosition;
        }
    }

    private void FixedUpdate()
    {
        if (_hoverPoints == null) return;
        
        float maxImpact = 0f;

        foreach (var point in _hoverPoints)
        {
            if (point != null)
            {
                float impact = CalculateImpact(point);
                if (impact > maxImpact) maxImpact = impact;
                
                ApplyForceAtPoint(point);
            }
        }
        
        if (maxImpact > 0.3f)
        {
            OnWaterImpact?.Invoke(maxImpact);
        }
    }

    private void ApplyForceAtPoint(Transform point)
    {
        if (_waterSystem == null) return;
        
        float waterHeight = _waterSystem.GetWaterHeightAt(point.position);
        
        float currentY = point.position.y;
        
        float heightDiff = currentY - waterHeight;
        
        if (heightDiff < _hoverHeight)
        {
            // Сжатие пружины
            float compression = (_hoverHeight - heightDiff) / _hoverHeight;
            
            // F_spring = K * x
            float springForce = _springForce * compression * LiftEfficiency;

            // F_damper = -C * v (вертикальная скорость в этой точке)
            float verticalVelocity = _rb.GetPointVelocity(point.position).y;
            float dampingForce = -verticalVelocity * _damperForce;

            // Итоговая сила
            float totalForce = Mathf.Max(0, springForce + dampingForce);

            _rb.AddForceAtPosition(Vector3.up * totalForce, point.position);
        }
    }
    
    private float CalculateImpact(Transform point)
    {
        float waterHeight = _waterSystem.GetWaterHeightAt(point.position);
        if (point.position.y < waterHeight)
        {
            float yVel = _rb.GetPointVelocity(point.position).y;
            if (yVel < -2f)
            {
                return Mathf.Clamp01(Mathf.Abs(yVel) / 10f);
            }
        }
        return 0f;
    }

    private void OnDrawGizmos()
    {
        if (_centerOfMass != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_centerOfMass.position, 0.2f);
        }
        
        if (_hoverPoints != null)
        {
            Gizmos.color = Color.yellow;
            foreach (var p in _hoverPoints)
            {
                if(p != null) Gizmos.DrawSphere(p.position, 0.1f);
            }
        }
    }
}