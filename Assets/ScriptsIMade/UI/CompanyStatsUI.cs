using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays company statistics on the UI (replaces whiteboard display)
/// </summary>
public class CompanyStatsUI : MonoBehaviour
{
    [Header("Main Stats")]
    public TextMeshProUGUI valueText;
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI employeeCountText;

    [Header("Employee Demographics")]
    public TextMeshProUGUI ratioText;
    public TextMeshProUGUI avgIQText;
    public TextMeshProUGUI avgAgeText;
    public TextMeshProUGUI avgExperienceText;

    [Header("Race Statistics")]
    public TextMeshProUGUI mostRaceText;
    public TextMeshProUGUI leastRaceText;

    [Header("Happiness & Trust")]
    public TextMeshProUGUI employeeHappinessText;
    public TextMeshProUGUI investorTrustText;
    public Image employeeHappinessFill;
    public Image investorTrustFill;

    [Header("Optional - Daily Change Indicator")]
    public TextMeshProUGUI dailyChangeText;
    public Image dailyChangeIcon;

    [Header("Colors")]
    public Color positiveColor = new Color(0.2f, 0.8f, 0.2f); // Green
    public Color negativeColor = new Color(0.9f, 0.2f, 0.2f); // Red
    public Color neutralColor = Color.white;

    [Header("Value Labels (Optional)")]
    public TextMeshProUGUI valueLabelText;
    public TextMeshProUGUI dayLabelText;
    public TextMeshProUGUI employeeLabelText;
    public TextMeshProUGUI ratioLabelText;
    public TextMeshProUGUI iqLabelText;
    public TextMeshProUGUI ageLabelText;
    public TextMeshProUGUI expLabelText;
    public TextMeshProUGUI mostRaceLabelText;
    public TextMeshProUGUI leastRaceLabelText;

    private Color defaultValueColor;
    private CompanyManager subscribedManager;   // manager whose events we listen to
    private double lastShownValue = double.NaN;

    void Start()
    {
        defaultValueColor = valueText != null ? valueText.color : Color.white;

        // Set default label texts if assigned
        SetDefaultLabels();

        TrySubscribe();

        UpdateStats();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    // The daily change comes from CompanyManager (DayEnded), which resets its baseline when a save
    // is loaded or the day number jumps, so a load never shows "loaded value - $50k" as today's change.
    void TrySubscribe()
    {
        CompanyManager cm = CompanyManager.Instance;
        if (cm == null || cm == subscribedManager) return;

        Unsubscribe();
        cm.StatsChanged += UpdateStats;
        cm.StateLoaded += OnStateLoaded;
        cm.DayEnded += OnDayEnded;
        subscribedManager = cm;

        ResetDailyChange();
    }

    void Unsubscribe()
    {
        if (subscribedManager != null)
        {
            subscribedManager.StatsChanged -= UpdateStats;
            subscribedManager.StateLoaded -= OnStateLoaded;
            subscribedManager.DayEnded -= OnDayEnded;
        }
        subscribedManager = null;
    }

    void SetDefaultLabels()
    {
        if (valueLabelText != null) valueLabelText.text = "Company Value";
        if (dayLabelText != null) dayLabelText.text = "Day";
        if (employeeLabelText != null) employeeLabelText.text = "Employees";
        if (ratioLabelText != null) ratioLabelText.text = "M : F Ratio";
        if (iqLabelText != null) iqLabelText.text = "Avg IQ";
        if (ageLabelText != null) ageLabelText.text = "Avg Age";
        if (expLabelText != null) expLabelText.text = "Experience";
        if (mostRaceLabelText != null) mostRaceLabelText.text = "Most Common";
        if (leastRaceLabelText != null) leastRaceLabelText.text = "Least Common";
    }

    void Update()
    {
        // Manager appeared after us (or was replaced)
        if (subscribedManager == null)
            TrySubscribe();

        CompanyManager cm = CompanyManager.Instance;
        if (cm == null) return;

        // Always update displayed value for smooth animation
        if (valueText != null && cm.displayedValue != lastShownValue)
        {
            lastShownValue = cm.displayedValue;
            valueText.text = FormatCurrency(cm.displayedValue);
        }
    }

    void OnDayEnded(int newDay, double dailyChange)
    {
        // Color the value by whether yesterday made or lost money
        if (valueText != null)
            valueText.color = ColorFor(dailyChange);

        UpdateDailyChangeIndicator(dailyChange);
    }

    void OnStateLoaded()
    {
        ResetDailyChange();
        UpdateStats();
    }

    void ResetDailyChange()
    {
        if (valueText != null)
            valueText.color = defaultValueColor;
        UpdateDailyChangeIndicator(0.0);
    }

    Color ColorFor(double change)
    {
        if (change > 0)
            return positiveColor;
        if (change < 0)
            return negativeColor;
        return neutralColor;
    }

    void UpdateDailyChangeIndicator(double change)
    {
        if (dailyChangeText != null)
        {
            if (change > 0)
                dailyChangeText.text = "+" + FormatCurrency(change);
            else if (change < 0)
                dailyChangeText.text = "-" + FormatCurrency(System.Math.Abs(change));
            else
                dailyChangeText.text = "$0";
            dailyChangeText.color = ColorFor(change);
        }

        if (dailyChangeIcon != null)
            dailyChangeIcon.color = ColorFor(change);
    }

    void UpdateStats()
    {
        if (CompanyManager.Instance == null) return;

        var cm = CompanyManager.Instance;

        // Main stats
        if (valueText != null)
        {
            lastShownValue = cm.displayedValue;
            valueText.text = FormatCurrency(cm.displayedValue);
        }
        if (dayText != null)
            dayText.text = cm.currentDay.ToString(CultureInfo.InvariantCulture);
        if (employeeCountText != null)
            employeeCountText.text = cm.totalEmployees.ToString(CultureInfo.InvariantCulture);

        // Demographics
        if (ratioText != null)
            ratioText.text = cm.maleCount + " : " + cm.femaleCount;
        if (avgIQText != null)
            avgIQText.text = cm.averageIQ.ToString("0", CultureInfo.InvariantCulture);
        if (avgAgeText != null)
            avgAgeText.text = cm.averageAge.ToString("0", CultureInfo.InvariantCulture);
        if (avgExperienceText != null)
            avgExperienceText.text = cm.averageExperience.ToString("0.0", CultureInfo.InvariantCulture) + " yrs";

        // Race stats (in the current language: ApplicantText, "None" / "Unknown" included)
        if (mostRaceText != null)
            mostRaceText.text = ApplicantText.Race(cm.mostCommonRace);
        if (leastRaceText != null)
            leastRaceText.text = ApplicantText.Race(cm.leastCommonRace);

        // Happiness & Trust stats
        UpdateHappinessAndTrust(cm);
    }

    void UpdateHappinessAndTrust(CompanyManager cm)
    {
        // Employee Happiness
        if (employeeHappinessText != null)
        {
            employeeHappinessText.text = cm.employeeHappiness.ToString("0", CultureInfo.InvariantCulture) + "%";
            employeeHappinessText.color = GetStatColor(cm.employeeHappiness);
        }
        if (employeeHappinessFill != null)
        {
            // Use RectTransform anchor scaling for fill
            RectTransform rt = employeeHappinessFill.rectTransform;
            float fillPercent = Mathf.Clamp01(cm.employeeHappiness / 100f);
            rt.anchorMax = new Vector2(fillPercent, rt.anchorMax.y);
            employeeHappinessFill.color = GetStatColor(cm.employeeHappiness);
        }

        // Investor Trust
        if (investorTrustText != null)
        {
            investorTrustText.text = cm.investorTrust.ToString("0", CultureInfo.InvariantCulture) + "%";
            investorTrustText.color = GetStatColor(cm.investorTrust);
        }
        if (investorTrustFill != null)
        {
            // Use RectTransform anchor scaling for fill
            RectTransform rt = investorTrustFill.rectTransform;
            float fillPercent = Mathf.Clamp01(cm.investorTrust / 100f);
            rt.anchorMax = new Vector2(fillPercent, rt.anchorMax.y);
            investorTrustFill.color = GetStatColor(cm.investorTrust);
        }
    }

    Color GetStatColor(float value)
    {
        if (value >= 70f)
            return positiveColor;
        else if (value <= 30f)
            return negativeColor;
        else
            return neutralColor;
    }

    // "$1,234,567" regardless of the PC's language settings
    static string FormatCurrency(double value)
    {
        return "$" + value.ToString("N0", CultureInfo.InvariantCulture);
    }

    // Utility method to format large numbers
    public static string FormatMoney(double value)
    {
        if (value >= 1000000000) // Billion
            return "$" + (value / 1000000000.0).ToString("0.00", CultureInfo.InvariantCulture) + "B";
        else if (value >= 1000000) // Million
            return "$" + (value / 1000000.0).ToString("0.00", CultureInfo.InvariantCulture) + "M";
        else if (value >= 1000) // Thousand
            return "$" + (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K";
        else
            return FormatCurrency(value);
    }
}
