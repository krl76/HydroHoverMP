using UnityEngine;
using Zenject;

namespace Physics.Water
{
    public class WaterPhysicsSystem : MonoBehaviour
    {
        [SerializeField] private WaveSettings _settings;
    
        private static readonly int Wave1Params = Shader.PropertyToID("_Wave1Params"); // x=len, y=amp, z=speed
        private static readonly int Wave1Dir = Shader.PropertyToID("_Wave1Dir");
        private static readonly int Wave2Params = Shader.PropertyToID("_Wave2Params");
        private static readonly int Wave2Dir = Shader.PropertyToID("_Wave2Dir");
    
        private float _amplitudeMultiplier = 1f;
        private float _targetMultiplier = 1f;
    
        [Inject]
        public void Construct(WaveSettings settings)
        {
            _settings = settings;
        }

        private void Update()
        {
            _amplitudeMultiplier = Mathf.MoveTowards(_amplitudeMultiplier, _targetMultiplier, Time.deltaTime * 0.5f);
            
            UpdateShaderGlobals();
        }

        private void UpdateShaderGlobals()
        {
            if (_settings == null) return;
        
            float finalAmp1 = _settings.Amplitude1 * _amplitudeMultiplier;
            float finalAmp2 = _settings.Amplitude2 * _amplitudeMultiplier;
        
            Shader.SetGlobalVector(Wave1Params, new Vector4(_settings.Wavelength1, finalAmp1, _settings.Speed1, 0));
            Shader.SetGlobalVector(Wave1Dir, _settings.Direction1.normalized);
        
            Shader.SetGlobalVector(Wave2Params, new Vector4(_settings.Wavelength2, finalAmp2, _settings.Speed2, 0));
            Shader.SetGlobalVector(Wave2Dir, _settings.Direction2.normalized);
        }
    
        public float GetWaterHeightAt(Vector3 worldPos)
        {
            float baseHeight = transform.position.y;
            if (_settings == null)
                return baseHeight;

            float time = Time.time;
        
            float finalAmp1 = _settings.Amplitude1 * _amplitudeMultiplier;
            float finalAmp2 = _settings.Amplitude2 * _amplitudeMultiplier;
        
            float y = baseHeight;
            y += CalculateGerstnerWave(worldPos, _settings.Wavelength1, finalAmp1, _settings.Speed1, _settings.Direction1, time);
            y += CalculateGerstnerWave(worldPos, _settings.Wavelength2, finalAmp2, _settings.Speed2, _settings.Direction2, time);
        
            return y;
        }

        private float CalculateGerstnerWave(Vector3 p, float wavelength, float amp, float speed, Vector2 dir, float time)
        {
            // k = 2 * PI / wavelength
            float k = 2 * Mathf.PI / wavelength;
            // c = speed (phase speed) = sqrt(g / k) - в упрощенной модели просто speed
            float f = k * (Vector2.Dot(dir.normalized, new Vector2(p.x, p.z)) - speed * time);
        
            // Формула Герстнера для Y: A * sin(f)
            // Для более острых волн (Trochoidal) формула сложнее, но начнем с синуса,
            // так как для физики подушки важна высота, а не острый гребень.
            return amp * Mathf.Sin(f);
        }
    
        public void SetRoughness(float multiplier)
        {
            _targetMultiplier = multiplier;
        }
    }
}
