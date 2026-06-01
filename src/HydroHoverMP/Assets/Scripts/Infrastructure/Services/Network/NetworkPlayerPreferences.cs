using UnityEngine;

namespace Infrastructure.Services.Network
{
    public static class NetworkPlayerPreferences
    {
        private const string NicknameKey = "HydroHoverMP.Network.Nickname";
        private const string AddressKey = "HydroHoverMP.Network.Address";
        private const string PortKey = "HydroHoverMP.Network.Port";
        private const int MaxNicknameLength = 18;

        public static string GetNickname()
        {
            return SanitizeNickname(PlayerPrefs.GetString(NicknameKey, "Pilot"));
        }

        public static void SetNickname(string nickname)
        {
            PlayerPrefs.SetString(NicknameKey, SanitizeNickname(nickname));
            PlayerPrefs.Save();
        }

        public static string GetAddress()
        {
            return GetAddress(DedicatedServerConfiguration.DefaultAddress);
        }

        public static string GetAddress(string defaultAddress)
        {
            string fallbackAddress = string.IsNullOrWhiteSpace(defaultAddress)
                ? DedicatedServerConfiguration.DefaultAddress
                : defaultAddress.Trim();
            string address = PlayerPrefs.GetString(AddressKey, fallbackAddress);
            return string.IsNullOrWhiteSpace(address) ? fallbackAddress : address.Trim();
        }

        public static void SetAddress(string address)
        {
            PlayerPrefs.SetString(AddressKey, string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim());
            PlayerPrefs.Save();
        }

        public static ushort GetPort()
        {
            return GetPort(DedicatedServerConfiguration.DefaultPort);
        }

        public static ushort GetPort(ushort defaultPort)
        {
            ushort fallbackPort = defaultPort == 0 ? DedicatedServerConfiguration.DefaultPort : defaultPort;
            int port = PlayerPrefs.GetInt(PortKey, fallbackPort);
            return port is > 0 and <= ushort.MaxValue ? (ushort)port : fallbackPort;
        }

        public static void SetPort(ushort port)
        {
            if (port == 0) return;

            PlayerPrefs.SetInt(PortKey, port);
            PlayerPrefs.Save();
        }

        private static string SanitizeNickname(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return "Pilot";

            string trimmed = nickname.Trim();
            return trimmed.Length <= MaxNicknameLength ? trimmed : trimmed[..MaxNicknameLength];
        }
    }
}
