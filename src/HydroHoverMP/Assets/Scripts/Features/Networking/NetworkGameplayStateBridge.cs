using System.Collections;
using Core.States.Base;
using Core.States.Game;
using FishNet;
using Infrastructure.Services.Input;
using Infrastructure.Services.Window;
using UnityEngine;
using Zenject;

namespace Features.Networking
{
    public sealed class NetworkGameplayStateBridge : MonoBehaviour
    {
        private GameStateMachine _stateMachine;
        private IWindowService _windowService;
        private IInputService _inputService;
        private bool _entered;
        private bool _resultsScreenOpen;
        private bool _phaseInitialized;
        private SessionPhase _lastPhase = SessionPhase.Disconnected;

        [Inject]
        public void Construct(GameStateMachine stateMachine, IWindowService windowService, IInputService inputService)
        {
            _stateMachine = stateMachine;
            _windowService = windowService;
            _inputService = inputService;
        }

        private IEnumerator Start()
        {
            yield return null;

            if (_entered) yield break;
            if (!InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted) yield break;

            _entered = true;
            _stateMachine.Enter<GameLoopState>();
        }

        private void Update()
        {
            if (!_entered) return;

            // Only a process that owns a local client renders UI. A headless dedicated server
            // has no windows to drive, so it never touches the results screen.
            if (!InstanceFinder.IsClientStarted) return;

            NetworkSessionController session = NetworkSessionController.Instance;
            if (session == null) return;

            SessionPhase phase = session.Phase.Value;

            if (!_phaseInitialized)
            {
                _phaseInitialized = true;
                _lastPhase = phase;
                // Handle the case where this client arrives already in the Results phase.
                if (phase == SessionPhase.Results)
                    OpenResultsScreen();
                return;
            }

            if (phase == _lastPhase) return;
            _lastPhase = phase;

            // Server-authoritative Phase drives the post-race choice screen: it opens for every
            // client when everyone has finished (Results) and closes again on a voted restart
            // (Lobby/Countdown/Race), restoring the HUD.
            if (phase == SessionPhase.Results)
                OpenResultsScreen();
            else
                CloseResultsScreen();
        }

        private void OpenResultsScreen()
        {
            if (_resultsScreenOpen) return;
            _resultsScreenOpen = true;

            if (_windowService.IsWindowOpened(WindowID.Pause))
                _windowService.Close(WindowID.Pause);

            _windowService.Close(WindowID.HUD);
            _inputService?.Disable();
            _windowService.Open(WindowID.Finish);
        }

        private void CloseResultsScreen()
        {
            if (!_resultsScreenOpen) return;
            _resultsScreenOpen = false;

            _windowService.Close(WindowID.Finish);
            _inputService?.Enable();
            _windowService.Open(WindowID.HUD);
        }
    }
}
