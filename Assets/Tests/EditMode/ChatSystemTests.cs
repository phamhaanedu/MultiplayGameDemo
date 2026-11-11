using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CapsuleArena.Core.Input;
using CapsuleArena.Gameplay.Match;
using CapsuleArena.UI.Chat;
using CapsuleArena.UI.Common;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// Comprehensive unit &amp; integration tests for Epic 12 In-Game Chat System:
    /// - Story 12-1: <see cref="ChatViewController"/> persistent <see cref="CanvasGroup"/> states (<c>activeSelf == true</c>),
    ///   click-through unfocused raycasts, <c>Enter</c>/<c>Escape</c>/<c>SendButton</c>/<c>ToggleButton</c>, <c>6.0s</c> wake reset, <c>MatchState.Ended</c> auto-unfocus
    /// - Story 12-2: <see cref="ChatCommandParser"/> &amp; <see cref="ChatNetworkRouter"/> Public (<c>#F8F9FA</c>) &amp; Whisper (<c>#C77DFF</c>, <c>[RpcTarget]</c>),
    ///   rich-text stripping, <c>120</c>-char &amp; <c>120</c>-byte UTF-8 clamping, whitespace/bare-<c>/w</c>/self/unknown edge cases
    /// - Story 12-3: <see cref="ChatRateLimiter"/> (<c>5 msgs / 3.0s</c> + end-to-end <c>#FFB703</c> <c>[SYSTEM]</c> warning) &amp;
    ///   <see cref="ChatMessageRingBuffer"/> (<c>50</c>-row <c>O(1)</c> modulo pool, zero runtime <c>Instantiate</c>/<c>Destroy</c>)
    /// </summary>
    [TestFixture]
    public sealed class ChatSystemTests
    {
        private GameObject _rootGo;
        private GameObject _matchGo;
        private InputFocusManager _focusManager;
        private MatchManager _matchManager;
        private ChatNetworkRouter _router;
        private ChatViewController _chatView;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("ChatTestRoot");
            _matchGo = new GameObject("ChatMatchHost");
            _focusManager = new InputFocusManager();
            _matchManager = _matchGo.AddComponent<MatchManager>();
            _matchManager.ConfigureServices(_focusManager, "CHAT01", PlayerRef.FromEncoded(1));

            _router = _matchGo.AddComponent<ChatNetworkRouter>();
            _chatView = _rootGo.AddComponent<ChatViewController>();

            var roster = new Dictionary<string, PlayerRef>
            {
                { "Player_1", PlayerRef.FromEncoded(1) },
                { "Player_2", PlayerRef.FromEncoded(2) },
                { "Player_3", PlayerRef.FromEncoded(3) }
            };
            _router.ConfigureRosterForSession(PlayerRef.FromEncoded(1), "Player_1", roster);
            _chatView.Initialize(_focusManager, _router, new ChatRateLimiter(), _matchManager);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rootGo != null)
            {
                Object.DestroyImmediate(_rootGo);
            }

            if (_matchGo != null)
            {
                Object.DestroyImmediate(_matchGo);
            }
        }

        [Test]
        public void ChatViewController_CanvasGroupClickThrough_WakeTimerReset_AndMatchEndUnfocus_EnforcesAllStates()
        {
            // Initial Passive state (alpha 0.65, interactable=false, blocksRaycasts=false, activeSelf=true)
            Assert.That(_chatView.gameObject.activeSelf, Is.True);
            Assert.That(_chatView.CurrentAlpha, Is.EqualTo(ChatViewController.PassiveAlpha).Within(0.001f));
            Assert.That(_chatView.PanelCanvasGroup.interactable, Is.False);
            Assert.That(_chatView.PanelCanvasGroup.blocksRaycasts, Is.False);
            Assert.That(_focusManager.IsGameplayInputAllowed, Is.True);

            // Enter focuses chat -> alpha 1.0, interactable=true, blocksRaycasts=true, gameplay input suppressed
            _chatView.HandleEnterPressed(0f);
            Assert.That(_chatView.IsChatFocused, Is.True);
            Assert.That(_chatView.CurrentAlpha, Is.EqualTo(ChatViewController.FocusedAlpha).Within(0.001f));
            Assert.That(_chatView.PanelCanvasGroup.interactable, Is.True);
            Assert.That(_chatView.PanelCanvasGroup.blocksRaycasts, Is.True);
            Assert.That(_focusManager.IsGameplayInputAllowed, Is.False);
            Assert.That(_focusManager.ActiveFlags.HasFlag(InputFocusFlag.ChatFocused), Is.True);

            // Escape clears draft and returns to click-through Passive (0.65) while keeping activeSelf == true
            _chatView.InputField.SetTextWithoutNotify("unsent draft");
            _chatView.HandleEscapePressed();
            Assert.That(_chatView.IsChatFocused, Is.False);
            Assert.That(_chatView.gameObject.activeSelf, Is.True);
            Assert.That(_chatView.InputField.text, Is.EqualTo(string.Empty));
            Assert.That(_chatView.CurrentAlpha, Is.EqualTo(ChatViewController.PassiveAlpha).Within(0.001f));
            Assert.That(_chatView.PanelCanvasGroup.interactable, Is.False);
            Assert.That(_chatView.PanelCanvasGroup.blocksRaycasts, Is.False);

            // Partial idle (4.0s) followed by incoming message wakes to 0.75 (click-through!) AND resets 6.0s timer!
            _chatView.TickIdleFade(4.0f);
            Assert.That(_chatView.CurrentVisualState, Is.EqualTo(ChatVisualState.Passive));

            _router.DeliverIncomingWhisperLocally("Player_2", "Flank left!");
            Assert.That(_chatView.IsChatFocused, Is.False);
            Assert.That(_chatView.CurrentVisualState, Is.EqualTo(ChatVisualState.IncomingWake));
            Assert.That(_chatView.CurrentAlpha, Is.EqualTo(ChatViewController.IncomingWakeAlpha).Within(0.001f));
            Assert.That(_chatView.PanelCanvasGroup.interactable, Is.False);
            Assert.That(_chatView.PanelCanvasGroup.blocksRaycasts, Is.False);

            // Another 4.0s is NOT enough to minimize because wake reset _idleTimerSec to 0; +2.1s reaches >= 6.0s -> 0.35!
            _chatView.TickIdleFade(4.0f);
            Assert.That(_chatView.CurrentVisualState, Is.EqualTo(ChatVisualState.IncomingWake));
            _chatView.TickIdleFade(2.1f);
            Assert.That(_chatView.CurrentVisualState, Is.EqualTo(ChatVisualState.Minimized));
            Assert.That(_chatView.CurrentAlpha, Is.EqualTo(ChatViewController.MinimizedAlpha).Within(0.001f));
            Assert.That(_chatView.PanelCanvasGroup.interactable, Is.False);
            Assert.That(_chatView.PanelCanvasGroup.blocksRaycasts, Is.False);
            Assert.That(_chatView.gameObject.activeSelf, Is.True);

            // MatchState.Ended automatically unfocuses active chat
            _chatView.SetChatFocus(true);
            Assert.That(_chatView.IsChatFocused, Is.True);
            _matchManager.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(1), "Player_1", isAlive: true);
            _matchManager.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(2), "Player_2", isAlive: true);
            _matchManager.SimulateStandaloneTicks(MatchManager.CountdownDurationTicks + 2);
            _matchManager.RegisterOrUpdateStandalonePlayer(PlayerRef.FromEncoded(2), "Player_2", isAlive: false);
            _matchManager.EvaluateWinConditions(isTimeoutSuddenDeath: false);
            Assert.That(_matchManager.CurrentMatchState, Is.EqualTo(MatchState.Ended));
            Assert.That(_chatView.IsChatFocused, Is.False, "MatchState.Ended must auto-unfocus chat.");
        }

        [Test]
        public void ChatViewController_EnterEmptyVsNonEmpty_UiButtons_RebindBlock_AndTabQuickReply()
        {
            // ToggleButton (BTN_ChatToggle) opens and closes focus cleanly
            _chatView.ToggleButton.onClick.Invoke();
            Assert.That(_chatView.IsChatFocused, Is.True);

            // Pressing Enter on whitespace-only input unfocuses with 0 RPCs and 0 rate-limit tokens consumed!
            _chatView.InputField.SetTextWithoutNotify("    ");
            _chatView.HandleEnterPressed(1.0f);
            Assert.That(_chatView.IsChatFocused, Is.False);
            Assert.That(_router.PublicRpcSentCount, Is.EqualTo(0));
            Assert.That(_chatView.RateLimiter.CurrentTokens, Is.EqualTo(ChatRateLimiter.DefaultCapacity).Within(0.001f));

            // Pressing SendButton while focused with non-empty text dispatches, clears draft, and returns to Passive
            _chatView.ToggleButton.onClick.Invoke();
            _chatView.InputField.SetTextWithoutNotify("Good luck!");
            _chatView.SendButton.onClick.Invoke();
            Assert.That(_chatView.IsChatFocused, Is.False);
            Assert.That(_chatView.InputField.text, Is.EqualTo(string.Empty));
            Assert.That(_router.PublicRpcSentCount, Is.EqualTo(1));

            // Blocked while RebindModalOpen is active
            _focusManager.SetFocusFlag(InputFocusFlag.RebindModalOpen, true);
            _chatView.HandleEnterPressed(2.0f);
            Assert.That(_chatView.IsChatFocused, Is.False);

            // Tab quick-reply populates "/w <LastSender> "
            _focusManager.SetFocusFlag(InputFocusFlag.RebindModalOpen, false);
            _router.DeliverIncomingWhisperLocally("Player_2", "Behind pillar!");
            _chatView.SetChatFocus(true);
            Assert.That(_chatView.HandleTabQuickReply(), Is.True);
            Assert.That(_chatView.InputField.text, Is.EqualTo("/w Player_2 "));
        }

        [Test]
        public void ChatCommandParserAndRouter_PublicAndWhisperColorFormatting_Utf8ByteClamp_AndEdgeCases()
        {
            // Public broadcast dispatch -> PublicRpcSentCount == 1, formatted "[Player_1]: ..." in PublicOffWhite (#F8F9FA)
            ParsedChatCommand pubSent = _router.SubmitRawChatInput("Hello <size=200>world</size>");
            Assert.That(pubSent.Kind, Is.EqualTo(ChatChannelKind.Public));
            Assert.That(_router.PublicRpcSentCount, Is.EqualTo(1));
            Assert.That(_chatView.RingBuffer.GetVisibleLineAtOrder(0), Is.EqualTo("[Player_1]: Hello size=200world/size"));
            Assert.That(_chatView.RingBuffer.GetVisibleColorAtOrder(0), Is.EqualTo(UiThemeAndFontProvider.PublicOffWhite));

            // Outgoing whisper echo "[To Player_3]: ..." AND incoming whisper "[From Player_2]: ..." render in WhisperViolet (#C77DFF)
            ParsedChatCommand whisperSent = _router.SubmitRawChatInput("/W player_3 Secret flank!");
            Assert.That(whisperSent.Kind, Is.EqualTo(ChatChannelKind.Whisper));
            Assert.That(_router.WhisperRpcSentCount, Is.EqualTo(1));
            Assert.That(_router.LastWhisperTargetSent, Is.EqualTo(PlayerRef.FromEncoded(3)));
            Assert.That(_chatView.RingBuffer.GetVisibleLineAtOrder(1), Is.EqualTo("[To Player_3]: Secret flank!"));
            Assert.That(_chatView.RingBuffer.GetVisibleColorAtOrder(1), Is.EqualTo(UiThemeAndFontProvider.WhisperViolet));

            _router.DeliverIncomingWhisperLocally("Player_2", "Copy that");
            Assert.That(_chatView.RingBuffer.GetVisibleLineAtOrder(2), Is.EqualTo("[From Player_2]: Copy that"));
            Assert.That(_chatView.RingBuffer.GetVisibleColorAtOrder(2), Is.EqualTo(UiThemeAndFontProvider.WhisperViolet));

            // Edge cases: whitespace-only public ("   "), bare "/w", empty whisper body, self-whisper, unknown target
            Assert.That(_router.SubmitRawChatInput("     ").Kind, Is.EqualTo(ChatChannelKind.Ignored));
            Assert.That(_router.SubmitRawChatInput("/w").Kind, Is.EqualTo(ChatChannelKind.LocalSystemError));
            Assert.That(_router.SubmitRawChatInput("/w Player_2   ").Kind, Is.EqualTo(ChatChannelKind.Ignored));
            Assert.That(_router.SubmitRawChatInput("/w Player_1 self").Kind, Is.EqualTo(ChatChannelKind.LocalSystemError));
            Assert.That(_router.SubmitRawChatInput("/whisper Unknown99 hi").Kind, Is.EqualTo(ChatChannelKind.LocalSystemError));
            Assert.That(_router.PublicRpcSentCount, Is.EqualTo(1));
            Assert.That(_router.WhisperRpcSentCount, Is.EqualTo(1));

            // ASCII 200-char clamp -> 120 chars AND Multi-byte UTF-8 CJK clamp -> <= 120 UTF-8 bytes for NetworkString<_128>!
            string asciiClamped = ChatCommandParser.SanitizeMessageBody(new string('A', 200));
            Assert.That(asciiClamped.Length, Is.EqualTo(ChatCommandParser.MaxMessageLength));

            string cjkInput = new string('漢', 90); // 270 UTF-8 bytes (3 bytes/char)
            string cjkClamped = ChatCommandParser.SanitizeMessageBody(cjkInput);
            Assert.That(Encoding.UTF8.GetByteCount(cjkClamped), Is.LessThanOrEqualTo(ChatCommandParser.MaxUtf8PayloadBytes));
            Assert.That(cjkClamped.Length, Is.EqualTo(40));

            // Reflection check: [RpcTarget] PlayerRef on Rpc_SendTargetedWhisper
            MethodInfo method = typeof(ChatNetworkRouter).GetMethod(
                nameof(ChatNetworkRouter.Rpc_SendTargetedWhisper),
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(PlayerRef)));
            Assert.That(parameters[0].GetCustomAttribute<RpcTargetAttribute>(), Is.Not.Null);
        }

        [Test]
        public void ChatRateLimiter_EndToEndChatViewBurst_Suppresses6thRpcAndRendersSolarAmberSystemWarning()
        {
            // Standalone token-bucket math verification
            var limiter = new ChatRateLimiter(5.0f, 3.0f);
            for (int i = 0; i < 5; i++)
            {
                Assert.That(limiter.TryConsume(0.0f), Is.True);
            }

            Assert.That(limiter.TryConsume(0.0f), Is.False);
            Assert.That(limiter.TryConsume(0.5f), Is.False);
            Assert.That(limiter.TryConsume(0.65f), Is.True);
            limiter.TryConsume(300.0f);
            Assert.That(limiter.CurrentTokens, Is.LessThanOrEqualTo(5.0f));

            // End-to-end ChatViewController burst: 5 messages at t=10.0s send RPCs; 6th is suppressed & pushes #FFB703 [SYSTEM] warning!
            for (int m = 1; m <= 5; m++)
            {
                Assert.That(_chatView.TrySubmitMessage($"Burst_{m}", 10.0f), Is.True);
            }

            Assert.That(_router.PublicRpcSentCount, Is.EqualTo(5));
            Assert.That(_chatView.TrySubmitMessage("Burst_6_Spam", 10.0f), Is.False);
            Assert.That(_router.PublicRpcSentCount, Is.EqualTo(5), "Rate-limited 6th message must NOT send an RPC.");

            int lastIdx = _chatView.RingBuffer.ActiveRowCount - 1;
            string warningLine = _chatView.RingBuffer.GetVisibleLineAtOrder(lastIdx);
            Color warningColor = _chatView.RingBuffer.GetVisibleColorAtOrder(lastIdx);
            Assert.That(warningLine, Does.StartWith("[SYSTEM]"));
            Assert.That(warningColor, Is.EqualTo(UiThemeAndFontProvider.SolarAmber));
        }

        [Test]
        public void ChatMessageRingBuffer_Prewarms50RowsAndRecyclesOldestInO1WithZeroInstantiate()
        {
            ChatMessageRingBuffer ring = _chatView.RingBuffer;
            Assert.That(ring.Capacity, Is.EqualTo(50));
            Assert.That(ring.ContentContainer.childCount, Is.EqualTo(50));
            Assert.That(ring.ActiveRowCount, Is.EqualTo(0));

            for (int i = 1; i <= 75; i++)
            {
                var label = ring.PushFormattedMessage("[P1]: ", $"Line_{i}", Color.white);
                Assert.That(label.richText, Is.False);
            }

            Assert.That(ring.ContentContainer.childCount, Is.EqualTo(50));
            Assert.That(ring.ActiveRowCount, Is.EqualTo(50));
            Assert.That(ring.RuntimeInstantiateCallsAfterPrewarm, Is.EqualTo(0));
            Assert.That(ring.RuntimeDestroyCalls, Is.EqualTo(0));
            Assert.That(ring.GetVisibleLineAtOrder(0), Is.EqualTo("[P1]: Line_26"));
            Assert.That(ring.GetVisibleLineAtOrder(49), Is.EqualTo("[P1]: Line_75"));
        }
    }
}
