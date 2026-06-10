using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using Infrastructure.Services.Input;
using Physics.Enviroment;
using Physics.Hover;
using Physics.Water;
using UnityEngine;
using Zenject;

namespace Features.Networking
{
    /// <summary>
    /// Server-authoritative client-side prediction for the hovercraft (FishNet 4.7.2 Prediction V2).
    ///
    /// Replaces the client-authoritative NetworkTransform + remote "visual float" path: the owner
    /// sends INPUT, owner and server run the SAME physics each tick, and spectators interpolate the
    /// reconciled authoritative state. Because remote boats now display the exact authoritative pose
    /// (not a per-client wave reconstruction), they can no longer diverge or "fly".
    ///
    /// Correctness rules honoured here:
    ///   * every gameplay force is queued through PredictionRigidbody so it replays on reconcile;
    ///   * buoyancy/water drag sample the wave field by the SIMULATED tick (deterministic);
    ///   * engine RPM (which carries between ticks) is part of the reconcile state;
    ///   * physics uses TimeManager.TickDelta, never Time.deltaTime.
    ///
    /// The hover/thrust/aero tuning is reused from the existing components (single source of truth);
    /// this motor only relocates WHERE/HOW the forces are applied. When networked it disables those
    /// components' own FixedUpdate so they cannot double-apply; offline (single-player) it never
    /// starts, so the original FixedUpdate physics keeps working unchanged.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(HoverController))]
    [RequireComponent(typeof(HoverCushion))]
    public sealed class PredictedHoverMotor : TickNetworkBehaviour
    {
        public struct MoveData : IReplicateData
        {
            public float Steer;     // MoveInput.x
            public float Throttle;  // MoveInput.y
            public float Lift;      // LiftInput
            public bool Handbrake;
            private uint _tick;

            public MoveData(float steer, float throttle, float lift, bool handbrake)
            {
                Steer = steer;
                Throttle = throttle;
                Lift = lift;
                Handbrake = handbrake;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct HoverReconcileData : IReconcileData
        {
            public PredictionRigidbody Body;
            public float LiftRpm;
            public float ThrustRpm;
            private uint _tick;

            public HoverReconcileData(PredictionRigidbody body, float liftRpm, float thrustRpm)
            {
                Body = body;
                LiftRpm = liftRpm;
                ThrustRpm = thrustRpm;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        private readonly PredictionRigidbody _prediction = new();

        private Rigidbody _rb;
        private HoverController _controller;
        private HoverCushion _cushion;
        private HoverAerodynamics _aero;
        private HoverEngine _liftEngine;
        private HoverEngine _thrustEngine;
        private WaterPhysicsSystem _water;
        private WindSystem _wind;
        private IInputService _input;

        [Inject]
        public void Construct(IInputService input, WaterPhysicsSystem water, WindSystem wind)
        {
            _input = input;
            _water = water;
            _wind = wind;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _controller = GetComponent<HoverController>();
            _cushion = GetComponent<HoverCushion>();
            _aero = GetComponent<HoverAerodynamics>();
            _liftEngine = _controller != null ? _controller.LiftEngine : null;
            _thrustEngine = _controller != null ? _controller.ThrustEngine : null;

            if (_cushion != null && _cushion.CenterOfMass != null)
                _rb.centerOfMass = _cushion.CenterOfMass.localPosition;

            _prediction.Initialize(_rb);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            ResolveSceneServicesIfNeeded();

            // Take over physics from the FixedUpdate components so forces are not applied twice.
            // The motor reuses their tuning + engine instances but owns all force application.
            if (_cushion != null) _cushion.enabled = false;
            if (_aero != null) _aero.enabled = false;
            if (_controller != null) _controller.SetInputEnabled(false);
            if (_rb != null) _rb.isKinematic = false;

            // Rigidbodies need both Tick (gather input + simulate) and PostTick (reconcile).
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
        }

        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        public override void CreateReconcile()
        {
            float liftRpm = _liftEngine != null ? _liftEngine.CurrentRPM : 0f;
            float thrustRpm = _thrustEngine != null ? _thrustEngine.CurrentRPM : 0f;
            HoverReconcileData data = new(_prediction, liftRpm, thrustRpm);
            PerformReconcile(data);
        }

        private MoveData BuildMoveData()
        {
            // Only the owner produces real input. On the server (no owner / remote owner) and on
            // spectators the replicate runs with the last-received/zero input and reconcile corrects it.
            if (!IsOwner || _input == null)
                return default;

            Vector2 move = _input.MoveInput;
            return new MoveData(move.x, move.y, _input.LiftInput, _input.HandbrakeInput);
        }

        [Replicate]
        private void PerformReplicate(MoveData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            // The water/wind systems live in the additively-loaded level; if they were not present
            // when OnStartNetwork ran, keep trying so buoyancy is never silently skipped (= the boat
            // sinking because only gravity is applied).
            if (_water == null || _wind == null)
                ResolveSceneServicesIfNeeded();

            float dt = (float)TimeManager.TickDelta;
            // Deterministic wave clock for THIS tick — identical on owner and server, and identical
            // on every reconcile replay of the same tick.
            float waveTime = (float)TimeManager.TicksToTime(data.GetTick());

            ApplyEngines(data, dt);
            ApplyBuoyancy(waveTime);
            ApplyThrustOrBrake(data);
            ApplyAerodynamics(data, waveTime);

            _prediction.Simulate();
        }

        [Reconcile]
        private void PerformReconcile(HoverReconcileData data, Channel channel = Channel.Unreliable)
        {
            // Restore engine RPM BEFORE the body replay so replayed ticks recompute identical torque.
            if (_liftEngine != null) _liftEngine.SetRpmState(data.LiftRpm);
            if (_thrustEngine != null) _thrustEngine.SetRpmState(data.ThrustRpm);
            _prediction.Reconcile(data.Body);
        }

        private void ApplyEngines(MoveData data, float dt)
        {
            float liftThrottle = Mathf.Clamp01(0.5f + data.Lift * 0.5f);
            if (_liftEngine != null)
            {
                _liftEngine.SetThrottle(liftThrottle);
                _liftEngine.CalculatePhysics(dt);
                if (_cushion != null)
                    _cushion.LiftEfficiency = _liftEngine.CurrentRPM / Mathf.Max(1f, _liftEngine.MaxRPM);
            }

            if (_thrustEngine != null)
            {
                _thrustEngine.SetThrottle(data.Throttle);
                _thrustEngine.CalculatePhysics(dt);
            }
        }

        private void ApplyBuoyancy(float waveTime)
        {
            if (_cushion == null || _water == null) return;

            Transform[] points = _cushion.HoverPoints;
            if (points == null) return;

            float hoverHeight = _cushion.HoverHeight;
            float springForce = _cushion.SpringForce;
            float damperForce = _cushion.DamperForce;
            float liftEfficiency = _cushion.LiftEfficiency;

            foreach (Transform point in points)
            {
                if (point == null) continue;

                float waterHeight = _water.GetWaterHeightAt(point.position, waveTime);
                float heightDiff = point.position.y - waterHeight;
                if (heightDiff >= hoverHeight) continue;

                // Clamp [0..1] — a fully submerged point must not catapult the boat (see HoverCushion).
                float compression = Mathf.Clamp01((hoverHeight - heightDiff) / hoverHeight);
                float spring = springForce * compression * liftEfficiency;
                float verticalVelocity = _rb.GetPointVelocity(point.position).y;
                float damping = -verticalVelocity * damperForce;
                float total = Mathf.Max(0f, spring + damping);
                _prediction.AddForceAtPosition(Vector3.up * total, point.position);
            }
        }

        private void ApplyThrustOrBrake(MoveData data)
        {
            if (_controller == null) return;

            if (data.Handbrake)
            {
                Vector3 velocity = _rb.linearVelocity;
                if (velocity.sqrMagnitude > 0.1f)
                    _prediction.AddForce(-velocity.normalized * _controller.BrakeForce, ForceMode.Acceleration);
                return;
            }

            if (_controller.ThrustPoint != null && _thrustEngine != null)
            {
                Vector3 force = transform.forward * (_thrustEngine.CurrentTorque * _controller.ForwardForceMultiplier);
                _prediction.AddForceAtPosition(force, _controller.ThrustPoint.position);
            }
        }

        private void ApplyAerodynamics(MoveData data, float waveTime)
        {
            if (_aero == null) return;

            // Air drag against the wind-relative velocity.
            if (_wind != null)
            {
                Vector3 relativeVelocity = _rb.linearVelocity - _wind.WindVector;
                float speed = relativeVelocity.magnitude;
                if (speed >= 0.1f)
                {
                    float forceMag = 0.5f * _aero.AirDensity * _aero.DragCoefficient * _aero.FrontalArea * speed * speed;
                    _prediction.AddForce(-relativeVelocity.normalized * forceMag, ForceMode.Force);
                }
            }

            // Water drag at each submerged hover point.
            if (_water != null && _cushion != null && _cushion.HoverPoints != null)
            {
                float pointArea = _aero.FrontalArea * 0.25f;
                foreach (Transform point in _cushion.HoverPoints)
                {
                    if (point == null) continue;
                    float waterH = _water.GetWaterHeightAt(point.position, waveTime);
                    if (point.position.y >= waterH) continue;

                    Vector3 pointVelocity = _rb.GetPointVelocity(point.position);
                    float speed = pointVelocity.magnitude;
                    if (speed < 0.1f) continue;

                    float forceMag = 0.5f * _aero.WaterDensity * _aero.WaterDragCoeff * pointArea * speed * speed;
                    forceMag = Mathf.Clamp(forceMag, 0f, 100000f);
                    _prediction.AddForceAtPosition(-pointVelocity.normalized * forceMag, point.position);
                }
            }

            // Steering torque (rudder) scaled by speed.
            float speedFactor = Mathf.Clamp01(_rb.linearVelocity.magnitude / 5f);
            float torque = data.Steer * _aero.RudderTorque * (0.2f + 0.8f * speedFactor);
            _prediction.AddRelativeTorque(Vector3.up * torque, ForceMode.Force);

            // Side drag to resist lateral sliding.
            Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);
            float sideDrag = -localVel.x * _aero.SideDrag * _rb.mass;
            _prediction.AddRelativeForce(Vector3.right * sideDrag, ForceMode.Force);
        }

        private void ResolveSceneServicesIfNeeded()
        {
            _water = _water != null ? _water : FindFirstObjectByType<WaterPhysicsSystem>();
            _wind = _wind != null ? _wind : FindFirstObjectByType<WindSystem>();

            if (_input != null) return;
            if (ProjectContext.HasInstance)
                _input = ProjectContext.Instance.Container.TryResolve<IInputService>();
        }
    }
}
