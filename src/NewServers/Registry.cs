using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NewServers
{
    internal sealed class Variant
    {
        public string Key;          // "blue-3u-gen2"; stable, used in save sidecars
        public string Label;        // "RISC 3U Gen 2"; for logs and messages
        public int Gen;
        public int SizeInU;
        public int TwinIndex;       // vanilla serverPrefabs index this upgrades
        public int Index;           // our serverPrefabs index, also the shop itemID
        public int Iops;
        public int Price;
        public int Xp;
        public float MaxProcessingSpeed;
        public GameObject Prefab;
        public ShopItemSO SO;
        public ShopItemSO TwinSO;
    }

    // Clones each vanilla server prefab into Gen 2/3/4 templates and appends them to
    // MainGameManager.serverPrefabs, so the game's own buy/spawn/rack/repair code handles them.
    internal static class Registry
    {
        public static readonly List<Variant> Variants = new();
        static readonly Dictionary<int, Variant> ByIndexMap = new();
        static readonly Dictionary<string, Variant> ByKeyMap = new();
        static GameObject _holder;
        static int _vanillaCount = -1;

        static readonly Dictionary<string, string> FamilyLabels = new()
        {
            ["Yellow"] = "System X",
            ["Blue"] = "RISC",
            ["Purple"] = "Mainframe",
            ["Green"] = "GPU",
        };

        public static bool Ready => Variants.Count > 0;
        public static int VanillaCount => _vanillaCount;
        public static Transform Holder => _holder == null ? null : _holder.transform;

        public static Variant ByIndex(int index) => ByIndexMap.TryGetValue(index, out var v) ? v : null;
        public static Variant ByKey(string key) => key != null && ByKeyMap.TryGetValue(key, out var v) ? v : null;

        public static Variant Previous(Variant v) =>
            Variants.FirstOrDefault(x => x.TwinIndex == v.TwinIndex && x.Gen == v.Gen - 1);

        // Returns true when the variants are registered on the current MainGameManager.
        public static bool EnsureRegistered(string reason)
        {
            var mgm = MainGameManager.instance;
            if (mgm == null) return false;
            var prefabs = mgm.serverPrefabs;
            if (prefabs == null) return false;

            if (!Ready)
            {
                _vanillaCount = prefabs.Length;
                BuildVariants(prefabs);
                LogTable();
            }

            if (prefabs.Length == _vanillaCount + Variants.Count && IsOurs(prefabs[Variants[0].Index]))
                return true;

            if (prefabs.Length != _vanillaCount)
            {
                NewServersMod.Log.Error($"serverPrefabs has {prefabs.Length} entries, expected {_vanillaCount}. " +
                                        "Another mod or a game update changed it; New Servers is not registering.");
                return false;
            }

            var extended = new Il2CppReferenceArray<GameObject>(_vanillaCount + Variants.Count);
            for (int i = 0; i < _vanillaCount; i++) extended[i] = prefabs[i];
            foreach (var v in Variants) extended[v.Index] = v.Prefab;
            mgm.serverPrefabs = extended;
            NewServersMod.Log.Msg($"Registered {Variants.Count} server variants at indices {_vanillaCount}-{_vanillaCount + Variants.Count - 1} ({reason}).");
            return true;
        }

        static bool IsOurs(GameObject go) =>
            go != null && Variants.Any(v => v.Prefab != null && v.Prefab.GetInstanceID() == go.GetInstanceID());

        static void BuildVariants(Il2CppReferenceArray<GameObject> prefabs)
        {
            // Templates live under an inactive holder so Awake/Start never run on them; the game's own
            // Instantiate calls produce active copies that initialise normally.
            _holder = new GameObject("NewServers.Templates");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);

            var vanilla = new List<(int index, GameObject go, Server server)>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                var go = prefabs[i];
                var s = go == null ? null : go.GetComponent<Server>();
                if (s == null || s.shopItemSO == null || s.maxProcessingSpeed <= 0f) continue;
                vanilla.Add((i, go, s));
            }

            static double PricePerIops(Server s) => s.shopItemSO.price / (s.maxProcessingSpeed * Balance.IopsUnit);

            // Per family (serverType): its smallest and biggest vanilla server.
            var families = vanilla
                .GroupBy(x => x.server.serverType)
                .ToDictionary(g => g.Key, g => (
                    small: g.OrderBy(x => x.server.sizeInU).First().server,
                    big: g.OrderByDescending(x => x.server.sizeInU).First().server));

            int next = prefabs.Length;
            foreach (var gen in Balance.Gens)
                foreach (var (index, go, s) in vanilla)
                {
                    var color = ColorOf(go.name);
                    var family = FamilyLabels.TryGetValue(color, out var label) ? label : color;
                    var (small, big) = families[s.serverType];
                    bool bigger = s.sizeInU > small.sizeInU;
                    int iops = Balance.Iops(gen, s.sizeInU, bigger);

                    // Money: the vanilla twin's price per IOPS, never less than a premium over the family's small server.
                    double pricePerIops = bigger
                        ? Math.Max(PricePerIops(s), PricePerIops(small) * Balance.BigServerMinPricePremium)
                        : PricePerIops(s);

                    // XP per IOPS comes from the family's biggest vanilla server: the smallest ones are starter
                    // items (one unlocks at 0 XP), so their rate would make upgrades nearly free.
                    double xpPerIops = big.shopItemSO.xpToUnlock / (big.maxProcessingSpeed * Balance.IopsUnit);

                    var v = new Variant
                    {
                        Key = $"{color.ToLowerInvariant()}-{s.sizeInU}u-gen{gen}",
                        Label = $"{family} {s.sizeInU}U Gen {gen}",
                        Gen = gen,
                        SizeInU = s.sizeInU,
                        TwinIndex = index,
                        Index = next++,
                        Iops = iops,
                        Price = Balance.RoundTo10(iops * pricePerIops * Balance.Premium(gen)),
                        Xp = Balance.RoundTo10(iops * xpPerIops * Balance.Premium(gen)),
                        MaxProcessingSpeed = iops / Balance.IopsUnit,
                        TwinSO = s.shopItemSO,
                    };

                    v.SO = Object.Instantiate(s.shopItemSO);
                    v.SO.name = $"ShopItemSO_NewServers_{v.Key}";
                    v.SO.itemID = v.Index;
                    v.SO.price = v.Price;
                    v.SO.xpToUnlock = v.Xp;
                    v.SO.hideFlags = HideFlags.DontUnloadUnusedAsset;

                    v.Prefab = Object.Instantiate(go, _holder.transform);
                    v.Prefab.name = $"{go.name}.Gen{gen}";
                    var cs = v.Prefab.GetComponent<Server>();
                    cs.maxProcessingSpeed = v.MaxProcessingSpeed;
                    cs.prefabID = v.Index;
                    cs.shopItemSO = v.SO;
                    Ports.Configure(cs, gen);

                    Variants.Add(v);
                    ByIndexMap[v.Index] = v;
                    ByKeyMap[v.Key] = v;
                }
        }

        // "Server.Blue1" -> "Blue"
        static string ColorOf(string prefabName)
        {
            var name = prefabName.StartsWith("Server.") ? prefabName.Substring("Server.".Length) : prefabName;
            return name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        }

        static void LogTable()
        {
            NewServersMod.Log.Msg($"{"variant",-22} {"index",5} {"twin",4} {"IOPS",7} {"price",7} {"XP",7}");
            foreach (var v in Variants.OrderBy(x => x.TwinIndex).ThenBy(x => x.Gen))
                NewServersMod.Log.Msg($"{v.Label,-22} {v.Index,5} {v.TwinIndex,4} {v.Iops,7} {v.Price,7} {v.Xp,7}");
        }

        // Postfix on Server.Start and Server.ServerInsertedInRack: keep our IOPS in case the game
        // resets it from its own tables. Logs once if that ever happens.
        static bool _resetLogged;
        public static void ServerPostfix(Server __instance)
        {
            try
            {
                var v = ByIndex(__instance.prefabID);
                if (v == null || Mathf.Approximately(__instance.maxProcessingSpeed, v.MaxProcessingSpeed)) return;
                if (!_resetLogged)
                {
                    _resetLogged = true;
                    NewServersMod.Log.Warning($"{v.Label}: game set maxProcessingSpeed to {__instance.maxProcessingSpeed}, restoring {v.MaxProcessingSpeed}.");
                }
                __instance.maxProcessingSpeed = v.MaxProcessingSpeed;
            }
            catch (Exception e) { NewServersMod.Log.Error($"ServerPostfix: {e.Message}"); }
        }
    }
}
