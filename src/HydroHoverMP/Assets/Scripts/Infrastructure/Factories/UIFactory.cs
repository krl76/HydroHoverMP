using System.Collections.Generic;
using System.Threading.Tasks;
using Infrastructure.Services.Window;
using UnityEngine;


namespace Infrastructure.Factories
{
    public interface IUIFactory
    {
        Task<GameObject> CreateScreen(string assetAddress, WindowID windowId);
        T GetScreenComponent<T>(WindowID windowId) where T : Component;
        void DestroyScreen(WindowID windowId);
        bool Exists(WindowID windowId);
    }

    public class UIFactory : IUIFactory
    {
        private readonly IGameObjectFactory _gameObjectFactory;
        private readonly Dictionary<WindowID, GameObject> _screenInstances = new();

        public UIFactory(
            IGameObjectFactory gameObjectFactory
        )
        {
            _gameObjectFactory = gameObjectFactory;
        }

        public async Task<GameObject> CreateScreen(string assetAddress, WindowID windowId)
        {
            if (_screenInstances.ContainsKey(windowId))
            {
                Debug.LogWarning($"Экран с WindowID {windowId} уже существует.. " +
                                 $"Замена существующего экранного объекта.");

                DestroyScreen(windowId);
            }

            var instance = await _gameObjectFactory.InstantiateAsync(assetAddress);

            if (_screenInstances.TryAdd(windowId, instance))
            {
                return instance;
            }

            Object.Destroy(instance);
            return null;
        }

        public T GetScreenComponent<T>(WindowID windowId) where T : Component
        {
            if (_screenInstances.TryGetValue(windowId, out var screenObject))
            {
                var screenComponent = screenObject.GetComponent<T>();
                if (screenComponent != null)
                {
                    return screenComponent;
                }

                Debug.LogError($"Компонент экрана типа {typeof(T)} не найден");
                return null;
            }

            Debug.LogError($"Экран с WindowID {windowId} не найден");
            return null;
        }

        public void DestroyScreen(WindowID windowId)
        {
            if (!_screenInstances.Remove(windowId, out var screenObject))
            {
                Debug.LogWarning($"Экран с WindowID {windowId} не найден");
                return;
            }

            _gameObjectFactory.Destroy(screenObject);
        }

        public bool Exists(WindowID windowId) => _screenInstances.ContainsKey(windowId);
    }
}