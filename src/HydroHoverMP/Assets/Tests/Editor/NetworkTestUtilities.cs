using System;
using System.Reflection;

namespace HydroHoverMP.Tests.Editor
{
    internal static class NetworkTestUtilities
    {
        public static string InvokePrivateStaticStringMethod(Type type, string methodName, params object[] args)
        {
            MethodInfo method = GetMethod(type, methodName);
            object[] parameters = args;
            return (string)method.Invoke(null, parameters);
        }

        public static bool InvokeTryParseCommandLinePort(string value, out ushort port)
        {
            MethodInfo method = GetMethod(typeof(Infrastructure.Services.Network.NetworkConnectionService), "TryParseCommandLinePort");
            object[] parameters = { value, (ushort)0 };
            bool success = (bool)method.Invoke(null, parameters);
            port = (ushort)parameters[1];
            return success;
        }

        public static bool InvokeTryGetCommandLineServerPort(string[] args, out ushort port, out string error)
        {
            MethodInfo method = GetMethod(typeof(Infrastructure.Services.Network.NetworkConnectionService), "TryGetCommandLineServerPort");
            object[] parameters = { args, (ushort)0, null };
            bool success = (bool)method.Invoke(null, parameters);
            port = (ushort)parameters[1];
            error = (string)parameters[2];
            return success;
        }

        private static MethodInfo GetMethod(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                throw new MissingMethodException(type.FullName, methodName);

            return method;
        }
    }
}
