using Core.States.Base;
using Zenject;

namespace Infrastructure.Factories
{
    public interface IStateFactory
    {
        T GetState<T>() where T : class, IExitable;
    }

    public class StateFactory : IStateFactory
    {
        private readonly IInstantiator _instantiator;
        
        public StateFactory(IInstantiator instantiator)
        {
            _instantiator = instantiator;
        }

        public T GetState<T>() where T : class, IExitable
        {
            return _instantiator.Instantiate<T>();
        }
    }
}