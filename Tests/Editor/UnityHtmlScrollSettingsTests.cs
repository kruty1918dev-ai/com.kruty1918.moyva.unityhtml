using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ReactUnity.UGUI.Behaviours;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    // P075: scroll settings (wheel sensitivity, inertia, deceleration, reduced
    // motion) come from the host's configuration and apply per live control.
    public sealed class UnityHtmlScrollSettingsTests
    {
        private const string ScrollMarkup =
            "<scroll id='list'><view class='tall'></view></scroll>";
        private const string Css =
            "scroll { height: 100px; width: 200px; } .tall { height: 2000px; }";

        private GameObject _root;
        private UnityHtmlHost _host;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HTML scroll", typeof(RectTransform), typeof(Canvas));
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 600);
            _host = new UnityHtmlHost();
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void Mount_AppliesConfiguredScrollSettingsToControls()
        {
            _host.ScrollSettings = new UnityHtmlScrollSettings
            {
                Smoothness = 0.3f,
                WheelSensitivity = 2f,
                Inertia = false,
                DecelerationRate = 0.5f,
                ReducedMotion = false,
            };
            Mount(ScrollMarkup);

            MoyvaSmoothScrollRect scroll = FindScroll("list");
            Assert.That(scroll.Smoothness, Is.EqualTo(0.3f).Within(0.0001f));
            // The scroll component ships a baseline sensitivity of 50; the
            // setting is a multiplier on top of it.
            Assert.That(scroll.scrollSensitivity, Is.EqualTo(100f).Within(0.01f));
            Assert.That(scroll.inertia, Is.False);
            Assert.That(scroll.decelerationRate, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(scroll.Settings.WheelSensitivity, Is.EqualTo(2f));
        }

        [Test]
        public void ScrollSettings_ChangeReappliesToMountedControls()
        {
            Mount(ScrollMarkup);
            MoyvaSmoothScrollRect scroll = FindScroll("list");
            float baseline = scroll.scrollSensitivity;

            _host.ScrollSettings = _host.ScrollSettings.WithReducedMotion(true);

            Assert.That(scroll.Smoothness, Is.EqualTo(0f));
            Assert.That(scroll.inertia, Is.False);
            Assert.That(scroll.Settings.ReducedMotion, Is.True);

            _host.ScrollSettings = UnityHtmlScrollSettings.Default;

            Assert.That(scroll.Smoothness, Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That(scroll.inertia, Is.True);
            Assert.That(scroll.scrollSensitivity, Is.EqualTo(baseline).Within(0.001f));
        }

        [Test]
        public void WheelSensitivity_ReapplyScalesFromBaselineWithoutCompounding()
        {
            Mount(ScrollMarkup);
            MoyvaSmoothScrollRect scroll = FindScroll("list");

            _host.ScrollSettings = _host.ScrollSettings.WithWheelSensitivity(0.5f);
            Assert.That(scroll.scrollSensitivity, Is.EqualTo(25f).Within(0.01f));

            _host.ScrollSettings = _host.ScrollSettings.WithWheelSensitivity(1.5f);
            Assert.That(scroll.scrollSensitivity, Is.EqualTo(75f).Within(0.01f));
        }

        [Test]
        public void ScrollSettings_PlainScrollRectsScaleFromTheirBaseline()
        {
            Mount(ScrollMarkup);

            var plain = new GameObject("plain-scroll", typeof(RectTransform));
            plain.transform.SetParent(_root.transform, false);
            var rect = plain.AddComponent<ScrollRect>();
            rect.scrollSensitivity = 28f;

            _host.ScrollSettings = _host.ScrollSettings.WithWheelSensitivity(0.5f);
            Assert.That(rect.scrollSensitivity, Is.EqualTo(14f).Within(0.01f));

            _host.ScrollSettings = _host.ScrollSettings.WithWheelSensitivity(1.5f);
            Assert.That(rect.scrollSensitivity, Is.EqualTo(42f).Within(0.01f));
        }

        [Test]
        public void ReducedMotion_ScrollSnapsInstantlyAndReversesWithoutDrift()
        {
            _host.ScrollSettings = _host.ScrollSettings.WithReducedMotion(true);
            Mount(ScrollMarkup);
            MoyvaSmoothScrollRect scroll = FindScroll("list");
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1f).Within(0.001f));

            Wheel(scroll, new Vector2(0, -10));
            float afterDown = scroll.verticalNormalizedPosition;
            Assert.That(afterDown, Is.LessThan(1f));

            // Abrupt reversal: with instant scrolling the second wheel event must
            // move the position back up, not continue drifting down.
            Wheel(scroll, new Vector2(0, 10));
            Assert.That(scroll.verticalNormalizedPosition, Is.GreaterThan(afterDown));
        }

        [Test]
        public void UpdateRegion_PreservesScrollPositionAndVelocityOfLiveControl()
        {
            _host.ScrollSettings = _host.ScrollSettings.WithReducedMotion(true);
            Mount("<view id='side'><text>Static</text></view>" + ScrollMarkup);
            MoyvaSmoothScrollRect scroll = FindScroll("list");

            Wheel(scroll, new Vector2(0, -10));
            float position = scroll.verticalNormalizedPosition;
            scroll.velocity = new Vector2(0, 3f);
            Assert.That(position, Is.LessThan(1f));

            Assert.That(_host.UpdateRegion("side", "<view id='side'><text>Changed</text></view>"), Is.True);

            Assert.That(FindScroll("list"), Is.SameAs(scroll));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(position).Within(0.001f));
            Assert.That(scroll.velocity.y, Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void UpdateRegion_NewScrollStartsAtTopAndInheritsSettings()
        {
            _host.ScrollSettings = _host.ScrollSettings.WithReducedMotion(true);
            Mount("<view id='side'><text>Static</text></view>");

            Assert.That(_host.UpdateRegion("side", "<view id='side'>" + ScrollMarkup + "</view>"), Is.True);

            MoyvaSmoothScrollRect scroll = FindScroll("list");
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1f).Within(0.001f));
            Assert.That(scroll.velocity, Is.EqualTo(Vector2.zero));
            Assert.That(scroll.Settings.ReducedMotion, Is.True);
            Assert.That(scroll.inertia, Is.False);
        }

        private void Mount(string html)
        {
            UnityHtmlMountResult result = _host.Mount(
                _root.GetComponent<RectTransform>(),
                new UnityHtmlDocument(html, Css, "Scroll settings"));
            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        }

        private static void Wheel(ScrollRect scroll, Vector2 delta)
        {
            var data = new PointerEventData(EventSystem.current) { scrollDelta = delta };
            scroll.OnScroll(data);
        }

        private MoyvaSmoothScrollRect FindScroll(string id) => _root
            .GetComponentsInChildren<ReactElement>(true)
            .Single(element => element.Component != null && element.Component.Id == id)
            .GetComponent<MoyvaSmoothScrollRect>();
    }
}
