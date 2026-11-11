public enum TraitType { Positive, Negative, Neutral }

[System.Serializable]
public class PersonalityTrait
{
    public string traitName;
    public TraitType type;

    public PersonalityTrait(string name, TraitType type)
    {
        this.traitName = name;
        this.type = type;
    }
}
