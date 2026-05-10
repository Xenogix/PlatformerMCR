using System;

[Flags]
public enum LevelPaintMode
{
    None    = 0,
    Paint   = 1 << 0,
    Erase   = 1 << 1,
    Replace = 1 << 2,
}
