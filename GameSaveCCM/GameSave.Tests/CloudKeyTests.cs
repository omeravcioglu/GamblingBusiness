namespace GameSave.Tests;

/// <summary>
/// Cloud Save only accepts item keys made of letters, digits, '_' and '-' (1-255 characters). The first keys had a dot
/// ("save.data"): every read and write failed with CloudSaveValidationException, and the in-memory test stores never
/// noticed. These tests hold every key the save contract uses to Cloud Save's rule.
/// </summary>
public class CloudKeyTests
{
    [Test]
    public void ContractKeysFollowCloudSaveRule()
    {
        Assert.That(SaveCloud.IsValidKey(SaveCloud.DataKey), Is.True, SaveCloud.DataKey);
        Assert.That(SaveCloud.IsValidKey(SaveCloud.MetaKey), Is.True, SaveCloud.MetaKey);
        // The Online City's account keys the server reads: prefix + city id
        foreach (string city in new[] { "ist", "lon", "nyc", "tok", "dxb", "sha" })
            Assert.That(SaveCloud.IsValidKey(SaveCloud.CityAccountPrefix + city), Is.True, SaveCloud.CityAccountPrefix + city);
    }

    [Test]
    public void RuleRejectsWhatCloudSaveRejects()
    {
        Assert.That(SaveCloud.IsValidKey("save.data"), Is.False, "dots are not allowed");
        Assert.That(SaveCloud.IsValidKey(""), Is.False, "empty");
        Assert.That(SaveCloud.IsValidKey(null), Is.False, "null");
        Assert.That(SaveCloud.IsValidKey("a b"), Is.False, "spaces");
        Assert.That(SaveCloud.IsValidKey(new string('a', 256)), Is.False, "longer than 255");
        Assert.That(SaveCloud.IsValidKey(new string('a', 255)), Is.True, "255 is the maximum");
        Assert.That(SaveCloud.IsValidKey("save_data-2"), Is.True, "letters, digits, '_' and '-'");
    }

    [Test]
    public void KeysAreDistinct()
    {
        Assert.That(SaveCloud.DataKey, Is.Not.EqualTo(SaveCloud.MetaKey));
        Assert.That(SaveCloud.DataKey.StartsWith(SaveCloud.CityAccountPrefix), Is.False);
        Assert.That(SaveCloud.MetaKey.StartsWith(SaveCloud.CityAccountPrefix), Is.False);
    }
}
