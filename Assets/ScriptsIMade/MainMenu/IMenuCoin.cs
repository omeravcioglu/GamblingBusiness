/// <summary>
/// What MainMenuUI needs from the New Game coin: the 3D toss (MenuCoinToss) or the 2D coin on the designed
/// menu's card table (MenuCoinFlip2D).
/// </summary>
public interface IMenuCoin
{
    /// <summary>The first flip's chance of success (MainMenuUI raises it after each failed flip).</summary>
    float BaseSuccessChance { get; }

    bool IsTossing { get; }

    bool LastResultWasSuccess { get; }

    void StartCoinToss(float chance);

    /// <summary>Put the coin back to rest (before a new toss, or when leaving the result).</summary>
    void CleanupCoin();
}
