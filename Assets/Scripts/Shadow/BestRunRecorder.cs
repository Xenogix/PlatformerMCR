using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Captures the whole performance — the live player and every clone that was alive — the moment a
/// level is finished, and persists it as a best-run shadow IF this run beat the saved time.
/// "Best" = the lowest finish <see cref="GameClock.Tick"/>. Called from <see cref="FinishFlag"/>
/// while the scene is still alive, so each body's <see cref="RigidbodyChannel"/> still holds its
/// full captured path.
/// </summary>
public static class BestRunRecorder
{
    public static void RecordIfBest()
    {
        if (!GameClock.HasInstance) return;

        int finishTick = GameClock.Instance.Tick;
        string levelKey = SceneManager.GetActiveScene().name;

        // Not an improvement (a lower tick is faster) — leave the existing record untouched.
        if (ShadowStore.TryLoad(levelKey, out var existing) && existing != null && finishTick >= existing.BestTick)
            return;

        var record = new ShadowRecord { BestTick = finishTick };

        // FindObjectsInactive.Include picks up despawned-but-retained clones too, so a clone that
        // had already caught up to its spawn still contributes its (now dormant) path.
        foreach (Player body in Object.FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var channel = body.GetComponent<RigidbodyChannel>();
            if (channel != null && channel.TryExportPositions(out int spawnTick, out List<Vector2> positions))
                record.Tracks.Add(new ShadowTrack { SpawnTick = spawnTick, Positions = positions });
        }

        if (record.Tracks.Count == 0) return;
        ShadowStore.Save(levelKey, record);
    }
}
