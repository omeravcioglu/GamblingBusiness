namespace OnlineCity.Tests;

public class CityLayoutTests
{
    static IEnumerable<string> Cities => CityWorlds.All.Select(w => w.Id);

    [Test]
    public void SixCities_EachItsOwnMap()
    {
        Assert.That(CityWorlds.All, Has.Length.EqualTo(6));
        Assert.That(Cities.Distinct().Count(), Is.EqualTo(6));
        Assert.That(CityLayout.Get("nowhere"), Is.Null);
        Assert.That(CityLayout.Get("ist"), Is.SameAs(CityLayout.Get("ist")), "maps are built once");
    }

    [TestCaseSource(nameof(Cities))]
    public void City_HasHundredsOfPlots_ThatNeverOverlap(string city)
    {
        var map = CityLayout.Get(city);
        TestContext.Out.WriteLine($"{map.Name}: {map.Plots.Count} plots, {map.Blocks.Count} blocks, {map.Districts.Count} districts, " +
            $"{map.Parks.Count} parks, {map.Bridges.Count} bridges, {map.Islands.Count} islands, {map.Width}x{map.Height}");

        Assert.That(map.Plots.Count, Is.InRange(450, 750));
        Assert.That(map.Districts, Has.Count.EqualTo(8));
        Assert.That(map.Plots.Select(p => p.Id).Distinct().Count(), Is.EqualTo(map.Plots.Count), "plot ids are unique");
        Assert.That(map.Plots.Select(p => p.Number), Is.EqualTo(Enumerable.Range(1, map.Plots.Count)), "plots are numbered 1..n");
        Assert.That(map.Plots.All(p => p.BasePrice >= 90_000_000L), Is.True);
        Assert.That(map.Plots.All(p => p.X >= 0 && p.Y >= 0 && p.X + p.W <= map.Width && p.Y + p.H <= map.Height), Is.True);

        // No two plots overlap (sorted sweep over x)
        var sorted = map.Plots.OrderBy(p => p.X).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var p = sorted[i];
            for (int j = i + 1; j < sorted.Count && sorted[j].X < p.X + p.W; j++)
            {
                var q = sorted[j];
                bool overlap = p.X < q.X + q.W && q.X < p.X + p.W && p.Y < q.Y + q.H && q.Y < p.Y + p.H;
                Assert.That(overlap, Is.False, $"{p.Id} overlaps {q.Id}");
            }
        }

        // Every district has room to grow and a place for its name
        foreach (var district in map.Districts)
        {
            Assert.That(district.Plots.Count, Is.GreaterThanOrEqualTo(15), district.Name);
            Assert.That(district.LabelX, Is.GreaterThan(0), district.Name);
        }

        // Landmarks: one named plot per landmark
        var landmarks = map.Plots.Where(p => p.Size == CityPlotSize.Landmark).ToList();
        Assert.That(landmarks.Select(p => p.Name), Is.EqualTo(map.World.Landmarks.Select(l => l.Name)));
        Assert.That(landmarks.All(p => p.BasePrice >= CityLayout.LandmarkPrice * 0.9), Is.True, "landmarks are the priciest plots");

        var first = map.Plots[0];
        Assert.That(map.PlotAt(first.CenterX, first.CenterY), Is.SameAs(first));
        Assert.That(map.Plots.Count(p => p.Waterfront), Is.GreaterThan(20), "every city has a waterfront");
    }

    [TestCaseSource(nameof(Cities))]
    public void City_BridgesLandOnBothEnds_AndIslandsSitInWater(string city)
    {
        var map = CityLayout.Get(city);
        foreach (var bridge in map.Bridges)
        {
            int c = bridge.Column, r = bridge.Row;
            if (bridge.Horizontal)
            {
                int left = c, right = c;
                while (map.KindAt(left - 1, r) == CitySlotKind.Bridge) left--;
                while (map.KindAt(right + 1, r) == CitySlotKind.Bridge) right++;
                Assert.That(map.IsLand(left - 1, r) && map.IsLand(right + 1, r), Is.True, $"{city} bridge at {c},{r}");
            }
            else
            {
                int top = r, bottom = r;
                while (map.KindAt(c, top - 1) == CitySlotKind.Bridge) top--;
                while (map.KindAt(c, bottom + 1) == CitySlotKind.Bridge) bottom++;
                Assert.That(map.IsLand(c, top - 1) && map.IsLand(c, bottom + 1), Is.True, $"{city} bridge at {c},{r}");
            }
        }
        foreach (var island in map.Islands)
        {
            Assert.That(map.PlotsById[island.PlotId].Size, Is.EqualTo(CityPlotSize.Landmark));
            Assert.That(map.PlotsById[island.PlotId].Waterfront, Is.True);
        }
        foreach (var park in map.Parks)
            Assert.That(park.Areas, Is.Not.Empty);
        Assert.That(map.Parks.Count, Is.EqualTo(map.World.Parks.Length), "every park has a name");
    }

    [TestCaseSource(nameof(Cities))]
    public void City_OpensWithRivalCompanies_AndMostPlotsFree(string city)
    {
        var map = CityLayout.Get(city);
        int owned = 0;
        var owners = new HashSet<string>();
        foreach (var district in map.Districts)
        {
            var seeded = CityNpcs.SeedDistrict(map, district);
            Assert.That(seeded.Plots.Keys.All(id => map.PlotsById[id].DistrictId == district.Id), Is.True);
            owned += seeded.Plots.Count;
            foreach (var plot in seeded.Plots.Values)
            {
                owners.Add(plot.OwnerId);
                Assert.That(plot.OwnerId, Does.StartWith(CityRules.NpcPrefix + city));
                Assert.That(CityLogo.Validate(plot.Logo, out var error), Is.Not.Null, error);
            }
        }
        Assert.That(owned, Is.InRange(map.Plots.Count / 8, map.Plots.Count / 3));
        Assert.That(owners.Count, Is.EqualTo(map.World.Npcs.Length));
    }

    /// <summary>
    /// Plot ids, places and prices are live data once the cities open. This fingerprint changes when the layout does:
    /// if that was on purpose (before launch), update it; after launch, don't change the layout.
    /// </summary>
    [Test]
    public void Layout_Fingerprint()
    {
        var parts = new List<string>();
        foreach (var world in CityWorlds.All)
        {
            var map = CityLayout.Get(world.Id);
            string all = string.Join(";", map.Plots.Select(p => $"{p.Id}:{p.Number}:{p.X}:{p.Y}:{p.W}:{p.H}:{p.BasePrice}"));
            parts.Add($"{world.Id}={map.Plots.Count}/{CityHash.Short(all, 12)}");
        }
        string fingerprint = string.Join(" ", parts);
        TestContext.Out.WriteLine(fingerprint);
        Assert.That(CityLayout.LayoutVersion, Is.EqualTo(2));
        Assert.That(fingerprint, Is.EqualTo(ExpectedFingerprint), "the city layouts changed");
    }

    const string ExpectedFingerprint =
        "lon=640/4437ab6ae1f7 nyc=620/7307684dd8c0 tyo=654/77847a18dc4a ist=636/297df2ed9cad dxb=595/85025409ab0b sha=633/0ddcbb97ec4a";
}
