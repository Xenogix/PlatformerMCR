using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One shadow body's path: the consecutive per-tick world positions it occupied,
/// the first one landing on <see cref="SpawnTick"/>. Replayed by clock tick so a
/// live-attempt rewind rewinds the shadow for free (see <see cref="ShadowPlayback"/>).
/// </summary>
public class ShadowTrack
{
    public int SpawnTick;
    public List<Vector2> Positions;
}

/// <summary>
/// A whole best-run performance for one level: the player plus every clone that was
/// alive at the finish, each as its own <see cref="ShadowTrack"/>. <see cref="BestTick"/>
/// is the finish <see cref="GameClock.Tick"/> — lower beats higher, so a faster run wins.
/// </summary>
public class ShadowRecord
{
    public int BestTick;
    public List<ShadowTrack> Tracks = new();
}
