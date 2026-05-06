using System.Collections;
using Core.States.Base;
using Core.States.Game;
using FishNet;
using UnityEngine;
using Zenject;

namespace Features.Networking
{
    public sealed class NetworkGameplayStateBridge : MonoBehaviour
    {
        private GameStateMachine _stateMachine;
        private bool _entered;

        [Inject]
        public void Construct(GameStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
        }

        private IEnumerator Start()
        {
            yield return null;

            if (_entered) yield break;
            if (!InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted) yield break;

            _entered = true;
            _stateMachine.Enter<GameLoopState>();
        }
    }
}
