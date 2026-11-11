using System;
using UnityEngine;

namespace CapsuleArena.Core.Config
{
    /// <summary>
    /// Designer-editable <see cref="ScriptableObject"/> controlling match start thresholds, pre-match countdown
    /// duration (default <c>5.0s</c>), active round length (<c>180.0s</c>), late-join active combatant grace window
    /// (default <c>60.0s</c> before falling back to Ghost Spectator), and post-match summary duration (<c>8.0s</c>).
    /// </summary>
    [CreateAssetMenu(
        fileName = "DefaultMatchRulesConfig",
        menuName = "CapsuleArena/Config/Match Rules Config")]
    public sealed class MatchRulesConfigSO : ScriptableObject, IMatchRulesConfig
    {
        /// <summary>Canonical default minimum players required to start countdown (2).</summary>
        public const int DefaultMinPlayersToStart = 2;

        /// <summary>Canonical default maximum players per Shared Mode room (4).</summary>
        public const int DefaultMaxPlayersPerMatch = 4;

        /// <summary>Canonical default pre-match countdown duration in seconds (5.0s).</summary>
        public const float DefaultCountdownSeconds = 5.0f;

        /// <summary>Canonical default active combat phase duration in seconds (180.0s).</summary>
        public const float DefaultPlayingSeconds = 180.0f;

        /// <summary>Canonical default opening window in seconds allowing 3rd/4th players to join alive (60.0s).</summary>
        public const float DefaultLateJoinActiveWindowSeconds = 60.0f;

        /// <summary>Canonical default post-match summary duration in seconds (8.0s).</summary>
        public const float DefaultEndedSeconds = 8.0f;

        private const float MinPhaseDurationSeconds = 0.5f;

        [Header("1. Roster Thresholds")]
        [Tooltip("Minimum connected players required to trigger and sustain pre-match countdown (default: 2).")]
        [SerializeField] private int _minPlayersToStart = DefaultMinPlayersToStart;

        [Tooltip("Maximum players supported per Shared Mode room (default: 4).")]
        [SerializeField] private int _maxPlayersPerMatch = DefaultMaxPlayersPerMatch;

        [Header("2. Match Phase Timers (Seconds)")]
        [Tooltip("Pre-match countdown duration once minimum players connect (default: 5.0s).")]
        [SerializeField] private float _countdownDurationSeconds = DefaultCountdownSeconds;

        [Tooltip("Active combat round duration before Sudden Death evaluation (default: 180.0s).")]
        [SerializeField] private float _playingDurationSeconds = DefaultPlayingSeconds;

        [Tooltip("Post-match summary duration before starting the next round (default: 8.0s).")]
        [SerializeField] private float _endedDurationSeconds = DefaultEndedSeconds;

        [Header("3. Mid-Match Late-Join Rule")]
        [Tooltip("Opening seconds of an active match during which players 3 & 4 still join as live combatants. After this window, late joiners enter Ghost Spectator mode (default: 60.0s).")]
        [SerializeField] private float _lateJoinActiveWindowSeconds = DefaultLateJoinActiveWindowSeconds;

        [NonSerialized] private int _countdownDurationTicks;
        [NonSerialized] private int _playingDurationTicks;
        [NonSerialized] private int _lateJoinActiveWindowTicks;
        [NonSerialized] private int _endedDurationTicks;

        /// <inheritdoc />
        public int MinPlayersToStart => _minPlayersToStart;

        /// <inheritdoc />
        public int MaxPlayersPerMatch => _maxPlayersPerMatch;

        /// <inheritdoc />
        public float CountdownDurationSeconds => _countdownDurationSeconds;

        /// <inheritdoc />
        public int CountdownDurationTicks => _countdownDurationTicks;

        /// <inheritdoc />
        public float PlayingDurationSeconds => _playingDurationSeconds;

        /// <inheritdoc />
        public int PlayingDurationTicks => _playingDurationTicks;

        /// <inheritdoc />
        public float LateJoinActiveWindowSeconds => _lateJoinActiveWindowSeconds;

        /// <inheritdoc />
        public int LateJoinActiveWindowTicks => _lateJoinActiveWindowTicks;

        /// <inheritdoc />
        public float EndedDurationSeconds => _endedDurationSeconds;

        /// <inheritdoc />
        public int EndedDurationTicks => _endedDurationTicks;

        private void OnEnable()
        {
            ValidateAndClamp();
        }

        private void OnValidate()
        {
            ValidateAndClamp();
        }

        /// <summary>
        /// Configures values programmatically for deterministic unit tests and refreshes precomputed 60Hz tick counts.
        /// </summary>
        public void ConfigureForTest(
            float countdownSeconds = DefaultCountdownSeconds,
            float playingSeconds = DefaultPlayingSeconds,
            float lateJoinActiveWindowSeconds = DefaultLateJoinActiveWindowSeconds,
            float endedSeconds = DefaultEndedSeconds,
            int minPlayersToStart = DefaultMinPlayersToStart,
            int maxPlayersPerMatch = DefaultMaxPlayersPerMatch)
        {
            _countdownDurationSeconds = countdownSeconds;
            _playingDurationSeconds = playingSeconds;
            _lateJoinActiveWindowSeconds = lateJoinActiveWindowSeconds;
            _endedDurationSeconds = endedSeconds;
            _minPlayersToStart = minPlayersToStart;
            _maxPlayersPerMatch = maxPlayersPerMatch;
            ValidateAndClamp();
        }

        /// <summary>
        /// Clamps all Inspector fields to valid physical bounds and precomputes 60Hz simulation tick counts.
        /// </summary>
        public void ValidateAndClamp(int tickRate = CharacterCombatConfigSO.DefaultTickRate)
        {
            _maxPlayersPerMatch = Mathf.Clamp(_maxPlayersPerMatch, 2, 4);
            _minPlayersToStart = Mathf.Clamp(_minPlayersToStart, 2, _maxPlayersPerMatch);

            _countdownDurationSeconds = Mathf.Clamp(
                SanitizeFloat(_countdownDurationSeconds, DefaultCountdownSeconds),
                MinPhaseDurationSeconds,
                60.0f);

            _playingDurationSeconds = Mathf.Clamp(
                SanitizeFloat(_playingDurationSeconds, DefaultPlayingSeconds),
                5.0f,
                3600.0f);

            _lateJoinActiveWindowSeconds = Mathf.Clamp(
                SanitizeFloat(_lateJoinActiveWindowSeconds, DefaultLateJoinActiveWindowSeconds),
                0.0f,
                _playingDurationSeconds);

            _endedDurationSeconds = Mathf.Clamp(
                SanitizeFloat(_endedDurationSeconds, DefaultEndedSeconds),
                MinPhaseDurationSeconds,
                60.0f);

            RecalculateDerivedTicks(tickRate);
        }

        /// <summary>Precomputes integer tick counts for zero-allocation FixedUpdateNetwork evaluation.</summary>
        public void RecalculateDerivedTicks(int tickRate = CharacterCombatConfigSO.DefaultTickRate)
        {
            int safeTickRate = Mathf.Max(1, tickRate);
            _countdownDurationTicks = Mathf.Max(1, Mathf.RoundToInt(_countdownDurationSeconds * safeTickRate));
            _playingDurationTicks = Mathf.Max(1, Mathf.RoundToInt(_playingDurationSeconds * safeTickRate));
            _lateJoinActiveWindowTicks = Mathf.Clamp(
                Mathf.RoundToInt(_lateJoinActiveWindowSeconds * safeTickRate),
                0,
                _playingDurationTicks);
            _endedDurationTicks = Mathf.Max(1, Mathf.RoundToInt(_endedDurationSeconds * safeTickRate));
        }

        private static float SanitizeFloat(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }
}
