using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

namespace Infrastructure.Services.Input
{
    public class InputService : IInputService, IInitializable, IDisposable
    {
        private HoverControls _controls;
        
        public event Action OnPausePressed;

        public Vector2 MoveInput => _controls.Player.Move.ReadValue<Vector2>() * SensitivityMultiplier;
        public float LiftInput => _controls.Player.Lift.ReadValue<float>();
        public bool HandbrakeInput => _controls.Player.Handbrake.IsPressed();

        public float SensitivityMultiplier { get; set; } = 1.0f;

        public void Initialize()
        {
            _controls = new HoverControls();
            
            _controls.Player.Pause.performed += _ => OnPausePressed?.Invoke();
            
            Enable();
        }

        public void Enable()
        {
            _controls.Enable();
        }

        public void Disable()
        {
            _controls.Disable();
        }

        public void Dispose()
        {
            if (_controls != null)
            {
                _controls.Player.Pause.performed -= _ => OnPausePressed?.Invoke();
                _controls.Dispose();
            }
        }
        
        public InputActionAsset GetActionAsset() => _controls.asset;

        public void LoadBindingOverrides(string jsonOverrides)
        {
            if (!string.IsNullOrEmpty(jsonOverrides))
                _controls.asset.LoadBindingOverridesFromJson(jsonOverrides);
        }

        public string SaveBindingOverrides() => _controls.asset.SaveBindingOverridesAsJson();
    }
}