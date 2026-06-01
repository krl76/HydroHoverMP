using Features.Networking;
using NUnit.Framework;

namespace HydroHoverMP.Tests.Editor
{
    public sealed class AddressablesSceneProcessorTests
    {
        [Test]
        public void MapSceneNameToAddress_PrefixesWithSceneFolder()
        {
            Assert.That(AddressablesSceneProcessor.MapSceneNameToAddress("Gameplay"), Is.EqualTo("Scene/Gameplay"));
            Assert.That(AddressablesSceneProcessor.MapSceneNameToAddress("Level"), Is.EqualTo("Scene/Level"));
        }

        [Test]
        public void IsAddressableScene_TrueForKnownScenes()
        {
            Assert.That(AddressablesSceneProcessor.IsAddressableScene("Gameplay"), Is.True);
            Assert.That(AddressablesSceneProcessor.IsAddressableScene("Level"), Is.True);
            Assert.That(AddressablesSceneProcessor.IsAddressableScene("MainMenu"), Is.True);
        }

        [Test]
        public void IsAddressableScene_FalseForBootstrap()
        {
            Assert.That(AddressablesSceneProcessor.IsAddressableScene("Bootstrap"), Is.False);
        }
    }
}
