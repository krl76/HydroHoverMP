using Core.States.Base;
using Core.States.Core;
using Core.States.MainMenu;
using Data;
using FishNet;
using Infrastructure.Services.Window;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.Pause
{
    public class PauseWindow : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _menuButton;

        private IWindowService _windowService;
        private GameStateMachine _stateMachine;

        [Inject]
        public void Construct(IWindowService windowService, GameStateMachine stateMachine)
        {
            _windowService = windowService;
            _stateMachine = stateMachine;
        }

        private void OnEnable()
        {
            if (!InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted)
                Time.timeScale = 0f;
        }

        private void Start()
        {
            _resumeButton.onClick.AddListener(Resume);
            _restartButton.onClick.AddListener(RestartRace);
            _settingsButton.onClick.AddListener(OpenSettings);
            _menuButton.onClick.AddListener(GoToMenu);
        }

        private void Resume()
        {
            Time.timeScale = 1f;
            _windowService.Close(WindowID.Pause);
        }

        private void RestartRace()
        {
            Time.timeScale = 1f;
            
            _windowService.Close(WindowID.Pause);
            
            _stateMachine.Enter<LoadLevelState, string>(ScenesPaths.GAMEPLAY);
        }

        private void OpenSettings()
        {
            _windowService.Close(WindowID.Pause);
            _windowService.Open(WindowID.Settings);
        }

        private void GoToMenu()
        {
            Time.timeScale = 1f;
            _windowService.Close(WindowID.Pause);
            _stateMachine.Enter<MainMenuState>();
        }
    }
}
