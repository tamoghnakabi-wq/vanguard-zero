using System;
using UnityEngine;

namespace VanguardZero.Animation
{
    /// <summary>
    /// Tactical weapon hold style used by <see cref="TacticalUpperBodyAimIK"/> to match
    /// VANGUARD ZERO's three weapons (VX-47 Assault Rifle, M890 Shotgun, M19 Pistol).
    /// </summary>
    public enum WeaponHoldStyle : byte
    {
        AssaultRifle = 0,
        Shotgun = 1,
        Pistol = 2,
    }

    /// <summary>
    /// Per-frame locomotion & combat state snapshot fed into <see cref="MotionBricksAnimatorDriver"/>
    /// from either a local/AI character controller or NetworkPlayerBridge / NetworkBotSync.
    /// Zero dependency on NGO types so Animation compiles standalone.
    /// </summary>
    [Serializable]
    public struct MotionBrickDriveState
    {
        public Vector3 WorldVelocity;
        public float AimPitchDegrees;
        public float CharacterYawDegrees;
        public bool IsGrounded;
        public bool IsCrouching;
        public bool IsSprinting;
        public bool IsSliding;
        public bool IsAimingDownSights;
        public bool IsReloading;
        public bool IsDead;
        public WeaponHoldStyle WeaponStyle;
    }

    /// <summary>
    /// Footstep audio event raised from MotionBricks neural foot-contact AnimationEvents.
    /// Subscribe from AudioManager / CharacterAudioBridge to play surface-aware footsteps.
    /// </summary>
    [Serializable]
    public struct MotionBricksFootstepPayload
    {
        public bool IsLeftFoot;
        public Vector3 WorldPosition;
        public Vector3 SurfaceNormal;
        public string SurfaceTag;
        public float Intensity;
        public bool IsCrouching;
        public bool IsSprinting;
    }
}
