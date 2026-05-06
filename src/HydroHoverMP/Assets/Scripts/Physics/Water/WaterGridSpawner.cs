using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Physics.Water
{
    public class WaterGridSpawner : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private AssetReference _waterTileAddress;
        [SerializeField] private int _gridSizeX = 3;
        [SerializeField] private int _gridSizeZ = 10;
        [SerializeField] private float _tileSize = 100f;

        private List<GameObject> _spawnedTiles = new List<GameObject>();

        private void Start()
        {
            SpawnGrid().Forget();
        }

        private async UniTaskVoid SpawnGrid()
        {
            Vector3 startPos = transform.position;

            for (int z = 0; z < _gridSizeZ; z++)
            {
                for (int x = 0; x < _gridSizeX; x++)
                {
                    Vector3 spawnPos = startPos + new Vector3(x * _tileSize, 0, z * _tileSize);
                    
                    var op = Addressables.InstantiateAsync(_waterTileAddress, spawnPos, Quaternion.identity, transform);
                    
                    GameObject tile = await op.ToUniTask();
                    _spawnedTiles.Add(tile);
                    
                    // await UniTask.Yield(); 
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var tile in _spawnedTiles)
            {
                if (tile != null) Addressables.ReleaseInstance(tile);
            }
            _spawnedTiles.Clear();
        }
    }
}