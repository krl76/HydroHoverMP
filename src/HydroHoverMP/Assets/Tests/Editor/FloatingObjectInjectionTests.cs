using System.Reflection;
using NUnit.Framework;
using Physics.Water;
using UnityEngine;

namespace HydroHoverMP.Tests.Editor
{
    /// <summary>
    /// Regression test for the dedicated server. FloatingObject (the buoy) lives in the Level
    /// scene, whose Zenject context inherits the GameplayContext contract. On the dedicated
    /// server the Gameplay scene can exist twice, so injecting WaterPhysicsSystem through the
    /// scene-context chain throws "multiple matches". The buoy therefore uses no DI and must
    /// resolve WaterPhysicsSystem via FindFirstObjectByType (like HoverCushion / HoverAerodynamics).
    /// </summary>
    public sealed class FloatingObjectInjectionTests
    {
        [Test]
        public void FloatingObject_ResolvesWaterSystem_WhenDependencyInjectionDidNotRun()
        {
            var waterObject = new GameObject(nameof(WaterPhysicsSystem));
            waterObject.AddComponent<WaterPhysicsSystem>();

            var buoyObject = new GameObject("Buoy");
            var buoy = buoyObject.AddComponent<FloatingObject>();

            try
            {
                // Simulate a frame in a build where no Zenject injection ran (the dedicated server).
                MethodInfo resolve = typeof(FloatingObject).GetMethod(
                    "ResolveWaterSystemIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(resolve, Is.Not.Null,
                    "FloatingObject should expose a water-system fallback resolver, mirroring HoverCushion.");

                resolve.Invoke(buoy, null);

                FieldInfo waterField = typeof(FloatingObject).GetField(
                    "_waterSystem", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(waterField.GetValue(buoy), Is.Not.Null,
                    "Buoy must resolve WaterPhysicsSystem via fallback when DI injection did not run.");
            }
            finally
            {
                Object.DestroyImmediate(buoyObject);
                Object.DestroyImmediate(waterObject);
            }
        }
    }
}
