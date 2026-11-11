using System;
using System.Collections.Generic;
using System.Text;
using Fusion;

namespace CapsuleArena.UI.Chat
{
    /// <summary>
    /// Classification of a parsed chat submission produced by <see cref="ChatCommandParser"/> (Story 12-2; TR-chat-002).
    /// </summary>
    public enum ChatChannelKind : byte
    {
        /// <summary>Empty or whitespace-only input; ignored with zero network traffic.</summary>
        Ignored = 0,

        /// <summary>Public broadcast message delivered to all peers in <c>#F8F9FA</c>.</summary>
        Public = 1,

        /// <summary>Targeted private whisper delivered exclusively via <c>[RpcTarget] PlayerRef</c> in <c>#C77DFF</c>.</summary>
        Whisper = 2,

        /// <summary>Local validation failure (unknown player, self-whisper, missing syntax); displayed locally only.</summary>
        LocalSystemError = 3
    }

    /// <summary>
    /// Immutable result of <see cref="ChatCommandParser.ParseAndSanitize"/>.
    /// </summary>
    public readonly struct ParsedChatCommand
    {
        /// <summary>Routing classification of the parsed chat command.</summary>
        public readonly ChatChannelKind Kind;

        /// <summary>Resolved recipient <see cref="PlayerRef"/> when <see cref="Kind"/> is <see cref="ChatChannelKind.Whisper"/>.</summary>
        public readonly PlayerRef TargetPlayer;

        /// <summary>Canonical roster display name of the whisper recipient.</summary>
        public readonly string TargetDisplayName;

        /// <summary>Sanitized message body stripped of <c>&lt;</c> and <c>&gt;</c> tags and clamped to 120 characters.</summary>
        public readonly string SanitizedBody;

        /// <summary>Local system feedback text when <see cref="Kind"/> is <see cref="ChatChannelKind.LocalSystemError"/>.</summary>
        public readonly string LocalErrorMessage;

        /// <summary>
        /// Initializes a new <see cref="ParsedChatCommand"/> result value.
        /// </summary>
        public ParsedChatCommand(
            ChatChannelKind kind,
            PlayerRef targetPlayer,
            string targetDisplayName,
            string sanitizedBody,
            string localErrorMessage)
        {
            Kind = kind;
            TargetPlayer = targetPlayer;
            TargetDisplayName = targetDisplayName ?? string.Empty;
            SanitizedBody = sanitizedBody ?? string.Empty;
            LocalErrorMessage = localErrorMessage ?? string.Empty;
        }
    }

    /// <summary>
    /// Pure stateless parser and rich-text sanitizer for Public messages and <c>/w &lt;TargetName&gt; &lt;Body&gt;</c>
    /// (<c>/whisper</c>) commands (Story 12-2; ADR-0007; TR-chat-002).
    /// </summary>
    public static class ChatCommandParser
    {
        /// <summary>Maximum allowed chat message body length in characters (fits inside <c>NetworkString&lt;_128&gt;</c>).</summary>
        public const int MaxMessageLength = 120;

        /// <summary>Maximum UTF-8 encoded payload byte length so multi-byte characters never overflow <c>NetworkString&lt;_128&gt;</c>.</summary>
        public const int MaxUtf8PayloadBytes = 120;

        [ThreadStatic]
        private static StringBuilder _sharedSanitizeBuffer;

        /// <summary>
        /// Trims whitespace, strips <c>&lt;</c> and <c>&gt;</c> characters to block TMP rich-text tag injection,
        /// normalizes control characters, and clamps length to <c>[0, maxLength]</c>.
        /// </summary>
        public static string SanitizeMessageBody(string rawText, int maxLength = MaxMessageLength)
        {
            if (string.IsNullOrWhiteSpace(rawText) || maxLength <= 0)
            {
                return string.Empty;
            }

            StringBuilder sb = _sharedSanitizeBuffer ??= new StringBuilder(160);
            sb.Clear();
            AppendSanitizedCharacters(rawText.Trim(), sb);
            return TrimAndClampBuffer(sb, maxLength);
        }

        /// <summary>
        /// Parses <paramref name="rawInput"/> into a Public message, targeted Whisper, local <c>[SYSTEM]</c> error,
        /// or ignored no-op against the active session <paramref name="roster"/>.
        /// </summary>
        public static ParsedChatCommand ParseAndSanitize(
            string rawInput,
            IReadOnlyDictionary<string, PlayerRef> roster,
            PlayerRef localPlayer)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                return IgnoredCommand();
            }

            string trimmed = rawInput.Trim();
            if (TryMatchWhisperPrefix(trimmed, out string remainder))
            {
                return ParseWhisperRemainder(remainder, roster, localPlayer);
            }

            string publicBody = SanitizeMessageBody(trimmed, MaxMessageLength);
            return string.IsNullOrEmpty(publicBody)
                ? IgnoredCommand()
                : new ParsedChatCommand(ChatChannelKind.Public, PlayerRef.None, string.Empty, publicBody, string.Empty);
        }

        private static void AppendSanitizedCharacters(string trimmed, StringBuilder sb)
        {
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c == '<' || c == '>')
                {
                    continue;
                }

                if (c == '\r' || c == '\n' || c == '\t')
                {
                    sb.Append(' ');
                }
                else if (!char.IsControl(c))
                {
                    sb.Append(c);
                }
            }
        }

        private static string TrimAndClampBuffer(StringBuilder sb, int maxLength)
        {
            while (sb.Length > 0 && char.IsWhiteSpace(sb[0]))
            {
                sb.Remove(0, 1);
            }

            while (sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]))
            {
                sb.Length -= 1;
            }

            if (sb.Length == 0)
            {
                return string.Empty;
            }

            if (sb.Length > maxLength)
            {
                sb.Length = maxLength;
            }

            string candidate = sb.ToString();
            return ClampToUtf8ByteBudget(candidate, MaxUtf8PayloadBytes);
        }

        private static string ClampToUtf8ByteBudget(string text, int maxBytes)
        {
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            {
                return text;
            }

            int byteSum = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int charBytes = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                    ? 4
                    : Encoding.UTF8.GetByteCount(text, i, 1);
                if (byteSum + charBytes > maxBytes)
                {
                    return text.Substring(0, i).TrimEnd();
                }

                byteSum += charBytes;
                if (charBytes == 4)
                {
                    i++;
                }
            }

            return text;
        }

        private static bool TryMatchWhisperPrefix(string trimmed, out string remainder)
        {
            if (trimmed.Equals("/w", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("/whisper", StringComparison.OrdinalIgnoreCase))
            {
                remainder = string.Empty;
                return true;
            }

            if (trimmed.StartsWith("/w ", StringComparison.OrdinalIgnoreCase))
            {
                remainder = trimmed.Substring(3).TrimStart();
                return true;
            }

            if (trimmed.StartsWith("/whisper ", StringComparison.OrdinalIgnoreCase))
            {
                remainder = trimmed.Substring(9).TrimStart();
                return true;
            }

            remainder = string.Empty;
            return false;
        }

        private static ParsedChatCommand ParseWhisperRemainder(
            string remainder,
            IReadOnlyDictionary<string, PlayerRef> roster,
            PlayerRef localPlayer)
        {
            if (string.IsNullOrWhiteSpace(remainder))
            {
                return SystemErrorCommand(string.Empty, "[SYSTEM] Usage: /w <PlayerName> <Message>");
            }

            int firstSpace = remainder.IndexOf(' ');
            if (firstSpace <= 0)
            {
                return IgnoredCommand();
            }

            string rawTargetName = SanitizeMessageBody(remainder.Substring(0, firstSpace), 32);
            string sanitizedBody = SanitizeMessageBody(remainder.Substring(firstSpace + 1), MaxMessageLength);
            if (string.IsNullOrEmpty(sanitizedBody))
            {
                return IgnoredCommand();
            }

            return ResolveWhisperRecipient(roster, localPlayer, rawTargetName, sanitizedBody);
        }

        private static ParsedChatCommand ResolveWhisperRecipient(
            IReadOnlyDictionary<string, PlayerRef> roster,
            PlayerRef localPlayer,
            string rawTargetName,
            string sanitizedBody)
        {
            if (!TryResolvePlayerCaseInsensitive(roster, rawTargetName, out string canonicalName, out PlayerRef targetRef))
            {
                return SystemErrorCommand(rawTargetName, $"[SYSTEM] Player '{rawTargetName}' not found.");
            }

            if (localPlayer != PlayerRef.None && targetRef == localPlayer)
            {
                return SystemErrorCommand(canonicalName, "[SYSTEM] Cannot whisper to yourself.");
            }

            return new ParsedChatCommand(ChatChannelKind.Whisper, targetRef, canonicalName, sanitizedBody, string.Empty);
        }

        private static ParsedChatCommand IgnoredCommand()
        {
            return new ParsedChatCommand(ChatChannelKind.Ignored, PlayerRef.None, string.Empty, string.Empty, string.Empty);
        }

        private static ParsedChatCommand SystemErrorCommand(string targetName, string error)
        {
            return new ParsedChatCommand(ChatChannelKind.LocalSystemError, PlayerRef.None, targetName, string.Empty, error);
        }

        private static bool TryResolvePlayerCaseInsensitive(
            IReadOnlyDictionary<string, PlayerRef> roster,
            string targetName,
            out string canonicalName,
            out PlayerRef resolvedPlayer)
        {
            canonicalName = string.Empty;
            resolvedPlayer = PlayerRef.None;

            if (roster == null || string.IsNullOrEmpty(targetName))
            {
                return false;
            }

            foreach (KeyValuePair<string, PlayerRef> kvp in roster)
            {
                if (string.Equals(kvp.Key, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalName = kvp.Key;
                    resolvedPlayer = kvp.Value;
                    return true;
                }
            }

            return false;
        }
    }
}
