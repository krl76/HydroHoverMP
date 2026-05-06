using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Infrastructure.Services.Input
{
    public interface IInputService
    {
        Vector2 MoveInput { get; }
        float LiftInput { get; }
        bool HandbrakeInput { get; }
        bool HydroPulsePressed { get; }

        float SensitivityMultiplier { get; set; }
        
        event Action OnPausePressed;
        
        void Enable();
        void Disable();
        
        InputActionAsset GetActionAsset();
        void LoadBindingOverrides(string jsonOverrides);
        string SaveBindingOverrides();
    }
}
