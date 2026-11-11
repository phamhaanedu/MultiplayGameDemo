using CapsuleArena.Core.Input;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying InputFocusManager multi-flag bitmask gating,
    /// event transition deduplication, unset-flag clear no-op, ResetAllFlags recovery,
    /// preserved LookYaw/LookPitch while suppressed, and MobileInputBridge/LocalInputPoller
    /// suppression while UI modals are open (Story 3-3; ADR-0002; TR-input-003).
    /// </summary>
    [TestFixture]
    public class InputFocusManagerTests
    {
        [Test]
        public void SetFocusFlag_OverlappingModalsAndUnsetClear_FiresTransitionOnlyOnBoundaryChanges()
        {
            InputFocusManager focus = new InputFocusManager();
            int transitionCount = 0;
            bool lastState = focus.IsGameplayInputAllowed;
            focus.OnFocusStateChanged += allowed =>
            {
                transitionCount++;
                lastState = allowed;
            };

            // Clearing an already-unset flag or passing None must be a pure no-op (0 events).
            focus.SetFocusFlag(InputFocusFlag.ChatFocused, false);
            focus.SetFocusFlag(InputFocusFlag.None, true);
            Assert.AreEqual(0, transitionCount);
            Assert.IsTrue(focus.IsGameplayInputAllowed);

            // Open Chat -> transitions true -> false (count = 1).
            focus.SetFocusFlag(InputFocusFlag.ChatFocused, true);
            Assert.IsFalse(focus.IsGameplayInputAllowed);
            Assert.AreEqual(1, transitionCount);
            Assert.IsFalse(lastState);

            // Also open Leaderboard -> still false, no redundant transition event!
            focus.SetFocusFlag(InputFocusFlag.LeaderboardOpen, true);
            Assert.IsFalse(focus.IsGameplayInputAllowed);
            Assert.AreEqual(1, transitionCount);

            // Close Chat -> Leaderboard remains open, Gameplay MUST stay blocked!
            focus.SetFocusFlag(InputFocusFlag.ChatFocused, false);
            Assert.IsFalse(focus.IsGameplayInputAllowed);
            Assert.AreEqual(InputFocusFlag.LeaderboardOpen, focus.ActiveFlags);
            Assert.AreEqual(1, transitionCount);

            // Close Leaderboard -> ActiveFlags == None -> transitions false -> true (count = 2).
            focus.SetFocusFlag(InputFocusFlag.LeaderboardOpen, false);
            Assert.IsTrue(focus.IsGameplayInputAllowed);
            Assert.AreEqual(2, transitionCount);
            Assert.IsTrue(lastState);
        }

        [Test]
        public void ResetAllFlags_WhenMultipleFlagsSet_ClearsAllAndFiresSingleUnlockEvent()
        {
            InputFocusManager focus = new InputFocusManager();
            focus.SetFocusFlag(InputFocusFlag.RebindModalOpen, true);
            focus.SetFocusFlag(InputFocusFlag.MatchEnded, true);

            int unlockEvents = 0;
            focus.OnFocusStateChanged += allowed =>
            {
                if (allowed) unlockEvents++;
            };

            focus.ResetAllFlags();
            Assert.AreEqual(InputFocusFlag.None, focus.ActiveFlags);
            Assert.IsTrue(focus.IsGameplayInputAllowed);
            Assert.AreEqual(1, unlockEvents);
        }

        [Test]
        public void FocusBlocked_ClearsAndBlocksMobileInputsZeroesMoveButtonsAndPreservesLastLookAngles()
        {
            GameObject go = new GameObject("FocusGating_Test");
            try
            {
                InputFocusManager focus = new InputFocusManager();
                MobileInputBridge bridge = go.AddComponent<MobileInputBridge>();
                LocalInputPoller poller = go.AddComponent<LocalInputPoller>();
                poller.Initialize(null, focus, bridge);
                poller.SetLookAngles(32.5f, -14.0f);

                // Player holds virtual joystick + fire button, then opens Chat without lifting finger.
                bridge.SetMoveVector(new Vector2(0f, 1f));
                bridge.PressButton(NetworkInputButtons.RangedFire);

                focus.SetFocusFlag(InputFocusFlag.ChatFocused, true);

                Assert.AreEqual(Vector2.zero, bridge.MoveVector, "Held mobile joystick must reset immediately on focus loss.");
                Assert.AreEqual(0, bridge.ConsumeButtons().Bits, "Held mobile buttons must clear immediately on focus loss.");

                // Attempting new mobile presses/joystick/look while suppressed must also be ignored:
                bridge.SetMoveVector(Vector2.right);
                bridge.PressButton(NetworkInputButtons.ForwardDash);
                bridge.TriggerTapButton(NetworkInputButtons.Jump);
                poller.ApplyLookDeltaDegrees(new Vector2(45f, 30f));

                NetworkInputData suppressed = poller.CollectNetworkInput();
                Assert.AreEqual(Vector2.zero, suppressed.MoveDirection);
                Assert.AreEqual(0, suppressed.Buttons.Bits);
                Assert.AreEqual(32.5f, suppressed.LookYaw, 0.01f, "Last valid LookYaw must be preserved so capsule never snaps to 0 deg.");
                Assert.AreEqual(-14.0f, suppressed.LookPitch, 0.01f, "Last valid LookPitch must be preserved while UI modal is open.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
