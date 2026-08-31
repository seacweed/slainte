using NUnit.Framework;
using Slainte.Shared.Input;
using Slainte.Shared.Lifecycle;
using UnityEngine;

namespace Slainte.Shared.Tests
{
    public sealed class SharedRuntimeContractTests
    {
        private GameObject testObject;

        [TearDown]
        public void TearDown()
        {
            if (testObject != null)
                Object.DestroyImmediate(testObject);
        }

        [Test]
        public void CameraDirection_ValuesRemainStable()
        {
            Assert.That((int)CameraDirection.DrawerOpen, Is.EqualTo(0));
            Assert.That((int)CameraDirection.DrawerClose, Is.EqualTo(1));
        }

        [Test]
        public void SceneSingleton_ReturnsExistingSceneComponent()
        {
            testObject = new GameObject("SceneSingletonProbe");
            SceneSingletonProbe probe = testObject.AddComponent<SceneSingletonProbe>();

            Assert.That(SceneSingletonProbe.Instance, Is.SameAs(probe));
        }

        [Test]
        public void SceneSingleton_FindsReplacementAfterInstanceIsDestroyed()
        {
            testObject = new GameObject("FirstSceneSingletonProbe");
            SceneSingletonProbe first = testObject.AddComponent<SceneSingletonProbe>();
            Assert.That(SceneSingletonProbe.Instance, Is.SameAs(first));

            Object.DestroyImmediate(testObject);
            testObject = new GameObject("SecondSceneSingletonProbe");
            SceneSingletonProbe second = testObject.AddComponent<SceneSingletonProbe>();

            Assert.That(SceneSingletonProbe.Instance, Is.SameAs(second));
        }
    }

    public sealed class SceneSingletonProbe : SceneSingleton<SceneSingletonProbe>
    {
    }
}
