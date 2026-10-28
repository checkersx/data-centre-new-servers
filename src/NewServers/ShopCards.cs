using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NewServers
{
    // Clones each vanilla server's shop card into the same family row, one per Gen.
    // Unlock state saves natively: the game keeps a guid -> unlocked map and ignores unknown guids.
    internal static class ShopCards
    {
        const string GuidPrefix = "newservers-";

        static readonly Dictionary<string, ShopItem> CardsByKey = new();
        static readonly Dictionary<int, ShopItem> VanillaCardsByIndex = new();
        static bool _textsLogged;

        public static void EnsureCards()
        {
            var shop = MainGameManager.instance?.computerShop;
            var items = shop?.shopItems;
            if (items == null) return;

            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].guid != null && items[i].guid.StartsWith(GuidPrefix))
                    return; // this shop already has our cards

            CardsByKey.Clear();
            VanillaCardsByIndex.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                var so = items[i] == null ? null : items[i].shopItemSO;
                if (so != null && IsServer(so.itemType) && so.itemID < Registry.VanillaCount)
                    VanillaCardsByIndex[so.itemID] = items[i];
            }

            var added = new List<ShopItem>();
            // Gen-major order: each family row reads vanilla 3U, 7U, Gen 2 3U, Gen 2 7U, Gen 3 ...
            foreach (var v in Registry.Variants.OrderBy(x => x.Gen).ThenBy(x => x.TwinIndex))
            {
                if (!VanillaCardsByIndex.TryGetValue(v.TwinIndex, out var vanilla))
                {
                    NewServersMod.Log.Warning($"No vanilla shop card for {v.Label}; it will not be sold.");
                    continue;
                }

                // Clone under the inactive holder so Awake sees our SO and guid, then move into the row.
                var go = Object.Instantiate(vanilla.gameObject, Registry.Holder);
                go.name = $"ShopCard.NewServers.{v.Key}";
                var card = go.GetComponent<ShopItem>();
                card.shopItemSO = v.SO;
                card.guid = GuidPrefix + v.Key;
                card.isUnlocked = false;
                card.itemDisplayName = v.Label;
                go.transform.SetParent(vanilla.transform.parent, false);
                go.transform.SetAsLastSibling();

                CardsByKey[v.Key] = card;
                added.Add(card);
            }

            var extended = new Il2CppReferenceArray<ShopItem>(items.Length + added.Count);
            for (int i = 0; i < items.Length; i++) extended[i] = items[i];
            for (int i = 0; i < added.Count; i++) extended[items.Length + i] = added[i];
            shop.shopItems = extended;
            NewServersMod.Log.Msg($"Added {added.Count} shop cards.");
        }

        static bool IsServer(PlayerManager.ObjectInHand t) =>
            t == PlayerManager.ObjectInHand.Server1U || t == PlayerManager.ObjectInHand.Server2U || t == PlayerManager.ObjectInHand.Server3U;

        static Variant VariantOf(ShopItem card)
        {
            var guid = card == null ? null : card.guid;
            return guid != null && guid.StartsWith(GuidPrefix) ? Registry.ByKey(guid.Substring(GuidPrefix.Length)) : null;
        }

        // Postfix on ShopItem.Start / UpdateVisualState: the game fills the name from the vanilla
        // card's text, so rewrite it to show the Gen and our IOPS.
        public static void RefreshTextPostfix(ShopItem __instance)
        {
            try
            {
                var v = VariantOf(__instance);
                if (v == null || __instance.txtName == null) return;
                VanillaCardsByIndex.TryGetValue(v.TwinIndex, out var vanilla);
                var vanillaText = vanilla?.txtName?.text ?? "";
                if (!_textsLogged && vanilla != null)
                {
                    _textsLogged = true;
                    NewServersMod.Log.Msg($"Vanilla card text: name='{vanillaText}' price='{vanilla.txtPrice?.text}' xp='{vanilla.txtXpToUnlock?.text}'");
                }
                var name = MakeName(vanillaText, v);
                __instance.txtName.text = name;
                __instance.itemDisplayName = name;
            }
            catch (Exception e) { NewServersMod.Log.Error($"RefreshTextPostfix: {e.Message}"); }
        }

        // Swap any vanilla IOPS figure in the card name for ours, then add the Gen.
        static string MakeName(string vanillaText, Variant v)
        {
            if (string.IsNullOrWhiteSpace(vanillaText)) return v.Label;
            int vanillaIops = VanillaIops(v);
            var text = vanillaText;
            bool replaced = false;
            foreach (var (from, to) in IopsForms(vanillaIops, v.Iops))
                if (text.Contains(from)) { text = text.Replace(from, to); replaced = true; break; }
            return replaced ? $"{text} Gen {v.Gen}" : $"{text} Gen {v.Gen} ({v.Iops:N0} IOPS)";
        }

        static int VanillaIops(Variant v)
        {
            var prefab = MainGameManager.instance?.serverPrefabs?[v.TwinIndex];
            var s = prefab == null ? null : prefab.GetComponent<Server>();
            return s == null ? 0 : (int)Math.Round(s.maxProcessingSpeed * Balance.IopsUnit);
        }

        static IEnumerable<(string, string)> IopsForms(int from, int to)
        {
            if (from <= 0) yield break;
            var inv = CultureInfo.InvariantCulture;
            yield return (from.ToString("N0", inv), to.ToString("N0", inv));                          // 5,000
            yield return (from.ToString("N0", inv).Replace(',', ' '), to.ToString("N0", inv).Replace(',', ' ')); // 5 000
            yield return (from.ToString("N0", inv).Replace(',', '.'), to.ToString("N0", inv).Replace(',', '.')); // 5.000
            yield return (from.ToString(inv), to.ToString(inv));                                    // 5000
            if (from % 1000 == 0) yield return ($"{from / 1000}K", $"{to / 1000}K");
            if (from % 1000 == 0) yield return ($"{from / 1000}k", $"{to / 1000}k");
        }

        // Prefix on ShopItem.ButtonBuyItem / TryUnlock: a Gen can only be unlocked once the Gen below
        // it (or the vanilla server, for Gen 2) is unlocked.
        public static bool UnlockGatePrefix(ShopItem __instance)
        {
            try
            {
                var v = VariantOf(__instance);
                if (v == null || __instance.isUnlocked) return true;

                ShopItem below;
                string belowName;
                var prev = Registry.Previous(v);
                if (prev == null)
                {
                    VanillaCardsByIndex.TryGetValue(v.TwinIndex, out below);
                    belowName = below?.txtName?.text ?? "the standard server";
                }
                else
                {
                    CardsByKey.TryGetValue(prev.Key, out below);
                    belowName = prev.Label;
                }

                if (below == null || below.isUnlocked) return true;
                StaticUIElements.instance?.AddMeesageInField($"Unlock {belowName} first.");
                return false;
            }
            catch (Exception e)
            {
                NewServersMod.Log.Error($"UnlockGatePrefix: {e.Message}");
                return true;
            }
        }
    }
}
