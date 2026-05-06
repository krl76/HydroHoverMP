using System.Threading.Tasks;
using Data.Paths;
using Infrastructure.Factories;
using UnityEngine;

namespace Infrastructure.Services.Window
{
    public class WindowService : IWindowService
    {
        private readonly IUIFactory _uiFactory;

        public WindowService(IUIFactory uiFactory)
        {
            _uiFactory = uiFactory;
        }

        public bool IsWindowOpened(WindowID windowID) => _uiFactory.Exists(windowID);

        public async Task Open(WindowID windowID)
        {
            var path = GetWindowsPath(windowID);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[WindowService] Path not found for ID: {windowID}");
                return;
            }
            await _uiFactory.CreateScreen(path, windowID);
        }

        public async Task<T> OpenAndGet<T>(WindowID windowID) where T : Component
        {
            await Open(windowID);
            return _uiFactory.GetScreenComponent<T>(windowID);
        }

        public T Get<T>(WindowID windowID) where T : Component => 
            _uiFactory.GetScreenComponent<T>(windowID);

        public void Close(WindowID windowID) => _uiFactory.DestroyScreen(windowID);

        private string GetWindowsPath(WindowID windowID) => windowID switch
        {
            WindowID.Loading => UIPaths.LOADING_SCREEN,
            WindowID.MainMenu => UIPaths.MAIN_MENU,
            WindowID.HUD => UIPaths.HUD,
            WindowID.Pause => UIPaths.PAUSE,
            WindowID.Settings => UIPaths.SETTINGS,
            WindowID.Finish => UIPaths.FINISH_SCREEN,
            WindowID.Leaderboard => UIPaths.LEADERBOARD_SCREEN,
            _ => null
        };
    }
}