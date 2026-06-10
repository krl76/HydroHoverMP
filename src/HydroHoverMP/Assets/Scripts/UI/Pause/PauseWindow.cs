using Core.States.Base;
using Core.States.Core;
using Core.States.MainMenu;
using Data;
using Features.Networking;
using FishNet;
using Infrastructure.Services.Network;
using Infrastructure.Services.Window;
using UI.Settings;
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
        private INetworkConnectionService _connectionService;

        [Inject]
        public void Construct(IWindowService windowService, GameStateMachine stateMachine, INetworkConnectionService connectionService)
        {
            _windowService = windowService;
            _stateMachine = stateMachine;
            _connectionService = connectionService;
        }

        private void OnEnable()
        {
            if (!InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted)
                Time.timeScale = 0f;
        }

        private void Start()
        {
            _resumeButton.onClick.AddListener(Resume);
            if (_restartButton != null)
                _restartButton.gameObject.SetActive(false); // Restart removed (multiplayer-only); use R to respawn at last checkpoint.
            _settingsButton.onClick.AddListener(OpenSettings);
            _menuButton.onClick.AddListener(GoToMenu);
        }

        private void Resume()
        {
            Time.timeScale = 1f;
            _windowService.Close(WindowID.Pause);
        }

        private async void OpenSettings()
        {
            _windowService.Close(WindowID.Pause);

            // Tell Settings to come back to Pause (not MainMenu) when it closes.
            SettingsWindow settings = await _windowService.OpenAndGet<SettingsWindow>(WindowID.Settings);
            if (settings != null)
                settings.SetReturnTarget(WindowID.Pause);
        }

        private void GoToMenu()
        {
            Time.timeScale = 1f;

            // Stop the active host/client so the menu's Host/Client buttons re-enable
            // (they only enable when connection status is Offline/Failed). Mirrors FinishScreen.
            if (NetworkSessionController.Instance != null)
                _connectionService?.StopConnection();

            _windowService.Close(WindowID.Pause);
            _stateMachine.Enter<MainMenuState>();
        }
    }
}
