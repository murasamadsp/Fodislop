using NUnit.Framework;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;

namespace Fodinae.Tests.Core
{
    [TestFixture]
    public class ServiceLocatorTests
    {
        private interface ITestService
        {
            int GetValue();
        }

        private class TestServiceImplementation : ITestService
        {
            public int GetValue() => 42;
        }

        private class AnotherTestServiceImplementation : ITestService
        {
            public int GetValue() => 100;
        }

        [SetUp]
        public void SetUp()
        {
            // Ensure clean state before each test
            ServiceLocator.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
        }

        [Test]
        public void RegisterAndResolve_ValidService_ReturnsRegisteredInstance()
        {
            var impl = new TestServiceImplementation();
            ServiceLocator.Register<ITestService>(impl);

            var resolved = ServiceLocator.Resolve<ITestService>();

            Assert.IsNotNull(resolved, "Resolved service should not be null.");
            Assert.AreEqual(42, resolved.GetValue());
        }

        [Test]
        public void Resolve_UnregisteredService_ReturnsNull()
        {
            var resolved = ServiceLocator.Resolve<ITestService>();

            Assert.IsNull(resolved, "Resolving an unregistered service should return null.");
        }

        [Test]
        public void Register_OverwriteExisting_ReturnsNewInstance()
        {
            var impl1 = new TestServiceImplementation();
            var impl2 = new AnotherTestServiceImplementation();

            ServiceLocator.Register<ITestService>(impl1);
            ServiceLocator.Register<ITestService>(impl2);

            var resolved = ServiceLocator.Resolve<ITestService>();

            Assert.IsNotNull(resolved);
            Assert.AreEqual(100, resolved.GetValue(), "ServiceLocator should return the latest registered implementation.");
        }

        [Test]
        public void Clear_RemovesAllRegistrations()
        {
            var impl = new TestServiceImplementation();
            ServiceLocator.Register<ITestService>(impl);

            ServiceLocator.Clear();

            var resolved = ServiceLocator.Resolve<ITestService>();
            Assert.IsNull(resolved, "After Clear(), all service registrations should be removed.");
        }

        [Test]
        public void TryResolve_RegisteredService_ReturnsTrueAndInstance()
        {
            var impl = new TestServiceImplementation();
            ServiceLocator.Register<ITestService>(impl);

            bool success = ServiceLocator.TryResolve<ITestService>(out var resolved);

            Assert.IsTrue(success);
            Assert.IsNotNull(resolved);
            Assert.AreEqual(42, resolved.GetValue());
        }

        [Test]
        public void Unregister_ExistingService_RemovesRegistration()
        {
            var impl = new TestServiceImplementation();
            ServiceLocator.Register<ITestService>(impl);

            bool unregistered = ServiceLocator.Unregister<ITestService>();

            Assert.IsTrue(unregistered);
            Assert.IsFalse(ServiceLocator.IsRegistered<ITestService>());
            Assert.IsNull(ServiceLocator.Resolve<ITestService>());
        }
    }
}
