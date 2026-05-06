using UnityEngine;

namespace Features.Enviroment
{
    public class SkyboxRotator : MonoBehaviour
    {
        [SerializeField] private float _rotationSpeed = 1.0f;
        
        private Material _skybox;

        private void Awake()
        {
            if (RenderSettings.skybox == null) return;

            _skybox = new Material(RenderSettings.skybox);
            RenderSettings.skybox = _skybox;
        }

        private void Update()
        {
            if (_skybox == null || !_skybox.HasProperty("_Rotation")) return;

            float currentRot = _skybox.GetFloat("_Rotation");
            _skybox.SetFloat("_Rotation", currentRot + _rotationSpeed * Time.deltaTime);
        }
    }
}
