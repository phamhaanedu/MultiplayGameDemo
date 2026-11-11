namespace CapsuleArena.Networking.Pooling
{
    /// <summary>
    /// Lifecycle hook implemented by pooled <see cref="Fusion.NetworkBehaviour"/> components
    /// (such as <c>NetworkProjectile</c>) to scrub stale velocity, trail, and hit flags on reuse.
    /// </summary>
    public interface IPooledResettable
    {
        /// <summary>Invoked right after the pooled GameObject is positioned and activated.</summary>
        void OnPoolAcquire();

        /// <summary>Invoked right before the pooled GameObject is deactivated and pushed back onto the pool stack.</summary>
        void OnPoolRelease();
    }
}
