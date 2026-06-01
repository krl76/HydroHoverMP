using System;

namespace Infrastructure.Services.Network
{
    /// <summary>
    /// Detects whether this process should run as a dedicated server.
    /// </summary>
    public static class ServerEnvironment
    {
        private const string DedicatedServerArg = "-dedicatedServer";
        private const string ServerOnlyArg = "-serverOnly";

        public static bool IsDedicatedServer
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return HasServerArgument(Environment.GetCommandLineArgs());
#endif
            }
        }

        /// <summary>
        /// Pure, testable check for a server launch flag in the supplied arguments.
        /// </summary>
        public static bool HasServerArgument(string[] args)
        {
            if (args == null) return false;

            foreach (string arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (string.Equals(arg, DedicatedServerArg, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, ServerOnlyArg, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
