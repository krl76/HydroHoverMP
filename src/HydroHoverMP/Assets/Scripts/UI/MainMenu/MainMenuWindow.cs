using Core.States.Base;
using Core.States.Core;
using Data;
using Infrastructure.Services.Window;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace UI.MainMenu
{
    public class MainMenuWindow : MonoBehaviour
    {
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _leaderboardButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _exitButton;

        private GameStateMachine _stateMachine;
        private IWindowService _windowService;

        [Inject]
        public void Construct(GameStateMachine stateMachine, IWindowService windowService)
        {
            _stateMachine = stateMachine;
            _windowService = windowService;
        }

        private void Start()
        {
            _playButton.onClick.AddListener(() => 
                _stateMachine.Enter<LoadLevelState, string>(ScenesPaths.GAMEPLAY));

            _leaderboardButton.onClick.AddListener(Leaderboard);

            _settingsButton.onClick.AddListener(Settings);
            
#if UNITY_EDITOR
            _exitButton.onClick.AddListener(EditorApplication.ExitPlaymode);
#else
            _exitButton.onClick.AddListener(Application.Quit);
#endif
            
        }
        
        private void Leaderboard()
        {
            _windowService.Open(WindowID.Leaderboard);
            _windowService.Close(WindowID.MainMenu);
        }
        
        private void Settings()
        {
            _windowService.Open(WindowID.Settings);
            _windowService.Close(WindowID.MainMenu);
        }
    }
}