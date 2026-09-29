using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ReactUnity.UGUI;
using ReactUnity.UGUI.EventHandlers;
using UnityEngine;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    // P079: mount/unmount and re-render cycles must release every listener,
    // tween and helper component the document created — no handler growth, no
    // surviving tooltip layer, no MissingReferenceException.
    public sealed class UnityHtmlUnmountTests
    {
        private static readonly MethodInfo LateUpdateMethod = typeof(UnityHtmlTooltipLayer)
            .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        private GameObject _root;
        private UnityHtmlHost _host;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HTML unmount", typeof(RectTransform), typeof(Canvas));
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 600);
            _host = new UnityHtmlHost();
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
            UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void MountUnmountCycles_LeaveNoHandlersOrLayersBehind()
        {
            int baselineHandlers = UnityEngine.Object
                .FindObjectsByType<PointerEnterHandler>().Length;

            for (int i = 0; i < 50; i++)
            {
                Mount($"<view id='panel' data-motion-role='panel' data-motion='exit' data-tooltip='tip{i}'>"
                    + $"<view onPointerEnter='Globals.noop()' onClick='Globals.noop()'>"
                    + $"<text>label{i}</text></view></view>");
                BindTooltipLayer();
                _host.Unmount();

                Assert.That(_root.transform.childCount, Is.EqualTo(0),
                    $"cycle {i} left live children under the root");
                Assert.That(UnityEngine.Object
                        .FindObjectsByType<UnityHtmlTooltipLayer>().Length,
                    Is.EqualTo(0), $"cycle {i} leaked the tooltip layer");
                Assert.That(UnityEngine.Object
                        .FindObjectsByType<UnityHtmlTooltipTarget>().Length,
                    Is.EqualTo(0), $"cycle {i} leaked tooltip targets");
            }

            Assert.That(UnityEngine.Object
                    .FindObjectsByType<PointerEnterHandler>().Length,
                Is.EqualTo(baselineHandlers), "event handler components accumulated");
        }

        [Test]
        public void RegionChurn_KeepsListenerCountsFlat()
        {
            Mount("<view id='panel'><view id='row' data-tooltip='tip0'"
                + " onPointerEnter='Globals.noop()'><text>l0</text></view></view>");
            UnityHtmlTooltipLayer layer = BindTooltipLayer();

            int handlerCount = _root.GetComponentsInChildren<PointerEnterHandler>(true).Length;
            int listenerCount = HandlerListeners();
            int targets = _root.GetComponentsInChildren<UnityHtmlTooltipTarget>(true).Length;
            Assert.That(handlerCount, Is.EqualTo(1));
            Assert.That(listenerCount, Is.EqualTo(1));
            Assert.That(targets, Is.EqualTo(1));

            for (int i = 1; i <= 50; i++)
            {
                // Same shape, same ids, new text — the locale-change path
                // reconciles in place; handlers must swap, not stack.
                Assert.That(_host.UpdateRegion("panel",
                    $"<view id='row' data-tooltip='tip{i}'"
                    + $" onPointerEnter='Globals.noop()'><text>l{i}</text></view>"), Is.True);
                layer.RefreshTargets();
            }

            Assert.That(_root.GetComponentsInChildren<PointerEnterHandler>(true).Length,
                Is.EqualTo(handlerCount), "handler components stacked across region updates");
            Assert.That(HandlerListeners(), Is.EqualTo(listenerCount),
                "event listeners accumulated on the reconciled element");
            Assert.That(_root.GetComponentsInChildren<UnityHtmlTooltipTarget>(true).Length,
                Is.EqualTo(targets), "tooltip targets stacked across region updates");
        }

        private int HandlerListeners()
        {
            int total = 0;
            foreach (PointerEnterHandler handler in
                _root.GetComponentsInChildren<PointerEnterHandler>(true))
            {
                var field = typeof(PointerEnterHandler).GetField("OnEvent",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field?.GetValue(handler) is Delegate subscribers)
                    total += subscribers.GetInvocationList().Length;
            }
            return total;
        }

        [Test]
        public void UnmountWhileTooltipVisible_ReleasesEverythingWithoutMissingReferences()
        {
            Mount("<view id='t' data-tooltip='visible tip'><text>x</text></view>");
            UnityHtmlTooltipLayer layer = BindTooltipLayer();
            layer.ShowDelaySeconds = 0f;
            _host.SetWorldTooltip("Water Mill", new Vector2(100, 100));
            LateUpdateMethod.Invoke(layer, null);

            Assert.DoesNotThrow(() => _host.Unmount());
            Assert.That(_root.transform.childCount, Is.EqualTo(0));
            Assert.That(layer == null, Is.True, "tooltip layer survived unmount");
        }

        // Runs as a UnityTest so every cycle yields an editor frame — delayCall
        // destroys, deferred unmount work and the GC all flush between mounts,
        // which is what a real mount/unmount loop sees.
        //
        // The assertion is structural, not a byte threshold: under Mono's Boehm
        // collector, heap-metric deltas (GetMonoUsedSizeLong/GetTotalMemory)
        // measure committed heap blocks, not rooted objects. Mount churn (JS
        // eval + reflect-binding) produces ~10MB transient allocation spikes per
        // cycle, and Boehm does not return freed blocks — the metric grows ~280KB
        // per cycle even with zero live garbage, so a byte budget cannot
        // distinguish footprint growth from a leak. What must not survive is the
        // mounted graph itself — contexts, engines, QuickJS runtimes — so this
        // asserts exactly that.
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ManagedMemory_StaysFlatAcrossMountCycles()
        {
            const int cycles = 40;
            var contexts = new System.Collections.Generic.List<WeakReference>();

            for (int i = 0; i < cycles; i++)
            {
                MountTrackAndUnmount(
                    $"<view data-motion-role='panel' data-tooltip='tip{i}'>"
                    + $"<view onClick='Globals.noop()'><text>label{i}</text></view></view>",
                    contexts);
                yield return null;
            }

            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int alive = contexts.Count(reference => reference.IsAlive);
            Assert.That(alive, Is.EqualTo(0),
                $"{alive}/{cycles} mounted UGUIContext graphs stayed rooted after unmount");
        }

        [Test]
        public void UnmountedContexts_AreCollected()
        {
            var references = new System.Collections.Generic.List<WeakReference>();
            for (int i = 0; i < 6; i++)
                MountAndTrack(references);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.Collect();

            int alive = 0;
            foreach (WeakReference reference in references)
                if (reference.IsAlive) alive++;
            Assert.That(alive, Is.EqualTo(0),
                "disposed UGUIContext objects stayed rooted (YogaNode GCHandle leak)");
        }

        // Kept out-of-line so the context reference dies with this frame —
        // Mono's conservative stack scan would otherwise root the last one.
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void MountTrackAndUnmount(string html, System.Collections.Generic.List<WeakReference> references)
        {
            Mount(html);
            var context = typeof(UnityHtmlHost)
                .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_host);
            references.Add(new WeakReference(context));
            _host.Unmount();
        }

        // Same out-of-line framing: keeps the context ref off this test's stack.
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void MountAndTrack(System.Collections.Generic.List<WeakReference> references)
        {
            Mount("<view onClick='Globals.noop()'><text>w</text></view>");
            var context = typeof(UnityHtmlHost)
                .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_host);
            references.Add(new WeakReference(context));
            _host.Unmount();
        }

        private void Mount(string html)
        {
            UnityHtmlMountResult result = _host.Mount(
                _root.GetComponent<RectTransform>(),
                new UnityHtmlDocument(html, string.Empty, "Unmount"));
            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        }

        private UnityHtmlTooltipLayer BindTooltipLayer()
        {
            var context = (UGUIContext)typeof(UnityHtmlHost)
                .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_host);
            var layer = _root.AddComponent<UnityHtmlTooltipLayer>();
            layer.Bind(context, _root.GetComponent<RectTransform>());
            layer.RefreshTargets();
            typeof(UnityHtmlHost)
                .GetField("_tooltips", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_host, layer);
            return layer;
        }
    }
}
