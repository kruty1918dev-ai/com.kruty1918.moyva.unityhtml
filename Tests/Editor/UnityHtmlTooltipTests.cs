using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ReactUnity.UGUI;
using ReactUnity.UGUI.Behaviours;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    // P077: one tooltip layer — delay, fade-in, hot-window retarget without
    // flicker, bounds clamping, and a screen-anchored world target feeding the
    // same panel instead of a parallel system.
    public sealed class UnityHtmlTooltipTests
    {
        private static readonly MethodInfo LateUpdateMethod = typeof(UnityHtmlTooltipLayer)
            .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        private GameObject _root;
        private UnityHtmlHost _host;
        private UnityHtmlTooltipLayer _layer;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HTML tooltips", typeof(RectTransform), typeof(Canvas));
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
        public void ElementTooltip_ShowsAfterDelayAndStaysInsideBounds()
        {
            Mount("<view id='target' data-tooltip='Warehouse stock'><text>x</text></view>");
            CreateLayer();
            _layer.ShowDelaySeconds = 0f;

            UnityHtmlTooltipTarget target = _root
                .GetComponentsInChildren<UnityHtmlTooltipTarget>(true)
                .Single(t => t.Source != null && t.Source.Id == "target");
            target.OnPointerEnter(new PointerEventData(EventSystem.current));
            Tick();

            RectTransform panel = PanelRect();
            Assert.That(panel.gameObject.activeSelf, Is.True);
            AssertInsideRoot(panel);
        }

        [Test]
        public void AdjacentTargets_RetargetWithinHotWindowWithoutFlicker()
        {
            Mount("<view id='a' data-tooltip='First'><text>a</text></view>"
                + "<view id='b' data-tooltip='Second'><text>b</text></view>");
            CreateLayer();
            _layer.ShowDelaySeconds = 0f;
            _layer.ReducedMotion = true;

            var targets = _root.GetComponentsInChildren<UnityHtmlTooltipTarget>(true)
                .ToDictionary(t => t.Source.Id);
            var eventData = new PointerEventData(EventSystem.current);
            targets["a"].OnPointerEnter(eventData);
            Tick();
            Assert.That(PanelRect().gameObject.activeSelf, Is.True);

            // Sweep to the adjacent control: the tooltip retargets in the same
            // frame and never deactivates — no flicker between controls.
            targets["a"].OnPointerExit(eventData);
            targets["b"].OnPointerEnter(eventData);
            Tick();

            RectTransform panel = PanelRect();
            Assert.That(panel.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void WorldTooltip_AnchorsAtPointerAndClampsInsideBounds()
        {
            Mount("<view><text>x</text></view>");
            CreateLayer();
            _layer.ShowDelaySeconds = 0f;
            _layer.ReducedMotion = true;

            // Screen edge: the panel must clamp back inside the root rect.
            Vector2 edge = new Vector2(Screen.width - 4, Screen.height - 4);
            _host.SetWorldTooltip("Water Mill", edge);
            _host.SetWorldTooltip("Water Mill", edge);
            Tick();

            RectTransform panel = PanelRect();
            Assert.That(panel.gameObject.activeSelf, Is.True);
            AssertInsideRoot(panel);

            _host.SetWorldTooltip(null, edge);
            Tick();
            Assert.That(panel.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void FadeIn_RespectsReducedMotion()
        {
            Mount("<view><text>x</text></view>");
            CreateLayer();
            _layer.ShowDelaySeconds = 0f;
            _layer.FadeSeconds = 60f; // huge so unscaledDeltaTime can't finish it

            _host.SetWorldTooltip("Mill", new Vector2(100, 100));
            Tick();
            var group = PanelRect().GetComponent<CanvasGroup>();
            Assert.That(group.alpha, Is.LessThan(1f));

            _host.SetWorldTooltip(null, Vector2.zero);
            _layer.ReducedMotion = true;
            _host.SetWorldTooltip("Mill", new Vector2(100, 100));
            Tick();
            Assert.That(group.alpha, Is.EqualTo(1f));
        }

        [Test]
        public void KeyboardFocus_ShowsAndHidesTooltip()
        {
            Mount("<view id='target' data-tooltip='Focus tip'><text>x</text></view>");
            CreateLayer();
            _layer.ShowDelaySeconds = 0f;
            _layer.ReducedMotion = true;

            UnityHtmlTooltipTarget target = Target("target");
            var eventData = new BaseEventData(EventSystem.current);
            target.OnSelect(eventData);
            Tick();
            Assert.That(PanelRect().gameObject.activeSelf, Is.True);

            target.OnDeselect(eventData);
            Tick();
            Assert.That(PanelRect().gameObject.activeSelf, Is.False);
        }

        [Test]
        public void TouchTap_DoesNotFlashTooltip()
        {
            Mount("<view id='target' data-tooltip='Hold tip'><text>x</text></view>");
            CreateLayer();
            _layer.ShowDelaySeconds = 0f;

            // Touch enter fires on finger-down — it must not arm the hover path.
            var down = new PointerEventData(EventSystem.current) { pointerId = 0 };
            Target("target").OnPointerEnter(down);
            Target("target").OnPointerDown(down);
            TickHold();
            Assert.That(PanelRect().gameObject.activeSelf, Is.False);
        }

        [Test]
        public void TouchHold_ShowsTooltipAndSuppressesTheTapClick()
        {
            Mount("<view id='target' data-tooltip='Hold tip'><text>x</text></view>");
            CreateLayer();
            _layer.ShowDelaySeconds = 0f;
            UnityHtmlTooltipTarget target = Target("target");
            target.TouchHoldSeconds = 0f;

            var down = new PointerEventData(EventSystem.current)
            { pointerId = 0, eligibleForClick = true };
            target.OnPointerDown(down);
            TickHold();
            Tick();

            Assert.That(PanelRect().gameObject.activeSelf, Is.True);
            Assert.That(down.eligibleForClick, Is.False);

            target.OnPointerUp(down);
            Assert.That(PanelRect().gameObject.activeSelf, Is.False);
        }

        private UnityHtmlTooltipTarget Target(string id) => _root
            .GetComponentsInChildren<UnityHtmlTooltipTarget>(true)
            .Single(t => t.Source != null && t.Source.Id == id);

        private void TickHold() => typeof(UnityHtmlTooltipTarget)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(Target("target"), null);

        private void Mount(string html)
        {
            UnityHtmlMountResult result = _host.Mount(
                _root.GetComponent<RectTransform>(),
                new UnityHtmlDocument(html, string.Empty, "Tooltips"));
            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        }

        private void CreateLayer()
        {
            var context = (UGUIContext)typeof(UnityHtmlHost)
                .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_host);
            _layer = _root.AddComponent<UnityHtmlTooltipLayer>();
            _layer.Bind(context, _root.GetComponent<RectTransform>());
            _layer.RefreshTargets();
            // The host only creates the layer in play mode; wire the test one
            // so the public SetWorldTooltip seam reaches it.
            typeof(UnityHtmlHost)
                .GetField("_tooltips", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_host, _layer);
        }

        private void Tick() => LateUpdateMethod.Invoke(_layer, null);

        private RectTransform PanelRect() => _root
            .GetComponentsInChildren<RectTransform>(true)
            .Single(r => r.GetComponent<Canvas>() != null
                && r.GetComponent<Canvas>().overrideSorting
                && r.GetComponent<Canvas>().sortingOrder == 32000);

        private void AssertInsideRoot(RectTransform panel)
        {
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            Rect rootRect = _root.GetComponent<RectTransform>().rect;
            Vector3 rootCenter = _root.transform.position;
            foreach (Vector3 world in corners)
            {
                Vector3 local = _root.transform.InverseTransformPoint(world);
                Assert.That(local.x, Is.GreaterThanOrEqualTo(rootRect.xMin - 1f),
                    $"tooltip left/bottom x={local.x} escapes {rootRect.xMin}");
                Assert.That(local.x, Is.LessThanOrEqualTo(rootRect.xMax + 1f),
                    $"tooltip right x={local.x} escapes {rootRect.xMax}");
                Assert.That(local.y, Is.GreaterThanOrEqualTo(rootRect.yMin - 1f),
                    $"tooltip bottom y={local.y} escapes {rootRect.yMin}");
                Assert.That(local.y, Is.LessThanOrEqualTo(rootRect.yMax + 1f),
                    $"tooltip top y={local.y} escapes {rootRect.yMax}");
            }
        }
    }
}
