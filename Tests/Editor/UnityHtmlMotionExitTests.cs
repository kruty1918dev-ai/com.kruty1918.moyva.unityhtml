using System;
using System.Reflection;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    /// <summary>
    /// P072: a declarative exit (data-motion="exit") carries a guaranteed
    /// completion callback — ExitFinished fires exactly once whether the tween
    /// completes or is cancelled before the element is destroyed.
    /// </summary>
    [TestFixture]
    internal sealed class UnityHtmlMotionExitTests
    {
        private static readonly MethodInfo InternalPlay = typeof(UnityHtmlMotionBridge)
            .GetMethod("Play", BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[]
                {
                    typeof(string), typeof(RectTransform), typeof(string),
                    typeof(float), typeof(float), typeof(float),
                    typeof(Ease), typeof(bool),
                },
                null);

        private static void PlayExit(UnityHtmlMotionBridge bridge, string id, RectTransform target)
            => InternalPlay.Invoke(bridge, new object[]
            {
                id, target, "fade-out", 0.12f, 0f, 0f, Ease.InQuad, true,
            });

        private static void PlayEnter(UnityHtmlMotionBridge bridge, string id, RectTransform target)
            => InternalPlay.Invoke(bridge, new object[]
            {
                id, target, "fade", 0.12f, 0f, 0f, Ease.OutCubic, false,
            });

        [Test]
        public void Exit_CancelledMidFlight_FiresExitFinishedOnce()
        {
            var bridge = new UnityHtmlMotionBridge();
            var go = new GameObject("exit-target", typeof(RectTransform));
            int calls = 0;
            string finishedId = null;
            bridge.ExitFinished += id => { calls++; finishedId = id; };
            try
            {
                PlayExit(bridge, "panel", (RectTransform)go.transform);
                bridge.Stop("panel"); // cancellation before destroy — like HandleComponentRemoved

                Assert.AreEqual(1, calls, "A cancelled exit must still fire its completion callback.");
                Assert.AreEqual("panel", finishedId);

                bridge.Stop("panel"); // already settled — must not fire again
                Assert.AreEqual(1, calls, "ExitFinished must fire exactly once.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Exit_TeardownBeforeFinish_CallbackStillFires()
        {
            var bridge = new UnityHtmlMotionBridge();
            var go = new GameObject("exit-target", typeof(RectTransform));
            int calls = 0;
            bridge.ExitFinished += _ => calls++;
            PlayExit(bridge, "panel", (RectTransform)go.transform);
            // Teardown mid-exit: the element is destroyed and the host detaches —
            // Stop cancels the tween, OnKill must still finish the exit.
            UnityEngine.Object.DestroyImmediate(go);
            bridge.Detach();
            Assert.AreEqual(1, calls,
                "Teardown mid-exit must cancel-and-finish the callback, not hang it.");
        }

        [Test]
        public void Enter_Cancelled_DoesNotFireExitFinished()
        {
            var bridge = new UnityHtmlMotionBridge();
            var go = new GameObject("enter-target", typeof(RectTransform));
            int calls = 0;
            bridge.ExitFinished += _ => calls++;
            try
            {
                PlayEnter(bridge, "panel", (RectTransform)go.transform);
                bridge.Stop("panel");
                Assert.AreEqual(0, calls, "Entry motions must not fire the exit callback.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ReducedMotion_Exit_FiresCallbackImmediatelyWithoutTween()
        {
            var bridge = new UnityHtmlMotionBridge { ReducedMotion = true };
            var go = new GameObject("exit-target", typeof(RectTransform));
            int calls = 0;
            bridge.ExitFinished += _ => calls++;
            try
            {
                PlayExit(bridge, "panel", (RectTransform)go.transform);
                Assert.AreEqual(1, calls,
                    "Under reduced motion an exit must complete instantly, keeping the callback.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ReducedMotion_Enter_LeavesElementAtRestingPose()
        {
            var bridge = new UnityHtmlMotionBridge { ReducedMotion = true };
            var go = new GameObject("enter-target", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            var resting = rect.anchoredPosition;
            int calls = 0;
            bridge.ExitFinished += _ => calls++;
            try
            {
                PlayEnter(bridge, "panel", rect);
                Assert.AreEqual(0, calls);
                Assert.AreEqual(resting, rect.anchoredPosition,
                    "Reduced motion must not move the element before its final pose.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ReducedMotion_ToggledMidExit_CompletesCallbackOnce()
        {
            var bridge = new UnityHtmlMotionBridge();
            var go = new GameObject("exit-target", typeof(RectTransform));
            int calls = 0;
            bridge.ExitFinished += _ => calls++;
            try
            {
                PlayExit(bridge, "panel", (RectTransform)go.transform);
                bridge.ReducedMotion = true; // toggled mid-close — in-flight exit snaps finished

                Assert.AreEqual(1, calls,
                    "Enabling reduced motion mid-exit must finish the close callback.");

                bridge.Stop("panel");
                Assert.AreEqual(1, calls, "The exit callback stays exactly-once.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
