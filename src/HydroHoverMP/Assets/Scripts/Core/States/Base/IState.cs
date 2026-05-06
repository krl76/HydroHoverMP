namespace Core.States.Base
{
    public interface IState : IExitable
    {
        void Enter();
    }
}