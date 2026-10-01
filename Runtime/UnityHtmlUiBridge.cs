using System;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Built-in <c>Globals.ui</c> object exposed to markup scripts. Surfaces
    /// package-level UI actions that don't belong to a project's bridge —
    /// starting with <c>ui.Back()</c> used by backdrops, headers and the
    /// navigation stack.
    /// </summary>
    public sealed class UnityHtmlUiBridge
    {
        private readonly Action _back;

        internal UnityHtmlUiBridge(Action back) => _back = back;

        public void Back() => _back?.Invoke();
    }
}
