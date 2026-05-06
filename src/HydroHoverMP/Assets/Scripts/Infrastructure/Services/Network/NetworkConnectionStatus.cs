namespace Infrastructure.Services.Network
{
    public enum NetworkConnectionStatus : byte
    {
        Offline = 0,
        StartingHost = 1,
        StartingClient = 2,
        StartingServer = 3,
        HostStarted = 4,
        ClientStarted = 5,
        ServerStarted = 6,
        Stopping = 7,
        Failed = 8
    }
}
