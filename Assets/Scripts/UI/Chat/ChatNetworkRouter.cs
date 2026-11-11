using System;
using System.Collections.Generic;
using CapsuleArena.Gameplay.Match;
using Fusion;
using UnityEngine;

namespace CapsuleArena.UI.Chat
{
    /// <summary>
    /// Networked Photon Fusion 2 RPC chat router delivering Public messages (<c>#F8F9FA</c>) to <see cref="RpcTargets.All"/>
    /// and Private Whispers (<c>#C77DFF</c>) strictly to the intended recipient via <c>[RpcTarget] PlayerRef target</c>
    /// so uninvolved clients never receive whisper packets over the wire (Story 12-2; ADR-0007; TR-chat-002).
    /// </summary>
    public sealed class ChatNetworkRouter : NetworkBehaviour
    {
        private readonly Dictionary<string, PlayerRef> _rosterLookup =
            new Dictionary<string, PlayerRef>(StringComparer.OrdinalIgnoreCase);

        [SerializeField] private MatchManager _matchManager;
        private PlayerRef _standaloneLocalPlayer = PlayerRef.None;
        private string _standaloneLocalDisplayName = "LocalPlayer";
        private string _lastWhisperSenderName = string.Empty;
        private int _publicRpcSentCount;
        private int _whisperRpcSentCount;
        private PlayerRef _lastWhisperTargetSent = PlayerRef.None;
        private bool _isNetworkSpawned;

        /// <summary>Raised locally when a public message arrives: (senderName, sanitizedBody).</summary>
        public event Action<string, string> OnPublicMessageReceived;

        /// <summary>Raised locally when an incoming whisper arrives for this player: (senderName, sanitizedBody).</summary>
        public event Action<string, string> OnIncomingWhisperReceived;

        /// <summary>Raised locally on the sender when an outgoing whisper is dispatched: (targetName, sanitizedBody).</summary>
        public event Action<string, string> OnOutgoingWhisperEchoed;

        /// <summary>Raised locally when a command produces a local <c>[SYSTEM]</c> validation/rate-limit notice.</summary>
        public event Action<string> OnSystemFeedbackGenerated;

        /// <summary>Global static observer event so <see cref="ChatViewController"/> receives routed chat across scene/prefab instances.</summary>
        public static event Action<ChatChannelKind, string, string> OnAnyChatLineDelivered;

        /// <summary>Gets the most recent whisper sender's display name for <c>Tab</c> quick-reply.</summary>
        public string LastWhisperSenderName => _lastWhisperSenderName;

        /// <summary>Gets the total number of outgoing public chat RPCs dispatched.</summary>
        public int PublicRpcSentCount => _publicRpcSentCount;

        /// <summary>Gets the total number of outgoing targeted whisper RPCs dispatched.</summary>
        public int WhisperRpcSentCount => _whisperRpcSentCount;

        /// <summary>Gets the <see cref="PlayerRef"/> targeted by the most recent outgoing whisper.</summary>
        public PlayerRef LastWhisperTargetSent => _lastWhisperTargetSent;

        private bool IsSpawnedOnRunner => _isNetworkSpawned && Object != null && Runner != null && Object.IsValid;
        private PlayerRef EffectiveLocalPlayer => IsSpawnedOnRunner ? Runner.LocalPlayer : _standaloneLocalPlayer;

        /// <inheritdoc />
        public override void Spawned()
        {
            _isNetworkSpawned = true;
        }

        /// <inheritdoc />
        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _isNetworkSpawned = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticChatRouterEvents()
        {
            OnAnyChatLineDelivered = null;
        }

        /// <summary>
        /// Configures standalone roster and local player identity for EditMode/PlayMode unit tests or scene wiring.
        /// </summary>
        public void ConfigureRosterForSession(
            PlayerRef localPlayer,
            string localDisplayName,
            IReadOnlyDictionary<string, PlayerRef> explicitRoster = null,
            MatchManager matchManager = null)
        {
            _standaloneLocalPlayer = localPlayer;
            _standaloneLocalDisplayName = string.IsNullOrWhiteSpace(localDisplayName) ? "Player_1" : localDisplayName.Trim();
            _matchManager = matchManager;

            _rosterLookup.Clear();
            if (explicitRoster != null)
            {
                foreach (KeyValuePair<string, PlayerRef> kvp in explicitRoster)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        _rosterLookup[kvp.Key.Trim()] = kvp.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Parses, sanitizes, and routes <paramref name="rawInput"/> either as a Public broadcast RPC,
        /// a targeted <c>[RpcTarget]</c> Whisper RPC (+ local <c>[To Name]</c> echo), or a local <c>[SYSTEM]</c> error.
        /// Returns the resulting <see cref="ParsedChatCommand"/>.
        /// </summary>
        public ParsedChatCommand SubmitRawChatInput(string rawInput)
        {
            RefreshRosterFromMatchManagerIfAvailable();
            ParsedChatCommand parsed = ChatCommandParser.ParseAndSanitize(rawInput, _rosterLookup, EffectiveLocalPlayer);

            switch (parsed.Kind)
            {
                case ChatChannelKind.Ignored:
                    return parsed;

                case ChatChannelKind.LocalSystemError:
                    OnSystemFeedbackGenerated?.Invoke(parsed.LocalErrorMessage);
                    OnAnyChatLineDelivered?.Invoke(ChatChannelKind.LocalSystemError, string.Empty, parsed.LocalErrorMessage);
                    return parsed;

                case ChatChannelKind.Public:
                    DispatchPublicMessage(parsed.SanitizedBody);
                    return parsed;

                case ChatChannelKind.Whisper:
                    DispatchTargetedWhisper(parsed.TargetPlayer, parsed.TargetDisplayName, parsed.SanitizedBody);
                    return parsed;

                default:
                    return parsed;
            }
        }

        /// <summary>
        /// Broadcasts a sanitized public chat message (<c>NetworkString&lt;_128&gt;</c>) to all peers in the room.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void Rpc_BroadcastPublicChat(
            PlayerRef sender,
            NetworkString<_128> senderDisplayName,
            NetworkString<_128> sanitizedBody)
        {
            string cleanSender = ChatCommandParser.SanitizeMessageBody(senderDisplayName.ToString(), 24);
            string cleanBody = ChatCommandParser.SanitizeMessageBody(sanitizedBody.ToString(), ChatCommandParser.MaxMessageLength);
            if (string.IsNullOrEmpty(cleanBody))
            {
                return;
            }

            if (string.IsNullOrEmpty(cleanSender))
            {
                cleanSender = $"Player_{Mathf.Max(1, sender.PlayerId)}";
            }

            DeliverPublicLocally(cleanSender, cleanBody);
        }

        /// <summary>
        /// Delivers a private whisper exclusively to <paramref name="target"/> at the Fusion transport layer
        /// via <see cref="RpcTargetAttribute"/> so uninvolved peers never receive the packet.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void Rpc_SendTargetedWhisper(
            [RpcTarget] PlayerRef target,
            PlayerRef sender,
            NetworkString<_128> senderDisplayName,
            NetworkString<_128> sanitizedBody)
        {
            string cleanSender = ChatCommandParser.SanitizeMessageBody(senderDisplayName.ToString(), 24);
            string cleanBody = ChatCommandParser.SanitizeMessageBody(sanitizedBody.ToString(), ChatCommandParser.MaxMessageLength);
            if (string.IsNullOrEmpty(cleanBody))
            {
                return;
            }

            if (string.IsNullOrEmpty(cleanSender))
            {
                cleanSender = $"Player_{Mathf.Max(1, sender.PlayerId)}";
            }

            DeliverIncomingWhisperLocally(cleanSender, cleanBody);
        }

        /// <summary>Simulates receiving an incoming whisper on the targeted recipient client (for unit tests).</summary>
        public void DeliverIncomingWhisperLocally(string senderDisplayName, string sanitizedBody)
        {
            string cleanSender = ChatCommandParser.SanitizeMessageBody(senderDisplayName, 24);
            string cleanBody = ChatCommandParser.SanitizeMessageBody(sanitizedBody, ChatCommandParser.MaxMessageLength);
            if (string.IsNullOrEmpty(cleanBody))
            {
                return;
            }

            _lastWhisperSenderName = cleanSender;
            string prefix = $"[From {cleanSender}]: ";
            OnIncomingWhisperReceived?.Invoke(cleanSender, cleanBody);
            OnAnyChatLineDelivered?.Invoke(ChatChannelKind.Whisper, prefix, cleanBody);
        }

        private void DispatchPublicMessage(string sanitizedBody)
        {
            _publicRpcSentCount++;
            string senderName = ResolveLocalSenderName();

            if (IsSpawnedOnRunner)
            {
                Rpc_BroadcastPublicChat(EffectiveLocalPlayer, senderName, sanitizedBody);
                return;
            }

            DeliverPublicLocally(senderName, sanitizedBody);
        }

        private void DispatchTargetedWhisper(PlayerRef target, string targetDisplayName, string sanitizedBody)
        {
            _whisperRpcSentCount++;
            _lastWhisperTargetSent = target;
            string senderName = ResolveLocalSenderName();

            // Local echo on sender ("[To Name]: ..."):
            string echoPrefix = $"[To {targetDisplayName}]: ";
            OnOutgoingWhisperEchoed?.Invoke(targetDisplayName, sanitizedBody);
            OnAnyChatLineDelivered?.Invoke(ChatChannelKind.Whisper, echoPrefix, sanitizedBody);

            if (IsSpawnedOnRunner)
            {
                Rpc_SendTargetedWhisper(target, EffectiveLocalPlayer, senderName, sanitizedBody);
            }
        }

        private void DeliverPublicLocally(string senderName, string cleanBody)
        {
            string prefix = $"[{senderName}]: ";
            OnPublicMessageReceived?.Invoke(senderName, cleanBody);
            OnAnyChatLineDelivered?.Invoke(ChatChannelKind.Public, prefix, cleanBody);
        }

        private string ResolveLocalSenderName()
        {
            if (_matchManager != null &&
                EffectiveLocalPlayer != PlayerRef.None &&
                _matchManager.TryGetPlayerStats(EffectiveLocalPlayer, out PlayerMatchStats stats))
            {
                string n = stats.DisplayName.ToString();
                if (!string.IsNullOrWhiteSpace(n))
                {
                    return n.Trim();
                }
            }

            return _standaloneLocalDisplayName;
        }

        private void RefreshRosterFromMatchManagerIfAvailable()
        {
            if (_matchManager == null || !IsSpawnedOnRunner)
            {
                return;
            }

            _rosterLookup.Clear();
            foreach (KeyValuePair<PlayerRef, PlayerMatchStats> kvp in _matchManager.MatchRoster)
            {
                string name = kvp.Value.DisplayName.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _rosterLookup[name.Trim()] = kvp.Key;
                }
            }
        }
    }
}
