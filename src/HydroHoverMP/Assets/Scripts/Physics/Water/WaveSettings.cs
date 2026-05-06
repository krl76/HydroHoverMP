using UnityEngine;

namespace Physics.Water
{
    [CreateAssetMenu(fileName = "WaveSettings", menuName = "HydroHover/WaveSettings")]
    public class WaveSettings : ScriptableObject
    {
        // Основная волна
        [Header("Wave 1")]
        public float Wavelength1 = 20f;
        public float Amplitude1 = 1.5f;
        public float Speed1 = 1f;
        public Vector2 Direction1 = new Vector2(1, 0.5f);

        // Детализация
        [Header("Wave 2")]
        public float Wavelength2 = 10f;
        public float Amplitude2 = 0.5f;
        public float Speed2 = 1.2f;
        public Vector2 Direction2 = new Vector2(0.5f, 1f);
    }
}