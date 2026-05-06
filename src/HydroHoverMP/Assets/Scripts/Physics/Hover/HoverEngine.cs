using UnityEngine;

public class HoverEngine : MonoBehaviour
{
    [Header("Specs")]
    [SerializeField] private float _maxRPM = 6000f;
    [SerializeField] private float _idleRPM = 800f;
    [Tooltip("График зависимости крутящего момента от оборотов.")]
    [SerializeField] private AnimationCurve _torqueCurve;
    
    [Header("Inertia")]
    [Tooltip("Инерция маховика. Больше значение -> медленнее набирает обороты.")]
    [SerializeField] private float _inertia = 0.5f;
    [SerializeField] private float _responseSpeed = 2f;
    
    public float CurrentRPM { get; private set; }
    public float CurrentTorque { get; private set; }
    public float MaxRPM => _maxRPM;

    private float _targetThrottle;

    private void Awake()
    {
        CurrentRPM = _idleRPM;
        
        if (_torqueCurve == null || _torqueCurve.length == 0)
        {
            _torqueCurve = new AnimationCurve();
            _torqueCurve.AddKey(0, 0.5f);
            _torqueCurve.AddKey(_maxRPM / 2, 1f);
            _torqueCurve.AddKey(_maxRPM, 0.8f);
        }
    }
    
    public void SetThrottle(float input)
    {
        _targetThrottle = Mathf.Clamp(input, -1f, 1f);
    }
    
    public void CalculatePhysics(float dt)
    {
        // 1. Целевые обороты
        float targetRPM = Mathf.Abs(_targetThrottle) > 0.01f 
            ? _maxRPM * Mathf.Abs(_targetThrottle) 
            : _idleRPM;

        // 2. Инерция (интерполяция)
        float lerpFactor = dt * _responseSpeed * (1f / Mathf.Max(_inertia, 0.001f));
        CurrentRPM = Mathf.Lerp(CurrentRPM, targetRPM, lerpFactor);

        // 3. Расчет момента
        float torqueFactor = _torqueCurve.Evaluate(CurrentRPM);
        CurrentTorque = torqueFactor * _targetThrottle;
    }
}