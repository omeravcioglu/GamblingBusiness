using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The open staff file (the CV-like sheet with the performance ledger and "Fire") of StaffFilesView. The runtime builder
/// fills these; a designed sheet can fill them instead. Empty references are skipped.
/// </summary>
[Serializable]
public class StaffFileSheet
{
    [Tooltip("The whole file layer (dim + sheet); hidden while no file is open")]
    public RectTransform root;
    [Tooltip("The sheet's border (turns gold-dark for golden hires)")]
    public BlueprintBox paper;

    [Header("Letterhead")]
    public TMP_Text firstNameText;
    public TMP_Text lastNameText;
    [Tooltip("Stars after the first name, shown for golden hires")]
    public RectTransform goldenStars;
    [Tooltip("\"29 · Female · Mixed\"")]
    public TMP_Text metaText;
    public Image photo;
    [Tooltip("Shown while there is no photo")]
    public GameObject photoPlaceholder;
    [Tooltip("The rule under the letterhead (gold for golden hires)")]
    public Graphic rule;

    [Header("CV")]
    public TMP_Text degreeText;
    public TMP_Text majorText;
    public TMP_Text gpaText;
    public TMP_Text experienceText;
    public TMP_Text salaryText;
    [Tooltip("One line per trait, with its icon (check / cross / wave)")]
    public TMP_Text[] traitTexts;
    public StaffIcon[] traitIcons;
    public TMP_Text iqText;
    [Tooltip("\"HAS A / RECORD\" in red or \"CLEAN / RECORD\" in green")]
    public TMP_Text recordText;
    public Graphic[] recordInk;
    public GameObject goldenSeal;
    public TMP_Text fileNumberText;

    [Header("Performance")]
    [Tooltip("\"last day\" or \"estimate\" next to the ledger's heading")]
    public TMP_Text basisText;
    public TMP_Text hiredText;
    public TMP_Text daysText;
    public TMP_Text outputText;
    public TMP_Text salaryDayText;
    public TMP_Text overheadText;
    public TMP_Text troubleText;
    public TMP_Text bonusText;
    public TMP_Text profitText;
    public TMP_Text lifetimeText;
    [Tooltip("Estimate, skill-point and severance notes")]
    public TMP_Text noteText;

    [Header("Buttons")]
    public Button fireButton;
    public Button closeButton;
}

/// <summary>
/// "Fire &lt;name&gt;? Severance $X · staff morale −N" with Fire / Keep (StaffFilesView; N is StaffLedger.MoraleDropFor).
/// The runtime builder fills these; a designed pop-up can fill them instead.
/// </summary>
[Serializable]
public class StaffFireConfirm
{
    [Tooltip("The whole confirmation layer (dim + sheet); hidden while closed")]
    public RectTransform root;
    [Tooltip("\"Fire Maya Collins?\"")]
    public TMP_Text titleText;
    [Tooltip("\"Severance $6,904 · staff morale −1.4\" (the drop firing really causes)")]
    public TMP_Text detailText;
    [Tooltip("Extra warnings: a quick fire (morale and skill points), an empty company")]
    public TMP_Text noteText;
    public Button fireButton;
    public Button keepButton;
}
