using System.Xml;
using NUnit.Framework;
using UnityEngine;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlAttributeExpanderTests
    {
        [TearDown]
        public void TearDown() => UnityHtmlEnvironment.ResetOverrides();

        private static XmlElement Expand(string html)
        {
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml("<root>" + html + "</root>");
            UnityHtmlAttributeExpander.Expand(document.DocumentElement);
            return document.DocumentElement;
        }

        [Test]
        public void Backdrop_ExpandsToFullscreenImageStack()
        {
            XmlElement root = Expand("<backdrop src='bg' dim='0.5' close='true'/>");

            var wrapper = (XmlElement)root.FirstChild;
            Assert.That(wrapper.Name, Is.EqualTo("view"));
            Assert.That(wrapper.GetAttribute("data-backdrop"), Is.EqualTo("true"));
            Assert.That(wrapper.GetAttribute("style"), Does.Contain("position:absolute"));

            var image = (XmlElement)wrapper.ChildNodes[0];
            Assert.That(image.Name, Is.EqualTo("image"));
            Assert.That(image.GetAttribute("source"), Is.EqualTo("bg"));

            var shade = (XmlElement)wrapper.ChildNodes[1];
            Assert.That(shade.Name, Is.EqualTo("image"));
            Assert.That(shade.GetAttribute("style"), Does.Contain("rgba(0, 0, 0, 0.5)"));
            Assert.That(shade.GetAttribute("onClick"), Is.EqualTo("Globals.ui.Back()"));
        }

        [Test]
        public void Backdrop_WithoutDimOrClose_EmitsNoShade()
        {
            XmlElement root = Expand("<backdrop src='bg'/>");
            var wrapper = (XmlElement)root.FirstChild;
            Assert.That(wrapper.ChildNodes.Count, Is.EqualTo(1));
        }

        [Test]
        public void Backdrop_KeepsDeclaredChildrenAboveLayers()
        {
            XmlElement root = Expand("<backdrop dim='0.5'><text>Title</text></backdrop>");
            var wrapper = (XmlElement)root.FirstChild;
            Assert.That(wrapper.LastChild.Name, Is.EqualTo("text"));
        }

        [Test]
        public void DataBg_InjectsBackdropSiblingBeforeElement()
        {
            XmlElement root = Expand(
                "<view><view data-bg='screen' data-bg-dim='0.4' data-bg-close='true' id='panel'/></view>");

            var container = (XmlElement)root.FirstChild;
            Assert.That(container.ChildNodes.Count, Is.EqualTo(2));
            var backdrop = (XmlElement)container.ChildNodes[0];
            Assert.That(backdrop.GetAttribute("data-backdrop"), Is.EqualTo("true"));
            var panel = (XmlElement)container.ChildNodes[1];
            Assert.That(panel.GetAttribute("id"), Is.EqualTo("panel"));
            Assert.That(panel.HasAttribute("data-bg"), Is.False);
        }

        [Test]
        public void Panel_BecomesViewWithMotionRole()
        {
            XmlElement root = Expand("<panel role='dialog' bg='screen'><text>x</text></panel>");
            var view = (XmlElement)root.FirstChild;
            Assert.That(view.Name, Is.EqualTo("view"));
            Assert.That(view.GetAttribute("data-motion-role"), Is.EqualTo("dialog"));
            Assert.That(view.HasAttribute("role"), Is.False);
            // bg="screen" still expands — the backdrop sibling comes first.
            Assert.That(root.ChildNodes.Count, Is.EqualTo(2));
        }

        [Test]
        public void Spacer_BecomesSizedView()
        {
            XmlElement root = Expand("<view><spacer size='8x16'/><spacer size='flex'/></view>");
            var fixed_ = (XmlElement)root.FirstChild.ChildNodes[0];
            var flex = (XmlElement)root.FirstChild.ChildNodes[1];
            Assert.That(fixed_.Name, Is.EqualTo("view"));
            Assert.That(fixed_.GetAttribute("style"), Does.Contain("width:8px").And.Contain("height:16px"));
            Assert.That(flex.GetAttribute("style"), Does.Contain("flex-grow:1"));
        }

        [Test]
        public void Divider_BecomesLine()
        {
            XmlElement root = Expand("<divider inset='8' color='#fff'/>");
            var view = (XmlElement)root.FirstChild;
            Assert.That(view.Name, Is.EqualTo("view"));
            Assert.That(view.GetAttribute("style"), Does.Contain("height:1px").And.Contain("#fff"));
            Assert.That(view.GetAttribute("style"), Does.Contain("margin-left:8px"));
        }

        [Test]
        public void DataAnchor_EmitsAbsolutePositioning()
        {
            XmlElement root = Expand("<view data-anchor='bottom-right'/>");
            var view = (XmlElement)root.FirstChild;
            Assert.That(view.GetAttribute("style"), Does.Contain("position:absolute")
                .And.Contain("bottom:0").And.Contain("right:0"));
            Assert.That(view.HasAttribute("data-anchor"), Is.False);
        }

        [Test]
        public void DataStretch_And_DataCenter_FillAndCenter()
        {
            XmlElement root = Expand("<view><view data-stretch/><view data-center/></view>");
            var stretch = (XmlElement)root.FirstChild.ChildNodes[0];
            var center = (XmlElement)root.FirstChild.ChildNodes[1];
            Assert.That(stretch.GetAttribute("style"), Does.Contain("left:0").And.Contain("bottom:0"));
            Assert.That(center.GetAttribute("style"), Does.Contain("margin:auto"));
        }

        [Test]
        public void DataPlatform_RemovesNonMatchingSubtrees()
        {
            UnityHtmlEnvironment.PlatformProvider = () => UnityHtmlEnvironment.PlatformClass.Desktop;
            XmlElement root = Expand(
                "<view><view data-platform='mobile' id='mobileOnly'/><view data-platform='desktop|console' id='keep'/></view>");

            var container = (XmlElement)root.FirstChild;
            Assert.That(container.ChildNodes.Count, Is.EqualTo(1));
            Assert.That(((XmlElement)container.FirstChild).GetAttribute("id"), Is.EqualTo("keep"));
        }

        [Test]
        public void DataOrientation_IsLeftForEffectsPass()
        {
            XmlElement root = Expand("<view data-orientation='portrait'/>");
            var view = (XmlElement)root.FirstChild;
            Assert.That(view.GetAttribute("data-orientation"), Is.EqualTo("portrait"));
        }

        [Test]
        public void Expansion_IsDeterministicAcrossReparses()
        {
            const string html = "<panel bg='screen' data-bg-dim='0.5'><divider/><spacer size='flex'/></panel>";
            Assert.That(Expand(html).OuterXml, Is.EqualTo(Expand(html).OuterXml));
        }
    }
}
