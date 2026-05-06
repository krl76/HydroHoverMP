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

            _label.text = $"{_playerData.Nickname.Value}\nHP {_playerData.HP.Value} | {_playerData.Score.Value}";
            _label.color = _networkBehaviour != null && _networkBehaviour.IsOwner ? _ownerColor : _remoteColor;
        }

        private void EnsureLabel()
        {
            if (_label != null) return;

            GameObject labelObject = new("Nameplate");
            labelObject.transform.SetParent(transform, false);
            _label = labelObject.AddComponent<TextMeshPro>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 3f;
            _label.text = "Pilot";
        }
    }
}
