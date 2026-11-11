using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class NameDatabase : MonoBehaviour
{
    public List<string> maleFirstNames = new List<string>();
    public List<string> femaleFirstNames = new List<string>();
    public List<string> lastNames = new List<string>();

    // Languages to load on start
    public string[] languagesToLoad = { "en", "tr", "es", "de", "jp", "ar" };

    void Awake()
    {
        // Load all languages on start for diverse names
        LoadAllLanguages();
    }

    public void LoadAllLanguages()
    {
        maleFirstNames.Clear();
        femaleFirstNames.Clear();
        lastNames.Clear();

        foreach (string lang in languagesToLoad)
        {
            LoadNames(lang, false); // false = don't clear, append instead
        }

        Debug.Log($"Total loaded: {maleFirstNames.Count} male names, {femaleFirstNames.Count} female names, {lastNames.Count} last names");
    }

    public void LoadNames(string languageCode, bool clearExisting = true)
    {
        if (clearExisting)
        {
            maleFirstNames.Clear();
            femaleFirstNames.Clear();
            lastNames.Clear();
        }

        string basePath = Path.Combine(Application.streamingAssetsPath, "Names", languageCode);

        string malePath = Path.Combine(basePath, "male_firstnames.txt");
        string femalePath = Path.Combine(basePath, "female_firstnames.txt");
        string lastPath = Path.Combine(basePath, "lastnames.txt");

        ReadNamesInto(malePath, maleFirstNames);
        ReadNamesInto(femalePath, femaleFirstNames);
        ReadNamesInto(lastPath, lastNames);

        Debug.Log($"Loaded names for [{languageCode}]");
    }

    // One name per line; a missing or unreadable file is skipped instead of throwing
    static void ReadNamesInto(string path, List<string> target)
    {
        if (!File.Exists(path)) return;

        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    target.Add(line.Trim());
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"NameDatabase: could not read {path}: {e.Message}");
        }
    }

    public string GetRandomFullName(string gender)
    {
        List<string> firstNameList;

        if (gender == "Male")
            firstNameList = maleFirstNames;
        else if (gender == "Female")
            firstNameList = femaleFirstNames;
        else // Other - pick randomly from either
            firstNameList = Random.value < 0.5f ? maleFirstNames : femaleFirstNames;

        if (firstNameList.Count == 0 || lastNames.Count == 0)
            return "Missing Name Data";

        string f = firstNameList[Random.Range(0, firstNameList.Count)];
        string l = lastNames[Random.Range(0, lastNames.Count)];
        return f + " " + l;
    }
}
