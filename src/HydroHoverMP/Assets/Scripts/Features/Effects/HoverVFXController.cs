using System;
using Physics.Hover;
using UnityEngine;

namespace Features.Effects
{
    public class HoverVFXController : MonoBehaviour
    {
        [Header("Particles")]
        [SerializeField] private ParticleSystem _rearSpray;
        [SerializeField] private ParticleSystem _skirtMist;

        [Header("Settings")]
        [SerializeField] private float _maxSprayEmission = 200f;
        [SerializeField] private float _maxSpraySpeed = 20f;
        
        private HoverController _hoverController;

        private void Start()
        {
            _hoverController = GetComponent<HoverController>();
        }

        private void Update()
        {
            if (_hoverController == null) return;

            UpdateRearSpray();
            UpdateSkirtMist();
        }

        private void UpdateRearSpray()
        {
            if (_rearSpray == null) return;
            
            float thrustRatio = _hoverController.ThrustEngine.CurrentRPM / _hoverController.ThrustEngine.MaxRPM;

            var emission = _rearSpray.emission;
            var main = _rearSpray.main;
            
            emission.rateOverTime = thrustRatio * _maxSprayEmission;
            
            main.startSpeed = 5f + (thrustRatio * _maxSpraySpeed);
        }

        private void UpdateSkirtMist()
        {
            if (_skirtMist == null) return;

            var emission = _skirtMist.emission;
            
            float liftRatio = _hoverController.LiftEngine.CurrentRPM / _hoverController.LiftEngine.MaxRPM;
            
            emission.rateOverTime = liftRatio * 50f; 
        }
    }
}