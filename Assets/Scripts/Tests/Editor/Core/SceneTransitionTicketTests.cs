#nullable enable

using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fodinae.Tests.Core
{
    /// <summary>
    /// SceneTransitionTicket guards the single-writer handshake between the
    /// persistent Bootstrap composition root and exactly one content-scene
    /// composition root. The state machine is the load-bearing invariant:
    /// attach-once → activation → startup-ready → presentation-ready, with
    /// failure short-circuiting every waiter exactly once.
    /// </summary>
    [TestFixture]
    public class SceneTransitionTicketTests
    {
        private Scene _scene;
        private SceneSetup[] _originalSetup = null!;

        [SetUp]
        public void SetUp()
        {
            _originalSetup = EditorSceneManager.GetSceneManagerSetup();
            _scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/Bootstrap.unity",
                OpenSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            if (_originalSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(_originalSetup);
            }
        }

        [Test]
        public void Attach_ValidScene_MarksAttached()
        {
            var ticket = new SceneTransitionTicket(_scene.name);

            Assert.IsFalse(ticket.IsAttached);
            Assert.DoesNotThrow(() => ticket.Attach(_scene));
            Assert.IsTrue(ticket.IsAttached);
        }

        [Test]
        public void Attach_SecondCompositionRoot_Throws()
        {
            var ticket = new SceneTransitionTicket(_scene.name);
            ticket.Attach(_scene);

            Assert.Throws<InvalidOperationException>(
                () => ticket.Attach(_scene));
        }

        [Test]
        public void Attach_SceneNameMismatch_Throws()
        {
            var ticket = new SceneTransitionTicket($"{_scene.name}.Other");

            Assert.Throws<InvalidOperationException>(
                () => ticket.Attach(_scene));
        }

        [Test]
        public void StateChange_BeforeAttach_Throws()
        {
            var ticket = new SceneTransitionTicket("MainGame");

            Assert.Throws<InvalidOperationException>(() => ticket.RequestActivation());
            Assert.Throws<InvalidOperationException>(() => ticket.MarkStartupReady());
            Assert.Throws<InvalidOperationException>(() => ticket.MarkPresentationReady());
        }

        [Test]
        public void PresentationReady_BeforeStartupReady_Throws()
        {
            var ticket = CreateAttachedTicket();

            Assert.Throws<InvalidOperationException>(() => ticket.MarkPresentationReady());
        }

        [Test]
        public void HappyPath_Attach_Activate_Startup_Presentation()
        {
            var ticket = CreateAttachedTicket();
            ticket.RequestActivation();
            ticket.MarkStartupReady();

            Assert.IsTrue(ticket.IsStartupReady);
            Assert.DoesNotThrow(() => ticket.MarkPresentationReady());
            Assert.IsTrue(ticket.IsPresentationReady);
        }

        [Test]
        public void Fail_AfterAttach_CompletesWaitersWithException()
        {
            var ticket = CreateAttachedTicket();
            var failure = new InvalidOperationException("world load failed");

            ticket.Fail(failure);

            Assert.Throws<InvalidOperationException>(
                () => ticket.WaitForStartupAsync().GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(
                () => ticket.WaitForPresentationAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Fail_CompletesFailureSignal()
        {
            var ticket = CreateAttachedTicket();

            ticket.Fail(new InvalidOperationException("startup failed"));

            Assert.DoesNotThrow(
                () => ticket.WaitForFailureAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Fail_AfterPresentationReady_IsIgnored()
        {
            var ticket = CreateAttachedTicket();
            ticket.RequestActivation();
            ticket.MarkStartupReady();
            ticket.MarkPresentationReady();

            Assert.DoesNotThrow(() => ticket.Fail(new InvalidOperationException("late failure")));
            Assert.IsTrue(ticket.IsPresentationReady);
        }

        [Test]
        public void Fail_BeforeAttach_StillCompletesStartupWaiter()
        {
            var ticket = new SceneTransitionTicket("MainGame");
            ticket.Fail(new InvalidOperationException("boot failure"));

            Assert.Throws<InvalidOperationException>(
                () => ticket.WaitForStartupAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Constructor_BlankSceneName_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SceneTransitionTicket(" "));
            Assert.Throws<ArgumentException>(() => new SceneTransitionTicket(""));
        }

        [Test]
        public void Constructor_NonPositiveTimeout_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SceneTransitionTicket("MainGame", TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SceneTransitionTicket("MainGame", TimeSpan.FromMilliseconds(-1)));
        }

        [UnityTest]
        public IEnumerator WaitForStartup_Timeout_FailsWithTimeoutException()
        {
            var ticket = new SceneTransitionTicket("MainGame", TimeSpan.FromMilliseconds(50));
            try
            {
                yield return UniTask.Delay(150).ToCoroutine();

                Assert.Throws<TimeoutException>(
                    () => ticket.WaitForStartupAsync().GetAwaiter().GetResult());
                Assert.Throws<TimeoutException>(
                    () => ticket.WaitForPresentationAsync().GetAwaiter().GetResult());
            }
            finally
            {
                ticket.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Timeout_RejectsLateStateChanges()
        {
            var ticket = new SceneTransitionTicket("MainGame", TimeSpan.FromMilliseconds(50));
            try
            {
                yield return UniTask.Delay(150).ToCoroutine();

                Assert.Throws<InvalidOperationException>(() => ticket.RequestActivation());
                Assert.Throws<InvalidOperationException>(() => ticket.MarkStartupReady());
                Assert.Throws<InvalidOperationException>(() => ticket.MarkPresentationReady());
            }
            finally
            {
                ticket.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Timeout_AfterPresentationReady_IsIgnored()
        {
            var ticket = CreateAttachedTicket(TimeSpan.FromMilliseconds(50));
            try
            {
                ticket.RequestActivation();
                ticket.MarkStartupReady();
                ticket.MarkPresentationReady();

                yield return UniTask.Delay(150).ToCoroutine();

                Assert.IsTrue(ticket.IsPresentationReady);
                Assert.DoesNotThrow(
                    () => ticket.Fail(new InvalidOperationException("late failure")));
                Assert.IsTrue(ticket.IsPresentationReady);
            }
            finally
            {
                ticket.Dispose();
            }
        }

        [Test]
        public void Dispose_CompletedTicket_IsSafe()
        {
            var ticket = CreateAttachedTicket();
            ticket.RequestActivation();
            ticket.MarkStartupReady();
            ticket.MarkPresentationReady();

            Assert.DoesNotThrow(() => ticket.Dispose());
            Assert.DoesNotThrow(() => ticket.Dispose());
            Assert.IsTrue(ticket.IsPresentationReady);
        }

        private SceneTransitionTicket CreateAttachedTicket()
        {
            var ticket = new SceneTransitionTicket(_scene.name);
            ticket.Attach(_scene);
            return ticket;
        }

        private SceneTransitionTicket CreateAttachedTicket(TimeSpan timeout)
        {
            var ticket = new SceneTransitionTicket(_scene.name, timeout);
            ticket.Attach(_scene);
            return ticket;
        }
    }
}
