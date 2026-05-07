using FishNet.Component.Scenes;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HydroHoverMP.Tests.Editor
{
    public sealed class SceneAndPrefabSmokeTests
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string HoverCraftPrefabPath = "Assets/Prefabs/Player/HoverCraft.prefab";

        [Test]
        public void BootstrapScene_ContainsFishNetSetupComponents()
        {
            Scene scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject networkObject = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == "FishNet NetworkManager")
                    {
                        networkObject = root;
                        break;
                    }
                }

                Assert.That(networkObject, Is.Not.Null, "Bootstrap should contain the FishNet network root object.");

                Assert.That(networkObject.GetComponent<NetworkManager>(), Is.Not.Null);
                Assert.That(networkObject.GetComponent<TransportManager>(), Is.Not.Null);
                Assert.That(networkObject.GetComponent<Tugboat>(), Is.Not.Null);
                Assert.That(networkObject.GetComponent<DefaultScene>(), Is.Not.Null);
                Assert.That(networkObject.GetComponent<PlayerSpawner>(), Is.Not.Null);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void HoverCraftPrefab_ContainsNetworkPlayerWiringComponents()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HoverCraftPrefabPath);

            Assert.That(prefab, Is.Not.Null, "HoverCraft prefab should be loadable at the expected path.");
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkTransform>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Features.Networking.NetworkPlayerData>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Features.Networking.NetworkHoverOwnerBridge>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Features.Networking.NetworkHydroPulse>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Features.Networking.PlayerNameplate>(), Is.Not.Null);
        }
    }
}
