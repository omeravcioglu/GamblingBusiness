using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Drawing the floor (one batch a frame), particles, floating numbers and the screen shake; turning simulation events into
// feedback.
public partial class CashFloorView
{
    struct Particle
    {
        public Vector2 pos, vel;   // world units
        public float life, maxLife, size, rot, spin, gravity, drag;
        public Color32 color;
        public byte sprite;        // 0 sparkle, 1 puff, 2 coin, 3 confetti, 4 soft dot, 5 ring
        public bool grow;
    }

    class FloatText
    {
        public TextMeshProUGUI text;
        public Vector2 pos, vel;
        public float life, maxLife, punch, baseSize;
        public bool active;
        public int mergeKey;       // 0 = none; deposits and department hauls merge into one rising number
        public double value;
    }

    const int MaxParticles = 700;
    readonly List<Particle> particles = new List<Particle>(MaxParticles);
    readonly List<FloatText> texts = new List<FloatText>();
    TextMeshProUGUI[] popupTexts;

    long[] sortKeys = new long[CashFloorSim.MaxBills];
    int[] sortItems = new int[CashFloorSim.MaxBills];
    readonly List<CashFloorSim.Agent> agentOrder = new List<CashFloorSim.Agent>();

    float shake;
    float safeOpen;
    float time;
    readonly float[] crateFlash = new float[CashLedger.DeptCount];
    readonly System.Random fx = new System.Random(4242);

    static readonly Color32 ShadowColor = new Color32(0, 0, 0, 255);

    // ------------------------------------------------------------------
    // Text pool
    // ------------------------------------------------------------------

    void BuildTextPool()
    {
        for (int i = 0; i < 28; i++)
        {
            TextMeshProUGUI t = Label(textLayer, "Float" + i, "", 26f, UIColors.Money, TextAlignmentOptions.Center, true);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 1f);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(420f, 60f);
            t.outlineWidth = 0.18f;
            t.outlineColor = new Color32(29, 36, 51, 255);
            t.gameObject.SetActive(false);
            texts.Add(new FloatText { text = t });
        }
        popupTexts = new TextMeshProUGUI[2];
        for (int i = 0; i < popupTexts.Length; i++)
        {
            TextMeshProUGUI t = BlueprintUI.Wrap(Label(textLayer, "Popup" + i, "", 18f, UIColors.Ink, TextAlignmentOptions.Center, false));
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 1f);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(240f, 110f);
            t.gameObject.SetActive(false);
            popupTexts[i] = t;
        }
    }

    /// <summary>A floating number / word over the floor (world position). Merge keys add up into one rising text.</summary>
    void Float(string message, Vector2 world, Color color, float size = 26f, float life = 1.1f, int mergeKey = 0, double value = 0.0, Func<double, string> format = null)
    {
        FloatText slot = null;
        if (mergeKey != 0)
        {
            foreach (FloatText f in texts)
            {
                if (f.active && f.mergeKey == mergeKey && f.life < f.maxLife * 0.6f)
                {
                    slot = f;
                    slot.value += value;
                    slot.text.text = format != null ? format(slot.value) : message;
                    slot.life = Mathf.Min(slot.life, 0.15f);
                    slot.punch = 1f;
                    slot.baseSize = Mathf.Min(size * 1.6f, slot.baseSize + 1.5f);
                    return;
                }
            }
        }
        foreach (FloatText f in texts)
        {
            if (!f.active)
            {
                slot = f;
                break;
            }
        }
        if (slot == null)
        {
            // Reuse the oldest
            float oldest = -1f;
            foreach (FloatText f in texts)
                if (f.life > oldest) { oldest = f.life; slot = f; }
        }
        if (slot == null) return;
        slot.active = true;
        slot.mergeKey = mergeKey;
        slot.value = value;
        slot.pos = world;
        slot.vel = new Vector2((float)(fx.NextDouble() - 0.5) * 30f, -70f);
        slot.life = 0f;
        slot.maxLife = life;
        slot.punch = 1f;
        slot.baseSize = size;
        slot.text.text = format != null ? format(value) : message;
        slot.text.color = color;
        slot.text.gameObject.SetActive(true);
    }

    void StepTexts(float dt)
    {
        foreach (FloatText f in texts)
        {
            if (!f.active) continue;
            f.life += dt;
            if (f.life >= f.maxLife)
            {
                f.active = false;
                f.text.gameObject.SetActive(false);
                continue;
            }
            f.pos += f.vel * dt;
            f.vel *= Mathf.Exp(-dt * 1.5f);
            f.punch = Mathf.Max(0f, f.punch - dt * 5f);
            float t = f.life / f.maxLife;
            float alpha = t < 0.75f ? 1f : 1f - (t - 0.75f) / 0.25f;
            Color c = f.text.color;
            c.a = alpha;
            f.text.color = c;
            f.text.fontSize = f.baseSize * zoomText * (1f + f.punch * 0.35f);
            f.text.rectTransform.anchoredPosition = TextPos(f.pos);
        }
    }

    float zoomText => Mathf.Clamp(zoom, 0.7f, 1.2f);

    /// <summary>World to the text layer's top-left-anchored space.</summary>
    Vector2 TextPos(Vector2 world)
    {
        return new Vector2(world.x * zoom, -world.y * zoom);
    }

    // ------------------------------------------------------------------
    // Particles
    // ------------------------------------------------------------------

    void Emit(Vector2 pos, int count, byte sprite, Color32 color, float speed, float life, float size, float gravity = 0f, float spread = 1f)
    {
        for (int i = 0; i < count && particles.Count < MaxParticles; i++)
        {
            float a = (float)(fx.NextDouble() * Math.PI * 2.0);
            float s = speed * (0.4f + (float)fx.NextDouble() * 0.6f);
            particles.Add(new Particle
            {
                pos = pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4f * spread,
                vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s,
                life = 0f,
                maxLife = life * (0.7f + (float)fx.NextDouble() * 0.6f),
                size = size * (0.7f + (float)fx.NextDouble() * 0.6f),
                rot = (float)fx.NextDouble() * 360f,
                spin = ((float)fx.NextDouble() - 0.5f) * 720f,
                gravity = gravity,
                drag = 2.5f,
                color = color,
                sprite = sprite
            });
        }
    }

    void Confetti(Vector2 pos, int count, float speed)
    {
        Color32[] palette = { UIColors.Highlight, UIColors.StickyPink, UIColors.StickyBlue, UIColors.StickyGreen, UIColors.Gold, UIColors.PaperHi };
        for (int i = 0; i < count && particles.Count < MaxParticles; i++)
        {
            float a = (float)(fx.NextDouble() * Math.PI * 2.0);
            float s = speed * (0.5f + (float)fx.NextDouble() * 0.5f);
            particles.Add(new Particle
            {
                pos = pos,
                vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a) - 0.7f) * s,
                maxLife = 1.2f + (float)fx.NextDouble() * 0.6f,
                size = 9f + (float)fx.NextDouble() * 6f,
                rot = (float)fx.NextDouble() * 360f,
                spin = ((float)fx.NextDouble() - 0.5f) * 900f,
                gravity = 520f,
                drag = 1.6f,
                color = palette[fx.Next(palette.Length)],
                sprite = 3
            });
        }
    }

    void StepParticles(float dt)
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle p = particles[i];
            p.life += dt;
            if (p.life >= p.maxLife)
            {
                particles.RemoveAt(i);
                continue;
            }
            p.vel.y += p.gravity * dt;
            p.vel *= Mathf.Exp(-p.drag * dt);
            p.pos += p.vel * dt;
            p.rot += p.spin * dt;
            particles[i] = p;
        }
    }

    // ------------------------------------------------------------------
    // Events → feedback
    // ------------------------------------------------------------------

    public void Handle(CashEvent e)
    {
        CashFloorSim sim = system.Sim;
        if (HandleExtra(e)) return;
        switch (e.type)
        {
            case CashEventType.TubePop:
                Emit(e.pos + new Vector2(0f, 10f), 3, 1, new Color32(255, 255, 255, 150), 70f, 0.4f, 22f);
                break;
            case CashEventType.BillLand:
                if (e.tier != (int)CashKind.Bill && e.tier != (int)CashKind.Check) Emit(e.pos, 4, 1, new Color32(220, 235, 220, 120), 60f, 0.4f, 18f);
                break;
            case CashEventType.Grab:
                Emit(e.pos, e.tier == 1 ? 10 : 5, 0, UIColors.PaperHi, 160f, 0.35f, 14f);
                if (e.tier == 1) Float(CashText.Get("cash.fx.got_it_back"), e.pos + new Vector2(0f, -40f), UIColors.Highlight, 24f);
                if (sim.tool == CashTool.Hands) coach?.OnHandsPicked();
                break;
            case CashEventType.GrabMany:
                Emit(e.pos, 8, 0, UIColors.PaperHi, 180f, 0.35f, 14f);
                break;
            case CashEventType.StackTick:
                break;
            case CashEventType.Throw:
                Emit(e.pos, 6, 1, new Color32(255, 255, 255, 110), 120f, 0.35f, 16f);
                break;
            case CashEventType.Drop:
                Emit(e.pos, 6, 1, new Color32(230, 230, 230, 130), 90f, 0.45f, 20f);
                break;
            case CashEventType.DepositBill:
                safeOpen = 0.35f;
                Emit(sim.SafeCenter + new Vector2(0f, -60f), 2, 0, UIColors.Gold, 220f, 0.45f, 16f, 300f);
                break;
            case CashEventType.DepositBatch:
            {
                Float("", sim.SafeCenter + new Vector2(0f, -175f), UIColors.Highlight, 34f, 1.3f, 1, e.value, v => "+" + CashText.Compact(v));
                cashPunch = 1f;
                if (e.count >= 10)
                {
                    shake = Mathf.Max(shake, 6f + Mathf.Min(10f, e.count * 0.2f));
                    Emit(sim.SafeCenter + new Vector2(0f, -40f), Mathf.Min(40, e.count), 2, UIColors.Gold, 420f, 1.1f, 20f, 900f);
                }
                coach?.OnDeposit(e.value);
                break;
            }
            case CashEventType.Swish:
                Float(CashText.Get("cash.fx.swish"), sim.SafeCenter + new Vector2(0f, -170f), UIColors.StickyGreen, 30f, 1f);
                Emit(sim.SafeCenter + new Vector2(0f, -40f), 14, 0, UIColors.Highlight, 300f, 0.6f, 18f);
                break;
            case CashEventType.RakeSweep:
                Emit(e.pos + new Vector2(0f, 10f), 2, 1, new Color32(200, 230, 210, 90), 50f, 0.4f, 16f);
                break;
            case CashEventType.Jam:
                Emit(e.pos, 10, 1, new Color32(90, 90, 90, 170), 80f, 0.9f, 26f, -60f);
                Float(CashText.Get("cash.fx.jammed"), e.pos + new Vector2(0f, -60f), UIColors.Loss, 26f);
                break;
            case CashEventType.DeptGrab:
                Emit(e.pos + new Vector2(0f, -40f), 3, 0, CashRules.DeptColors[Mathf.Clamp(e.dept, 0, 2)], 120f, 0.3f, 12f);
                break;
            case CashEventType.DeptDeposit:
            {
                int d = Mathf.Clamp(e.dept, 0, 2);
                crateFlash[d] = 0.3f;
                Float("", sim.DeptCenter[d] + new Vector2(0f, -90f), CashRules.DeptColors[d], 22f, 1.1f, 10 + d, e.value, v => "+" + CashText.Compact(v));
                break;
            }
            case CashEventType.DeptLevelUp:
            case CashEventType.DeptJoined:
            {
                int d = Mathf.Clamp(e.dept, 0, 2);
                string key = e.type == CashEventType.DeptJoined ? "cash.fx.dept_joined" : "cash.fx.dept_level";
                Float(CashText.Get(key, ("dept", CashText.DeptName(d)), ("level", e.tier)), sim.DeptCenter[d] + new Vector2(0f, -130f), CashRules.DeptColors[d], 30f, 2f);
                Confetti(sim.DeptCenter[d], 30, 420f);
                shake = Mathf.Max(shake, 7f);
                coach?.OnDepartment(d, e.type == CashEventType.DeptJoined, e.tier);
                break;
            }
            case CashEventType.Telegraph:
                coach?.OnInterference((CashInterference)e.tier, e.dept);
                break;
            case CashEventType.Interference:
            {
                var kind = (CashInterference)e.tier;
                Float(CashText.Get("cash.fx." + CashText.InterferenceId(kind)), e.pos + new Vector2(0f, -70f), CashRules.DeptColors[Mathf.Clamp(e.dept, 0, 2)], 28f, 1.4f);
                if (kind == CashInterference.Confetti) Confetti(e.pos, 50, 520f);
                if (kind == CashInterference.Firewall || kind == CashInterference.Popup) shake = Mathf.Max(shake, 4f);
                break;
            }
            case CashEventType.Steal:
                if (e.tier == 1) Float(CashText.Get("cash.fx.shoved", ("count", e.count)), e.pos + new Vector2(0f, -60f), UIColors.Loss, 28f);
                else Float(CashText.Get("cash.fx.stolen", ("amount", CashText.Compact(e.value))), e.pos + new Vector2(0f, -60f), UIColors.Loss, 28f);
                shake = Mathf.Max(shake, 8f);
                break;
            case CashEventType.Blocked:
                Float(CashText.Get("cash.fx.blocked"), e.pos + new Vector2(0f, -60f), UIColors.StickyGreen, 28f);
                Emit(e.pos, 10, 0, UIColors.StickyGreen, 220f, 0.4f, 16f);
                break;
            case CashEventType.Stun:
                Float(CashText.Get("cash.fx.stapled"), e.pos + new Vector2(0f, -110f), UIColors.Highlight, 28f);
                Emit(e.pos + new Vector2(0f, -60f), 12, 0, UIColors.Highlight, 260f, 0.5f, 18f);
                shake = Mathf.Max(shake, 5f);
                break;
            case CashEventType.Miss:
                Emit(e.pos, 4, 1, new Color32(255, 255, 255, 120), 60f, 0.3f, 14f);
                break;
            case CashEventType.PaperweightPlaced:
                Emit(e.pos, 10, 1, new Color32(255, 255, 255, 140), 140f, 0.5f, 18f);
                shake = Mathf.Max(shake, 3f);
                break;
            case CashEventType.FanBlast:
                for (int i = 0; i < 3; i++)
                    particles.Add(new Particle { pos = e.pos, maxLife = 0.45f + i * 0.08f, size = 40f, grow = true, sprite = 5, color = new Color32(255, 255, 255, 170), drag = 0f });
                Emit(e.pos, 16, 1, new Color32(255, 255, 255, 120), 520f, 0.5f, 18f);
                shake = Mathf.Max(shake, 6f);
                break;
            case CashEventType.PopupClosed:
                Emit(e.pos, 12, 1, new Color32(255, 255, 255, 160), 160f, 0.4f, 22f);
                break;
            case CashEventType.ComboTier:
            {
                string key = "cash.fx.streak." + Mathf.Clamp(e.tier, 1, 6);
                Float(CashText.Get(key, ("count", e.count)), new Vector2(sim.Size.x * 0.5f, sim.Size.y * 0.32f), UIColors.Highlight, 40f + e.tier * 4f, 1.6f);
                Confetti(sim.SafeCenter + new Vector2(0f, -60f), 20 + e.tier * 10, 480f);
                shake = Mathf.Max(shake, 4f + e.tier * 2f);
                break;
            }
            case CashEventType.Denied:
                Float(CashText.Get("cash.fx.denied"), e.pos + new Vector2(0f, -50f), UIColors.Loss, 22f, 0.8f);
                break;
            case CashEventType.Payday:
                Float(CashText.Get("cash.fx.payday", ("amount", CashText.Compact(e.value))), new Vector2(sim.Size.x * 0.5f, 120f), UIColors.Highlight, 40f, 2.2f);
                foreach (Vector2 m in sim.TubeMouths) Emit(m + new Vector2(0f, 20f), 12, 2, UIColors.Gold, 360f, 1.2f, 18f, 800f);
                shake = Mathf.Max(shake, 5f);
                break;
            case CashEventType.Seized:
                if (e.value > 0.0) Float(CashText.Get("cash.fx.seized", ("amount", CashText.Compact(e.value))), sim.SafeCenter + new Vector2(0f, -150f), UIColors.Loss, 28f, 1.8f);
                else Emit(e.pos, 4, 1, new Color32(255, 200, 200, 140), 80f, 0.5f, 16f);
                break;
        }
    }

    /// <summary>The CEO is at the desk: departments arriving or upgrading still deserve a word (a toast).</summary>
    public void HandleAway(CashEvent e)
    {
        switch (e.type)
        {
            case CashEventType.DeptJoined:
                GameNotifications.Show(CashText.Get("cash.toast.dept_joined", ("dept", CashText.DeptName(e.dept))), NotificationKind.Info);
                coach?.OnDepartment(e.dept, true, e.tier, away: true);
                break;
            case CashEventType.DeptLevelUp:
                GameNotifications.Show(CashText.Get("cash.toast.dept_level", ("dept", CashText.DeptName(e.dept)), ("level", e.tier)), NotificationKind.Bad);
                break;
            case CashEventType.Payday:
                CashDeskPeek.Pulse();
                break;
            case CashEventType.DeptGrab:
                CashDeskPeek.NoteGrab(e.dept);
                break;
        }
    }

    // ------------------------------------------------------------------
    // Render
    // ------------------------------------------------------------------

    void Render(float dt)
    {
        time += dt;
        CashFloorSim sim = system.Sim;
        CashAtlas atlas = CashAtlas.Shared;
        shake = Mathf.Max(0f, shake - dt * 30f);
        safeOpen = Mathf.Max(0f, safeOpen - dt);
        for (int d = 0; d < crateFlash.Length; d++) crateFlash[d] = Mathf.Max(0f, crateFlash[d] - dt);
        Vector2 jitter = shake > 0.05f ? new Vector2((float)(fx.NextDouble() - 0.5), (float)(fx.NextDouble() - 0.5)) * shake : Vector2.zero;
        shakeRoot.anchoredPosition = jitter;

        StepParticles(dt);
        StepTexts(dt);
        PlaceLabels(sim);

        batch.Begin();
        DrawStations(sim, atlas);
        DrawExtrasUnder(sim, atlas);
        DrawHazardsUnder(sim, atlas);
        DrawBills(sim, atlas, 0);   // on the felt
        DrawAgents(sim, atlas);
        DrawBills(sim, atlas, 1);   // in the air, carried, flying
        DrawHose(sim);
        DrawBills(sim, atlas, 2);   // sucked
        if (sim.tool == CashTool.Tray)
        {
            DrawTool(sim, atlas);       // the tray under its stack
            DrawBills(sim, atlas, 3);
        }
        else
        {
            DrawBills(sim, atlas, 3);   // held, then the hand over them
            DrawTool(sim, atlas);
        }
        DrawHazardsOver(sim, atlas);
        DrawExtrasOver(sim, atlas);
        DrawParticles(atlas);
        batch.End();
    }

    void PlaceLabels(CashFloorSim sim)
    {
        for (int d = 0; d < CashLedger.DeptCount; d++)
        {
            TextMeshProUGUI t = crateLabels[d];
            bool on = system.DeptLevel(d) > 0;
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            if (!on) continue;
            t.rectTransform.anchoredPosition = TextPos(sim.DeptCenter[d] + new Vector2(0f, 42f));
            t.fontSize = 15f * zoomText;
        }
        bool holding = sim.Held.Count > 0 && sim.tool != CashTool.Rake;
        if (safeLabel.gameObject.activeSelf != holding) safeLabel.gameObject.SetActive(holding);
        if (holding)
        {
            safeLabel.rectTransform.anchoredPosition = TextPos(sim.SafeCenter + new Vector2(0f, -112f - Mathf.Sin(time * 6f) * 4f));
            safeLabel.fontSize = 17f * zoomText;
        }
    }

    Vector2 SizeOf(in CashAtlas.Sprite s, float scale) => s.size * (zoom * scale);

    void DrawStations(CashFloorSim sim, CashAtlas atlas)
    {
        // Deposit zone: a pulsing gold ring while the hand carries money
        if (sim.Held.Count > 0 && sim.tool != CashTool.Rake)
        {
            float pulse = 0.6f + Mathf.Sin(time * 7f) * 0.15f;
            batch.Ring(W2L(sim.SafeCenter), sim.DepositRadius * zoom, 5f * zoom, new Color(UIColors.Highlight.r, UIColors.Highlight.g, UIColors.Highlight.b, pulse));
        }

        // Crates
        for (int d = 0; d < CashLedger.DeptCount; d++)
        {
            bool on = system.DeptLevel(d) > 0;
            CashAtlas.Sprite s = atlas.Get(CashRules.DeptSprites[d]);
            float squash = 1f + crateFlash[d] * 0.4f;
            Color32 tint = on ? (Color32)Color.white : new Color32(255, 255, 255, 70);
            if (!atlas.HasArt)
            {
                batch.Box(W2L(sim.DeptCenter[d]), new Vector2(168f, 120f) * zoom, 0f, on ? CashRules.DeptColors[d] : new Color(1f, 1f, 1f, 0.2f));
                continue;
            }
            batch.Sprite(atlas.Get("shadow"), W2L(sim.DeptCenter[d] + new Vector2(6f, 10f)), new Vector2(190f, 120f) * zoom, 0f, new Color32(0, 0, 0, on ? (byte)90 : (byte)30));
            batch.Sprite(s, W2L(sim.DeptCenter[d]), new Vector2(s.size.x * squash, s.size.y / squash) * zoom, 0f, tint);
        }

        // Tubes along the top
        CashAtlas.Sprite tube = atlas.Get("tube");
        for (int i = 0; i < sim.TubeMouths.Length; i++)
        {
            float kick = i < sim.TubeKick.Length ? sim.TubeKick[i] : 0f;
            Vector2 mouth = sim.TubeMouths[i] + new Vector2(0f, 16f + kick * 8f);
            if (atlas.HasArt) batch.Sprite(tube, W2L(mouth), new Vector2(tube.size.x * (1f + kick * 0.12f), tube.size.y) * zoom, 0f, Color.white);
            else batch.Box(W2L(mouth + new Vector2(0f, -40f)), new Vector2(60f, 80f) * zoom, 0f, UIColors.Manila);
        }

        // The safe
        CashAtlas.Sprite safe = atlas.Get(safeOpen > 0f ? "safe_open" : "safe");
        if (atlas.HasArt)
        {
            batch.Sprite(atlas.Get("shadow"), W2L(sim.SafeCenter + new Vector2(8f, 18f)), new Vector2(240f, 140f) * zoom, 0f, new Color32(0, 0, 0, 110));
            float punch = 1f + safeOpen * 0.12f;
            batch.Sprite(safe, W2L(sim.SafeCenter), safe.size * (zoom * punch), 0f, Color.white);
        }
        else
        {
            batch.Box(W2L(sim.SafeCenter), new Vector2(208f, 160f) * zoom, 0f, UIColors.Hex(0x8A929E));
        }
    }

    void DrawHazardsUnder(CashFloorSim sim, CashAtlas atlas)
    {
        foreach (CashFloorSim.Hazard h in sim.hazards)
        {
            float fade = Mathf.Clamp01(h.time / 0.4f) * Mathf.Clamp01((h.duration - h.time) / 0.25f + 0.2f);
            switch (h.kind)
            {
                case CashFloorSim.HazardKind.Magnet:
                {
                    Color c = CashRules.DeptColors[(int)CashDept.Marketing];
                    for (int k = 0; k < 3; k++)
                    {
                        float t = Mathf.Repeat(time * 0.8f + k / 3f, 1f);
                        float r = Mathf.Lerp(h.radius, 40f, t) * zoom;
                        batch.Ring(W2L(h.center), r, 5f * zoom, new Color(c.r, c.g, c.b, (1f - Mathf.Abs(t - 0.5f) * 2f) * 0.55f * fade));
                    }
                    break;
                }
                case CashFloorSim.HazardKind.Paperweight:
                {
                    batch.Ring(W2L(h.center), h.radius * zoom, 3f * zoom, new Color(1f, 1f, 1f, 0.35f * fade), Mathf.Clamp01(h.time / Mathf.Max(0.1f, h.duration)));
                    CashAtlas.Sprite pw = atlas.Get("gadget_paperweight");
                    batch.Sprite(atlas.Get("soft_dot"), W2L(h.center + new Vector2(5f, 8f)), new Vector2(70f, 40f) * zoom, 0f, new Color32(0, 0, 0, 90));
                    batch.Sprite(pw, W2L(h.center), (atlas.HasArt ? pw.size : new Vector2(60f, 60f)) * zoom, 0f, Color.white);
                    break;
                }
            }
        }
    }

    void DrawHazardsOver(CashFloorSim sim, CashAtlas atlas)
    {
        // Firewalls over the money: the player must see exactly where grabbing is blocked
        foreach (CashFloorSim.Hazard h in sim.hazards)
        {
            if (h.kind != CashFloorSim.HazardKind.Firewall) continue;
            float fade = Mathf.Clamp01(h.time / 0.4f) * Mathf.Clamp01((h.duration - h.time) / 0.25f + 0.2f);
            Rect r = WRect(h.rect);
            Color blue = UIColors.Info;
            batch.Rect(r, new Color(blue.r, blue.g, blue.b, 0.34f * fade));
            float stripe = 26f * zoom;
            for (float x = r.xMin - r.height; x < r.xMax; x += stripe)
            {
                Vector2 a = new Vector2(Mathf.Max(r.xMin, x), r.yMin + Mathf.Max(0f, r.xMin - x));
                Vector2 b = new Vector2(Mathf.Min(r.xMax, x + r.height), r.yMin + Mathf.Min(r.height, r.xMax - x));
                if (b.x > a.x) batch.Line(a, b, 3f * zoom, new Color(0.75f, 0.88f, 1f, 0.35f * fade));
            }
            Outline(r, 4f * zoom, new Color(0.75f, 0.88f, 1f, 0.95f * fade));
            batch.Circle(r.center, 26f * zoom, new Color(blue.r * 0.6f, blue.g * 0.6f, blue.b * 0.6f, 0.85f * fade));
            batch.Ring(r.center, 24f * zoom, 4f * zoom, new Color(1f, 1f, 1f, 0.9f * fade), Mathf.Clamp01(h.time / Mathf.Max(0.1f, h.duration)));
            batch.Sprite(atlas.Get("icon_lock"), r.center, new Vector2(28f, 28f) * zoom, 0f, new Color(1f, 1f, 1f, fade));
        }

        int used = 0;
        foreach (CashFloorSim.Hazard h in sim.hazards)
        {
            if (h.kind != CashFloorSim.HazardKind.Popup) continue;
            Rect r = WRect(h.rect);
            float appear = Mathf.Clamp01((h.duration - h.time) / 0.15f);
            Rect shown = new Rect(r.center - r.size * 0.5f * appear, r.size * appear);
            batch.Rect(new Rect(shown.position + new Vector2(6f, -8f) * zoom, shown.size), new Color(0f, 0f, 0f, 0.35f));
            batch.Rect(shown, UIColors.PaperHi);
            Rect title = new Rect(shown.xMin, shown.yMax - 30f * zoom, shown.width, 30f * zoom);
            batch.Rect(title, CashRules.DeptColors[Mathf.Clamp(h.dept, 0, 2)]);
            Outline(shown, 3f * zoom, UIColors.Ink);
            Vector2 x = new Vector2(shown.xMax - 16f * zoom, shown.yMax - 15f * zoom);
            batch.Box(x, new Vector2(22f, 22f) * zoom, 0f, UIColors.Loss);
            batch.Line(x + new Vector2(-6f, -6f) * zoom, x + new Vector2(6f, 6f) * zoom, 3f * zoom, UIColors.PaperHi);
            batch.Line(x + new Vector2(-6f, 6f) * zoom, x + new Vector2(6f, -6f) * zoom, 3f * zoom, UIColors.PaperHi);
            if (used < popupTexts.Length)
            {
                TextMeshProUGUI t = popupTexts[used++];
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                t.text = CashText.Get("cash.popup.text");
                t.rectTransform.anchoredPosition = new Vector2(h.rect.center.x * zoom, -(h.rect.center.y + 10f) * zoom);
                t.rectTransform.sizeDelta = new Vector2(h.rect.width - 30f, h.rect.height - 50f) * zoom;
                t.fontSize = 18f * zoomText;
                t.alpha = appear;
            }
        }
        for (int i = used; i < popupTexts.Length; i++)
            if (popupTexts[i].gameObject.activeSelf) popupTexts[i].gameObject.SetActive(false);
    }

    Rect WRect(Rect world)
    {
        Vector2 a = W2L(new Vector2(world.xMin, world.yMax));
        Vector2 b = W2L(new Vector2(world.xMax, world.yMin));
        return new Rect(a, b - a);
    }

    void Outline(Rect r, float w, Color c)
    {
        batch.Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), w, c);
        batch.Line(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), w, c);
        batch.Line(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), w, c);
        batch.Line(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), w, c);
    }

    static readonly Color[] FallbackBillColors =
    {
        UIColors.Hex(0xDDE8D6), UIColors.Hex(0xE3DDF0), UIColors.Hex(0xF5E1C8), UIColors.Hex(0xD3EBD8), UIColors.Hex(0xF5D6D6), UIColors.Hex(0xD2E8E6)
    };

    /// <summary>Bills of one layer, back to front: 0 on the felt, 1 airborne / carried / flying to a crate or the safe, 2 sucked, 3 held.</summary>
    void DrawBills(CashFloorSim sim, CashAtlas atlas, int layer)
    {
        int count = 0;
        CashBill[] bills = sim.Bills;
        for (int i = 0; i < bills.Length; i++)
        {
            ref CashBill b = ref bills[i];
            if (b.state == BillState.Inactive) continue;
            int l = LayerOf(ref b);
            if (l != layer) continue;
            if (count >= sortKeys.Length) break;
            sortKeys[count] = layer == 1 ? (long)(b.z * 100f) * 10000000L + (b.order % 10000000) : b.order;
            sortItems[count] = i;
            count++;
        }
        if (count == 0) return;
        Array.Sort(sortKeys, sortItems, 0, count);

        // Shadows first (so a pile's bills shade the ones below them but never the ones above)
        CashAtlas.Sprite shadow = atlas.Get("shadow");
        for (int k = 0; k < count; k++)
        {
            ref CashBill b = ref bills[sortItems[k]];
            CashAtlas.Sprite s = atlas.Get(CashLadder.All[b.denom].sprite);
            Vector2 size = (atlas.HasArt ? s.size : new Vector2(110f, 50f)) * 0.92f;
            float lift = Mathf.Max(0f, b.z);
            Vector2 offset = new Vector2(4f + lift * 0.35f, 5f + lift * 0.45f);
            byte alpha = (byte)Mathf.Clamp(70f * (1f - lift / 220f) + 20f, 15f, 110f);
            batch.Sprite(shadow, W2L(b.pos + offset), size * zoom * (1.05f + lift * 0.002f), -b.angle, new Color32(0, 0, 0, alpha));
        }
        for (int k = 0; k < count; k++)
        {
            ref CashBill b = ref bills[sortItems[k]];
            CashAtlas.Sprite s = atlas.Get(CashLadder.All[b.denom].sprite);
            float lift = Mathf.Max(0f, b.z);
            float scale = 0.92f * (1f + lift * 0.0025f);
            // Paper flutters while it flies: squash it along one axis
            float flutter = b.airborne && (b.kind == CashKind.Bill || b.kind == CashKind.Check) ? 0.65f + 0.35f * Mathf.Abs(Mathf.Cos(b.age * 9f + b.phase)) : 1f;
            Vector2 pos = W2L(b.pos + new Vector2(0f, -lift * 0.55f));
            Color32 tint = b.special == CashSpecial.Golden ? new Color32(255, 222, 110, 255)
                         : b.special == CashSpecial.Marked ? new Color32(255, 196, 188, 255)
                         : b.pinned > 0f ? new Color32(235, 245, 255, 255) : (Color32)Color.white;
            if (atlas.HasArt)
            {
                Vector2 size = new Vector2(s.size.x, s.size.y * flutter) * (zoom * scale);
                batch.Sprite(s, pos, size, -b.angle, tint);
            }
            else
            {
                Color c = b.denom < FallbackBillColors.Length ? FallbackBillColors[b.denom] : UIColors.Gold;
                batch.Box(pos, new Vector2(110f, 50f * flutter) * (zoom * scale), -b.angle, c);
            }
        }
    }

    static int LayerOf(ref CashBill b)
    {
        switch (b.state)
        {
            case BillState.Free: return b.z > 1f || b.airborne ? 1 : 0;
            case BillState.Held: return 3;
            case BillState.Sucked: return 2;
            default: return 1;   // carried, flying to the safe or a crate
        }
    }

    void DrawAgents(CashFloorSim sim, CashAtlas atlas)
    {
        agentOrder.Clear();
        foreach (CashFloorSim.Agent a in sim.agents)
            if (a.active) agentOrder.Add(a);
        agentOrder.Sort((x, y) => x.pos.y.CompareTo(y.pos.y));

        CashAtlas.Sprite dot = atlas.Get("soft_dot");
        foreach (CashFloorSim.Agent a in agentOrder)
        {
            string id = CashRules.DeptIds[a.dept];
            bool stunned = a.state == CashFloorSim.AgentState.Stunned;
            bool moving = a.vel.sqrMagnitude > 400f;
            string frame = stunned ? "_stun" : moving && ((int)a.walkPhase & 1) == 1 ? "_b" : "_a";
            CashAtlas.Sprite s = atlas.Get("agent_" + id + frame);
            Vector2 feet = W2L(a.pos);
            float bobY = moving ? Mathf.Abs(Mathf.Sin(a.walkPhase * Mathf.PI)) * 4f * zoom : 0f;
            batch.Sprite(dot, feet + new Vector2(0f, -2f * zoom), new Vector2(54f, 18f) * zoom, 0f, new Color32(0, 0, 0, 110));
            float squash = 1f - a.bob * 0.12f;
            float tilt = stunned ? Mathf.Sin(time * 14f) * 8f : -a.vel.x * 0.02f;
            if (atlas.HasArt)
            {
                Vector2 size = new Vector2(s.size.x / squash, s.size.y * squash) * zoom;
                batch.SpriteFlipped(s, feet + new Vector2(0f, bobY), size, tilt, Color.white, a.facing < 0f);
            }
            else
            {
                batch.Circle(feet + new Vector2(0f, 30f * zoom + bobY), 20f * zoom, CashRules.DeptColors[a.dept]);
                batch.Circle(feet + new Vector2(0f, 62f * zoom + bobY), 14f * zoom, UIColors.PaperHi);
            }

            Vector2 head = feet + new Vector2(0f, 96f * zoom);
            if (a.state == CashFloorSim.AgentState.Telegraph)
            {
                float pulse = 1f + Mathf.Sin(time * 18f) * 0.15f;
                batch.Circle(head, 19f * zoom * pulse, UIColors.Ink);
                batch.Circle(head, 16f * zoom * pulse, UIColors.Loss);
                batch.Box(head + new Vector2(0f, 3f) * zoom, new Vector2(5f, 15f) * zoom * pulse, 0f, UIColors.PaperHi);
                batch.Box(head + new Vector2(0f, -9f) * zoom, new Vector2(5f, 5f) * zoom * pulse, 0f, UIColors.PaperHi);
            }
            else if (stunned)
            {
                CashAtlas.Sprite star = atlas.Get("sparkle");
                for (int k = 0; k < 3; k++)
                {
                    float ang = time * 6f + k * 2.094f;
                    batch.Sprite(star, head + new Vector2(Mathf.Cos(ang) * 22f, Mathf.Sin(ang) * 8f - 10f) * zoom, new Vector2(18f, 18f) * zoom, 0f, UIColors.Highlight);
                }
            }
            else if (a.state == CashFloorSim.AgentState.Dash)
            {
                for (int k = 1; k <= 3; k++)
                    batch.Line(feet + new Vector2(-a.facing * (14f + k * 10f), 30f + k * 8f) * zoom, feet + new Vector2(-a.facing * (30f + k * 14f), 30f + k * 8f) * zoom, 3f * zoom,
                               new Color(1f, 1f, 1f, 0.6f));
            }
        }
    }

    void DrawHose(CashFloorSim sim)
    {
        bool vacuum = sim.tool == CashTool.Vacuum || sim.tool == CashTool.Industrial;
        if (!vacuum || !sim.Input.onFloor) return;
        Vector2 nozzle = sim.HandPos;
        Color ink = UIColors.Ink;
        Color body = sim.tool == CashTool.Industrial ? UIColors.Hex(0xC79A1E) : UIColors.Hex(0x5E6673);
        Color shine = sim.tool == CashTool.Industrial ? UIColors.Hex(0xF2D16B) : UIColors.Hex(0x9AA3AF);
        float width = (sim.tool == CashTool.Industrial ? 40f : 30f) * zoom;
        const int segments = 28;
        var points = hosePoints;
        points.Clear();
        for (int i = 0; i <= segments; i++) points.Add(W2L(sim.HosePoint(nozzle, i / (float)segments)));
        // Ink outline, then the hose's body, a highlight stripe and ribs (it reads as a fat flexible hose)
        for (int i = 1; i < points.Count; i++) batch.Line(points[i - 1], points[i], width + 5f * zoom, ink);
        for (int i = 0; i < points.Count; i++) batch.Circle(points[i], (width + 5f * zoom) * 0.5f, ink, 12);
        for (int i = 1; i < points.Count; i++) batch.Line(points[i - 1], points[i], width, body);
        for (int i = 0; i < points.Count; i++) batch.Circle(points[i], width * 0.5f, body, 12);
        for (int i = 1; i < points.Count; i++)
        {
            Vector2 a = points[i - 1], b = points[i];
            Vector2 dir = (b - a).normalized;
            Vector2 n = new Vector2(-dir.y, dir.x);
            batch.Line(a + n * width * 0.18f, b + n * width * 0.18f, width * 0.18f, shine);
            Vector2 mid = (a + b) * 0.5f;
            batch.Line(mid - n * width * 0.5f, mid + n * width * 0.5f, 2.2f * zoom, new Color(ink.r, ink.g, ink.b, 0.55f));
        }
    }

    readonly List<Vector2> hosePoints = new List<Vector2>(32);

    void DrawTool(CashFloorSim sim, CashAtlas atlas)
    {
        if (!sim.Input.onFloor && sim.Held.Count == 0) return;
        Vector2 hand = W2L(sim.HandPos);
        bool pressing = sim.Input.held;
        string sprite;
        switch (sim.tool)
        {
            case CashTool.Scoop: sprite = "tool_scoop"; break;
            case CashTool.Rake: sprite = "tool_rake"; break;
            case CashTool.Tray: sprite = "tool_tray"; break;
            case CashTool.Vacuum: sprite = "tool_vacuum"; break;
            case CashTool.Industrial: sprite = "tool_industrial"; break;
            default: sprite = pressing || sim.Held.Count > 0 ? "tool_hand_closed" : "tool_hand_open"; break;
        }

        // Reach ring (faint), brighter while the tool works
        if (sim.tool != CashTool.Hands && sim.tool != CashTool.Rake)
        {
            float alpha = pressing ? 0.45f : 0.18f;
            batch.Ring(hand, sim.ToolRadius * zoom, 2.5f * zoom, new Color(1f, 1f, 1f, alpha));
        }
        if (sim.tool == CashTool.Rake)
        {
            float half = sim.ToolRadius;
            batch.Line(hand + new Vector2(-half, 0f) * zoom, hand + new Vector2(half, 0f) * zoom, 3f * zoom, new Color(1f, 1f, 1f, pressing ? 0.45f : 0.2f));
        }

        CashAtlas.Sprite s = atlas.Get(sprite);
        float tilt = sim.tool == CashTool.Rake ? Mathf.Clamp(-sim.CursorVelocity.x * 0.01f, -12f, 12f) : (pressing ? -8f : 0f);
        float scale = sim.tool == CashTool.Hands && pressing ? 0.9f : 1f;
        if (sim.RateLimited) scale *= 0.92f + Mathf.Sin(time * 30f) * 0.03f;
        if (atlas.HasArt)
        {
            batch.Sprite(atlas.Get("shadow"), hand + new Vector2(10f, -14f) * zoom, s.size * zoom * 0.8f, tilt, new Color32(0, 0, 0, 70));
            Color tint = sim.JamTimer > 0f ? new Color(0.75f, 0.75f, 0.75f, 1f) : Color.white;
            batch.Sprite(s, hand, s.size * zoom * scale, tilt, tint);
        }
        else
        {
            batch.Circle(hand, 18f * zoom, UIColors.PaperHi);
        }

        // Vacuum heat
        if (sim.tool == CashTool.Vacuum && (sim.VacuumHeat > 0.02f || sim.JamTimer > 0f))
        {
            Vector2 at = hand + new Vector2(-40f, 72f) * zoom;
            Rect back = new Rect(at, new Vector2(80f, 10f) * zoom);
            batch.Rect(back, UIColors.Dark(0.6f));
            float h = sim.VacuumHeat;
            Color c = Color.Lerp(UIColors.StickyGreen, UIColors.Loss, h);
            batch.Rect(new Rect(at, new Vector2(80f * h, 10f) * zoom), c);
        }
        if (sim.RateLimited)
        {
            Vector2 at = hand + new Vector2(46f, 40f) * zoom;
            batch.Box(at, new Vector2(54f, 26f) * zoom, -6f, UIColors.StickyBlue);
            batch.Box(at + new Vector2(0f, -1f) * zoom, new Vector2(40f, 4f) * zoom, -6f, UIColors.Info);
        }
    }

    void DrawParticles(CashAtlas atlas)
    {
        CashAtlas.Sprite sparkle = atlas.Get("sparkle"), puff = atlas.Get("puff"), coin = atlas.Get("coin"), dot = atlas.Get("soft_dot"), ring = atlas.Get("ring");
        foreach (Particle p in particles)
        {
            float t = p.life / Mathf.Max(0.01f, p.maxLife);
            float alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
            Color32 c = p.color;
            c.a = (byte)(c.a * alpha);
            Vector2 pos = W2L(p.pos);
            float size = p.size * zoom * (p.grow ? 1f + t * 12f : (p.sprite == 1 ? 1f + t * 0.8f : 1f));
            switch (p.sprite)
            {
                case 0: batch.Sprite(sparkle, pos, new Vector2(size, size), p.rot, c); break;
                case 1: batch.Sprite(puff, pos, new Vector2(size, size), p.rot * 0.2f, c); break;
                case 2: batch.Sprite(coin, pos, new Vector2(size, size * Mathf.Abs(Mathf.Cos(p.rot * Mathf.Deg2Rad))), 0f, c); break;
                case 3: batch.Box(pos, new Vector2(size, size * 0.55f * Mathf.Abs(Mathf.Cos(p.rot * 0.03f))), p.rot, c); break;
                case 5: batch.Sprite(ring, pos, new Vector2(size, size), 0f, c); break;
                default: batch.Sprite(dot, pos, new Vector2(size, size), 0f, c); break;
            }
        }
    }
}
