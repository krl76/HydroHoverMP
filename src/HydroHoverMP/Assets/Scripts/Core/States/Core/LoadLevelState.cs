using Core.States.Base;
using Core.States.Game;
using Data;
using FishNet;
using Infrastructure.Factories;
using Infrastructure.Services.Player;
using Infrastructure.Services.SceneManagement;
using UnityEngine;
using Zenject;

namespace Core.States.Core
{
    public class LoadLevelState : IPayloaded<string>
    {
        private readonly GameStateMachine _stateMachine;
        private readonly ISceneLoaderService _sceneLoader;
        private readonly IGameObjectFactory _gameObjectFactory;
        private readonly IPlayerService _playerService;

        public LoadLevelState(GameStateMachine stateMachine, 
            ISceneLoaderService sceneLoader, 
            IGameObjectFactory gameObjectFactory,
            IPlayerService playerService)
        {
            _stateMachine = stateMachine;
            _sceneLoader = sceneLoader;
            _gameObjectFactory = gameObjectFactory;
            _playerService = playerService;
        }

        public void Enter(string sceneName)
        {
            _sceneLoader.LoadScene(sceneName, () => OnLoaded());
        }

        private async void OnLoaded()
        {
            if (InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted)
            {
                _sceneLoader.LoadSceneAdditive(ScenesPaths.LEVEL);
                _stateMachine.Enter<GameLoopState>();
                return;
            }

            var startPoint = GameObject.FindWithTag("Spawn"); 
            Vector3 pos = startPoint ? startPoint.transform.position : Vector3.zero;
            Quaternion rot = startPoint ? startPoint.transform.rotation : Quaternion.identity;
            
            var sceneContext = Object.FindFirstObjectByType<SceneContext>();
            
            var player = await _gameObjectFactory.InstantiateAsync("Player", pos, rot, null, 
                sceneContext.Container);
            _playerService.RegisterPlayer(player);
            
            _sceneLoader.LoadSceneAdditive(ScenesPaths.LEVEL);
            
            _stateMachine.Enter<GameLoopState>();
        }

        public void Exit()
        {
        }
    }
}
