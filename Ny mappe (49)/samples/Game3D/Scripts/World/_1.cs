using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;

// ================================================================
//  Relationship + Kingdom System (v3 - with visible world effects)
//  This file must contain ONLY this class (no other "_1").
//
//  Visible effects (use only Transform.Position / Transform.Rotate):
//    - Relationship improves  -> NPCs hop and walk toward each other
//    - Romance milestone      -> bigger double hop
//    - Relationship worsens   -> NPCs shake and step apart
//    - Duel / kingdom war     -> NPCs spin
//  Set Is2D = true below if your scene is 2D.
// ================================================================

// -------------------- PERSONAL BONDS --------------------
public record Strangers();
public record Acquaintances(int Friendship = 15);
public record Friends(int Friendship = 45);
public record GoodFriends(int Friendship = 70);
public record BestFriends(int Friendship = 100);
public record Crush(int Romance = 30, int Friendship = 40);
public record Dating(int Romance = 55, int Friendship = 50);
public record Partners(int Romance = 80, int Friendship = 70);
public record Engaged(int Romance = 95, int Friendship = 80);
public record Married(int Romance = 100, int Friendship = 90);
public record Rivals(int Rivalry = 40);
public record Enemies(int Rivalry = 75);
public record Nemesis(int Rivalry = 100);

public union Bond(
    Strangers,
    Acquaintances, Friends, GoodFriends, BestFriends,
    Crush, Dating, Partners, Engaged, Married,
    Rivals, Enemies, Nemesis
);

// -------------------- KINGDOM RELATIONS --------------------
public record Neutral();
public record Friendly(int Reputation = 25);
public record Allied(int Reputation = 70);
public record Vassal(int Reputation = 85);
public record Overlord(int Reputation = 85);
public record Hostile(int Reputation = -35);
public record War(int Reputation = -80);
public record EternalWar(int Reputation = -100);

public union KingdomRelation(
    Neutral, Friendly, Allied, Vassal, Overlord,
    Hostile, War, EternalWar
);

// -------------------- INTERACTIONS --------------------
public record Chat();
public record Joke(bool Funny = true);
public record Gift(string Item, int Value);
public record Hug();
public record Flirt(bool Bold = false);
public record Kiss();
public record Argue();
public record Insult();
public record ChallengeToDuel();
public record OfferAlliance();
public record DeclareWar();
public record ProposeMarriage();
public record Betray();

public union Interaction(
    Chat, Joke, Gift, Hug, Flirt, Kiss, Argue, Insult,
    ChallengeToDuel, OfferAlliance, DeclareWar, ProposeMarriage, Betray
);

// -------------------- SAVE DATA --------------------
public class SaveData
{
    public Dictionary<string, string> Bonds { get; set; } = new();
    public Dictionary<string, string> KingdomRelations { get; set; } = new();
    public Dictionary<string, string> NpcKingdoms { get; set; } = new();
}

// ================================================================
//  WORLD SCRIPT
// ================================================================
public class _1 : WorldScript
{
    // ---- Tuning ----
    private const string CastPrefix = "NPC";
    private const float Range = 5.5f;
    private const float Interval = 5f;
    private const float PairCooldown = 12f;
    private const float KingdomChance = 0.25f;
    private const float AutosaveEvery = 60f;

    // ---- Visual tuning ----
    private const bool Is2D = false;        // true = move on X/Y plane and spin around Z
    private const float HopHeight = 0.6f;
    private const float MoveSpeed = 1.2f;   // units per second while walking together/apart
    private const float MinTogetherDist = 1.5f;

    private float _timer = Interval;
    private float _clock = 0f;
    private float _autosave = AutosaveEvery;

    private readonly Dictionary<string, Bond> _bonds = new();
    private readonly Dictionary<string, KingdomRelation> _kingdomRelations = new();
    private readonly Dictionary<string, string> _npcKingdom = new();
    private readonly Dictionary<string, float> _pairLastTime = new();

    private static readonly string[] Kingdoms = { "Northfire", "Silverhaven", "Ironclad", "Moonveil" };

    private static string SavePath => Path.Combine(AppContext.BaseDirectory, "relationships.json");

    // -------------------- VISUAL EFFECTS --------------------
    private enum FxKind { Hop, BigHop, Shake, Spin, Move }

    private class Fx
    {
        public string Npc = "";
        public FxKind Kind;
        public float T;
        public float Dur;
        public float LastOffset;   // offset already applied (so we can undo it cleanly)
        public float DirA;         // movement direction (first axis)
        public float DirB;         // movement direction (second axis)
    }

    private readonly List<Fx> _fx = new();

    private void AddFx(string npc, FxKind kind, float dur, float dirA = 0f, float dirB = 0f)
    {
        _fx.Add(new Fx { Npc = npc, Kind = kind, Dur = dur, DirA = dirA, DirB = dirB });
    }

    private void UpdateEffects(float dt)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var fx = _fx[i];
            string npcName = fx.Npc;
            var obj = World.Objects.FirstOrDefault(o => !o.Destroyed && o.Name == npcName);

            fx.T += dt;
            bool finished = fx.T >= fx.Dur;

            if (obj == null)
            {
                _fx.RemoveAt(i);
                continue;
            }

            float k = Math.Min(1f, fx.T / fx.Dur);
            float dx = 0f, dy = 0f, dz = 0f;
            float spin = 0f;

            switch (fx.Kind)
            {
                case FxKind.Hop:
                {
                    float off = finished ? 0f : (float)Math.Abs(Math.Sin(k * Math.PI)) * HopHeight;
                    dy = off - fx.LastOffset;
                    fx.LastOffset = off;
                    break;
                }
                case FxKind.BigHop:
                {
                    float off = finished ? 0f : (float)Math.Abs(Math.Sin(k * Math.PI * 2)) * HopHeight * 1.6f;
                    dy = off - fx.LastOffset;
                    fx.LastOffset = off;
                    break;
                }
                case FxKind.Shake:
                {
                    float off = finished ? 0f : (float)Math.Sin(k * 45f) * 0.15f * (1f - k);
                    dx = off - fx.LastOffset;
                    fx.LastOffset = off;
                    break;
                }
                case FxKind.Spin:
                {
                    spin = 720f * dt;
                    break;
                }
                case FxKind.Move:
                {
                    float step = MoveSpeed * dt;
                    dx = fx.DirA * step;
                    if (Is2D) dy = fx.DirB * step;
                    else dz = fx.DirB * step;
                    break;
                }
            }

            if (dx != 0f || dy != 0f || dz != 0f)
            {
                var t = obj.Transform;
                var p = t.Position;
                t.Position = new Vector3(p.X + dx, p.Y + dy, p.Z + dz);
            }

            if (spin != 0f)
            {
                if (Is2D) obj.Transform.Rotate(new Vector3(0f, 0f, spin));
                else obj.Transform.Rotate(new Vector3(0f, spin, 0f));
            }

            if (finished) _fx.RemoveAt(i);
        }
    }

    // -------------------- LIFECYCLE --------------------
    public override void Start()
    {
        Console.WriteLine("[Kingdom Social] System started (v3 - visual effects)");
        Load();
    }

    public override void OnSceneLoaded(string scene)
    {
        Console.WriteLine($"[Kingdom] Scene loaded: {scene}");
    }

    public override void OnMergeChanged(IReadOnlyCollection<string> merged)
    {
        if (merged is { Count: > 0 })
            Console.WriteLine("[Kingdom] Active scenes: " + string.Join(", ", merged));
    }

    public override void Update(float dt)
    {
        _clock += dt;

        UpdateEffects(dt);

        _autosave -= dt;
        if (_autosave <= 0f)
        {
            _autosave = AutosaveEvery;
            Save();
        }

        _timer -= dt;
        if (_timer > 0f) return;
        _timer = Interval;

        ProcessNearbyNpcs();
    }

    public override void OnStop()
    {
        Console.WriteLine("[Kingdom Social] Stopped");
        Console.WriteLine(GetReport());
        _fx.Clear();
        Save();
    }

    // -------------------- MAIN LOGIC --------------------
    private void ProcessNearbyNpcs()
    {
        var npcs = World.Objects
            .Where(o => !o.Destroyed && o.Active &&
                        o.Name != null &&
                        o.Name.StartsWith(CastPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (int i = 0; i < npcs.Count; i++)
        {
            for (int j = i + 1; j < npcs.Count; j++)
            {
                var a = npcs[i];
                var b = npcs[j];

                var pa = a.Transform.Position;
                var pb = b.Transform.Position;

                if (Vector3.Distance(pa, pb) > Range)
                    continue;

                string nameA = a.Name;
                string nameB = b.Name;
                if (nameA == nameB) continue;

                string pairKey = MakeKey(nameA, nameB);
                if (_pairLastTime.TryGetValue(pairKey, out var last) && _clock - last < PairCooldown)
                    continue;
                _pairLastTime[pairKey] = _clock;

                EnsureKingdom(nameA);
                EnsureKingdom(nameB);

                // Direction from A to B on the movement plane (normalised)
                float dirA = pb.X - pa.X;
                float dirB = Is2D ? (pb.Y - pa.Y) : (pb.Z - pa.Z);
                float len = (float)Math.Sqrt(dirA * dirA + dirB * dirB);
                if (len > 0.0001f) { dirA /= len; dirB /= len; }
                else { dirA = 1f; dirB = 0f; len = 0f; }

                // ---------- Personal relationship ----------
                var currentBond = GetBond(nameA, nameB);
                var personalAction = ChoosePersonalAction(currentBond);
                var newBond = ApplyPersonal(currentBond, personalAction);
                SetBond(nameA, nameB, newBond);

                Console.WriteLine($"[Personal] {nameA} & {nameB}: {currentBond} -> {newBond} via {personalAction}");

                // ---------- Visible reaction in the world ----------
                if (personalAction is ChallengeToDuel)
                {
                    AddFx(nameA, FxKind.Spin, 1.0f);
                    AddFx(nameB, FxKind.Spin, 1.0f);
                }

                if (BondToString(currentBond) != BondToString(newBond))
                {
                    int delta = BondRank(newBond) - BondRank(currentBond);
                    if (delta > 0)
                    {
                        var hop = BondRank(newBond) >= 5 ? FxKind.BigHop : FxKind.Hop;
                        AddFx(nameA, hop, 1.0f);
                        AddFx(nameB, hop, 1.0f);

                        if (len > MinTogetherDist)
                        {
                            AddFx(nameA, FxKind.Move, 1.2f, dirA, dirB);     // A walks toward B
                            AddFx(nameB, FxKind.Move, 1.2f, -dirA, -dirB);   // B walks toward A
                        }
                    }
                    else
                    {
                        AddFx(nameA, FxKind.Shake, 0.8f);
                        AddFx(nameB, FxKind.Shake, 0.8f);
                        AddFx(nameA, FxKind.Move, 1.2f, -dirA, -dirB);       // step apart
                        AddFx(nameB, FxKind.Move, 1.2f, dirA, dirB);
                    }
                }

                // ---------- Kingdom relationship (different kingdoms only) ----------
                string ka = _npcKingdom[nameA];
                string kb = _npcKingdom[nameB];
                if (ka != kb && Random.Shared.NextDouble() < KingdomChance)
                {
                    var currentRel = GetKingdomRelation(ka, kb);
                    var kingdomAction = ChooseKingdomAction(currentRel);
                    var newRel = ApplyKingdom(currentRel, kingdomAction);
                    SetKingdomRelation(ka, kb, newRel);

                    Console.WriteLine($"[Kingdom] {ka} & {kb}: {currentRel} -> {newRel} via {kingdomAction}");

                    if (KingdomToString(currentRel) != KingdomToString(newRel))
                    {
                        if (newRel is War or EternalWar)
                        {
                            AddFx(nameA, FxKind.Spin, 1.4f);
                            AddFx(nameB, FxKind.Spin, 1.4f);
                        }
                        else if (newRel is Hostile)
                        {
                            AddFx(nameA, FxKind.Shake, 0.8f);
                            AddFx(nameB, FxKind.Shake, 0.8f);
                        }
                        else
                        {
                            AddFx(nameA, FxKind.Hop, 1.0f);
                            AddFx(nameB, FxKind.Hop, 1.0f);
                        }
                    }
                }
            }
        }
    }

    // Higher = closer bond, negative = hostile
    private static int BondRank(Bond bond) => bond switch
    {
        Strangers     => 0,
        Acquaintances => 1,
        Friends       => 2,
        GoodFriends   => 3,
        BestFriends   => 4,
        Crush         => 5,
        Dating        => 6,
        Partners      => 7,
        Engaged       => 8,
        Married       => 9,
        Rivals        => -1,
        Enemies       => -2,
        Nemesis       => -3,
        _             => 0
    };

    // -------------------- CHOICES --------------------
    private Interaction ChoosePersonalAction(Bond bond)
    {
        double r = Random.Shared.NextDouble();
        return bond switch
        {
            Strangers     => new Chat(),
            Acquaintances => r < 0.15 ? new Argue()  : new Joke(true),
            Friends       => r < 0.10 ? new Insult() : new Gift("Flower", 10),
            GoodFriends   => r < 0.05 ? new Argue()  : new Hug(),
            BestFriends   => r < 0.30 ? new Flirt(true) : new Hug(),
            Crush         => r < 0.70 ? new Flirt(true) : new Kiss(),
            Dating        => r < 0.30 ? new ProposeMarriage() : new Kiss(),
            Partners      => r < 0.40 ? new ProposeMarriage() : new Kiss(),
            Engaged       => r < 0.10 ? new Argue()  : new Hug(),
            Married       => r < 0.03 ? new Betray() : (r < 0.10 ? new Argue() : new Hug()),
            Rivals        => r < 0.10 ? new Gift("Peace Offering", 15) : (r < 0.50 ? new Insult() : new Argue()),
            Enemies       => r < 0.30 ? new ChallengeToDuel() : new Insult(),
            Nemesis       => new ChallengeToDuel(),
            _             => new Chat()
        };
    }

    private Interaction ChooseKingdomAction(KingdomRelation rel)
    {
        double r = Random.Shared.NextDouble();
        return rel switch
        {
            Neutral     => r < 0.60 ? new OfferAlliance() : (r < 0.80 ? new Argue() : new Chat()),
            Friendly    => r < 0.50 ? new OfferAlliance() : (r < 0.70 ? new Argue() : new Chat()),
            Allied      => r < 0.50 ? new Gift("Royal Gift", 40) : (r < 0.55 ? new Betray() : new Chat()),
            Vassal      => r < 0.05 ? new Betray() : new Chat(),
            Overlord    => r < 0.05 ? new Betray() : new Chat(),
            Hostile     => r < 0.30 ? new DeclareWar() : (r < 0.50 ? new OfferAlliance() : new Argue()),
            War         => r < 0.10 ? new DeclareWar() : (r < 0.30 ? new OfferAlliance() : new Chat()),
            EternalWar  => new Chat(),
            _           => new Chat()
        };
    }

    // -------------------- APPLY CHANGES (specific cases first) --------------------
    private Bond ApplyPersonal(Bond current, Interaction action)
    {
        return (current, action) switch
        {
            (Engaged or Married or Partners, Argue)      => current,
            (Enemies or Nemesis, Argue or Insult)        => current,

            (Strangers, Chat)                            => new Acquaintances(),
            (Acquaintances, Joke)                        => new Friends(),
            (Friends, Gift)                              => new GoodFriends(),
            (GoodFriends, Hug)                           => new BestFriends(),
            (BestFriends, Flirt)                         => new Crush(),
            (Crush, Flirt or Kiss)                       => new Dating(),
            (Dating, Kiss)                               => new Partners(),
            (Dating, ProposeMarriage)                    => new Engaged(),
            (Partners, ProposeMarriage)                  => new Engaged(),
            (Engaged, Hug or Kiss)                       => new Married(),

            (Married or Engaged or Partners, Betray)     => new Enemies(),

            (Rivals, Insult)                             => new Enemies(),
            (Enemies, ChallengeToDuel)                   => new Nemesis(),

            (Rivals, Gift)                               => new Acquaintances(),

            (_, Argue or Insult)                         => new Rivals(),

            _                                            => current
        };
    }

    private KingdomRelation ApplyKingdom(KingdomRelation current, Interaction action)
    {
        return (current, action) switch
        {
            (War, DeclareWar)                            => new EternalWar(),
            (EternalWar, _)                              => current,
            (_, DeclareWar)                              => new War(),

            (War, OfferAlliance)                         => new Hostile(),
            (Hostile, OfferAlliance)                     => new Neutral(),

            (Allied, Betray)                             => new Hostile(),
            (Vassal or Overlord, Betray)                 => new War(),

            (Neutral, OfferAlliance)                     => new Friendly(),
            (Friendly, OfferAlliance)                    => new Allied(),

            (Allied a, Gift)                             => (a.Reputation + 10 >= 85)
                                                                ? new Vassal()
                                                                : new Allied(a.Reputation + 10),

            (War or EternalWar, Argue or Insult)         => current,
            (Hostile, Argue or Insult)                   => current,
            (_, Argue or Insult)                         => new Hostile(),

            _                                            => current
        };
    }

    // -------------------- HELPERS --------------------
    private Bond GetBond(string a, string b)
    {
        return _bonds.TryGetValue(MakeKey(a, b), out var bond) ? bond : new Strangers();
    }

    private void SetBond(string a, string b, Bond bond)
    {
        _bonds[MakeKey(a, b)] = bond;
    }

    private KingdomRelation GetKingdomRelation(string kingdomA, string kingdomB)
    {
        return _kingdomRelations.TryGetValue(MakeKey(kingdomA, kingdomB), out var rel) ? rel : new Neutral();
    }

    private void SetKingdomRelation(string kingdomA, string kingdomB, KingdomRelation rel)
    {
        if (kingdomA == kingdomB) return;
        _kingdomRelations[MakeKey(kingdomA, kingdomB)] = rel;
    }

    private void EnsureKingdom(string npcName)
    {
        if (_npcKingdom.ContainsKey(npcName)) return;
        _npcKingdom[npcName] = Kingdoms[StableHash(npcName) % Kingdoms.Length];
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char c in s) h = h * 31 + c;
            return h & 0x7FFFFFFF;
        }
    }

    private static string MakeKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) < 0 ? $"{a}||{b}" : $"{b}||{a}";
    }

    private string GetReport()
    {
        return $"Personal Bonds: {_bonds.Count} | Kingdom Relations: {_kingdomRelations.Count} | NPCs tracked: {_npcKingdom.Count}";
    }

    // -------------------- SAVE / LOAD --------------------
    private static string BondToString(Bond bond) => bond switch
    {
        Strangers       => "Strangers:0",
        Acquaintances x => $"Acquaintances:{x.Friendship}",
        Friends x       => $"Friends:{x.Friendship}",
        GoodFriends x   => $"GoodFriends:{x.Friendship}",
        BestFriends x   => $"BestFriends:{x.Friendship}",
        Crush x         => $"Crush:{x.Romance}",
        Dating x        => $"Dating:{x.Romance}",
        Partners x      => $"Partners:{x.Romance}",
        Engaged x       => $"Engaged:{x.Romance}",
        Married x       => $"Married:{x.Romance}",
        Rivals x        => $"Rivals:{x.Rivalry}",
        Enemies x       => $"Enemies:{x.Rivalry}",
        Nemesis x       => $"Nemesis:{x.Rivalry}",
        _               => "Strangers:0"
    };

    private static Bond BondFromString(string s)
    {
        var (name, value) = SplitValue(s);
        return name switch
        {
            "Acquaintances" => new Acquaintances(value),
            "Friends"       => new Friends(value),
            "GoodFriends"   => new GoodFriends(value),
            "BestFriends"   => new BestFriends(value),
            "Crush"         => new Crush(value),
            "Dating"        => new Dating(value),
            "Partners"      => new Partners(value),
            "Engaged"       => new Engaged(value),
            "Married"       => new Married(value),
            "Rivals"        => new Rivals(value),
            "Enemies"       => new Enemies(value),
            "Nemesis"       => new Nemesis(value),
            _               => new Strangers()
        };
    }

    private static string KingdomToString(KingdomRelation rel) => rel switch
    {
        Neutral         => "Neutral:0",
        Friendly x      => $"Friendly:{x.Reputation}",
        Allied x        => $"Allied:{x.Reputation}",
        Vassal x        => $"Vassal:{x.Reputation}",
        Overlord x      => $"Overlord:{x.Reputation}",
        Hostile x       => $"Hostile:{x.Reputation}",
        War x           => $"War:{x.Reputation}",
        EternalWar x    => $"EternalWar:{x.Reputation}",
        _               => "Neutral:0"
    };

    private static KingdomRelation KingdomFromString(string s)
    {
        var (name, value) = SplitValue(s);
        return name switch
        {
            "Friendly"   => new Friendly(value),
            "Allied"     => new Allied(value),
            "Vassal"     => new Vassal(value),
            "Overlord"   => new Overlord(value),
            "Hostile"    => new Hostile(value),
            "War"        => new War(value),
            "EternalWar" => new EternalWar(value),
            _            => new Neutral()
        };
    }

    private static (string name, int value) SplitValue(string s)
    {
        int idx = s.IndexOf(':');
        if (idx < 0) return (s, 0);
        int.TryParse(s.AsSpan(idx + 1), out int v);
        return (s.Substring(0, idx), v);
    }

    private void Save()
    {
        try
        {
            var data = new SaveData();
            foreach (var kv in _bonds) data.Bonds[kv.Key] = BondToString(kv.Value);
            foreach (var kv in _kingdomRelations) data.KingdomRelations[kv.Key] = KingdomToString(kv.Value);
            foreach (var kv in _npcKingdom) data.NpcKingdoms[kv.Key] = kv.Value;

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SavePath, json);
            Console.WriteLine($"[Kingdom Social] Saved -> {SavePath}");
        }
        catch (Exception e)
        {
            Console.WriteLine("[Kingdom Social] Save failed: " + e.Message);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return;

            var data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath));
            if (data == null) return;

            _bonds.Clear();
            _kingdomRelations.Clear();
            _npcKingdom.Clear();

            foreach (var kv in data.Bonds) _bonds[kv.Key] = BondFromString(kv.Value);
            foreach (var kv in data.KingdomRelations) _kingdomRelations[kv.Key] = KingdomFromString(kv.Value);
            foreach (var kv in data.NpcKingdoms) _npcKingdom[kv.Key] = kv.Value;

            Console.WriteLine("[Kingdom Social] Loaded. " + GetReport());
        }
        catch (Exception e)
        {
            Console.WriteLine("[Kingdom Social] Load failed: " + e.Message);
        }
    }
}