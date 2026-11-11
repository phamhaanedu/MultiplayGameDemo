using System;
using System.IO;
using CapsuleArena.CameraSystem;
using CapsuleArena.Combat;
using CapsuleArena.Core.Config;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.Player;
using CapsuleArena.UI.Common;
using CapsuleArena.UI.HUD;
using DG.Tweening;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying <see cref="OverheadBillboardUI"/> transform rotation/offset/scale,
    /// local green (<c>#22C55E</c>) vs enemy red (<c>#EF4444</c>) HP colors, FPS hide, Ghost <c>[GHOST]</c> 50% fade,
    /// and disabled <see cref="GraphicRaycaster"/>; <see cref="MobileActionPadView"/> pairwise non-overlapping
    /// 7-button geometry, <see cref="VirtualJoystick"/> vectors, shared cooldowns, tenths countdown text, <c>0.90x</c>
    /// press scale, and focus-loss auto-release; and <see cref="CombatHudPresenter"/> dynamic crosshair spread,
    /// lethal <c>#E63946</c> hit-marker, 90-deg sweep indicator, C# event wiring, 0 HP vignette clear, and modal buttons
    /// (Stories 10-1, 10-2, 10-3; ADR-0007; TR-hud-001..003).
    /// </summary>
    [TestFixture]
    public class CombatHudPresenterTests
    {
        private CharacterCombatConfigSO _config;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            DamageReceiverRegistry.ClearAll();
            _config = ScriptableObject.CreateInstance<CharacterCombatConfigSO>();
            _tempDir = Path.Combine(Path.GetTempPath(), "CapsuleArena_HudTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            DamageReceiverRegistry.ClearAll();
            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        [Test]
        public void OverheadBillboardUI_TransformLookRotationDistanceScaleLocalVsEnemyColorsFpsHideAndGhost_Verified()
        {
            Assert.AreEqual(0.75f, OverheadBillboardUI.CalculateBillboardScale(2.0f), 0.001f);
            Assert.AreEqual(1.00f, OverheadBillboardUI.CalculateBillboardScale(8.0f), 0.001f);
            Assert.AreEqual(1.35f, OverheadBillboardUI.CalculateBillboardScale(20.0f), 0.001f);

            GameObject playerGo = new GameObject("PlayerWithBillboard");
            GameObject billboardGo = new GameObject("OverheadBillboard", typeof(RectTransform));
            billboardGo.transform.SetParent(playerGo.transform, worldPositionStays: false);
            GameObject enemyGo = new GameObject("EnemyWithBillboard");
            GameObject enemyBillboardGo = new GameObject("EnemyOverheadBillboard", typeof(RectTransform));
            enemyBillboardGo.transform.SetParent(enemyGo.transform, worldPositionStays: false);
            GameObject camGo = new GameObject("Camera_BillboardTest");
            Camera cam = camGo.AddComponent<Camera>();
            try
            {
                playerGo.transform.position = Vector3.zero;
                enemyGo.transform.position = new Vector3(0f, 0f, 8.0f);
                camGo.transform.SetPositionAndRotation(new Vector3(0f, 2.35f, -8.0f), Quaternion.identity);

                PlayerHealth health = playerGo.AddComponent<PlayerHealth>();
                health.ConfigureForStandaloneTest(_config, PlayerRef.FromEncoded(1));
                PlayerHealth enemyHealth = enemyGo.AddComponent<PlayerHealth>();
                enemyHealth.ConfigureForStandaloneTest(_config, PlayerRef.FromEncoded(2));

                CameraController camCtrl = playerGo.AddComponent<CameraController>();
                camCtrl.BindLocalCamera(cam, null, null, _config);

                GraphicRaycaster strayRaycaster = billboardGo.AddComponent<GraphicRaycaster>();
                OverheadBillboardUI billboard = billboardGo.AddComponent<OverheadBillboardUI>();
                billboard.Initialize(health, "Vanguard", isLocalPlayer: true, cam.transform);

                OverheadBillboardUI enemyBillboard = enemyBillboardGo.AddComponent<OverheadBillboardUI>();
                enemyBillboard.Initialize(enemyHealth, "Rival", isLocalPlayer: false, cam.transform);

                // 1. Verify +2.35m offset, LookRotation, and live localScale at 8.0m (0.01 * 1.00 = 0.01) vs 16.0m (0.01 * 1.35 = 0.0135):
                billboard.UpdateBillboardTransform(cam.transform.position, cam.transform.rotation);
                Assert.AreEqual(OverheadBillboardUI.CanonicalLocalOffsetY, billboard.transform.localPosition.y, 0.001f);
                Assert.AreEqual(0.01f, billboard.transform.localScale.x, 0.0005f, "At 8.0m reference distance, world scale must be 0.01 * 1.0.");
                enemyBillboard.UpdateBillboardTransform(cam.transform.position, cam.transform.rotation);
                Assert.AreEqual(0.0135f, enemyBillboard.transform.localScale.x, 0.0005f, "At 16.0m distance, world scale must clamp to 0.01 * 1.35.");

                // 2. Local HealthyGreen (#22C55E) vs EnemyHealthyRed (#EF4444) at > 30% HP:
                Assert.IsFalse(strayRaycaster.enabled);
                Assert.AreEqual(UiThemeAndFontProvider.HealthyGreen, billboard.CurrentInstantFillColor);
                Assert.AreEqual(OverheadBillboardUI.EnemyHealthyRed, enemyBillboard.CurrentInstantFillColor);

                // 3. Deal 75 damage (5 * 15 = 75 -> 25% <= 30% critical threshold) -> instant bar turns critical crimson & delayed bar trails over 0.35s:
                for (int t = 1; t <= 5; t++)
                {
                    health.RequestDamageFromAttacker(PlayerRef.FromEncoded(2), 15, AttackType.RangedProjectile, enemyGo.transform.position, attackTick: t);
                    enemyHealth.RequestDamageFromAttacker(PlayerRef.FromEncoded(1), 15, AttackType.RangedProjectile, playerGo.transform.position, attackTick: t);
                }

                Assert.AreEqual(0.25f, billboard.InstantFillAmount, 0.001f);
                Assert.AreEqual(UiThemeAndFontProvider.CrimsonDanger, billboard.CurrentInstantFillColor);
                Assert.AreEqual(OverheadBillboardUI.EnemyCriticalCrimson, enemyBillboard.CurrentInstantFillColor);
                Assert.Greater(billboard.DelayedFillAmount, 0.25f);
                DOTween.ManualUpdate(0.40f);
                Assert.AreEqual(0.25f, billboard.DelayedFillAmount, 0.02f);

                // 4. FPS hides ONLY local billboard (enemy billboard remains alpha == 1.0!):
                camCtrl.SetPerspective(CameraPerspectiveMode.FPS);
                Assert.AreEqual(0.0f, billboard.CurrentAlpha, 0.001f);
                Assert.AreEqual(1.0f, enemyBillboard.CurrentAlpha, 0.001f, "Enemy billboards must stay visible in FPS mode.");
                camCtrl.SetPerspective(CameraPerspectiveMode.TPS);
                Assert.AreEqual(1.0f, billboard.CurrentAlpha, 0.001f);

                // 5. Lethal hits -> Ghost transition hides HP bar, appends " [GHOST]", and dims alpha to 0.50:
                health.RequestDamageFromAttacker(PlayerRef.FromEncoded(2), 15, AttackType.RangedProjectile, enemyGo.transform.position, attackTick: 6);
                health.RequestDamageFromAttacker(PlayerRef.FromEncoded(2), 15, AttackType.RangedProjectile, enemyGo.transform.position, attackTick: 7);
                Assert.AreEqual("Vanguard [GHOST]", billboard.RenderedNameText);
                Assert.AreEqual(0.50f, billboard.CurrentAlpha, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(enemyGo);
                UnityEngine.Object.DestroyImmediate(playerGo);
            }
        }

        [Test]
        public void MobileActionPadView_NonOverlappingGeometryJoystickVectorsSharedCooldownsPressScaleAndFocusAutoRelease_Verified()
        {
            GameObject canvasGo = new GameObject("MobilePadCanvas", typeof(RectTransform));
            GameObject padGo = new GameObject("MobileActionPad", typeof(RectTransform));
            padGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            GameObject bridgeGo = new GameObject("MobileBridge_Test");
            try
            {
                MobileInputBridge bridge = bridgeGo.AddComponent<MobileInputBridge>();
                KeybindingManager keyMgr = new KeybindingManager(_tempDir);
                keyMgr.MobilePadMode = MobilePadVisibilityMode.ForceOn;
                InputFocusManager focusMgr = new InputFocusManager();

                MobileActionPadView pad = padGo.AddComponent<MobileActionPadView>();
                pad.Initialize(bridge, keyMgr, focusMgr, simulateTouchDeviceOverride: false);

                Assert.IsTrue(pad.IsPadVisible);
                Assert.AreEqual(7, pad.ActionButtons.Count);

                // 1. Pairwise non-overlap check across all 7 circular buttons (minimum 8px edge-to-edge gap) + >= 84px WCAG floor:
                for (int i = 0; i < pad.ActionButtons.Count; i++)
                {
                    RectTransform rtA = pad.ActionButtons[i].GetComponent<RectTransform>();
                    Assert.GreaterOrEqual(rtA.sizeDelta.x, 84f);
                    Assert.GreaterOrEqual(rtA.sizeDelta.y, 84f);

                    for (int j = i + 1; j < pad.ActionButtons.Count; j++)
                    {
                        RectTransform rtB = pad.ActionButtons[j].GetComponent<RectTransform>();
                        float centerDist = Vector2.Distance(rtA.anchoredPosition, rtB.anchoredPosition);
                        float sumRadii = (rtA.sizeDelta.x * 0.5f) + (rtB.sizeDelta.x * 0.5f);
                        Assert.GreaterOrEqual(centerDist, sumRadii + 8f,
                            $"Buttons {pad.ActionButtons[i].name} and {pad.ActionButtons[j].name} must not overlap (dist={centerDist:F1}, radii={sumRadii:F1}).");
                    }
                }

                // 2. VirtualJoystick drag clamping (MaxKnobRadiusPx = 90) & release reset:
                Assert.IsNotNull(pad.Joystick);
                pad.Joystick.ApplyLocalOffset(new Vector2(180f, 0f));
                Assert.AreEqual(1.0f, pad.Joystick.CurrentMoveVector.x, 0.01f);
                Assert.AreEqual(1.0f, bridge.MoveVector.x, 0.01f);
                pad.Joystick.ResetKnobAndInput();
                Assert.AreEqual(Vector2.zero, bridge.MoveVector);

                // 3. Locate buttons for SweepL/SweepR, DodgeL/DodgeR, and Dash:
                MobileActionButton sweepL = null, sweepR = null, dodgeL = null, dodgeR = null, dashBtn = null;
                foreach (MobileActionButton btn in pad.ActionButtons)
                {
                    switch (btn.MappedGameAction)
                    {
                        case GameInputAction.HorizontalSweepLeft: sweepL = btn; break;
                        case GameInputAction.HorizontalSweepRight: sweepR = btn; break;
                        case GameInputAction.DodgeLeft: dodgeL = btn; break;
                        case GameInputAction.DodgeRight: dodgeR = btn; break;
                        case GameInputAction.ForwardDash: dashBtn = btn; break;
                    }
                }

                // 4. Shared cooldowns (SweepL + SweepR share HorizontalSweep; DodgeL + DodgeR share DodgeRoll) + tenths string "1.5":
                pad.ForwardCooldownToButtons(CombatSkillId.HorizontalSweep, remainingSec: 0.6f, totalSec: 1.2f);
                Assert.AreEqual(0.50f, sweepL.RadialFillAmount, 0.01f);
                Assert.AreEqual(0.50f, sweepR.RadialFillAmount, 0.01f, "Sweep Left and Right must share HorizontalSweep cooldown.");
                Assert.AreEqual("0.6", sweepL.CurrentCooldownText);

                pad.ForwardCooldownToButtons(CombatSkillId.DodgeRoll, remainingSec: 1.5f, totalSec: 3.0f);
                Assert.AreEqual(0.50f, dodgeL.RadialFillAmount, 0.01f);
                Assert.AreEqual(0.50f, dodgeR.RadialFillAmount, 0.01f, "Dodge Left and Right must share DodgeRoll cooldown.");
                Assert.AreEqual("1.5", dodgeL.CurrentCooldownText);

                // 5. Press-scale 0.90x feedback + focus-loss auto-release when Chat opens while holding button + joystick:
                PointerEventData ptr = new PointerEventData(null);
                dashBtn.OnPointerDown(ptr);
                DOTween.ManualUpdate(0.10f);
                Assert.AreEqual(0.90f, dashBtn.transform.localScale.x, 0.02f, "PointerDown must tween button scale to 0.90x.");
                Assert.IsTrue(dashBtn.IsPointerHeld);

                pad.Joystick.ApplyLocalOffset(new Vector2(0f, 90f));
                focusMgr.SetFocusFlag(InputFocusFlag.ChatFocused, true);
                Assert.IsFalse(dashBtn.IsPointerHeld, "Focus loss must immediately release held mobile buttons.");
                Assert.AreEqual(Vector2.zero, pad.Joystick.CurrentMoveVector, "Focus loss must reset VirtualJoystick.");
                focusMgr.SetFocusFlag(InputFocusFlag.ChatFocused, false);

                // 6. Dynamic rebind badge update:
                keyMgr.TryRebindAction(GameInputAction.ForwardDash, "<Keyboard>/r");
                Assert.AreEqual("[R]", dashBtn.CurrentBadgeText);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bridgeGo);
                UnityEngine.Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void CombatHudPresenter_EventWiringDynamicCrosshairLethalHitMarkerSweepArcVignetteClearAndModalButtons_Verified()
        {
            GameObject hudGo = new GameObject("CombatHud_Test", typeof(RectTransform));
            GameObject mmGo = new GameObject("MatchMgr_HudTest");
            GameObject playerGo = new GameObject("Player_HudTest");
            try
            {
                MatchManager mm = mmGo.AddComponent<MatchManager>();
                PlayerRef p1 = PlayerRef.FromEncoded(1);
                PlayerRef p2 = PlayerRef.FromEncoded(2);
                mm.ConfigureServices(null, "HUD01", p1);
                mm.RegisterOrUpdateStandalonePlayer(p1, "Alpha", isAlive: true);
                mm.RegisterOrUpdateStandalonePlayer(p2, "Bravo", isAlive: true);
                mm.SimulateStandaloneTicks(1 + MatchManager.CountdownDurationTicks);

                PlayerHealth localHp = playerGo.AddComponent<PlayerHealth>();
                localHp.ConfigureForStandaloneTest(_config, p1);
                PlayerCombatController localCombat = playerGo.AddComponent<PlayerCombatController>();
                localCombat.ConfigureForTest(_config, p1, localHp);

                CombatHudPresenter presenter = hudGo.AddComponent<CombatHudPresenter>();
                presenter.BindMatchManager(mm);
                presenter.BindLocalPlayer(localHp, localCombat, p1);

                // 1. 1 Hz Clock & <= 30s urgent highlight:
                Assert.IsTrue(presenter.RefreshMatchClock1Hz(125.9f, MatchState.Playing));
                Assert.AreEqual("02:06", presenter.DisplayedClockText);
                Assert.IsFalse(presenter.RefreshMatchClock1Hz(125.2f, MatchState.Playing));
                Assert.IsTrue(presenter.RefreshMatchClock1Hz(29.4f, MatchState.Playing));
                Assert.AreEqual("00:30", presenter.DisplayedClockText);

                // 2. Dynamic Crosshair impulse & decay + 90-deg Sweep Arc indicator (0.20s) + Normal (#FFB703) vs Lethal (#E63946) Hit-Marker:
                Assert.AreEqual(CombatHudPresenter.BaseCrosshairSizePx, presenter.CurrentCrosshairSpreadPx, 0.01f);
                presenter.TriggerHorizontalSweepArcIndicator(isLeftSweep: true);
                Assert.IsTrue(presenter.IsSweepArcIndicatorVisible);
                Assert.Greater(presenter.CurrentCrosshairSpreadPx, CombatHudPresenter.BaseCrosshairSizePx);

                presenter.TriggerHitMarker(isLethal: false);
                Assert.IsTrue(presenter.IsHitMarkerVisible);
                Assert.AreEqual(UiThemeAndFontProvider.SolarAmber, presenter.CurrentHitMarkerColor);

                presenter.TriggerHitMarker(isLethal: true);
                Assert.AreEqual(UiThemeAndFontProvider.CrimsonDanger, presenter.CurrentHitMarkerColor, "Lethal hit-marker must turn Crimson #E63946.");

                presenter.TickTransientIndicators(0.50f);
                Assert.IsFalse(presenter.IsHitMarkerVisible);
                Assert.IsFalse(presenter.IsSweepArcIndicatorVisible);
                Assert.AreEqual(CombatHudPresenter.BaseCrosshairSizePx, presenter.CurrentCrosshairSpreadPx, 0.01f);

                // 3. Real PlayerHealth event wiring -> <= 25 HP activates vignette; lethal 0 HP CLEARS vignette!
                for (int t = 1; t <= 3; t++)
                {
                    localHp.RequestDamageFromAttacker(p2, 25, AttackType.VerticalSlash, Vector3.forward, attackTick: t);
                }

                Assert.IsTrue(presenter.IsLowHealthVignetteActive, "25 HP must activate low-health crimson vignette.");
                localHp.RequestDamageFromAttacker(p2, 25, AttackType.VerticalSlash, Vector3.forward, attackTick: 4);
                Assert.IsFalse(presenter.IsLowHealthVignetteActive, "0 HP elimination must clear low-health vignette so spectator view is unobstructed.");

                // 4. 4-item Kill Feed Pool zero runtime instantiate:
                KillFeedPoolView feed = presenter.KillFeed;
                feed.EnsurePoolPrewarmed();
                for (int i = 1; i <= 6; i++)
                {
                    feed.PushKillNotification($"Killer_{i}", $"Victim_{i}");
                }

                Assert.AreEqual(4, feed.transform.childCount);
                feed.TickTimers(4.1f);
                Assert.AreEqual(0, feed.ActiveEntryCount);

                // 5. Match End Modal via real MatchManager.OnMatchEnded + PlayAgain / ReturnToLobby button click events:
                int playAgainClicks = 0;
                int returnLobbyClicks = 0;
                presenter.OnPlayAgainRequested += () => playAgainClicks++;
                presenter.OnReturnToLobbyRequested += () => returnLobbyClicks++;

                mm.RecordElimination(killer: p2, victim: p1);
                Assert.IsTrue(presenter.IsMatchEndModalOpen);
                Assert.AreEqual("DEFEAT", presenter.MatchEndHeadline);

                presenter.PlayAgainButton.onClick.Invoke();
                presenter.ReturnToLobbyButton.onClick.Invoke();
                Assert.AreEqual(1, playAgainClicks);
                Assert.AreEqual(1, returnLobbyClicks);

                presenter.HandleMatchEnded(new MatchResultSummary("M1", p1, "Alpha", isDraw: false, localDidWin: true, localKills: 3, localPlacementRank: 1));
                Assert.AreEqual("VICTORY", presenter.MatchEndHeadline);
                presenter.HandleMatchEnded(new MatchResultSummary("M3", PlayerRef.None, "DRAW", isDraw: true, localDidWin: false, localKills: 2, localPlacementRank: 1));
                Assert.AreEqual("DRAW", presenter.MatchEndHeadline);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerGo);
                UnityEngine.Object.DestroyImmediate(mmGo);
                UnityEngine.Object.DestroyImmediate(hudGo);
            }
        }
    }
}
