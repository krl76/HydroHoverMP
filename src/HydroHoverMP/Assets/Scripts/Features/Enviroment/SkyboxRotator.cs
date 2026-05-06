using UnityEngine;

namespace Features.Enviroment
{
    public class SkyboxRotator : MonoBehaviour
    {
        [SerializeField] private float _rotationSpeed = 1.0f;

        private void Update()
        {
            float currentRot = RenderSettings.skybox.GetFloat("_Rotation");
            RenderSettings.skybox.SetFloat("_Rotation", currentRot + _rotationSpeed * Time.deltaTime);
        }
    }
}