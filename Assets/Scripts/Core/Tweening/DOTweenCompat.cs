#if !DOTWEEN
using System;
using UnityEngine;
using UnityEngine.UI;

namespace DG.Tweening
{
    /// <summary>
    /// Standard easing curves supported by the lightweight zero-GC <see cref="DOTween"/> runtime compatibility layer.
    /// </summary>
    public enum Ease
    {
        Linear = 0,
        OutQuad = 1,
        InOutQuad = 2,
        OutCubic = 3,
        OutBack = 4,
        Punch = 5
    }

    internal enum TweenValueKind : byte
    {
        CustomSetter = 0,
        TransformScale = 1,
        TransformPulseScale = 2,
        RectAnchorPos = 3,
        CanvasGroupAlpha = 4,
        ImageFillAmount = 5
    }

    /// <summary>
    /// Pre-allocated zero-closure fluent handle returned by <see cref="ShortcutExtensions"/> tween methods.
    /// </summary>
    public sealed class Tweener
    {
        internal object Target;
        internal TweenValueKind Kind;
        internal Vector3 StartVec3;
        internal Vector3 EndVec3;
        internal Action<float> CustomSetter;
        internal float Duration;
        internal float Elapsed;
        internal Ease EaseMode;
        internal Action CompleteCallback;
        internal bool IsActive;

        /// <summary>Assigns the easing curve for this tween.</summary>
        public Tweener SetEase(Ease ease)
        {
            EaseMode = ease;
            return this;
        }

        /// <summary>Registers a callback invoked when the tween reaches completion (including <c>DOKill(true)</c>).</summary>
        public Tweener OnComplete(Action onComplete)
        {
            CompleteCallback = onComplete;
            return this;
        }

        /// <summary>Cancels this tween, optionally snapping immediately to <c>t = 1.0f</c> first.</summary>
        public void Kill(bool complete = false)
        {
            DOTween.KillTweenInstance(this, complete);
        }

        internal void ApplySample(float easedT)
        {
            switch (Kind)
            {
                case TweenValueKind.TransformScale:
                    if (Target is Transform tr && tr != null)
                    {
                        tr.localScale = Vector3.LerpUnclamped(StartVec3, EndVec3, easedT);
                    }
                    break;

                case TweenValueKind.TransformPulseScale:
                    ApplyPulseScale(easedT);
                    break;

                case TweenValueKind.RectAnchorPos:
                    if (Target is RectTransform rt && rt != null)
                    {
                        Vector2 start2 = new Vector2(StartVec3.x, StartVec3.y);
                        Vector2 end2 = new Vector2(EndVec3.x, EndVec3.y);
                        rt.anchoredPosition = Vector2.LerpUnclamped(start2, end2, easedT);
                    }
                    break;

                case TweenValueKind.CanvasGroupAlpha:
                    if (Target is CanvasGroup cg && cg != null)
                    {
                        cg.alpha = Mathf.Lerp(StartVec3.x, EndVec3.x, Mathf.Clamp01(easedT));
                    }
                    break;

                case TweenValueKind.ImageFillAmount:
                    if (Target is Image img && img != null)
                    {
                        img.fillAmount = Mathf.Lerp(StartVec3.x, EndVec3.x, Mathf.Clamp01(easedT));
                    }
                    break;

                default:
                    CustomSetter?.Invoke(easedT);
                    break;
            }
        }

        private void ApplyPulseScale(float normalizedT)
        {
            if (Target is not Transform tr || tr == null)
            {
                return;
            }

            if (normalizedT >= 1f)
            {
                tr.localScale = Vector3.one;
                return;
            }

            float wave = normalizedT < 0.35f
                ? (normalizedT / 0.35f)
                : (1f - ((normalizedT - 0.35f) / 0.65f));
            tr.localScale = Vector3.LerpUnclamped(Vector3.one, EndVec3, Mathf.Clamp01(wave));
        }
    }

    /// <summary>
    /// Pre-allocated fixed-capacity tween scheduler providing zero-closure <c>DG.Tweening</c> APIs
    /// (<c>DOScale</c>, <c>DOFade</c>, <c>DOFillAmount</c>, <c>DOAnchorPos</c>, <c>DOKill</c>)
    /// with deterministic <see cref="ManualUpdate"/> support for EditMode unit tests.
    /// </summary>
    public static class DOTween
    {
        private const int MaxConcurrentTweens = 96;
        private static readonly Tweener[] Pool = CreatePrewarmedPool();
        private static readonly Tweener InactiveNoOpTweener = new Tweener();
        private static DOTweenRuntimeDriver _driver;

        /// <summary>Starts a zero-allocation custom normalized [0, 1] tween bound to <paramref name="target"/>.</summary>
        public static Tweener To(object target, Action<float> normalizedSetter, float duration)
        {
            if (target == null || normalizedSetter == null)
            {
                return InactiveNoOpTweener;
            }

            Tweener t = StartTypedTween(target, TweenValueKind.CustomSetter, Vector3.zero, Vector3.one, duration);
            t.CustomSetter = normalizedSetter;
            t.EaseMode = Ease.Linear;
            if (duration > 0f)
            {
                t.ApplySample(0f);
            }

            return t;
        }

        /// <summary>Advances all active tweens by <paramref name="deltaTime"/> seconds.</summary>
        public static void ManualUpdate(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            for (int i = 0; i < MaxConcurrentTweens; i++)
            {
                StepActiveSlot(Pool[i], deltaTime);
            }
        }

        /// <summary>Cancels all active tweens targeting <paramref name="target"/>, snapping to end state if <paramref name="complete"/> is true.</summary>
        public static int Kill(object target, bool complete = false)
        {
            if (target == null)
            {
                return 0;
            }

            int killed = 0;
            for (int i = 0; i < MaxConcurrentTweens; i++)
            {
                Tweener t = Pool[i];
                if (t.IsActive && ReferenceEquals(t.Target, target))
                {
                    KillTweenInstance(t, complete);
                    killed++;
                }
            }

            return killed;
        }

        /// <summary>Returns true when at least one active tween is currently targeting <paramref name="target"/>.</summary>
        public static bool IsTweening(object target)
        {
            if (target == null)
            {
                return false;
            }

            for (int i = 0; i < MaxConcurrentTweens; i++)
            {
                if (Pool[i].IsActive && ReferenceEquals(Pool[i].Target, target))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Clears all active tweens across the pool (invoked on domain/test reset).</summary>
        public static void ClearAll()
        {
            for (int i = 0; i < MaxConcurrentTweens; i++)
            {
                ResetSlot(Pool[i]);
            }
        }

        internal static Tweener StartTypedTween(
            object target,
            TweenValueKind kind,
            Vector3 startVec,
            Vector3 endVec,
            float duration)
        {
            if (target == null)
            {
                return InactiveNoOpTweener;
            }

            EnsureDriverInPlayMode();
            Tweener slot = AcquireSlot();
            slot.Target = target;
            slot.Kind = kind;
            slot.StartVec3 = startVec;
            slot.EndVec3 = endVec;
            slot.CustomSetter = null;
            slot.Duration = Mathf.Max(0.0001f, duration);
            slot.Elapsed = 0f;
            slot.EaseMode = Ease.OutQuad;
            slot.CompleteCallback = null;
            slot.IsActive = true;

            if (duration <= 0f)
            {
                CompleteAndRelease(slot);
            }
            else
            {
                slot.ApplySample(0f);
            }

            return slot;
        }

        internal static void KillTweenInstance(Tweener tween, bool complete)
        {
            if (tween == null || !tween.IsActive)
            {
                return;
            }

            if (complete)
            {
                CompleteAndRelease(tween);
            }
            else
            {
                ResetSlot(tween);
            }
        }

        private static void StepActiveSlot(Tweener t, float deltaTime)
        {
            if (!t.IsActive)
            {
                return;
            }

            if (t.Target is UnityEngine.Object unityObj && unityObj == null)
            {
                ResetSlot(t);
                return;
            }

            t.Elapsed += deltaTime;
            if (t.Elapsed >= t.Duration)
            {
                CompleteAndRelease(t);
                return;
            }

            float normalized = Mathf.Clamp01(t.Elapsed / Mathf.Max(0.0001f, t.Duration));
            float sample = t.Kind == TweenValueKind.TransformPulseScale
                ? normalized
                : EvaluateEase(t.EaseMode, normalized);
            t.ApplySample(sample);
        }

        private static void CompleteAndRelease(Tweener tween)
        {
            Action callback = tween.CompleteCallback;
            tween.ApplySample(1f);
            ResetSlot(tween);
            callback?.Invoke();
        }

        private static void ResetSlot(Tweener tween)
        {
            tween.IsActive = false;
            tween.Target = null;
            tween.CustomSetter = null;
            tween.CompleteCallback = null;
            tween.Elapsed = 0f;
        }

        private static Tweener AcquireSlot()
        {
            for (int i = 0; i < MaxConcurrentTweens; i++)
            {
                if (!Pool[i].IsActive)
                {
                    return Pool[i];
                }
            }

            ResetSlot(Pool[0]);
            return Pool[0];
        }

        private static Tweener[] CreatePrewarmedPool()
        {
            Tweener[] arr = new Tweener[MaxConcurrentTweens];
            for (int i = 0; i < MaxConcurrentTweens; i++)
            {
                arr[i] = new Tweener();
            }

            return arr;
        }

        private static float EvaluateEase(Ease ease, float t)
        {
            return ease switch
            {
                Ease.OutQuad => 1f - (1f - t) * (1f - t),
                Ease.InOutQuad => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f,
                Ease.OutCubic => 1f - Mathf.Pow(1f - t, 3f),
                Ease.OutBack => 1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f),
                Ease.Punch => Mathf.Sin(t * Mathf.PI),
                _ => t
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload()
        {
            ClearAll();
            _driver = null;
        }

        private static void EnsureDriverInPlayMode()
        {
            if (!Application.isPlaying || _driver != null)
            {
                return;
            }

            GameObject go = new GameObject("[DOTweenRuntimeDriver]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _driver = go.AddComponent<DOTweenRuntimeDriver>();
        }

        private sealed class DOTweenRuntimeDriver : MonoBehaviour
        {
            private void Update()
            {
                ManualUpdate(Time.unscaledDeltaTime);
            }
        }
    }

    /// <summary>
    /// Zero-closure extension methods matching <c>DG.Tweening</c> syntax on <see cref="Transform"/>,
    /// <see cref="RectTransform"/>, <see cref="CanvasGroup"/>, and <see cref="Image"/>.
    /// </summary>
    public static class ShortcutExtensions
    {
        /// <summary>Tweens <paramref name="target"/> localScale toward <paramref name="endValue"/> with 0 B closure allocation.</summary>
        public static Tweener DOScale(this Transform target, Vector3 endValue, float duration)
        {
            Vector3 start = target != null ? target.localScale : Vector3.one;
            return DOTween.StartTypedTween(target, TweenValueKind.TransformScale, start, endValue, duration);
        }

        /// <summary>Applies a symmetric squash-and-recover pulse from <paramref name="peakScale"/> back to <c>Vector3.one</c> with 0 B closure allocation.</summary>
        public static Tweener DOPulseScaleToOne(this Transform target, Vector3 peakScale, float duration)
        {
            return DOTween.StartTypedTween(target, TweenValueKind.TransformPulseScale, Vector3.one, peakScale, duration);
        }

        /// <summary>Tweens <paramref name="target"/> anchoredPosition toward <paramref name="endValue"/> with 0 B closure allocation.</summary>
        public static Tweener DOAnchorPos(this RectTransform target, Vector2 endValue, float duration)
        {
            Vector2 s = target != null ? target.anchoredPosition : Vector2.zero;
            return DOTween.StartTypedTween(target, TweenValueKind.RectAnchorPos, new Vector3(s.x, s.y, 0f), new Vector3(endValue.x, endValue.y, 0f), duration);
        }

        /// <summary>Tweens <paramref name="target"/> alpha toward <paramref name="endValue"/> with 0 B closure allocation.</summary>
        public static Tweener DOFade(this CanvasGroup target, float endValue, float duration)
        {
            float s = target != null ? target.alpha : 0f;
            return DOTween.StartTypedTween(target, TweenValueKind.CanvasGroupAlpha, new Vector3(s, 0f, 0f), new Vector3(endValue, 0f, 0f), duration);
        }

        /// <summary>Tweens <paramref name="target"/> fillAmount toward <paramref name="endValue"/> with 0 B closure allocation.</summary>
        public static Tweener DOFillAmount(this Image target, float endValue, float duration)
        {
            float s = target != null ? target.fillAmount : 0f;
            return DOTween.StartTypedTween(target, TweenValueKind.ImageFillAmount, new Vector3(s, 0f, 0f), new Vector3(endValue, 0f, 0f), duration);
        }

        /// <summary>Kills all active tweens bound to <paramref name="target"/>.</summary>
        public static int DOKill(this Component target, bool complete = false)
        {
            return DOTween.Kill(target, complete);
        }
    }
}
#endif
