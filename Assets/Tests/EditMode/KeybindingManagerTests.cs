using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Player;
using CapsuleArena.UI.HUD;
using CapsuleArena.UI.Settings;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying <see cref="KeybindingManager"/> atomic <c>.tmp</c> JSON persistence,
    /// all 14 canonical default bindings, <c>.corrupt.bak</c> recovery, missing-action backfill,
    /// conflict-swap rebinding, reserved Escape/F1-F12/Meta rejection, <see cref="KeybindingRebindModal"/>
    /// keyboard + mouse listen, 5.0s timeout cancel, conflict-swap <c>#FFB703</c> + <c>#0F172A</c> contrast,
    /// sensitivity slider UI, auto-close on <c>MatchEnded</c> / player death, and <see cref="MobileActionPadView"/>
    /// 3-way <c>Auto / ForceOn / ForceOff</c> segmented button toggles without runtime <c>Destroy</c>
    /// (Stories 3-1, 11-2, 11-3; ADR-0002; TR-input-001, TR-rebind-002, TR-rebind-003).
    /// </summary>
    [TestFixture]
    public class KeybindingManagerTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            DamageReceiverRegistry.ClearAll();
            _tempDir = Path.Combine(Path.GetTempPath(), "CapsuleArena_KeybindTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
            if (Directory.Exists(_tempDir))
            {
                foreach (string file in Directory.GetFiles(_tempDir))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Test]
        public void Constructor_FreshDirectory_WritesAll14CanonicalDefaultsWithoutLeftoverTempFile()
        {
            KeybindingManager manager = new KeybindingManager(_tempDir);

            Assert.IsTrue(File.Exists(manager.SaveFilePath));
            Assert.IsFalse(File.Exists(manager.TempSaveFilePath));

            Assert.AreEqual("<Keyboard>/w", manager.GetBindingPath(GameInputAction.MoveForward));
            Assert.AreEqual("<Keyboard>/s", manager.GetBindingPath(GameInputAction.MoveBackward));
            Assert.AreEqual("<Keyboard>/a", manager.GetBindingPath(GameInputAction.MoveLeft));
            Assert.AreEqual("<Keyboard>/d", manager.GetBindingPath(GameInputAction.MoveRight));
            Assert.AreEqual("<Keyboard>/space", manager.GetBindingPath(GameInputAction.Jump));
            Assert.AreEqual("<Keyboard>/q", manager.GetBindingPath(GameInputAction.DodgeLeft));
            Assert.AreEqual("<Keyboard>/e", manager.GetBindingPath(GameInputAction.DodgeRight));
            Assert.AreEqual("<Keyboard>/2", manager.GetBindingPath(GameInputAction.ForwardDash));
            Assert.AreEqual("<Mouse>/leftButton", manager.GetBindingPath(GameInputAction.VerticalSlash));
            Assert.AreEqual("<Keyboard>/1", manager.GetBindingPath(GameInputAction.HorizontalSweepLeft));
            Assert.AreEqual("<Keyboard>/3", manager.GetBindingPath(GameInputAction.HorizontalSweepRight));
            Assert.AreEqual("<Mouse>/rightButton", manager.GetBindingPath(GameInputAction.RangedFire));
            Assert.AreEqual("<Keyboard>/v", manager.GetBindingPath(GameInputAction.ToggleCameraView));
            Assert.AreEqual("<Keyboard>/enter", manager.GetBindingPath(GameInputAction.ToggleChatFocus));
        }

        [Test]
        public void TryRebindAction_ConflictingKeyAndResetToDefaults_SwapsBindingsPersistentlyAndRestoresCanonicalMap()
        {
            KeybindingManager manager = new KeybindingManager(_tempDir);
            int eventCount = 0;
            manager.OnBindingsChanged += () => eventCount++;

            bool ok = manager.TryRebindAction(GameInputAction.DodgeLeft, "<Keyboard>/e");

            Assert.IsTrue(ok);
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual("<Keyboard>/e", manager.GetBindingPath(GameInputAction.DodgeLeft));
            Assert.AreEqual("<Keyboard>/q", manager.GetBindingPath(GameInputAction.DodgeRight));

            KeybindingManager reloaded = new KeybindingManager(_tempDir);
            Assert.AreEqual("<Keyboard>/e", reloaded.GetBindingPath(GameInputAction.DodgeLeft));
            Assert.AreEqual("<Keyboard>/q", reloaded.GetBindingPath(GameInputAction.DodgeRight));

            reloaded.ResetToDefaults();
            Assert.AreEqual("<Keyboard>/q", reloaded.GetBindingPath(GameInputAction.DodgeLeft));
            Assert.AreEqual("<Keyboard>/e", reloaded.GetBindingPath(GameInputAction.DodgeRight));
        }

        [Test]
        public void TryRebindAction_ReservedEscapeFunctionKeysOrMeta_ReturnsFalseAndPreservesCurrentBinding()
        {
            KeybindingManager manager = new KeybindingManager(_tempDir);

            Assert.IsFalse(manager.TryRebindAction(GameInputAction.Jump, "<Keyboard>/escape"));
            Assert.IsFalse(manager.TryRebindAction(GameInputAction.Jump, "<Keyboard>/f1"));
            Assert.IsFalse(manager.TryRebindAction(GameInputAction.Jump, "<Keyboard>/f12"));
            Assert.IsFalse(manager.TryRebindAction(GameInputAction.Jump, "<Keyboard>/leftMeta"));
            Assert.IsFalse(manager.TryRebindAction(GameInputAction.Jump, "   "));
            Assert.AreEqual("<Keyboard>/space", manager.GetBindingPath(GameInputAction.Jump));
        }

        [Test]
        public void Load_CorruptAndDuplicateKeyJson_CreatesCorruptBakBackupRestoresDefaultsAndFiresEvent()
        {
            string targetFile = Path.Combine(_tempDir, KeybindingManager.FileName);
            File.WriteAllText(targetFile, "{ \"_schemaVersion\": 1, \"_bindings\": [ { \"_action\": \"Jump", Encoding.UTF8);

            KeybindingManager manager = new KeybindingManager(_tempDir);
            Assert.IsTrue(File.Exists(manager.CorruptBackupFilePath));
            Assert.AreEqual("<Keyboard>/space", manager.GetBindingPath(GameInputAction.Jump));

            string duplicateJson = "{\n" +
                                   "  \"_schemaVersion\": 1,\n" +
                                   "  \"_mobilePadMode\": 0,\n" +
                                   "  \"_mobileControlMode\": \"Joystick\",\n" +
                                   "  \"_mouseSensitivity\": 1.0,\n" +
                                   "  \"_bindings\": [\n" +
                                   "    { \"_action\": \"Jump\", \"_bindingPath\": \"<Keyboard>/space\" },\n" +
                                   "    { \"_action\": \"DodgeLeft\", \"_bindingPath\": \"<Keyboard>/space\" }\n" +
                                   "  ]\n" +
                                   "}";
            File.WriteAllText(targetFile, duplicateJson, Encoding.UTF8);

            int loadEvents = 0;
            manager.OnBindingsChanged += () => loadEvents++;
            manager.Load();

            Assert.AreEqual(1, loadEvents);
            Assert.AreEqual("<Keyboard>/q", manager.GetBindingPath(GameInputAction.DodgeLeft));
        }

        [Test]
        public void Load_PartialJsonMissingActions_BackfillsAll14UniqueBindingsWithoutDuplicateCollision()
        {
            string targetFile = Path.Combine(_tempDir, KeybindingManager.FileName);
            string partialJson = "{\n" +
                                 "  \"_schemaVersion\": 1,\n" +
                                 "  \"_mobilePadMode\": 1,\n" +
                                 "  \"_mobileControlMode\": \"DPad\",\n" +
                                 "  \"_mouseSensitivity\": 1.8,\n" +
                                 "  \"_bindings\": [\n" +
                                 "    { \"_action\": \"Jump\", \"_bindingPath\": \"<Keyboard>/q\" }\n" +
                                 "  ]\n" +
                                 "}";
            File.WriteAllText(targetFile, partialJson, Encoding.UTF8);

            KeybindingManager manager = new KeybindingManager(_tempDir);
            Assert.AreEqual("<Keyboard>/q", manager.GetBindingPath(GameInputAction.Jump));
            Assert.AreEqual(MobilePadVisibilityMode.ForceOn, manager.MobilePadMode);
            Assert.AreEqual(MobileControlMode.DPad, manager.ActiveMobileControlMode);
            Assert.AreEqual(1.8f, manager.MouseSensitivity, 0.001f);

            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameInputAction action in Enum.GetValues(typeof(GameInputAction)))
            {
                string path = manager.GetBindingPath(action);
                Assert.IsFalse(string.IsNullOrWhiteSpace(path));
                Assert.IsTrue(unique.Add(path));
            }
        }

        [Test]
        public void Save_ReadOnlyTargetFile_CatchesIoFailureAndPreservesInMemoryRebindWithoutCrashing()
        {
            KeybindingManager manager = new KeybindingManager(_tempDir);
            File.SetAttributes(manager.SaveFilePath, FileAttributes.ReadOnly);

            bool ok = false;
            Assert.DoesNotThrow(() => ok = manager.TryRebindAction(GameInputAction.ForwardDash, "<Keyboard>/r"));
            Assert.IsTrue(ok);
            Assert.AreEqual("<Keyboard>/r", manager.GetBindingPath(GameInputAction.ForwardDash));
        }

        [Test]
        public void MouseSensitivityAndMobileModes_OutOfBoundsAndNaN_ClampsAndPersists()
        {
            KeybindingManager manager = new KeybindingManager(_tempDir);

            manager.MouseSensitivity = 99.0f;
            Assert.AreEqual(5.0f, manager.MouseSensitivity, 0.0001f);

            manager.MouseSensitivity = -3.0f;
            Assert.AreEqual(0.1f, manager.MouseSensitivity, 0.0001f);

            manager.MouseSensitivity = float.NaN;
            Assert.AreEqual(1.0f, manager.MouseSensitivity, 0.0001f);

            manager.ActiveMobileControlMode = MobileControlMode.DPad;
            manager.MobilePadMode = MobilePadVisibilityMode.ForceOff;

            KeybindingManager reloaded = new KeybindingManager(_tempDir);
            Assert.AreEqual(1.0f, reloaded.MouseSensitivity, 0.0001f);
            Assert.AreEqual(MobileControlMode.DPad, reloaded.ActiveMobileControlMode);
            Assert.AreEqual(MobilePadVisibilityMode.ForceOff, reloaded.MobilePadMode);
        }

        [Test]
        public void KeybindingRebindModal_KeyboardAndMouseListen5sTimeoutReservedRejectSwapContrastSliderAndAutoClose_Verified()
        {
            GameObject modalGo = new GameObject("KeybindingRebindModal_Test", typeof(RectTransform));
            GameObject mmGo = new GameObject("MatchManager_RebindModalTest");
            GameObject playerGo = new GameObject("Player_RebindModalTest");
            CharacterCombatConfigSO cfg = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            try
            {
                KeybindingManager keyMgr = new KeybindingManager(_tempDir);
                InputFocusManager focusMgr = new InputFocusManager();
                MatchManager mm = mmGo.AddComponent<MatchManager>();
                PlayerRef p1 = PlayerRef.FromEncoded(1);
                PlayerRef p2 = PlayerRef.FromEncoded(2);
                mm.ConfigureServices(focusMgr, "REBIND01", p1);
                mm.RegisterOrUpdateStandalonePlayer(p1, "P1", isAlive: true);
                mm.RegisterOrUpdateStandalonePlayer(p2, "P2", isAlive: true);
                mm.SimulateStandaloneTicks(1 + MatchManager.CountdownDurationTicks);

                PlayerHealth localHp = playerGo.AddComponent<PlayerHealth>();
                localHp.ConfigureForStandaloneTest(cfg, p1);

                KeybindingRebindModal modal = modalGo.AddComponent<KeybindingRebindModal>();
                modal.Initialize(keyMgr, focusMgr, mm);
                modal.BindLocalPlayerHealth(localHp);

                modal.OpenModal();
                Assert.IsTrue(modal.IsOpen);
                Assert.IsFalse(focusMgr.IsGameplayInputAllowed);
                Assert.AreEqual("[LMB]", modal.RowsByAction[GameInputAction.VerticalSlash].CurrentBadgeText);
                Assert.AreEqual("[RMB]", modal.RowsByAction[GameInputAction.RangedFire].CurrentBadgeText);

                // 1. Interactive probe includes BOTH <Keyboard>/anyKey AND <Mouse>/*Button + Escape cancel:
                modal.StartInteractiveRebind(GameInputAction.Jump);
                Assert.IsTrue(modal.IsListeningForKey);
                Assert.IsNotNull(modal.ActiveProbeAction);
                Assert.AreEqual(2, modal.ActiveProbeAction.bindings.Count, "Probe action must listen to both Keyboard and Mouse buttons.");
                Assert.IsFalse(modal.ApplyCandidateBindingPath(GameInputAction.Jump, "<Keyboard>/escape"));
                Assert.IsFalse(modal.IsListeningForKey);

                // 2. 5.0s interactive listen timeout auto-cancels & keeps previous binding intact:
                modal.StartInteractiveRebind(GameInputAction.Jump);
                Assert.IsTrue(modal.IsListeningForKey);
                modal.TickListenCountdown(5.1f);
                Assert.IsFalse(modal.IsListeningForKey, "5.0s listen timeout must cancel interactive rebind cleanly.");
                Assert.AreEqual("[Space]", modal.RowsByAction[GameInputAction.Jump].CurrentBadgeText);

                // 3. Reserved system key (<Keyboard>/f1 & <Keyboard>/leftMeta) shows error toast and keeps binding:
                Assert.IsFalse(modal.ApplyCandidateBindingPath(GameInputAction.Jump, "<Keyboard>/f1"));
                Assert.IsTrue(modal.ToastView.IsShowing);
                Assert.AreEqual("[Space]", modal.RowsByAction[GameInputAction.Jump].CurrentBadgeText);

                // 4. Mouse button swap (<Mouse>/rightButton onto VerticalSlash swaps LMB <-> RMB) + dark #0F172A text contrast on #FFB703 row!
                Assert.IsTrue(modal.ApplyCandidateBindingPath(GameInputAction.VerticalSlash, "<Mouse>/rightButton"));
                Assert.AreEqual("[RMB]", modal.RowsByAction[GameInputAction.VerticalSlash].CurrentBadgeText);
                Assert.AreEqual("[LMB]", modal.RowsByAction[GameInputAction.RangedFire].CurrentBadgeText);
                Assert.IsTrue(modal.RowsByAction[GameInputAction.VerticalSlash].IsSwapHighlighted);
                Assert.AreEqual(KeybindingRowView.HighlightDarkForeground, modal.RowsByAction[GameInputAction.VerticalSlash].CurrentActionLabelColor,
                    "Highlighted #FFB703 row must switch text to #0F172A for high legibility.");

                // 5. Sensitivity slider UI interaction ([0.1, 5.0]):
                modal.SensitivitySlider.value = 2.4f;
                Assert.AreEqual(2.4f, keyMgr.MouseSensitivity, 0.01f);

                // 6. Reset to Defaults:
                modal.OnResetDefaultsClicked();
                Assert.AreEqual("[LMB]", modal.RowsByAction[GameInputAction.VerticalSlash].CurrentBadgeText);

                // 7. Auto-close when local player dies mid-rebind:
                modal.StartInteractiveRebind(GameInputAction.ForwardDash);
                for (int t = 1; t <= 4; t++)
                {
                    localHp.RequestDamageFromAttacker(p2, 25, AttackType.VerticalSlash, Vector3.forward, attackTick: t);
                }

                Assert.IsFalse(modal.IsOpen, "Elimination mid-rebind must auto-close modal and release RebindModalOpen.");
                Assert.IsFalse(modal.IsListeningForKey);

                // 8. Re-open modal, then end match -> MatchState.Ended auto-closes modal!
                modal.OpenModal();
                Assert.IsTrue(modal.IsOpen);
                mm.RecordElimination(killer: p2, victim: p1);
                Assert.IsFalse(modal.IsOpen, "MatchState.Ended must auto-close KeybindingRebindModal.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cfg);
                UnityEngine.Object.DestroyImmediate(playerGo);
                UnityEngine.Object.DestroyImmediate(mmGo);
                UnityEngine.Object.DestroyImmediate(modalGo);
            }
        }

        [Test]
        public void MobilePadVisibilityModeToggle_SegmentedUiButtonsCanvasGroupBlocksRaycastsZeroDestroyAndGhostGuardrail_Verified()
        {
            Assert.IsFalse(MobileActionPadView.EvaluateVisibility(MobilePadVisibilityMode.Auto, isMobileOrTouch: false, isLocalPlayerAlive: true));
            Assert.IsTrue(MobileActionPadView.EvaluateVisibility(MobilePadVisibilityMode.Auto, isMobileOrTouch: true, isLocalPlayerAlive: true));
            Assert.IsTrue(MobileActionPadView.EvaluateVisibility(MobilePadVisibilityMode.ForceOn, isMobileOrTouch: false, isLocalPlayerAlive: true));
            Assert.IsFalse(MobileActionPadView.EvaluateVisibility(MobilePadVisibilityMode.ForceOff, isMobileOrTouch: true, isLocalPlayerAlive: true));
            Assert.IsFalse(MobileActionPadView.ShouldShowMobilePad(1, isTouchDevice: true, isAlive: false));

            GameObject modalGo = new GameObject("Modal_PadToggleTest", typeof(RectTransform));
            GameObject padGo = new GameObject("Pad_ToggleTest", typeof(RectTransform));
            GameObject playerGo = new GameObject("Player_PadToggleTest");
            CharacterCombatConfigSO cfg = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            try
            {
                KeybindingManager keyMgr = new KeybindingManager(_tempDir);
                InputFocusManager focusMgr = new InputFocusManager();
                MobileInputBridge bridge = padGo.AddComponent<MobileInputBridge>();
                PlayerHealth health = playerGo.AddComponent<PlayerHealth>();
                health.ConfigureForStandaloneTest(cfg, PlayerRef.FromEncoded(1));

                MobileActionPadView pad = padGo.AddComponent<MobileActionPadView>();
                pad.Initialize(bridge, keyMgr, focusMgr, simulateTouchDeviceOverride: false);
                pad.BindLocalPlayer(health);

                KeybindingRebindModal modal = modalGo.AddComponent<KeybindingRebindModal>();
                modal.Initialize(keyMgr, focusMgr);

                Assert.IsFalse(pad.IsPadVisible);
                Assert.AreEqual(7, pad.ActionButtons.Count);

                // Click actual segmented UI buttons (FORCE ON -> FORCE OFF -> AUTO -> FORCE ON):
                modal.PadModeForceOnButton.onClick.Invoke();
                Assert.IsTrue(pad.IsPadVisible);
                Assert.IsTrue(pad.PadCanvasGroup.interactable);
                Assert.IsTrue(pad.PadCanvasGroup.blocksRaycasts);
                Assert.AreEqual(MobilePadVisibilityMode.ForceOn, new KeybindingManager(_tempDir).MobilePadMode);

                modal.PadModeForceOffButton.onClick.Invoke();
                Assert.IsFalse(pad.IsPadVisible);
                Assert.AreEqual(0f, pad.PadCanvasGroup.alpha, 0.001f);
                Assert.IsFalse(pad.PadCanvasGroup.interactable, "ForceOff must set CanvasGroup.interactable = false.");
                Assert.IsFalse(pad.PadCanvasGroup.blocksRaycasts, "ForceOff must set CanvasGroup.blocksRaycasts = false.");
                Assert.AreEqual(7, pad.ActionButtons.Count, "Toggling visibility must NEVER Destroy any of the 7 pre-allocated buttons.");

                modal.PadModeAutoButton.onClick.Invoke();
                Assert.AreEqual(MobilePadVisibilityMode.Auto, keyMgr.MobilePadMode);
                Assert.IsFalse(pad.IsPadVisible);

                modal.PadModeForceOnButton.onClick.Invoke();
                Assert.IsTrue(pad.IsPadVisible);
                for (int t = 1; t <= 4; t++)
                {
                    health.RequestDamageFromAttacker(PlayerRef.FromEncoded(2), 25, AttackType.VerticalSlash, Vector3.forward, attackTick: t);
                }

                Assert.IsFalse(pad.IsPadVisible);
                Assert.IsFalse(pad.PadCanvasGroup.blocksRaycasts);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cfg);
                UnityEngine.Object.DestroyImmediate(playerGo);
                UnityEngine.Object.DestroyImmediate(padGo);
                UnityEngine.Object.DestroyImmediate(modalGo);
            }
        }
    }
}
