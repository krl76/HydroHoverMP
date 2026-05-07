using UnityEngine;

namespace Features.Networking
{
    [DisallowMultipleComponent]
    public sealed class NetworkSpawnPointRegistry : MonoBehaviour
    {
        [SerializeField] private Transform[] _spawnPoints;

        public static NetworkSpawnPointRegistry Instance { get; private set; }

        public int Count => _spawnPoints != null ? _spawnPoints.Length : 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"Duplicate {nameof(NetworkSpawnPointRegistry)} found on {name}. Using the newest instance.");

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public bool TryGetSpawn(int index, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (_spawnPoints == null || _spawnPoints.Length == 0)
                return false;

            int normalizedIndex = ((index % _spawnPoints.Length) + _spawnPoints.Length) % _spawnPoints.Length;
            Transform spawn = _spawnPoints[normalizedIndex];
            if (spawn == null)
                return false;

            position = spawn.position;
            rotation = spawn.rotation;
            return true;
        }
    }
}
