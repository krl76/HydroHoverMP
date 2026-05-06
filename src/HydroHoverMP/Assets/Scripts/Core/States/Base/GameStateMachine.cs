using System;
using System.Collections.Generic;
using Infrastructure.Factories;

namespace Core.States.Base
{
    public class GameStateMachine
    {
        private readonly IStateFactory _stateFactory;
        private readonly Dictionary<Type, IExitable> _states;
        private IExitable _activeState;

        public GameStateMachine(IStateFactory stateFactory)
        {
            _stateFactory = stateFactory;
            _states = new Dictionary<Type, IExitable>();
        }

        public void Enter<TState>() where TState : class, IState
        {
            IState state = ChangeState<TState>();
            state.Enter();
        }

        public void Enter<TState, TPayload>(TPayload payload) where TState : class, IPayloaded<TPayload>
        {
            TState state = ChangeState<TState>();
            state.Enter(payload);
        }

        private TState ChangeState<TState>() where TState : class, IExitable
        {
            _activeState?.Exit();
            
            var type = typeof(TState);
            if (!_states.TryGetValue(type, out var state))
            {
                state = _stateFactory.GetState<TState>();
                _states[type] = state;
            }

            TState typedState = state as TState;
            _activeState = typedState;
            
            return typedState;
        }
    }
}