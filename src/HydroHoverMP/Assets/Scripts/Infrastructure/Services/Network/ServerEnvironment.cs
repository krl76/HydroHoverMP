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

        /// <summary>
        /// Reads the value of a launch argument of the form "-key value" or "-key=value".
        /// Returns null when the key is absent or has no value.
        /// </summary>
        public static string GetCommandLineValue(string key)
        {
            return TryGetCommandLineValue(Environment.GetCommandLineArgs(), key, out string value)
                ? value
                : null;
        }

        /// <summary>
        /// Pure, testable parser for a "-key value" / "-key=value" launch argument.
        /// </summary>
        public static bool TryGetCommandLineValue(string[] args, string key, out string value)
        {
            value = null;
            if (args == null || string.IsNullOrWhiteSpace(key)) return false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrWhiteSpace(arg)) continue;

                // "-key=value"
                int eq = arg.IndexOf('=');
                if (eq > 0 && string.Equals(arg[..eq], key, StringComparison.OrdinalIgnoreCase))
                {
                    string inline = arg[(eq + 1)..].Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(inline))
                    {
                        value = inline;
                        return true;
                    }
                }

                // "-key value"
                if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string next = args[i + 1];
                    if (!string.IsNullOrWhiteSpace(next) && !next.StartsWith("-"))
                    {
                        value = next.Trim().Trim('"');
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
