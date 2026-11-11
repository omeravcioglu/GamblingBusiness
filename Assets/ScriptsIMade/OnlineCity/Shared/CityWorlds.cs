using System;
using System.Collections.Generic;

/// <summary>A district of a city: grid letter 'a' is Districts[0], 'b' is Districts[1] ...</summary>
public struct CityDistrictSeed
{
    public string Id, Name;
    public double Rate;
    public CityDistrictSeed(string id, string name, double rate) { Id = id; Name = name; Rate = rate; }
}

/// <summary>A landmark, in reading order of the grid's capital letters and '@' islands.</summary>
public struct CityLandmarkSeed
{
    public string Name;
    public string DistrictId; // islands only: the district an '@' belongs to
    public CityLandmarkSeed(string name, string districtId = null) { Name = name; DistrictId = districtId; }
}

/// <summary>A name written on the water. X/Y are in grid slots (1.5 = the middle of the second slot).</summary>
public struct CityWaterLabel
{
    public string Text;
    public float X, Y, Rotate;
    public CityWaterLabel(string text, float x, float y, float rotate = 0f) { Text = text; X = x; Y = y; Rotate = rotate; }
}

public struct CityNpcSeed
{
    public string Name, Logo;
    public CityNpcSeed(string name, string logo) { Name = name; Logo = logo; }
}

/// <summary>
/// One of the six online cities. Each is its own world: its own map, plots, bank accounts and rich list.
/// The map is a grid of slots, one character per city block:
///   a-h  a block of district a-h (cut into plots)     A-H  a landmark block of that district
///   ~    water          =  a bridge over water        @    an island with a landmark plot
///   *    park           .  open ground (nothing to buy)
/// </summary>
public class CityWorldDef
{
    public string Id;
    public string Name;
    public string Country;
    public string Tagline;
    public string Accent;       // postcard colour (CityLogo palette)
    public string Locale;       // "tr": district names are Turkish (capitals follow Turkish rules: i -> İ)
    public string[] Grid;
    public CityDistrictSeed[] Districts;
    public CityLandmarkSeed[] Landmarks;
    public string[] Parks;      // in reading order of their first slot
    public CityWaterLabel[] WaterLabels;
    public CityNpcSeed[] Npcs;
}

/// <summary>
/// The six cities, in the order the picker shows them. The grids are live data once the cities open: changing a grid moves or renames plots people own
/// (CityLayout.LayoutVersion). Names can change freely.
/// </summary>
public static class CityWorlds
{
    public static readonly CityWorldDef[] All =
    {
        new CityWorldDef
        {
            Id = "lon", Name = "London", Country = "United Kingdom", Accent = "2B5BA8",
            Tagline = "Old money on the Thames.",
            Grid = new[]
            {
                "..ggg**gggg...",
                "hhh*bbbccdddd.",
                "hhh*bBbccdDdd.",
                "hhhhaAaaddddee",
                "hhhh~=~~=~eee~",
                "~~=~~fFff~~Ee~",
                "ff*fffffff~ee~",
                "..fff*fff.~~~~",
            },
            Districts = new[]
            {
                new CityDistrictSeed("wes", "Westminster", 1.35),
                new CityDistrictSeed("may", "Mayfair", 1.40),
                new CityDistrictSeed("sho", "Soho", 1.20),
                new CityDistrictSeed("cit", "The City", 1.30),
                new CityDistrictSeed("can", "Canary Wharf", 1.25),
                new CityDistrictSeed("sou", "South Bank", 1.00),
                new CityDistrictSeed("cam", "Camden", 0.95),
                new CityDistrictSeed("ken", "Kensington", 1.25),
            },
            Landmarks = new[]
            {
                new CityLandmarkSeed("Buckingham Palace"),
                new CityLandmarkSeed("The Pickle"),
                new CityLandmarkSeed("Big Ben"),
                new CityLandmarkSeed("The Splinter"),
                new CityLandmarkSeed("One Wharf Tower"),
            },
            Parks = new[] { "Regent's Park", "Hyde Park", "Battersea Park", "Burgess Park" },
            WaterLabels = new[]
            {
                new CityWaterLabel("RIVER THAMES", 7.0f, 4.5f),
            },
            Npcs = new[]
            {
                new CityNpcSeed("Thames & Co", "1|square|BFE3F5|crown|1D2433||1"),
                new CityNpcSeed("Old Money Ltd", "1|square|6E4829|key|FFE680||1"),
                new CityNpcSeed("Crown Estate Co", "1|shield|7A4FB5|crown|FFE14D||1"),
                new CityNpcSeed("Big Smoke Group", "1|circle|676B74|factory|FFFDF6||1"),
                new CityNpcSeed("Tea & Tailors", "1|badge|FFC7D1|coffee|1D2433||1"),
                new CityNpcSeed("Mind The Gap PLC", "1|circle|C0352B|pin|FFFDF6||1"),
            },
        },
        new CityWorldDef
        {
            Id = "nyc", Name = "New York", Country = "United States", Accent = "FFE14D",
            Tagline = "The city that never stops betting.",
            Grid = new[]
            {
                "hh.~~aaaaa~ggg",
                "hhh~~b***b~ggg",
                "hhh~~b***b~ggg",
                "hhh~~ccCcc~ggg",
                "hhh~~ccccc=ggg",
                "hh~~~ddddd~fff",
                "h~~~~dDdd~~fff",
                "~@~~~~eEe==fFf",
                "~~~~~~~~~~~fff",
            },
            Districts = new[]
            {
                new CityDistrictSeed("har", "Harlem", 0.95),
                new CityDistrictSeed("ues", "Upper East Side", 1.30),
                new CityDistrictSeed("mid", "Midtown", 1.35),
                new CityDistrictSeed("soh", "SoHo", 1.30),
                new CityDistrictSeed("fid", "Financial District", 1.40),
                new CityDistrictSeed("bkn", "Brooklyn", 1.05),
                new CityDistrictSeed("qns", "Queens", 0.95),
                new CityDistrictSeed("jcy", "Jersey City", 1.00),
            },
            Landmarks = new[]
            {
                new CityLandmarkSeed("Umpire State Building"),
                new CityLandmarkSeed("Washington Square Arch"),
                new CityLandmarkSeed("Statue of Liberty", "jcy"),
                new CityLandmarkSeed("Stock Exchange"),
                new CityLandmarkSeed("Coney Wheel"),
            },
            Parks = new[] { "Central Park" },
            WaterLabels = new[]
            {
                new CityWaterLabel("HUDSON RIVER", 4.0f, 2.5f, 90f),
                new CityWaterLabel("EAST RIVER", 10.5f, 1.6f, 90f),
                new CityWaterLabel("UPPER BAY", 5.5f, 8.5f),
            },
            Npcs = new[]
            {
                new CityNpcSeed("Big Apple Capital", "1|shield|1D5A3E|dollar|FFFDF6||1"),
                new CityNpcSeed("Wall St Partners", "1|square|1D2433|chart|FFE14D||1"),
                new CityNpcSeed("Empire Holdings", "1|hexagon|2B5BA8|tower|FFFDF6||1"),
                new CityNpcSeed("Yellow Cab Ventures", "1|circle|FFE14D|rocket|1D2433||1"),
                new CityNpcSeed("Broadway Bros", "1|badge|C0352B|star|FFFDF6||1"),
                new CityNpcSeed("Liberty Trust", "1|diamond|1F8A8A|bell|FFFDF6||1"),
            },
        },
        new CityWorldDef
        {
            Id = "tyo", Name = "Tokyo", Country = "Japan", Accent = "FF8FA3",
            Tagline = "Neon, noodles and very tall rent.",
            Grid = new[]
            {
                ".gggeeee.~fff.",
                ".GggeeeEe~fFf.",
                ".aaa**eee=fff.",
                "aAaa**ccc~~fff",
                "a*aaddcCc~~~ff",
                "b*bbddddcc~~~~",
                "bBbbdDdd~=~~~~",
                "bbb.dd~~~hhh~~",
                "bb.~~~~~~hHh~~",
            },
            Districts = new[]
            {
                new CityDistrictSeed("shj", "Shinjuku", 1.20),
                new CityDistrictSeed("sby", "Shibuya", 1.25),
                new CityDistrictSeed("gnz", "Ginza", 1.40),
                new CityDistrictSeed("rpg", "Roppongi", 1.30),
                new CityDistrictSeed("akb", "Akihabara", 1.05),
                new CityDistrictSeed("ask", "Asakusa", 0.95),
                new CityDistrictSeed("ikb", "Ikebukuro", 1.00),
                new CityDistrictSeed("odb", "Odaiba", 1.10),
            },
            Landmarks = new[]
            {
                new CityLandmarkSeed("Sunshine Tower"),
                new CityLandmarkSeed("Electric Town"),
                new CityLandmarkSeed("Sky Needle"),
                new CityLandmarkSeed("Kabukichō Gate"),
                new CityLandmarkSeed("Ginza Clock Tower"),
                new CityLandmarkSeed("Shibuya Crossing"),
                new CityLandmarkSeed("Tokyo Tower"),
                new CityLandmarkSeed("Odaiba Big Wheel"),
            },
            Parks = new[] { "Imperial Palace Gardens", "Yoyogi Park" },
            WaterLabels = new[]
            {
                new CityWaterLabel("SUMIDA RIVER", 9.5f, 1.0f, 90f),
                new CityWaterLabel("TOKYO BAY", 12.0f, 6.5f),
            },
            Npcs = new[]
            {
                new CityNpcSeed("Kaiju Holdings", "1|circle|1D2433|bolt|FFE14D||1"),
                new CityNpcSeed("Neon Zaibatsu", "1|hexagon|FF8FA3|eye|1D2433||1"),
                new CityNpcSeed("Pachinko Palace", "1|square|7A4FB5|chip|FFFDF6||1"),
                new CityNpcSeed("Bullet Train Capital", "1|shield|2B5BA8|rocket|FFFDF6||1"),
                new CityNpcSeed("Karaoke Kings", "1|badge|E8742C|note|FFFDF6||1"),
                new CityNpcSeed("Lucky Cat Group", "1|circle|FFFDF6|paw|C0352B||1"),
            },
        },
        new CityWorldDef
        {
            Id = "ist", Name = "Istanbul", Country = "Türkiye", Accent = "C0352B", Locale = "tr",
            Tagline = "Two continents, one Bosphorus.",
            Grid = new[]
            {
                "..ffffffff~~gg",
                ".eeeeeefdd~~gg",
                ".ee*eeddd==ggg",
                ".bbbbbdDd~~g*g",
                "..bbBbcc~~~ggg",
                ".~~~~=~~~~~hhh",
                "aaa*aaaA~~@~hh",
                "aaaaaaa~~~~hhh",
                "~~~~~~~~~~~hHh",
            },
            Districts = new[]
            {
                new CityDistrictSeed("fat", "Fatih", 1.00),
                new CityDistrictSeed("bey", "Beyoğlu", 1.15),
                new CityDistrictSeed("kar", "Karaköy", 1.10),
                new CityDistrictSeed("bes", "Beşiktaş", 1.20),
                new CityDistrictSeed("sis", "Şişli", 1.05),
                new CityDistrictSeed("lev", "Levent", 1.35),
                new CityDistrictSeed("usk", "Üsküdar", 0.95),
                new CityDistrictSeed("kad", "Kadıköy", 1.05),
            },
            Landmarks = new[]
            {
                new CityLandmarkSeed("Dolmabahçe Palace"),
                new CityLandmarkSeed("Galata Tower"),
                new CityLandmarkSeed("Hagia Sophia"),
                new CityLandmarkSeed("Maiden's Tower", "usk"),
                new CityLandmarkSeed("Haydarpaşa Station"),
            },
            Parks = new[] { "Maçka Park", "Çamlıca Hill", "Gülhane Park" },
            WaterLabels = new[]
            {
                new CityWaterLabel("BOSPHORUS", 11.0f, 1.0f, 90f),
                new CityWaterLabel("GOLDEN HORN", 3.0f, 5.5f),
                new CityWaterLabel("SEA OF MARMARA", 5.5f, 8.5f),
            },
            Npcs = new[]
            {
                new CityNpcSeed("Golden Horn Trading", "1|badge|E2B13C|anchor|1D2433||1"),
                new CityNpcSeed("Bosphorus Holding", "1|shield|2B5BA8|wave|FFFDF6||1"),
                new CityNpcSeed("Sultan Capital", "1|circle|C0352B|crown|FFE14D||1"),
                new CityNpcSeed("Galata & Sons", "1|square|6E4829|tower|FFE680||1"),
                new CityNpcSeed("Anatolia Group", "1|hexagon|1E7B45|mountain|FFFDF6||1"),
                new CityNpcSeed("Grand Bazaar Co", "1|diamond|FFE14D|gem|1D2433||1"),
            },
        },
        new CityWorldDef
        {
            Id = "dxb", Name = "Dubai", Country = "United Arab Emirates", Accent = "E2B13C",
            Tagline = "Built on sand. Paid in gold.",
            Grid = new[]
            {
                "~~~~~~~~~~~~~~",
                "~e~e~e~~~~~~~~",
                "~eeeee~~~@~~ff",
                "c~~e~~dddd~~ff",
                "c~CdddDddgg~ff",
                "c~cdaaaaggG~Ff",
                "hhhhaaAaggg=ff",
                "hhHh*bbbb~~~ff",
                "hhhh.bBbb.....",
            },
            Districts = new[]
            {
                new CityDistrictSeed("dtn", "Downtown", 1.40),
                new CityDistrictSeed("bzb", "Business Bay", 1.20),
                new CityDistrictSeed("mrn", "Dubai Marina", 1.30),
                new CityDistrictSeed("jum", "Jumeirah", 1.25),
                new CityDistrictSeed("plm", "Palm Jumeirah", 1.35),
                new CityDistrictSeed("dei", "Deira", 1.00),
                new CityDistrictSeed("bur", "Bur Dubai", 1.05),
                new CityDistrictSeed("brs", "Al Barsha", 0.95),
            },
            Landmarks = new[]
            {
                new CityLandmarkSeed("Sail Hotel", "jum"),
                new CityLandmarkSeed("Desert Eye"),
                new CityLandmarkSeed("Jumeirah Mosque"),
                new CityLandmarkSeed("The Frame"),
                new CityLandmarkSeed("Gold Souk"),
                new CityLandmarkSeed("Tallest Tower"),
                new CityLandmarkSeed("Ski Dome"),
                new CityLandmarkSeed("Canal Crossing"),
            },
            Parks = new[] { "Al Barsha Pond Park" },
            WaterLabels = new[]
            {
                new CityWaterLabel("ARABIAN GULF", 9.5f, 0.6f),
                new CityWaterLabel("DUBAI CREEK", 11.5f, 4.5f, 90f),
            },
            Npcs = new[]
            {
                new CityNpcSeed("Desert Rose", "1|hexagon|E8742C|sun|FFFDF6||1"),
                new CityNpcSeed("Palm Capital", "1|circle|1E7B45|leaf|FFFDF6||1"),
                new CityNpcSeed("Golden Falcon Group", "1|shield|E2B13C|plane|1D2433||1"),
                new CityNpcSeed("Oasis Ventures", "1|badge|BFE3F5|drop|1D2433||1"),
                new CityNpcSeed("Sandstorm Holdings", "1|square|FFE680|wave|6E4829||1"),
                new CityNpcSeed("Gold Souk Traders", "1|diamond|1D2433|gem|FFE14D||1"),
            },
        },
        new CityWorldDef
        {
            Id = "sha", Name = "Shanghai", Country = "China", Accent = "7A4FB5",
            Tagline = "Old Bund money, new Pudong towers.",
            Grid = new[]
            {
                "....gggggg....",
                "..gggggg.~~~~~",
                "~~~=~~~=~~~bbb",
                "dddcccaaA~~bBb",
                "ddDcCcaaa~~bbb",
                "ffdeeeeaa~~bBb",
                "fffeEee*a~~hhh",
                "fFf.*eee.~~hHh",
                "fff.eee..~~h*h",
            },
            Districts = new[]
            {
                new CityDistrictSeed("bnd", "The Bund", 1.40),
                new CityDistrictSeed("ljz", "Lujiazui", 1.45),
                new CityDistrictSeed("pps", "People's Square", 1.20),
                new CityDistrictSeed("jga", "Jing'an", 1.25),
                new CityDistrictSeed("xtd", "Xintiandi", 1.30),
                new CityDistrictSeed("frc", "French Concession", 1.15),
                new CityDistrictSeed("hkw", "Hongkou", 0.95),
                new CityDistrictSeed("pud", "Pudong", 1.00),
            },
            Landmarks = new[]
            {
                new CityLandmarkSeed("Customs House"),
                new CityLandmarkSeed("Pearl Tower"),
                new CityLandmarkSeed("Jing'an Temple"),
                new CityLandmarkSeed("Grand Theatre"),
                new CityLandmarkSeed("The Twist"),
                new CityLandmarkSeed("Yu Garden"),
                new CityLandmarkSeed("Wukang Mansion"),
                new CityLandmarkSeed("Expo Pavilion"),
            },
            Parks = new[] { "Fuxing Park", "Xujiahui Park", "Century Park" },
            WaterLabels = new[]
            {
                new CityWaterLabel("HUANGPU RIVER", 10.0f, 5.5f, 90f),
                new CityWaterLabel("SUZHOU CREEK", 1.6f, 2.5f),
            },
            Npcs = new[]
            {
                new CityNpcSeed("Huangpu Holdings", "1|shield|C0352B|wave|FFE14D||1"),
                new CityNpcSeed("Dragon Gate Group", "1|badge|E2B13C|flame|C0352B||1"),
                new CityNpcSeed("Silk Road Ventures", "1|circle|7A4FB5|globe|FFFDF6||1"),
                new CityNpcSeed("Red Lantern Co", "1|hexagon|C0352B|bell|FFE14D||1"),
                new CityNpcSeed("Bund Bankers", "1|square|1D2433|dollar|E2B13C||1"),
                new CityNpcSeed("Lucky Dumpling Inc", "1|circle|FFFDF6|clover|1E7B45||1"),
            },
        },
    };

    static Dictionary<string, CityWorldDef> byId;

    public static CityWorldDef Get(string id)
    {
        if (byId == null)
        {
            var map = new Dictionary<string, CityWorldDef>(StringComparer.Ordinal);
            foreach (var world in All) map[world.Id] = world;
            byId = map;
        }
        return id != null && byId.TryGetValue(id, out var def) ? def : null;
    }

    public static bool Exists(string id) => Get(id) != null;
}
