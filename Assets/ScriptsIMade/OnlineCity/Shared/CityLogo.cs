using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// A company logo from the in-game logo maker: a shape, an icon and up to 3 letters, in palette colours.
/// Stored as a short code ("1|shield|E2B13C|rocket|1D2433|NH|1") on the account and on every plot the company owns.
/// Only known shapes, icons and palette colours are accepted. Players can also draw their own (CityPixelLogo).
/// </summary>
public class CityLogo
{
    public string Shape = "circle";
    public string Background = "E2B13C";
    public string Icon = "star";
    public string Foreground = "1D2433";
    public string Letters = "";
    public bool Ring = true;

    public static readonly string[] Shapes = { "circle", "square", "shield", "hexagon", "diamond", "badge" };

    /// <summary>The design's paper-and-ink palette plus a few extra inks.</summary>
    public static readonly string[] Palette =
    {
        "1D2433", "FFFDF6", "E2B13C", "FFE14D", "C0352B", "FF8FA3", "FFC7D1", "2B5BA8",
        "BFE3F5", "1E7B45", "CDEBA6", "1D5A3E", "6E4829", "7A4FB5", "E8742C", "1F8A8A", "676B74", "FFE680",
    };

    /// <summary>Icon ids (their drawings live in the game: CityIcons). "none" shows only the letters.</summary>
    public static readonly string[] Icons =
    {
        "none", "star", "crown", "rocket", "bolt", "gem", "dollar", "chart", "dice", "cards", "clover", "horseshoe",
        "flame", "leaf", "anchor", "globe", "key", "tower", "heart", "gear", "atom", "mountain", "sun", "moon",
        "drop", "factory", "plane", "briefcase", "bell", "trophy", "eye", "spade", "chip", "pin", "tag", "skyline",
        "coffee", "paw", "note", "wave",
    };

    public const int MaxLetters = 3;

    public string Encode()
    {
        return "1|" + Shape + "|" + Background + "|" + Icon + "|" + Foreground + "|" + Letters + "|" + (Ring ? "1" : "0");
    }

    public override string ToString() => Encode();

    public CityLogo Clone() => (CityLogo)MemberwiseClone();

    /// <summary>Read a code; anything unknown falls back to a default part (never throws).</summary>
    public static CityLogo Decode(string code)
    {
        var logo = new CityLogo();
        if (string.IsNullOrEmpty(code)) return logo;
        string[] parts = code.Split('|');
        if (parts.Length < 7 || parts[0] != "1") return logo;
        if (Array.IndexOf(Shapes, parts[1]) >= 0) logo.Shape = parts[1];
        if (IsPaletteColour(parts[2])) logo.Background = parts[2].ToUpperInvariant();
        if (Array.IndexOf(Icons, parts[3]) >= 0) logo.Icon = parts[3];
        if (IsPaletteColour(parts[4])) logo.Foreground = parts[4].ToUpperInvariant();
        logo.Letters = CleanLetters(parts[5]);
        logo.Ring = parts[6] != "0";
        if (logo.Icon == "none" && logo.Letters.Length == 0) logo.Icon = "star";
        return logo;
    }

    /// <summary>The code in its only accepted form, or null when it isn't a valid logo.</summary>
    public static string Validate(string code, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(code) || code.Length > 64)
        {
            error = "Make a logo first.";
            return null;
        }
        string[] parts = code.Split('|');
        if (parts.Length != 7 || parts[0] != "1"
            || Array.IndexOf(Shapes, parts[1]) < 0 || Array.IndexOf(Icons, parts[3]) < 0
            || !IsPaletteColour(parts[2]) || !IsPaletteColour(parts[4]) || (parts[6] != "0" && parts[6] != "1"))
        {
            error = "That logo can't be used.";
            return null;
        }
        string letters = CleanLetters(parts[5]);
        if (letters != parts[5].ToUpperInvariant())
        {
            error = "Logo letters can only be letters and numbers.";
            return null;
        }
        if (letters.Length > 0 && CityRules.ContainsBlockedWord(letters))
        {
            error = "Those logo letters aren't allowed.";
            return null;
        }
        if (parts[3] == "none" && letters.Length == 0)
        {
            error = "Pick an icon or type some letters.";
            return null;
        }
        return Decode(code).Encode();
    }

    public static bool IsPaletteColour(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return false;
        foreach (var colour in Palette)
        {
            if (string.Equals(colour, hex, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static string CleanLetters(string letters)
    {
        if (string.IsNullOrEmpty(letters)) return "";
        var sb = new StringBuilder();
        foreach (char c in letters)
        {
            if (sb.Length >= MaxLetters) break;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>First letters of the first words of a company name ("Nova Holdings" = "NH").</summary>
    public static string InitialsOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder();
        foreach (var word in name.Split(new[] { ' ', '-', '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (char c in word)
            {
                if (!char.IsLetterOrDigit(c)) continue;
                sb.Append(char.ToUpperInvariant(c));
                break;
            }
            if (sb.Length >= 2) break;
        }
        return sb.ToString();
    }

    /// <summary>A pleasant starting logo for a company (always valid).</summary>
    public static CityLogo Suggest(string companyName, int variant = 0)
    {
        var rng = new CityRandom((companyName ?? "") + "#" + variant);
        var logo = new CityLogo
        {
            Shape = Shapes[rng.Range(0, Shapes.Length)],
            Icon = Icons[rng.Range(1, Icons.Length)],
            Letters = "",
            Ring = true,
        };
        // Readable pairs: a light or bright background with ink, or a dark one with paper
        string[] light = { "E2B13C", "FFE14D", "FF8FA3", "FFC7D1", "BFE3F5", "CDEBA6", "FFE680", "FFFDF6" };
        string[] dark = { "1D2433", "C0352B", "2B5BA8", "1E7B45", "1D5A3E", "7A4FB5", "1F8A8A", "6E4829" };
        if (rng.NextDouble() < 0.5)
        {
            logo.Background = light[rng.Range(0, light.Length)];
            logo.Foreground = "1D2433";
        }
        else
        {
            logo.Background = dark[rng.Range(0, dark.Length)];
            logo.Foreground = rng.NextDouble() < 0.7 ? "FFFDF6" : "E2B13C";
        }
        return logo;
    }
}

/// <summary>
/// A player's own drawing: 32x32 pixels, each one of Palette (index 0 is see-through). Stored once per city under
/// its hash (CityLogoRecord); accounts and plots carry only the reference "p|hash". Players can report drawings.
/// Code: "px1:" + base64 of the pixels packed 5 bits each (640 bytes).
/// </summary>
public class CityPixelLogo
{
    public const int Size = 32;
    public const string CodePrefix = "px1:";
    public const string RefPrefix = "p|";
    /// <summary>A drawing needs at least this many painted pixels.</summary>
    public const int MinPainted = 16;
    const int PackedBytes = Size * Size * 5 / 8;

    /// <summary>The ink, paper and pastel colours of the City plus brights and darks. Index 0 is see-through.</summary>
    public static readonly string[] Palette =
    {
        null,
        "1D2433", "3A3F4B", "676B74", "C9CCD3", "FFFDF6", "0B0D12",
        "C0352B", "7D1E18", "E8742C", "F6B98C", "E2B13C", "FFE14D", "FFE680",
        "1E7B45", "1D5A3E", "8BC34A", "CDEBA6", "7FD8B6", "1F8A8A", "3CC7D6",
        "2B5BA8", "16305E", "5FA8E8", "BFE3F5", "7A4FB5", "C8B3F0", "D0368A",
        "FF8FA3", "FFC7D1", "6E4829", "D8B98A",
    };

    public readonly byte[] Pixels = new byte[Size * Size];

    public byte this[int x, int y]
    {
        get => Pixels[y * Size + x];
        set => Pixels[y * Size + x] = value < Palette.Length ? value : (byte)0;
    }

    public int Painted
    {
        get
        {
            int count = 0;
            foreach (byte p in Pixels) if (p != 0) count++;
            return count;
        }
    }

    public CityPixelLogo Clone()
    {
        var copy = new CityPixelLogo();
        Array.Copy(Pixels, copy.Pixels, Pixels.Length);
        return copy;
    }

    public string Encode()
    {
        var packed = new byte[PackedBytes];
        for (int i = 0; i < Pixels.Length; i++)
        {
            int value = Pixels[i] & 31;
            for (int b = 0; b < 5; b++)
            {
                if (((value >> b) & 1) == 0) continue;
                int bit = i * 5 + b;
                packed[bit >> 3] |= (byte)(1 << (bit & 7));
            }
        }
        return CodePrefix + Convert.ToBase64String(packed);
    }

    /// <summary>Read a drawing code (null when it isn't one).</summary>
    public static CityPixelLogo Decode(string code)
    {
        if (string.IsNullOrEmpty(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal)) return null;
        byte[] packed;
        try
        {
            packed = Convert.FromBase64String(code.Substring(CodePrefix.Length));
        }
        catch (FormatException)
        {
            return null;
        }
        if (packed.Length != PackedBytes) return null;

        var logo = new CityPixelLogo();
        for (int i = 0; i < logo.Pixels.Length; i++)
        {
            int value = 0;
            for (int b = 0; b < 5; b++)
            {
                int bit = i * 5 + b;
                if ((packed[bit >> 3] & (1 << (bit & 7))) != 0) value |= 1 << b;
            }
            if (value >= Palette.Length) return null;
            logo.Pixels[i] = (byte)value;
        }
        return logo;
    }

    /// <summary>The code in its only accepted form, or null with the reason it can't be used.</summary>
    public static string Validate(string code, out string error)
    {
        error = null;
        var logo = Decode(code);
        if (logo == null)
        {
            error = "That drawing can't be used.";
            return null;
        }
        if (logo.Painted < MinPainted)
        {
            error = "Draw a bit more first - your logo is nearly empty.";
            return null;
        }
        return logo.Encode();
    }

    public static string HashOf(string code) => CityHash.Fnv64(code).ToString("x16");
    public static string RefFor(string hash) => RefPrefix + hash;
    public static bool IsRef(string logo) => logo != null && logo.StartsWith(RefPrefix, StringComparison.Ordinal);
    public static string HashOfRef(string logo) => IsRef(logo) ? logo.Substring(RefPrefix.Length) : null;

    /// <summary>A plain logo for a company whose drawing was taken down (always valid).</summary>
    public static string FallbackFor(string companyName)
    {
        var logo = CityLogo.Suggest(companyName, 7);
        logo.Icon = "none";
        logo.Letters = CityLogo.InitialsOf(companyName);
        if (logo.Letters.Length == 0) logo.Icon = "star";
        return logo.Encode();
    }
}

/// <summary>The rival companies each city starts with: they own some plots, and challengers can win them at the table.</summary>
public static class CityNpcs
{
    public static string NpcId(string cityId, int index) => CityRules.NpcPrefix + cityId + (index + 1);

    /// <summary>
    /// The district the way its city opens: about a fifth of the plots belong to the city's rival companies.
    /// Deterministic, so every server that creates it creates the same district.
    /// </summary>
    public static CityDistrict SeedDistrict(CityMap map, CityDistrictDef def)
    {
        var npcs = map.World.Npcs;
        var district = new CityDistrict { Id = def.Id, Version = 1 };
        foreach (var plot in def.Plots)
        {
            var rng = new CityRandom("seed:" + map.CityId + ":" + plot.Id);
            double chance = plot.Size == CityPlotSize.Landmark ? 0.35 : 0.22;
            if (rng.NextDouble() >= chance) continue;

            int n = rng.Range(0, npcs.Length);
            district.Plots[plot.Id] = new CityPlot
            {
                OwnerId = NpcId(map.CityId, n),
                OwnerName = npcs[n].Name,
                Logo = npcs[n].Logo,
                PricePaid = plot.BasePrice,
                AcquiredAt = 0,
                ShieldUntil = 0,
            };
        }
        return district;
    }
}
