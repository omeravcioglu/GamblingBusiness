using System.Collections.Generic;
using UnityEngine;

public class TraitDatabase : MonoBehaviour
{
    public List<PersonalityTrait> traits = new List<PersonalityTrait>();

    private void Awake()
    {
        EnsureDefaults();
    }

    // Built-in traits are only added when nothing was set up in the Inspector
    // (adding them on top would duplicate entries and skew the odds)
    void EnsureDefaults()
    {
        if (traits == null)
            traits = new List<PersonalityTrait>();
        if (traits.Count > 0) return;

        traits.AddRange(CreateDefaults());
    }

    /// <summary>
    /// The built-in traits, new objects on every call. Their English names are ids (saved, checked by EmployeeVisual and
    /// hiring tasks) and show through ApplicantText.Trait; the Editor's ApplicantTextSource lists them for translation.
    /// </summary>
    public static List<PersonalityTrait> CreateDefaults()
    {
        return new List<PersonalityTrait>
        {
            // Positive
            new PersonalityTrait("Hard Worker", TraitType.Positive),
            new PersonalityTrait("Never Late", TraitType.Positive),
            new PersonalityTrait("Fast Learner", TraitType.Positive),
            new PersonalityTrait("Team Player", TraitType.Positive),
            new PersonalityTrait("Calm Under Pressure", TraitType.Positive),
            new PersonalityTrait("Organized", TraitType.Positive),

            // Negative
            new PersonalityTrait("Always Late", TraitType.Negative),
            new PersonalityTrait("Works Slow", TraitType.Negative),
            new PersonalityTrait("Drama Starter", TraitType.Negative),
            new PersonalityTrait("Lazy", TraitType.Negative),
            new PersonalityTrait("Hot-Headed", TraitType.Negative),
            new PersonalityTrait("Gets Offended Easily", TraitType.Negative),

            // Neutral / Funny
            new PersonalityTrait("Steals Office Snacks", TraitType.Neutral),
            new PersonalityTrait("Addicted to Coffee", TraitType.Neutral),
            new PersonalityTrait("Sleeps at Desk", TraitType.Neutral),
            new PersonalityTrait("Obsessed with Crypto", TraitType.Neutral),
            new PersonalityTrait("Printer Anxiety", TraitType.Neutral),
            new PersonalityTrait("Talks Too Much", TraitType.Neutral),
        };
    }

    /// <summary>Random trait, or null if there are none.</summary>
    public PersonalityTrait GetRandomTrait()
    {
        EnsureDefaults();
        if (traits.Count == 0) return null;
        return traits[Random.Range(0, traits.Count)];
    }
}
