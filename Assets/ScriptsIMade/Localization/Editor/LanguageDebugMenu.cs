using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Debug > Language: switch the game's language (L10n.SetLanguage, i.e. LocalizationSettings.SelectedLocale).
/// In Play mode the game changes at once and remembers the choice for the next start (PlayerPrefs); in Edit mode it only
/// sets the Editor's preview language (the game picks its own when Play starts). The current language has a check mark.
/// </summary>
public static class LanguageDebugMenu
{
    const string Root = "Tools/Game/Debug/Language/";
    const string English = Root + "English";
    const string Chinese = Root + "简体中文";
    const string Japanese = Root + "日本語";
    const string Korean = Root + "한국어";
    const string Turkish = Root + "Türkçe";
    const string Pseudo = Root + "Pseudo";

    [MenuItem(English, false, 300)] static void SetEnglish() => Set("en");
    [MenuItem(Chinese, false, 301)] static void SetChinese() => Set("zh-Hans");
    [MenuItem(Japanese, false, 302)] static void SetJapanese() => Set("ja");
    [MenuItem(Korean, false, 303)] static void SetKorean() => Set("ko");
    [MenuItem(Turkish, false, 304)] static void SetTurkish() => Set("tr");
    [MenuItem(Pseudo, false, 320)] static void SetPseudo() => Set(L10n.PseudoCode);

    [MenuItem(English, true)] static bool CanEnglish() => Validate(English, "en");
    [MenuItem(Chinese, true)] static bool CanChinese() => Validate(Chinese, "zh-Hans");
    [MenuItem(Japanese, true)] static bool CanJapanese() => Validate(Japanese, "ja");
    [MenuItem(Korean, true)] static bool CanKorean() => Validate(Korean, "ko");
    [MenuItem(Turkish, true)] static bool CanTurkish() => Validate(Turkish, "tr");
    [MenuItem(Pseudo, true)] static bool CanPseudo() => Validate(Pseudo, L10n.PseudoCode);

    static bool Validate(string menu, string code)
    {
        bool ready = L10n.IsReady;
        Menu.SetChecked(menu, ready && L10n.CurrentCode == code);
        return ready;
    }

    static void Set(string code)
    {
        if (!L10n.SetLanguage(code)) return;
        Debug.Log("[L10n] Language: " + L10n.NativeName(code) + " (" + code + ")" +
                  (EditorApplication.isPlaying ? "" : " as the Editor preview language; in Play mode the game starts in the saved or system language"));
    }
}
