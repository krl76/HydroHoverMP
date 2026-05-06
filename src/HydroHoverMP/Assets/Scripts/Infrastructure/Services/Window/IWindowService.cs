using System.Threading.Tasks;
using UnityEngine;

namespace Infrastructure.Services.Window
{
    public interface IWindowService
    {
        Task Open(WindowID windowID);
        Task<T> OpenAndGet<T>(WindowID windowID) where T : Component;
        T Get<T>(WindowID windowID) where T : Component;
        void Close(WindowID windowID);
        bool IsWindowOpened(WindowID windowID);
    }
}