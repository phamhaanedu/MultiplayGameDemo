using System.Runtime.InteropServices;
using Fusion;
using UnityEngine;

namespace CapsuleArena.Core.Input
{
    /// <summary>
    /// Deterministic bit indices inside <see cref="NetworkInputData.Buttons"/> (bits 0..7).
    /// </summary>
    public enum NetworkInputButtons : int
    {
        /// <summary>Bit 0: Single vertical jump (Space / Mobile Jump).</summary>
        Jump = 0,

        /// <summary>Bit 1: Lateral dodge roll left (Q / Mobile Dodge L).</summary>
        DodgeLeft = 1,

        /// <summary>Bit 2: Lateral dodge roll right (E / Mobile Dodge R).</summary>
        DodgeRight = 2,

        /// <summary>Bit 3: Forward burst dash (Key 2 / Mobile Dash).</summary>
        ForwardDash = 3,

        /// <summary>Bit 4: Vertical melee slash (LMB / Mobile Slash).</summary>
        VerticalSlash = 4,

        /// <summary>Bit 5: Horizontal melee sweep left + 90-deg left turn (Key 1 / Mobile Sweep L).</summary>
        HorizontalSweepLeft = 5,

        /// <summary>Bit 6: Horizontal melee sweep right + 90-deg right turn (Key 3 / Mobile Sweep R).</summary>
        HorizontalSweepRight = 6,

        /// <summary>Bit 7: Ranged bolt fire (RMB / Mobile Fire).</summary>
        RangedFire = 7
    }

    /// <summary>
    /// Compact 16-byte blittable input payload polled at 60Hz for Photon Fusion 2 simulation
    /// (Story 3-2; ADR-0002; TR-input-002; Marshal.SizeOf == 16 &lt;= 16 bytes, 4x 32-bit words).
    /// Packs normalized planar movement into a single 32-bit word (two signed 16-bit fixed-point axes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NetworkInputData : INetworkInput
    {
        private const float MoveQuantizationScale = 32767.0f;
        private const float InvMoveQuantizationScale = 1.0f / MoveQuantizationScale;

        /// <summary>Bit-packed planar move axes (low 16 bits = signed X strafe, high 16 bits = signed Y forward).</summary>
        public int PackedMove;

        /// <summary>Horizontal character aim yaw in world degrees.</summary>
        public float LookYaw;

        /// <summary>Vertical camera aim pitch in degrees, clamped to [-80, +80].</summary>
        public float LookPitch;

        /// <summary>Bit-packed combat and mobility action buttons (bits 0..7).</summary>
        public NetworkButtons Buttons;

        /// <summary>
        /// Gets or sets the normalized 2D local planar movement vector (x = strafe, y = forward; ||v|| &lt;= 1.0)
        /// backed by a single 32-bit word so Fusion's 32-bit word weaver keeps the wire struct strictly 16 bytes.
        /// </summary>
        public Vector2 MoveDirection
        {
            get
            {
                short qx = (short)(PackedMove & 0xFFFF);
                short qy = (short)((PackedMove >> 16) & 0xFFFF);
                return new Vector2(qx * InvMoveQuantizationScale, qy * InvMoveQuantizationScale);
            }
            set
            {
                Vector2 clamped = Vector2.ClampMagnitude(value, 1.0f);
                ushort ux = (ushort)(short)Mathf.RoundToInt(clamped.x * MoveQuantizationScale);
                ushort uy = (ushort)(short)Mathf.RoundToInt(clamped.y * MoveQuantizationScale);
                PackedMove = ux | (uy << 16);
            }
        }
    }
}
