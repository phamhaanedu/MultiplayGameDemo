using System;
using System.Collections.Generic;
using CapsuleArena.Combat;
using CapsuleArena.Combat.Projectile;
using CapsuleArena.Player;
using Fusion;
using UnityEngine;
using UnityEngine.Audio;

namespace CapsuleArena.Presentation.Audio
{
    /// <summary>
    /// Canonical sound effect identifiers routed through <see cref="SoundManager"/> (Story 14-1; ADR-0008; TR-audio-001).
    /// </summary>
    public enum SoundId : byte
    {
        /// <summary>LMB Vertical Slash swing whoosh.</summary>
        VerticalSlash = 0,

        /// <summary>Keys 1/3 Horizontal Sweep wide whoosh.</summary>
        HorizontalSweep = 1,

        /// <summary>RMB Ranged energy bolt launch.</summary>
        RangedFire = 2,

        /// <summary>Confirmed melee hit on an enemy capsule.</summary>
        MeleeHitPlayer = 3,

        /// <summary>Confirmed ranged bolt hit on an enemy capsule.</summary>
        ProjectileHitPlayer = 4,

        /// <summary>Projectile or melee spark impact against a Layer 6 wall.</summary>
        WallImpact = 5,

        /// <summary>Key 2 Forward Dash burst impulse.</summary>
        ForwardDash = 6,

        /// <summary>Keys Q/E Lateral Dodge Roll impulse.</summary>
        DodgeRoll = 7,

        /// <summary>Lethal elimination chime.</summary>
        PlayerEliminated = 8,

        /// <summary>2D UI button click / tab switch.</summary>
        UiClick = 9
    }

    /// <summary>
    /// Encapsulated inspector-assignable mapping between a <see cref="SoundId"/> and an <see cref="AudioClip"/>.
    /// </summary>
    [Serializable]
    public struct SoundCueBinding
    {
        [SerializeField] private SoundId _id;
        [SerializeField] private AudioClip _clip;
        [Range(0f, 1f)]
        [SerializeField] private float _volume;

        /// <summary>Gets the logical sound cue identifier.</summary>
        public SoundId Id => _id;

        /// <summary>Gets the assigned audio asset.</summary>
        public AudioClip Clip => _clip;

        /// <summary>Gets the base playback volume in [0, 1].</summary>
        public float Volume => _volume;

        /// <summary>Initializes an immutable <see cref="SoundCueBinding"/> entry.</summary>
        public SoundCueBinding(SoundId id, AudioClip clip, float volume = 0.85f)
        {
            _id = id;
            _clip = clip;
            _volume = Mathf.Clamp01(volume);
        }
    }

    /// <summary>
    /// Pre-warmed 3D spatial audio pool (<c>16</c> 3D <see cref="AudioSource"/> voices + <c>2</c> 2D UI channels)
    /// enforcing <c>30.0m</c> distance culling (<c>sqrMagnitude &gt; 900f</c>), <c>[0.93, 1.07]</c> pitch jitter,
    /// deterministic oldest-voice stealing when all 16 emitters are active, procedural PCM clip fallbacks,
    /// and zero runtime <c>PlayClipAtPoint</c> / <c>Instantiate</c> calls (Story 14-1; ADR-0008; TR-audio-001).
    /// </summary>
    public sealed class SoundManager : MonoBehaviour
    {
        /// <summary>Fixed number of pre-warmed 3D spatial <see cref="AudioSource"/> emitters (16).</summary>
        public const int SpatialEmitterPoolSize = 16;

        /// <summary>Fixed number of pre-warmed 2D UI <see cref="AudioSource"/> channels (2).</summary>
        public const int UiChannelPoolSize = 2;

        /// <summary>Full-volume 3D attenuation inner radius in meters (2.5m).</summary>
        public const float SpatialMinDistanceMeters = 2.5f;

        /// <summary>Zero-volume 3D linear rolloff outer radius in meters (28.0m).</summary>
        public const float SpatialMaxDistanceMeters = 28.0f;

        /// <summary>Hard distance culling cutoff beyond which 3D sounds are skipped (30.0m).</summary>
        public const float MaxAudibleDistanceMeters = 30.0f;

        /// <summary>Squared distance culling cutoff (900.0 m^2).</summary>
        public const float MaxAudibleDistanceSqr = MaxAudibleDistanceMeters * MaxAudibleDistanceMeters;

        /// <summary>Minimum randomized pitch multiplier (0.93).</summary>
        public const float MinPitchJitter = 0.93f;

        /// <summary>Maximum randomized pitch multiplier (1.07).</summary>
        public const float MaxPitchJitter = 1.07f;

        private const int RangedDamageCeiling = 15;

        [Header("Mixer Routing & Clips")]
        [SerializeField] private AudioMixerGroup _sfx3dMixerGroup;
        [SerializeField] private AudioMixerGroup _ui2dMixerGroup;
        [SerializeField] private Transform _listenerTransform;
        [SerializeField] private bool _useProceduralFallbacks = true;
        [SerializeField] private SoundCueBinding[] _customCues;

        private readonly Dictionary<SoundId, AudioClip> _clipLookup = new Dictionary<SoundId, AudioClip>(12);
        private readonly Dictionary<SoundId, float> _volumeLookup = new Dictionary<SoundId, float>(12);
        private readonly List<AudioClip> _proceduralClips = new List<AudioClip>(12);
        private readonly HashSet<SoundId> _warnedMissingClips = new HashSet<SoundId>();

        private AudioSource[] _emitters3D;
        private float[] _emitterStartTimes;
        private float[] _emitterEndTimes;
        private AudioSource[] _channels2D;
        private int _uiCursor;
        private bool _isPoolBuilt;

        /// <summary>Gets the pre-warmed 16-item 3D <see cref="AudioSource"/> array for inspection and tests.</summary>
        public IReadOnlyList<AudioSource> SpatialEmitters
        {
            get
            {
                EnsurePoolsInitialized();
                return _emitters3D;
            }
        }

        /// <summary>Gets the pre-warmed 2-item 2D UI <see cref="AudioSource"/> array for inspection and tests.</summary>
        public IReadOnlyList<AudioSource> UiChannels
        {
            get
            {
                EnsurePoolsInitialized();
                return _channels2D;
            }
        }

        /// <summary>Gets the assigned 3D SFX <see cref="AudioMixerGroup"/>.</summary>
        public AudioMixerGroup Sfx3dMixerGroup => _sfx3dMixerGroup;

        /// <summary>Gets the assigned 2D UI <see cref="AudioMixerGroup"/>.</summary>
        public AudioMixerGroup Ui2dMixerGroup => _ui2dMixerGroup;

        /// <summary>Gets the number of unique missing-clip warnings emitted so far.</summary>
        public int MissingClipWarningCount => _warnedMissingClips.Count;

        private void Awake()
        {
            EnsurePoolsInitialized();
        }

        private void OnEnable()
        {
            PlayerCombatController.OnAnyMeleeSwingExecuted += HandleMeleeSwing;
            PlayerCombatController.OnAnyRangedShotExecuted += HandleRangedShot;
            NetworkProjectile.OnAnyProjectileImpact += HandleProjectileImpact;
            PlayerHealth.OnAnyHitFeedbackBroadcast += HandleHitFeedback;
            PlayerMobilityController.OnAnyMobilityBurstStarted += HandleMobilityBurst;
        }

        private void OnDisable()
        {
            PlayerCombatController.OnAnyMeleeSwingExecuted -= HandleMeleeSwing;
            PlayerCombatController.OnAnyRangedShotExecuted -= HandleRangedShot;
            NetworkProjectile.OnAnyProjectileImpact -= HandleProjectileImpact;
            PlayerHealth.OnAnyHitFeedbackBroadcast -= HandleHitFeedback;
            PlayerMobilityController.OnAnyMobilityBurstStarted -= HandleMobilityBurst;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _proceduralClips.Count; i++)
            {
                AudioClip clip = _proceduralClips[i];
                if (clip == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(clip);
                }
                else
                {
                    DestroyImmediate(clip);
                }
            }

            _proceduralClips.Clear();
        }

        /// <summary>Binds the active listener transform (typically <c>MainCamera.transform</c>) via DI.</summary>
        public void SetListenerTransform(Transform listenerTransform)
        {
            _listenerTransform = listenerTransform;
            EnsurePoolsInitialized();
        }

        /// <summary>Assigns 3D SFX and 2D UI <see cref="AudioMixerGroup"/> outputs across all pooled sources.</summary>
        public void SetMixerGroups(AudioMixerGroup sfx3dGroup, AudioMixerGroup ui2dGroup)
        {
            _sfx3dMixerGroup = sfx3dGroup;
            _ui2dMixerGroup = ui2dGroup;
            EnsurePoolsInitialized();

            for (int i = 0; i < _emitters3D.Length; i++)
            {
                if (_emitters3D[i] != null)
                {
                    _emitters3D[i].outputAudioMixerGroup = _sfx3dMixerGroup;
                }
            }

            for (int i = 0; i < _channels2D.Length; i++)
            {
                if (_channels2D[i] != null)
                {
                    _channels2D[i].outputAudioMixerGroup = _ui2dMixerGroup;
                }
            }
        }

        /// <summary>Overrides or clears a clip entry (used by EditMode tests to verify null-clip warn-once safety).</summary>
        public void SetClipOverride(SoundId id, AudioClip clip, float volume = 0.85f)
        {
            EnsurePoolsInitialized();
            _clipLookup[id] = clip;
            _volumeLookup[id] = Mathf.Clamp01(volume);
        }

        /// <summary>
        /// Plays a 3D spatial sound at <paramref name="worldPosition"/> if within <c>30.0m</c> of the listener.
        /// Returns the acquired emitter index <c>[0..15]</c>, or <c>-1</c> when culled or unmapped.
        /// </summary>
        public int Play3D(
            SoundId id,
            Vector3 worldPosition,
            Vector3? listenerPositionOverride = null,
            float currentTimeSec = -1f)
        {
            EnsurePoolsInitialized();
            Vector3 listenerPos = ResolveListenerPosition(listenerPositionOverride);
            if ((worldPosition - listenerPos).sqrMagnitude > MaxAudibleDistanceSqr)
            {
                return -1;
            }

            if (!TryResolveClip(id, out AudioClip clip, out float volume))
            {
                return -1;
            }

            float now = currentTimeSec >= 0f ? currentTimeSec : Time.unscaledTime;
            int index = AcquireEmitterIndex(now);
            ConfigureAndFire3DEmitter(index, clip, volume, worldPosition, now);
            return index;
        }

        /// <summary>Plays a non-spatial 2D UI sound cue using round-robin UI channels and returns the channel index (<c>0..1</c>).</summary>
        public int Play2D(SoundId id = SoundId.UiClick)
        {
            EnsurePoolsInitialized();
            if (!TryResolveClip(id, out AudioClip clip, out float volume))
            {
                return -1;
            }

            int selected = _uiCursor;
            AudioSource src = _channels2D[selected];
            _uiCursor = (_uiCursor + 1) % UiChannelPoolSize;
            src.pitch = 1.0f;
            src.volume = volume;
            src.clip = clip;
            src.Play();
            return selected;
        }

        /// <summary>
        /// Selects a free 3D emitter in <c>[0..15]</c> or deterministically steals the oldest voice
        /// (<c>argmin _emitterStartTimes[i]</c>) when all 16 voices are busy.
        /// </summary>
        public int AcquireEmitterIndex(float currentTimeSec)
        {
            EnsurePoolsInitialized();
            int oldestIndex = 0;
            float oldestStart = float.MaxValue;

            for (int i = 0; i < SpatialEmitterPoolSize; i++)
            {
                bool busy = currentTimeSec < _emitterEndTimes[i] || _emitters3D[i].isPlaying;
                if (!busy)
                {
                    return i;
                }

                if (_emitterStartTimes[i] < oldestStart)
                {
                    oldestStart = _emitterStartTimes[i];
                    oldestIndex = i;
                }
            }

            return oldestIndex;
        }

        /// <summary>Pre-warms the 16 spatial emitters, 2 UI channels, and procedural fallback clips idempotently.</summary>
        public void EnsurePoolsInitialized()
        {
            if (_isPoolBuilt && _emitters3D != null && _emitters3D.Length == SpatialEmitterPoolSize)
            {
                return;
            }

            _isPoolBuilt = true;
            Build3DEmitterPool();
            Build2DUiPool();
            PopulateClipLibrary();
        }

        private void ConfigureAndFire3DEmitter(int index, AudioClip clip, float volume, Vector3 pos, float now)
        {
            AudioSource src = _emitters3D[index];
            src.Stop();
            src.transform.position = pos;
            src.clip = clip;
            src.volume = volume;
            src.pitch = UnityEngine.Random.Range(MinPitchJitter, MaxPitchJitter);
            src.Play();

            _emitterStartTimes[index] = now;
            float duration = clip.length / Mathf.Max(0.5f, src.pitch);
            _emitterEndTimes[index] = now + Mathf.Max(0.05f, duration);
        }

        private bool TryResolveClip(SoundId id, out AudioClip clip, out float volume)
        {
            volume = _volumeLookup.TryGetValue(id, out float v) ? v : 0.85f;
            if (_clipLookup.TryGetValue(id, out clip) && clip != null)
            {
                return true;
            }

            if (_warnedMissingClips.Add(id))
            {
                Debug.LogWarning($"[SoundManager] Missing AudioClip for SoundId.{id}; skipping playback.");
            }

            clip = null;
            return false;
        }

        private Vector3 ResolveListenerPosition(Vector3? explicitOverride)
        {
            if (explicitOverride.HasValue)
            {
                return explicitOverride.Value;
            }

            return _listenerTransform != null ? _listenerTransform.position : transform.position;
        }

        private void Build3DEmitterPool()
        {
            _emitters3D = new AudioSource[SpatialEmitterPoolSize];
            _emitterStartTimes = new float[SpatialEmitterPoolSize];
            _emitterEndTimes = new float[SpatialEmitterPoolSize];

            for (int i = 0; i < SpatialEmitterPoolSize; i++)
            {
                GameObject go = new GameObject($"Pooled3DEmitter_{i:00}");
                go.transform.SetParent(transform, worldPositionStays: false);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 1.0f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = SpatialMinDistanceMeters;
                src.maxDistance = SpatialMaxDistanceMeters;
                src.dopplerLevel = 0.0f;
                if (_sfx3dMixerGroup != null)
                {
                    src.outputAudioMixerGroup = _sfx3dMixerGroup;
                }

                _emitters3D[i] = src;
            }
        }

        private void Build2DUiPool()
        {
            _channels2D = new AudioSource[UiChannelPoolSize];
            for (int i = 0; i < UiChannelPoolSize; i++)
            {
                GameObject go = new GameObject($"Pooled2DUiChannel_{i:00}");
                go.transform.SetParent(transform, worldPositionStays: false);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0.0f;
                if (_ui2dMixerGroup != null)
                {
                    src.outputAudioMixerGroup = _ui2dMixerGroup;
                }

                _channels2D[i] = src;
            }
        }

        private void PopulateClipLibrary()
        {
            if (_useProceduralFallbacks)
            {
                RegisterProceduralCue(SoundId.VerticalSlash, 320f, 160f, 0.14f, 0.80f);
                RegisterProceduralCue(SoundId.HorizontalSweep, 240f, 110f, 0.20f, 0.85f);
                RegisterProceduralCue(SoundId.RangedFire, 680f, 280f, 0.12f, 0.75f);
                RegisterProceduralCue(SoundId.MeleeHitPlayer, 190f, 90f, 0.15f, 0.92f);
                RegisterProceduralCue(SoundId.ProjectileHitPlayer, 440f, 180f, 0.12f, 0.88f);
                RegisterProceduralCue(SoundId.WallImpact, 520f, 220f, 0.09f, 0.65f);
                RegisterProceduralCue(SoundId.ForwardDash, 280f, 420f, 0.14f, 0.75f);
                RegisterProceduralCue(SoundId.DodgeRoll, 220f, 320f, 0.16f, 0.72f);
                RegisterProceduralCue(SoundId.PlayerEliminated, 560f, 140f, 0.32f, 0.95f);
                RegisterProceduralCue(SoundId.UiClick, 740f, 880f, 0.06f, 0.60f);
            }

            if (_customCues == null)
            {
                return;
            }

            for (int i = 0; i < _customCues.Length; i++)
            {
                if (_customCues[i].Clip != null)
                {
                    _clipLookup[_customCues[i].Id] = _customCues[i].Clip;
                    _volumeLookup[_customCues[i].Id] = Mathf.Clamp01(_customCues[i].Volume);
                }
            }
        }

        private void RegisterProceduralCue(SoundId id, float startHz, float endHz, float durationSec, float volume)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.Max(256, Mathf.RoundToInt(sampleRate * durationSec));
            float[] pcm = new float[sampleCount];
            float phase = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float hz = Mathf.Lerp(startHz, endHz, t);
                phase += (2f * Mathf.PI * hz) / sampleRate;
                float envelope = (1f - t) * (1f - t);
                pcm[i] = Mathf.Sin(phase) * envelope * 0.6f;
            }

            AudioClip clip = AudioClip.Create($"ProcSfx_{id}", sampleCount, 1, sampleRate, stream: false);
            clip.SetData(pcm, 0);
            _proceduralClips.Add(clip);
            _clipLookup[id] = clip;
            _volumeLookup[id] = volume;
        }

        private void HandleMeleeSwing(MeleeSwingEvent evt)
        {
            SoundId id = evt.Kind == MeleeSwingKind.VerticalSlash ? SoundId.VerticalSlash : SoundId.HorizontalSweep;
            Play3D(id, evt.Origin);
        }

        private void HandleRangedShot(Vector3 muzzlePos, Vector3 aimDir, bool blockedByFlushWall)
        {
            Play3D(blockedByFlushWall ? SoundId.WallImpact : SoundId.RangedFire, muzzlePos);
        }

        private void HandleProjectileImpact(Vector3 impactPos, bool hitPlayer)
        {
            if (!hitPlayer)
            {
                Play3D(SoundId.WallImpact, impactPos);
            }
        }

        private void HandleHitFeedback(PlayerHealth victim, Vector3 hitPos, int damage, bool isLethal, PlayerRef attacker)
        {
            if (isLethal)
            {
                Play3D(SoundId.PlayerEliminated, hitPos);
                return;
            }

            SoundId hitCue = damage <= RangedDamageCeiling ? SoundId.ProjectileHitPlayer : SoundId.MeleeHitPlayer;
            Play3D(hitCue, hitPos);
        }

        private void HandleMobilityBurst(PlayerMobilityController controller, MobilityState state, Vector3 origin, Quaternion rotation)
        {
            SoundId cue = state == MobilityState.Dashing ? SoundId.ForwardDash : SoundId.DodgeRoll;
            Play3D(cue, origin);
        }
    }
}
