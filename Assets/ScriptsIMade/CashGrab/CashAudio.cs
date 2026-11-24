using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The cash floor's sounds: its own small pool of 2D voices (the floor fires many short sounds a second, and its streak
/// chimes need their pitch), clips from Assets/Resources/CashGrab/Audio (synthesized by Tools/Audio/make_cash_sfx.py:
/// "grab_1.wav", "grab_2.wav"...), each play a random variant at a jittered pitch, scaled by the game's SFX volume.
/// Loops (vacuum, industrial collector) fade in and out. Missing clips are simply silent.
/// Only plays while the floor is on screen (CashGrabSystem routes the events here then).
/// </summary>
[DisallowMultipleComponent]
public class CashAudio : MonoBehaviour
{
    const int VoiceCount = 16;
    const string Folder = "CashGrab/Audio/";

    readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
    readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
    AudioSource[] voices;
    float[] voiceStart;
    AudioSource vacuumLoop, industrialLoop;
    float vacuumLevel, industrialLevel;

    void Awake()
    {
        voices = new AudioSource[VoiceCount];
        voiceStart = new float[VoiceCount];
        for (int i = 0; i < VoiceCount; i++) voices[i] = NewSource(false);
        vacuumLoop = NewSource(true);
        industrialLoop = NewSource(true);
    }

    AudioSource NewSource(bool loop)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;
        s.dopplerLevel = 0f;
        s.bypassReverbZones = true;
        s.priority = 96;
        return s;
    }

    static float Volume => GameSettings.SfxVolume;

    AudioClip[] Clips(string name)
    {
        if (clips.TryGetValue(name, out AudioClip[] found)) return found;
        var list = new List<AudioClip>();
        for (int n = 1; n <= 8; n++)
        {
            AudioClip clip = Resources.Load<AudioClip>(Folder + name + "_" + n);
            if (clip == null) break;
            list.Add(clip);
        }
        found = list.ToArray();
        clips[name] = found;
        return found;
    }

    /// <summary>Play a sound (a random variant). Returns false when there is no clip or it played too recently.</summary>
    public bool Play(string name, float volume = 1f, float pitch = 1f, float minGap = 0.03f, float jitter = 0.05f)
    {
        AudioClip[] set = Clips(name);
        if (set.Length == 0) return false;
        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(name, out float last) && now - last < minGap) return false;
        lastPlayed[name] = now;

        int voice = 0;
        float oldest = float.MaxValue;
        for (int i = 0; i < VoiceCount; i++)
        {
            if (!voices[i].isPlaying)
            {
                voice = i;
                oldest = -1f;
                break;
            }
            if (voiceStart[i] < oldest)
            {
                oldest = voiceStart[i];
                voice = i;
            }
        }
        AudioSource s = voices[voice];
        s.Stop();
        s.clip = set[Random.Range(0, set.Length)];
        s.pitch = Mathf.Clamp(pitch * (1f + Random.Range(-jitter, jitter)), 0.3f, 3f);
        s.volume = Mathf.Clamp01(volume * Volume);
        s.Play();
        voiceStart[voice] = now;
        return true;
    }

    void Update()
    {
        CashGrabSystem system = CashGrabSystem.Instance;
        CashFloorSim sim = system != null ? system.Sim : null;
        bool open = system != null && system.IsFloorOpen && sim != null && sim.Visible;
        bool vacuum = open && sim.Sucking && sim.tool == CashTool.Vacuum;
        bool industrial = open && sim.Sucking && sim.tool == CashTool.Industrial;
        vacuumLevel = Mathf.MoveTowards(vacuumLevel, vacuum ? 1f : 0f, Time.unscaledDeltaTime * (vacuum ? 6f : 3f));
        industrialLevel = Mathf.MoveTowards(industrialLevel, industrial ? 1f : 0f, Time.unscaledDeltaTime * (industrial ? 5f : 2.5f));
        DriveLoop(vacuumLoop, "vacuum_loop", vacuumLevel, 0.85f + (sim != null ? sim.VacuumHeat * 0.35f : 0f));
        DriveLoop(industrialLoop, "industrial_loop", industrialLevel, 1f);
    }

    void DriveLoop(AudioSource source, string name, float level, float pitch)
    {
        if (level <= 0.001f)
        {
            if (source.isPlaying) source.Stop();
            return;
        }
        if (source.clip == null)
        {
            AudioClip[] set = Clips(name);
            if (set.Length == 0) return;
            source.clip = set[0];
        }
        source.volume = level * 0.75f * Volume;
        source.pitch = pitch;
        if (!source.isPlaying) source.Play();
    }

    /// <summary>Cut the short sounds (the floor closed). The loops fade out by themselves in Update (no click).</summary>
    public void StopAll()
    {
        foreach (AudioSource s in voices) s.Stop();
    }

    // ------------------------------------------------------------------
    // Events → sounds
    // ------------------------------------------------------------------

    public void Handle(CashEvent e, CashFloorSim sim)
    {
        switch (e.type)
        {
            case CashEventType.TubePop:
                Play("tube_pop", 0.55f, 1f, 0.06f);
                if (Random.value < 0.5f) Play("bill_flutter", 0.4f, 1f, 0.12f);
                break;
            case CashEventType.BillLand:
                Play("bill_land", e.tier == (int)CashKind.Gold || e.tier == (int)CashKind.Brick ? 0.7f : 0.45f,
                     e.tier == (int)CashKind.Gold ? 0.8f : 1f, 0.05f, 0.12f);
                break;
            case CashEventType.Grab:
                Play("grab", 0.8f, 1f, 0.02f, 0.08f);
                break;
            case CashEventType.GrabMany:
                Play("grab_many", 0.85f, 1f, 0.05f);
                break;
            case CashEventType.StackTick:
                Play("stack_tick", 0.6f, 0.9f + Mathf.Min(1.1f, e.tier * 0.025f), 0.015f, 0.02f);
                break;
            case CashEventType.Throw:
                Play("throw", 0.8f);
                break;
            case CashEventType.Drop:
                Play("drop", 0.7f, 1f, 0.1f);
                break;
            case CashEventType.DepositBill:
                Play("stack_tick", 0.35f, 1.3f + Mathf.Min(0.6f, sim.Streak * 0.01f), 0.02f, 0.03f);
                break;
            case CashEventType.DepositBatch:
                if (e.count >= 12) Play("deposit_big", 0.9f, 1f, 0.15f);
                else Play("deposit", e.count > 1 ? 0.85f : 0.6f, 1f, 0.08f);
                break;
            case CashEventType.Swish:
                Play("combo", 0.7f, 1.5f, 0.05f, 0f);
                break;
            case CashEventType.RakeSweep:
                Play("rake", 0.65f, 1f, 0.15f);
                break;
            case CashEventType.VacuumSuck:
                Play("vacuum_suck", 0.55f, 1f + Random.Range(-0.1f, 0.15f), 0.04f);
                break;
            case CashEventType.Jam:
                Play("jam", 0.9f, 1f, 0.5f);
                break;
            case CashEventType.DeptGrab:
                Play("dept_grab", 0.5f, 1f, 0.06f);
                break;
            case CashEventType.DeptDeposit:
                Play("dept_deposit", 0.4f, 1f, 0.12f);
                break;
            case CashEventType.DeptLevelUp:
            case CashEventType.DeptJoined:
                Play("dept_levelup", 0.9f, 1f, 0.5f, 0f);
                break;
            case CashEventType.Telegraph:
                Play("alarm", 0.55f, 1f, 0.4f, 0f);
                break;
            case CashEventType.Interference:
                switch ((CashInterference)e.tier)
                {
                    case CashInterference.Firewall: Play("tape", 0.7f); break;
                    case CashInterference.Popup: Play("denied", 0.6f, 1.3f); break;
                    case CashInterference.Magnet: Play("vacuum_suck", 0.6f, 0.6f, 0.2f); break;
                    case CashInterference.Confetti: Play("fan", 0.8f, 1.3f); break;
                    case CashInterference.RateLimit: Play("jam", 0.6f, 1.4f); break;
                }
                break;
            case CashEventType.Steal:
                Play(e.tier == 1 ? "shove" : "steal", 0.9f, 1f, 0.15f);
                break;
            case CashEventType.Blocked:
                Play("thunk", 0.7f, 1.4f, 0.1f);
                break;
            case CashEventType.Stun:
                Play("stun", 0.9f, 1f, 0.1f);
                break;
            case CashEventType.Miss:
                Play("denied", 0.45f, 1f, 0.25f);
                break;
            case CashEventType.PaperweightPlaced:
                Play("thunk", 0.85f);
                break;
            case CashEventType.FanBlast:
                Play("fan", 0.9f);
                break;
            case CashEventType.PopupClosed:
                Play("grab", 0.6f, 0.7f, 0.05f);
                break;
            case CashEventType.ComboTier:
                Play("combo", 0.75f, Mathf.Pow(1.122f, Mathf.Clamp(e.tier, 0, 8) * 2f), 0.05f, 0f);
                break;
            case CashEventType.Denied:
                Play("denied", 0.5f, 1f, 0.2f);
                break;
            case CashEventType.Payday:
                Play("coin_shower", 0.85f, 1f, 1f, 0f);
                break;
            case CashEventType.Seized:
                if (e.value > 0.0) Play("steal", 0.7f, 0.8f, 0.5f);
                break;

            // ---- The design canvas's extras (the game's casino and stamp sounds where they fit) ----
            case CashEventType.PokerHand:
                GameSfx.Play(Sfx.CardFan, 0.9f);
                if (e.tier >= (int)CashPoker.Rank.ThreeOfAKind) GameSfx.PlayDelayed(Sfx.ChipsWin, 0.18f, 0.8f + 0.04f * e.tier);
                else Play("combo", 0.6f, 1.2f + e.tier * 0.06f, 0.05f, 0f);
                break;
            case CashEventType.BonusPaid:
                GameSfx.PlayThrottled(Sfx.ChipsClink, 0.25f, 0.7f);
                break;
            case CashEventType.BoostStarted:
                if ((CashBoost)e.tier == CashBoost.Jackpot)
                {
                    GameSfx.Play(Sfx.ChipsWin, 1f);
                    Play("coin_shower", 0.9f, 1.1f, 1f, 0f);
                }
                else
                {
                    Play("combo", 0.85f, (CashBoost)e.tier == CashBoost.Payday ? 1f : 0.85f, 0.3f, 0f);
                }
                break;
            case CashEventType.ReelSpin:
                GameSfx.PlayThrottled(Sfx.UiTick, 0.3f, 0.45f);
                break;
            case CashEventType.SpecialSpawned:
                if ((CashSpecial)e.tier == CashSpecial.Golden) GameSfx.PlayThrottled(Sfx.UiGlass, 0.4f, 0.7f);
                break;
            case CashEventType.WrittenUp:
                GameSfx.Play(Sfx.UiScratch, 1f);
                Play("stun", 0.6f, 0.9f, 0.1f);
                break;
            case CashEventType.StampMiss:
                Play("denied", 0.45f, 0.9f, 0.3f);
                break;
            case CashEventType.MarkedBanked:
                GameSfx.PlayThrottled(Sfx.UiGlitch, 0.6f, 0.35f);
                break;
        }
    }
}
