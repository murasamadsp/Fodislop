#nullable enable

using Fodinae.Networking.Connection;
using NUnit.Framework;

namespace Fodinae.Tests.Networking
{
    [TestFixture]
    public class ReconnectBackoffTests
    {
        [Test]
        public void FreshBackoff_StartsWithOneSecond()
        {
            var backoff = new ReconnectBackoff();
            Assert.AreEqual(0, backoff.AttemptCount);
            Assert.AreEqual(1f, backoff.CurrentDelay);
        }

        [Test]
        public void RecordFailure_ScalesExponentiallyThenCapsAtThirtySeconds()
        {
            var backoff = new ReconnectBackoff();
            float[] expectedDelays = [1f, 2f, 4f, 8f, 16f, 30f, 30f, 30f];
            foreach (float delay in expectedDelays)
            {
                Assert.AreEqual(delay, backoff.CurrentDelay);
                backoff.RecordFailure();
            }
        }

        [Test]
        public void Reset_RestartsSequenceFromOneSecond()
        {
            var backoff = new ReconnectBackoff();
            backoff.RecordFailure();
            backoff.RecordFailure();
            Assert.AreEqual(4f, backoff.CurrentDelay);

            backoff.Reset();
            Assert.AreEqual(0, backoff.AttemptCount);
            Assert.AreEqual(1f, backoff.CurrentDelay);
        }
    }
}
