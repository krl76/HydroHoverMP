using TMPro;
using UnityEngine;

namespace Features.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerData))]
    public sealed class PlayerNameplate : MonoBehaviour
    {
        [SerializeField] private TextMeshPro _label;
        [SerializeField] private Vector3 _worldOffset = new(0f, 2.4f, 0f);
        [SerializeField] private Color _ownerColor = new(0.4f, 0.9f, 1f);
        [SerializeField] private Color _remoteColor = Color.white;
        [SerializeField] private Color _readyColor = new(0.57f, 1f, 0.67f);
        [SerializeField] private Color _damagedColor = new(1f, 0.68f, 0.34f);

        private NetworkPlayerData _playerData;
        private FishNet.Object.NetworkBehaviour _networkBehaviour;
        private UnityEngine.Camera _camera;

        private void Awake()
        {
            _playerData = GetComponent<NetworkPlayerData>();
            _networkBehaviour = GetComponent<FishNet.Object.NetworkBehaviour>();
            EnsureLabel();
        }

        private void OnEnable()
        {
            if (_playerData != null)
            {
                _playerData.OnAnyValueChanged += OnPlayerValueChanged;
                Refresh();
            }
        }

        private void OnDisable()
        {
            if (_playerData != null)
                _playerData.OnAnyValueChanged -= OnPlayerValueChanged;
        }

        private void LateUpdate()
        {
            if (_label == null) return;

            _camera ??= UnityEngine.Camera.main;
            if (_camera == null) return;

            Transform labelTransform = _label.transform;
            labelTransform.position = transform.position + _worldOffset;
            labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - _camera.transform.position);
        }

        private void OnPlayerValueChanged(NetworkPlayerData player)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_label == null || _playerData == null) return;

            string status = BuildStatus();
            _label.text = $"{_playerData.Nickname.Value}\nHP {_playerData.HP.Value}  Score {_playerData.Score.Value}\n{status}";
            _label.color = ResolveColor();
        }

        private string BuildStatus()
        {
            if (_playerData.IsFinished.Value)
                return $"FIN {FormatTime(_playerData.FinishTime.Value)}";

            if (!_playerData.IsAlive)
                return "OUT";

            if (NetworkSessionController.Instance != null && NetworkSessionController.Instance.Phase.Value == SessionPhase.Lobby)
                return _playerData.IsReady.Value ? "READY" : "WAITING";

            return $"CP {_playerData.CheckpointIndex.Value}";
        }

        private Color ResolveColor()
        {
            if (_playerData.IsFinished.Value || _playerData.IsReady.Value)
                return _readyColor;

            if (_playerData.HP.Value <= 35)
                return _damagedColor;

            return _networkBehaviour != null && _networkBehaviour.IsOwner ? _ownerColor : _remoteColor;
        }

        private void EnsureLabel()
        {
            if (_label != null) return;

            GameObject labelObject = new("Nameplate");
            labelObject.transform.SetParent(transform, false);
            _label = labelObject.AddComponent<TextMeshPro>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 2.4f;
            _label.text = "Pilot";
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.outlineWidth = 0.16f;
            _label.color = _remoteColor;
        }

        private static string FormatTime(float timeSeconds)
        {
            int minutes = (int)(timeSeconds / 60f);
            int seconds = (int)(timeSeconds % 60f);
            int milliseconds = (int)((timeSeconds * 100f) % 100f);
            return $"{minutes:00}:{seconds:00}.{milliseconds:00}";
        }
    }
}
