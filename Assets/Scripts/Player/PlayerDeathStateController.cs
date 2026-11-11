using System;
using System.Text;
using CapsuleArena.Core.Config;
using TMPro;
using UnityEngine;

namespace CapsuleArena.Player
{
    /// <summary>
    /// Transitions eliminated capsules onto Layer 10 (<see cref="ArenaLayers.GhostSpectator"/>),
    /// swaps body renderers to translucent <c>#A8DADC</c> (alpha 0.35), cancels mobility tilts,
    /// disables combat hitboxes, hides the overhead HP bar and local Mobile Action Pad, dims the
    /// nameplate to 50% opacity with <c>[GHOST]</c>, and fires elimination VFX/SFX &amp; spectator hooks
    /// without despawning the <see cref="Fusion.NetworkObject"/> (Story 6-3; ADR-0004; TR-health-003).
    /// </summary>
    public sealed class PlayerDeathStateController : MonoBehaviour, IPlayerDeathStateController
    {
        /// <summary>Tag appended to eliminated spectator nameplates.</summary>
        public const string GhostSuffix = " [GHOST]";

        /// <summary>Nameplate opacity applied when the combatant enters Ghost Spectator mode (50%).</summary>
        public const float GhostNameplateAlpha = 0.50f;

        [Header("Visual & UI References")]
        [Tooltip("Body mesh renderers swapped between opaque alive and translucent ghost materials.")]
        [SerializeField] private Renderer[] _bodyRenderers = Array.Empty<Renderer>();

        [Tooltip("Opaque URP Lit material applied while the combatant is alive.")]
        [SerializeField] private Material _aliveMaterial;

        [Tooltip("Translucent URP Lit material (#A8DADC, Alpha = 0.35) applied on elimination.")]
        [SerializeField] private Material _ghostMaterial;

        [Tooltip("Optional overhead world-space HP bar root hidden upon death.")]
        [SerializeField] private GameObject _overheadHealthBarRoot;

        [Tooltip("Optional overhead TextMeshPro nameplate label.")]
        [SerializeField] private TMP_Text _overheadNameplateText;

        [Tooltip("Optional child weapon/combat hitbox container disabled upon elimination.")]
        [SerializeField] private GameObject _combatHitboxRoot;

        [Tooltip("Optional local player's 7-button Mobile Action Pad CanvasGroup hidden on death.")]
        [SerializeField] private CanvasGroup _localMobileActionPad;

        [Tooltip("Mobility controller notified to cancel Dash/Dodge and suppress combat bursts.")]
        [SerializeField] private PlayerMobilityController _mobilityController;

        private readonly StringBuilder _nameBuilder = new StringBuilder(48);
        private string _basePlayerName = "Player";
        private bool _isInGhostState;
        private bool _isCombatInputLocked;
        private bool _isLocalPlayerAvatar = true;

        /// <summary>Raised at the world elimination position so pooled VFX/SFX systems play death burst feedback.</summary>
        public event Action<Vector3> OnEliminationEffectsRequested;

        /// <summary>Raised on the local peer when its capsule transitions into Ghost Spectator mode.</summary>
        public event Action OnLocalSpectatorModeEntered;

        /// <inheritdoc />
        public bool IsInGhostState => _isInGhostState;

        /// <inheritdoc />
        public bool IsCombatInputLocked => _isCombatInputLocked;

        /// <summary>Gets the current formatted nameplate text for UI/test inspection.</summary>
        public string CurrentFormattedNameplate { get; private set; } = "Player";

        /// <summary>Gets the current nameplate alpha value in [0, 1].</summary>
        public float CurrentNameplateAlpha { get; private set; } = 1.0f;

        private void Awake()
        {
            if (_mobilityController == null)
            {
                TryGetComponent(out _mobilityController);
            }

            if (_bodyRenderers == null || _bodyRenderers.Length == 0)
            {
                _bodyRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }
        }

        /// <summary>Configures dependencies programmatically for prefab builders, runtime HUD binding, and unit tests.</summary>
        public void ConfigureReferences(
            Renderer[] renderers,
            Material aliveMaterial,
            Material ghostMaterial,
            GameObject overheadHpBar,
            TMP_Text nameplateText,
            PlayerMobilityController mobilityController,
            string basePlayerName = "Player",
            GameObject combatHitboxRoot = null,
            CanvasGroup localMobileActionPad = null,
            bool isLocalPlayerAvatar = true)
        {
            _bodyRenderers = renderers ?? Array.Empty<Renderer>();
            _aliveMaterial = aliveMaterial;
            _ghostMaterial = ghostMaterial;
            _overheadHealthBarRoot = overheadHpBar;
            _overheadNameplateText = nameplateText;
            _mobilityController = mobilityController;
            _combatHitboxRoot = combatHitboxRoot;
            _localMobileActionPad = localMobileActionPad;
            _isLocalPlayerAvatar = isLocalPlayerAvatar;

            if (!string.IsNullOrWhiteSpace(basePlayerName))
            {
                _basePlayerName = basePlayerName;
            }

            CurrentFormattedNameplate = _basePlayerName;
            CurrentNameplateAlpha = 1.0f;
        }

        /// <summary>Sets whether this avatar belongs to the local peer for HUD/Action Pad gating.</summary>
        public void SetIsLocalPlayerAvatar(bool isLocalPlayer)
        {
            _isLocalPlayerAvatar = isLocalPlayer;
        }

        /// <inheritdoc />
        public void ApplyDeathState(bool isAuthoritativeSimulation = true)
        {
            bool wasAlreadyGhost = _isInGhostState;
            _isInGhostState = true;
            _isCombatInputLocked = true;

            ArenaLayers.SetLayerRecursively(gameObject, ArenaLayers.GhostSpectator);
            ApplyMaterialToRenderers(_ghostMaterial);
            NotifyMobilityGhostTransition(isGhost: true, isAuthoritativeSimulation);
            ApplyGhostUiAndHitboxState(isGhost: true);
            UpdateNameplateLabel(appendGhostTag: true, alpha: GhostNameplateAlpha);

            if (!wasAlreadyGhost)
            {
                OnEliminationEffectsRequested?.Invoke(transform.position);
                if (_isLocalPlayerAvatar)
                {
                    OnLocalSpectatorModeEntered?.Invoke();
                }
            }
        }

        /// <inheritdoc />
        public void RestoreAliveState(string overrideBaseName = null, bool isAuthoritativeSimulation = true)
        {
            if (!string.IsNullOrWhiteSpace(overrideBaseName))
            {
                _basePlayerName = overrideBaseName;
            }

            _isInGhostState = false;
            _isCombatInputLocked = false;

            ArenaLayers.SetLayerRecursively(gameObject, ArenaLayers.PlayerCharacter);
            ApplyMaterialToRenderers(_aliveMaterial);
            NotifyMobilityGhostTransition(isGhost: false, isAuthoritativeSimulation);
            ApplyGhostUiAndHitboxState(isGhost: false);
            UpdateNameplateLabel(appendGhostTag: false, alpha: 1.0f);
        }

        private void NotifyMobilityGhostTransition(bool isGhost, bool isAuthoritativeSimulation)
        {
            if (_mobilityController == null)
            {
                return;
            }

            if (isAuthoritativeSimulation)
            {
                _mobilityController.SetGhostSpectatorMode(isGhost);
            }
            else
            {
                _mobilityController.ApplyGhostVisualReset();
            }
        }

        private void ApplyGhostUiAndHitboxState(bool isGhost)
        {
            if (_overheadHealthBarRoot != null)
            {
                _overheadHealthBarRoot.SetActive(!isGhost);
            }

            if (_combatHitboxRoot != null)
            {
                _combatHitboxRoot.SetActive(!isGhost);
            }

            if (_isLocalPlayerAvatar && _localMobileActionPad != null)
            {
                _localMobileActionPad.alpha = isGhost ? 0f : 1f;
                _localMobileActionPad.interactable = !isGhost;
                _localMobileActionPad.blocksRaycasts = !isGhost;
            }
        }

        private void ApplyMaterialToRenderers(Material targetMaterial)
        {
            if (targetMaterial == null || _bodyRenderers == null)
            {
                return;
            }

            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                if (_bodyRenderers[i] != null)
                {
                    _bodyRenderers[i].sharedMaterial = targetMaterial;
                }
            }
        }

        private void UpdateNameplateLabel(bool appendGhostTag, float alpha)
        {
            _nameBuilder.Clear();
            _nameBuilder.Append(_basePlayerName);
            if (appendGhostTag)
            {
                _nameBuilder.Append(GhostSuffix);
            }

            CurrentFormattedNameplate = _nameBuilder.ToString();
            CurrentNameplateAlpha = alpha;
            if (_overheadNameplateText != null)
            {
                _overheadNameplateText.text = CurrentFormattedNameplate;
                _overheadNameplateText.alpha = alpha;
            }
        }
    }
}
