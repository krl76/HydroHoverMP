using System;
using System.Collections;
using Features.Networking;
using Infrastructure.Services.Player;
using Infrastructure.Services.RaceManager;
using Physics.Hover;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.HUD
{
    public class HUDWindow : MonoBehaviour
    {
        [Header("Texts")]
        [SerializeField] private TextMeshProUGUI _speedText;
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private TextMeshProUGUI _checkpointText;
        [SerializeField] private TextMeshProUGUI _fpsText;
        
        [Header("Speedometer")]
        [SerializeField] private RectTransform _speedNeedle;
        [SerializeField] private float _minSpeed = 0f;
        [SerializeField] private float _maxSpeed = 200f;
        [SerializeField] private float _minAngle = 135f;
        [SerializeField] private float _maxAngle = -135f;
        
        [Header("Bars")]
        [SerializeField] private Image _liftBar;
        [SerializeField] private Image _thrustBar;

        private IPlayerService _playerService;
        private IRaceManagerService _raceManagerService;
        private HoverController _hoverController;
        private NetworkPlayerData _networkPlayerData;
        
        private int _lastSpeed = -1;
        private int _lastSeconds = -1;
        private int _lastCheckpointIndex = -1;
        private int _lastFps = -1;

        [Inject]
        public void Construct(IPlayerService playerService, IRaceManagerService raceManagerService)
        {
            _playerService = playerService;
            _raceManagerService = raceManagerService;
        }

        private void Start()
        {
            StartCoroutine(UpdateGameMetrics());
            
            _raceManagerService.OnRaceStarted += UpdateRaceInfoUI;
            _raceManagerService.OnCheckpointPassed += OnCheckpointPassedHandler;
            
            if (_raceManagerService.IsRaceActive)
            {
                UpdateRaceInfoUI();
            }
        }

        private void Update()
        {
            if (!_playerService.IsPlayerCreated) return;
            
            if (_hoverController == null)
            {
                _hoverController = _playerService.Transform.gameObject.GetComponent<HoverController>();
                return;
            }

            UpdatePhysicsUI();
            UpdateNetworkOrRaceInfoUI();
        }

        private void UpdatePhysicsUI()
        {
            var rb = _hoverController.Rb;
            
            float rawSpeed = rb.linearVelocity.magnitude * 3.6f;
            int displaySpeed = Mathf.RoundToInt(rawSpeed);
            
            if (displaySpeed != _lastSpeed)
            {
                _speedText.text = $"{displaySpeed} km/h";
                _lastSpeed = displaySpeed;
            }
            
            float t = Mathf.InverseLerp(_minSpeed, _maxSpeed, rawSpeed);
            float angle = Mathf.Lerp(_minAngle, _maxAngle, t);
            _speedNeedle.localRotation = Quaternion.Euler(0, 0, angle);
            
            var lift = _hoverController.LiftEngine;
            var thrust = _hoverController.ThrustEngine;

            if (_liftBar) _liftBar.fillAmount = lift.CurrentRPM / lift.MaxRPM;
            if (_thrustBar) _thrustBar.fillAmount = thrust.CurrentRPM / thrust.MaxRPM;
        }

        private void UpdateRaceInfoUI()
        {
            float currentTime = _raceManagerService.CurrentTime;

            int minutes = (int)(currentTime / 60);
            int seconds = (int)(currentTime % 60);
            int milliseconds = (int)((currentTime * 100) % 100);
            
            _timerText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
            
            int currentCp = _raceManagerService.CurrentCheckpointIndex;
            if (currentCp != _lastCheckpointIndex)
            {
                _checkpointText.text = $"{currentCp} / {_raceManagerService.TotalCheckpoints}";
                _lastCheckpointIndex = currentCp;
            }
        }


        private void UpdateNetworkOrRaceInfoUI()
        {
            if (_networkPlayerData == null && _playerService.IsLocalPlayerCreated)
                _networkPlayerData = _playerService.LocalPlayerTransform.GetComponent<NetworkPlayerData>();

            if (_networkPlayerData == null)
            {
                UpdateRaceInfoUI();
                return;
            }

            _timerText.text = NetworkSessionController.Instance != null
                ? NetworkSessionController.Instance.Phase.Value.ToString()
                : "Network";

            _checkpointText.text =
                $"CP {_networkPlayerData.CheckpointIndex.Value} / HP {_networkPlayerData.HP.Value} / Score {_networkPlayerData.Score.Value}";
        }

        private IEnumerator UpdateGameMetrics()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                int fps = Mathf.RoundToInt(1f / Time.unscaledDeltaTime);
                
                if (fps != _lastFps && _fpsText != null)
                {
                    _fpsText.text = $"FPS: {fps}";
                    _lastFps = fps;
                    
                    _fpsText.color = fps < 30 ? Color.red : Color.green;
                }
                
                yield return wait;
            }
        }
        
        private void OnCheckpointPassedHandler(int index)
        {
            UpdateRaceInfoUI();
        }
        
        private void OnDestroy()
        {
            if (_raceManagerService != null)
            {
                _raceManagerService.OnRaceStarted -= UpdateRaceInfoUI;
                _raceManagerService.OnCheckpointPassed -= OnCheckpointPassedHandler;
            }
        }
    }
}