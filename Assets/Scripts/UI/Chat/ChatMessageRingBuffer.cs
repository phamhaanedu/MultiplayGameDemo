using System.Collections.Generic;
using System.Text;
using CapsuleArena.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CapsuleArena.UI.Chat
{
    /// <summary>
    /// Pre-warmed 50-row circular UI pool for chat message history.
    /// Pre-allocates all <c>50</c> <c>ChatMessageRow</c> GameObjects at startup (<c>0</c> active initially)
    /// and recycles the oldest row in <c>O(1)</c> time via <c>transform.SetAsLastSibling()</c> on message <c>51+</c>
    /// with <c>richText = false</c> and <c>0</c> runtime <c>Instantiate</c>/<c>Destroy</c> calls
    /// (Story 12-3; ADR-0007; TR-chat-003).
    /// </summary>
    public sealed class ChatMessageRingBuffer : MonoBehaviour
    {
        /// <summary>Canonical number of pre-warmed chat row UI elements (50).</summary>
        public const int CanonicalCapacity = 50;

        [SerializeField] private RectTransform _contentContainer;
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private int _poolCapacity = CanonicalCapacity;

        private readonly List<TMP_Text> _pooledRowLabels = new List<TMP_Text>(CanonicalCapacity);
        private readonly List<GameObject> _pooledRowObjects = new List<GameObject>(CanonicalCapacity);
        private readonly StringBuilder _formatBuffer = new StringBuilder(192);

        private bool _isPrewarmed;
        private int _activeRowCount;
        private int _totalMessagesPushed;
        private int _runtimeInstantiateCount;
        private int _runtimeDestroyCount;

        /// <summary>Gets the configured ring buffer capacity (canonical: 50).</summary>
        public int Capacity => _pooledRowObjects.Count > 0 ? _pooledRowObjects.Count : Mathf.Max(1, _poolCapacity);

        /// <summary>Gets the number of currently active (visible) message rows in <c>[0, Capacity]</c>.</summary>
        public int ActiveRowCount => _activeRowCount;

        /// <summary>Gets the cumulative count of messages pushed since initialization.</summary>
        public int TotalMessagesPushed => _totalMessagesPushed;

        /// <summary>Gets runtime Instantiate calls after Prewarm (guaranteed 0).</summary>
        public int RuntimeInstantiateCallsAfterPrewarm => _runtimeInstantiateCount;

        /// <summary>Gets runtime Destroy calls after Prewarm (guaranteed 0).</summary>
        public int RuntimeDestroyCalls => _runtimeDestroyCount;

        /// <summary>Gets the underlying content container holding the 50 pooled rows.</summary>
        public RectTransform ContentContainer => _contentContainer;

        private void Awake()
        {
            EnsurePrewarmed(_poolCapacity);
        }

        /// <summary>
        /// Pre-warms <paramref name="capacity"/> inactive <c>ChatMessageRow</c> GameObjects under the content container.
        /// Safe and idempotent if called multiple times.
        /// </summary>
        public void EnsurePrewarmed(int capacity = CanonicalCapacity)
        {
            if (_isPrewarmed && _pooledRowObjects.Count > 0)
            {
                return;
            }

            _poolCapacity = Mathf.Max(1, capacity);
            EnsureContentContainerAndLayout();

            _pooledRowObjects.Clear();
            _pooledRowLabels.Clear();
            int existingCount = _contentContainer.childCount;
            for (int i = 0; i < _poolCapacity; i++)
            {
                if (i < existingCount)
                {
                    BindExistingRow(_contentContainer.GetChild(i).gameObject);
                }
                else
                {
                    CreatePrewarmedRow(i);
                }
            }

            _activeRowCount = 0;
            _totalMessagesPushed = 0;
            _runtimeInstantiateCount = 0;
            _runtimeDestroyCount = 0;
            _isPrewarmed = true;
        }

        /// <summary>
        /// Appends a formatted chat line (<c>prefix + body</c>) in <paramref name="lineColor"/> using <c>O(1)</c>
        /// ring-buffer recycling (<c>index = (totalCount - 1) % Capacity</c>) with zero heap object instantiations.
        /// </summary>
        public TMP_Text PushFormattedMessage(string prefix, string body, Color lineColor)
        {
            EnsurePrewarmed(_poolCapacity);

            _totalMessagesPushed++;
            int slotIndex = (_totalMessagesPushed - 1) % _pooledRowObjects.Count;
            GameObject rowGo = _pooledRowObjects[slotIndex];
            TMP_Text label = _pooledRowLabels[slotIndex];

            FormatLineBuffer(prefix, body);
            label.richText = false;
            label.color = lineColor;
            label.SetText(_formatBuffer);

            if (!rowGo.activeSelf)
            {
                rowGo.SetActive(true);
                _activeRowCount = Mathf.Min(_pooledRowObjects.Count, _activeRowCount + 1);
            }

            rowGo.transform.SetAsLastSibling();
            if (_scrollRect != null)
            {
                _scrollRect.verticalNormalizedPosition = 0f;
            }

            return label;
        }

        /// <summary>Returns the text currently displayed on the <paramref name="nthVisibleFromTop"/> active child in hierarchy order.</summary>
        public string GetVisibleLineAtOrder(int nthVisibleFromTop)
        {
            if (_contentContainer == null)
            {
                return string.Empty;
            }

            int seen = 0;
            int childCount = _contentContainer.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = _contentContainer.GetChild(i);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                if (seen == nthVisibleFromTop)
                {
                    return child.TryGetComponent(out TMP_Text txt) ? txt.text : string.Empty;
                }

                seen++;
            }

            return string.Empty;
        }

        /// <summary>Returns the text color currently displayed on the <paramref name="nthVisibleFromTop"/> active child in hierarchy order.</summary>
        public Color GetVisibleColorAtOrder(int nthVisibleFromTop)
        {
            if (_contentContainer == null)
            {
                return Color.clear;
            }

            int seen = 0;
            int childCount = _contentContainer.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = _contentContainer.GetChild(i);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                if (seen == nthVisibleFromTop)
                {
                    return child.TryGetComponent(out TMP_Text txt) ? txt.color : Color.clear;
                }

                seen++;
            }

            return Color.clear;
        }

        /// <summary>Binds an optional <see cref="ScrollRect"/> for auto-scroll-to-bottom on new messages.</summary>
        public void BindScrollRect(ScrollRect scrollRect)
        {
            _scrollRect = scrollRect;
        }

        private void EnsureContentContainerAndLayout()
        {
            if (_contentContainer == null && !TryGetComponent(out _contentContainer))
            {
                _contentContainer = gameObject.AddComponent<RectTransform>();
            }

            if (!_contentContainer.TryGetComponent(out VerticalLayoutGroup vlg))
            {
                vlg = _contentContainer.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.LowerLeft;
                vlg.spacing = 2f;
                vlg.padding = new RectOffset(6, 6, 4, 4);
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
            }
        }

        private void BindExistingRow(GameObject rowGo)
        {
            if (!rowGo.TryGetComponent(out TMP_Text tmp))
            {
                TextMeshProUGUI createdTmp = rowGo.AddComponent<TextMeshProUGUI>();
                UiThemeAndFontProvider.EnsureFontAssigned(createdTmp);
                createdTmp.fontSize = 13.5f;
                createdTmp.alignment = TextAlignmentOptions.MidlineLeft;
                createdTmp.textWrappingMode = TextWrappingModes.Normal;
                createdTmp.overflowMode = TextOverflowModes.Ellipsis;
                createdTmp.richText = false;
                createdTmp.raycastTarget = false;
                createdTmp.color = UiThemeAndFontProvider.PublicOffWhite;
                tmp = createdTmp;
            }
            else
            {
                tmp.text = string.Empty;
                tmp.richText = false;
            }

            rowGo.SetActive(false);
            _pooledRowObjects.Add(rowGo);
            _pooledRowLabels.Add(tmp);
        }

        private void CreatePrewarmedRow(int index)
        {
            GameObject rowGo = new GameObject($"ChatMessageRow_{index:00}", typeof(RectTransform));
            rowGo.transform.SetParent(_contentContainer, worldPositionStays: false);

            LayoutElement le = rowGo.AddComponent<LayoutElement>();
            le.minHeight = 20f;
            le.preferredHeight = 22f;

            TextMeshProUGUI tmp = rowGo.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.fontSize = 13.5f;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.richText = false;
            tmp.raycastTarget = false;
            tmp.color = UiThemeAndFontProvider.PublicOffWhite;

            rowGo.SetActive(false);
            _pooledRowObjects.Add(rowGo);
            _pooledRowLabels.Add(tmp);
        }

        private void FormatLineBuffer(string prefix, string body)
        {
            _formatBuffer.Clear();
            if (!string.IsNullOrEmpty(prefix))
            {
                _formatBuffer.Append(prefix);
            }

            if (!string.IsNullOrEmpty(body))
            {
                _formatBuffer.Append(body);
            }
        }
    }
}
