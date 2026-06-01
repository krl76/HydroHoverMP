using Core.States.Base;
using Core.States.MainMenu;
using Infrastructure.Services.Network;

namespace Core.States.Core
{
    public class BootstrapState : IState
    {
        private readonly GameStateMachine _stateMachine;

        public BootstrapState(GameStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
        }

        public void Enter()
        {
            if (ServerEnvironment.IsDedicatedServer)
                _stateMachine.Enter<ServerBootstrapState>();
            else
                _stateMachine.Enter<MainMenuState>();
        }

        public void Exit()
        {
        }
    }
}
