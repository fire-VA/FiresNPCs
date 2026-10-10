using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using BepInEx;
using Debug = UnityEngine.Debug;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Legacy territory loader compatible with KG-style .cfg profiles under Config/FiresRPGmaker/Territories/legacy.
    /// Supports circle/square/rectangle, colors/flags, and optional overrides like gradient/height bounds.
    /// </summary>
    public static class TerritoryLedger
    {
        private static bool _initialized;
        private static readonly List<Territory> _territories = new();
        private static readonly HashSet<string> _loggedColorNames = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _loggedFlagProblems = new(StringComparer.Ordinal);
        private static readonly char[] FlagSegmentTerminators = { '=', ',', '|', ';' };
        private const string HeightBoundsFlag = "HeightBounds";
        private const string NoFlagsMarker = "None";
        private const string ForSaleFlag = "ForSale";

        private static string BaseDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories);
        private static string LegacyDir => Path.Combine(BaseDir, "legacy");
        private static string OutlinesDir => Path.Combine(BaseDir, "outlines");
        private static string DrawnDir => Path.Combine(BaseDir, "drawn");
        private static string LegacySamplePath => Path.Combine(LegacyDir, "SampleTerritory.cfg");

        public static IReadOnlyList<Territory> Territories => _territories;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
          var sw = Stopwatch.StartNew();
  try
      {
       Directory.CreateDirectory(LegacyDir);
     Directory.CreateDirectory(OutlinesDir);
    Directory.CreateDirectory(DrawnDir);
   WriteSampleIfMissing();
       LoadLegacyTerritories();
     // Invalidate snow territory cache so it builds with the loaded territories
      TerritoryRuntime.InvalidateSnowTerritoryCache();
                // Apply terrain paint once on server startup so painted ground is ready before players login
     TerritoryRuntime.ApplyAllCustomPaints(clearCache: true);
        sw.Stop();
       Debug.Log($"[FiresRPGmaker] TerritoryLedger loaded {_territories.Count} territories from legacy cfg in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
         sw.Stop();
     Debug.LogWarning($"[FiresRPGmaker] TerritoryLedger init failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
     }
        }

        public static void Reload()
        {
            var sw = Stopwatch.StartNew();
     try
      {
   LoadLegacyTerritories();
 // Invalidate snow territory cache so it rebuilds with new territory data
            TerritoryRuntime.InvalidateSnowTerritoryCache();
 // Re-apply terrain paint when configs change
 TerritoryRuntime.ApplyAllCustomPaints(clearCache: true);
      sw.Stop();
  Debug.Log($"[FiresRPGmaker] TerritoryLedger reloaded {_territories.Count} territories in {sw.ElapsedMilliseconds}ms");
         }
catch (Exception ex) 
  { 
         sw.Stop();
     Debug.LogWarning($"[FiresRPGmaker] TerritoryLedger reload failed after {sw.ElapsedMilliseconds}ms: {ex.Message}"); 
     }
        }

        private static void LoadLegacyTerritories()
        {
   var sw = Stopwatch.StartNew();
            _territories.Clear();
        _loggedColorNames.Clear();
        _loggedFlagProblems.Clear();
    var sources = new[] { LegacyDir, OutlinesDir, DrawnDir };
     int fileCount = 0;
        foreach (var dir in sources)
     {
        if (!Directory.Exists(dir)) continue;
       foreach (var file in Directory.GetFiles(dir, "*.cfg", SearchOption.AllDirectories))
       {
          try 
        { 
            ParseLegacyFile(file); 
        fileCount++;
 } 
           catch (Exception ex) 
  { 
       Debug.LogWarning($"[FiresRPGmaker] Territory parse failed {file}: {ex.Message}"); 
          }
                }
 }
            _territories.Sort((a, b) => b.Priority.CompareTo(a.Priority));
 sw.Stop();
Debug.Log($"[FiresRPGmaker] LoadLegacyTerritories parsed {fileCount} files, {_territories.Count} territories in {sw.ElapsedMilliseconds}ms");
  }

        private enum InputType
        {
            Text,
            Transition,
            Command,
            RandomCommand,
            Icon,
            Condition,
            AlwaysVisible,
            Color,
            RandomTransition,
            OverrideError
        }

        private static void ParseLegacyFile(string path)
        {
            var lines = File.ReadAllLines(path).ToList();
            var relativePath = path.Substring(BaseDir.Length).TrimStart('\\', '/').Replace('\\', '/');
            string currentProfile = "default";
            int currentPriority = 1;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.Contains("]"))
                {
                    var split = line.Trim('[', ']').Split('@');
                    currentProfile = split[0].Trim();
                    currentPriority = split.Length > 1 && int.TryParse(split[1], out int pr) ? pr : 1;
                    continue;
                }

                // expect shape
                if (!Enum.TryParse(line, true, out TerritoryShape shape)) continue;
                if (i + 4 >= lines.Count) break;

                // coords/radius
                var xyz = lines[i + 1].Replace(" ", string.Empty).Split(',');
                var territory = new Territory { Name = currentProfile, Shape = shape, Priority = currentPriority };
                switch (shape)
                {
                    case TerritoryShape.Circle:
                    case TerritoryShape.Square:
                        territory.X = new[] { int.Parse(xyz[0]) };
                        territory.Y = new[] { int.Parse(xyz[1]) };
                        territory.Radius = int.Parse(xyz[2]);
                        break;
                    case TerritoryShape.Rectangle:
                        territory.X = new[] { int.Parse(xyz[0]), int.Parse(xyz[2]) };
                        territory.Y = new[] { int.Parse(xyz[1]), int.Parse(xyz[3]) };
                        break;
                }

                // colors: r,g,b[,a][,true/false]
                const byte defaultAlpha = 80;
                var colorLine = lines[i + 2];
                var tokens = colorLine.Split(',').Select(t => t.Trim()).ToList();

                // ShowExternalWater flag from any bool token
                foreach (var tok in tokens)
                {
                    if (bool.TryParse(tok, out var water)) { territory.ShowExternalWater = water; break; }
                }

                // collect numeric tokens in order
                var nums = new List<byte>();
                foreach (var tok in tokens)
                {
                    if (byte.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val)) nums.Add(val);
                }

                if (nums.Count >= 3)
                {
                    byte r = nums[0];
                    byte g = nums[1];
                    byte b = nums[2];
                    byte a = nums.Count >= 4 ? nums[3] : defaultAlpha;
                    territory.Colors.Add(new Color32(r, g, b, a));
                    if (_loggedColorNames.Add(territory.Name))
                    {
                        Debug.Log($"[FiresRPGmaker] Parsed territory '{territory.Name}' color tokens=[{string.Join(",", nums)}] -> rgba({r},{g},{b},{a})");
                    }
                }
                else
                {
                    Debug.LogWarning($"[FiresRPGmaker] Territory '{territory.Name}' color line could not parse RGB: {colorLine}");
                }

                // flags (supports optional leading alpha override carried from commands)
                territory.Flags = TerritoryFlags.None;
                territory.AdditionalFlags = AdditionalTerritoryFlags.None;
                var flagsLine = lines[i + 3];
                var flagsSource = $"{relativePath}:{i + 4}";
                var flagTokens = SplitFlagTokens(flagsLine);

                // If the command pushed alpha onto the flags line (numeric first token), consume it as alpha override
                if (flagTokens.Count > 0 && byte.TryParse(flagTokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var alphaOverride))
                {
                    if (territory.Colors.Count > 0)
                    {
                        var c = territory.Colors[0];
                        territory.Colors[0] = new Color32(c.r, c.g, c.b, alphaOverride);
                    }
                    flagTokens.RemoveAt(0);
                }

                foreach (var flagToken in flagTokens)
                {
                    var parts = flagToken.Split(new[] { '=' }, 2, StringSplitOptions.None);
                    var flagName = parts[0].Trim();
                    var custom = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                    // Height bounds custom syntax: HeightBounds=min,max (max optional, defaults to 600; min defaults to 0)
                    if (flagName.Equals(HeightBoundsFlag, StringComparison.OrdinalIgnoreCase))
                    {
                        int minY = 0;
                        int maxY = 600;
                        if (!string.IsNullOrWhiteSpace(custom))
                        {
                            var hb = custom.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (hb.Length >= 1 && int.TryParse(hb[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMin))
                                minY = parsedMin;
                            if (hb.Length >= 2 && int.TryParse(hb[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMax))
                                maxY = parsedMax;
                        }
                        territory.HeightBounds = new ValueTuple<int, int>(minY, maxY);
                        continue;
                    }

                    // ForceSeason=<Spring|Summer|Fall|Winter> / ForceVedrWeather=<environment> (0.0.533, Fire 10-08): line keys,
                    // applied client-side through Vedr's public API while a player is inside (TerritoryRuntime.ApplyVedr).
                    if (flagName.Equals("ForceSeason", StringComparison.OrdinalIgnoreCase)) { territory.ForceSeason = custom; continue; }
                    if (flagName.Equals("ForceVedrWeather", StringComparison.OrdinalIgnoreCase)) { territory.ForceVedrWeather = custom; continue; }

                    // ForSale=<coins> (Fire 10-08): offers the area on the large map ("Buy <Name> for N Coins"); a bare
                    // ForSale is free. Nothing could set Type/SellPrice before, so the purchase path was unreachable.
                    if (flagName.Equals(ForSaleFlag, StringComparison.OrdinalIgnoreCase))
                    {
                        int price = 0;
                        if (!string.IsNullOrWhiteSpace(custom)
                            && (!int.TryParse(custom, NumberStyles.Integer, CultureInfo.InvariantCulture, out price) || price < 0))
                        {
                            WarnFlagProblem(territory, flagsSource, flagsLine, $"invalid ForSale value '{custom}' (whole Coins, 0 or more)");
                            continue;
                        }
                        territory.Type = TerritoryType.ForSale;
                        territory.SellPrice = price;
                        continue;
                    }

                    // JoinOtherServer (kg: send the player to another server on entry) was parsed and never acted on.
                    // Fire 10-08: not supported in 1.0, so it is reported and left off.
                    if (flagName.Equals(nameof(AdditionalTerritoryFlags.JoinOtherServer), StringComparison.OrdinalIgnoreCase))
                    {
                        WarnFlagProblem(territory, flagsSource, flagsLine, "JoinOtherServer is not supported");
                        continue;
                    }

                    if (TryParseAdditionalFlag(flagName, out var addFlag) && addFlag != AdditionalTerritoryFlags.None)
                    {
                        territory.AdditionalFlags |= addFlag;
                        if (!ApplyAdditionalData(territory, addFlag, custom))
                            WarnFlagProblem(territory, flagsSource, flagsLine, $"invalid {flagName} value '{custom}'");
                        continue;
                    }
                    if (TryParseTerritoryFlag(flagName, out var tflag) && tflag != TerritoryFlags.None)
                    {
                        territory.Flags |= tflag;
                        if (!ApplyFlagData(territory, tflag, custom))
                            WarnFlagProblem(territory, flagsSource, flagsLine, $"invalid {flagName} value '{custom}'");
                        continue;
                    }
                    if (!flagName.Equals(NoFlagsMarker, StringComparison.OrdinalIgnoreCase))
                        WarnFlagProblem(territory, flagsSource, flagsLine, $"unknown flag '{flagToken}'");
                }

                // permitted owners line
                territory.Permitted = lines[i + 4].Trim();

                _territories.Add(territory);
            }
        }

        private static bool ApplyFlagData(Territory territory, TerritoryFlags flag, string custom)
        {
            switch (flag)
            {
                case TerritoryFlags.CustomEnvironment:
                    territory.CustomEnvironment = (custom ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    break;
                case TerritoryFlags.PeriodicDamage:
                    return TryAssignOptionalFloat(custom, ref territory.PeriodicDamageValue);
                case TerritoryFlags.PeriodicHeal:
                case TerritoryFlags.PeriodicHealALL:
                    return ApplyPeriodicHeal(territory, custom);
                case TerritoryFlags.IncreasedMonsterDamage:
                    return TryAssignOptionalFloat(custom, ref territory.IncreasedMonsterDamageValue);
                case TerritoryFlags.IncreasedPlayerDamage:
                    return TryAssignOptionalFloat(custom, ref territory.IncreasedPlayerDamageValue);
                case TerritoryFlags.MoveSpeedMultiplier:
                    return TryAssignOptionalFloat(custom, ref territory.MoveSpeedMultiplier);
                case TerritoryFlags.ForceGroundHeight:
                case TerritoryFlags.AddGroundHeight:
                case TerritoryFlags.LimitZoneHeight:
                    return TryAssignOptionalFloat(custom, ref territory.OverridenHeight);
                case TerritoryFlags.ForceBiome:
                    if (int.TryParse(custom, NumberStyles.Integer, CultureInfo.InvariantCulture, out var biomeVal) && IsValidBiomeValue(biomeVal))
                    {
                        territory.OverridenBiome = biomeVal;
                        break;
                    }
                    territory.OverridenBiome = 0;
                    return false;
                case TerritoryFlags.MonstersAddStars:
                    return TryAssignOptionalInt(custom, ref territory.AddMonsterLevel);
                case TerritoryFlags.CustomPaint:
                    if (int.TryParse(custom, out var paintVal))
                    {
                        territory.PaintGround = (PaintType)paintVal;
                    }
                    else if (!string.IsNullOrWhiteSpace(custom) && Enum.TryParse<PaintType>(custom, true, out var paintEnum))
                    {
                        territory.PaintGround = paintEnum;
                    }
                    else
                    {
                        return string.IsNullOrWhiteSpace(custom);
                    }
                    break;
            }
            return true;
        }

        /// <summary>PeriodicHeal / PeriodicHealALL take "amount" (KG form, healed every 1 s tick) or "interval,amount".</summary>
        private static bool ApplyPeriodicHeal(Territory territory, string custom)
        {
            var values = custom.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0) return true;
            if (values.Length > 2) return false;
            if (!float.TryParse(values[values.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var healAmount)) return false;
            if (values.Length == 2)
            {
                if (!float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var interval)) return false;
                territory.PeriodicHealInterval = interval;
            }
            territory.PeriodicHealValue = healAmount;
            return true;
        }

        private static bool TryAssignOptionalFloat(string custom, ref float target)
        {
            if (string.IsNullOrWhiteSpace(custom)) return true;
            if (!float.TryParse(custom, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;
            target = parsed;
            return true;
        }

        private static bool TryAssignOptionalInt(string custom, ref int target)
        {
            if (string.IsNullOrWhiteSpace(custom)) return true;
            if (!int.TryParse(custom, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return false;
            target = parsed;
            return true;
        }

        private static void WarnFlagProblem(Territory territory, string flagsSource, string flagsLine, string problem)
        {
            if (!_loggedFlagProblems.Add($"{territory.Name}|{flagsLine}|{problem}")) return;
            Debug.LogWarning($"[FiresRPGmaker] Territory '{territory.Name}' ({flagsSource}): {problem}, ignoring it. Flags line: \"{flagsLine.Trim()}\"");
        }

        private static bool ApplyAdditionalData(Territory territory, AdditionalTerritoryFlags flag, string custom)
        {
            switch (flag)
            {
                case AdditionalTerritoryFlags.ForceWind:
                    return TryAssignOptionalFloat(custom, ref territory.Wind);
                case AdditionalTerritoryFlags.DropMultiplier:
                    return TryAssignOptionalFloat(custom, ref territory.DropMultiplier);
                case AdditionalTerritoryFlags.Gravity:   // 0.0.498: -10..10, 1 = vanilla
                    if (!TryAssignOptionalFloat(custom, ref territory.Gravity)) return false;
                    return territory.Gravity >= TerritoryRuntime.GravityMin && territory.Gravity <= TerritoryRuntime.GravityMax;
                case AdditionalTerritoryFlags.FirstPerson:   // 0.0.505: "FirstPerson" or "FirstPerson=true"; "=false" clears it
                    if (string.IsNullOrWhiteSpace(custom) || custom.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (custom.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)) { territory.AdditionalFlags &= ~AdditionalTerritoryFlags.FirstPerson; return true; }
                    return false;
                case AdditionalTerritoryFlags.Scale:     // 0.0.498: 0.25..4, 1 = normal
                    if (!TryAssignOptionalFloat(custom, ref territory.Scale)) return false;
                    return territory.Scale >= TerritoryRuntime.ScaleMin && territory.Scale <= TerritoryRuntime.ScaleMax;
                case AdditionalTerritoryFlags.OnlyForGuild:
                    territory.OnlyForGuild = custom;
                    break;
                case AdditionalTerritoryFlags.OnlyForFaction:
                    territory.OnlyForFaction = custom;
                    break;
                case AdditionalTerritoryFlags.EWPSpawner:
                     if (!string.IsNullOrEmpty(custom)) territory.CustomSpawnerNames.Add(custom);
                     var def = ParseCustomSpawnDef(custom);
                     if (def != null) territory.CustomSpawners.Add(def);
                     break;
                case AdditionalTerritoryFlags.JoinOtherServer:
                    territory.JoinOtherServerString = custom;
                    break;
                case AdditionalTerritoryFlags.RevealOnMap:
                    // RevealOnMap supports: "radius" or "x,z,radius" (comma separated). Empty uses territory radius/center.
                    if (string.IsNullOrWhiteSpace(custom))
                    {
                        territory.RevealHasCustomCenter = false;
                        territory.RevealRadius = territory.Radius;
                        break;
                    }
                    var parts = custom.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1)
                    {
                        float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out territory.RevealRadius);
                    }
                    else if (parts.Length >= 2)
                    {
                        territory.RevealHasCustomCenter = true;
                        float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out territory.RevealCenterX);
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out territory.RevealCenterZ);
                        if (parts.Length >= 3)
                            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out territory.RevealRadius);
                    }
                    if (territory.RevealRadius <= 0f)
                    {
                        territory.RevealRadius = territory.Radius > 0 ? territory.Radius : 50f;
                    }
                    break;
            }
            return true;
        }

        private static void WriteSampleIfMissing()
        {
            if (File.Exists(LegacySamplePath)) return;
            try
            {
                Directory.CreateDirectory(LegacyDir);
                var sample = "" +
                             "[starter_zone]\n" +
                             "Circle\n" +
                             "0,0,50\n" +
                             "0,255,0,true\n" +
                             "NoBuild,NoPickaxe,MoveSpeedMultiplier=1.2\n" +
                             "ALL\n";
                File.WriteAllText(LegacySamplePath, sample);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to write sample territory: {ex.Message}");
            }
        }

        private static bool TryParseTerritoryFlag(string name, out TerritoryFlags flag)
        {
            if (Enum.TryParse<TerritoryFlags>(name, true, out flag)) return true;
            var normalized = (name ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            switch (normalized)
            {
                case "periodichealall":
                case "periodicheallall":
                    flag = TerritoryFlags.PeriodicHealALL;
                    return true;
            }
            flag = TerritoryFlags.None;
            return false;
        }

        private static bool IsValidBiomeValue(int val)
        {
            // Heightmap.Biome defined values we support
            switch (val)
            {
                case 1: // Meadows
                case 2: // Swamp
                case 4: // Mountain
                case 8: // BlackForest
                case 16: // Plains
                case 32: // AshLands
                case 64: // DeepNorth
                case 256: // Ocean
                case 512: // Mistlands
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Splits on '|', ';' and ','. Inside a Flag=value a comma only splits when the text after it names a flag,
        /// so multi-value flags (HeightBounds=0,600, PeriodicHeal=1,5, CustomEnvironment=a,b) keep their commas.
        /// </summary>
        private static List<string> SplitFlagTokens(string flagsLine)
        {
            var tokens = new List<string>();
            int tokenStart = 0;
            bool insideValue = false;
            for (int index = 0; index < flagsLine.Length; index++)
            {
                char character = flagsLine[index];
                if (character == '=')
                {
                    insideValue = true;
                    continue;
                }
                bool endsToken = character == '|' || character == ';'
                    || (character == ',' && (!insideValue || NextSegmentNamesFlag(flagsLine, index + 1)));
                if (!endsToken) continue;
                AddFlagToken(tokens, flagsLine.Substring(tokenStart, index - tokenStart));
                tokenStart = index + 1;
                insideValue = false;
            }
            AddFlagToken(tokens, flagsLine.Substring(tokenStart));
            return tokens;
        }

        private static void AddFlagToken(List<string> tokens, string token)
        {
            token = token.Trim();
            if (token.Length > 0) tokens.Add(token);
        }

        private static bool NextSegmentNamesFlag(string flagsLine, int segmentStart)
        {
            int segmentEnd = flagsLine.IndexOfAny(FlagSegmentTerminators, segmentStart);
            if (segmentEnd < 0) segmentEnd = flagsLine.Length;
            return IsFlagName(flagsLine.Substring(segmentStart, segmentEnd - segmentStart).Trim());
        }

        /// <summary>Letter-first because Enum.TryParse also accepts numbers ("20" parses as NoAttack|NoPickaxe).</summary>
        private static bool IsFlagName(string candidate)
        {
            if (candidate.Length == 0 || !char.IsLetter(candidate[0])) return false;
            if (candidate.Equals(HeightBoundsFlag, StringComparison.OrdinalIgnoreCase)) return true;
            if (candidate.Equals(ForSaleFlag, StringComparison.OrdinalIgnoreCase)) return true;
            if (candidate.Equals("ForceSeason", StringComparison.OrdinalIgnoreCase) || candidate.Equals("ForceVedrWeather", StringComparison.OrdinalIgnoreCase)) return true;
            return (TryParseAdditionalFlag(candidate, out var additionalFlag) && additionalFlag != AdditionalTerritoryFlags.None)
                || (TryParseTerritoryFlag(candidate, out var territoryFlag) && territoryFlag != TerritoryFlags.None);
        }

        private static CustomSpawnDef ParseCustomSpawnDef(string custom)
        {
            if (string.IsNullOrWhiteSpace(custom)) return null;

            // strip leading flag if present (EWPSpawner=...)
            int eq = custom.IndexOf('=');
            if (eq >=0 && eq < custom.Length -1)
            {
                // Only strip a single leading token if it looks like a flag name (no commas before '=')
                var prefix = custom.Substring(0, eq);
                if (!prefix.Contains(",") && !prefix.Contains(";"))
                custom = custom.Substring(eq +1);
            }

            var def = new CustomSpawnDef();
            def.Interval =300f;
            def.Amount =1;
            def.Level =1;

            var positional = new List<string>();

            // Tokenize by comma/semicolon but keep order. We'll treat key=value tokens specially and also support "key, value" style.
            var raw = custom.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();

            for (int i =0; i < raw.Count; i++)
            {
                var tok = raw[i];
                if (string.IsNullOrEmpty(tok)) continue;

                // xyz can be provided as "xyz=32,32,341" or as "xyz,32,32,341"
                if (tok.StartsWith("xyz=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = tok.Substring(tok.IndexOf('=') +1);
                    // If the value already contains commas, split it; otherwise try to take next two tokens
                    var parts = val.Split(new[] { ',' , ';' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length>0).ToList();
                    if (parts.Count <3)
                    {
                        if (i +2 < raw.Count)
                        {
                            parts = new List<string> { val };
                            parts.Add(raw[i +1]);
                            parts.Add(raw[i +2]);
                            i +=2;
                        }
                    }

                    if (parts.Count >=3
                        && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                        && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
                        && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                    {
                        def.HasPosition = true;
                        def.Position = new Vector3(x, y, z);
                    }
                    continue;
                }

                // support "xyz" as separate key followed by three tokens
                if (string.Equals(tok, "xyz", StringComparison.OrdinalIgnoreCase) && i +3 < raw.Count)
                {
                    if (float.TryParse(raw[i +1], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                        && float.TryParse(raw[i +2], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                        && float.TryParse(raw[i +3], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    {
                        def.HasPosition = true;
                        def.Position = new Vector3(x, y, z);
                        i +=3;
                        continue;
                    }
                }

                // data can be "data=Name" or "data,Name"
                if (tok.StartsWith("data=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = tok.Substring(tok.IndexOf('=') +1).Trim();
                    def.DataName = val;
                    continue;
                }
                if (string.Equals(tok, "data", StringComparison.OrdinalIgnoreCase) && i +1 < raw.Count)
                {
                    def.DataName = raw[i +1];
                    i +=1;
                    continue;
                }

                // respawn interval: respawn=SECONDS or respawn,SECONDS
                if (tok.StartsWith("respawn=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = tok.Substring(tok.IndexOf('=') +1).Trim();
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var respawnSec))
                    def.Interval = Math.Max(1f, respawnSec);
                    continue;
                }
                if (string.Equals(tok, "respawn", StringComparison.OrdinalIgnoreCase) && i +1 < raw.Count)
                {
                    if (float.TryParse(raw[i +1], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var respawnSec))
                    {
                        def.Interval = Math.Max(1f, respawnSec);
                    }
                    i +=1;
                    continue;
                }

                // generic key=value fallback: if key recognized, consume; otherwise treat as positional
                int kv = tok.IndexOf('=');
                if (kv >0 && kv < tok.Length -1)
                {
                    var key = tok.Substring(0, kv).Trim().ToLowerInvariant();
                    var val = tok.Substring(kv +1).Trim();
                    switch (key)
                    {
                        case "xyz":
                            var xyzParts = val.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                            if (xyzParts.Length >=3
                                && float.TryParse(xyzParts[0], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var xx)
                                && float.TryParse(xyzParts[1], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var yy)
                                && float.TryParse(xyzParts[2], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var zz))
                            {
                                def.HasPosition = true;
                                def.Position = new Vector3(xx, yy, zz);
                            }
                            break;
                        case "data":
                            def.DataName = val;
                            break;
                        case "respawn":
                            if (float.TryParse(val, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var rsec))
                            def.Interval = Math.Max(1f, rsec);
                            break;
                        default:
                            // unknown key -> treat as positional token so older configs still work
                            positional.Add(tok);
                            break;
                    }
                }
                else
                {
                    // plain token -> positional
                    positional.Add(tok);
                }
            }

            // Now map positional: prefab, amount, level
            if (positional.Count ==0)
            return null;

            def.Prefab = positional[0];
            if (positional.Count >=2 && int.TryParse(positional[1], System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var amt))
            {
                def.Amount = Math.Max(1, amt);
            }
            if (positional.Count >=3 && int.TryParse(positional[2], System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl))
            {
                def.Level = Math.Max(1, lvl);
            }

            if (string.IsNullOrEmpty(def.DataName))
            def.DataName = string.Empty;

            return def;
        }

        private static bool TryParseAdditionalFlag(string name, out AdditionalTerritoryFlags flag)
        {
            if (Enum.TryParse<AdditionalTerritoryFlags>(name, true, out flag) && flag != AdditionalTerritoryFlags.None)
                return true;

            var normalized = (name ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            switch (normalized)
            {
                case "ewpspawner":
                case "ewpspawn":
                case "customspawner":
                case "customjerespawner":
                case "jerespawner":
                case "ewpspawnerflag":
                    flag = AdditionalTerritoryFlags.EWPSpawner;
                    return true;
            }
            flag = AdditionalTerritoryFlags.None;
            return false;
        }
    }
}
