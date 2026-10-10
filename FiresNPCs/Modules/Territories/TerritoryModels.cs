using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    [Flags]
    public enum TerritoryFlags
    {
        None = 0,
        PushAway = 1 << 0,
        NoBuild = 1 << 1,
        NoPickaxe = 1 << 2,
        NoInteract = 1 << 3,
        NoAttack = 1 << 4,
        PvpOnly = 1 << 5,
        PveOnly = 1 << 6,
        PeriodicHeal = 1 << 7,
        PeriodicDamage = 1 << 8,
        IncreasedPlayerDamage = 1 << 9,
        IncreasedMonsterDamage = 1 << 10,
        NoMonsters = 1 << 11,
        CustomEnvironment = 1 << 12,
        MoveSpeedMultiplier = 1 << 13,
        NoDeathPenalty = 1 << 14,
        NoPortals = 1 << 15,
        PeriodicHealALL = 1 << 16,
        ForceGroundHeight = 1 << 17,
        ForceBiome = 1 << 18,
        AddGroundHeight = 1 << 19,
        NoBuildDamage = 1 << 20,
        MonstersAddStars = 1 << 21,
        InfiniteFuel = 1 << 22,
        NoInteractItems = 1 << 23,
        NoInteractCraftingStation = 1 << 24,
        NoInteractItemStands = 1 << 25,
        NoInteractChests = 1 << 26,
        NoInteractDoors = 1 << 27,
        NoStructureSupport = 1 << 28,
        NoInteractPortals = 1 << 29,
        CustomPaint = 1 << 30,
        LimitZoneHeight = 1 << 31,
    }

    [Flags]
    public enum AdditionalTerritoryFlags
    {
        None = 0,
        NoItemLoss = 1 << 0,
        SnowMask = 1 << 1,
        NoMist = 1 << 2,
        InfiniteEitr = 1 << 3,
        InfiniteStamina = 1 << 4,
        DropMultiplier = 1 << 5,
        ForceWind = 1 << 6,
        GodMode = 1 << 7,
        OnlyForGuild = 1 << 8,
        EWPSpawner = 1 << 9,
        CustomJereSpawner = EWPSpawner, // legacy alias
        JoinOtherServer = 1 << 10,
        RevealOnMap = 1 << 11,
        NoTool = 1 << 12,
        OnlyForFaction = 1 << 13, // KG parity — pairs with OnlyForGuild, gates territory access by faction membership
        /// <summary>Gravity=g, -10..10: 1 = vanilla, 0 = weightless, 0.5 = half, 3 = three times, negative = pulls up (players only).</summary>
        Gravity = 1 << 14,
        /// <summary>Scale=s, 0.25..4: the player's size (body and colliders; speed, jump and reach unchanged). Seen by everyone.</summary>
        Scale = 1 << 15,
        /// <summary>FirstPerson: the camera is forced to first person inside (Core's CrawlCamera); zoom-out ignored. No admin bypass.</summary>
        FirstPerson = 1 << 16,
    }

    public enum TerritoryShape
    {
        Circle,
        Square,
        Rectangle
    }

    public enum TerritoryType
    {
        AdminCreated,
        ForSale
    }

    public enum GradientType
    {
        FromCenter,
        ToCenter,
        LeftRight,
        RightLeft,
        TopBottom,
        BottomTop,
        BottomRightTopLeft,
        TopRightBottomLeft,
        BottomLeftTopRight,
        TopLeftBottomRight
    }

    public enum PaintType
    {
        Dirt,
        Cultivated,
        Paved,
    Grass,
        ClearVegetation
    }

    [Serializable]
    public class Territory
    {
        public TerritoryShape Shape;
        public TerritoryFlags Flags;
        public AdditionalTerritoryFlags AdditionalFlags;
        public int[] X;
        public int[] Y;
        public string Name = string.Empty;
        public List<Color32> Colors = new();
        public int Radius;
        public bool ShowExternalWater = true;
        public int Priority;
        public GradientType GradientType = GradientType.LeftRight;
        public float ExponentialValue = 1f;
        public string OnlyForGuild = null;
        /// <summary>KG-parity: pairs with <see cref="OnlyForGuild"/>. When the AdditionalTerritoryFlags
        /// OnlyForFaction bit is set, this stores the faction tag the territory is restricted to.
        /// Enforcement (entry / interaction gating) lives alongside OnlyForGuild's enforcement path.</summary>
        public string OnlyForFaction = null;
        public int UID = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        public float PeriodicHealValue;
        public float PeriodicHealInterval = 1f;
        public float PeriodicDamageValue;
        public float IncreasedPlayerDamageValue;
        public float IncreasedMonsterDamageValue;
        public string[] CustomEnvironment;
        public float MoveSpeedMultiplier = 1f;
        public float Gravity = 1f;   // 0.0.498, with AdditionalTerritoryFlags.Gravity
        public float Scale = 1f;     // 0.0.498, with AdditionalTerritoryFlags.Scale
        public float OverridenHeight;
        public int OverridenBiome;
        public int AddMonsterLevel;
        public float Wind;
        public float DropMultiplier = 1f;
        public bool RevealHasCustomCenter;
        public float RevealCenterX;
        public float RevealCenterZ;
        public float RevealRadius;
        public PaintType PaintGround;
        public HashSet<string> CustomSpawnerNames = new();
        public List<CustomSpawnDef> CustomSpawners = new();
        public string JoinOtherServerString = string.Empty;
        public TerritoryType Type;
        public string Owner = string.Empty;
        public int SellPrice;
        // 0.0.533 (Fire 10-08): Vedr season / weather shown while inside (client-local, Vedr API 7+). Empty = none.
        public string ForceSeason = string.Empty;
        public string ForceVedrWeather = string.Empty;
        public ValueTuple<int, int> HeightBounds = new(-100000, 100000);
        public string Permitted = string.Empty;

        public Vector3 Pos3D() => new(X?.Length > 0 ? X[0] : 0, (HeightBounds.Item1 + HeightBounds.Item2) / 2f, Y?.Length > 0 ? Y[0] : 0);

        // The middle of the area on the ground plane. A Rectangle's X/Y hold its min corner and its size.
        public Vector2 Center2D() => Shape == TerritoryShape.Rectangle && X?.Length > 1 && Y?.Length > 1
            ? new Vector2(X[0] + X[1] * 0.5f, Y[0] + Y[1] * 0.5f)
            : new Vector2(X?.Length > 0 ? X[0] : 0, Y?.Length > 0 ? Y[0] : 0);
        public bool DrawOnMap() => Colors.Count > 0;
        public bool UsingGradient() => Shape != TerritoryShape.Rectangle && Colors.Count > 1;

        public bool IsInside(Vector3 pos) => IsInside2D(pos.x, pos.z) && pos.y >= HeightBounds.Item1 && pos.y <= HeightBounds.Item2;
        public bool IsInside2D(Vector2 pos) => IsInside2D(pos.x, pos.y);
        public bool IsInside2D(float x, float y)
        {
            switch (Shape)
            {
                case TerritoryShape.Square:
                    return x >= X[0] - Radius && x <= X[0] + Radius && y >= Y[0] - Radius && y <= Y[0] + Radius;
                case TerritoryShape.Circle:
                    return Vector2.Distance(new Vector2(X[0], Y[0]), new Vector2(x, y)) <= Radius;
                case TerritoryShape.Rectangle:
                    return x >= X[0] && x <= X[0] + X[1] && y >= Y[0] && y <= Y[0] + Y[1];
                default:
                    return false;
            }
        }

        public Color32 GetColor() => Colors.Count > 0 ? Colors[0] : new Color32(0, 255, 0, 255);

        public void Serialize(ZPackage pkg)
        {
            pkg.Write((int)Shape);
            pkg.Write((int)Flags);
            pkg.Write((int)AdditionalFlags);
            pkg.Write(X?.Length ?? 0); if (X != null) foreach (var v in X) pkg.Write(v);
            pkg.Write(Y?.Length ?? 0); if (Y != null) foreach (var v in Y) pkg.Write(v);
            pkg.Write(Name ?? "");
            pkg.Write(Colors.Count);
            foreach (var c in Colors) { pkg.Write(c.r); pkg.Write(c.g); pkg.Write(c.b); pkg.Write(c.a); }
            pkg.Write(Radius);
            pkg.Write(ShowExternalWater);
            pkg.Write(Priority);
            pkg.Write(PeriodicHealValue);
            pkg.Write(PeriodicHealInterval);
            pkg.Write(PeriodicDamageValue);
            pkg.Write(IncreasedPlayerDamageValue);
            pkg.Write(IncreasedMonsterDamageValue);
            pkg.Write(CustomEnvironment?.Length ?? 0);
            if (CustomEnvironment != null) foreach (var env in CustomEnvironment) pkg.Write(env ?? "");
            pkg.Write(MoveSpeedMultiplier);
            pkg.Write(Permitted ?? "");
            pkg.Write(OverridenHeight);
            pkg.Write(OverridenBiome);
            pkg.Write(AddMonsterLevel);
            pkg.Write((int)PaintGround);
            pkg.Write((int)GradientType);
            pkg.Write(ExponentialValue);
            pkg.Write(HeightBounds.Item1); pkg.Write(HeightBounds.Item2);
            pkg.Write(Wind);
            pkg.Write(DropMultiplier);
            pkg.Write(OnlyForGuild ?? "");
            pkg.Write(OnlyForFaction ?? "");
            pkg.Write(CustomSpawnerNames.Count); foreach (var s in CustomSpawnerNames) pkg.Write(s);
            pkg.Write(CustomSpawners.Count);
            foreach (var sp in CustomSpawners)
            {
                pkg.Write(sp.Prefab ?? "");
                pkg.Write(sp.Amount);
                pkg.Write(sp.DataName ?? "");
                pkg.Write(sp.Interval);
                pkg.Write(sp.Level);
                pkg.Write(sp.HasPosition);
                if (sp.HasPosition)
                {
                    pkg.Write(sp.Position.x);
                    pkg.Write(sp.Position.y);
                    pkg.Write(sp.Position.z);
                }
            }
            pkg.Write((int)Type);
            pkg.Write(Owner ?? "");
            pkg.Write(SellPrice);
            pkg.Write(UID);
            pkg.Write(JoinOtherServerString ?? "");
            pkg.Write(RevealHasCustomCenter);
            pkg.Write(RevealCenterX);
            pkg.Write(RevealCenterZ);
            pkg.Write(RevealRadius);
            pkg.Write(Gravity);   // 0.0.498: appended last, read with a fallback, so older packages still load
            pkg.Write(Scale);
            pkg.Write(ForceSeason ?? "");       // 0.0.533, appended last with a fallback
            pkg.Write(ForceVedrWeather ?? "");
        }

        public static Territory Deserialize(ZPackage pkg)
        {
            var t = new Territory
            {
                Shape = (TerritoryShape)pkg.ReadInt(),
                Flags = (TerritoryFlags)pkg.ReadInt(),
                AdditionalFlags = (AdditionalTerritoryFlags)pkg.ReadInt()
            };
            int xc = pkg.ReadInt(); t.X = new int[xc]; for (int i = 0; i < xc; i++) t.X[i] = pkg.ReadInt();
            int yc = pkg.ReadInt(); t.Y = new int[yc]; for (int i = 0; i < yc; i++) t.Y[i] = pkg.ReadInt();
            t.Name = pkg.ReadString();
            int colors = pkg.ReadInt();
            t.Colors = new List<Color32>();
            for (int i = 0; i < colors; i++)
            {
                byte r = pkg.ReadByte();
                byte g = pkg.ReadByte();
                byte b = pkg.ReadByte();
                byte a = 255;
                try { a = pkg.ReadByte(); } catch { a = 255; }
                t.Colors.Add(new Color32(r, g, b, a));
            }
            t.Radius = pkg.ReadInt();
            t.ShowExternalWater = pkg.ReadBool();
            t.Priority = pkg.ReadInt();
            t.PeriodicHealValue = pkg.ReadSingle();
            try { t.PeriodicHealInterval = pkg.ReadSingle(); } catch { t.PeriodicHealInterval = 1f; }
            t.PeriodicDamageValue = pkg.ReadSingle();
            t.IncreasedPlayerDamageValue = pkg.ReadSingle();
            t.IncreasedMonsterDamageValue = pkg.ReadSingle();
            int envCount = pkg.ReadInt();
            t.CustomEnvironment = new string[envCount]; for (int i = 0; i < envCount; i++) t.CustomEnvironment[i] = pkg.ReadString();
            t.MoveSpeedMultiplier = pkg.ReadSingle();
            t.Permitted = pkg.ReadString();
            t.OverridenHeight = pkg.ReadSingle();
            t.OverridenBiome = pkg.ReadInt();
            t.AddMonsterLevel = pkg.ReadInt();
            t.PaintGround = (PaintType)pkg.ReadInt();
            t.GradientType = (GradientType)pkg.ReadInt();
            t.ExponentialValue = pkg.ReadSingle();
            t.HeightBounds = new(pkg.ReadInt(), pkg.ReadInt());
            t.Wind = pkg.ReadSingle();
            t.DropMultiplier = pkg.ReadSingle();
            t.OnlyForGuild = pkg.ReadString();
            t.OnlyForFaction = pkg.ReadString();
            int customSpawnerCount = pkg.ReadInt();
            t.CustomSpawnerNames = new HashSet<string>();
            for (int i = 0; i < customSpawnerCount; i++) t.CustomSpawnerNames.Add(pkg.ReadString());
            try
            {
                int spCount = pkg.ReadInt();
                t.CustomSpawners = new List<CustomSpawnDef>(spCount);
                for (int i = 0; i < spCount; i++)
                {
                    var sp = new CustomSpawnDef();
                    sp.Prefab = pkg.ReadString();
                    sp.Amount = pkg.ReadInt();
                    sp.DataName = pkg.ReadString();
                    sp.Interval = pkg.ReadSingle();
                    try { sp.Level = pkg.ReadInt(); } catch { sp.Level = 1; }
                    try
                    {
                        sp.HasPosition = pkg.ReadBool();
                        if (sp.HasPosition)
                        {
                            sp.Position = new Vector3(pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle());
                        }
                    }
                    catch { sp.HasPosition = false; sp.Position = Vector3.zero; }
                    t.CustomSpawners.Add(sp);
                }
            }
            catch { t.CustomSpawners = new List<CustomSpawnDef>(); }
            t.Type = (TerritoryType)pkg.ReadInt();
            t.Owner = pkg.ReadString();
            t.SellPrice = pkg.ReadInt();
            t.UID = pkg.ReadInt();
            t.JoinOtherServerString = pkg.ReadString();
            try
            {
                t.RevealHasCustomCenter = pkg.ReadBool();
                t.RevealCenterX = pkg.ReadSingle();
                t.RevealCenterZ = pkg.ReadSingle();
                t.RevealRadius = pkg.ReadSingle();
            }
            catch { }
            try { t.Gravity = pkg.ReadSingle(); t.Scale = pkg.ReadSingle(); }
            catch { t.Gravity = 1f; t.Scale = 1f; }
            try { t.ForceSeason = pkg.ReadString(); t.ForceVedrWeather = pkg.ReadString(); }
            catch { t.ForceSeason = string.Empty; t.ForceVedrWeather = string.Empty; }
            return t;
        }

        public Color32 CalculateGradient(float t)
        {
            if (Colors.Count == 0) return new Color32(0, 255, 0, 255);
            if (Colors.Count == 1) return Colors[0];
            t = Mathf.Clamp01(t);
            if (t <= 0f) return Colors[0];
            if (t >= 1f) return Colors[Colors.Count - 1];
            float intervalSize = 1f / (Colors.Count - 1);
            int index = Mathf.FloorToInt(t / intervalSize);
            float tInInterval = (t - index * intervalSize) / intervalSize;
            float expValue = 1f - Mathf.Pow(1f - tInInterval, ExponentialValue <= 0 ? 1f : ExponentialValue);
            return Color32.Lerp(Colors[index], Colors[index + 1], expValue);
        }

        public Color32 GetGradientX(float x, bool reverse = false)
        {
            float startX = X[0] - Radius;
            float endX = X[0] + Radius;
            float t = Mathf.Clamp01((x - startX) / (endX - startX));
            if (reverse) t = 1f - t;
            return CalculateGradient(t);
        }

        public Color32 GetGradientY(float y, bool reverse = false)
        {
            float startY = Y[0] - Radius;
            float endY = Y[0] + Radius;
            float t = Mathf.Clamp01((y - startY) / (endY - startY));
            if (reverse) t = 1f - t;
            return CalculateGradient(t);
        }

        public Color32 GetGradientXY(Vector2 pos, bool reverse = false)
        {
            float startX = X[0] - Radius;
            float endX = X[0] + Radius;
            float startY = Y[0] - Radius;
            float endY = Y[0] + Radius;
            float tX = Mathf.Clamp01((pos.x - startX) / (endX - startX));
            float tY = Mathf.Clamp01((pos.y - startY) / (endY - startY));
            float t = (tX + tY) * 0.5f;
            if (reverse) t = 1f - t;
            return CalculateGradient(t);
        }

        public Color32 GetGradientFromCenter(Vector2 pos, bool reverse = false)
        {
            float t = Mathf.Clamp01(Vector2.Distance(pos, new Vector2(X[0], Y[0])) / Mathf.Max(1f, Radius));
            if (reverse) t = 1f - t;
            return CalculateGradient(t);
        }
    }

    [Serializable]
    public class CustomSpawnDef
    {
        public string Prefab = string.Empty;
        public int Amount = 1;
        public string DataName = string.Empty;
        public float Interval = 300f;
        public int Level = 1;
        public bool HasPosition = false;
        public Vector3 Position = Vector3.zero;
    }
}
