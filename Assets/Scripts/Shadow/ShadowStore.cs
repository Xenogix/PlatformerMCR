using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Disk persistence for best-run shadows — one compact, versioned binary file per level under
/// <c>Application.persistentDataPath/ghosts/&lt;levelKey&gt;.ghost</c>. Reads are defensive: any
/// problem (missing/corrupt/wrong-version) is treated as "no record" (returns false), so a bad
/// file never blocks gameplay. Saves swallow IO errors as a warning rather than throwing.
/// </summary>
public static class ShadowStore
{
    private const uint Magic = 0x53484457; // "SHDW"
    private const int Version = 1;

    private static string Dir => Path.Combine(Application.persistentDataPath, "ghosts");
    private static string FilePath(string levelKey) => Path.Combine(Dir, levelKey + ".ghost");

    /// <summary>
    /// Load the saved record for a level. Returns false (record = null) when there is none or it
    /// can't be read for any reason — never throws, so callers can branch on a single bool.
    /// </summary>
    public static bool TryLoad(string levelKey, out ShadowRecord record)
    {
        record = null;
        try
        {
            string path = FilePath(levelKey);
            if (!File.Exists(path)) return false;

            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadUInt32() != Magic) return false;
            if (reader.ReadInt32() != Version) return false;

            var loaded = new ShadowRecord { BestTick = reader.ReadInt32() };
            int trackCount = reader.ReadInt32();
            for (int t = 0; t < trackCount; t++)
            {
                int spawnTick = reader.ReadInt32();
                int count = reader.ReadInt32();
                var positions = new List<Vector2>(count);
                for (int i = 0; i < count; i++)
                {
                    float x = reader.ReadSingle();
                    float y = reader.ReadSingle();
                    positions.Add(new Vector2(x, y));
                }
                loaded.Tracks.Add(new ShadowTrack { SpawnTick = spawnTick, Positions = positions });
            }

            record = loaded;
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"ShadowStore: failed to load '{levelKey}': {e.Message}");
            record = null;
            return false;
        }
    }

    /// <summary>
    /// Overwrite the saved record for a level. IO failures are logged as a warning, never thrown,
    /// so a denied write can't crash a level finish.
    /// </summary>
    public static void Save(string levelKey, ShadowRecord record)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            using var writer = new BinaryWriter(File.Create(FilePath(levelKey)));
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(record.BestTick);
            writer.Write(record.Tracks.Count);
            foreach (ShadowTrack track in record.Tracks)
            {
                writer.Write(track.SpawnTick);
                writer.Write(track.Positions.Count);
                foreach (Vector2 p in track.Positions)
                {
                    writer.Write(p.x);
                    writer.Write(p.y);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"ShadowStore: failed to save '{levelKey}': {e.Message}");
        }
    }
}
