using NUnit.Framework;
using ReactUnity;
using ReactUnity.Helpers;
using ReactUnity.Scheduling;
using ReactUnity.Scripting;
using ReactUnity.Styling;
using ReactUnity.Styling.Rules;
using ReactUnity.UGUI;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UnityHTML.Tests
{
    public sealed class ReactUnityCompatibilityTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void UguiHtmlCssAndControlsRenderOnUnity6000()
        {
            var rootObject = new GameObject("UnityHTML Compatibility Root", typeof(RectTransform), typeof(Canvas));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 800f);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 600f);

            UGUIContext context = null;
            try
            {
                var source = ScriptSource.Text(
                    "<view className='spike'><text>UnityHTML ready</text><button><text>Click</text></button><img /></view>",
                    ScriptSourceLanguage.Html);
                context = new UGUIContext(new UGUIContext.Options
                {
                    HostElement = root,
                    Globals = new GlobalRecord(),
                    Source = source,
                    Timer = UnscaledTimer.Instance,
                    MediaProvider = DefaultMediaProvider.CreateMediaProvider("runtime", "ugui", false),
                    EngineType = ResolveTestEngineType(),
                    Pooling = ReactContext.PoolingType.None,
                    UnknownPropertyHandling = ReactContext.UnknownPropertyHandling.Exception
                });
                context.InsertStyle(".spike { display: flex; flex-direction: row; width: 400px; height: 100px; gap: 8px; } button:hover { opacity: 0.8; }");
                context.Start();
                context.CalculateLayoutRecursively();

                var text = rootObject.GetComponentInChildren<TextMeshProUGUI>(true);
                var button = rootObject.GetComponentInChildren<Button>(true);
                var image = rootObject.GetComponentInChildren<Image>(true);
                Assert.That(text, Is.Not.Null);
                Assert.That(text.text, Does.Contain("UnityHTML ready"));
                Assert.That(button, Is.Not.Null);
                Assert.That(image, Is.Not.Null);

                var clicked = false;
                button.onClick.AddListener(() => clicked = true);
                button.onClick.Invoke();
                Assert.That(clicked, Is.True);

                var renderedRoot = root.GetChild(2) as RectTransform;
                Assert.That(renderedRoot, Is.Not.Null);
                Assert.That(renderedRoot.rect.width, Is.GreaterThan(0f));
                Assert.That(renderedRoot.rect.height, Is.GreaterThan(0f));
            }
            finally
            {
                DisposeContextForEditMode(context);
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        private static void DisposeContextForEditMode(UGUIContext context)
        {
            if (context == null)
                return;

            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= context.Dispose;
            context.Dispatcher?.Dispose();
            context.Globals?.Dispose();
            foreach (var disposable in context.Disposables)
                disposable?.Invoke();
            context.Disposables.Clear();
            context.Script?.Dispose();
        }

        private static JavascriptEngineType ResolveTestEngineType()
        {
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
#pragma warning disable CS0612
            return JavascriptEngineType.Jint;
#pragma warning restore CS0612
#else
            return JavascriptEngineType.QuickJS;
#endif
        }
    }
}
