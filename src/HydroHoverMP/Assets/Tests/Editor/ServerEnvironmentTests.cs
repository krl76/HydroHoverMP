using Infrastructure.Services.Network;
using NUnit.Framework;

namespace HydroHoverMP.Tests.Editor
{
    public sealed class ServerEnvironmentTests
    {
        [Test]
        public void HasServerArgument_TrueForDedicatedServerFlag()
        {
            Assert.That(ServerEnvironment.HasServerArgument(new[] { "app.exe", "-dedicatedServer" }), Is.True);
        }

        [Test]
        public void HasServerArgument_TrueForServerOnlyFlagCaseInsensitive()
        {
            Assert.That(ServerEnvironment.HasServerArgument(new[] { "app.exe", "-SERVERONLY" }), Is.True);
        }

        [Test]
        public void HasServerArgument_FalseForClientArgs()
        {
            Assert.That(ServerEnvironment.HasServerArgument(new[] { "app.exe", "-port", "7770" }), Is.False);
        }

        [Test]
        public void HasServerArgument_FalseForNullOrEmpty()
        {
            Assert.That(ServerEnvironment.HasServerArgument(null), Is.False);
            Assert.That(ServerEnvironment.HasServerArgument(new string[0]), Is.False);
        }
    }
}
