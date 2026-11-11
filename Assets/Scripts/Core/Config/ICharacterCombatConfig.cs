using UnityEngine;

namespace CapsuleArena.Core.Config
{
    /// <summary>
    /// Read-only contract exposing character combat, mobility, presentation, and
    /// precomputed 60Hz tick/trigonometric values for dependency injection and unit testing.
    /// </summary>
    public interface ICharacterCombatConfig
    {
        /// <summary>Gets the maximum hit points at spawn (canonical: 100).</summary>
        int MaxHealth { get; }

        /// <summary>Gets the alias for maximum hit points matching TR-config-003 (canonical: 100).</summary>
        int MaxHP { get; }

        /// <summary>Gets the base horizontal movement speed in meters per second (canonical: 6.0).</summary>
        float MoveSpeed { get; }

        /// <summary>Gets the sprint speed multiplier relative to MoveSpeed (canonical: 1.35).</summary>
        float SprintMultiplier { get; }

        /// <summary>Gets the target apex jump height in meters (canonical: 1.5).</summary>
        float JumpHeight { get; }

        /// <summary>Gets the downward gravitational acceleration in m/s^2 (canonical: -19.62).</summary>
        float Gravity { get; }

        /// <summary>Gets the base yaw/pitch look sensitivity multiplier (canonical: 1.0).</summary>
        float RotationSensitivity { get; }

        /// <summary>Gets the Forward Dash horizontal speed in m/s (canonical: 18.0).</summary>
        float DashSpeed { get; }

        /// <summary>Gets the Forward Dash active duration in seconds (canonical: 0.20).</summary>
        float DashDurationSec { get; }

        /// <summary>Gets the Forward Dash cooldown in seconds (canonical: 3.0).</summary>
        float DashCooldownSec { get; }

        /// <summary>Gets the Lateral Dodge Roll horizontal speed in m/s (canonical: 12.0).</summary>
        float DodgeSpeed { get; }

        /// <summary>Gets the Lateral Dodge Roll active duration in seconds (canonical: 0.25).</summary>
        float DodgeDurationSec { get; }

        /// <summary>Gets the shared Lateral Dodge Roll cooldown in seconds (canonical: 1.5).</summary>
        float DodgeCooldownSec { get; }

        /// <summary>Gets the Vertical Slash (LMB) damage per hit (canonical: 25).</summary>
        int VerticalSlashDamage { get; }

        /// <summary>Gets the Vertical Slash reach radius in meters (canonical: 2.5).</summary>
        float VerticalSlashRange { get; }

        /// <summary>Gets the full cone angle of Vertical Slash in degrees (canonical: 50.0).</summary>
        float VerticalSlashAngleDeg { get; }

        /// <summary>Gets the Vertical Slash cooldown in seconds (canonical: 0.6).</summary>
        float VerticalSlashCooldownSec { get; }

        /// <summary>Gets the Horizontal Sweep (Key 1/3) damage per hit (canonical: 30).</summary>
        int HorizontalSweepDamage { get; }

        /// <summary>Gets the Horizontal Sweep reach radius in meters (canonical: 3.0).</summary>
        float HorizontalSweepRange { get; }

        /// <summary>Gets the full cone angle of Horizontal Sweep in degrees (canonical: 90.0).</summary>
        float HorizontalSweepAngleDeg { get; }

        /// <summary>Gets the yaw rotation applied across Horizontal Sweep in degrees (canonical: 90.0).</summary>
        float HorizontalSweepTurnDeg { get; }

        /// <summary>Gets the duration of the 90-degree yaw turn in seconds (canonical: 0.18).</summary>
        float HorizontalSweepTurnDurationSec { get; }

        /// <summary>Gets the shared Horizontal Sweep cooldown in seconds (canonical: 1.2).</summary>
        float HorizontalSweepCooldownSec { get; }

        /// <summary>Gets the Ranged Fire (RMB) damage per hit (canonical: 15).</summary>
        int RangedDamage { get; }

        /// <summary>Gets the Ranged Projectile linear speed in m/s (canonical: 15.0).</summary>
        float RangedProjectileSpeed { get; }

        /// <summary>Gets the maximum travel distance of a ranged bolt in meters (canonical: 30.0).</summary>
        float RangedMaxDistance { get; }

        /// <summary>Gets the sphere-cast collision radius of a ranged bolt in meters (canonical: 0.20).</summary>
        float RangedProjectileRadius { get; }

        /// <summary>Gets the Ranged Fire cooldown in seconds (canonical: 0.4).</summary>
        float RangedFireCooldownSec { get; }

        /// <summary>Gets the TPS camera boom distance in meters (canonical: 4.5).</summary>
        float TpsCameraDistance { get; }

        /// <summary>Gets the TPS camera vertical pivot height in meters (canonical: 1.6).</summary>
        float TpsCameraHeight { get; }

        /// <summary>Gets the hit-flash emission duration in seconds (canonical: 0.08).</summary>
        float HitFlashDurationSec { get; }

        /// <summary>Gets the squash-and-stretch tween duration in seconds (canonical: 0.18).</summary>
        float SquashStretchDurationSec { get; }

        /// <summary>Gets the visual stretch scale applied during dashes/jumps.</summary>
        Vector3 VisualStretchScale { get; }

        /// <summary>Gets the visual squash scale applied on landing/impact.</summary>
        Vector3 VisualSquashScale { get; }

        /// <summary>Gets the visual tilt angle in degrees for mobility lean cues.</summary>
        float DodgeVisualTiltAngle { get; }

        /// <summary>Gets the precomputed Forward Dash tick count at 60Hz (canonical: 12).</summary>
        int DashTicks { get; }

        /// <summary>Gets the precomputed Forward Dash cooldown tick count at 60Hz (canonical: 180).</summary>
        int DashCooldownTicks { get; }

        /// <summary>Gets the precomputed Dodge Roll tick count at 60Hz (canonical: 15).</summary>
        int DodgeTicks { get; }

        /// <summary>Gets the precomputed Dodge Roll cooldown tick count at 60Hz (canonical: 90).</summary>
        int DodgeCooldownTicks { get; }

        /// <summary>Gets the precomputed Horizontal Sweep turn tick count at 60Hz (canonical: 11).</summary>
        int HorizontalTurnTicks { get; }

        /// <summary>Gets the precomputed projectile lifetime in seconds (RangedMaxDistance / RangedProjectileSpeed).</summary>
        float ProjectileLifetimeSec { get; }

        /// <summary>Gets the precomputed projectile lifetime in ticks at 60Hz (canonical: 120).</summary>
        int ProjectileLifetimeTicks { get; }

        /// <summary>Gets cos(VerticalSlashAngleDeg * 0.5f * Deg2Rad) (canonical: 0.9063078).</summary>
        float VerticalSlashCosHalfAngle { get; }

        /// <summary>Gets cos(HorizontalSweepAngleDeg * 0.5f * Deg2Rad) (canonical: 0.7071068).</summary>
        float HorizontalSweepCosHalfAngle { get; }

        /// <summary>Gets VerticalSlashRange squared for sqrt-free distance checks (canonical: 6.25).</summary>
        float VerticalSlashRangeSqr { get; }

        /// <summary>Gets HorizontalSweepRange squared for sqrt-free distance checks (canonical: 9.0).</summary>
        float HorizontalSweepRangeSqr { get; }

        /// <summary>Gets initial vertical jump impulse sqrt(2 * |Gravity| * JumpHeight) (canonical: ~7.672).</summary>
        float JumpInitialVelocity { get; }

        /// <summary>
        /// Returns the configured cooldown duration in seconds for the specified skill.
        /// </summary>
        /// <param name="skillId">Skill identifier.</param>
        /// <returns>Cooldown duration in seconds.</returns>
        float GetCooldown(CombatSkillId skillId);
    }
}
