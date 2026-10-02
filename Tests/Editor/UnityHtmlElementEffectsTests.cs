using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlElementEffectsTests
    {
        private GameObject _eventSystemObject;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        [TearDown]
        public void TearDown()
        {
            UnityHtmlEnvironment.ResetOverrides();
            LogAssert.ignoreFailingMessages = false;
            if (_eventSystemObject != null) Object.DestroyImmediate(_eventSystemObject);
        }

        private static GameObject CreateRoot()
        {
            var rootObject = new GameObject("UnityHTML Effects Test Root", typeof(RectTransform), typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 800f);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 600f);
            return rootObject;
        }

        [Test]
        public void DataRaycastOff_DisablesGraphicRaycasts()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<button data-raycast='off'><text>B</text></button>", null, "raycast"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var graphics = rootObject.GetComponentsInChildren<Graphic>(true);
                Assert.That(graphics.All(g => !g.raycastTarget), Is.True);
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void CanvasGroupAttributes_Apply()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<view data-alpha='0.5' data-interactable='false' data-blocks-raycasts='false'><text>x</text></view>",
                        null, "canvasgroup"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var group = rootObject.GetComponentInChildren<CanvasGroup>(true);
                Assert.That(group, Is.Not.Null);
                Assert.That(group.alpha, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(group.interactable, Is.False);
                Assert.That(group.blocksRaycasts, Is.False);
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataShadow_AddsShadowComponent()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<text data-shadow='2,-3'>x</text>", null, "shadow"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var shadow = rootObject.GetComponentInChildren<Shadow>(true);
                Assert.That(shadow, Is.Not.Null);
                Assert.That(shadow.effectDistance, Is.EqualTo(new Vector2(2f, -3f)));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataOrientation_DeactivatesMismatchedElement()
        {
            UnityHtmlEnvironment.OrientationProvider = () => UnityHtmlEnvironment.OrientationClass.Landscape;
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<view><view data-orientation='portrait' id='p'><text>portrait</text></view>" +
                        "<view data-orientation='landscape' id='l'><text>landscape</text></view></view>",
                        null, "orientation"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);

                var texts = rootObject.GetComponentsInChildren<TMPro.TMP_Text>(true);
                var portrait = texts.First(t => t.text == "portrait");
                var landscape = texts.First(t => t.text == "landscape");
                Assert.That(portrait.gameObject.activeInHierarchy, Is.False);
                Assert.That(landscape.gameObject.activeInHierarchy, Is.True);
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataSafeArea_SetsYogaPadding()
        {
            UnityHtmlEnvironment.SafeAreaProvider = () => new Rect(0, 0, 100, 956);
            UnityHtmlEnvironment.ScreenSizeProvider = () => new Vector2(100, 1000);
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<view data-safe-area='top'><text>x</text></view>", null, "safearea"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);

                var element = rootObject.GetComponentsInChildren<ReactUnity.UGUI.Behaviours.ReactElement>(true)
                    .First(e => e.Component != null && e.Component.Data.ContainsKey("safe-area"));
                Assert.That(element.Component.Layout.PaddingTop.Value, Is.EqualTo(44f).Within(0.01f));
                Assert.That(element.Component.Layout.PaddingBottom.Value, Is.EqualTo(0f).Within(0.01f));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataNav_WiresExplicitNavigation()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<view><button id='a' data-nav-down='#b'><text>A</text></button>" +
                        "<button id='b' data-nav-up='#a'><text>B</text></button></view>",
                        null, "nav"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);

                var elements = rootObject.GetComponentsInChildren<ReactUnity.UGUI.Behaviours.ReactElement>(true);
                Button ButtonById(string id) => elements
                    .First(e => e.Component?.Id == id).GetComponentInChildren<Button>(true);
                var a = ButtonById("a");
                var b = ButtonById("b");
                Assert.That(a.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
                Assert.That(a.navigation.selectOnDown, Is.EqualTo(b));
                Assert.That(b.navigation.selectOnUp, Is.EqualTo(a));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataNavWrap_CyclesEnds()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<view data-nav-wrap='true'><button id='x'><text>X</text></button>" +
                        "<button id='y'><text>Y</text></button><button id='z'><text>Z</text></button></view>",
                        null, "wrap"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);

                var buttons = rootObject.GetComponentsInChildren<Button>(true)
                    .OrderBy(b => b.transform.GetSiblingIndex()).ToArray();
                Assert.That(buttons[0].navigation.selectOnUp, Is.EqualTo(buttons[2]));
                Assert.That(buttons[2].navigation.selectOnDown, Is.EqualTo(buttons[0]));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void DataFirstSelected_SelectsOnMount()
        {
            // EventSystem is not ExecuteAlways: register its lifecycle for this
            // EditMode fixture instead of relying on a previous test's scene.
            var eventObject = new GameObject("focus-test",typeof(EventSystem));
            var system=eventObject.GetComponent<EventSystem>();
            typeof(EventSystem).GetMethod("OnEnable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(system,null);
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<view><button><text>A</text></button><button data-first-selected='true'><text>B</text></button></view>",
                        null, "firstselected"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var selected = EventSystem.current.currentSelectedGameObject;
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected.GetComponentInChildren<TMPro.TMP_Text>()?.text, Is.EqualTo("B"));
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                typeof(EventSystem).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(system,null);
                Object.DestroyImmediate(eventObject);
            }
        }

        [Test]
        public void DataBg_MountsBackdropBehindPanel()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument(
                        "<view><view id='panel' data-bg='screen' data-bg-dim='0.6'><text>P</text></view></view>",
                        null, "backdrop"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                var images = rootObject.GetComponentsInChildren<Image>(true);
                Assert.That(images.Length, Is.GreaterThanOrEqualTo(2), "backdrop + dim layers expected");
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void UiBridge_Back_RaisesBackRequested()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            var backCount = 0;
            host.BackRequested += () => backCount++;
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<button onClick=\"Globals.ui.Back()\"><text>Back</text></button>", null, "uiback"));
                Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
                rootObject.GetComponentInChildren<Button>(true).onClick.Invoke();
                Assert.That(backCount, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void ParseError_ReportsLineAndExcerpt()
        {
            var rootObject = CreateRoot();
            using var host = new UnityHtmlHost();
            try
            {
                var result = host.Mount(rootObject.GetComponent<RectTransform>(),
                    new UnityHtmlDocument("<view>\n<button></view>", null, "badmarkup"));
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorMessage, Does.Contain("line"));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }
    }
}
