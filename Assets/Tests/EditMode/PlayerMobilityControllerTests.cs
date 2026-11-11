using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Player;
using NUnit.Framework;
using UnityEngine;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit &amp; physics integration tests verifying 60Hz tick-accurate walk (6.0 m/s),
    /// diagonal speed clamp, single jump impulse (7.672 m/s -&gt; 1.50m apex) + double-jump rejection
    /// + Coyote Time + Jump Buffer, out-of-bounds Y &lt; -5.0m teleport, Forward Dash (Key 2: 12 ticks,
    /// 3.60m, 3.0s cooldown, physical Layer 6 CapsuleCastNonAlloc wall-slide), and Lateral Dodge Roll
    /// (Keys Q/E: 15 ticks, +-3.00m, +-360 deg Z-roll, 1.5s shared cooldown, Dash/Dodge mutual exclusion,
    /// and Ghost burst lock) (Stories 5-1, 5-2, 5-3; ADR-0004; TR-move-001..003).
    /// </summary>
    [TestFixture]
    public class PlayerMobilityControllerTests
    {
        private const float FixedDt = 1f / 60f;

        private CharacterCombatConfigSO _config;
        private GameObject _playerGo;
        private Transform _visualRoot;
        private Transform _cameraPivot;
        private PlayerMobilityController _mobility;

        [SetUp]
        public void SetUp()
        {
            ArenaLayers.ApplyCollisionMatrix();
            _config = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();

            _playerGo = new GameObject("PlayerCapsule_Test");
            _visualRoot = new GameObject("VisualRoot").transform;
            _visualRoot.SetParent(_playerGo.transform, false);

            _cameraPivot = new GameObject("CameraPivot").transform;
            _cameraPivot.SetParent(_playerGo.transform, false);

            _mobility = _playerGo.AddComponent<PlayerMobilityController>();
            _mobility.ConfigureReferences(_config, controller: null, _visualRoot, _cameraPivot);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void WalkForwardAndDiagonal_SixtyTicksAt60Hz_CoversExactly6MetersWithoutDiagonalBoostAndRecoversOob()
        {
            _playerGo.transform.position = new Vector3(0f, 25f, 0f);
            NetworkInputData cardinal = new NetworkInputData
            {
                MoveDirection = Vector2.up,
                LookYaw = 0f,
                LookPitch = -15f
            };

            for (int tick = 0; tick < 60; tick++)
            {
                _mobility.SimulateAuthoritativeTick(in cardinal, FixedDt, _config, groundedOverride: true);
            }

            Assert.AreEqual(6.0f, _playerGo.transform.position.z, 0.01f, "60 ticks at 6.0 m/s must equal 6.0m.");
            Assert.AreEqual(0f, _cameraPivot.localEulerAngles.z, 0.001f);

            // Diagonal WASD (1, 1): planar displacement over 60 ticks must STILL be 6.0m (no 1.414x speed exploit)!
            _playerGo.transform.position = new Vector3(0f, 25f, 0f);
            NetworkInputData diagonal = new NetworkInputData
            {
                MoveDirection = new Vector2(1f, 1f),
                LookYaw = 0f
            };

            for (int tick = 0; tick < 60; tick++)
            {
                _mobility.SimulateAuthoritativeTick(in diagonal, FixedDt, _config, groundedOverride: true);
            }

            Vector2 planarDelta = new Vector2(_playerGo.transform.position.x, _playerGo.transform.position.z);
            Assert.AreEqual(6.0f, planarDelta.magnitude, 0.015f, "Normalized diagonal WASD over 60 ticks must travel strictly 6.0m.");

            // Force Y below -5.0m -> must recover back above Y >= 0.0m!
            _playerGo.transform.position = new Vector3(0f, -6.5f, 0f);
            _mobility.SimulateAuthoritativeTick(in cardinal, FixedDt, _config, groundedOverride: false);
            Assert.GreaterOrEqual(_playerGo.transform.position.y, 0.0f, "Out-of-bounds capsule must teleport back to safe spawn.");
        }

        [Test]
        public void JumpAndGravity_GroundedImpulseReaches1Point5MeterApexRejectsDoubleJumpAndHonorsCoyoteAndBuffer()
        {
            _playerGo.transform.position = new Vector3(0f, 0f, 0f);

            NetworkInputData jumpPress = new NetworkInputData { LookYaw = 0f };
            jumpPress.Buttons.Set((int)NetworkInputButtons.Jump, true);
            NetworkInputData airborneNoInput = new NetworkInputData { LookYaw = 0f };

            // Tick 0: grounded jump launch:
            _mobility.SimulateAuthoritativeTick(in jumpPress, FixedDt, _config, groundedOverride: true);
            Assert.AreEqual(7.672f, _mobility.LastJumpLaunchVelocity, 0.01f, "Initial jump velocity must match sqrt(2 * 19.62 * 1.5) = 7.672 m/s.");

            float maxApexY = _playerGo.transform.position.y;
            for (int tick = 1; tick <= 50; tick++)
            {
                // Attempt double-jump mid-air at tick 10 -> must be rejected!
                NetworkInputData stepInput = tick == 10 ? jumpPress : airborneNoInput;
                _mobility.SimulateAuthoritativeTick(in stepInput, FixedDt, _config, groundedOverride: false);
                if (_playerGo.transform.position.y > maxApexY)
                {
                    maxApexY = _playerGo.transform.position.y;
                }
            }

            Assert.AreEqual(1.50f, maxApexY, 0.03f, "Single jump apex must reach 1.50m +- 0.03m without mid-air double jump.");

            // Verify Coyote Time (step off ledge: grounded on tick 0, ungrounded for 4 ticks <= 6-tick grace, press Jump on tick 4):
            _playerGo.transform.position = new Vector3(0f, 10f, 0f);
            _mobility.SimulateAuthoritativeTick(in airborneNoInput, FixedDt, _config, groundedOverride: true);
            for (int i = 0; i < 3; i++)
            {
                _mobility.SimulateAuthoritativeTick(in airborneNoInput, FixedDt, _config, groundedOverride: false);
            }

            _mobility.SimulateAuthoritativeTick(in jumpPress, FixedDt, _config, groundedOverride: false);
            Assert.Greater(_mobility.CurrentVerticalVelocity, 6.0f, "Coyote Time within 0.10s (6 ticks) must allow ledge jump.");

            // Verify Jump Buffer (press Jump 3 ticks before touching ground while falling -> launches automatically on landing tick):
            _playerGo.transform.position = new Vector3(0f, 5f, 0f);
            for (int i = 0; i < 30; i++)
            {
                _mobility.SimulateAuthoritativeTick(in airborneNoInput, FixedDt, _config, groundedOverride: false);
            }

            _mobility.SimulateAuthoritativeTick(in jumpPress, FixedDt, _config, groundedOverride: false);
            _mobility.SimulateAuthoritativeTick(in airborneNoInput, FixedDt, _config, groundedOverride: false);
            _mobility.SimulateAuthoritativeTick(in airborneNoInput, FixedDt, _config, groundedOverride: true);
            Assert.Greater(_mobility.CurrentVerticalVelocity, 6.0f, "Buffered jump within 0.10s before landing must fire on touchdown.");
        }

        [Test]
        public void ForwardDash_Locks12TicksCovers3Point6mSlidesAlongPhysicalLayer6WallAndHonors180TickCooldown()
        {
            _playerGo.transform.position = new Vector3(0f, 20f, 0f);

            NetworkInputData dashTrigger = new NetworkInputData { MoveDirection = Vector2.up, LookYaw = 0f };
            dashTrigger.Buttons.Set((int)NetworkInputButtons.ForwardDash, true);
            // Press DodgeLeft on same tick as ForwardDash -> ForwardDash must take deterministic priority!
            dashTrigger.Buttons.Set((int)NetworkInputButtons.DodgeLeft, true);

            _mobility.SimulateAuthoritativeTick(in dashTrigger, FixedDt, _config, groundedOverride: true);
            Assert.AreEqual(MobilityState.Dashing, _mobility.CurrentMobilityState);
            Assert.AreEqual(PlayerMobilityController.DashVisualPitchDeg, _visualRoot.localEulerAngles.x, 0.01f);

            // Attempt mid-dash steering + DodgeRight during active Dash -> ignored!
            NetworkInputData midDashSteer = new NetworkInputData { MoveDirection = Vector2.right, LookYaw = 90f };
            midDashSteer.Buttons.Set((int)NetworkInputButtons.DodgeRight, true);
            for (int tick = 1; tick < 12; tick++)
            {
                _mobility.SimulateAuthoritativeTick(in midDashSteer, FixedDt, _config, groundedOverride: true);
            }

            Assert.AreEqual(MobilityState.Normal, _mobility.CurrentMobilityState, "Dash must end cleanly after 12th tick.");
            Assert.AreEqual(3.60f, _playerGo.transform.position.z, 0.01f, "12 ticks at 18.0 m/s must cover 3.60m along locked +Z.");
            Assert.AreEqual(0f, _playerGo.transform.position.x, 0.01f, "Mid-dash steering must not alter locked dash vector.");
            Assert.AreEqual(169, _mobility.RemainingDashCooldownTicks, "180 cooldown ticks minus 11 elapsed ticks = 169 remaining.");

            // Tick 179 (1 tick before 180-tick expiry) -> still blocked; Tick 180 -> Dash ready again:
            NetworkInputData idle = new NetworkInputData { LookYaw = 45f };
            for (int i = 0; i < 167; i++)
            {
                _mobility.SimulateAuthoritativeTick(in idle, FixedDt, _config, groundedOverride: true);
            }

            Assert.AreEqual(2, _mobility.RemainingDashCooldownTicks);
            NetworkInputData dashOnly = new NetworkInputData { LookYaw = 0f };
            dashOnly.Buttons.Set((int)NetworkInputButtons.ForwardDash, true);
            _mobility.SimulateAuthoritativeTick(in dashOnly, FixedDt, _config, groundedOverride: true);
            Assert.AreEqual(1, _mobility.RemainingDashCooldownTicks);
            Assert.AreEqual(MobilityState.Normal, _mobility.CurrentMobilityState, "Tick 179 (1 tick remaining) must still reject Dash.");

            // Next tick clears cooldown (0 remaining) and physical Layer 6 wall at z = 5.0m tests CapsuleCastNonAlloc 45-deg slide!
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = ArenaLayers.EnvironmentWall;
                wall.transform.position = new Vector3(0f, 21f, 4.45f);
                wall.transform.localScale = new Vector3(10f, 4f, 0.5f);
                Physics.SyncTransforms();

                NetworkInputData angledDash = new NetworkInputData { LookYaw = 45f };
                angledDash.Buttons.Set((int)NetworkInputButtons.ForwardDash, true);

                float startZ = _playerGo.transform.position.z;
                float startX = _playerGo.transform.position.x;
                _mobility.SimulateAuthoritativeTick(in angledDash, FixedDt, _config, groundedOverride: true);
                Assert.AreEqual(MobilityState.Dashing, _mobility.CurrentMobilityState, "Dash must succeed once 180-tick cooldown expires.");

                for (int t = 1; t < 12; t++)
                {
                    _mobility.SimulateAuthoritativeTick(in idle, FixedDt, _config, groundedOverride: true);
                }

                Assert.Greater(_playerGo.transform.position.x - startX, 1.5f, "45-deg Dash against Layer 6 wall must slide tangentially along +X.");
                Assert.Less(_playerGo.transform.position.z, 4.25f, "CapsuleCastNonAlloc must prevent penetrating the Layer 6 wall.");
                Assert.GreaterOrEqual(_playerGo.transform.position.z, startZ - 0.01f, "Wall impact must not bounce backward.");
            }
            finally
            {
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void DodgeRoll_LeftAndRightAndGhostSuppression_Covers3MetersShares90TickCooldownAndPreservesCameraRoll()
        {
            _playerGo.transform.position = new Vector3(0f, 20f, 0f);

            // Q + E simultaneous -> DodgeLeft (-3.00m):
            NetworkInputData bothPressed = new NetworkInputData { LookYaw = 0f, LookPitch = 10f };
            bothPressed.Buttons.Set((int)NetworkInputButtons.DodgeLeft, true);
            bothPressed.Buttons.Set((int)NetworkInputButtons.DodgeRight, true);

            _mobility.SimulateAuthoritativeTick(in bothPressed, FixedDt, _config, groundedOverride: true);
            Assert.AreEqual(MobilityState.DodgingLeft, _mobility.CurrentMobilityState);
            Assert.AreEqual(Quaternion.Euler(0f, 0f, 0f), _playerGo.transform.rotation);
            Assert.AreEqual(0f, _cameraPivot.localEulerAngles.z, 0.001f);

            // Attempt ForwardDash mid-dodge -> rejected by mutual exclusion!
            NetworkInputData midDodgeDash = new NetworkInputData { LookYaw = 0f, LookPitch = 10f };
            midDodgeDash.Buttons.Set((int)NetworkInputButtons.ForwardDash, true);
            for (int tick = 1; tick < 15; tick++)
            {
                _mobility.SimulateAuthoritativeTick(in midDodgeDash, FixedDt, _config, groundedOverride: true);
            }

            Assert.AreEqual(MobilityState.Normal, _mobility.CurrentMobilityState);
            Assert.AreEqual(-3.00f, _playerGo.transform.position.x, 0.01f);
            Assert.AreEqual(Quaternion.identity, _visualRoot.localRotation);

            // DodgeRight (E) immediately after DodgeLeft must be blocked by the shared 1.5s (90-tick) cooldown:
            NetworkInputData dodgeRight = new NetworkInputData { LookYaw = 0f, LookPitch = -5f };
            dodgeRight.Buttons.Set((int)NetworkInputButtons.DodgeRight, true);
            _mobility.SimulateAuthoritativeTick(in dodgeRight, FixedDt, _config, groundedOverride: true);
            Assert.AreEqual(MobilityState.Normal, _mobility.CurrentMobilityState, "Shared 90-tick Dodge cooldown must block E right after Q.");

            // Advance remaining cooldown ticks, then trigger DodgeRight (+3.00m back to X = 0, -360 deg Z roll):
            NetworkInputData neutral = new NetworkInputData { LookYaw = 0f, LookPitch = -5f };
            while (_mobility.RemainingDodgeCooldownTicks > 0)
            {
                _mobility.SimulateAuthoritativeTick(in neutral, FixedDt, _config, groundedOverride: true);
            }

            _mobility.SimulateAuthoritativeTick(in dodgeRight, FixedDt, _config, groundedOverride: true);
            Assert.AreEqual(MobilityState.DodgingRight, _mobility.CurrentMobilityState);
            Assert.AreEqual(0f, _cameraPivot.localEulerAngles.z, 0.001f);

            for (int tick = 1; tick < 15; tick++)
            {
                _mobility.SimulateAuthoritativeTick(in neutral, FixedDt, _config, groundedOverride: true);
            }

            Assert.AreEqual(0f, _playerGo.transform.position.x, 0.01f, "15 ticks of DodgeRight at 12 m/s must displace +3.00m back to X=0.");
            Assert.AreEqual(Quaternion.identity, _visualRoot.localRotation);

            // Ghost Spectator mode must suppress Jump, Dash, and Dodge while still allowing 6.0 m/s WASD roam:
            _mobility.SetGhostSpectatorMode(true);
            while (_mobility.RemainingDodgeCooldownTicks > 0)
            {
                _mobility.SimulateAuthoritativeTick(in neutral, FixedDt, _config, groundedOverride: true);
            }

            NetworkInputData ghostTryBurst = new NetworkInputData { MoveDirection = Vector2.up, LookYaw = 0f };
            ghostTryBurst.Buttons.Set((int)NetworkInputButtons.ForwardDash, true);
            ghostTryBurst.Buttons.Set((int)NetworkInputButtons.DodgeLeft, true);
            ghostTryBurst.Buttons.Set((int)NetworkInputButtons.Jump, true);

            float zBefore = _playerGo.transform.position.z;
            _mobility.SimulateAuthoritativeTick(in ghostTryBurst, FixedDt, _config, groundedOverride: true);
            Assert.AreEqual(MobilityState.Normal, _mobility.CurrentMobilityState, "Ghost mode must block Dash and Dodge bursts.");
            Assert.LessOrEqual(_mobility.CurrentVerticalVelocity, 0f, "Ghost mode must block Jump impulses.");
            Assert.AreEqual(0.10f, _playerGo.transform.position.z - zBefore, 0.005f, "Ghost still walks smoothly at 6.0 m/s (0.10m/tick).");
        }
    }
}
