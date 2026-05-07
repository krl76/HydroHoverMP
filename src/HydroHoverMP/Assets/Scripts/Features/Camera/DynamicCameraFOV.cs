using Cinemachine;
using Infrastructure.Services.Player;
using UnityEngine;
using Zenject;

namespace Features.Camera
{
    public class DynamicCameraFOV : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _minFOV = 60f;
        [SerializeField] private float _maxFOV = 90f;
        [SerializeField] private float _zoomSpeed = 5f;
        
        [SerializeField] private float _maxSpeedForEffect = 50f; 

        private CinemachineVirtualCamera _vcam;
        private Rigidbody _targetRb;
        private Transform _targetTransform;
        
        private IPlayerService _playerService;

        [Inject]
        public void Construct(IPlayerService playerService)
        {
            _playerService =  playerService;
        }

        private void Awake()
        {
            _vcam = GetComponent<CinemachineVirtualCamera>();
        }

        private void Update()
        {
            if (_playerService == null || _vcam == null) return;

            if (!_playerService.IsLocalPlayerCreated)
            {
                _targetRb = null;
                _targetTransform = null;
                return;
            }

            Transform localPlayerTransform = _playerService.LocalPlayerTransform;
            if (localPlayerTransform != _targetTransform)
            {
                _targetTransform = localPlayerTransform;
                _targetRb = _targetTransform != null
                    ? _targetTransform.GetComponent<Rigidbody>()
                    : null;
            }

            if (_targetRb == null) return;

            float currentSpeed = _targetRb.linearVelocity.magnitude;
            float t = Mathf.Clamp01(currentSpeed / _maxSpeedForEffect);
        
            float targetFOV = Mathf.Lerp(_minFOV, _maxFOV, t);
            
            _vcam.m_Lens.FieldOfView = Mathf.Lerp(_vcam.m_Lens.FieldOfView, targetFOV, Time.deltaTime * _zoomSpeed);
        }
    }
}
