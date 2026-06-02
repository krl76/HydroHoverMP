using System;

namespace Infrastructure.Services.Network
{
    [Serializable]
    public sealed class DedicatedServerConfiguration
    {
        public const string DefaultAddress = "localhost";
        public const ushort DefaultPort = 7770;

        public string Address = DefaultAddress;
        public ushort Port = DefaultPort;

        // Когда true — игра рассчитана на выделенный сервер: на клиенте доступен только Client (join),
        // а кнопка Host в главном меню отключается.
        public bool DedicatedServerOnly;

        public string NormalizedAddress => string.IsNullOrWhiteSpace(Address)
            ? DefaultAddress
            : Address.Trim();

        public ushort NormalizedPort => Port == 0
            ? DefaultPort
            : Port;
    }
}
