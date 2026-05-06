using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UI.Settings
{
    public class RebindButton : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TextMeshProUGUI _actionNameText;
        [SerializeField] private TextMeshProUGUI _bindingText;
        [SerializeField] private Button _button;
        [SerializeField] private GameObject _waitingOverlay;

        private InputAction _action;
        private int _bindingIndex;
        private InputActionRebindingExtensions.RebindingOperation _rebindingOperation;
        
        private Action _onRebindCompleted; 

        public void Setup(InputAction action, int bindingIndex, string displayName, Action onRebindCompleted)
        {
            _action = action;
            _bindingIndex = bindingIndex;
            _actionNameText.text = displayName;
            _onRebindCompleted = onRebindCompleted;

            UpdateBindingDisplay();
            
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(StartRebinding);
        }

        private void StartRebinding()
        {
            _button.interactable = false;
            if (_waitingOverlay) _waitingOverlay.SetActive(true);

            _action.Disable();

            _rebindingOperation = _action.PerformInteractiveRebinding(_bindingIndex)
                .WithControlsExcluding("Mouse")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(operation => FinishRebinding())
                .OnCancel(operation => FinishRebinding())
                .Start();
        }

        private void FinishRebinding()
        {
            _rebindingOperation?.Dispose();
            _rebindingOperation = null;

            _action.Enable();
            
            if (_waitingOverlay) _waitingOverlay.SetActive(false);
            _button.interactable = true;
            
            _onRebindCompleted?.Invoke(); 
        }
        
        public void UpdateBindingDisplay()
        {
            if (_action != null)
            {
                _bindingText.text = InputControlPath.ToHumanReadableString(
                    _action.bindings[_bindingIndex].effectivePath,
                    InputControlPath.HumanReadableStringOptions.OmitDevice);
            }
        }
        
        private void OnDestroy()
        {
            _rebindingOperation?.Dispose();
        }
    }
}