using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityHTML.Runtime;
namespace UnityHTML.Tests
{
    public sealed class UnityHtmlSwitchMotionTests
    {
        [UnityTest] public IEnumerator RapidClicksResizeAndReducedMotionSettleWithoutDuplicateEvents()
        {
            var root=new GameObject("switch-motion",typeof(RectTransform),typeof(Canvas));
            root.GetComponent<RectTransform>().sizeDelta=new Vector2(800,600);
            using var host=new UnityHtmlHost();
            Assert.IsTrue(host.Mount(root.GetComponent<RectTransform>(),new UnityHtmlDocument("<switch id='s' value='false'/>","switch { width:124px;height:68px; }","switch")).Succeeded);
            yield return null;
            var toggle=root.GetComponentInChildren<Toggle>();int events=0;toggle.onValueChanged.AddListener(_=>events++);
            for(int i=0;i<15;i++){toggle.OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});yield return null;}
            Assert.AreEqual(15,events);Assert.IsTrue(toggle.isOn);
            host.Motion.ReducedMotion=true;
            yield return null;yield return null;
            var knob=(RectTransform)toggle.transform.Find("Knob");var rect=toggle.GetComponent<RectTransform>();
            Assert.That(knob.rect.width,Is.EqualTo(knob.rect.height).Within(.1));
            Assert.That(knob.anchoredPosition.x,Is.EqualTo(rect.rect.width-knob.rect.width-6).Within(.5));
            host.Unmount();yield return null;
            Assert.AreEqual(0,root.transform.childCount);
            Object.Destroy(root);
        }
    }
}
