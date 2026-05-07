using Features.Networking;
using Infrastructure.Services.Network;
using NUnit.Framework;
using UnityEngine;

namespace HydroHoverMP.Tests.Editor
{
    public sealed class NetworkLogicTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        [Test]
        public void NetworkPlayerData_SanitizeNickname_UsesDefaultTrimmingAndLengthCap()
        {
            string defaultName = NetworkTestUtilities.InvokePrivateStaticStringMethod(typeof(NetworkPlayerPreferences), "SanitizeNickname", new object[] { null });
            string trimmed = NetworkTestUtilities.InvokePrivateStaticStringMethod(typeof(NetworkPlayerPreferences), "SanitizeNickname", "  Pilot One  ");
            string truncated = NetworkTestUtilities.InvokePrivateStaticStringMethod(typeof(NetworkPlayerPreferences), "SanitizeNickname", "12345678901234567890");

            Assert.That(defaultName, Is.EqualTo("Pilot"));
            Assert.That(trimmed, Is.EqualTo("Pilot One"));
            Assert.That(truncated, Is.EqualTo("123456789012345678"));
        }

        [Test]
        public void NetworkPlayerPreferences_TrimmedAddressAndValidPortRoundTrip()
        {
            NetworkPlayerPreferences.SetAddress("  192.168.0.20  ");
            NetworkPlayerPreferences.SetPort(7778);

            Assert.That(NetworkPlayerPreferences.GetAddress(), Is.EqualTo("192.168.0.20"));
            Assert.That(NetworkPlayerPreferences.GetPort(), Is.EqualTo((ushort)7778));
        }

        [Test]
        public void NetworkPlayerPreferences_IgnoresInvalidZeroPortAndDefaultsToLocalhost()
        {
            NetworkPlayerPreferences.SetAddress("   ");
            NetworkPlayerPreferences.SetPort(0);

            Assert.That(NetworkPlayerPreferences.GetAddress(), Is.EqualTo("localhost"));
            Assert.That(NetworkPlayerPreferences.GetPort(), Is.EqualTo((ushort)7770));
        }

        [Test]
        public void NetworkConnectionService_CommandLinePortParsingSupportsDedicatedServerForms()
        {
            bool validInline = NetworkTestUtilities.InvokeTryGetCommandLineServerPort(new[] { "-dedicatedServer", "-port=7777" }, out ushort inlinePort, out string inlineError);
            bool validSplit = NetworkTestUtilities.InvokeTryGetCommandLineServerPort(new[] { "-serverOnly", "-serverPort", "7778" }, out ushort splitPort, out string splitError);
            bool invalid = NetworkTestUtilities.InvokeTryGetCommandLineServerPort(new[] { "-dedicatedServer", "-serverPort", "abc" }, out ushort invalidPort, out string invalidError);

            Assert.That(validInline, Is.True);
            Assert.That(inlinePort, Is.EqualTo((ushort)7777));
            Assert.That(inlineError, Is.Null);

            Assert.That(validSplit, Is.True);
            Assert.That(splitPort, Is.EqualTo((ushort)7778));
            Assert.That(splitError, Is.Null);

            Assert.That(invalid, Is.False);
            Assert.That(invalidPort, Is.EqualTo((ushort)7770));
            Assert.That(invalidError, Does.Contain("invalid"));
        }

        [Test]
        public void NetworkConnectionService_TryParseCommandLinePortRejectsZeroAndNonNumericValues()
        {
            bool valid = NetworkTestUtilities.InvokeTryParseCommandLinePort("7776", out ushort validPort);
            bool zero = NetworkTestUtilities.InvokeTryParseCommandLinePort("0", out ushort zeroPort);
            bool text = NetworkTestUtilities.InvokeTryParseCommandLinePort("abc", out ushort textPort);

            Assert.That(valid, Is.True);
            Assert.That(validPort, Is.EqualTo((ushort)7776));

            Assert.That(zero, Is.False);
            Assert.That(zeroPort, Is.EqualTo((ushort)7770));

            Assert.That(text, Is.False);
            Assert.That(textPort, Is.EqualTo((ushort)7770));
        }
    }
}
