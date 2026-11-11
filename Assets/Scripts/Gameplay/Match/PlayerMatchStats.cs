using System.Runtime.InteropServices;
using Fusion;

namespace CapsuleArena.Gameplay.Match
{
    /// <summary>
    /// Networked match lifecycle phases governed by <see cref="MatchManager"/> (Story 9-1).
    /// </summary>
    public enum MatchState : byte
    {
        /// <summary>Waiting in arena/lobby until at least 2 players connect.</summary>
        WaitingForPlayers = 0,

        /// <summary>3.0-second (180-tick) pre-round countdown; aborts if connected players drop below 2.</summary>
        Countdown = 1,

        /// <summary>Active 180.0-second (10,800-tick) Last Man Standing combat phase.</summary>
        Playing = 2,

        /// <summary>8.0-second (480-tick) post-match summary overlay before automatic reset/rematch.</summary>
        Ended = 3
    }

    /// <summary>
    /// Blittable per-player match statistics replicated inside <c>MatchManager.MatchRoster</c>
    /// so any surviving peer can inherit <c>StateAuthority</c> without losing kills or alive flags
    /// (Stories 9-1, 9-2, 9-3; ADR-0006; TR-match-001..003).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayerMatchStats : INetworkStruct
    {
        /// <summary>True while this player is connected and has CurrentHP &gt; 0.</summary>
        public NetworkBool IsAlive;

        /// <summary>Confirmed eliminations scored during the active match.</summary>
        public int Kills;

        /// <summary>Final placement rank (1 = Winner, 2..4 = eliminated order).</summary>
        public int PlacementRank;

        /// <summary>Synchronized player display name (3..16 chars).</summary>
        public NetworkString<_16> DisplayName;
    }

    /// <summary>
    /// Lightweight stack-allocated candidate passed into <see cref="MatchManager.ResolveWinnerOrDraw"/>.
    /// </summary>
    public readonly struct SurvivorCandidate
    {
        /// <summary>Combatant's <see cref="PlayerRef"/>.</summary>
        public readonly PlayerRef Player;

        /// <summary>Combatant's remaining hit points at evaluation time.</summary>
        public readonly int CurrentHP;

        /// <summary>Combatant's confirmed kills in the current match.</summary>
        public readonly int Kills;

        /// <summary>Initializes a new <see cref="SurvivorCandidate"/> evaluation entry.</summary>
        public SurvivorCandidate(PlayerRef player, int currentHp, int kills)
        {
            Player = player;
            CurrentHP = currentHp;
            Kills = kills;
        }
    }

    /// <summary>
    /// Immutable match summary payload broadcast to Combat HUD and <c>PlayFabStatsService</c> when a match enters <see cref="MatchState.Ended"/>.
    /// </summary>
    public readonly struct MatchResultSummary
    {
        /// <summary>Deterministic unique match identifier preventing duplicate PlayFab stat submissions.</summary>
        public readonly string MatchId;

        /// <summary>Winning <see cref="PlayerRef"/>, or <see cref="PlayerRef.None"/> on a Draw.</summary>
        public readonly PlayerRef WinnerPlayer;

        /// <summary>Display name of the winning player (or "DRAW" when <see cref="WinnerPlayer"/> is None).</summary>
        public readonly string WinnerDisplayName;

        /// <summary>True when no single winner emerged (mutual double-KO or tied Sudden Death).</summary>
        public readonly bool IsDraw;

        /// <summary>True when the local player is <see cref="WinnerPlayer"/>.</summary>
        public readonly bool LocalDidWin;

        /// <summary>Confirmed kills scored by the local player in this match.</summary>
        public readonly int LocalKills;

        /// <summary>Final placement rank of the local player (1..4).</summary>
        public readonly int LocalPlacementRank;

        /// <summary>Initializes an immutable <see cref="MatchResultSummary"/> broadcast payload.</summary>
        public MatchResultSummary(
            string matchId,
            PlayerRef winnerPlayer,
            string winnerDisplayName,
            bool isDraw,
            bool localDidWin,
            int localKills,
            int localPlacementRank)
        {
            MatchId = matchId;
            WinnerPlayer = winnerPlayer;
            WinnerDisplayName = winnerDisplayName;
            IsDraw = isDraw;
            LocalDidWin = localDidWin;
            LocalKills = localKills;
            LocalPlacementRank = localPlacementRank;
        }
    }
}
