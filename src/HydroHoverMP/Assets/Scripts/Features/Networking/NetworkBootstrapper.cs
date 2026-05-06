using FishNet.Managing;
using UnityEngine;

namespace Features.Networking
{
    public static class NetworkBootstrapper
    {
        public static NetworkManager EnsureRuntimeObjects()
        {
            NetworkManager networkManager = Object.FindFirstObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                GameObject managerObject = new("FishNet NetworkManager");
                networkManager = managerObject.AddComponent<NetworkManager>();
                Object.DontDestroyOnLoad(managerObject);
            }

            if (Object.FindFirstObjectByType<NetworkRuntimeOverlay>() == null)
            {
                GameObject overlayObject = new("Network Runtime Overlay");
                overlayObject.AddComponent<NetworkRuntimeOverlay>();
                Object.DontDestroyOnLoad(overlayObject);
            }

            return networkManager;
        }
    }
}
