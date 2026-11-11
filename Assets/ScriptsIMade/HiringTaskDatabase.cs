using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Database of hiring tasks with humorous descriptions
/// </summary>
[CreateAssetMenu(fileName = "HiringTaskDatabase", menuName = "Game/Hiring Task Database")]
public class HiringTaskDatabase : ScriptableObject
{
    public List<HiringTask> tasks = new List<HiringTask>();
    
    /// <summary>
    /// Get a random task from the database
    /// </summary>
    public HiringTask GetRandomTask()
    {
        if (tasks == null || tasks.Count == 0)
        {
            InitializeDefaultTasks();
        }

        if (tasks == null || tasks.Count == 0) return null;

        return tasks[Random.Range(0, tasks.Count)];
    }

    /// <summary>
    /// Get a random task of specific difficulty
    /// </summary>
    public HiringTask GetRandomTaskByDifficulty(int difficulty)
    {
        if (tasks == null || tasks.Count == 0) return GetRandomTask();
        var filtered = tasks.FindAll(t => t != null && t.difficulty == difficulty);
        if (filtered.Count == 0) return GetRandomTask();
        return filtered[Random.Range(0, filtered.Count)];
    }
    
    /// <summary>
    /// Initialize with 200+ default tasks
    /// </summary>
    public void InitializeDefaultTasks()
    {
        if (tasks != null && tasks.Count > 0) return;
        
        tasks = new List<HiringTask>();
        
        // ============ RACE-BASED TASKS (20) ============
        AddTask("Diversity Quota", "HR says we're 'too vanilla'. Time to spice things up!", TaskRequirementType.HireRace, "Black", 4000, 1500, 90, 2);
        AddTask("The Tokenism Special", "Board meeting next week. Need some diversity for the photo op.", TaskRequirementType.HireRace, "Black", 5000, 2000, 90, 2);
        AddTask("Cultural Exchange", "Someone mentioned 'bubble tea' and everyone looked confused. Fix this.", TaskRequirementType.HireRace, "Asian", 4000, 1500, 90, 2);
        AddTask("Tech Support Stereotype", "IT needs reinforcements. You know what to do.", TaskRequirementType.HireRace, "Asian", 4500, 1800, 90, 2);
        AddTask("Taco Tuesday Initiative", "The cafeteria wants authentic recipes.", TaskRequirementType.HireRace, "Hispanic", 4000, 1500, 90, 2);
        AddTask("Spicy Hire", "Someone complained the office is too bland.", TaskRequirementType.HireRace, "Hispanic", 4500, 1800, 90, 2);
        AddTask("The Mayonnaise Offensive", "Need someone who thinks mayo is spicy.", TaskRequirementType.HireRace, "White", 3000, 1000, 90, 1);
        AddTask("Country Club Vibes", "The golf committee needs a fourth.", TaskRequirementType.HireRace, "White", 3500, 1200, 90, 1);
        AddTask("Mixed Signals", "We need someone who can check multiple boxes on forms.", TaskRequirementType.HireRace, "Mixed", 5000, 2000, 120, 3);
        AddTask("Best of Both Worlds", "Looking for someone with diverse perspectives. Literally.", TaskRequirementType.HireRace, "Mixed", 5500, 2200, 120, 3);
        AddTask("International Relations", "Need someone who understands geopolitics... personally.", TaskRequirementType.HireRace, "Middle Eastern", 5000, 2000, 120, 3);
        AddTask("Oil Money Energy", "The finance team needs someone with natural resource vibes.", TaskRequirementType.HireRace, "Middle Eastern", 5500, 2200, 120, 3);
        
        // ============ GENDER-BASED TASKS (15) ============
        AddTask("Boys Club Recruitment", "The fantasy football league needs fresh meat.", TaskRequirementType.HireGender, "Male", 3000, 1000, 90, 1);
        AddTask("Testosterone Boost", "Too much drama in the office. Need someone simple.", TaskRequirementType.HireGender, "Male", 3500, 1200, 90, 1);
        AddTask("The Bro Code", "Sales team wants someone who 'gets it'.", TaskRequirementType.HireGender, "Male", 4000, 1500, 90, 1);
        AddTask("Gender Balance Act", "HR's diversity report is looking sus.", TaskRequirementType.HireGender, "Female", 4000, 1500, 90, 2);
        AddTask("Girl Power Hour", "The book club needs reinforcements.", TaskRequirementType.HireGender, "Female", 3500, 1200, 90, 1);
        AddTask("Break the Glass Ceiling", "Time to hire someone who can do it while looking fabulous.", TaskRequirementType.HireGender, "Female", 4500, 1800, 90, 2);
        AddTask("Bathroom Debate Settler", "Just... hire someone interesting.", TaskRequirementType.HireGender, "Other", 6000, 2500, 120, 4);
        AddTask("Pronoun Powerplay", "The email signatures are getting too boring.", TaskRequirementType.HireGender, "Other", 6500, 2800, 120, 4);
        
        // ============ AGE-BASED TASKS (25) ============
        AddTask("OK Boomer", "Need someone who remembers dial-up.", TaskRequirementType.HireAgeMin, "55", 5000, 2000, 90, 3);
        AddTask("Wisdom Hire", "The interns keep asking 'who's that?' about historical figures.", TaskRequirementType.HireAgeMin, "50", 4500, 1800, 90, 2);
        AddTask("Vintage Employee", "Someone who calls the internet 'the information superhighway'.", TaskRequirementType.HireAgeMin, "60", 6000, 2500, 120, 4);
        AddTask("The Retirement Risk", "Hire someone who might not make it to their pension.", TaskRequirementType.HireAgeMin, "58", 5500, 2200, 120, 3);
        AddTask("Gen Z Energy", "We need someone who can explain TikTok to the CEO.", TaskRequirementType.HireAgeMax, "25", 4000, 1500, 90, 2);
        AddTask("Zoomer Zoom Call", "The meeting backgrounds are too boring.", TaskRequirementType.HireAgeMax, "23", 4500, 1800, 90, 2);
        AddTask("Unpaid Intern Vibes", "But we'll actually pay them. A little.", TaskRequirementType.HireAgeMax, "22", 3500, 1200, 90, 2);
        AddTask("Fresh Meat", "Someone who still has hope in their eyes.", TaskRequirementType.HireAgeMax, "24", 4000, 1500, 90, 2);
        AddTaskRange("Millennial Mediator", "Need someone who gets both generations.", TaskRequirementType.HireAgeRange, "30", "40", 4500, 1800, 90, 2);
        AddTaskRange("Prime Years", "Peak performance age. Science says so.", TaskRequirementType.HireAgeRange, "28", "35", 5000, 2000, 90, 2);
        AddTaskRange("Quarter-Life Crisis", "Hire someone going through it. They work harder.", TaskRequirementType.HireAgeRange, "24", "28", 4000, 1500, 90, 2);
        AddTaskRange("Midlife Crisis Hire", "Someone who just bought a sports car.", TaskRequirementType.HireAgeRange, "45", "55", 5000, 2000, 90, 3);
        AddTask("Teen Spirit", "We need that Nirvana energy. Hire young.", TaskRequirementType.HireAgeMax, "20", 5500, 2200, 120, 3);
        AddTask("Almost Retired", "Someone counting down the days.", TaskRequirementType.HireAgeMin, "62", 6500, 2800, 120, 4);
        
        // ============ IQ-BASED TASKS (30) ============
        AddTask("Smooth Brain Energy", "Too many ideas in meetings. Hire someone quiet.", TaskRequirementType.HireIQMax, "85", 5000, 2000, 90, 3);
        AddTask("The Yes Man", "Need someone who won't question anything.", TaskRequirementType.HireIQMax, "90", 4500, 1800, 90, 2);
        AddTask("Intellectual Dip", "The smart people are getting uppity. Balance it out.", TaskRequirementType.HireIQMax, "88", 4800, 1900, 90, 3);
        AddTask("Dunning-Kruger Hire", "Someone confident despite... everything.", TaskRequirementType.HireIQMax, "85", 5000, 2000, 90, 3);
        AddTask("Room Temperature IQ", "In Celsius. We need basic.", TaskRequirementType.HireIQMax, "80", 6000, 2500, 120, 4);
        AddTask("Barely Functional", "Someone who can follow simple instructions. Maybe.", TaskRequirementType.HireIQMax, "82", 5500, 2200, 120, 3);
        AddTask("Big Brain Time", "The strategy team needs reinforcements.", TaskRequirementType.HireIQMin, "120", 5000, 2000, 90, 3);
        AddTask("Galaxy Brain", "Someone to explain blockchain to the board.", TaskRequirementType.HireIQMin, "130", 6000, 2500, 120, 4);
        AddTask("Genius Grant", "We need someone intimidatingly smart.", TaskRequirementType.HireIQMin, "140", 8000, 3500, 120, 5);
        AddTask("Mensa Material", "The puzzle room needs a champion.", TaskRequirementType.HireIQMin, "135", 7000, 3000, 120, 4);
        AddTaskRange("Average Joe", "Someone beautifully, perfectly average.", TaskRequirementType.HireIQRange, "95", "105", 4000, 1500, 90, 2);
        AddTaskRange("The Bell Curve Peak", "Statistically optimal employee.", TaskRequirementType.HireIQRange, "98", "102", 5000, 2000, 120, 3);
        AddTaskRange("Slightly Below Average", "Smart enough to work, dumb enough to stay.", TaskRequirementType.HireIQRange, "85", "95", 4500, 1800, 90, 2);
        AddTaskRange("Almost Smart", "So close, yet so far.", TaskRequirementType.HireIQRange, "100", "115", 4000, 1500, 90, 2);
        AddTaskRange("The Goldilocks Zone", "Not too smart, not too dumb. Just right.", TaskRequirementType.HireIQRange, "90", "110", 3500, 1200, 90, 1);
        
        // ============ EDUCATION-BASED TASKS (25) ============
        AddTask("High School Dropout Energy", "We need someone 'street smart'.", TaskRequirementType.HireDegree, "High School", 4000, 1500, 90, 2);
        AddTask("No Debt Club", "Someone who didn't waste money on college.", TaskRequirementType.HireDegree, "High School", 3500, 1200, 90, 1);
        AddTask("Community College Represent", "Associate's degrees are underrated.", TaskRequirementType.HireDegree, "Associate Degree", 4500, 1800, 90, 2);
        AddTask("Two-Year Wonder", "Quick learner, quick degree.", TaskRequirementType.HireDegree, "Associate Degree", 4000, 1500, 90, 2);
        AddTask("Bachelor Pad", "Standard issue employee needed.", TaskRequirementType.HireDegree, "Bachelor", 3000, 1000, 90, 1);
        AddTask("Four Years of Partying", "I mean... studying. Definitely studying.", TaskRequirementType.HireDegree, "Bachelor", 3500, 1200, 90, 1);
        AddTask("Master of None", "Someone with a Master's who still can't find a job.", TaskRequirementType.HireDegree, "Master", 5000, 2000, 90, 2);
        AddTask("Overqualified", "Time to exploit someone's poor life choices.", TaskRequirementType.HireDegree, "Master", 5500, 2200, 90, 3);
        AddTask("Doctor in the House", "Not that kind of doctor. The useless kind.", TaskRequirementType.HireDegree, "PhD", 7000, 3000, 120, 4);
        AddTask("Thesis Defense", "Someone who spent 7 years on something no one will read.", TaskRequirementType.HireDegree, "PhD", 7500, 3200, 120, 4);
        AddTask("Eternal Student", "PhD holders who still don't know what they want to do.", TaskRequirementType.HireDegree, "PhD", 8000, 3500, 120, 4);
        
        // ============ MAJOR-BASED TASKS (30) ============
        AddTask("STEM Lord", "Someone who thinks they're better than everyone.", TaskRequirementType.HireMajorContains, "Engineering", 5000, 2000, 90, 2);
        AddTask("Code Monkey", "We need a programmer. Any programmer.", TaskRequirementType.HireMajorContains, "Computer", 5000, 2000, 90, 2);
        AddTask("Business Buzzwords", "Someone who says 'synergy' unironically.", TaskRequirementType.HireMajorContains, "Business", 4000, 1500, 90, 1);
        AddTask("Art History Moment", "We need someone to curate the office art.", TaskRequirementType.HireMajorContains, "Art", 4500, 1800, 90, 2);
        AddTask("Psychology Project", "Hire someone who can analyze why we're all here.", TaskRequirementType.HireMajorContains, "Psychology", 4500, 1800, 90, 2);
        AddTask("Math Nerd", "Someone to fix the Excel sheets.", TaskRequirementType.HireMajorContains, "Math", 5500, 2200, 90, 3);
        AddTask("Communications Major", "Finally, someone who can talk good.", TaskRequirementType.HireMajorContains, "Communication", 4000, 1500, 90, 1);
        AddTask("English Major Hire", "Someone to fix everyone's grammar.", TaskRequirementType.HireMajorContains, "English", 4000, 1500, 90, 2);
        AddTask("Basket Weaving", "Hire someone with a... unique major.", TaskRequirementType.HireMajorContains, "Basket", 8000, 3500, 120, 5);
        AddTask("Meme Economist", "The social media team needs help.", TaskRequirementType.HireMajorContains, "Meme", 7000, 3000, 120, 4);
        AddTask("Professional Napper", "HR is introducing nap pods.", TaskRequirementType.HireMajorContains, "Nap", 7500, 3200, 120, 4);
        AddTask("Finance Bro", "Someone who wears Patagonia vests.", TaskRequirementType.HireMajorContains, "Finance", 5000, 2000, 90, 2);
        AddTask("Marketing Magician", "Hire someone who can make nothing sound amazing.", TaskRequirementType.HireMajorContains, "Marketing", 4500, 1800, 90, 2);
        AddTask("Philosophy Major", "Someone to ponder why we're doing this.", TaskRequirementType.HireMajorContains, "Philosophy", 5000, 2000, 90, 2);
        AddTask("History Buff", "Need someone who knows we're repeating mistakes.", TaskRequirementType.HireMajorContains, "History", 4500, 1800, 90, 2);
        
        // ============ GPA-BASED TASKS (15) ============
        AddTask("Straight A's", "Need a nerd. A real one.", TaskRequirementType.HireGPAMin, "3.8", 6000, 2500, 90, 3);
        AddTask("Dean's List", "Someone who actually tried in school.", TaskRequirementType.HireGPAMin, "3.5", 5000, 2000, 90, 2);
        AddTask("Honor Roll", "Academic excellence required.", TaskRequirementType.HireGPAMin, "3.7", 5500, 2200, 90, 3);
        AddTask("C's Get Degrees", "Hire someone who knows what matters.", TaskRequirementType.HireGPAMax, "2.5", 5000, 2000, 90, 3);
        AddTask("Barely Passed", "Someone who partied hard but still graduated.", TaskRequirementType.HireGPAMax, "2.2", 5500, 2200, 90, 3);
        AddTask("Academic Probation Survivor", "They made it. Somehow.", TaskRequirementType.HireGPAMax, "2.0", 6000, 2500, 120, 4);
        AddTask("The Party Animal", "GPA doesn't matter when you have connections.", TaskRequirementType.HireGPAMax, "2.3", 5000, 2000, 90, 3);
        
        // ============ EXPERIENCE-BASED TASKS (20) ============
        AddTask("Fresh Graduate", "Someone who doesn't know how bad it gets.", TaskRequirementType.HireExperienceMax, "1", 4000, 1500, 90, 2);
        AddTask("Zero Experience", "A blank slate. Perfect for molding.", TaskRequirementType.HireExperienceMax, "0", 5000, 2000, 90, 3);
        AddTask("Entry Level", "Someone we can underpay legally.", TaskRequirementType.HireExperienceMax, "2", 3500, 1200, 90, 1);
        AddTask("Seasoned Veteran", "10+ years of corporate trauma.", TaskRequirementType.HireExperienceMin, "10", 5500, 2200, 90, 3);
        AddTask("Industry Expert", "Someone who's seen it all.", TaskRequirementType.HireExperienceMin, "15", 6500, 2800, 120, 4);
        AddTask("Career Lifer", "20+ years. They're never leaving.", TaskRequirementType.HireExperienceMin, "20", 7500, 3200, 120, 4);
        AddTask("Living Legend", "Someone who remembers the before times.", TaskRequirementType.HireExperienceMin, "25", 8500, 3800, 120, 5);
        AddTask("Mid-Career Crisis", "5-10 years and questioning everything.", TaskRequirementType.HireExperienceMin, "5", 4500, 1800, 90, 2);
        
        // ============ SALARY-BASED TASKS (15) ============
        AddTask("Budget Hire", "Maximum value, minimum cost.", TaskRequirementType.HireSalaryMax, "50000", 4000, 1500, 90, 2);
        AddTask("Cheap Labor", "Someone desperate enough.", TaskRequirementType.HireSalaryMax, "40000", 5000, 2000, 90, 3);
        AddTask("Bargain Basement", "The clearance rack of employees.", TaskRequirementType.HireSalaryMax, "35000", 6000, 2500, 120, 4);
        AddTask("Big Spender", "Hire someone expensive. We need clout.", TaskRequirementType.HireSalaryMin, "150000", 5000, 2000, 90, 3);
        AddTask("Premium Employee", "Quality costs money.", TaskRequirementType.HireSalaryMin, "180000", 6000, 2500, 120, 4);
        AddTask("Executive Material", "Someone who costs as much as a house.", TaskRequirementType.HireSalaryMin, "200000", 7000, 3000, 120, 4);
        
        // ============ CRIMINAL RECORD TASKS (15) ============
        AddTask("Second Chance", "Everyone deserves redemption. Even them.", TaskRequirementType.HireWithCriminalRecord, "", 6000, 2500, 120, 3);
        AddTask("Reformed Criminal", "They've paid their debt to society.", TaskRequirementType.HireWithCriminalRecord, "", 5500, 2200, 120, 3);
        AddTask("Diverse Background", "HR says 'criminal record' is just 'alternative experience'.", TaskRequirementType.HireWithCriminalRecord, "", 5000, 2000, 90, 3);
        AddTask("Risk Taker", "The accounting department needs someone... flexible.", TaskRequirementType.HireWithCriminalRecord, "", 6500, 2800, 120, 4);
        AddTask("Squeaky Clean", "Background check must be spotless.", TaskRequirementType.HireWithoutCriminalRecord, "", 3000, 1000, 90, 1);
        AddTask("Boy Scout", "Need someone boringly law-abiding.", TaskRequirementType.HireWithoutCriminalRecord, "", 3500, 1200, 90, 1);
        AddTask("Clearance Ready", "Government contract incoming.", TaskRequirementType.HireWithoutCriminalRecord, "", 4000, 1500, 90, 2);
        
        // ============ GOLDEN APPLICANT TASKS (5) ============
        AddTask("Unicorn Hunt", "Find the mythical perfect employee.", TaskRequirementType.HireGolden, "", 15000, 5000, 180, 5);
        AddTask("Golden Child", "Only the best of the best.", TaskRequirementType.HireGolden, "", 12000, 4000, 180, 5);
        AddTask("Diamond in the Rough", "Find that 1 in 100.", TaskRequirementType.HireGolden, "", 10000, 3500, 180, 5);
        AddTask("The Chosen One", "Neo is out there. Find them.", TaskRequirementType.HireGolden, "", 13000, 4500, 180, 5);
        AddTask("Legendary Hire", "A mythical being walks among us.", TaskRequirementType.HireGolden, "", 14000, 4800, 180, 5);
        
        // ============ EASY/ANY HIRE TASKS (10) ============
        AddTask("Warm Body Needed", "Just... anyone. Please.", TaskRequirementType.HireAny, "", 2000, 500, 60, 1);
        AddTask("Headcount Quota", "HR says we need more people. Any people.", TaskRequirementType.HireAny, "", 2500, 800, 60, 1);
        AddTask("Emergency Hire", "Someone quit. Fill the desk.", TaskRequirementType.HireAny, "", 3000, 1000, 45, 1);
        AddTask("Rapid Recruitment", "The board is watching. Hire fast.", TaskRequirementType.HireAny, "", 3500, 1200, 30, 1);
        AddTask("Open Position", "Looking for a human. Requirements: pulse.", TaskRequirementType.HireAny, "", 2000, 500, 90, 1);
        
        // ============ DARK HUMOR SPECIAL TASKS (30) ============
        AddTask("Insurance Premium", "Hire someone old. The premiums are lower. Wait, that's illegal. Do it anyway.", TaskRequirementType.HireAgeMin, "60", 7000, 3000, 120, 4);
        AddTask("Lawsuit Bait", "HR needs to justify their existence.", TaskRequirementType.HireRace, "Black", 5000, 2000, 90, 2);
        AddTask("Exploitation Special", "Fresh graduate + low IQ = cheap labor forever.", TaskRequirementType.HireIQMax, "88", 5500, 2200, 90, 3);
        AddTask("Trophy Hire", "Someone pretty for the front desk.", TaskRequirementType.HireGender, "Female", 4000, 1500, 90, 2);
        AddTask("The Fall Guy", "Need someone to blame when things go wrong.", TaskRequirementType.HireWithCriminalRecord, "", 6000, 2500, 120, 3);
        AddTask("Intellectual Diversity", "Too many smart people. They're unionizing.", TaskRequirementType.HireIQMax, "85", 5000, 2000, 90, 3);
        AddTask("Young and Naive", "They don't know their worth yet. Perfect.", TaskRequirementType.HireAgeMax, "22", 4500, 1800, 90, 2);
        AddTask("Old and Desperate", "They need the job. We have leverage.", TaskRequirementType.HireAgeMin, "58", 5000, 2000, 90, 3);
        AddTask("Debt Slave", "PhD + low salary = loyalty.", TaskRequirementType.HireDegree, "PhD", 6000, 2500, 120, 4);
        AddTask("The Sacrificial Lamb", "Layoffs are coming. Hire someone expendable.", TaskRequirementType.HireExperienceMax, "1", 4000, 1500, 90, 2);
        AddTask("Scapegoat Material", "Someone to absorb blame professionally.", TaskRequirementType.HireIQMax, "90", 5000, 2000, 90, 3);
        AddTask("Tax Write-Off", "Diversity initiative = tax benefits.", TaskRequirementType.HireRace, "Hispanic", 4500, 1800, 90, 2);
        AddTask("Optics Hire", "The investors are visiting next week.", TaskRequirementType.HireGender, "Female", 4000, 1500, 90, 2);
        AddTask("Social Media Defense", "Twitter found out we're all white. Fix it.", TaskRequirementType.HireRace, "Asian", 5000, 2000, 90, 2);
        AddTask("Liability Reduction", "Someone without a criminal past. For insurance.", TaskRequirementType.HireWithoutCriminalRecord, "", 3500, 1200, 90, 1);
        
        // ============ MORE RACE TASKS (10) ============
        AddTask("United Colors", "Benetton called. They want their diversity back.", TaskRequirementType.HireRace, "Mixed", 5500, 2200, 120, 3);
        AddTask("The Token", "One for the brochure.", TaskRequirementType.HireRace, "Black", 4500, 1800, 90, 2);
        AddTask("Affirmative Action", "The quota isn't going to fill itself.", TaskRequirementType.HireRace, "Hispanic", 4500, 1800, 90, 2);
        AddTask("K-Pop Energy", "The office playlist needs updating.", TaskRequirementType.HireRace, "Asian", 5000, 2000, 90, 2);
        AddTask("Chai Tea Consultant", "We need someone to explain why it's not 'chai tea'.", TaskRequirementType.HireRace, "Middle Eastern", 5000, 2000, 120, 3);
        AddTask("Vanilla Extract", "Sometimes you just need the classic flavor.", TaskRequirementType.HireRace, "White", 3000, 1000, 90, 1);
        AddTask("Melanin Mandate", "Sunscreen budget is too high. Hire someone who doesn't need it.", TaskRequirementType.HireRace, "Black", 4500, 1800, 90, 2);
        AddTask("Anime Consultant", "We need cultural expertise.", TaskRequirementType.HireRace, "Asian", 5000, 2000, 90, 2);
        AddTask("Passport Collection", "HR is making a world map of employee origins.", TaskRequirementType.HireRace, "Middle Eastern", 5500, 2200, 120, 3);
        AddTask("The Rainbow Coalition", "Pride month is coming. Prepare.", TaskRequirementType.HireRace, "Mixed", 5000, 2000, 120, 3);
        
        // ============ MORE IQ TASKS (15) ============
        AddTask("Village Idiot", "Every village needs one. We're overdue.", TaskRequirementType.HireIQMax, "78", 7000, 3000, 120, 5);
        AddTask("Forrest Gump Hire", "Stupid is as stupid does.", TaskRequirementType.HireIQMax, "82", 6000, 2500, 120, 4);
        AddTask("NPC Energy", "Need someone to fill space.", TaskRequirementType.HireIQMax, "88", 5000, 2000, 90, 3);
        AddTask("Meat in the Seat", "Warm body. Minimal thought required.", TaskRequirementType.HireIQMax, "85", 5500, 2200, 90, 3);
        AddTask("Rick Sanchez", "Need a genius. Accept the consequences.", TaskRequirementType.HireIQMin, "145", 9000, 4000, 120, 5);
        AddTask("Einstein Mode", "Relatively speaking, we need someone smart.", TaskRequirementType.HireIQMin, "138", 7500, 3200, 120, 4);
        AddTask("Stephen Hawking Jr", "Brief history of hiring smart people.", TaskRequirementType.HireIQMin, "142", 8000, 3500, 120, 5);
        AddTask("Think Tank Filler", "Our think tank is running low.", TaskRequirementType.HireIQMin, "125", 5500, 2200, 90, 3);
        AddTask("Brainiac", "Need someone to explain our own product to us.", TaskRequirementType.HireIQMin, "128", 6000, 2500, 90, 3);
        AddTaskRange("Below Median", "Perfectly unimpressive.", TaskRequirementType.HireIQRange, "88", "98", 4500, 1800, 90, 2);
        AddTaskRange("Slightly Dim", "Just enough to do the job. Barely.", TaskRequirementType.HireIQRange, "80", "90", 5000, 2000, 90, 3);
        AddTaskRange("Impressively Average", "The most average person you can find.", TaskRequirementType.HireIQRange, "97", "103", 5500, 2200, 120, 3);
        AddTask("Room Temp IQ Gang", "In Fahrenheit this time.", TaskRequirementType.HireIQMax, "75", 8000, 3500, 120, 5);
        AddTask("Moderately Challenged", "Intellectual modesty required.", TaskRequirementType.HireIQMax, "92", 4500, 1800, 90, 2);
        AddTaskRange("Competently Dim", "Dumb enough to stay, smart enough to work.", TaskRequirementType.HireIQRange, "85", "95", 4500, 1800, 90, 2);
        
        // ============ MORE AGE TASKS (15) ============
        AddTask("Fossil Fuel", "Hire someone who runs on nostalgia.", TaskRequirementType.HireAgeMin, "63", 7000, 3000, 120, 4);
        AddTask("Jurassic Hire", "Someone who saw the dinosaurs.", TaskRequirementType.HireAgeMin, "64", 7500, 3200, 120, 5);
        AddTask("Antique Employee", "Vintage value.", TaskRequirementType.HireAgeMin, "59", 6000, 2500, 120, 4);
        AddTask("Silver Fox", "Distinguished gray hair required.", TaskRequirementType.HireAgeMin, "52", 5000, 2000, 90, 3);
        AddTask("Baby Employee", "Fresh from the womb of academia.", TaskRequirementType.HireAgeMax, "21", 4500, 1800, 90, 2);
        AddTask("Child Labor Adjacent", "Legally an adult. Barely.", TaskRequirementType.HireAgeMax, "19", 5500, 2200, 120, 3);
        AddTask("TikTok Native", "Someone who doesn't remember a world without internet.", TaskRequirementType.HireAgeMax, "26", 4000, 1500, 90, 2);
        AddTask("iPad Baby", "They were raised by a screen. Perfect for screen work.", TaskRequirementType.HireAgeMax, "24", 4500, 1800, 90, 2);
        AddTaskRange("Thirtysomething Crisis", "Peak anxiety years.", TaskRequirementType.HireAgeRange, "32", "38", 4000, 1500, 90, 2);
        AddTaskRange("The Answer to Everything", "Someone exactly 42 years old.", TaskRequirementType.HireAgeRange, "42", "42", 8000, 3500, 120, 5);
        AddTaskRange("Late Bloomer", "Started career late. Extra hungry.", TaskRequirementType.HireAgeRange, "35", "45", 4500, 1800, 90, 2);
        AddTaskRange("Pre-Retirement Panic", "5 years from the finish line.", TaskRequirementType.HireAgeRange, "60", "65", 5500, 2200, 120, 3);
        AddTaskRange("Existential Crisis Era", "The 'is this all there is?' years.", TaskRequirementType.HireAgeRange, "40", "50", 4500, 1800, 90, 2);
        AddTask("Youth Movement", "Bring down the average age.", TaskRequirementType.HireAgeMax, "28", 4000, 1500, 90, 2);
        AddTask("Wisdom Injection", "Raise the average age.", TaskRequirementType.HireAgeMin, "48", 4500, 1800, 90, 2);
        
        // ============ MORE EDUCATION TASKS (10) ============
        AddTask("Dropout Redemption", "Bill Gates did it. So can they.", TaskRequirementType.HireDegree, "High School", 4500, 1800, 90, 2);
        AddTask("Trade School Victory", "Not everyone needs a four-year degree. But you still need to hire one.", TaskRequirementType.HireDegree, "Associate Degree", 4500, 1800, 90, 2);
        AddTask("State School Pride", "Bachelor's from a party school counts.", TaskRequirementType.HireDegree, "Bachelor", 3500, 1200, 90, 1);
        AddTask("Grad School Survivor", "They endured. Barely.", TaskRequirementType.HireDegree, "Master", 5500, 2200, 90, 3);
        AddTask("Academic Masochist", "PhD in suffering.", TaskRequirementType.HireDegree, "PhD", 7000, 3000, 120, 4);
        AddTask("No Student Loans", "Hire someone debt-free.", TaskRequirementType.HireDegree, "High School", 4000, 1500, 90, 2);
        AddTask("Ivy League Dropout", "They got in. That's what matters.", TaskRequirementType.HireDegree, "High School", 5000, 2000, 90, 3);
        AddTask("Community College King", "Two years and done.", TaskRequirementType.HireDegree, "Associate Degree", 4000, 1500, 90, 2);
        AddTask("Honors Graduate", "Someone with Latin on their diploma.", TaskRequirementType.HireGPAMin, "3.9", 7000, 3000, 120, 4);
        AddTask("Summa Cum Laude", "The cream of the crop.", TaskRequirementType.HireGPAMin, "3.95", 8000, 3500, 120, 5);
        
        // ============ MORE SALARY TASKS (10) ============
        AddTask("Poverty Wages", "Hire someone desperate.", TaskRequirementType.HireSalaryMax, "32000", 6500, 2800, 120, 4);
        AddTask("Living Wage Lite", "Just above minimum.", TaskRequirementType.HireSalaryMax, "38000", 5500, 2200, 90, 3);
        AddTask("Mid-Range Mediocrity", "Average pay, average expectations.", TaskRequirementType.HireSalaryMax, "65000", 4000, 1500, 90, 2);
        AddTask("Six Figure Club", "Someone who knows their worth.", TaskRequirementType.HireSalaryMin, "100000", 4500, 1800, 90, 2);
        AddTask("Quarter Million", "Money talks.", TaskRequirementType.HireSalaryMin, "220000", 7500, 3200, 120, 5);
        AddTask("Budget Constraint", "We're broke. Hire cheap.", TaskRequirementType.HireSalaryMax, "45000", 5000, 2000, 90, 2);
        AddTask("Money's No Object", "Hire someone expensive just because.", TaskRequirementType.HireSalaryMin, "160000", 5500, 2200, 90, 3);
        AddTask("Negotiation Practice", "Hire someone asking too much.", TaskRequirementType.HireSalaryMin, "175000", 6000, 2500, 120, 4);
        AddTask("Steal of a Deal", "Amazing talent, cheap price.", TaskRequirementType.HireSalaryMax, "42000", 5500, 2200, 90, 3);
        AddTask("Premium Price", "Pay top dollar.", TaskRequirementType.HireSalaryMin, "190000", 6500, 2800, 120, 4);
        
        // ============ MORE EXPERIENCE TASKS (10) ============
        AddTask("Blank Slate", "No experience = no bad habits.", TaskRequirementType.HireExperienceMax, "0", 5500, 2200, 90, 3);
        AddTask("Newbie Energy", "Still believes in work-life balance.", TaskRequirementType.HireExperienceMax, "2", 4000, 1500, 90, 2);
        AddTask("Battle-Tested", "At least 8 years of corporate warfare.", TaskRequirementType.HireExperienceMin, "8", 5000, 2000, 90, 2);
        AddTask("Survivor", "12+ years and still standing.", TaskRequirementType.HireExperienceMin, "12", 5500, 2200, 90, 3);
        AddTask("Ancient Wisdom", "30+ years in the game.", TaskRequirementType.HireExperienceMin, "30", 8000, 3500, 120, 5);
        AddTask("Career Starter", "First real job material.", TaskRequirementType.HireExperienceMax, "1", 4500, 1800, 90, 2);
        AddTask("The Grind Veteran", "15 years of emails.", TaskRequirementType.HireExperienceMin, "15", 6000, 2500, 120, 3);
        AddTask("Office Politics PhD", "20 years of survival.", TaskRequirementType.HireExperienceMin, "20", 7000, 3000, 120, 4);
        AddTask("Fresh Blood", "No corporate trauma yet.", TaskRequirementType.HireExperienceMax, "3", 3500, 1200, 90, 1);
        AddTask("Hardened Professional", "7+ years of disappointment.", TaskRequirementType.HireExperienceMin, "7", 4500, 1800, 90, 2);
        
        // ============ MORE GENDER TASKS (7) ============
        AddTask("Locker Room Talk", "The boys need a new member.", TaskRequirementType.HireGender, "Male", 3500, 1200, 90, 1);
        AddTask("Bro Down", "Time for some masculine energy.", TaskRequirementType.HireGender, "Male", 4000, 1500, 90, 1);
        AddTask("The Wine Club", "Friday wine nights need fresh members.", TaskRequirementType.HireGender, "Female", 3500, 1200, 90, 1);
        AddTask("Maternity Coverage", "Someone to cover potential leaves.", TaskRequirementType.HireGender, "Male", 4000, 1500, 90, 2);
        AddTask("Gender Ratio", "The balance is off. Fix it.", TaskRequirementType.HireGender, "Female", 4000, 1500, 90, 2);
        AddTask("Progressive Points", "Hire someone non-binary.", TaskRequirementType.HireGender, "Other", 6500, 2800, 120, 4);
        AddTask("Spectrum Representative", "Expand the gender diversity.", TaskRequirementType.HireGender, "Other", 7000, 3000, 120, 4);
        
        // ============ MORE MAJOR TASKS (15) ============
        AddTask("Liberal Arts Appreciation", "Someone who studied things.", TaskRequirementType.HireMajorContains, "Art", 4500, 1800, 90, 2);
        AddTask("Science Fiction", "Hire a scientist. Any scientist.", TaskRequirementType.HireMajorContains, "Science", 4500, 1800, 90, 2);
        AddTask("Number Cruncher", "Someone who likes math.", TaskRequirementType.HireMajorContains, "Statistic", 5500, 2200, 90, 3);
        AddTask("Word Nerd", "Someone who reads books. Voluntarily.", TaskRequirementType.HireMajorContains, "Literature", 4500, 1800, 90, 2);
        AddTask("Healthcare Helper", "Someone who knows CPR maybe.", TaskRequirementType.HireMajorContains, "Nursing", 5000, 2000, 90, 2);
        AddTask("Future Lawyer", "Someone argumentative.", TaskRequirementType.HireMajorContains, "Law", 5500, 2200, 90, 3);
        AddTask("Architect of Destiny", "Someone who can draw buildings.", TaskRequirementType.HireMajorContains, "Architecture", 5500, 2200, 90, 3);
        AddTask("Chemistry Set", "Someone who knows elements.", TaskRequirementType.HireMajorContains, "Chemistry", 5000, 2000, 90, 2);
        AddTask("Bio Major", "Life sciences enthusiast.", TaskRequirementType.HireMajorContains, "Biology", 5000, 2000, 90, 2);
        AddTask("Economist Mode", "Someone who can explain why we're poor.", TaskRequirementType.HireMajorContains, "Economics", 5000, 2000, 90, 2);
        AddTask("Crypto Bro", "Someone who understands blockchain. Maybe.", TaskRequirementType.HireMajorContains, "Crypto", 6000, 2500, 120, 4);
        AddTask("AI Evangelist", "Someone who won't shut up about AI.", TaskRequirementType.HireMajorContains, "Artificial", 5500, 2200, 90, 3);
        AddTask("Data Wizard", "Someone who can Excel. At Excel.", TaskRequirementType.HireMajorContains, "Data", 5000, 2000, 90, 2);
        AddTask("Security Expert", "Someone paranoid.", TaskRequirementType.HireMajorContains, "Security", 5500, 2200, 90, 3);
        AddTask("Robot Wrangler", "Someone who works with machines.", TaskRequirementType.HireMajorContains, "Robotic", 6000, 2500, 120, 3);
        
        // ============ FINAL DARK HUMOR (15) ============
        AddTask("Organ Donor", "For the company health plan.", TaskRequirementType.HireAgeMax, "25", 5000, 2000, 90, 2);
        AddTask("Burnout Material", "Someone with enough energy to burn.", TaskRequirementType.HireExperienceMax, "2", 4500, 1800, 90, 2);
        AddTask("Disposable Workforce", "Easy to replace.", TaskRequirementType.HireIQMax, "92", 5000, 2000, 90, 3);
        AddTask("Future Layoff", "Hire now, fire later.", TaskRequirementType.HireAny, "", 3000, 1000, 60, 1);
        AddTask("Compliant Employee", "Someone who won't ask questions.", TaskRequirementType.HireIQMax, "88", 5500, 2200, 90, 3);
        AddTask("Stress Test Subject", "How much can one person take?", TaskRequirementType.HireExperienceMax, "1", 4500, 1800, 90, 2);
        AddTask("Overwork Candidate", "Someone without hobbies.", TaskRequirementType.HireAgeMax, "26", 4500, 1800, 90, 2);
        AddTask("Golden Parachute Anchor", "Someone to weigh down the executives.", TaskRequirementType.HireSalaryMax, "45000", 5000, 2000, 90, 2);
        AddTask("Blame Magnet", "HR loves having options.", TaskRequirementType.HireWithCriminalRecord, "", 5500, 2200, 120, 3);
        AddTask("Quiet Quitter", "They're here but are they really?", TaskRequirementType.HireExperienceMin, "8", 5000, 2000, 90, 2);
        AddTask("Legacy Diversity", "Old school affirmative action.", TaskRequirementType.HireRace, "Black", 5000, 2000, 90, 2);
        AddTask("PR Nightmare Prevention", "Hire before Twitter notices.", TaskRequirementType.HireRace, "Asian", 5000, 2000, 90, 2);
        AddTaskRange("The Useful Idiot", "Smart enough to work, dumb enough to not realize.", TaskRequirementType.HireIQRange, "88", "98", 5500, 2200, 90, 3);
        AddTask("Corporate Hostage", "Student loans mean they can't leave.", TaskRequirementType.HireDegree, "Master", 5500, 2200, 90, 3);
        AddTask("The Eternal Intern", "Intern energy, employee title.", TaskRequirementType.HireAgeMax, "23", 4500, 1800, 90, 2);
        
        Debug.Log($"[HiringTaskDatabase] Initialized with {tasks.Count} tasks");
    }
    
    void AddTask(string title, string desc, TaskRequirementType type, string target, 
                 int reward, int penalty, float time, int difficulty)
    {
        tasks.Add(new HiringTask(title, desc, type, target, reward, penalty, time, difficulty));
    }
    
    void AddTaskRange(string title, string desc, TaskRequirementType type, string min, string max,
                      int reward, int penalty, float time, int difficulty)
    {
        tasks.Add(new HiringTask(title, desc, type, min, max, reward, penalty, time, difficulty));
    }
}
