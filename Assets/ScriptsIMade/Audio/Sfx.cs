/// <summary>
/// Every sound effect cue of the game: GameSfx.Play(Sfx.ChipsWin) plays one of the cue's clips (see SfxLibrary).
/// The numbers are stored in the library asset: add new cues at the end and never renumber the old ones.
/// The comment after each cue is its name in the library (and in SfxLibraryBuilder's clip map).
/// </summary>
public enum Sfx
{
    /// <summary>Plays nothing (handy for conditional calls).</summary>
    None = -1,

    // Interface (kenney_interface-sounds)
    UiClick = 0,        // ui_click: every uGUI button (UIClickSounds)
    UiOpen = 1,         // ui_open
    UiClose = 2,        // ui_close
    UiBack = 3,         // ui_back: Escape stepped back
    UiConfirm = 4,      // ui_confirm
    UiError = 5,        // ui_error
    UiQuestion = 6,     // ui_question
    UiToggle = 7,       // ui_toggle: toggles, tool keys
    UiTick = 8,         // ui_tick: slider drags, spinning reels
    UiDrop = 9,         // ui_drop
    UiGlitch = 10,      // ui_glitch: warnings
    UiBong = 11,        // ui_bong
    UiPluck = 12,       // ui_pluck
    UiMaximize = 13,    // ui_maximize
    UiMinimize = 14,    // ui_minimize
    UiScratch = 15,     // ui_scratch: rubber stamps
    UiGlass = 16,       // ui_glass: sparkle

    // Casino table (kenney_casino-audio)
    CardDeal = 17,      // card_deal
    CardPlace = 18,     // card_place
    CardToss = 19,      // card_toss
    CardShuffle = 20,   // card_shuffle
    CardFan = 21,       // card_fan
    ChipBet = 22,       // chip_bet
    ChipsWin = 23,      // chips_win
    ChipsCollect = 24,  // chips_collect
    ChipsClink = 25,    // chips_clink
    DiceShake = 26,     // dice_shake
    DiceThrow = 27,     // dice_throw
    CoinFlick = 28,     // coin_flick
    CoinLand = 29,      // coin_land
}

/// <summary>How much a cue matters when sounds pile up.</summary>
public enum SfxPriority
{
    /// <summary>Clicks, toasts, "back": waits for the end of the frame and gives way to anything more important.</summary>
    Low,
    Normal,
    /// <summary>Wins and warnings: never cut short by a less important sound.</summary>
    High
}
