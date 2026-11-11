using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Combat
{
    /// <summary>
    /// Interface implemented by damageable player capsules so melee and ranged hit detectors
    /// request victim-authoritative damage without coupling to concrete health internals.
    /// </summary>
    public interface IDamageReceiver
    {
        /// <summary>Gets the <see cref="PlayerRef"/> that owns this combatant.</summary>
        PlayerRef OwnerPlayer { get; }

        /// <summary>Gets a value indicating whether this combatant is already eliminated.</summary>
        bool IsDead { get; }

        /// <summary>Gets the authoritative world transform of this combatant.</summary>
        Transform ReceiverTransform { get; }

        /// <summary>
        /// Submits a damage request from an attacker to the victim's StateAuthority.
        /// </summary>
        /// <param name="attacker">Attacker's <see cref="PlayerRef"/>.</param>
        /// <param name="damage">Requested canonical damage amount.</param>
        /// <param name="attackType">Canonical attack enum identifier.</param>
        /// <param name="attackerOrigin">World position of the attacker (or launch muzzle for ranged).</param>
        /// <param name="attackTick">Simulation tick identifier for RPC idempotency (0 in standalone tests).</param>
        /// <param name="hitPoint">World impact coordinate for hit VFX/SFX feedback.</param>
        void RequestDamageFromAttacker(
            PlayerRef attacker,
            int damage,
            AttackType attackType,
            Vector3 attackerOrigin,
            int attackTick = 0,
            Vector3 hitPoint = default);
    }

    /// <summary>
    /// Zero-allocation O(1) lookup registry mapping <see cref="Collider"/> <see cref="EntityId"/>s and
    /// <see cref="PlayerRef"/>s to <see cref="IDamageReceiver"/> instances so 60Hz hit detection inside
    /// <c>FixedUpdateNetwork()</c> never calls <c>GetComponent</c> or allocates GC memory (ADR-0003, ADR-0004).
    /// </summary>
    public static class DamageReceiverRegistry
    {
        private static readonly Dictionary<EntityId, IDamageReceiver> ByColliderId =
            new Dictionary<EntityId, IDamageReceiver>(32);

        private static readonly Dictionary<PlayerRef, IDamageReceiver> ByPlayerRef =
            new Dictionary<PlayerRef, IDamageReceiver>(8);

        private static readonly List<IDamageReceiver> ActiveReceivers =
            new List<IDamageReceiver>(8);

        /// <summary>Raised whenever a damageable avatar registers with the static registry.</summary>
        public static event System.Action<IDamageReceiver> OnReceiverRegistered;

        /// <summary>Raised whenever a networked <see cref="PlayerRef"/> owner is assigned to a registered receiver.</summary>
        public static event System.Action<PlayerRef, IDamageReceiver> OnReceiverOwnerAssigned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticRegistry()
        {
            OnReceiverRegistered = null;
            OnReceiverOwnerAssigned = null;
            ClearAll();
        }

        /// <summary>Registers all colliders belonging to <paramref name="root"/> with <paramref name="receiver"/>.</summary>
        public static void Register(GameObject root, IDamageReceiver receiver)
        {
            if (root == null || receiver == null)
            {
                return;
            }

            bool newlyAdded = !ActiveReceivers.Contains(receiver);
            if (newlyAdded)
            {
                ActiveReceivers.Add(receiver);
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    ByColliderId[colliders[i].GetEntityId()] = receiver;
                }
            }

            if (receiver.OwnerPlayer != PlayerRef.None)
            {
                ByPlayerRef[receiver.OwnerPlayer] = receiver;
            }

            if (newlyAdded)
            {
                OnReceiverRegistered?.Invoke(receiver);
            }
        }

        /// <summary>Updates the <see cref="PlayerRef"/> association once <c>Spawned()</c> or test setup runs.</summary>
        public static void RegisterPlayerOwner(PlayerRef owner, IDamageReceiver receiver)
        {
            if (owner != PlayerRef.None && receiver != null)
            {
                ByPlayerRef[owner] = receiver;
                OnReceiverOwnerAssigned?.Invoke(owner, receiver);
            }
        }

        /// <summary>Unregisters all colliders and player mappings for <paramref name="receiver"/>.</summary>
        public static void Unregister(GameObject root, IDamageReceiver receiver)
        {
            if (receiver != null)
            {
                ActiveReceivers.Remove(receiver);
                if (receiver.OwnerPlayer != PlayerRef.None &&
                    ByPlayerRef.TryGetValue(receiver.OwnerPlayer, out IDamageReceiver current) &&
                    ReferenceEquals(current, receiver))
                {
                    ByPlayerRef.Remove(receiver.OwnerPlayer);
                }
            }

            if (root == null)
            {
                return;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    ByColliderId.Remove(colliders[i].GetEntityId());
                }
            }
        }

        /// <summary>Resolves an <see cref="IDamageReceiver"/> by collider <see cref="EntityId"/> in O(1) with 0 B GC.</summary>
        public static bool TryGetByCollider(Collider collider, out IDamageReceiver receiver)
        {
            if (collider == null)
            {
                receiver = null;
                return false;
            }

            return ByColliderId.TryGetValue(collider.GetEntityId(), out receiver) && receiver != null;
        }

        /// <summary>Resolves an active <see cref="IDamageReceiver"/> by <see cref="PlayerRef"/>.</summary>
        public static bool TryGetByPlayer(PlayerRef player, out IDamageReceiver receiver)
        {
            if (player == PlayerRef.None)
            {
                receiver = null;
                return false;
            }

            return ByPlayerRef.TryGetValue(player, out receiver) && receiver != null;
        }

        /// <summary>
        /// Fills <paramref name="buffer"/> with world positions of currently living combatants (excluding <paramref name="exclude"/>)
        /// with zero heap allocations, returning the written count.
        /// </summary>
        public static int CopyLivingPositionsNonAlloc(Vector3[] buffer, IDamageReceiver exclude = null)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return 0;
            }

            int written = 0;
            for (int i = 0; i < ActiveReceivers.Count && written < buffer.Length; i++)
            {
                IDamageReceiver candidate = ActiveReceivers[i];
                if (candidate == null || ReferenceEquals(candidate, exclude) || candidate.IsDead)
                {
                    continue;
                }

                Transform tr = candidate.ReceiverTransform;
                if (tr != null)
                {
                    buffer[written++] = tr.position;
                }
            }

            return written;
        }

        /// <summary>
        /// Copies all registered receivers of type <typeparamref name="T"/> into <paramref name="destination"/>
        /// (excluding <paramref name="exclude"/>) with 0 B heap allocation.
        /// </summary>
        public static int CopyTypedReceiversNonAlloc<T>(List<T> destination, T exclude = null)
            where T : UnityEngine.Object, IDamageReceiver
        {
            if (destination == null)
            {
                return 0;
            }

            destination.Clear();
            for (int i = 0; i < ActiveReceivers.Count; i++)
            {
                if (ActiveReceivers[i] is T typed && typed != null && !ReferenceEquals(typed, exclude))
                {
                    destination.Add(typed);
                }
            }

            return destination.Count;
        }

        /// <summary>Clears all static registrations (called on domain/test reset).</summary>
        public static void ClearAll()
        {
            ByColliderId.Clear();
            ByPlayerRef.Clear();
            ActiveReceivers.Clear();
        }
    }
}
