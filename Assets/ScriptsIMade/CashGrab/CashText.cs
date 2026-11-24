using System;
using System.Globalization;

/// <summary>
/// The Cash Floor's texts: the "CashGrab" String Table (Assets/Localization/Source/CashGrab.csv, English plus zh-Hans, ja,
/// ko and tr). Money stays "$1,234,567" in every language (UIFormat).
/// </summary>
public static class CashText
{
    public const string Table = "CashGrab";

    public static string Get(string key) => L10n.Get(Table, key);

    public static string Get(string key, params (string name, object value)[] args) => L10n.Get(Table, key, args);

    public static string Money(double value) => UIFormat.Money(value);

    public static string SignedMoney(double value) => UIFormat.SignedMoney(value);

    /// <summary>Big amounts on flying numbers: "$12,400", then "$1.25M", "$3.4B" from a million up.</summary>
    public static string Compact(double value)
    {
        double a = Math.Abs(value);
        string sign = value < 0 ? UIFormat.Minus : "";
        if (a < 1e6) return sign + "$" + Math.Round(a).ToString("N0", CultureInfo.InvariantCulture);
        string[] suffix = { "M", "B", "T", "Q" };
        double scaled = a / 1e6;
        int s = 0;
        while (scaled >= 1000.0 && s < suffix.Length - 1)
        {
            scaled /= 1000.0;
            s++;
        }
        string number = scaled >= 100 ? scaled.ToString("0", CultureInfo.InvariantCulture)
                      : scaled >= 10 ? scaled.ToString("0.#", CultureInfo.InvariantCulture)
                      : scaled.ToString("0.##", CultureInfo.InvariantCulture);
        return sign + "$" + number + suffix[s];
    }

    public static string DeptName(int dept) => Get("cash.dept." + CashRules.DeptIds[Clamp(dept)] + ".name");

    public static string ToolName(CashTool tool) => Get("cash.tool." + ToolId(tool) + ".name");

    public static string ToolBlurb(CashTool tool) => Get("cash.tool." + ToolId(tool) + ".blurb");

    public static string GadgetName(CashGadget gadget) => gadget == CashGadget.None ? "" : Get("cash.gadget." + GadgetId(gadget) + ".name");

    public static string GadgetBlurb(CashGadget gadget) => gadget == CashGadget.None ? "" : Get("cash.gadget." + GadgetId(gadget) + ".blurb");

    /// <summary>The tool's icon in the atlas ("icon_hand" for bare hands).</summary>
    public static string ToolIcon(CashTool tool) => tool == CashTool.Hands ? "icon_hand" : "icon_" + ToolId(tool);

    /// <summary>The gadget's icon in the atlas.</summary>
    public static string GadgetIcon(CashGadget gadget) => "icon_" + GadgetId(gadget);

    public static string ToolId(CashTool tool)
    {
        switch (tool)
        {
            case CashTool.Scoop: return "scoop";
            case CashTool.Rake: return "rake";
            case CashTool.Tray: return "tray";
            case CashTool.Vacuum: return "vacuum";
            case CashTool.Industrial: return "industrial";
            default: return "hands";
        }
    }

    public static string GadgetId(CashGadget gadget)
    {
        switch (gadget)
        {
            case CashGadget.Paperweight: return "paperweight";
            case CashGadget.Fan: return "fan";
            case CashGadget.Gloves: return "gloves";
            default: return "stapler";
        }
    }

    public static string InterferenceId(CashInterference kind)
    {
        switch (kind)
        {
            case CashInterference.Firewall: return "firewall";
            case CashInterference.RateLimit: return "rate_limit";
            case CashInterference.Shove: return "shove";
            case CashInterference.Popup: return "popup";
            case CashInterference.Yoink: return "yoink";
            case CashInterference.Magnet: return "magnet";
            case CashInterference.Confetti: return "confetti";
            default: return "none";
        }
    }

    static int Clamp(int dept) => dept < 0 ? 0 : dept >= CashRules.DeptIds.Length ? CashRules.DeptIds.Length - 1 : dept;
}
