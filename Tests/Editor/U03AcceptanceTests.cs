using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    /// <summary>
    /// U03 acceptance — the wheel-sensitivity multiplier is one canonical
    /// value: bounded at the settings boundary, never compounded on reapply,
    /// and invalid persisted values recover to a usable default instead of
    /// inverting or wedging every mounted scroll control.
    /// </summary>
    [TestFixture]
    internal sealed class U03AcceptanceTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            _go = null;
        }

        private MoyvaScrollSensitivity CreateBinding(float baseline)
        {
            _go = new GameObject("scroll", typeof(RectTransform), typeof(ScrollRect));
            _go.GetComponent<ScrollRect>().scrollSensitivity = baseline;
            return _go.AddComponent<MoyvaScrollSensitivity>();
        }

        [Test]
        public void WheelSensitivity_InvalidValues_FallBackToDefault()
        {
            Assert.AreEqual(1f, UnityHtmlScrollSettings.Default
                .WithWheelSensitivity(float.NaN).WheelSensitivity);
            Assert.AreEqual(1f, UnityHtmlScrollSettings.Default
                .WithWheelSensitivity(float.PositiveInfinity).WheelSensitivity);
            Assert.AreEqual(1f, UnityHtmlScrollSettings.Default
                .WithWheelSensitivity(-0.5f).WheelSensitivity,
                "A negative multiplier would invert wheel scrolling.");
        }

        [Test]
        public void WheelSensitivity_Extremes_StayWithinCanonicalBounds()
        {
            Assert.AreEqual(0f, UnityHtmlScrollSettings.Default
                .WithWheelSensitivity(0f).WheelSensitivity,
                "Zero is a legal explicit 'wheel off' — saved min values clamp upstream.");
            Assert.AreEqual(UnityHtmlScrollSettings.MaxWheelSensitivity,
                UnityHtmlScrollSettings.Default
                    .WithWheelSensitivity(999f).WheelSensitivity,
                "A corrupt huge multiplier must not make lists unusable.");
        }

        [Test]
        public void WheelSensitivity_ObjectInitializer_Path_IsClampedToo()
        {
            // Consumers may build the struct directly (tests, future callers)
            // instead of going through WithWheelSensitivity — the boundary
            // must hold either way.
            var settings = new UnityHtmlScrollSettings { WheelSensitivity = -3f };
            Assert.AreEqual(1f, settings.WheelSensitivity);
        }

        [Test]
        public void Apply_MultipliesBaseline_WithoutCompounding()
        {
            MoyvaScrollSensitivity binding = CreateBinding(10f);

            binding.Apply(2f);
            Assert.AreEqual(20f, _go.GetComponent<ScrollRect>().scrollSensitivity,
                "First apply: baseline × multiplier.");

            binding.Apply(4f);
            Assert.AreEqual(40f, _go.GetComponent<ScrollRect>().scrollSensitivity,
                "Reapply must scale the captured baseline, not the last result.");
        }

        [Test]
        public void Apply_InvalidMultiplier_RecoversToUsableScroll()
        {
            MoyvaScrollSensitivity binding = CreateBinding(10f);

            binding.Apply(float.NaN);
            Assert.AreEqual(10f, _go.GetComponent<ScrollRect>().scrollSensitivity,
                "Invalid input must not poison scrollSensitivity.");

            binding.Apply(-1f);
            Assert.AreEqual(10f, _go.GetComponent<ScrollRect>().scrollSensitivity,
                "A negative multiplier must not invert the wheel.");
        }

        [Test]
        public void Apply_ZeroMultiplier_DisablesWheelScrolling()
        {
            MoyvaScrollSensitivity binding = CreateBinding(10f);
            binding.Apply(0f);
            Assert.AreEqual(0f, _go.GetComponent<ScrollRect>().scrollSensitivity,
                "0 is a deliberate 'wheel off' value, not corruption.");
        }
    }
}
