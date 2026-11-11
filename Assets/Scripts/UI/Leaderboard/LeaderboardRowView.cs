using System.Text;
using CapsuleArena.Cloud;
using CapsuleArena.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CapsuleArena.UI.Leaderboard
{
    /// <summary>
    /// Pre-warmed UI row item representing one entry in the Top-20 PlayFab Leaderboard Modal
    /// with Gold (<c>#FFD166</c>), Silver (<c>#E0E1DD</c>), Bronze (<c>#CD7F32</c>) podium badges and
    /// Electric Cyan (<c>#00F5D4</c>) local-player row highlighting (Story 13-3; ADR-0008; TR-playfab-003).
    /// </summary>
    public sealed class LeaderboardRowView : MonoBehaviour
    {
        /// <summary>Rank #1 Gold badge color (#FFD166).</summary>
        public static readonly Color RankGoldColor = new Color32(255, 209, 102, 255);

        /// <summary>Rank #2 Silver badge color (#E0E1DD).</summary>
        public static readonly Color RankSilverColor = new Color32(224, 225, 221, 255);

        /// <summary>Rank #3 Bronze badge color (#CD7F32).</summary>
        public static readonly Color RankBronzeColor = new Color32(205, 127, 50, 255);

        /// <summary>Local player row background tint (#00F5D4 at 22% alpha).</summary>
        public static readonly Color LocalRowHighlightBg = new Color32(0, 245, 212, 56);

        /// <summary>Alternating even row background (#1E293B).</summary>
        public static readonly Color EvenRowBg = new Color32(30, 41, 59, 210);

        /// <summary>Alternating odd row background (#152033).</summary>
        public static readonly Color OddRowBg = new Color32(21, 32, 51, 210);

        [SerializeField] private Image _rowBackground;
        [SerializeField] private Image _leftAccentBar;
        [SerializeField] private TMP_Text _rankText;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _scoreText;

        private bool _isLocalPlayerRow;

        /// <summary>Gets the current rank label text (e.g., "#1").</summary>
        public string RankLabel => _rankText != null ? _rankText.text : string.Empty;

        /// <summary>Gets the current rank text color.</summary>
        public Color RankColor => _rankText != null ? _rankText.color : Color.clear;

        /// <summary>Gets the displayed player name.</summary>
        public string DisplayedPlayerName => _nameText != null ? _nameText.text : string.Empty;

        /// <summary>Gets the displayed score text.</summary>
        public string DisplayedScore => _scoreText != null ? _scoreText.text : string.Empty;

        /// <summary>Gets a value indicating whether this row is highlighted as the local player.</summary>
        public bool IsHighlightedAsLocal => _isLocalPlayerRow;

        /// <summary>Gets the left accent bar color (Electric Cyan for local player, podium tint for Top 3).</summary>
        public Color AccentColor => _leftAccentBar != null ? _leftAccentBar.color : Color.clear;

        /// <summary>Wires internal sub-controls created by <see cref="LeaderboardModalView"/>.</summary>
        public void BindControls(
            Image background,
            Image leftAccentBar,
            TMP_Text rankText,
            TMP_Text nameText,
            TMP_Text scoreText)
        {
            _rowBackground = background;
            _leftAccentBar = leftAccentBar;
            _rankText = rankText;
            _nameText = nameText;
            _scoreText = scoreText;
        }

        /// <summary>Binds a <see cref="LeaderboardEntryDto"/> into this pre-warmed row with 0 runtime allocations.</summary>
        public void Populate(LeaderboardEntryDto entry, int rowIndex, bool isLocalPlayer, StringBuilder sb)
        {
            gameObject.SetActive(true);
            _isLocalPlayerRow = isLocalPlayer;
            int oneBasedRank = entry.Position + 1;
            Color rankColor = ResolveRankColor(oneBasedRank);

            PopulateTextCells(entry, oneBasedRank, isLocalPlayer, rankColor, sb);
            ApplyRowStyling(rowIndex, oneBasedRank, isLocalPlayer, rankColor);
        }

        /// <summary>Hides this pooled row without destroying its GameObject.</summary>
        public void HideRow()
        {
            _isLocalPlayerRow = false;
            gameObject.SetActive(false);
        }

        /// <summary>Resolves the canonical podium or frost color for 1-based rank positions.</summary>
        public static Color ResolveRankColor(int oneBasedRank)
        {
            return oneBasedRank switch
            {
                1 => RankGoldColor,
                2 => RankSilverColor,
                3 => RankBronzeColor,
                _ => UiThemeAndFontProvider.PureFrost
            };
        }

        private void PopulateTextCells(
            LeaderboardEntryDto entry,
            int oneBasedRank,
            bool isLocal,
            Color rankColor,
            StringBuilder sb)
        {
            if (_rankText != null)
            {
                sb.Clear();
                sb.Append('#');
                sb.Append(oneBasedRank);
                _rankText.SetText(sb);
                _rankText.color = rankColor;
            }

            if (_nameText != null)
            {
                string safeName = string.IsNullOrEmpty(entry.DisplayName) ? "Guest" : entry.DisplayName;
                _nameText.richText = false;
                _nameText.text = isLocal ? $"{safeName} (YOU)" : safeName;
                _nameText.color = isLocal ? UiThemeAndFontProvider.ElectricCyan : rankColor;
            }

            if (_scoreText != null)
            {
                sb.Clear();
                sb.Append(entry.StatValue);
                _scoreText.SetText(sb);
                _scoreText.color = isLocal ? UiThemeAndFontProvider.ElectricCyan : UiThemeAndFontProvider.PureFrost;
            }
        }

        private void ApplyRowStyling(int rowIndex, int oneBasedRank, bool isLocal, Color rankColor)
        {
            if (_rowBackground != null)
            {
                _rowBackground.color = isLocal
                    ? LocalRowHighlightBg
                    : (rowIndex % 2 == 0 ? EvenRowBg : OddRowBg);
            }

            if (_leftAccentBar != null)
            {
                bool showBar = isLocal || oneBasedRank <= 3;
                _leftAccentBar.enabled = showBar;
                _leftAccentBar.color = isLocal ? UiThemeAndFontProvider.ElectricCyan : rankColor;
            }
        }
    }
}
