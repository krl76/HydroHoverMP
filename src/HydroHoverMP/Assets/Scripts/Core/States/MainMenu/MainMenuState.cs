using Core.States.Base;
using Data;
using Infrastructure.Services.SceneManagement;
using Infrastructure.Services.Window;

namespace Core.States.MainMenu
{
    public class MainMenuState : IState
    {
        private readonly ISceneLoaderService _sceneLoader;
        private readonly IWindowService _windowService;

        public MainMenuState(ISceneLoaderService sceneLoader, IWindowService windowService)
        {
            _sceneLoader = sceneLoader;
            _windowService = windowService;
        }

        public void Enter()
        {
            _sceneLoader.LoadScene(ScenesPaths.MAIN_MENU, OnLoaded);
        }

        private void OnLoaded()
        {
            _windowService.Open(WindowID.MainMenu);
        }

        public void Exit()
        {
            _windowService.Close(WindowID.MainMenu);
        }
    }
}