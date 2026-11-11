using System.Text;
using CapsuleArena.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CapsuleArena.UI.HUD
{
    /// <summary>
    /// Pre-warmed 4-slot top-right Kill Feed pool that recycles its oldest entry in O(1) without calling
    /// <c>Object.Instantiate</c> or <c>Object.Destroy</c>, auto-fading entries after <c>4.0s</c>
    /// (Story 10-3; ADR-0007; TR-hud-003).
    /// </summary>
    public sealed class KillFeedPoolView : MonoBehaviour
    {
        /// <summary>Fixed pre-warmed row pool capacity (4).</summary>
        public const int PoolCapacity = 4;

        /// <summary>Duration in seconds before an active kill feed row fades out (4.0s).</summary>
        public const float EntryLifetimeSeconds = 4.0f;

        private sealed class KillFeedSlot
        {
            public GameObject Root { get; }
            public CanvasGroup Group { get; }
            public TMP_Text Text { get; }
            public float RemainingSeconds { get; set; }
            public bool IsActive { get; set; }

            public KillFeedSlot(GameObject root, CanvasGroup group, TMP_Text text)
            {
                Root = root;
                Group = group;
                Text = text;
            }
        }

        private readonly KillFeedSlot[] _slots = new KillFeedSlot[PoolCapacity];
        private readonly StringBuilder _sb = new StringBuilder(96);
        private int _writeCursor;
        private int _totalPushedCount;
        private bool _initialized;

        /// <summary>Gets the fixed number of pre-warmed child rows (always 4).</summary>
        public int PrewarmedRowCount => _initialized ? PoolCapacity : 0;

        /// <summary>Gets the number of currently visible kill feed rows (0..4).</summary>
        public int ActiveEntryCount
        {
            get
            {
                int active = 0;
                for (int i = 0; i < PoolCapacity; i++)
                {
                    if (_slots[i] != null && _slots[i].IsActive)
                    {
                        active++;
                    }
                }

                return active;
            }
        }

        /// <summary>Gets the total kill events pushed since initialization.</summary>
        public int TotalPushedCount => _totalPushedCount;

        private void Awake()
        {
            EnsurePoolPrewarmed();
        }

        private void Update()
        {
            TickTimers(Time.deltaTime);
        }

        /// <summary>Pre-warms all 4 <c>KillFeedEntryView</c> child rows once at initialization.</summary>
        public void EnsurePoolPrewarmed()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            for (int i = 0; i < PoolCapacity; i++)
            {
                _slots[i] = CreatePrewarmedRow(i);
            }
        }

        /// <summary>
        /// Pushes a new elimination notice into the 4-row ring buffer using a cached <see cref="StringBuilder"/>.
        /// </summary>
        public void PushKillNotification(string killerName, string victimName)
        {
            EnsurePoolPrewarmed();
            KillFeedSlot slot = _slots[_writeCursor];
            _writeCursor = (_writeCursor + 1) % PoolCapacity;
            _totalPushedCount++;

            _sb.Clear();
            _sb.Append(string.IsNullOrEmpty(killerName) ? "Arena" : killerName);
            _sb.Append("  >>  ");
            _sb.Append(string.IsNullOrEmpty(victimName) ? "Player" : victimName);

            slot.Text.SetText(_sb);
            slot.RemainingSeconds = EntryLifetimeSeconds;
            slot.IsActive = true;
            slot.Group.alpha = 1.0f;
            slot.Root.SetActive(true);
            slot.Root.transform.SetAsLastSibling();
        }

        /// <summary>Advances entry fade timers deterministically (callable in EditMode unit tests).</summary>
        public void TickTimers(float deltaTime)
        {
            if (!_initialized || deltaTime <= 0f)
            {
                return;
            }

            for (int i = 0; i < PoolCapacity; i++)
            {
                TickSlot(_slots[i], deltaTime);
            }
        }

        private static void TickSlot(KillFeedSlot slot, float deltaTime)
        {
            if (slot == null || !slot.IsActive)
            {
                return;
            }

            slot.RemainingSeconds -= deltaTime;
            if (slot.RemainingSeconds <= 0f)
            {
                slot.RemainingSeconds = 0f;
                slot.IsActive = false;
                slot.Group.alpha = 0f;
                slot.Root.SetActive(false);
            }
            else if (slot.RemainingSeconds < 0.6f)
            {
                slot.Group.alpha = Mathf.Clamp01(slot.RemainingSeconds / 0.6f);
            }
        }

        private KillFeedSlot CreatePrewarmedRow(int index)
        {
            GameObject rowGo = new GameObject($"KillFeedRow_{index}", typeof(RectTransform));
            rowGo.transform.SetParent(transform, worldPositionStays: false);
            rowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 30f);

            Image bg = rowGo.AddComponent<Image>();
            bg.sprite = UiThemeAndFontProvider.GetWhiteSprite();
            bg.color = UiThemeAndFontProvider.PanelDark;
            bg.raycastTarget = false;

            CanvasGroup cg = rowGo.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            TextMeshProUGUI tmp = CreateRowLabel(rowGo.transform);
            rowGo.SetActive(false);
            return new KillFeedSlot(rowGo, cg, tmp);
        }

        private static TextMeshProUGUI CreateRowLabel(Transform parent)
        {
            GameObject txtGo = new GameObject("Txt_KillEntry", typeof(RectTransform));
            txtGo.transform.SetParent(parent, worldPositionStays: false);
            RectTransform trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 2f);
            trt.offsetMax = new Vector2(-10f, -2f);

            TextMeshProUGUI tmp = txtGo.AddComponent<TextMeshProUGUI>();
            UiThemeAndFontProvider.EnsureFontAssigned(tmp);
            tmp.fontSize = 14f;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.color = UiThemeAndFontProvider.PublicOffWhite;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
