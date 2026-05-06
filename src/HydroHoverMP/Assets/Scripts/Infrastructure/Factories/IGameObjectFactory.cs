using System.Threading.Tasks;
using Infrastructure.Providers.Assets;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Zenject;

namespace Infrastructure.Factories
{
    public interface IGameObjectFactory
    {
        Task<GameObject> InstantiateAsync(
            string path,
            Vector3? position = null,
            Quaternion? rotation = null,
            Transform parent = null,
            DiContainer container = null
        );

        Task<GameObject> InstantiateAsync(
            AssetReference path,
            Vector3? position = null,
            Quaternion? rotation = null,
            Transform parent = null,
            DiContainer container = null
        );

        Task<T> InstantiateAndGetComponent<T>(
            string path,
            Vector3? position = null,
            Quaternion? rotation = null,
            Transform parent = null,
            DiContainer container = null
        ) where T : Component;

        Task<T> InstantiateAndGetComponent<T>(
            AssetReference path,
            Vector3? position = null,
            Quaternion? rotation = null,
            Transform parent = null,
            DiContainer container = null
        ) where T : Component;

        void Destroy(GameObject gameObject);
    }

    public class GameObjectFactory : IGameObjectFactory
    {
        private readonly DiContainer _container;
        private readonly IAssetsAddressablesProvider _assetsProvider;

        public GameObjectFactory(
            DiContainer container,
            IAssetsAddressablesProvider assetsProvider)
        {
            _container = container;
            _assetsProvider = assetsProvider;
        }

        public async Task<GameObject> InstantiateAsync(string path, Vector3? position = null,
            Quaternion? rotation = null, Transform parent = null, DiContainer container = null)
        {
            return InstantiateAsync(await _assetsProvider.GetAsset<GameObject>(path), position, rotation, parent, container);
        }

        public async Task<GameObject> InstantiateAsync(AssetReference path, Vector3? position = null,
            Quaternion? rotation = null, Transform parent = null, DiContainer container = null)
        {
            return InstantiateAsync(await _assetsProvider.GetAsset<GameObject>(path), position, rotation, parent, container);
        }

        public async Task<T> InstantiateAndGetComponent<T>(string path, Vector3? position = null,
            Quaternion? rotation = null, Transform parent = null, DiContainer container = null) where T : Component
        {
            return (await InstantiateAsync(path, position, rotation, parent, container)).GetComponent<T>();
        }

        public async Task<T> InstantiateAndGetComponent<T>(AssetReference path, Vector3? position = null,
            Quaternion? rotation = null, Transform parent = null, DiContainer container = null) where T : Component
        {
            return (await InstantiateAsync(path, position, rotation, parent, container)).GetComponent<T>();
        }

        public void Destroy(GameObject gameObject)
        {
            Object.Destroy(gameObject);
        }

        private GameObject InstantiateAsync(GameObject prefab, Vector3? position = null, Quaternion? rotation = null,
            Transform parent = null, DiContainer container = null)
        {
            var containerToUse = container ?? _container; 
            
            return containerToUse.InstantiatePrefab(prefab, position ?? Vector3.zero, rotation ?? Quaternion.identity,
                parent);
        }
    }
}