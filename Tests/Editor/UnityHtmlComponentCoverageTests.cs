using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlComponentCoverageTests
    {
        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            UnityHtmlEnvironment.ResetOverrides();
            LogAssert.ignoreFailingMessages = false;
        }

        private static GameObject CreateRoot()
        {
            var rootObject = new GameObject("UnityHTML W2 Test Root", typeof(RectTransform), typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 800f);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 600f);
            return rootObject;
        }

        [Test]
        public void Mask_CreatesMaskWithGraphic()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<mask><text>clipped</text></mask>", null, "mask"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var mask = rootObject.GetComponentInChildren<Mask>(true);
                Assert.That(mask, Is.Not.Null);
                Assert.That(mask.showMaskGraphic, Is.False);
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void RectMask_AppliesSoftness()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<rectmask softness='4,8'><text>soft</text></rectmask>", null, "rectmask"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var mask = rootObject.GetComponentInChildren<RectMask2D>(true);
                Assert.That(mask, Is.Not.Null);
                Assert.That(mask.softness, Is.EqualTo(new Vector2Int(4, 8)));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void Scrollbar_LinksToScrollRectViaDataFor()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<view><scroll id='list' direction='vertical'><text>content</text></scroll>" +
                        "<scrollbar data-for='#list'/></view>",
                        null, "scrollbar"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);

                var scrollbar = rootObject.GetComponentsInChildren<Scrollbar>(true)
                    .FirstOrDefault(s => s.GetComponentInParent<ReactUnity.UGUI.Behaviours.ReactElement>()?.Component?.Tag == "scrollbar");
                Assert.That(scrollbar, Is.Not.Null, "scrollbar tag must create a Scrollbar");
                var scrollRect = rootObject.GetComponentsInChildren<ScrollRect>(true)
                    .FirstOrDefault(s => s.verticalScrollbar == scrollbar);
                Assert.That(scrollRect, Is.Not.Null, "scrollbar must link to the target's verticalScrollbar");
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataFill_SetsFilledImage()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<image data-fill='radial' data-fill-amount='0.75' data-fill-clockwise='false'/>",
                        null, "fill"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var image = rootObject.GetComponentInChildren<Image>(true);
                Assert.That(image.type, Is.EqualTo(Image.Type.Filled));
                Assert.That(image.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
                Assert.That(image.fillAmount, Is.EqualTo(0.75f).Within(0.001f));
                Assert.That(image.fillClockwise, Is.False);
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataGradient_AppliesVertexGradient()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<text data-gradient='#ffffff,#ff0000'>g</text>", null, "gradient"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var text = rootObject.GetComponentInChildren<TMPro.TMP_Text>(true);
                Assert.That(text.enableVertexGradient, Is.True);
                Assert.That(text.colorGradient.bottomLeft, Is.EqualTo(Color.red));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void Select_MaxHeightAndItemHeight_Apply()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<select options='A|B|C' value='1' max-height='120' item-height='40'/>",
                        null, "selectcfg"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var dropdown = rootObject.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
                Assert.That(dropdown, Is.Not.Null);
                Assert.That(dropdown.template.sizeDelta.y, Is.EqualTo(120f).Within(0.01f));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void Select_Searchable_AddsSearchInputToTemplate()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<select options='Alpha|Beta|Gamma' searchable='true'/>", null, "searchable"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var input = rootObject.GetComponentsInChildren<TMPro.TMP_InputField>(true)
                    .FirstOrDefault(i => i.name == "Search");
                Assert.That(input, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void Select_FilterMapsToOriginalIndex()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<select options='Alpha|Beta|Gamma' searchable='true'/>", null, "filter"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);

                var element = rootObject.GetComponentsInChildren<ReactUnity.UGUI.Behaviours.ReactElement>(true)
                    .First(e => e.Component?.Tag == "select");
                var component = element.Component as UnityHtmlSelectComponent;
                Assert.That(component, Is.Not.Null);
                component.ApplyFilter("gamma");
                Assert.That(component.Dropdown.options.Count, Is.EqualTo(1));
                Assert.That(component.OriginalIndex(0), Is.EqualTo(2));
                component.RestoreOptions();
                Assert.That(component.Dropdown.options.Count, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataHaptic_AttachesTrigger()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<button data-haptic='heavy'>go</button>", null, "haptic"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var trigger = rootObject.GetComponentInChildren<UnityHtmlHapticTrigger>(true);
                Assert.That(trigger, Is.Not.Null);
                Assert.That(trigger.Level, Is.EqualTo("heavy"));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void InputEnsure_CreatesEventSystemWithModule()
        {
            var existing = Object.FindFirstObjectByType<EventSystem>();
            Assert.That(existing, Is.Null, "test scene must start without an EventSystem");
            UnityHtmlInput.Ensure();
            try
            {
                var eventSystem = Object.FindFirstObjectByType<EventSystem>();
                Assert.That(eventSystem, Is.Not.Null);
                Assert.That(eventSystem.GetComponent<BaseInputModule>(), Is.Not.Null);
            }
            finally
            {
                foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(es.gameObject);
                var driver = Object.FindFirstObjectByType<UnityHtmlInputDriver>();
                if (driver != null)
                    Object.DestroyImmediate(driver.gameObject);
            }
        }

        [Test]
        public void InputBackRequested_Propagates()
        {
            var fired = 0;
            UnityHtmlInput.BackRequested += Handler;
            try
            {
                UnityHtmlInput.NotifyBackRequested();
                Assert.That(fired, Is.EqualTo(1));
            }
            finally { UnityHtmlInput.BackRequested -= Handler; }
            return;

            void Handler() => fired++;
        }

        [Test]
        public void Progress_BuildsTrackAndFill()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<progress value='0.6' max='1' fill-color='#00ff00'/>", null, "progress"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var images = rootObject.GetComponentsInChildren<Image>(true);
                var fill = images.FirstOrDefault(i => i.name == "Fill" && i.type == Image.Type.Filled);
                Assert.That(fill, Is.Not.Null);
                Assert.That(fill.fillAmount, Is.EqualTo(0.6f).Within(0.001f));
                Assert.That(fill.color, Is.EqualTo(Color.green));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void Radial_UsesRadialFillMethod()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<radial value='0.25'/>", null, "radial"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var fill = rootObject.GetComponentsInChildren<Image>(true)
                    .FirstOrDefault(i => i.name == "Fill");
                Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
                Assert.That(fill.fillAmount, Is.EqualTo(0.25f).Within(0.001f));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void Progress_LowThreshold_TintsFill()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<progress value='0.2' low-threshold='0.3' low-color='#ff0000' fill-color='#00ff00'/>",
                        null, "low"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var fill = rootObject.GetComponentsInChildren<Image>(true)
                    .FirstOrDefault(i => i.name == "Fill");
                Assert.That(fill.color, Is.EqualTo(Color.red));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void Switch_BuildsToggleAndKnob()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<switch value='true'/>", null, "switch"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var toggle = rootObject.GetComponentInChildren<Toggle>(true);
                Assert.That(toggle, Is.Not.Null);
                Assert.That(toggle.isOn, Is.True);
                Assert.That(rootObject.GetComponentsInChildren<Image>(true)
                    .Any(i => i.name == "Knob"), Is.True);
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void RawImage_TagExists()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<rawimage/>", null, "rawimage"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                Assert.That(rootObject.GetComponentInChildren<RawImage>(true), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(rootObject); }
        }
    }
}
