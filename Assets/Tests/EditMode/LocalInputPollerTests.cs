using System;
using System.IO;
using System.Runtime.InteropServices;
using CapsuleArena.Core.Input;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode integration tests verifying NetworkInputData 16-byte blittable wire size,
    /// PC + Mobile bitwise OR merging, diagonal vector clamping, +-80 deg pitch clamping,
    /// sub-tick mobile tap latching + multi-tick hold, LocalInputPoller instance collection,
    /// and zero-GC hot-path execution (Story 3-2; ADR-0002; TR-input-002).
    /// </summary>
    [TestFixture]
    public class LocalInputPollerTests
    {
        [Test]
        public void NetworkInputData_MarshalSizeOf_IsStrictly16BytesAndWordAligned()
        {
            int byteSize = Marshal.SizeOf<NetworkInputData>();
            Assert.AreEqual(16, byteSize, "ADR-0002 & control-manifest require NetworkInputData == 16 bytes (4x 32-bit words).");
            Assert.LessOrEqual(byteSize, 16);
            Assert.AreEqual(0, byteSize % 4, "Fusion INetworkInput struct must be 4-byte word-aligned.");
        }

        [Test]
        public void MergeInputs_DiagonalPcAndMobileVectorsWithDisjointButtons_ClampsMagnitudeAndBothPitchBounds()
        {
            NetworkButtons pcButtons = default;
            pcButtons.Set((int)NetworkInputButtons.Jump, true);
            pcButtons.Set((int)NetworkInputButtons.VerticalSlash, true);

            NetworkButtons mobileButtons = default;
            mobileButtons.Set((int)NetworkInputButtons.ForwardDash, true);
            mobileButtons.Set((int)NetworkInputButtons.RangedFire, true);

            NetworkInputData merged = LocalInputPoller.MergeInputs(
                pcMove: new Vector2(1f, 1f),
                pcButtons: pcButtons,
                mobileMove: new Vector2(0.8f, 0.8f),
                mobileButtons: mobileButtons,
                lookYaw: 45f,
                lookPitch: 120f,
                isGameplayAllowed: true);

            Assert.AreEqual(1.0f, merged.MoveDirection.magnitude, 0.001f, "Combined diagonal move must clamp to <= 1.0.");
            Assert.AreEqual(45f, merged.LookYaw, 0.001f);
            Assert.AreEqual(80f, merged.LookPitch, 0.001f, "LookPitch must clamp to +80 degrees.");
            Assert.IsTrue(merged.Buttons.IsSet((int)NetworkInputButtons.Jump));
            Assert.IsTrue(merged.Buttons.IsSet((int)NetworkInputButtons.VerticalSlash));
            Assert.IsTrue(merged.Buttons.IsSet((int)NetworkInputButtons.ForwardDash));
            Assert.IsTrue(merged.Buttons.IsSet((int)NetworkInputButtons.RangedFire));
            Assert.IsFalse(merged.Buttons.IsSet((int)NetworkInputButtons.DodgeLeft));

            NetworkInputData lowerClamped = LocalInputPoller.MergeInputs(
                Vector2.zero, default, Vector2.zero, default, -200f, -120f, true);
            Assert.AreEqual(160f, lowerClamped.LookYaw, 0.001f, "Yaw must normalize cleanly into [-180, +180].");
            Assert.AreEqual(-80f, lowerClamped.LookPitch, 0.001f, "LookPitch must clamp to -80 degrees.");
        }

        [Test]
        public void MobileInputBridge_SubTickTapAndMultiTickContinuousHold_LatchesAndSustainsUntilRelease()
        {
            GameObject go = new GameObject("MobileBridge_Test");
            try
            {
                MobileInputBridge bridge = go.AddComponent<MobileInputBridge>();
                bridge.TriggerTapButton(NetworkInputButtons.DodgeLeft);
                bridge.PressButton(NetworkInputButtons.RangedFire);
                bridge.ReleaseButton(NetworkInputButtons.RangedFire);

                NetworkButtons firstTick = bridge.ConsumeButtons();
                Assert.IsTrue(firstTick.IsSet((int)NetworkInputButtons.DodgeLeft), "Tap must be observed on first tick.");
                Assert.IsTrue(firstTick.IsSet((int)NetworkInputButtons.RangedFire), "Press+Release within sub-tick must still latch once.");

                NetworkButtons secondTick = bridge.ConsumeButtons();
                Assert.AreEqual(0, secondTick.Bits, "Latched buttons must clear automatically after one tick.");

                // Multi-tick continuous hold: stays active across 3 consecutive ticks until ReleaseButton!
                bridge.PressButton(NetworkInputButtons.ForwardDash);
                for (int tick = 0; tick < 3; tick++)
                {
                    Assert.IsTrue(bridge.ConsumeButtons().IsSet((int)NetworkInputButtons.ForwardDash));
                }

                bridge.ReleaseButton(NetworkInputButtons.ForwardDash);
                Assert.IsFalse(bridge.ConsumeButtons().IsSet((int)NetworkInputButtons.ForwardDash));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CollectNetworkInput_PollerInstanceWithMobileBridgeAndSensitivity_ProducesExpected16ByteStruct()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PollerTest_" + Guid.NewGuid().ToString("N"));
            GameObject go = new GameObject("LocalInputPoller_InstanceTest");
            try
            {
                KeybindingManager keybinds = new KeybindingManager(tempDir) { MouseSensitivity = 2.0f };
                InputFocusManager focus = new InputFocusManager();
                MobileInputBridge bridge = go.AddComponent<MobileInputBridge>();
                LocalInputPoller poller = go.AddComponent<LocalInputPoller>();
                poller.Initialize(keybinds, focus, bridge);
                poller.SetLookAngles(10f, 0f);

                bridge.SetMoveVector(new Vector2(0.6f, 0.8f));
                bridge.AddLookDelta(new Vector2(15f, -10f));
                bridge.TriggerTapButton(NetworkInputButtons.Jump);

                NetworkInputData polled = poller.CollectNetworkInput();
                Assert.AreEqual(0.6f, polled.MoveDirection.x, 0.001f);
                Assert.AreEqual(0.8f, polled.MoveDirection.y, 0.001f);
                Assert.AreEqual(40f, polled.LookYaw, 0.01f, "10 + 15 * 2.0 sensitivity = 40 deg yaw.");
                Assert.AreEqual(20f, polled.LookPitch, 0.01f, "0 - (-10 * 2.0) = +20 deg pitch.");
                Assert.IsTrue(polled.Buttons.IsSet((int)NetworkInputButtons.Jump));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void MergeInputs_OneThousandIterations_AllocatesZeroManagedBytes()
        {
            NetworkButtons pc = default;
            pc.Set((int)NetworkInputButtons.Jump, true);
            NetworkButtons mob = default;
            mob.Set((int)NetworkInputButtons.DodgeRight, true);

            NetworkInputData warmup = LocalInputPoller.MergeInputs(Vector2.up, pc, Vector2.right, mob, 15f, -95f, true);
            float sink = warmup.LookPitch;

            GC.Collect();
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1000; i++)
            {
                NetworkInputData step = LocalInputPoller.MergeInputs(Vector2.up, pc, Vector2.right, mob, 15f, -95f, true);
                sink += step.MoveDirection.x + step.LookPitch;
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            Assert.AreEqual(0L, allocatedBytes, $"Expected 0B GC allocation across 1000 MergeInputs calls, got {allocatedBytes}B (sink={sink}).");
        }
    }
}
