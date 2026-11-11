using UnityEngine;

namespace CapsuleArena.UI.Chat
{
    /// <summary>
    /// Deterministic Token-Bucket chat rate limiter enforcing <c>5 messages / 3.0s</c>
    /// (<c>Capacity = 5.0</c>, <c>RefillRate = 1.6667 tokens/s</c>) with explicit timestamp injection
    /// for unit tests (Story 12-3; ADR-0007; TR-chat-003).
    /// </summary>
    public sealed class ChatRateLimiter
    {
        /// <summary>Canonical burst token capacity (5.0 tokens).</summary>
        public const float DefaultCapacity = 5.0f;

        /// <summary>Canonical burst window in seconds (3.0s -> 5.0 / 3.0 = 1.6667 tokens/s refill).</summary>
        public const float DefaultWindowSeconds = 3.0f;

        private readonly float _capacity;
        private readonly float _refillRatePerSec;
        private float _tokens;
        private float _lastTimestampSec;
        private bool _hasInitializedTimestamp;

        /// <summary>Gets the maximum burst token capacity.</summary>
        public float Capacity => _capacity;

        /// <summary>Gets the steady-state token refill rate in tokens per second.</summary>
        public float RefillRatePerSecond => _refillRatePerSec;

        /// <summary>Gets the currently available token count in <c>[0, Capacity]</c>.</summary>
        public float CurrentTokens => _tokens;

        /// <summary>
        /// Initializes a new <see cref="ChatRateLimiter"/> with a full token bucket.
        /// </summary>
        public ChatRateLimiter(float capacity = DefaultCapacity, float windowSeconds = DefaultWindowSeconds)
        {
            _capacity = Mathf.Max(1.0f, capacity);
            float safeWindow = Mathf.Max(0.1f, windowSeconds);
            _refillRatePerSec = _capacity / safeWindow;
            _tokens = _capacity;
            _lastTimestampSec = 0f;
            _hasInitializedTimestamp = false;
        }

        /// <summary>
        /// Refills tokens elapsed since the last check (<c>tokens = min(Capacity, tokens + dt * RefillRate)</c>)
        /// and consumes <c>1.0</c> token if available, returning <c>true</c>; otherwise leaves tokens intact and returns <c>false</c>.
        /// </summary>
        public bool TryConsumeToken(float currentTimeSec)
        {
            if (!_hasInitializedTimestamp)
            {
                _lastTimestampSec = currentTimeSec;
                _hasInitializedTimestamp = true;
            }
            else
            {
                float dt = Mathf.Max(0f, currentTimeSec - _lastTimestampSec);
                if (dt > 0f)
                {
                    _tokens = Mathf.Min(_capacity, _tokens + dt * _refillRatePerSec);
                    _lastTimestampSec = currentTimeSec;
                }
            }

            if (_tokens < 1.0f)
            {
                return false;
            }

            _tokens -= 1.0f;
            return true;
        }

        /// <summary>Alias for <see cref="TryConsumeToken"/> supporting concise test ergonomics.</summary>
        public bool TryConsume(float currentTimeSec)
        {
            return TryConsumeToken(currentTimeSec);
        }

        /// <summary>Resets the bucket back to full <see cref="Capacity"/> at <paramref name="currentTimeSec"/>.</summary>
        public void ResetBucket(float currentTimeSec = 0f)
        {
            _tokens = _capacity;
            _lastTimestampSec = currentTimeSec;
            _hasInitializedTimestamp = true;
        }
    }
}
