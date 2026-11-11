using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// All risk-card events, defined in code.
/// The lists are [NonSerialized] on purpose: the scene used to store an old 7-per-list copy that
/// replaced these definitions at load time. Edit the events here, not in the Inspector.
/// Every card gets its translation key as its list is built (Keyed): the titles and stories in other languages are in the
/// Events String Table (Assets/Localization/Source/Events.csv, merged from these lists by Tools > Game > Localization >
/// Sync Content CSVs); RandomEvent.DisplayName / DisplayDescription read it.
/// </summary>
public class EventDatabase : MonoBehaviour
{
    // Excellent good events (for High Risk card) - 100 events
    [System.NonSerialized]
    public List<RandomEvent> excellentGoodEvents = ExcellentGoodDefaults();

    static List<RandomEvent> ExcellentGoodDefaults() => Keyed(ExcellentGoodList, new List<RandomEvent>()
    {
        new RandomEvent("Viral Product!", "Your product went viral on social media! Massive sales incoming!", true, 15000f, 0.25f),
        new RandomEvent("Government Contract", "You landed a huge government contract!", true, 25000f, 0.20f),
        new RandomEvent("Tech Breakthrough", "Your R&D team made a revolutionary discovery!", true, 20000f, 0.30f),
        new RandomEvent("Celebrity Endorsement", "A famous celebrity endorsed your company for free!", true, 18000f, 0.15f),
        new RandomEvent("Acquisition Offer", "A big company wants to invest heavily!", true, 30000f, 0.20f),
        new RandomEvent("Patent Approved", "Your patent was approved - licensing money flows in!", true, 22000f, 0.18f),
        new RandomEvent("Market Monopoly", "Competitors went bankrupt - you dominate the market!", true, 28000f, 0.35f),
        new RandomEvent("Billionaire Investor", "A billionaire decided to invest in your company!", true, 35000f, 0.25f),
        new RandomEvent("International Expansion", "Your product launched successfully in 20 new countries!", true, 27000f, 0.22f),
        new RandomEvent("Lottery Win", "The company lottery pool won big!", true, 40000f, 0.15f),
        new RandomEvent("Oil Discovery", "Oil was discovered under company property!", true, 50000f, 0.30f),
        new RandomEvent("Merger Success", "A strategic merger doubled your market share!", true, 32000f, 0.28f),
        new RandomEvent("AI Revolution", "Your AI product is now industry standard!", true, 28000f, 0.32f),
        new RandomEvent("Crypto Jackpot", "Company crypto investments skyrocketed!", true, 38000f, 0.20f),
        new RandomEvent("Movie Deal", "Hollywood wants to make a movie about your company!", true, 25000f, 0.18f),
        new RandomEvent("Nobel Connection", "A Nobel Prize winner joined your board!", true, 20000f, 0.25f),
        new RandomEvent("Space Contract", "NASA selected your company for a space mission!", true, 45000f, 0.22f),
        new RandomEvent("Cure Discovery", "Your research team discovered a medical breakthrough!", true, 55000f, 0.35f),
        new RandomEvent("Royal Endorsement", "A royal family endorsed your brand!", true, 30000f, 0.20f),
        new RandomEvent("Super Bowl Ad", "Your Super Bowl ad went legendary viral!", true, 35000f, 0.28f),
        new RandomEvent("Stock Split", "Stock split made everyone rich!", true, 25000f, 0.30f),
        new RandomEvent("Unicorn Status", "Your company achieved unicorn valuation!", true, 40000f, 0.25f),
        new RandomEvent("Defense Contract", "Military signed a massive defense contract!", true, 48000f, 0.22f),
        new RandomEvent("Streaming Rights", "Netflix bought streaming rights to your content!", true, 33000f, 0.18f),
        new RandomEvent("Franchise Boom", "Franchise locations are exploding globally!", true, 29000f, 0.26f),
        new RandomEvent("Green Energy Grant", "Government awarded huge green energy grant!", true, 36000f, 0.20f),
        new RandomEvent("Influencer Army", "1000 influencers promoting you for free!", true, 22000f, 0.24f),
        new RandomEvent("Treasure Found", "Construction workers found treasure on your land!", true, 42000f, 0.15f),
        new RandomEvent("Lawsuit Won", "You won a massive lawsuit - huge settlement!", true, 38000f, 0.22f),
        new RandomEvent("Exclusive Deal", "Walmart wants exclusive rights to your product!", true, 31000f, 0.25f),
        new RandomEvent("TikTok Sensation", "Your product became a TikTok sensation overnight!", true, 26000f, 0.28f),
        new RandomEvent("Amazon Partnership", "Amazon featured you as their top partner!", true, 34000f, 0.20f),
        new RandomEvent("Elon's Tweet", "Elon Musk tweeted about your company positively!", true, 28000f, 0.30f),
        new RandomEvent("IPO Success", "Your IPO exceeded all expectations!", true, 45000f, 0.25f),
        new RandomEvent("Inheritance", "Company received unexpected inheritance!", true, 50000f, 0.10f),
        new RandomEvent("Diamond Mine", "Diamonds found during office renovation!", true, 55000f, 0.20f),
        new RandomEvent("Quantum Leap", "Quantum computing breakthrough changes everything!", true, 40000f, 0.32f),
        new RandomEvent("Metaverse Land", "Your metaverse property value exploded!", true, 30000f, 0.25f),
        new RandomEvent("Sports Sponsorship", "Major sports team wants you as main sponsor!", true, 27000f, 0.18f),
        new RandomEvent("Genius Hire", "You hired a genius who revolutionized operations!", true, 25000f, 0.28f),
        new RandomEvent("Secret Formula", "Discovered a secret formula worth millions!", true, 35000f, 0.22f),
        new RandomEvent("Viral Meme", "Your company became a positive viral meme!", true, 20000f, 0.25f),
        new RandomEvent("Time Magazine", "TIME named you Company of the Year!", true, 32000f, 0.20f),
        new RandomEvent("Oprah Effect", "Oprah featured your product on her show!", true, 38000f, 0.24f),
        new RandomEvent("Olympic Sponsor", "Selected as official Olympic sponsor!", true, 42000f, 0.22f),
        new RandomEvent("Hedge Fund Love", "Hedge funds are buying your stock aggressively!", true, 30000f, 0.28f),
        new RandomEvent("Patent Troll Defeated", "Defeated patent trolls - they pay YOU!", true, 25000f, 0.15f),
        new RandomEvent("Cloud Contract", "Signed massive cloud computing contract!", true, 33000f, 0.20f),
        new RandomEvent("Real Estate Boom", "Company real estate tripled in value!", true, 40000f, 0.25f),
        new RandomEvent("Subscription Surge", "Subscription numbers exploded 500%!", true, 28000f, 0.30f),
        new RandomEvent("Gaming Hit", "Your mobile game topped the charts!", true, 35000f, 0.22f),
        new RandomEvent("Podcast Fame", "Company podcast became #1 worldwide!", true, 22000f, 0.18f),
        new RandomEvent("Food Trend", "Your product became the hottest food trend!", true, 26000f, 0.24f),
        new RandomEvent("Fashion Icon", "Fashion industry adopted your brand!", true, 30000f, 0.20f),
        new RandomEvent("Music Collab", "Famous artist wants to collaborate!", true, 24000f, 0.22f),
        new RandomEvent("Charity Boost", "Charity work generated massive goodwill!", true, 20000f, 0.25f),
        new RandomEvent("Automation Win", "Full automation cut costs by 70%!", true, 35000f, 0.28f),
        new RandomEvent("Supply Chain Gold", "Secured exclusive supply chain deal!", true, 28000f, 0.20f),
        new RandomEvent("University Partnership", "MIT wants to partner with you!", true, 25000f, 0.22f),
        new RandomEvent("Recall Competitor", "Competitor's recall sent customers to you!", true, 32000f, 0.25f),
        new RandomEvent("Weather Luck", "Perfect weather boosted seasonal sales 300%!", true, 22000f, 0.18f),
        new RandomEvent("Currency Win", "Currency fluctuations worked in your favor!", true, 30000f, 0.20f),
        new RandomEvent("Shipping Deal", "Negotiated incredible shipping rates!", true, 18000f, 0.22f),
        new RandomEvent("Warehouse Find", "Found valuable inventory in old warehouse!", true, 25000f, 0.15f),
        new RandomEvent("Tax Loophole", "Accountants found a legal tax loophole!", true, 35000f, 0.18f),
        new RandomEvent("Rebrand Success", "Company rebrand was massively successful!", true, 28000f, 0.25f),
        new RandomEvent("Algorithm Favor", "Social media algorithms favor your content!", true, 24000f, 0.22f),
        new RandomEvent("B2B Bonanza", "Landed 50 new B2B contracts at once!", true, 40000f, 0.20f),
        new RandomEvent("Loyalty Program", "Loyalty program exceeded all projections!", true, 22000f, 0.24f),
        new RandomEvent("Trade Show Win", "Won best product at major trade show!", true, 26000f, 0.18f),
        new RandomEvent("Crowdfunding Record", "Crowdfunding campaign broke records!", true, 45000f, 0.22f),
        new RandomEvent("Angel Investor", "Angel investor doubled their investment!", true, 38000f, 0.25f),
        new RandomEvent("Regulatory Win", "New regulations favor your business model!", true, 30000f, 0.28f),
        new RandomEvent("Competitor Buyout", "Bought out a competitor for cheap!", true, 35000f, 0.20f),
        new RandomEvent("Data Gold", "Your data became incredibly valuable!", true, 32000f, 0.25f),
        new RandomEvent("Subscription Box Hit", "Subscription box became cult favorite!", true, 28000f, 0.22f),
        new RandomEvent("White House Visit", "Invited to the White House!", true, 25000f, 0.20f),
        new RandomEvent("UN Recognition", "UN recognized your sustainability efforts!", true, 30000f, 0.18f),
        new RandomEvent("Bestseller", "Your book became a bestseller!", true, 22000f, 0.24f),
        new RandomEvent("Documentary", "Documentary about your success went viral!", true, 26000f, 0.22f),
        new RandomEvent("Theme Park Deal", "Disney wants to feature your brand!", true, 40000f, 0.25f),
        new RandomEvent("Airline Partnership", "Major airline partnership announced!", true, 35000f, 0.20f),
        new RandomEvent("Hotel Chain Deal", "Marriott wants exclusive partnership!", true, 32000f, 0.22f),
        new RandomEvent("Esports Victory", "Your esports team won world championship!", true, 28000f, 0.25f),
        new RandomEvent("NFT Jackpot", "Company NFTs sold for millions!", true, 45000f, 0.20f),
        new RandomEvent("Robotics Breakthrough", "Robotics division made huge breakthrough!", true, 38000f, 0.28f),
        new RandomEvent("Solar Success", "Solar initiative saves millions annually!", true, 30000f, 0.25f),
        new RandomEvent("Battery Innovation", "Battery technology patent worth billions!", true, 50000f, 0.30f),
        new RandomEvent("5G Pioneer", "First to market with 5G product!", true, 35000f, 0.25f),
        new RandomEvent("Drone Delivery", "Drone delivery system revolutionizes logistics!", true, 40000f, 0.22f),
        new RandomEvent("Biotech Boom", "Biotech division discovered miracle compound!", true, 55000f, 0.32f),
        new RandomEvent("VR Revolution", "VR product became industry standard!", true, 38000f, 0.28f),
        new RandomEvent("Smart Home King", "Dominated the smart home market!", true, 33000f, 0.25f),
        new RandomEvent("Wearable Wonder", "Wearable tech product went platinum!", true, 30000f, 0.22f),
        new RandomEvent("Food Tech Win", "Food tech innovation won global award!", true, 28000f, 0.20f),
        new RandomEvent("Clean Water Hero", "Clean water initiative gained global praise!", true, 25000f, 0.25f),
        new RandomEvent("Education Deal", "Schools nationwide adopted your product!", true, 35000f, 0.22f),
        new RandomEvent("Healthcare Contract", "Major healthcare system signed contract!", true, 42000f, 0.25f),
    });

    // Terrible bad events (for High Risk card) - 100 events
    [System.NonSerialized]
    public List<RandomEvent> terribleBadEvents = TerribleBadDefaults();

    static List<RandomEvent> TerribleBadDefaults() => Keyed(TerribleBadList, new List<RandomEvent>()
    {
        new RandomEvent("Major Lawsuit", "You're being sued for millions!", false, -20000f, -0.25f),
        new RandomEvent("Data Breach", "Hackers stole customer data - massive fines incoming!", false, -25000f, -0.20f),
        new RandomEvent("Product Recall", "Defective products must be recalled!", false, -18000f, -0.30f),
        new RandomEvent("Executive Scandal", "Your CEO was caught in a scandal!", false, -15000f, -0.25f),
        new RandomEvent("Factory Fire", "Your main facility burned down!", false, -30000f, -0.20f),
        new RandomEvent("Market Crash", "The market crashed - investors are fleeing!", false, -22000f, -0.35f),
        new RandomEvent("Fraud Investigation", "Government is investigating your company!", false, -28000f, -0.15f),
        new RandomEvent("Massive Hack", "Ransomware locked all company systems!", false, -35000f, -0.25f),
        new RandomEvent("Bankruptcy Threat", "Major client went bankrupt - owes you millions!", false, -40000f, -0.20f),
        new RandomEvent("Toxic Spill", "Environmental disaster at your facility!", false, -45000f, -0.30f),
        new RandomEvent("Class Action", "Class action lawsuit filed against you!", false, -38000f, -0.25f),
        new RandomEvent("Stock Crash", "Stock price crashed 50% overnight!", false, -30000f, -0.35f),
        new RandomEvent("Embargo Hit", "Trade embargo destroyed international sales!", false, -32000f, -0.28f),
        new RandomEvent("CEO Arrested", "CEO arrested for financial crimes!", false, -40000f, -0.30f),
        new RandomEvent("Factory Explosion", "Factory explosion injured workers!", false, -50000f, -0.25f),
        new RandomEvent("Pandemic Impact", "New pandemic variant shut down operations!", false, -35000f, -0.32f),
        new RandomEvent("Flood Damage", "Flooding destroyed warehouse inventory!", false, -28000f, -0.22f),
        new RandomEvent("Hurricane Hit", "Hurricane destroyed multiple facilities!", false, -45000f, -0.28f),
        new RandomEvent("Earthquake", "Earthquake damaged headquarters!", false, -38000f, -0.25f),
        new RandomEvent("Tornado Strike", "Tornado wiped out distribution center!", false, -33000f, -0.22f),
        new RandomEvent("Wildfire Loss", "Wildfires destroyed company property!", false, -40000f, -0.25f),
        new RandomEvent("Terrorist Attack", "Terrorist attack targeted your building!", false, -50000f, -0.30f),
        new RandomEvent("Insider Trading", "Executives caught insider trading!", false, -35000f, -0.28f),
        new RandomEvent("Ponzi Scheme", "CFO ran a Ponzi scheme with company funds!", false, -55000f, -0.35f),
        new RandomEvent("Embezzlement", "Massive embezzlement discovered!", false, -42000f, -0.25f),
        new RandomEvent("Union Strike", "Company-wide strike halted all production!", false, -30000f, -0.22f),
        new RandomEvent("FDA Shutdown", "FDA shut down your main product line!", false, -38000f, -0.30f),
        new RandomEvent("EPA Violation", "EPA fined you for major violations!", false, -35000f, -0.25f),
        new RandomEvent("SEC Investigation", "SEC launched formal investigation!", false, -28000f, -0.28f),
        new RandomEvent("IRS Audit", "IRS audit revealed massive tax issues!", false, -45000f, -0.22f),
        new RandomEvent("Patent Lost", "Lost major patent lawsuit!", false, -32000f, -0.25f),
        new RandomEvent("Trade Secret Stolen", "Competitor stole trade secrets!", false, -40000f, -0.28f),
        new RandomEvent("Whistleblower", "Whistleblower exposed company wrongdoing!", false, -35000f, -0.25f),
        new RandomEvent("Media Exposé", "60 Minutes did an exposé on your company!", false, -30000f, -0.30f),
        new RandomEvent("Social Media Storm", "Social media cancel campaign went viral!", false, -25000f, -0.28f),
        new RandomEvent("Boycott", "Major boycott movement against your brand!", false, -28000f, -0.25f),
        new RandomEvent("Celebrity Callout", "Celebrity publicly called out your company!", false, -22000f, -0.22f),
        new RandomEvent("Documentary Attack", "Damning documentary released about you!", false, -30000f, -0.25f),
        new RandomEvent("Bankruptcy Filing", "Major supplier filed for bankruptcy!", false, -35000f, -0.20f),
        new RandomEvent("Currency Collapse", "Currency collapse in key market!", false, -32000f, -0.28f),
        new RandomEvent("War Impact", "War disrupted critical supply chains!", false, -40000f, -0.25f),
        new RandomEvent("Sanction Hit", "New sanctions blocked major deals!", false, -38000f, -0.22f),
        new RandomEvent("Tariff Shock", "Unexpected tariffs crushed margins!", false, -28000f, -0.25f),
        new RandomEvent("Interest Rate Spike", "Interest rate spike increased debt costs!", false, -25000f, -0.22f),
        new RandomEvent("Inflation Crisis", "Hyperinflation destroyed profit margins!", false, -30000f, -0.28f),
        new RandomEvent("Recession Hit", "Recession caused demand to plummet!", false, -35000f, -0.30f),
        new RandomEvent("Bank Failure", "Your bank failed - funds frozen!", false, -45000f, -0.25f),
        new RandomEvent("Crypto Crash", "Company crypto holdings became worthless!", false, -40000f, -0.22f),
        new RandomEvent("NFT Worthless", "NFT investments became worthless!", false, -30000f, -0.20f),
        new RandomEvent("Metaverse Bust", "Metaverse investments completely failed!", false, -35000f, -0.25f),
        new RandomEvent("AI Disaster", "AI system made catastrophic decisions!", false, -38000f, -0.28f),
        new RandomEvent("Algorithm Fail", "Algorithm failure caused massive losses!", false, -32000f, -0.25f),
        new RandomEvent("Server Meltdown", "Server farm completely melted down!", false, -40000f, -0.22f),
        new RandomEvent("Cloud Outage", "Cloud provider outage lasted weeks!", false, -28000f, -0.25f),
        new RandomEvent("Satellite Lost", "Company satellite destroyed in space!", false, -50000f, -0.20f),
        new RandomEvent("Ship Sunk", "Cargo ship sank with your products!", false, -35000f, -0.25f),
        new RandomEvent("Plane Crash", "Company plane crashed - executives lost!", false, -45000f, -0.30f),
        new RandomEvent("Train Derailment", "Train carrying inventory derailed!", false, -30000f, -0.22f),
        new RandomEvent("Truck Heist", "Truck convoy hijacked!", false, -25000f, -0.20f),
        new RandomEvent("Warehouse Robbery", "Armed robbery at main warehouse!", false, -28000f, -0.18f),
        new RandomEvent("Counterfeits", "Market flooded with counterfeits!", false, -32000f, -0.25f),
        new RandomEvent("Quality Crisis", "Major quality control failure!", false, -35000f, -0.28f),
        new RandomEvent("Safety Recall", "Safety issues forced massive recall!", false, -40000f, -0.25f),
        new RandomEvent("Death Lawsuit", "Wrongful death lawsuit filed!", false, -50000f, -0.30f),
        new RandomEvent("Injury Claims", "Multiple injury claims filed!", false, -38000f, -0.25f),
        new RandomEvent("Discrimination Suit", "Major discrimination lawsuit!", false, -35000f, -0.28f),
        new RandomEvent("Harassment Scandal", "Harassment scandal went public!", false, -30000f, -0.30f),
        new RandomEvent("Wage Theft", "Accused of systematic wage theft!", false, -32000f, -0.25f),
        new RandomEvent("Child Labor", "Child labor discovered in supply chain!", false, -45000f, -0.32f),
        new RandomEvent("Slavery Scandal", "Modern slavery found in suppliers!", false, -50000f, -0.30f),
        new RandomEvent("Animal Cruelty", "Animal cruelty exposed in testing!", false, -35000f, -0.28f),
        new RandomEvent("Deforestation", "Linked to illegal deforestation!", false, -30000f, -0.25f),
        new RandomEvent("Ocean Dumping", "Caught dumping waste in ocean!", false, -40000f, -0.28f),
        new RandomEvent("Air Pollution", "Massive air pollution violation!", false, -35000f, -0.25f),
        new RandomEvent("Water Contamination", "Contaminated local water supply!", false, -45000f, -0.30f),
        new RandomEvent("Nuclear Incident", "Radioactive material leak!", false, -55000f, -0.35f),
        new RandomEvent("Chemical Spill", "Chemical spill evacuated neighborhood!", false, -42000f, -0.28f),
        new RandomEvent("Gas Leak", "Gas leak caused evacuations!", false, -30000f, -0.22f),
        new RandomEvent("Building Collapse", "Office building partially collapsed!", false, -48000f, -0.30f),
        new RandomEvent("Bridge Failure", "Private bridge collapsed!", false, -35000f, -0.25f),
        new RandomEvent("Dam Break", "Company dam broke - flooding!", false, -50000f, -0.32f),
        new RandomEvent("Mine Collapse", "Mine collapse trapped workers!", false, -45000f, -0.28f),
        new RandomEvent("Oil Rig Disaster", "Oil rig explosion and spill!", false, -55000f, -0.35f),
        new RandomEvent("Pipeline Burst", "Pipeline burst caused disaster!", false, -40000f, -0.28f),
        new RandomEvent("Refinery Fire", "Refinery fire burned for days!", false, -45000f, -0.30f),
        new RandomEvent("Power Plant Fail", "Power plant failure caused blackout!", false, -38000f, -0.25f),
        new RandomEvent("Grid Collapse", "Caused regional grid collapse!", false, -42000f, -0.28f),
        new RandomEvent("Telecom Outage", "Telecom failure affected millions!", false, -35000f, -0.25f),
        new RandomEvent("GPS Failure", "GPS system failure disrupted logistics!", false, -28000f, -0.22f),
        new RandomEvent("Satellite Hack", "Satellites hacked by foreign actors!", false, -40000f, -0.28f),
        new RandomEvent("Drone Incident", "Company drone caused injuries!", false, -32000f, -0.25f),
        new RandomEvent("Robot Malfunction", "Factory robot injured workers!", false, -38000f, -0.28f),
        new RandomEvent("Self-Driving Crash", "Self-driving vehicle caused fatality!", false, -50000f, -0.32f),
        new RandomEvent("Medical Device Fail", "Medical device caused deaths!", false, -55000f, -0.35f),
        new RandomEvent("Drug Recall", "Pharmaceutical recall - deaths reported!", false, -60000f, -0.35f),
        new RandomEvent("Vaccine Scandal", "Vaccine batch contaminated!", false, -50000f, -0.30f),
        new RandomEvent("Food Poisoning", "Mass food poisoning outbreak!", false, -45000f, -0.28f),
        new RandomEvent("E.Coli Outbreak", "E.coli traced to your products!", false, -40000f, -0.30f),
        new RandomEvent("Listeria Crisis", "Listeria outbreak from your facility!", false, -42000f, -0.28f),
        new RandomEvent("Salmonella Scare", "Salmonella contamination discovered!", false, -38000f, -0.25f),
    });

    // Average good events (for Balanced card) - 100 events
    [System.NonSerialized]
    public List<RandomEvent> averageGoodEvents = AverageGoodDefaults();

    static List<RandomEvent> AverageGoodDefaults() => Keyed(AverageGoodList, new List<RandomEvent>()
    {
        new RandomEvent("Good Quarter", "Sales exceeded expectations this quarter!", true, 8000f, 0.10f),
        new RandomEvent("New Client", "You signed a valuable new client!", true, 6000f, 0.08f),
        new RandomEvent("Efficiency Boost", "New processes improved productivity!", true, 5000f, 0.12f),
        new RandomEvent("Positive Review", "A major publication gave you a great review!", true, 4000f, 0.10f),
        new RandomEvent("Tax Refund", "You received an unexpected tax refund!", true, 7000f, 0.05f),
        new RandomEvent("Partnership", "You formed a beneficial partnership!", true, 6500f, 0.08f),
        new RandomEvent("Award Won", "Your company won an industry award!", true, 5500f, 0.10f),
        new RandomEvent("Team Bonus", "Team exceeded all targets - morale up!", true, 4500f, 0.08f),
        new RandomEvent("New Market", "Successfully entered a new market!", true, 7500f, 0.10f),
        new RandomEvent("Cost Reduction", "Found significant cost reductions!", true, 6000f, 0.09f),
        new RandomEvent("Patent Filed", "New patent application filed!", true, 5000f, 0.08f),
        new RandomEvent("Investor Interest", "New investors showing interest!", true, 8000f, 0.07f),
        new RandomEvent("Media Coverage", "Positive media coverage boosted brand!", true, 4000f, 0.10f),
        new RandomEvent("Customer Loyalty", "Customer retention rates improved!", true, 5500f, 0.08f),
        new RandomEvent("Process Improvement", "Streamlined operations saved money!", true, 6500f, 0.07f),
        new RandomEvent("Training Success", "Employee training program paid off!", true, 4500f, 0.09f),
        new RandomEvent("Quality Award", "Won quality excellence award!", true, 5000f, 0.08f),
        new RandomEvent("Green Initiative", "Green initiative reduced costs!", true, 6000f, 0.07f),
        new RandomEvent("Safety Record", "Perfect safety record bonus!", true, 4000f, 0.06f),
        new RandomEvent("Supplier Deal", "Negotiated better supplier terms!", true, 7000f, 0.08f),
        new RandomEvent("Lease Renewal", "Favorable lease renewal terms!", true, 5500f, 0.06f),
        new RandomEvent("Insurance Discount", "Insurance premiums reduced!", true, 4500f, 0.05f),
        new RandomEvent("Utility Savings", "Energy efficiency reduced bills!", true, 3500f, 0.07f),
        new RandomEvent("Recycling Revenue", "Recycling program generating income!", true, 3000f, 0.06f),
        new RandomEvent("Referral Program", "Referral program brought new business!", true, 5000f, 0.08f),
        new RandomEvent("Website Traffic", "Website traffic increased 50%!", true, 4500f, 0.09f),
        new RandomEvent("App Downloads", "App downloads exceeded expectations!", true, 5500f, 0.08f),
        new RandomEvent("Email Campaign", "Email campaign had record open rates!", true, 4000f, 0.07f),
        new RandomEvent("SEO Success", "SEO improvements boosted visibility!", true, 5000f, 0.08f),
        new RandomEvent("Social Growth", "Social media following grew 30%!", true, 4500f, 0.07f),
        new RandomEvent("Podcast Feature", "Featured on popular podcast!", true, 5500f, 0.08f),
        new RandomEvent("Blog Viral", "Company blog post went viral!", true, 4000f, 0.09f),
        new RandomEvent("Newsletter Growth", "Newsletter subscribers doubled!", true, 3500f, 0.07f),
        new RandomEvent("Webinar Success", "Webinar generated quality leads!", true, 5000f, 0.08f),
        new RandomEvent("Conference Win", "Great success at industry conference!", true, 6000f, 0.09f),
        new RandomEvent("Networking Event", "Networking event led to deals!", true, 5500f, 0.08f),
        new RandomEvent("Trade Show Lead", "Trade show generated hot leads!", true, 6500f, 0.07f),
        new RandomEvent("Speaking Gig", "CEO's speech impressed investors!", true, 5000f, 0.10f),
        new RandomEvent("Panel Success", "Panel discussion raised profile!", true, 4500f, 0.08f),
        new RandomEvent("Interview Boost", "CEO interview went great!", true, 4000f, 0.09f),
        new RandomEvent("Magazine Feature", "Featured in industry magazine!", true, 5500f, 0.08f),
        new RandomEvent("TV Appearance", "Product featured on morning show!", true, 7000f, 0.10f),
        new RandomEvent("Radio Mention", "Positive radio mention!", true, 3500f, 0.06f),
        new RandomEvent("Newspaper Article", "Favorable newspaper article!", true, 4000f, 0.07f),
        new RandomEvent("Online Review", "Flood of 5-star reviews!", true, 5000f, 0.09f),
        new RandomEvent("Testimonial Video", "Customer testimonial went viral!", true, 4500f, 0.08f),
        new RandomEvent("Case Study", "Case study attracted new clients!", true, 5500f, 0.07f),
        new RandomEvent("White Paper", "White paper established expertise!", true, 4000f, 0.08f),
        new RandomEvent("Research Published", "Research paper got attention!", true, 5000f, 0.09f),
        new RandomEvent("Innovation Award", "Won innovation award!", true, 6000f, 0.10f),
        new RandomEvent("Startup Award", "Named top startup to watch!", true, 5500f, 0.09f),
        new RandomEvent("Best Employer", "Named best employer in region!", true, 4500f, 0.08f),
        new RandomEvent("Diversity Award", "Won diversity excellence award!", true, 5000f, 0.07f),
        new RandomEvent("Sustainability Prize", "Won sustainability prize!", true, 5500f, 0.08f),
        new RandomEvent("Community Award", "Community service award!", true, 4000f, 0.06f),
        new RandomEvent("Charity Match", "Charity matching boosted image!", true, 3500f, 0.07f),
        new RandomEvent("Volunteer Day", "Volunteer day improved morale!", true, 3000f, 0.08f),
        new RandomEvent("Scholarship Fund", "Scholarship program gained praise!", true, 4000f, 0.06f),
        new RandomEvent("Mentorship Program", "Mentorship program success!", true, 3500f, 0.07f),
        new RandomEvent("Internship Win", "Intern program attracted talent!", true, 4500f, 0.08f),
        new RandomEvent("Campus Recruiting", "Campus recruiting exceeded goals!", true, 5000f, 0.07f),
        new RandomEvent("Job Fair Success", "Job fair brought great candidates!", true, 4000f, 0.06f),
        new RandomEvent("LinkedIn Growth", "LinkedIn presence grew significantly!", true, 3500f, 0.08f),
        new RandomEvent("Glassdoor Rating", "Glassdoor rating improved!", true, 4000f, 0.07f),
        new RandomEvent("Employee Survey", "Employee satisfaction scores up!", true, 4500f, 0.08f),
        new RandomEvent("Retention Bonus", "Key employee retention success!", true, 5000f, 0.06f),
        new RandomEvent("Promotion Round", "Internal promotions boosted morale!", true, 3500f, 0.09f),
        new RandomEvent("Benefits Upgrade", "New benefits package well-received!", true, 4000f, 0.07f),
        new RandomEvent("Office Upgrade", "Office renovation improved productivity!", true, 5500f, 0.08f),
        new RandomEvent("Remote Success", "Remote work policy working great!", true, 4500f, 0.07f),
        new RandomEvent("Hybrid Win", "Hybrid model exceeding expectations!", true, 5000f, 0.08f),
        new RandomEvent("Tool Investment", "New tools improved efficiency!", true, 6000f, 0.09f),
        new RandomEvent("Software Upgrade", "Software upgrade streamlined work!", true, 5500f, 0.08f),
        new RandomEvent("Hardware Refresh", "Hardware refresh boosted speed!", true, 5000f, 0.07f),
        new RandomEvent("Cloud Migration", "Cloud migration saved money!", true, 6500f, 0.09f),
        new RandomEvent("Security Audit", "Passed security audit with flying colors!", true, 4000f, 0.06f),
        new RandomEvent("Compliance Win", "Compliance certification achieved!", true, 5000f, 0.07f),
        new RandomEvent("ISO Certified", "ISO certification obtained!", true, 5500f, 0.08f),
        new RandomEvent("SOC 2 Passed", "SOC 2 compliance achieved!", true, 6000f, 0.07f),
        new RandomEvent("GDPR Ready", "GDPR compliance ahead of schedule!", true, 4500f, 0.06f),
        new RandomEvent("Audit Clean", "Clean financial audit!", true, 5000f, 0.08f),
        new RandomEvent("Credit Upgrade", "Credit rating upgraded!", true, 7000f, 0.09f),
        new RandomEvent("Loan Approved", "Favorable loan terms approved!", true, 6500f, 0.07f),
        new RandomEvent("Grant Received", "Research grant received!", true, 8000f, 0.08f),
        new RandomEvent("Subsidy Won", "Government subsidy approved!", true, 7500f, 0.07f),
        new RandomEvent("Tax Credit", "Tax credit qualification!", true, 6000f, 0.06f),
        new RandomEvent("Incentive Package", "Economic incentive package won!", true, 7000f, 0.08f),
        new RandomEvent("Zoning Approved", "Zoning approval for expansion!", true, 5500f, 0.09f),
        new RandomEvent("Permit Fast-Track", "Permits fast-tracked!", true, 4500f, 0.07f),
        new RandomEvent("License Renewed", "Key license renewed easily!", true, 4000f, 0.06f),
        new RandomEvent("Contract Extended", "Major contract extended!", true, 7000f, 0.10f),
        new RandomEvent("Renewal Rate", "High contract renewal rate!", true, 6000f, 0.08f),
        new RandomEvent("Upsell Success", "Upselling campaign succeeded!", true, 5500f, 0.09f),
        new RandomEvent("Cross-Sell Win", "Cross-selling increased revenue!", true, 5000f, 0.08f),
        new RandomEvent("Bundle Deal", "Bundle deals popular with customers!", true, 6000f, 0.07f),
        new RandomEvent("Premium Tier", "Premium tier adoption increased!", true, 6500f, 0.09f),
        new RandomEvent("Enterprise Deal", "Landed enterprise customer!", true, 8000f, 0.10f),
        new RandomEvent("SMB Growth", "SMB segment growing fast!", true, 5500f, 0.08f),
        new RandomEvent("Consumer Win", "Consumer product sales up!", true, 5000f, 0.09f),
        new RandomEvent("B2B Success", "B2B sales exceeded targets!", true, 7000f, 0.08f),
    });

    // Average bad events (for Balanced card) - 100 events
    [System.NonSerialized]
    public List<RandomEvent> averageBadEvents = AverageBadDefaults();

    static List<RandomEvent> AverageBadDefaults() => Keyed(AverageBadList, new List<RandomEvent>()
    {
        new RandomEvent("Equipment Failure", "Important equipment broke down!", false, -6000f, -0.08f),
        new RandomEvent("Employee Quit", "A key employee left for a competitor!", false, -5000f, -0.10f),
        new RandomEvent("Bad Review", "A negative review went viral!", false, -4000f, -0.12f),
        new RandomEvent("Supply Issues", "Supply chain problems increased costs!", false, -7000f, -0.08f),
        new RandomEvent("Minor Lawsuit", "A small lawsuit needs to be settled!", false, -8000f, -0.05f),
        new RandomEvent("Rent Increase", "Your office rent increased significantly!", false, -5500f, -0.06f),
        new RandomEvent("Lost Client", "An important client switched to a competitor!", false, -6500f, -0.10f),
        new RandomEvent("Project Delay", "Major project delayed by months!", false, -5000f, -0.08f),
        new RandomEvent("Budget Overrun", "Project went over budget!", false, -6000f, -0.07f),
        new RandomEvent("Deadline Missed", "Critical deadline missed!", false, -4500f, -0.09f),
        new RandomEvent("Quality Issue", "Quality issues with recent batch!", false, -5500f, -0.08f),
        new RandomEvent("Return Spike", "Product returns increased sharply!", false, -6000f, -0.09f),
        new RandomEvent("Warranty Claims", "Warranty claims eating into profits!", false, -5000f, -0.07f),
        new RandomEvent("Support Overload", "Customer support overwhelmed!", false, -4000f, -0.08f),
        new RandomEvent("System Crash", "Important system crashed!", false, -5500f, -0.06f),
        new RandomEvent("Data Loss", "Minor data loss incident!", false, -6500f, -0.08f),
        new RandomEvent("Backup Failure", "Backup system failed!", false, -5000f, -0.07f),
        new RandomEvent("Network Down", "Network outage affected operations!", false, -4500f, -0.08f),
        new RandomEvent("Email Hack", "Company email compromised!", false, -6000f, -0.09f),
        new RandomEvent("Phishing Attack", "Employees fell for phishing!", false, -5500f, -0.07f),
        new RandomEvent("Malware Found", "Malware detected on systems!", false, -5000f, -0.08f),
        new RandomEvent("Password Breach", "Password breach detected!", false, -4500f, -0.09f),
        new RandomEvent("Vendor Issue", "Key vendor having problems!", false, -5500f, -0.07f),
        new RandomEvent("Shipping Delay", "Shipping delays affecting customers!", false, -4000f, -0.08f),
        new RandomEvent("Inventory Error", "Inventory count was wrong!", false, -5000f, -0.06f),
        new RandomEvent("Stockout", "Popular product out of stock!", false, -6000f, -0.09f),
        new RandomEvent("Overstock", "Overstocked on slow-moving items!", false, -5500f, -0.07f),
        new RandomEvent("Spoilage", "Product spoilage in warehouse!", false, -6500f, -0.08f),
        new RandomEvent("Damage Claim", "Shipping damage claims!", false, -4500f, -0.06f),
        new RandomEvent("Lost Shipment", "Shipment lost in transit!", false, -5000f, -0.08f),
        new RandomEvent("Customs Hold", "Products held in customs!", false, -5500f, -0.07f),
        new RandomEvent("Import Tax", "Unexpected import taxes!", false, -6000f, -0.06f),
        new RandomEvent("Export Block", "Export license delayed!", false, -5000f, -0.08f),
        new RandomEvent("Currency Loss", "Currency exchange losses!", false, -6500f, -0.07f),
        new RandomEvent("Bank Fee", "Unexpected bank fees!", false, -4000f, -0.05f),
        new RandomEvent("Interest Hike", "Interest rates increased!", false, -5500f, -0.08f),
        new RandomEvent("Credit Issue", "Credit line reduced!", false, -6000f, -0.09f),
        new RandomEvent("Invoice Dispute", "Client disputing invoices!", false, -5000f, -0.07f),
        new RandomEvent("Late Payment", "Major client paying late!", false, -4500f, -0.08f),
        new RandomEvent("Bad Debt", "Customer defaulted on payment!", false, -7000f, -0.06f),
        new RandomEvent("Accounting Error", "Accounting error discovered!", false, -5500f, -0.07f),
        new RandomEvent("Tax Penalty", "Tax filing penalty!", false, -6000f, -0.05f),
        new RandomEvent("Audit Issue", "Minor audit issues found!", false, -5000f, -0.08f),
        new RandomEvent("Compliance Fine", "Minor compliance fine!", false, -6500f, -0.06f),
        new RandomEvent("License Issue", "License renewal problems!", false, -4500f, -0.07f),
        new RandomEvent("Permit Delay", "Permit approval delayed!", false, -5000f, -0.08f),
        new RandomEvent("Zoning Problem", "Zoning issue discovered!", false, -5500f, -0.07f),
        new RandomEvent("Inspection Fail", "Failed routine inspection!", false, -6000f, -0.09f),
        new RandomEvent("Safety Violation", "Minor safety violation!", false, -5000f, -0.08f),
        new RandomEvent("OSHA Visit", "OSHA found minor issues!", false, -5500f, -0.07f),
        new RandomEvent("HR Complaint", "HR complaint filed!", false, -4000f, -0.08f),
        new RandomEvent("Harassment Claim", "Harassment claim to investigate!", false, -6000f, -0.10f),
        new RandomEvent("Discrimination Complaint", "Discrimination complaint!", false, -5500f, -0.09f),
        new RandomEvent("Wrongful Term", "Wrongful termination claim!", false, -7000f, -0.08f),
        new RandomEvent("Union Grievance", "Union filed grievance!", false, -5000f, -0.07f),
        new RandomEvent("Strike Threat", "Employees threatening strike!", false, -6000f, -0.10f),
        new RandomEvent("Walkout", "Partial employee walkout!", false, -6500f, -0.09f),
        new RandomEvent("Sick Epidemic", "Many employees sick at once!", false, -5000f, -0.08f),
        new RandomEvent("Burnout Wave", "Employee burnout affecting work!", false, -4500f, -0.09f),
        new RandomEvent("Morale Drop", "Employee morale declining!", false, -4000f, -0.10f),
        new RandomEvent("Talent Drain", "Losing talent to competitors!", false, -6000f, -0.08f),
        new RandomEvent("Hiring Freeze", "Budget forced hiring freeze!", false, -5000f, -0.07f),
        new RandomEvent("Training Cut", "Had to cut training budget!", false, -4000f, -0.08f),
        new RandomEvent("Benefit Reduction", "Had to reduce benefits!", false, -4500f, -0.09f),
        new RandomEvent("Salary Freeze", "Salary increases frozen!", false, -5000f, -0.10f),
        new RandomEvent("Bonus Cut", "Bonuses reduced this quarter!", false, -5500f, -0.08f),
        new RandomEvent("Layoff Rumors", "Layoff rumors hurting morale!", false, -4000f, -0.09f),
        new RandomEvent("Restructuring", "Restructuring causing chaos!", false, -6000f, -0.08f),
        new RandomEvent("Merger Talks", "Merger talks creating uncertainty!", false, -5500f, -0.07f),
        new RandomEvent("Leadership Change", "Leadership change disrupting operations!", false, -6500f, -0.10f),
        new RandomEvent("Strategy Shift", "Strategy pivot causing confusion!", false, -5000f, -0.08f),
        new RandomEvent("Rebranding Cost", "Rebranding more expensive than planned!", false, -7000f, -0.07f),
        new RandomEvent("Marketing Flop", "Marketing campaign flopped!", false, -6000f, -0.09f),
        new RandomEvent("Ad Controversy", "Advertisement caused controversy!", false, -5500f, -0.10f),
        new RandomEvent("PR Mistake", "PR team made a mistake!", false, -4500f, -0.08f),
        new RandomEvent("Social Backlash", "Social media backlash!", false, -5000f, -0.09f),
        new RandomEvent("Influencer Issue", "Influencer partnership went wrong!", false, -5500f, -0.07f),
        new RandomEvent("Event Cancelled", "Major event cancelled!", false, -6000f, -0.06f),
        new RandomEvent("Conference Flop", "Conference booth was a waste!", false, -5000f, -0.05f),
        new RandomEvent("Webinar Fail", "Webinar had technical issues!", false, -4000f, -0.06f),
        new RandomEvent("Product Launch Delay", "Product launch delayed!", false, -7000f, -0.10f),
        new RandomEvent("Feature Cut", "Had to cut promised features!", false, -5500f, -0.08f),
        new RandomEvent("Bug Discovery", "Major bug discovered post-launch!", false, -6000f, -0.09f),
        new RandomEvent("Compatibility Issue", "Compatibility issues reported!", false, -5000f, -0.07f),
        new RandomEvent("Integration Fail", "Integration with partner failed!", false, -5500f, -0.08f),
        new RandomEvent("API Problems", "API causing partner issues!", false, -4500f, -0.07f),
        new RandomEvent("Documentation Gap", "Documentation incomplete!", false, -4000f, -0.06f),
        new RandomEvent("Support Backlog", "Support ticket backlog growing!", false, -5000f, -0.08f),
        new RandomEvent("Response Time", "Response times too slow!", false, -4500f, -0.09f),
        new RandomEvent("SLA Breach", "SLA breach with client!", false, -6000f, -0.07f),
        new RandomEvent("Uptime Issue", "Uptime below promised levels!", false, -5500f, -0.08f),
        new RandomEvent("Performance Drop", "System performance degraded!", false, -5000f, -0.07f),
        new RandomEvent("Scalability Hit", "Scalability issues appearing!", false, -6000f, -0.09f),
        new RandomEvent("Tech Debt", "Technical debt catching up!", false, -5500f, -0.08f),
        new RandomEvent("Legacy System", "Legacy system causing problems!", false, -6500f, -0.07f),
        new RandomEvent("Migration Issue", "Data migration had issues!", false, -5000f, -0.08f),
        new RandomEvent("Vendor Lock-in", "Vendor lock-in costing more!", false, -5500f, -0.06f),
        new RandomEvent("Contract Dispute", "Contract dispute with vendor!", false, -6000f, -0.07f),
        new RandomEvent("Price Increase", "Supplier increased prices!", false, -5000f, -0.08f),
        new RandomEvent("Material Shortage", "Raw material shortage!", false, -6500f, -0.09f),
    });

    // Small good events (for Low Risk card) - 100 events
    [System.NonSerialized]
    public List<RandomEvent> smallGoodEvents = SmallGoodDefaults();

    static List<RandomEvent> SmallGoodDefaults() => Keyed(SmallGoodList, new List<RandomEvent>()
    {
        new RandomEvent("Small Bonus", "A minor investment paid off!", true, 2000f, 0.03f),
        new RandomEvent("Cost Savings", "You found ways to cut some costs!", true, 1500f, 0.04f),
        new RandomEvent("Good Feedback", "Customers are leaving positive feedback!", true, 1800f, 0.03f),
        new RandomEvent("Intern Success", "An intern's idea saved some money!", true, 1200f, 0.02f),
        new RandomEvent("Bulk Discount", "You negotiated a small bulk discount!", true, 2200f, 0.03f),
        new RandomEvent("Free Publicity", "Local news featured your company!", true, 1600f, 0.04f),
        new RandomEvent("Referral Bonus", "Employee referrals brought new clients!", true, 2500f, 0.03f),
        new RandomEvent("Early Payment", "Client paid invoice early!", true, 1400f, 0.02f),
        new RandomEvent("Tip Jar", "Found money in old account!", true, 1000f, 0.02f),
        new RandomEvent("Rebate Check", "Received rebate check!", true, 1300f, 0.02f),
        new RandomEvent("Utility Refund", "Utility company refund!", true, 1100f, 0.02f),
        new RandomEvent("Insurance Refund", "Insurance premium refund!", true, 1500f, 0.02f),
        new RandomEvent("Deposit Return", "Security deposit returned!", true, 1800f, 0.01f),
        new RandomEvent("Overpayment Found", "Found client overpayment to return favor!", true, 1200f, 0.03f),
        new RandomEvent("Scrap Value", "Old equipment had scrap value!", true, 1400f, 0.02f),
        new RandomEvent("Recycling Money", "Recycling brought in cash!", true, 800f, 0.02f),
        new RandomEvent("Vending Revenue", "Vending machines doing well!", true, 600f, 0.01f),
        new RandomEvent("Parking Revenue", "Parking lot generating income!", true, 1000f, 0.02f),
        new RandomEvent("Sublease Income", "Subleasing unused space!", true, 1500f, 0.02f),
        new RandomEvent("Equipment Rental", "Rented out unused equipment!", true, 1200f, 0.02f),
        new RandomEvent("Consulting Fee", "Small consulting gig!", true, 2000f, 0.03f),
        new RandomEvent("Speaking Fee", "Employee earned speaking fee!", true, 1800f, 0.02f),
        new RandomEvent("Article Payment", "Paid for industry article!", true, 1000f, 0.02f),
        new RandomEvent("Survey Reward", "Completed surveys for rewards!", true, 500f, 0.01f),
        new RandomEvent("Beta Tester Pay", "Paid to beta test software!", true, 800f, 0.02f),
        new RandomEvent("Focus Group", "Participated in focus group!", true, 1200f, 0.01f),
        new RandomEvent("Research Study", "Participated in research study!", true, 1500f, 0.02f),
        new RandomEvent("Affiliate Income", "Affiliate links generating income!", true, 1800f, 0.03f),
        new RandomEvent("Ad Revenue", "Website ads performing well!", true, 1400f, 0.02f),
        new RandomEvent("Sponsorship", "Small sponsorship deal!", true, 2200f, 0.03f),
        new RandomEvent("Merchandise Sale", "Company merch selling!", true, 1600f, 0.02f),
        new RandomEvent("Book Royalties", "Book royalties came in!", true, 1000f, 0.02f),
        new RandomEvent("Patent License", "Small patent licensing fee!", true, 2500f, 0.03f),
        new RandomEvent("Software License", "Software license revenue!", true, 2000f, 0.03f),
        new RandomEvent("API Access Fee", "API access fees!", true, 1800f, 0.02f),
        new RandomEvent("Data License", "Data licensing income!", true, 2200f, 0.03f),
        new RandomEvent("White Label", "White label deal!", true, 2500f, 0.03f),
        new RandomEvent("Reseller Deal", "Reseller agreement signed!", true, 1500f, 0.02f),
        new RandomEvent("Distributor Bonus", "Distributor performance bonus!", true, 1800f, 0.02f),
        new RandomEvent("Loyalty Reward", "Vendor loyalty reward!", true, 1200f, 0.02f),
        new RandomEvent("Volume Bonus", "Volume purchase bonus!", true, 1600f, 0.02f),
        new RandomEvent("Early Bird", "Early bird discount on supplies!", true, 1400f, 0.02f),
        new RandomEvent("Clearance Find", "Found great clearance deals!", true, 1000f, 0.02f),
        new RandomEvent("Auction Win", "Won equipment at auction cheap!", true, 1800f, 0.02f),
        new RandomEvent("Barter Deal", "Beneficial barter arrangement!", true, 1500f, 0.02f),
        new RandomEvent("Trade Credit", "Received trade credit!", true, 1200f, 0.02f),
        new RandomEvent("Gift Card Found", "Found unused gift cards!", true, 800f, 0.01f),
        new RandomEvent("Points Redemption", "Redeemed reward points!", true, 1000f, 0.01f),
        new RandomEvent("Cashback", "Credit card cashback!", true, 1400f, 0.02f),
        new RandomEvent("Miles Converted", "Converted airline miles!", true, 1100f, 0.01f),
        new RandomEvent("Hotel Points", "Used hotel points for event!", true, 900f, 0.01f),
        new RandomEvent("Free Upgrade", "Got free service upgrade!", true, 1500f, 0.02f),
        new RandomEvent("Comp Tickets", "Received complimentary tickets!", true, 800f, 0.01f),
        new RandomEvent("Free Samples", "Vendor sent free samples!", true, 600f, 0.01f),
        new RandomEvent("Trial Extended", "Free trial extended!", true, 1000f, 0.02f),
        new RandomEvent("Beta Access", "Free beta access to tool!", true, 1200f, 0.02f),
        new RandomEvent("Open Source Win", "Open source tool saved money!", true, 1500f, 0.02f),
        new RandomEvent("Free Training", "Vendor offered free training!", true, 1800f, 0.02f),
        new RandomEvent("Certification Paid", "Vendor paid for certification!", true, 2000f, 0.02f),
        new RandomEvent("Conference Comp", "Free conference pass!", true, 1400f, 0.02f),
        new RandomEvent("Webinar Lead", "Webinar generated a lead!", true, 1600f, 0.03f),
        new RandomEvent("Newsletter Sub", "Newsletter subscriber boost!", true, 1000f, 0.02f),
        new RandomEvent("Social Share", "Content got shared widely!", true, 1200f, 0.02f),
        new RandomEvent("Mention", "Positive mention in article!", true, 1500f, 0.02f),
        new RandomEvent("Backlink", "Got valuable backlink!", true, 1100f, 0.02f),
        new RandomEvent("Directory Listing", "Free directory listing!", true, 800f, 0.01f),
        new RandomEvent("Review Response", "Review response appreciated!", true, 1000f, 0.02f),
        new RandomEvent("Testimonial", "Got great testimonial!", true, 1400f, 0.02f),
        new RandomEvent("Case Study OK", "Client approved case study!", true, 1600f, 0.02f),
        new RandomEvent("Reference Given", "Client willing to be reference!", true, 1200f, 0.02f),
        new RandomEvent("Intro Made", "Client made introduction!", true, 1800f, 0.03f),
        new RandomEvent("Referral Given", "Received client referral!", true, 2000f, 0.03f),
        new RandomEvent("Warm Lead", "Got a warm lead!", true, 1500f, 0.02f),
        new RandomEvent("Meeting Set", "Meeting with prospect set!", true, 1200f, 0.02f),
        new RandomEvent("Demo Scheduled", "Product demo scheduled!", true, 1400f, 0.02f),
        new RandomEvent("Trial Started", "Prospect started trial!", true, 1600f, 0.02f),
        new RandomEvent("Proposal Sent", "Sent proposal to prospect!", true, 1000f, 0.02f),
        new RandomEvent("Quote Accepted", "Quote accepted!", true, 2200f, 0.03f),
        new RandomEvent("Contract Sent", "Contract sent for signature!", true, 1800f, 0.02f),
        new RandomEvent("Deal Closed", "Small deal closed!", true, 2500f, 0.03f),
        new RandomEvent("Upsell Made", "Made small upsell!", true, 1500f, 0.02f),
        new RandomEvent("Add-on Sold", "Sold add-on service!", true, 1200f, 0.02f),
        new RandomEvent("Renewal Early", "Client renewed early!", true, 1800f, 0.02f),
        new RandomEvent("Multi-Year", "Client signed multi-year!", true, 2200f, 0.03f),
        new RandomEvent("Prepayment", "Client prepaid for year!", true, 2000f, 0.02f),
        new RandomEvent("Deposit Received", "Received project deposit!", true, 1600f, 0.02f),
        new RandomEvent("Milestone Hit", "Hit project milestone!", true, 1400f, 0.02f),
        new RandomEvent("Phase Complete", "Completed project phase!", true, 1800f, 0.02f),
        new RandomEvent("Deliverable Done", "Deliverable accepted!", true, 1500f, 0.02f),
        new RandomEvent("Sign-off Got", "Got client sign-off!", true, 1200f, 0.02f),
        new RandomEvent("Feedback Positive", "Positive project feedback!", true, 1000f, 0.02f),
        new RandomEvent("Scope Held", "Kept scope from creeping!", true, 1400f, 0.02f),
        new RandomEvent("On Budget", "Project on budget!", true, 1600f, 0.02f),
        new RandomEvent("On Time", "Project on time!", true, 1500f, 0.02f),
        new RandomEvent("Quality Met", "Quality standards met!", true, 1200f, 0.02f),
        new RandomEvent("Bug Fixed", "Fixed bug quickly!", true, 1000f, 0.01f),
        new RandomEvent("Feature Shipped", "Shipped new feature!", true, 1800f, 0.02f),
        new RandomEvent("Update Released", "Released smooth update!", true, 1400f, 0.02f),
        new RandomEvent("Patch Deployed", "Deployed patch successfully!", true, 1100f, 0.01f),
        new RandomEvent("Hotfix Done", "Hotfix resolved issue!", true, 900f, 0.01f),
    });

    // Small bad events (for Low Risk card) - 100 events
    [System.NonSerialized]
    public List<RandomEvent> smallBadEvents = SmallBadDefaults();

    static List<RandomEvent> SmallBadDefaults() => Keyed(SmallBadList, new List<RandomEvent>()
    {
        new RandomEvent("Minor Repairs", "Office needs some minor repairs!", false, -1500f, -0.02f),
        new RandomEvent("Sick Days", "Several employees called in sick!", false, -1200f, -0.03f),
        new RandomEvent("Parking Ticket", "Company vehicles got parking tickets!", false, -800f, -0.01f),
        new RandomEvent("Software Bug", "A minor bug caused small delays!", false, -1800f, -0.02f),
        new RandomEvent("Coffee Machine Broke", "The office coffee machine died!", false, -500f, -0.02f),
        new RandomEvent("Internet Outage", "Internet was down for a few hours!", false, -1000f, -0.03f),
        new RandomEvent("Printer Jam", "The printer jammed during deadline!", false, -600f, -0.01f),
        new RandomEvent("AC Broken", "Air conditioning broke!", false, -1200f, -0.02f),
        new RandomEvent("Heating Issue", "Heating system problems!", false, -1100f, -0.02f),
        new RandomEvent("Plumbing Problem", "Minor plumbing issue!", false, -900f, -0.01f),
        new RandomEvent("Electrical Issue", "Electrical outlet problems!", false, -800f, -0.01f),
        new RandomEvent("Light Bulbs", "Multiple light bulbs burned out!", false, -400f, -0.01f),
        new RandomEvent("Door Lock", "Door lock needs replacement!", false, -600f, -0.01f),
        new RandomEvent("Window Crack", "Window got cracked!", false, -1000f, -0.01f),
        new RandomEvent("Carpet Stain", "Carpet needs cleaning!", false, -500f, -0.01f),
        new RandomEvent("Paint Touch-up", "Walls need paint touch-up!", false, -700f, -0.01f),
        new RandomEvent("Pest Control", "Need pest control visit!", false, -800f, -0.01f),
        new RandomEvent("Fire Extinguisher", "Fire extinguisher expired!", false, -400f, -0.01f),
        new RandomEvent("First Aid Kit", "First aid kit needs restocking!", false, -300f, -0.01f),
        new RandomEvent("Cleaning Supplies", "Ran out of cleaning supplies!", false, -200f, -0.01f),
        new RandomEvent("Office Supplies", "Office supplies running low!", false, -500f, -0.01f),
        new RandomEvent("Toner Empty", "Printer toner ran out!", false, -400f, -0.01f),
        new RandomEvent("Paper Jam", "Paper order delayed!", false, -300f, -0.01f),
        new RandomEvent("Stapler Broken", "Office stapler broke!", false, -100f, -0.01f),
        new RandomEvent("Chair Broken", "Office chair broke!", false, -600f, -0.01f),
        new RandomEvent("Desk Wobble", "Desk needs repair!", false, -400f, -0.01f),
        new RandomEvent("Monitor Issue", "Monitor flickering!", false, -800f, -0.01f),
        new RandomEvent("Keyboard Spill", "Coffee spilled on keyboard!", false, -300f, -0.01f),
        new RandomEvent("Mouse Dead", "Wireless mouse died!", false, -200f, -0.01f),
        new RandomEvent("Headset Broke", "Headset broke!", false, -400f, -0.01f),
        new RandomEvent("Webcam Fuzzy", "Webcam quality poor!", false, -300f, -0.01f),
        new RandomEvent("Speaker Static", "Speakers have static!", false, -250f, -0.01f),
        new RandomEvent("USB Hub", "USB hub stopped working!", false, -200f, -0.01f),
        new RandomEvent("Cable Mess", "Cables need organizing!", false, -150f, -0.01f),
        new RandomEvent("Adapter Lost", "Power adapter lost!", false, -350f, -0.01f),
        new RandomEvent("Charger Missing", "Phone charger missing!", false, -100f, -0.01f),
        new RandomEvent("Badge Issue", "Access badge not working!", false, -200f, -0.01f),
        new RandomEvent("Key Copy", "Need extra key copy!", false, -150f, -0.01f),
        new RandomEvent("Parking Pass", "Parking pass expired!", false, -300f, -0.01f),
        new RandomEvent("Toll Account", "Toll account needs refill!", false, -400f, -0.01f),
        new RandomEvent("Gas Card", "Gas card declined!", false, -500f, -0.01f),
        new RandomEvent("Mileage Log", "Mileage log incomplete!", false, -200f, -0.01f),
        new RandomEvent("Expense Report", "Expense report rejected!", false, -600f, -0.01f),
        new RandomEvent("Receipt Missing", "Missing receipt for expense!", false, -400f, -0.01f),
        new RandomEvent("Reimbursement Delay", "Reimbursement delayed!", false, -300f, -0.01f),
        new RandomEvent("Petty Cash", "Petty cash short!", false, -500f, -0.01f),
        new RandomEvent("Bank Error", "Minor bank error!", false, -700f, -0.01f),
        new RandomEvent("Check Bounce", "Small check bounced!", false, -800f, -0.02f),
        new RandomEvent("Late Fee", "Late payment fee!", false, -600f, -0.01f),
        new RandomEvent("Overdraft", "Minor overdraft fee!", false, -500f, -0.01f),
        new RandomEvent("ATM Fee", "Unexpected ATM fees!", false, -200f, -0.01f),
        new RandomEvent("Wire Fee", "Wire transfer fee!", false, -400f, -0.01f),
        new RandomEvent("Currency Fee", "Currency conversion fee!", false, -350f, -0.01f),
        new RandomEvent("Service Charge", "Monthly service charge!", false, -300f, -0.01f),
        new RandomEvent("Subscription Forgot", "Forgot to cancel subscription!", false, -500f, -0.01f),
        new RandomEvent("Auto-Renew", "Unwanted auto-renewal!", false, -800f, -0.01f),
        new RandomEvent("Trial Ended", "Free trial ended!", false, -600f, -0.02f),
        new RandomEvent("Price Increase", "Service price increased!", false, -700f, -0.01f),
        new RandomEvent("Plan Downgrade", "Had to downgrade plan!", false, -400f, -0.01f),
        new RandomEvent("Feature Removed", "Feature we used removed!", false, -500f, -0.02f),
        new RandomEvent("API Limit", "Hit API rate limit!", false, -600f, -0.01f),
        new RandomEvent("Storage Full", "Cloud storage full!", false, -400f, -0.01f),
        new RandomEvent("Bandwidth Overage", "Bandwidth overage charge!", false, -800f, -0.01f),
        new RandomEvent("Domain Renewal", "Domain renewal due!", false, -300f, -0.01f),
        new RandomEvent("SSL Expiring", "SSL certificate expiring!", false, -400f, -0.01f),
        new RandomEvent("Hosting Issue", "Minor hosting issue!", false, -600f, -0.02f),
        new RandomEvent("CDN Problem", "CDN had hiccup!", false, -500f, -0.01f),
        new RandomEvent("DNS Delay", "DNS propagation slow!", false, -400f, -0.01f),
        new RandomEvent("Email Bounce", "Important email bounced!", false, -300f, -0.01f),
        new RandomEvent("Spam Filter", "Email caught in spam!", false, -200f, -0.01f),
        new RandomEvent("Calendar Conflict", "Calendar double-booked!", false, -400f, -0.01f),
        new RandomEvent("Meeting Missed", "Someone missed meeting!", false, -600f, -0.02f),
        new RandomEvent("Call Dropped", "Important call dropped!", false, -500f, -0.01f),
        new RandomEvent("Video Lag", "Video call was laggy!", false, -300f, -0.01f),
        new RandomEvent("Screen Share Fail", "Screen share didn't work!", false, -400f, -0.01f),
        new RandomEvent("Presentation Glitch", "Presentation had glitch!", false, -600f, -0.01f),
        new RandomEvent("Demo Crash", "Demo crashed mid-show!", false, -800f, -0.02f),
        new RandomEvent("Typo Found", "Typo in sent proposal!", false, -500f, -0.01f),
        new RandomEvent("Wrong Attachment", "Sent wrong attachment!", false, -400f, -0.01f),
        new RandomEvent("Reply All", "Embarrassing reply-all!", false, -600f, -0.01f),
        new RandomEvent("Out of Office", "Forgot out of office!", false, -300f, -0.01f),
        new RandomEvent("Voicemail Full", "Voicemail box full!", false, -400f, -0.01f),
        new RandomEvent("Contact Lost", "Lost a contact's info!", false, -500f, -0.01f),
        new RandomEvent("File Corrupted", "File got corrupted!", false, -700f, -0.02f),
        new RandomEvent("Version Conflict", "Document version conflict!", false, -500f, -0.01f),
        new RandomEvent("Merge Conflict", "Code merge conflict!", false, -600f, -0.01f),
        new RandomEvent("Deploy Delay", "Deployment delayed!", false, -800f, -0.02f),
        new RandomEvent("Test Failed", "Test suite failed!", false, -500f, -0.01f),
        new RandomEvent("Build Broken", "Build broke temporarily!", false, -600f, -0.01f),
        new RandomEvent("Lint Error", "Lint errors to fix!", false, -300f, -0.01f),
        new RandomEvent("Code Review", "Code review took longer!", false, -400f, -0.01f),
        new RandomEvent("Documentation", "Documentation needs update!", false, -500f, -0.01f),
        new RandomEvent("Comment Missing", "Code comments missing!", false, -200f, -0.01f),
        new RandomEvent("Refactor Needed", "Minor refactor needed!", false, -600f, -0.01f),
        new RandomEvent("Dependency Issue", "Dependency conflict!", false, -700f, -0.01f),
        new RandomEvent("Package Update", "Package needs updating!", false, -400f, -0.01f),
        new RandomEvent("Security Patch", "Security patch needed!", false, -500f, -0.01f),
        new RandomEvent("Browser Issue", "Browser compatibility issue!", false, -600f, -0.01f),
        new RandomEvent("Mobile Bug", "Mobile-specific bug!", false, -700f, -0.02f),
        new RandomEvent("Responsive Fix", "Responsive design fix needed!", false, -500f, -0.01f),
    });

    // ------------------------------------------------------------------
    // Translation keys (Events table: Assets/Localization/Source/Events.csv)
    // ------------------------------------------------------------------

    /// <summary>The list ids in the cards' translation keys: "event." + list + "." + L10n.Slug(title) + ".title" / ".desc".</summary>
    public const string ExcellentGoodList = "excellent_good", TerribleBadList = "terrible_bad",
                        AverageGoodList = "average_good", AverageBadList = "average_bad",
                        SmallGoodList = "small_good", SmallBadList = "small_bad";

    /// <summary>
    /// Give every card of a list its translation key (RandomEvent.key): "event." + list + "." + L10n.Slug(its English title),
    /// e.g. "event.small_good.tip_jar"; a title repeated within one list gets "_2", "_3"... The list scopes the key, so the
    /// same title in two lists ("Price Increase") keeps two entries. An edited title makes a new key: the card shows its
    /// English until the Events table has it (Tools > Game > Localization > Sync Content CSVs adds it).
    /// </summary>
    static List<RandomEvent> Keyed(string list, List<RandomEvent> events)
    {
        var used = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (RandomEvent e in events)
        {
            if (e == null) continue;
            string slug = L10n.Slug(e.eventName);
            string key = "event." + list + "." + (slug.Length > 0 ? slug : "untitled");
            string unique = key;
            for (int n = 2; !used.Add(unique); n++) unique = key + "_" + n;
            e.key = unique;
        }
        return events;
    }

    /// <summary>
    /// The six lists as defined above, keyed, with their list ids, in this order (a fresh copy on every call). The Editor's
    /// EventsTextSource writes the Events table from it.
    /// </summary>
    public static List<KeyValuePair<string, List<RandomEvent>>> DefaultLists()
    {
        return new List<KeyValuePair<string, List<RandomEvent>>>
        {
            new KeyValuePair<string, List<RandomEvent>>(ExcellentGoodList, ExcellentGoodDefaults()),
            new KeyValuePair<string, List<RandomEvent>>(TerribleBadList, TerribleBadDefaults()),
            new KeyValuePair<string, List<RandomEvent>>(AverageGoodList, AverageGoodDefaults()),
            new KeyValuePair<string, List<RandomEvent>>(AverageBadList, AverageBadDefaults()),
            new KeyValuePair<string, List<RandomEvent>>(SmallGoodList, SmallGoodDefaults()),
            new KeyValuePair<string, List<RandomEvent>>(SmallBadList, SmallBadDefaults())
        };
    }

    static Dictionary<string, string> s_KeyByTitle;

    /// <summary>
    /// The translation key of the card with this English title (the first list that has it), or null. For run stats saved
    /// before the cards had keys: they kept only the title.
    /// </summary>
    public static string KeyForTitle(string englishTitle)
    {
        if (string.IsNullOrWhiteSpace(englishTitle)) return null;
        if (s_KeyByTitle == null)
        {
            var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<RandomEvent>> list in DefaultLists())
            {
                foreach (RandomEvent e in list.Value)
                {
                    string title = e != null && e.eventName != null ? e.eventName.Trim() : "";
                    if (title.Length > 0 && !map.ContainsKey(title)) map[title] = e.key;
                }
            }
            s_KeyByTitle = map;
        }
        return s_KeyByTitle.TryGetValue(englishTitle.Trim(), out string key) ? key : null;
    }

    // Tier order used when the requested list is empty (closest tier first)
    static readonly CardRisk[] FallbackOrder = { CardRisk.Balanced, CardRisk.LowRisk, CardRisk.HighRisk };

    /// <summary>Total number of events across all lists.</summary>
    public int TotalEventCount =>
        Count(excellentGoodEvents) + Count(terribleBadEvents) +
        Count(averageGoodEvents) + Count(averageBadEvents) +
        Count(smallGoodEvents) + Count(smallBadEvents);

    /// <summary>
    /// A random event for the card tier and outcome. If that list is empty it falls back to the same
    /// outcome from another tier, then to any event, so it only returns null when there are no events at all.
    /// </summary>
    public RandomEvent GetRandomEvent(CardRisk risk, bool isGood)
    {
        RandomEvent evt = PickFrom(GetEventList(risk, isGood));
        if (evt != null) return evt;

        foreach (CardRisk other in FallbackOrder)
        {
            if (other == risk) continue;
            evt = PickFrom(GetEventList(other, isGood));
            if (evt != null) return evt;
        }

        foreach (CardRisk other in FallbackOrder)
        {
            evt = PickFrom(GetEventList(other, !isGood));
            if (evt != null) return evt;
        }

        return null;
    }

    /// <summary>The list used for a card tier and outcome.</summary>
    public List<RandomEvent> GetEventList(CardRisk risk, bool isGood)
    {
        switch (risk)
        {
            case CardRisk.HighRisk:
                return isGood ? excellentGoodEvents : terribleBadEvents;
            case CardRisk.LowRisk:
                return isGood ? smallGoodEvents : smallBadEvents;
            default: // Balanced
                return isGood ? averageGoodEvents : averageBadEvents;
        }
    }

    static RandomEvent PickFrom(List<RandomEvent> list)
    {
        if (list == null || list.Count == 0) return null;

        RandomEvent evt = list[Random.Range(0, list.Count)];
        if (evt != null) return evt;

        // Skip null entries
        foreach (RandomEvent e in list)
            if (e != null) return e;
        return null;
    }

    static int Count(List<RandomEvent> list) => list != null ? list.Count : 0;
}

