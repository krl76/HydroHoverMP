using System.Collections.Generic;
using Data.Settings;
using Infrastructure.Services.Settings;
using Infrastructure.Services.Input;
using Infrastructure.Services.Window;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Zenject;

namespace UI.Settings
{
    public class SettingsWindow : MonoBehaviour
    {
        [Header("Visuals")]
        [Tooltip("Объект (например, Image), который будет включаться только если настройки открыты из Паузы")]
        [SerializeField] private GameObject _pauseBackground;

        [Header("Audio")]
        [SerializeField] private Toggle _muteToggle;
        [SerializeField] private Slider _masterSlider;
        [SerializeField] private Slider _musicSlider;
        [SerializeField] private Slider _sfxSlider;
        
        [Header("Gameplay")]
        [SerializeField] private Slider _sensitivitySlider;

        [Header("Rebinding")]
        [SerializeField] private Transform _controlsContainer;
        [SerializeField] private RebindButton _rebindPrefab;

        [Header("Main Buttons")]
        [SerializeField] private Button _applyButton;
        [SerializeField] private Button _closeButton;

        private ISettingsService _settingsService;
        private IWindowService _windowService;
        private IInputService _inputService;

        private List<RebindButton> _spawnedButtons = new List<RebindButton>();
        
        private SettingsData _backupData; 
        private bool _isDirty = false;

        [Inject]
        public void Construct(ISettingsService settingsService, IWindowService windowService, IInputService inputService)
        {
            _settingsService = settingsService;
            _windowService = windowService;
            _inputService = inputService;
        }

        private void Start()
        {
            if (_pauseBackground != null)
            {
                _pauseBackground.SetActive(Time.timeScale == 0);
            }

            CreateBackup();
            
            InitializeUI();
            GenerateRebindUI();
            SubscribeEvents();
            
            UpdateApplyButtonState();
        }

        private void CreateBackup()
        {
            _backupData = new SettingsData
            {
                IsMuted = _settingsService.IsMuted,
                MasterVolume = _settingsService.MasterVolume,
                MusicVolume = _settingsService.MusicVolume,
                SFXVolume = _settingsService.SFXVolume,
                MouseSensitivity = _settingsService.Sensitivity,
                InputOverridesJson = _inputService.SaveBindingOverrides()
            };
        }

        private void InitializeUI()
        {
            _muteToggle.SetIsOnWithoutNotify(_backupData.IsMuted);
            _masterSlider.SetValueWithoutNotify(_backupData.MasterVolume);
            _musicSlider.SetValueWithoutNotify(_backupData.MusicVolume);
            _sfxSlider.SetValueWithoutNotify(_backupData.SFXVolume);
            _sensitivitySlider.SetValueWithoutNotify(_backupData.MouseSensitivity);
        }

        private void SubscribeEvents()
        {
            _muteToggle.onValueChanged.AddListener(val => { _settingsService.IsMuted = val; OnSettingsChanged(); });
            _masterSlider.onValueChanged.AddListener(val => { _settingsService.MasterVolume = val; OnSettingsChanged(); });
            _musicSlider.onValueChanged.AddListener(val => { _settingsService.MusicVolume = val; OnSettingsChanged(); });
            _sfxSlider.onValueChanged.AddListener(val => { _settingsService.SFXVolume = val; OnSettingsChanged(); });
            _sensitivitySlider.onValueChanged.AddListener(val => { _settingsService.Sensitivity = val; OnSettingsChanged(); });

            _applyButton.onClick.AddListener(ApplySettings);
            _closeButton.onClick.AddListener(CloseWindow);
        }

        private void OnSettingsChanged()
        {
            _isDirty = true;
            UpdateApplyButtonState();
        }

        private void UpdateApplyButtonState()
        {
            if (_applyButton) _applyButton.gameObject.SetActive(_isDirty);
        }

        private void GenerateRebindUI()
        {
            foreach (Transform child in _controlsContainer) Destroy(child.gameObject);
            _spawnedButtons.Clear();

            InputActionAsset asset = _inputService.GetActionAsset();
            InputActionMap map = asset.FindActionMap("Player");

            if (map == null) return;

            foreach (var action in map.actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    if (action.bindings[i].isComposite) continue;

                    string displayName = action.name;
                    if (action.bindings[i].isPartOfComposite)
                        displayName += $" {action.bindings[i].name.ToUpper()}";

                    RebindButton item = Instantiate(_rebindPrefab, _controlsContainer);
                    
                    item.Setup(action, i, displayName, OnRebindComplete);
                    
                    _spawnedButtons.Add(item);
                }
            }
        }

        private void OnRebindComplete()
        {
            ResolveDuplicateBindings();
            
            foreach (var btn in _spawnedButtons)
            {
                btn.UpdateBindingDisplay();
            }
            
            OnSettingsChanged();
        }

        private void ResolveDuplicateBindings()
        {
            InputActionAsset asset = _inputService.GetActionAsset();
            InputActionMap map = asset.FindActionMap("Player");
            
            foreach (var action in map.actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding binding = action.bindings[i];
                    if (binding.isComposite) continue;
                    
                    if (string.IsNullOrEmpty(binding.effectivePath)) continue;

                    foreach (var otherAction in map.actions)
                    {
                        for (int j = 0; j < otherAction.bindings.Count; j++)
                        {
                            if (otherAction == action && i == j) continue;
                            
                            InputBinding otherBinding = otherAction.bindings[j];
                            if(otherBinding.isComposite) continue;
                            
                            if (binding.effectivePath == otherBinding.effectivePath)
                            {
                                Debug.Log($"Duplicate found! {action.name} conflicts with {otherAction.name} on key {binding.effectivePath}");
                                
                                otherAction.ApplyBindingOverride(j, "");
                            }
                        }
                    }
                }
            }
        }

        private void ApplySettings()
        {
            _settingsService.Save();
            
            CreateBackup();
            
            _isDirty = false;
            UpdateApplyButtonState();
        }

        private void CloseWindow()
        {
            if (_isDirty)
            {
                _settingsService.IsMuted = _backupData.IsMuted;
                _settingsService.MasterVolume = _backupData.MasterVolume;
                _settingsService.MusicVolume = _backupData.MusicVolume;
                _settingsService.SFXVolume = _backupData.SFXVolume;
                _settingsService.Sensitivity = _backupData.MouseSensitivity;
                
                if (!string.IsNullOrEmpty(_backupData.InputOverridesJson))
                {
                    _inputService.LoadBindingOverrides(_backupData.InputOverridesJson);
                }
                else
                {
                    _inputService.GetActionAsset().RemoveAllBindingOverrides();
                }
                
                Debug.Log("Changes discarded.");
            }
            
            if(Time.timeScale == 0) 
            {
                _windowService.Open(WindowID.Pause);
            }
            else 
            {
                _windowService.Open(WindowID.MainMenu);
            }
            
            _windowService.Close(WindowID.Settings);
        }
    }
}