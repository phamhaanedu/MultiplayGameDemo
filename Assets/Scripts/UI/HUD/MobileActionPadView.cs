using System.Collections.Generic;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CapsuleArena.UI.HUD
{
    /// <summary>
    /// Renders and coordinates the bottom-right 7-button radial Mobile Action Pad
    /// (<c>BTN_SlashVertical 136x136px</c> + 6 non-overlapping satellite buttons <c>&gt;= 84x84px</c>) and bottom-left
    /// <c>240x240px</c> <see cref="VirtualJoystick"/>, gated via <see cref="CanvasGroup"/> according to
    /// <see cref="MobilePadVisibilityMode"/> (<c>Auto / ForceOn / ForceOff</c>), focus flags, and alive state
    /// (Stories 10-2, 11-3; ADR-0002, ADR-0007; TR-hud-002, TR-rebind-003).
    /// </summary>
    public sealed class MobileActionPadView : MonoBehaviour
    {
        /// <summary>Minimum WCAG-compliant touch target dimension at 1080p (84px).</summary>
        public const float MinButtonSizePx = 84.0f;

        /// <summary>Primary anchor button dimension for Vertical Slash (136px).</summary>
        public const float PrimarySlashButtonSizePx = 136.0f;

        [Header("Hierarchy References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private VirtualJoystick _virtualJoystick;
        [SerializeField] private MobileActionButton[] _actionButtons;

        [Header("Runtime Bridges")]
        [SerializeField] private MobileInputBridge _mobileBridge;

        private IKeybindingService _keybindingService;
        private IInputFocusService _focusService;
        private PlayerHealth _localHealth;
        private PlayerCombatController _localCombat;
        private PlayerMobilityController _localMobility;
        private bool _isLocalPlayerAlive = true;
        private bool? _simulateTouchDeviceOverride;

        /// <summary>Gets the read-only list of all 7 configured mobile action buttons.</summary>
        public IReadOnlyList<MobileActionButton> ActionButtons => _actionButtons;

        /// <summary>Gets the virtual movement joystick.</summary>
        public VirtualJoystick Joystick => _virtualJoystick;

        /// <summary>Gets the underlying <see cref="CanvasGroup"/> controlling visibility and raycast blocking.</summary>
        public CanvasGroup PadCanvasGroup => _canvasGroup;

        /// <summary>Gets a value indicating whether the pad is currently visible and interactive.</summary>
        public bool IsPadVisible => _canvasGroup != null && _canvasGroup.alpha > 0.5f && _canvasGroup.interactable;

        private void Awake()
        {
            EnsurePadHierarchyBuilt();
        }

        private void OnDestroy()
        {
            UnbindAll();
        }

        /// <summary>
        /// Injects input, keybinding, focus, and local player components and refreshes badges &amp; visibility.
        /// </summary>
        public void Initialize(
            MobileInputBridge mobileBridge,
            IKeybindingService keybindingService,
            IInputFocusService focusService = null,
            bool? simulateTouchDeviceOverride = null)
        {
            UnbindServiceEvents();
            _mobileBridge = mobileBridge;
            _keybindingService = keybindingService;
            _focusService = focusService;
            _simulateTouchDeviceOverride = simulateTouchDeviceOverride;

            EnsurePadHierarchyBuilt();
            BindBridgeToChildren();

            if (_keybindingService != null)
            {
                _keybindingService.OnBindingsChanged += HandleBindingsOrModeChanged;
            }

            if (_focusService != null)
            {
                _focusService.OnFocusStateChanged += HandleFocusStateChanged;
            }

            HandleBindingsOrModeChanged();
        }

        /// <summary>Binds the local player's health, combat, and mobility controllers for cooldowns and Ghost hiding.</summary>
        public void BindLocalPlayer(
            PlayerHealth health,
            PlayerCombatController combat = null,
            PlayerMobilityController mobility = null)
        {
            UnbindPlayerEvents();
            _localHealth = health;
            _localCombat = combat;
            _localMobility = mobility;
            _isLocalPlayerAlive = _localHealth == null || !_localHealth.IsDead;

            if (_localHealth != null)
            {
                _localHealth.OnPlayerDied += HandleLocalPlayerDied;
                _localHealth.OnHealthChanged += HandleLocalHealthChanged;
            }

            if (_localCombat != null)
            {
                _localCombat.OnCooldownUpdated += ForwardCooldownToButtons;
            }

            if (_localMobility != null)
            {
                _localMobility.OnCooldownUpdated += ForwardCooldownToButtons;
            }

            RefreshPadVisibility();
        }

        /// <summary>Refreshes <see cref="CanvasGroup"/> visibility without destroying any UI GameObjects.</summary>
        public void RefreshPadVisibility()
        {
            EnsurePadHierarchyBuilt();
            MobilePadVisibilityMode mode = ResolveCurrentVisibilityMode();
            bool isTouch = ResolveIsTouchDevice();
            bool focusAllows = _focusService == null || _focusService.IsGameplayInputAllowed;
            bool show = EvaluateVisibility(mode, isTouch, _isLocalPlayerAlive);

            ApplyCanvasGroupVisibility(show, focusAllows);
            if (!show || !focusAllows)
            {
                ReleaseAllHeldControls();
            }
        }

        /// <summary>
        /// Pure visibility rule for <see cref="MobilePadVisibilityMode"/> (Story 11-3 AC-2).
        /// </summary>
        public static bool EvaluateVisibility(
            MobilePadVisibilityMode mode,
            bool isMobileOrTouch,
            bool isLocalPlayerAlive)
        {
            if (!isLocalPlayerAlive)
            {
                return false;
            }

            return mode switch
            {
                MobilePadVisibilityMode.ForceOn => true,
                MobilePadVisibilityMode.ForceOff => false,
                _ => isMobileOrTouch
            };
        }

        /// <summary>
        /// Overload accepting raw integer mode (<c>0=Auto, 1=ForceOn, 2=ForceOff</c>) for unit tests (Story 11-3).
        /// </summary>
        public static bool ShouldShowMobilePad(int mode, bool isTouchDevice, bool isAlive)
        {
            MobilePadVisibilityMode parsed = System.Enum.IsDefined(typeof(MobilePadVisibilityMode), mode)
                ? (MobilePadVisibilityMode)mode
                : MobilePadVisibilityMode.Auto;
            return EvaluateVisibility(parsed, isTouchDevice, isAlive);
        }

        /// <summary>Forwards a skill cooldown event to all matching buttons on the pad.</summary>
        public void ForwardCooldownToButtons(CombatSkillId skillId, float remainingSec, float totalSec)
        {
            if (_actionButtons == null)
            {
                return;
            }

            for (int i = 0; i < _actionButtons.Length; i++)
            {
                if (_actionButtons[i] != null)
                {
                    _actionButtons[i].NotifySkillCooldown(skillId, remainingSec, totalSec);
                }
            }
        }

        private MobilePadVisibilityMode ResolveCurrentVisibilityMode()
        {
            return _keybindingService != null ? _keybindingService.MobilePadMode : MobilePadVisibilityMode.Auto;
        }

        private bool ResolveIsTouchDevice()
        {
            if (_simulateTouchDeviceOverride.HasValue)
            {
                return _simulateTouchDeviceOverride.Value;
            }

            return Application.isMobilePlatform || Touchscreen.current != null;
        }

        private void ApplyCanvasGroupVisibility(bool show, bool focusAllows)
        {
            if (_canvasGroup == null)
            {
                return;
            }

            _canvasGroup.alpha = show ? 1.0f : 0.0f;
            bool interactive = show && focusAllows;
            _canvasGroup.interactable = interactive;
            _canvasGroup.blocksRaycasts = interactive;
        }

        private void HandleBindingsOrModeChanged()
        {
            if (_actionButtons != null && _keybindingService != null)
            {
                for (int i = 0; i < _actionButtons.Length; i++)
                {
                    MobileActionButton btn = _actionButtons[i];
                    if (btn != null)
                    {
                        btn.RefreshBadgeFromBindingPath(_keybindingService.GetBindingPath(btn.MappedGameAction));
                    }
                }
            }

            RefreshPadVisibility();
        }

        private void HandleFocusStateChanged(bool isGameplayAllowed)
        {
            RefreshPadVisibility();
        }

        private void HandleLocalPlayerDied(PlayerRef victim, PlayerRef killer)
        {
            _isLocalPlayerAlive = false;
            RefreshPadVisibility();
        }

        private void HandleLocalHealthChanged(int currentHp, int maxHp)
        {
            bool aliveNow = currentHp > 0 && (_localHealth == null || !_localHealth.IsDead);
            if (_isLocalPlayerAlive != aliveNow)
            {
                _isLocalPlayerAlive = aliveNow;
                RefreshPadVisibility();
            }
        }

        private void ReleaseAllHeldControls()
        {
            if (_virtualJoystick != null)
            {
                _virtualJoystick.ResetKnobAndInput();
            }

            if (_actionButtons == null)
            {
                return;
            }

            for (int i = 0; i < _actionButtons.Length; i++)
            {
                if (_actionButtons[i] != null)
                {
                    _actionButtons[i].ReleaseIfHeld();
                }
            }
        }

        private void BindBridgeToChildren()
        {
            if (_virtualJoystick != null)
            {
                _virtualJoystick.BindBridge(_mobileBridge);
            }

            if (_actionButtons == null)
            {
                return;
            }

            for (int i = 0; i < _actionButtons.Length; i++)
            {
                if (_actionButtons[i] != null)
                {
                    _actionButtons[i].BindBridge(_mobileBridge);
                }
            }
        }

        private void EnsurePadHierarchyBuilt()
        {
            if (!TryGetComponent(out RectTransform selfRect))
            {
                selfRect = gameObject.AddComponent<RectTransform>();
            }

            selfRect.anchorMin = Vector2.zero;
            selfRect.anchorMax = Vector2.one;
            selfRect.offsetMin = Vector2.zero;
            selfRect.offsetMax = Vector2.zero;

            if (_canvasGroup == null && !TryGetComponent(out _canvasGroup))
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            EnsureVirtualJoystick();
            if (_actionButtons == null || _actionButtons.Length < 7)
            {
                BuildSevenRadialButtons();
            }
        }

        private void EnsureVirtualJoystick()
        {
            if (_virtualJoystick != null)
            {
                return;
            }

            GameObject joyGo = new GameObject("VirtualJoystick", typeof(RectTransform));
            joyGo.transform.SetParent(transform, worldPositionStays: false);
            RectTransform jrt = joyGo.GetComponent<RectTransform>();
            jrt.anchorMin = Vector2.zero;
            jrt.anchorMax = Vector2.zero;
            jrt.pivot = new Vector2(0.5f, 0.5f);
            jrt.anchoredPosition = new Vector2(180f, 175f);
            jrt.sizeDelta = new Vector2(VirtualJoystick.CanonicalOuterDiameterPx, VirtualJoystick.CanonicalOuterDiameterPx);
            _virtualJoystick = joyGo.AddComponent<VirtualJoystick>();
            _virtualJoystick.BindBridge(_mobileBridge);
        }

        private void BuildSevenRadialButtons()
        {
            _actionButtons = new MobileActionButton[7];
            _actionButtons[0] = CreateRadialButton("BTN_SlashVertical", new Vector2(-125f, 125f), PrimarySlashButtonSizePx,
                NetworkInputButtons.VerticalSlash, GameInputAction.VerticalSlash, CombatSkillId.VerticalSlash, "SLASH", new Color32(0, 180, 216, 225));
            _actionButtons[1] = CreateRadialButton("BTN_SweepLeft", new Vector2(-262f, 165f), 92f,
                NetworkInputButtons.HorizontalSweepLeft, GameInputAction.HorizontalSweepLeft, CombatSkillId.HorizontalSweep, "SWEEP L", new Color32(255, 159, 28, 225));
            _actionButtons[2] = CreateRadialButton("BTN_SweepRight", new Vector2(-95f, 262f), 92f,
                NetworkInputButtons.HorizontalSweepRight, GameInputAction.HorizontalSweepRight, CombatSkillId.HorizontalSweep, "SWEEP R", new Color32(255, 159, 28, 225));
            _actionButtons[3] = CreateRadialButton("BTN_RangedFire", new Vector2(-210f, 258f), 92f,
                NetworkInputButtons.RangedFire, GameInputAction.RangedFire, CombatSkillId.RangedFire, "FIRE", new Color32(155, 93, 229, 225));
            _actionButtons[4] = CreateRadialButton("BTN_Dash", new Vector2(-162f, 362f), 88f,
                NetworkInputButtons.ForwardDash, GameInputAction.ForwardDash, CombatSkillId.ForwardDash, "DASH", new Color32(34, 197, 94, 225));
            _actionButtons[5] = CreateRadialButton("BTN_DodgeLeft", new Vector2(-366f, 78f), 86f,
                NetworkInputButtons.DodgeLeft, GameInputAction.DodgeLeft, CombatSkillId.DodgeRoll, "DODGE L", new Color32(56, 189, 248, 220));
            _actionButtons[6] = CreateRadialButton("BTN_DodgeRight", new Vector2(-262f, 62f), 86f,
                NetworkInputButtons.DodgeRight, GameInputAction.DodgeRight, CombatSkillId.DodgeRoll, "DODGE R", new Color32(56, 189, 248, 220));
        }

        private MobileActionButton CreateRadialButton(
            string goName,
            Vector2 bottomRightAnchoredPos,
            float diameterPx,
            NetworkInputButtons buttonBit,
            GameInputAction gameAction,
            CombatSkillId skillId,
            string title,
            Color color)
        {
            GameObject go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = bottomRightAnchoredPos;
            rt.sizeDelta = new Vector2(diameterPx, diameterPx);

            MobileActionButton btn = go.AddComponent<MobileActionButton>();
            btn.Configure(_mobileBridge, buttonBit, gameAction, skillId, title, color);
            btn.RefreshBadgeFromBindingPath(KeybindingManager.GetDefaultBindingPath(gameAction));
            return btn;
        }

        private void UnbindServiceEvents()
        {
            if (_keybindingService != null)
            {
                _keybindingService.OnBindingsChanged -= HandleBindingsOrModeChanged;
            }

            if (_focusService != null)
            {
                _focusService.OnFocusStateChanged -= HandleFocusStateChanged;
            }
        }

        private void UnbindPlayerEvents()
        {
            if (_localHealth != null)
            {
                _localHealth.OnPlayerDied -= HandleLocalPlayerDied;
                _localHealth.OnHealthChanged -= HandleLocalHealthChanged;
            }

            if (_localCombat != null)
            {
                _localCombat.OnCooldownUpdated -= ForwardCooldownToButtons;
            }

            if (_localMobility != null)
            {
                _localMobility.OnCooldownUpdated -= ForwardCooldownToButtons;
            }
        }

        private void UnbindAll()
        {
            UnbindServiceEvents();
            UnbindPlayerEvents();
        }
    }
}
