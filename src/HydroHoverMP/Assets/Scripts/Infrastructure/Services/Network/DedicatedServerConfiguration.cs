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

        public string NormalizedAddress => string.IsNullOrWhiteSpace(Address)
            ? DefaultAddress
            : Address.Trim();

        public ushort NormalizedPort => Port == 0
            ? DefaultPort
            : Port;
    }
}
