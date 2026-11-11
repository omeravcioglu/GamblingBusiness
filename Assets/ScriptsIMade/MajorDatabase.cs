using UnityEngine;

/// <summary>
/// Database of majors for applicants - includes both legitimate and humorous majors
/// </summary>
[CreateAssetMenu(fileName = "MajorDatabase", menuName = "Game/Major Database")]
public class MajorDatabase : ScriptableObject
{
    [Header("Major Lists")]
    [Tooltip("Legitimate, professional majors")]
    public string[] normalMajors;
    
    [Tooltip("Funny, useless, or absurd majors")]
    public string[] funnyMajors;
    
    [Header("Settings")]
    [Range(0f, 1f)]
    [Tooltip("Chance of getting a funny major (0.3 = 30%)")]
    public float funnyMajorChance = 0.35f;
    
    /// <summary>
    /// Get a random major (mix of normal and funny based on chance)
    /// </summary>
    public string GetRandomMajor()
    {
        bool useFunny = Random.value < funnyMajorChance && funnyMajors != null && funnyMajors.Length > 0;
        
        if (useFunny)
        {
            return funnyMajors[Random.Range(0, funnyMajors.Length)];
        }
        else if (normalMajors != null && normalMajors.Length > 0)
        {
            return normalMajors[Random.Range(0, normalMajors.Length)];
        }
        
        return "Undeclared";
    }
    
    /// <summary>
    /// Get a random normal/professional major
    /// </summary>
    public string GetRandomNormalMajor()
    {
        if (normalMajors != null && normalMajors.Length > 0)
            return normalMajors[Random.Range(0, normalMajors.Length)];
        return "Business Administration";
    }
    
    /// <summary>
    /// Get a random funny/absurd major
    /// </summary>
    public string GetRandomFunnyMajor()
    {
        if (funnyMajors != null && funnyMajors.Length > 0)
            return funnyMajors[Random.Range(0, funnyMajors.Length)];
        return "Underwater Basket Weaving";
    }
    
    /// <summary>
    /// Initialize with default majors if empty
    /// </summary>
    public void InitializeDefaults()
    {
        if (normalMajors == null || normalMajors.Length == 0)
        {
            normalMajors = new string[]
            {
                // Business & Finance (15)
                "Business Administration",
                "Accounting",
                "Finance",
                "Marketing",
                "Economics",
                "International Business",
                "Management",
                "Entrepreneurship",
                "Supply Chain Management",
                "Human Resources",
                "Real Estate",
                "Insurance",
                "Banking",
                "Investment Management",
                "Corporate Strategy",
                
                // STEM - Computer & Tech (15)
                "Computer Science",
                "Software Engineering",
                "Information Technology",
                "Cybersecurity",
                "Data Science",
                "Artificial Intelligence",
                "Computer Engineering",
                "Web Development",
                "Game Development",
                "Database Administration",
                "Network Engineering",
                "Cloud Computing",
                "Machine Learning",
                "Robotics",
                "Blockchain Technology",
                
                // STEM - Engineering (12)
                "Mechanical Engineering",
                "Electrical Engineering",
                "Civil Engineering",
                "Chemical Engineering",
                "Aerospace Engineering",
                "Biomedical Engineering",
                "Environmental Engineering",
                "Industrial Engineering",
                "Materials Engineering",
                "Nuclear Engineering",
                "Petroleum Engineering",
                "Structural Engineering",
                
                // STEM - Science (12)
                "Biology",
                "Chemistry",
                "Physics",
                "Mathematics",
                "Statistics",
                "Biochemistry",
                "Genetics",
                "Neuroscience",
                "Environmental Science",
                "Marine Biology",
                "Geology",
                "Astronomy",
                
                // Healthcare (10)
                "Nursing",
                "Pre-Medicine",
                "Pharmacy",
                "Public Health",
                "Health Administration",
                "Physical Therapy",
                "Dental Hygiene",
                "Radiology",
                "Medical Laboratory Science",
                "Veterinary Science",
                
                // Arts & Humanities (10)
                "English Literature",
                "History",
                "Philosophy",
                "Art History",
                "Music",
                "Theater",
                "Creative Writing",
                "Journalism",
                "Communications",
                "Film Studies",
                
                // Social Sciences (8)
                "Psychology",
                "Sociology",
                "Political Science",
                "Anthropology",
                "Criminal Justice",
                "Social Work",
                "International Relations",
                "Urban Planning",
                
                // Other Professional (8)
                "Law",
                "Education",
                "Architecture",
                "Graphic Design",
                "Interior Design",
                "Hospitality Management",
                "Culinary Arts",
                "Sports Management"
            };
        }
        
        if (funnyMajors == null || funnyMajors.Length == 0)
        {
            funnyMajors = new string[]
            {
                // Classic Memes (15)
                "Underwater Basket Weaving",
                "Professional Napping",
                "Advanced Procrastination",
                "Meme Economics",
                "TikTok Dance Theory",
                "Netflix Binge Studies",
                "Professional Gaming",
                "Influencer Marketing",
                "Cryptocurrency Speculation",
                "NFT Appreciation",
                "Flat Earth Research",
                "Conspiracy Theory Analysis",
                "Couch Potato Sciences",
                "Professional Ghosting",
                "Reply Guy Studies",
                
                // Absurd Academic (15)
                "Medieval Torture Device Maintenance",
                "Paranormal Investigation",
                "Zombie Preparedness",
                "Alien Communication",
                "Time Travel Paradoxes",
                "Bigfoot Tracking",
                "Loch Ness Monster Biology",
                "Crop Circle Geometry",
                "Psychic Powers Development",
                "Fortune Cookie Philosophy",
                "Horoscope Engineering",
                "Crystal Healing Mechanics",
                "Essential Oils Chemistry",
                "Flat Design Flat Earth",
                "Ancient Aliens Architecture",
                
                // Ridiculous Skills (15)
                "Professional Line Standing",
                "Elevator Button Pressing",
                "Door Holding Dynamics",
                "Awkward Silence Management",
                "Meeting That Could've Been an Email",
                "PowerPoint Animation",
                "Corporate Jargon",
                "Buzzword Engineering",
                "Synergy Maximization",
                "Inbox Zero Strategies",
                "Reply All Prevention",
                "Coffee Machine Operation",
                "Printer Troubleshooting",
                "Cable Management",
                "Desk Plant Maintenance",
                
                // Food-Related (10)
                "Pizza Topping Philosophy",
                "Cereal Pour Dynamics",
                "Microwave Cuisine",
                "Ramen Noodleology",
                "Hot Dog Sandwich Debate",
                "Pineapple Pizza Ethics",
                "Avocado Toast Economics",
                "Energy Drink Mixology",
                "Midnight Snack Logistics",
                "Fast Food Menu Memorization",
                
                // Internet Culture (10)
                "Reddit Moderation",
                "Discord Server Management",
                "Twitter Ratio Studies",
                "YouTube Comment Analysis",
                "Wikipedia Editing",
                "Online Argument Tactics",
                "Keyboard Warrior Training",
                "Emoji Communication",
                "Hashtag Strategy",
                "Viral Content Creation",
                
                // Life Skills Gone Wrong (10)
                "Parallel Parking Failure Analysis",
                "IKEA Furniture Assembly",
                "Tax Procrastination",
                "Laundry Color Separation",
                "Sock Matching Theory",
                "Tupperware Lid Finding",
                "Remote Control Locating",
                "Phone Charger Placement",
                "Password Forgetting",
                "Terms and Conditions Reading",
                
                // Workplace Absurdity (10)
                "Unnecessary Meeting Hosting",
                "Water Cooler Conversation",
                "Passive Aggressive Email Writing",
                "Office Gossip Networks",
                "Lunch Theft Investigation",
                "Thermostat War Strategy",
                "Open Office Survival",
                "Zoom Background Curation",
                "Mute Button Awareness",
                "Work From Home Pajama Studies",
                
                // Extremely Specific (10)
                "Left-Handed Scissors Engineering",
                "Bubble Wrap Popping",
                "Velcro Sound Design",
                "Shopping Cart Wheel Repair",
                "Elevator Music Composition",
                "Hold Music Psychology",
                "Automatic Door Sensor Calibration",
                "Parking Meter Optimization",
                "Speed Bump Architecture",
                "Traffic Cone Placement",
                
                // Gaming & Pop Culture (10)
                "Speedrun Optimization",
                "Achievement Hunting",
                "Video Game Journalism Ethics",
                "Loot Box Probability",
                "Battle Pass Completion",
                "Rage Quit Prevention",
                "Controller Throwing Dynamics",
                "Gaming Chair Ergonomics",
                "RGB Lighting Design",
                "Keyboard Clacking ASMR"
            };
        }
    }
    
    void OnEnable()
    {
        // Auto-initialize if empty
        if ((normalMajors == null || normalMajors.Length == 0) && 
            (funnyMajors == null || funnyMajors.Length == 0))
        {
            InitializeDefaults();
        }
    }
}
