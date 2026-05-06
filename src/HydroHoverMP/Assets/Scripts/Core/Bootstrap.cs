using Core.States.Base;
using Core.States.Core;
using Features.Networking;
using UnityEngine;
using Zenject;

namespace Core
{
    public class Bootstrap : MonoBehaviour
    {
        private GameStateMachine _stateMachine;

        [Inject]
        public void Construct(GameStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
        }

        private void Start()
        {
            NetworkBootstrapper.EnsureRuntimeObjects();
            _stateMachine.Enter<BootstrapState>();
        }
    }
}
