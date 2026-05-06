using Core.States.Base;
using Infrastructure.Services.Input;
using Infrastructure.Services.RaceManager;
using Infrastructure.Services.Window;

namespace Core.States.Game
{
    public class GameLoopState : IState
    {
        private readonly IRaceManagerService _raceService;
        private readonly IInputService _inputService;
        private readonly IWindowService _windowService;

        public GameLoopState(IWindowService windowService, IRaceManagerService raceService, IInputService inputService)
        {
            _windowService = windowService;
            _raceService = raceService;
            _inputService = inputService;
        }
        
        public async void Enter()
        {
            await _windowService.Open(WindowID.HUD);
            
            _inputService.Enable();
            
            _raceService.OnRaceFinished += OnRaceFinished;
            _inputService.OnPausePressed += OnPausePressed;
        }
        
        private void OnPausePressed()
        {
            if (_windowService.IsWindowOpened(WindowID.Pause) || 
                _windowService.IsWindowOpened(WindowID.Finish)) 
                return;

            _windowService.Open(WindowID.Pause);
        }
        
        private void OnRaceFinished()
        {
            _inputService.OnPausePressed -= OnPausePressed;
            
            _windowService.Close(WindowID.HUD);
            _inputService.Disable();
            _windowService.Open(WindowID.Finish);
        }
        
        public void Exit()
        {
            _raceService.OnRaceFinished -= OnRaceFinished;
            _inputService.OnPausePressed -= OnPausePressed;
            
            _windowService.Close(WindowID.HUD);
            
            if (_windowService.IsWindowOpened(WindowID.Pause))
            {
                _windowService.Close(WindowID.Pause);
            }
        }
    }
}