using System;
using HarmonyLib;
using Il2Cpp;

namespace NewServers
{
    internal static class Patching
    {
        public static void Install(HarmonyLib.Harmony h)
        {
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.SaveGame), typeof(SaveCompat), nameof(SaveCompat.SaveGamePrefix), nameof(SaveCompat.SaveGamePostfix));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.WriteMeta), typeof(SaveCompat), nameof(SaveCompat.WriteMetaPrefix), null);
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.LoadGame), typeof(SaveCompat), null, nameof(SaveCompat.LoadGamePostfix));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.MoveSaveFiles), typeof(SaveCompat), null, nameof(SaveCompat.MoveSaveFilesPostfix));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.DeleteSaveFile), typeof(SaveCompat), null, nameof(SaveCompat.DeleteSaveFilePostfix));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.DeleteSaveFilesQuiet), typeof(SaveCompat), null, nameof(SaveCompat.DeleteSaveFilePostfix));

            Patch(h, typeof(Server), nameof(Server.Start), typeof(Registry), null, nameof(Registry.ServerPostfix));
            Patch(h, typeof(Server), nameof(Server.ServerInsertedInRack), typeof(Registry), null, nameof(Registry.ServerPostfix));

            Patch(h, typeof(ShopItem), nameof(ShopItem.Start), typeof(ShopCards), null, nameof(ShopCards.RefreshTextPostfix));
            Patch(h, typeof(ShopItem), nameof(ShopItem.UpdateVisualState), typeof(ShopCards), null, nameof(ShopCards.RefreshTextPostfix));
            Patch(h, typeof(ShopItem), nameof(ShopItem.ButtonBuyItem), typeof(ShopCards), nameof(ShopCards.UnlockGatePrefix), null);
            Patch(h, typeof(ShopItem), nameof(ShopItem.TryUnlock), typeof(ShopCards), nameof(ShopCards.UnlockGatePrefix), null);
        }

        static void Patch(HarmonyLib.Harmony h, Type type, string method, Type patchType, string prefix, string postfix)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    NewServersMod.Log.Error($"Patch target {type.Name}.{method} not found; the game may have changed.");
                    return;
                }
                h.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(patchType, prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(patchType, postfix)));
            }
            catch (Exception e)
            {
                NewServersMod.Log.Error($"Patching {type.Name}.{method} failed: {e.Message}");
            }
        }
    }
}
