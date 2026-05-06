using UnityEngine;
using Zenject;

namespace Physics.Water
{
    public class FloatingObject : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Расстояние между точками замера для вычисления наклона. Чем больше объект, тем больше число.")]
        [SerializeField] private float _sampleDistance = 0.5f;
    
        [Tooltip("Смещение по высоте")]
        [SerializeField] private float _heightOffset = 0.0f;

        private WaterPhysicsSystem _waterSystem;

        [Inject]
        public void Construct(WaterPhysicsSystem waterSystem)
        {
            _waterSystem = waterSystem;
        }
        
        private void Update()
        {
            if (_waterSystem == null) 
            {
                Debug.LogError("WaterSystem is NULL on Buoy!");
                return;
            }
            UpdateFloating();
        }

        private void UpdateFloating()
        {
            Vector3 myPos = transform.position;
            float heightCenter = _waterSystem.GetWaterHeightAt(myPos);
        
            transform.position = new Vector3(myPos.x, heightCenter + _heightOffset, myPos.z);
        
            Vector3 posForward = myPos + transform.forward * _sampleDistance;
            Vector3 posRight = myPos + transform.right * _sampleDistance;

            float heightForward = _waterSystem.GetWaterHeightAt(posForward);
            float heightRight = _waterSystem.GetWaterHeightAt(posRight);
        
            Vector3 vForward = new Vector3(0, heightForward - heightCenter, _sampleDistance);
        
            Vector3 vRight = new Vector3(_sampleDistance, heightRight - heightCenter, 0);
        
            Vector3 normal = Vector3.Cross(vForward, vRight).normalized;
        
            Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, normal);
        
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation * Quaternion.Euler(0, transform.eulerAngles.y, 0), Time.deltaTime * 5f);
        }
    }
}