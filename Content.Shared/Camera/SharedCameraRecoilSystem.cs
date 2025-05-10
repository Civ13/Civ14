using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using JetBrains.Annotations;
using Robust.Shared.Network;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared.Camera;

[UsedImplicitly]
public abstract class SharedCameraRecoilSystem : EntitySystem
{
    /// <summary>
    ///     Maximum rate of magnitude restore towards 0 kick.
    /// </summary>
    private const float RestoreRateMax = 30f;

    /// <summary>
    ///     Minimum rate of magnitude restore towards 0 kick.
    /// </summary>
    private const float RestoreRateMin = 0.1f;

    /// <summary>
    ///     Time in seconds since the last kick that lerps RestoreRateMin and RestoreRateMax
    /// </summary>
    private const float RestoreRateRamp = 4f;

    /// <summary>
    ///     The maximum magnitude of the kick applied to the camera at any point.
    /// </summary>
    protected const float KickMagnitudeMax = 1f;

    [Dependency] private readonly SharedContentEyeSystem _eye = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CameraRecoilComponent, GetEyeOffsetEvent>(OnCameraRecoilGetEyeOffset);
    }

    private void OnCameraRecoilGetEyeOffset(Entity<CameraRecoilComponent> ent, ref GetEyeOffsetEvent args)
    {
        args.Offset += ent.Comp.BaseOffset + ent.Comp.CurrentKick;
    }

    /// <summary>
    ///     Applies explosion/recoil/etc kickback to the view of the entity.
    /// </summary>
    /// <remarks>
    ///     If the entity is missing <see cref="CameraRecoilComponent" /> and/or <see cref="EyeComponent" />,
    ///     this call will have no effect. It is safe to call this function on any entity.
    /// </remarks>
    public abstract void KickCamera(EntityUid euid, Vector2 kickback, CameraRecoilComponent? component = null);

    private void UpdateEyes(float frameTime)
    {
        var query = AllEntityQuery<CameraRecoilComponent, EyeComponent>();

        while (query.MoveNext(out var uid, out var recoil, out var eye))
        {
            // Check if CurrentKick is invalid (NaN or Infinite) and reset if necessary.
            // This prevents downstream errors, especially with Math.Sign.
            if (!float.IsFinite(recoil.CurrentKick.X) || !float.IsFinite(recoil.CurrentKick.Y))
            {
                // Log error and reset
                Log.Error($"Camera recoil CurrentKick is invalid for entity {ToPrettyString(uid)}. Resetting kick. Value: {recoil.CurrentKick}");
                recoil.CurrentKick = Vector2.Zero;
                // Allow logic to continue to potentially update eye if LastKick wasn't zero
            }

            var magnitude = recoil.CurrentKick.Length();
            if (magnitude <= 0.005f)
            {
                // If it's already zero, skip updates. Otherwise, set it to zero.
                if (recoil.CurrentKick == Vector2.Zero)
                    continue;

                recoil.CurrentKick = Vector2.Zero;
            }
            else // Continually restore camera to 0.
            {
                var normalized = recoil.CurrentKick.Normalized();
                recoil.LastKickTime += frameTime;
                var restoreRate = MathHelper.Lerp(RestoreRateMin, RestoreRateMax, Math.Min(1, recoil.LastKickTime / RestoreRateRamp));
                var restore = normalized * restoreRate * frameTime;

                // Sanity check: ensure restore vector is finite.
                if (!float.IsFinite(restore.X) || !float.IsFinite(restore.Y))
                {
                    Log.Error($"Camera recoil restore vector is invalid for entity {ToPrettyString(uid)}. Resetting kick. Restore: {restore}, Normalized: {normalized}, Rate: {restoreRate}, FrameTime: {frameTime}");
                    recoil.CurrentKick = Vector2.Zero;
                }
                else
                {
                    var newKick = recoil.CurrentKick - restore;
                    var (x, y) = newKick;

                    // Check if the sign flipped (meaning we overshot zero)
                    if (Math.Sign(x) != Math.Sign(recoil.CurrentKick.X))
                        x = 0;

                    if (Math.Sign(y) != Math.Sign(recoil.CurrentKick.Y))
                        y = 0;

                    recoil.CurrentKick = new Vector2(x, y);
                }
            }

            if (recoil.CurrentKick == recoil.LastKick)
                continue;

            recoil.LastKick = recoil.CurrentKick;
            _eye.UpdateEyeOffset((uid, eye));
        }
    }

    public override void Update(float frameTime)
    {
        if (_net.IsServer)
            UpdateEyes(frameTime);
    }

    public override void FrameUpdate(float frameTime)
    {
        UpdateEyes(frameTime);
    }
}

[Serializable]
[NetSerializable]
public sealed class CameraKickEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;
    public readonly Vector2 Recoil;

    public CameraKickEvent(NetEntity netEntity, Vector2 recoil)
    {
        Recoil = recoil;
        NetEntity = netEntity;
    }
}
