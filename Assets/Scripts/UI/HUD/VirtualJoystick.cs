using CapsuleArena.Core.Input;
using CapsuleArena.UI.Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CapsuleArena.UI.HUD
{
    /// <summary>
    /// Bottom-left <c>240x240px</c> virtual movement joystick forwarding normalized 2D vectors to
    /// <see cref="MobileInputBridge.SetMoveVector"/> via <see cref="IPointerDownHandler"/>,
    /// <see cref="IDragHandler"/>, and <see cref="IPointerUpHandler"/> (Story 10-2; ADR-0007; TR-hud-002).
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>Canonical outer touch zone diameter at 1080p (240px).</summary>
        public const float CanonicalOuterDiameterPx = 240.0f;

        /// <summary>Maximum knob travel radius in UI pixels (90px).</summary>
        public const float MaxKnobRadiusPx = 90.0f;

        [SerializeField] private RectTransform _backgroundRing;
        [SerializeField] private RectTransform _handleKnob;
        [SerializeField] private MobileInputBridge _mobileBridge;

        private Vector2 _currentMoveVector;

        /// <summary>Gets the current normalized joystick vector in [-1, +1]^2.</summary>
        public Vector2 CurrentMoveVector => _currentMoveVector;

        private void Awake()
        {
            EnsureVisualsBuilt();
        }

        private void OnDisable()
        {
            ResetKnobAndInput();
        }

        /// <summary>Binds the target <see cref="MobileInputBridge"/>.</summary>
        public void BindBridge(MobileInputBridge bridge)
        {
            _mobileBridge = bridge;
            EnsureVisualsBuilt();
        }

        /// <inheritdoc />
        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        /// <inheritdoc />
        public void OnDrag(PointerEventData eventData)
        {
            if (_backgroundRing == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _backgroundRing,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
            {
                ApplyLocalOffset(localPoint);
            }
        }

        /// <inheritdoc />
        public void OnPointerUp(PointerEventData eventData)
        {
            ResetKnobAndInput();
        }

        /// <summary>Directly applies a local drag offset in pixels (used by pointer events and deterministic unit tests).</summary>
        public void ApplyLocalOffset(Vector2 localOffsetPixels)
        {
            Vector2 clampedPixels = Vector2.ClampMagnitude(localOffsetPixels, MaxKnobRadiusPx);
            _currentMoveVector = Vector2.ClampMagnitude(clampedPixels / MaxKnobRadiusPx, 1.0f);

            if (_handleKnob != null)
            {
                _handleKnob.anchoredPosition = clampedPixels;
            }

            _mobileBridge?.SetMoveVector(_currentMoveVector);
        }

        /// <summary>Centers the handle knob and clears the movement vector.</summary>
        public void ResetKnobAndInput()
        {
            _currentMoveVector = Vector2.zero;
            if (_handleKnob != null)
            {
                _handleKnob.anchoredPosition = Vector2.zero;
            }

            _mobileBridge?.SetMoveVector(Vector2.zero);
        }

        private void EnsureVisualsBuilt()
        {
            if (_backgroundRing == null)
            {
                _backgroundRing = GetComponent<RectTransform>();
                if (_backgroundRing == null)
                {
                    _backgroundRing = gameObject.AddComponent<RectTransform>();
                }
            }

            if (_backgroundRing.sizeDelta.sqrMagnitude < 1f)
            {
                _backgroundRing.sizeDelta = new Vector2(CanonicalOuterDiameterPx, CanonicalOuterDiameterPx);
            }

            if (!TryGetComponent(out Image ringImg))
            {
                ringImg = gameObject.AddComponent<Image>();
                ringImg.sprite = UiThemeAndFontProvider.GetCircleSprite();
                ringImg.color = new Color32(15, 23, 42, 145);
                ringImg.raycastTarget = true;
            }

            if (_handleKnob == null)
            {
                GameObject knobGo = new GameObject("HandleKnob", typeof(RectTransform));
                knobGo.transform.SetParent(_backgroundRing, worldPositionStays: false);
                _handleKnob = knobGo.GetComponent<RectTransform>();
                _handleKnob.anchorMin = new Vector2(0.5f, 0.5f);
                _handleKnob.anchorMax = new Vector2(0.5f, 0.5f);
                _handleKnob.sizeDelta = new Vector2(92f, 92f);

                Image knobImg = knobGo.AddComponent<Image>();
                knobImg.sprite = UiThemeAndFontProvider.GetCircleSprite();
                knobImg.color = new Color32(0, 245, 212, 210);
                knobImg.raycastTarget = false;
            }
        }
    }
}
