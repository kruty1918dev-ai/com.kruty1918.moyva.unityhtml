using TMPro;
using UnityEngine;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Marker on the select element's GameObject bridging the cloned dropdown
    /// template back to its owning <see cref="UnityHtmlSelectComponent"/>.
    /// </summary>
    internal sealed class UnityHtmlSelectHandle : MonoBehaviour
    {
        internal UnityHtmlSelectComponent Owner;
    }

    /// <summary>
    /// Search row inside a <c>&lt;select searchable&gt;</c> dropdown template.
    /// TMP_Dropdown clones the template on every open, so this component
    /// resolves its owner per clone through the serialized
    /// <see cref="TMP_Dropdown"/> reference, and restores the full option list
    /// when the popup closes.
    /// </summary>
    internal sealed class UnityHtmlSelectSearch : MonoBehaviour
    {
        [SerializeField] internal TMP_Dropdown Dropdown;
        private TMP_InputField _input;

        private void Awake() => _input = GetComponent<TMP_InputField>();

        private void OnEnable()
        {
            if (_input == null)
                _input = GetComponent<TMP_InputField>();
            if (_input != null)
                _input.onValueChanged.AddListener(OnFilterChanged);
        }

        private void OnDisable()
        {
            if (_input != null)
                _input.onValueChanged.RemoveListener(OnFilterChanged);
            Owner()?.RestoreOptions();
        }

        private void OnFilterChanged(string query) => Owner()?.ApplyFilter(query);

        private UnityHtmlSelectComponent Owner()
            => Dropdown != null
                ? Dropdown.GetComponent<UnityHtmlSelectHandle>()?.Owner
                : null;
    }
}
