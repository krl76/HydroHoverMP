namespace Core.States.Base
{
    public interface IPayloaded<TPayload> : IExitable
    {
        void Enter(TPayload payload);
    }
}