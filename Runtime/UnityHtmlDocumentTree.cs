using System;
using System.Collections.Generic;
using System.Xml;
using ReactUnity;
using ReactUnity.Helpers;
using Yoga;

namespace UnityHTML.Runtime
{
    // Preserve React component identity across HTML updates, including native
    // input focus, pointer capture, scroll positions and active animations.
    internal sealed class UnityHtmlDocumentTree
    {
        private readonly ReactContext _context;
        private readonly List<Node> _roots = new();
        private string _html;
        public UnityHtmlDocumentTree(ReactContext context) => _context = context;

        /// <summary>
        /// Fired right before a reconciled-out component is destroyed/pooled, so the
        /// host can stop animations whose target is leaving the tree. Without this a
        /// live tween keeps writing into a recycled element belonging to the new page.
        /// </summary>
        internal Action<IReactComponent> ComponentRemoved;

        public bool Update(string html)
        {
            if (_html == html) return false;
            Reconcile(_context.Host, _roots, Parse(html));
            _html = html;
            return true;
        }

        public bool UpdateRegion(string id, string html)
            => UpdateRegions(new Dictionary<string, string> { [id] = html }, out _);

        public bool UpdateRegions(IReadOnlyDictionary<string, string> regions, out bool changed)
        {
            changed = false;
            var pending = new List<(Node Node, XmlElement Xml, string Html)>();
            // Resolve and parse the whole batch before changing any native controls.
            foreach (var region in regions)
            {
                Node node = Find(_roots, region.Key);
                if (node?.Component is not IContainerComponent) return false;
                string html = region.Value ?? string.Empty;
                if (node.RegionHtml != html) pending.Add((node, Parse(html), html));
            }
            foreach (var entry in pending)
                for (Node parent = entry.Node.Parent; parent != null; parent = parent.Parent)
                    foreach (var other in pending)
                        if (other.Node == parent) return false;

            foreach (var entry in pending)
            {
                Node node = entry.Node;
                Reconcile((IContainerComponent)node.Component, node.Children, entry.Xml, node);
                Invalidate(node);
                node.RegionHtml = entry.Html;
            }
            changed = pending.Count > 0;
            if (changed) _html = null;
            return true;
        }

        public bool SetValue(string id, string value)
        {
            Node node = Find(_roots, id);
            if (node == null) return false;
            if (node.Component is ITextComponent text) text.SetText(value ?? string.Empty);
            else
            {
                node.Component.SetProperty("value", value ?? string.Empty);
                node.Attributes["value"] = value ?? string.Empty;
            }
            Invalidate(node);
            _html = null;
            return true;
        }

        private static XmlElement Parse(string html)
        {
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml("<root>" + html + "</root>");
            return document.DocumentElement;
        }

        private void Reconcile(IContainerComponent parent, List<Node> nodes, XmlNode xml, Node owner = null)
        {
            int index = 0;
            foreach (XmlNode child in xml.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element && child.NodeType != XmlNodeType.Text) continue;
                string key = child.Attributes?["id"]?.Value ?? child.Attributes?["data-key"]?.Value;
                string tag = child.NodeType == XmlNodeType.Text ? "_text" : child.Name;
                int match = index;
                while (match < nodes.Count && !nodes[match].Matches(tag, key)) match++;
                Node node;
                if (match < nodes.Count)
                {
                    node = nodes[match];
                    if (match != index)
                    {
                        nodes.RemoveAt(match);
                        node.Component.SetParent(parent, nodes[index].Component);
                        nodes.Insert(index, node);
                    }
                }
                else
                {
                    bool textNode = tag is "_text" or "text" or "icon" or "style" or "script" or "html";
                    IReactComponent component = tag == "_text"
                        ? _context.CreateText(tag, child.InnerText)
                        : _context.CreateComponent(tag, textNode ? child.InnerText : string.Empty);
                    node = new Node(component, tag, key, textNode, owner);
                    component.SetParent(parent, index < nodes.Count ? nodes[index].Component : null);
                    nodes.Insert(index, node);
                }
                UpdateNode(node, child);
                index++;
            }
            for (int i = nodes.Count - 1; i >= index; i--)
            { Remove(nodes[i].Component); nodes.RemoveAt(i); }
        }

        private void Remove(IReactComponent component)
        {
            ComponentRemoved?.Invoke(component);
            // Collect the subtree before Destroy() clears Children — every node
            // carries a rooted GCHandle that must be released (see ReleaseYoga).
            var subtree = new List<IReactComponent>();
            CollectSubtree(component, subtree);
#if UNITY_EDITOR
            if (!UnityEngine.Application.isPlaying && component is ReactUnity.UGUI.UGUIComponent ugui)
            {
                // React detaches scroll thumbs during Destroy. Keep native rects alive
                // until that finishes; a temporary pool bypasses edit-mode DestroySelf.
                var retired = new Stack<IPoolableComponent>();
                foreach (var element in ugui.GameObject.GetComponentsInChildren<ReactUnity.UGUI.Behaviours.ReactElement>(true))
                    if (element.Component is IPoolableComponent poolable)
                        poolable.PoolStack = retired;
                component.Destroy();
                while (retired.Count > 0)
                {
                    IPoolableComponent pooled = retired.Pop();
                    pooled.PoolStack = null;
                    if (pooled is ReactUnity.UGUI.UGUIComponent native && native.GameObject != null)
                        UnityEngine.Object.DestroyImmediate(native.GameObject);
                }
                foreach (IReactComponent dead in subtree) ReleaseYoga(dead);
                return;
            }
#endif
            component.Destroy();
            // Pooled components keep their YogaNode for reuse — releasing its
            // handle would break GetManaged on the next native callback.
            foreach (IReactComponent dead in subtree)
                if (dead is not IPoolableComponent poolable || poolable.PoolStack == null)
                    ReleaseYoga(dead);
        }

        private static void CollectSubtree(IReactComponent component, List<IReactComponent> flat)
        {
            if (component == null)
                return;
            flat.Add(component);
            if (component is IContainerComponent container && container.Children != null)
                foreach (IReactComponent child in container.Children)
                    CollectSubtree(child, flat);
        }

        // Upstream leak: every YogaNode registers a strong GCHandle on itself
        // (YGNodeHandle.SetContext) that only its own SafeHandle finalizer frees —
        // but the handle keeps the node alive, so the finalizer never runs.
        // Layout.Data then chains node -> component -> context -> JS engine, so a
        // single leaked node roots the whole mounted graph. Release explicitly.
        private static readonly System.Reflection.FieldInfo YogaHandleField = typeof(YogaNode)
            .GetField("_ygNode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.MethodInfo YogaReleaseManaged = YogaHandleField?.FieldType
            .GetMethod("ReleaseManaged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        internal static void ReleaseYoga(IReactComponent component)
        {
            YogaNode node = component?.Layout;
            if (node == null)
                return;
            object handle = YogaHandleField?.GetValue(node);
            if (handle != null)
                YogaReleaseManaged?.Invoke(handle, null);
        }

        /// <summary>
        /// Releases every rooted Yoga handle under a context — mounted elements,
        /// pooled leftovers and the host's own node. Call before disposing the
        /// context; the whole object graph becomes collectable afterwards.
        /// </summary>
        internal static void ReleaseContextNodes(ReactUnity.UGUI.UGUIContext context)
        {
            if (context?.Host is not ReactUnity.UGUI.UGUIComponent host)
                return;
            ReleaseYoga(host);
            var hostObject = host.GameObject;
            if (hostObject == null)
                return;
            foreach (var element in hostObject
                .GetComponentsInChildren<ReactUnity.UGUI.Behaviours.ReactElement>(true))
                if (element != null)
                    ReleaseYoga(element.Component);
        }

        private void UpdateNode(Node node, XmlNode xml)
        {
            string markup = xml.OuterXml;
            if (node.Markup == markup) return;
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (xml.Attributes != null)
                foreach (XmlAttribute attr in xml.Attributes)
                {
                    attributes[attr.Name] = attr.Value;
                    if (!node.Attributes.TryGetValue(attr.Name, out string old) || old != attr.Value)
                        SetAttribute(node.Component, attr.Name, attr.Value);
                }
            foreach (string name in node.Attributes.Keys)
                if (!attributes.ContainsKey(name)) SetAttribute(node.Component, name, null);
            node.Attributes = attributes;
            if (node.IsText && node.Component is ITextComponent text)
            {
                if (text.Content != xml.InnerText) text.SetText(xml.InnerText);
            }
            else if (!node.IsText && node.Component is IContainerComponent container)
                Reconcile(container, node.Children, xml, node);
            node.RegionHtml = null;
            node.Markup = markup;
        }

        private void SetAttribute(IReactComponent component, string name, string value)
        {
            if (name.StartsWith("on", StringComparison.Ordinal))
                component.SetEventListener(name, value == null ? null : Callback.From(value, _context, component));
            else if (name.StartsWith("data-", StringComparison.Ordinal)) component.SetData(name.Substring(5), value);
            else component.SetProperty(name, value);
        }

        private static Node Find(List<Node> nodes, string id)
        {
            foreach (Node node in nodes)
            {
                if (node.Component.Id == id) return node;
                Node child = Find(node.Children, id);
                if (child != null) return child;
            }
            return null;
        }
        private static void Invalidate(Node node)
        {
            for (; node != null; node = node.Parent)
            { node.Markup = null; node.RegionHtml = null; }
        }
        private sealed class Node
        {
            public readonly IReactComponent Component;
            public readonly string Tag, Key;
            public readonly bool IsText;
            public readonly Node Parent;
            public readonly List<Node> Children = new();
            public Dictionary<string, string> Attributes = new();
            public string Markup;
            public string RegionHtml;
            public Node(IReactComponent component, string tag, string key, bool isText, Node parent)
            { Component = component; Tag = tag; Key = key; IsText = isText; Parent = parent; }
            public bool Matches(string tag, string key) => !Component.Destroyed && Tag == tag && Key == key;
        }
    }
}
