using UnityEngine;

/// <summary>
/// Something a character can push by exerting force into it (e.g. a <see cref="PushableObstacle"/>).
/// Each tick a character deliberately presses inward against it, it calls <see cref="ApplyPush"/>;
/// the object accumulates the net push and resolves it (static hold vs kinetic glide) in the
/// GameClock LATE phase. Deliberate pushing is what separates a pusher from a bystander — incidental
/// contact never calls this.
/// </summary>
public interface IPushable
{
    /// <summary>Accumulate one pusher's force contribution for the current tick.</summary>
    void ApplyPush(Vector2 force);
}
