using DG.Tweening;
using NUnit.Framework;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    /// <summary>
    /// P071: one global motion policy. Role declarations resolve through the
    /// shared token table; explicit data-motion attributes override locally;
    /// "exit" replays the role's exit preset; "none" opts out entirely.
    /// </summary>
    [TestFixture]
    internal sealed class UnityHtmlMotionPolicyTests
    {
        [Test]
        public void RoleDialog_AndExplicitAttrs_ResolveIdentical()
        {
            // P071 check: a dialog declared only by role must produce the same
            // motion lifecycle as one spelled out attribute-by-attribute.
            UnityHtmlDeclaredMotion byRole = UnityHtmlMotionPolicy.ResolveDeclared(
                "dialog", null, null, null, null, null);
            UnityHtmlDeclaredMotion explicit_ = UnityHtmlMotionPolicy.ResolveDeclared(
                null, "scale", "0.18", null, "20", "out-back");

            Assert.IsTrue(byRole.Animated);
            Assert.IsTrue(explicit_.Animated);
            Assert.AreEqual(explicit_.Preset, byRole.Preset);
            Assert.AreEqual(explicit_.Duration, byRole.Duration);
            Assert.AreEqual(explicit_.Distance, byRole.Distance);
            Assert.AreEqual(explicit_.Ease, byRole.Ease);
        }

        [Test]
        public void ExitToken_PlaysRoleExitPreset()
        {
            UnityHtmlDeclaredMotion motion = UnityHtmlMotionPolicy.ResolveDeclared(
                "panel", "exit", null, null, null, null);

            Assert.IsTrue(motion.Animated);
            Assert.AreEqual("fade-out", motion.Preset);
            Assert.AreEqual(Ease.InQuad, motion.Ease);
            Assert.AreEqual(0.12f, motion.Duration, 0.001f);
        }

        [Test]
        public void ExitToken_ExplicitDuration_OverridesRoleExit()
        {
            UnityHtmlDeclaredMotion motion = UnityHtmlMotionPolicy.ResolveDeclared(
                "panel", "exit", "0.12", null, null, null);

            Assert.AreEqual(0.12f, motion.Duration, 0.001f);
            Assert.AreEqual("fade-out", motion.Preset);
        }

        [Test]
        public void ExplicitTokens_OverrideRoleDefaults()
        {
            UnityHtmlDeclaredMotion motion = UnityHtmlMotionPolicy.ResolveDeclared(
                "panel", "fade", "0.5", "0.1", "64", "linear");

            Assert.AreEqual("fade", motion.Preset);
            Assert.AreEqual(0.5f, motion.Duration, 0.001f);
            Assert.AreEqual(0.1f, motion.Delay, 0.001f);
            Assert.AreEqual(64f, motion.Distance, 0.001f);
            Assert.AreEqual(Ease.Linear, motion.Ease);
        }

        [Test]
        public void NoneRole_IsMotionless()
        {
            Assert.IsFalse(UnityHtmlMotionPolicy.ResolveDeclared(
                "none", null, null, null, null, null).Animated);
        }

        [Test]
        public void NonePreset_IsMotionless()
        {
            Assert.IsFalse(UnityHtmlMotionPolicy.ResolveDeclared(
                "panel", "none", null, null, null, null).Animated);
        }

        [Test]
        public void Exit_WithoutRole_ProducesNothing()
        {
            Assert.IsFalse(UnityHtmlMotionPolicy.ResolveDeclared(
                null, "exit", null, null, null, null).Animated);
        }

        [Test]
        public void NoMotionAttributes_ProducesNothing()
        {
            Assert.IsFalse(UnityHtmlMotionPolicy.ResolveDeclared(
                null, null, null, null, null, null).Animated);
        }

        [Test]
        public void UnknownRole_FallsBackToExplicitPreset()
        {
            UnityHtmlDeclaredMotion motion = UnityHtmlMotionPolicy.ResolveDeclared(
                "not-a-role", "fade", null, null, null, null);

            Assert.IsTrue(motion.Animated);
            Assert.AreEqual("fade", motion.Preset);
            Assert.AreEqual(UnityHtmlMotionPolicy.DefaultDuration, motion.Duration, 0.001f);
        }

        [Test]
        public void EveryRole_HasEnterAndExitPresets()
        {
            foreach (string role in UnityHtmlMotionPolicy.RoleNames)
            {
                Assert.IsTrue(UnityHtmlMotionPolicy.TryResolve(role, out var spec),
                    $"Role '{role}' must resolve.");
                if (spec.Motionless) continue;
                Assert.IsFalse(string.IsNullOrWhiteSpace(spec.EnterPreset),
                    $"Role '{role}' needs an entry preset.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(spec.ExitPreset),
                    $"Role '{role}' needs an exit preset so closable UI can leave smoothly.");
            }
        }
    }
}
