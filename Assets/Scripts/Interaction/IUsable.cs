/// <summary>
/// Anything the player can activate with the "Use" action: levers, buttons, doors…
/// The activating player is passed in so the usable can react to who used it (and so
/// a clone activating it works identically to the live player).
/// </summary>
public interface IUsable
{
    void Use(Player user);
}
