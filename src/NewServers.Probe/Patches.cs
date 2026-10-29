using System;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using static NewServers.Probe.ProbeMod;

namespace NewServers.Probe
{
    // Logging-only Harmony patches. Every body is wrapped so nothing can throw into game code.
    // A patch that never logs is also an answer: the method may be inlined by IL2CPP.
    internal static class Patches
    {
        internal static bool ShopDumped;
        static int _getServerPrefabCalls, _shopPrefabCalls, _mgmPrefabCalls;
        const int CallLogLimit = 200;

        public static void Install(HarmonyLib.Harmony h)
        {
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.SaveGame), nameof(SaveGamePre), nameof(SaveGamePost));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.SaveGameData), nameof(SaveGameDataPre), nameof(SaveGameDataPost));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.SerializeToBytes), nameof(SerializePre), null);
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.WriteMeta), nameof(WriteMetaPre), null);
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.AutoSave), nameof(AutoSavePre), null);
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.LoadGame), nameof(LoadGamePre), nameof(LoadGamePost));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.Load), nameof(LoadPre), nameof(LoadPost));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.LoadGameData), nameof(LoadGameDataPre), nameof(LoadGameDataPost));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.LoadFromBytes), nameof(LoadFromBytesPre), null);
            Patch(h, typeof(ComputerShop), nameof(ComputerShop.UnlockFromSave), nameof(UnlockFromSavePre), null);
            Patch(h, typeof(ComputerShop), nameof(ComputerShop.ButtonShopScreen), null, nameof(ShopScreenPost));
            Patch(h, typeof(ComputerShop), nameof(ComputerShop.ButtonBuyShopItem), nameof(BuyPre), null);
            Patch(h, typeof(ComputerShop), nameof(ComputerShop.SpawnPhysicalItem), nameof(SpawnPre), null);
            Patch(h, typeof(ComputerShop), nameof(ComputerShop.GetPrefabForItem), null, nameof(ShopPrefabForItemPost));
            Patch(h, typeof(MainGameManager), nameof(MainGameManager.GetServerPrefab), null, nameof(GetServerPrefabPost));
            Patch(h, typeof(MainGameManager), nameof(MainGameManager.GetPrefabForItem), null, nameof(MgmPrefabForItemPost));
            Patch(h, typeof(Server), nameof(Server.ServerInsertedInRack), nameof(InsertedPre), null);
            Patch(h, typeof(ShopItem), nameof(ShopItem.TryUnlock), nameof(TryUnlockPre), nameof(TryUnlockPost));
            Patch(h, typeof(ShopItem), nameof(ShopItem.UnlockButton), nameof(UnlockButtonPre), null);
            Patch(h, typeof(TechnicianManager), nameof(TechnicianManager.SendTechnician), nameof(SendTechPre), null);
            Patch(h, typeof(CableLink), nameof(CableLink.InteractOnHover), null, nameof(PortHoverPost));

            // Is the save's SaveData visible at serialisation time? (A fallback place to edit save data.)
            Patch(h, typeof(Il2CppSystem.Runtime.Serialization.Formatters.Binary.BinaryFormatter), "Serialize", nameof(FormatterSerializePre), null,
                new[] { typeof(Il2CppSystem.IO.Stream), typeof(Il2CppSystem.Object) });
        }

        static void Patch(HarmonyLib.Harmony h, Type type, string method, string prefix, string postfix, Type[] args = null)
        {
            try
            {
                var target = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
                if (target == null) { Log($"patch {type.Name}.{method}: not found", console: true); return; }
                h.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), postfix)));
                Log($"patched {type.Name}.{method}");
            }
            catch (Exception e) { Log($"patch {type.Name}.{method} failed: {e.Message}", console: true); }
        }

        static void Guard(string name, Action body)
        {
            try { body(); }
            catch (Exception e) { Log($"!!! {name}: {e}"); }
        }

        // ---- port hover outline (once per server port) ----

        static readonly System.Collections.Generic.HashSet<int> HoveredPorts = new();

        static void PortHoverPost(CableLink __instance) => Guard("PortHoverPost", () =>
        {
            var server = __instance.parentServer;
            if (server == null || !HoveredPorts.Add(__instance.GetInstanceID())) return;
            Log($">> hover port '{__instance.name}' on '{server.name}' prefabID={server.prefabID} sfpPort={__instance.isSFPPort} " +
                $"inserted={__instance.sfpTypeInserted} lrot={__instance.transform.localEulerAngles}", console: true);
            Dumps.Outline(__instance, server.transform);
        });

        // ---- save / load pipeline ----

        static void SaveGamePre(string __0, string __1) => Guard("SaveGamePre", () =>
        {
            Log($">> SaveSystem.SaveGame('{__0}', '{__1}')", console: true);
            Dumps.Save("SaveGame enter", SaveData.instance);
        });

        static void SaveGamePost() => Guard("SaveGamePost", () =>
        {
            Dumps.Save("SaveGame exit", SaveData.instance);
            Log("<< SaveSystem.SaveGame");
            PendingLiveDump = true;
        });

        static void SaveGameDataPre() => Guard("SaveGameDataPre", () =>
        {
            Log(">> SaveSystem.SaveGameData()", console: true);
            Dumps.Save("SaveGameData enter", SaveData.instance);
        });

        static void SaveGameDataPost() => Guard("SaveGameDataPost", () =>
        {
            Dumps.Save("SaveGameData exit", SaveData.instance);
            Log("<< SaveSystem.SaveGameData");
        });

        static void SerializePre(SaveData __0) => Guard("SerializePre", () =>
        {
            Log(">> SaveSystem.SerializeToBytes", console: true);
            Dumps.Save("SerializeToBytes", __0);
        });

        static void WriteMetaPre(string __0, int __1, string __2) =>
            Guard("WriteMetaPre", () => Log($">> SaveSystem.WriteMeta('{__0}', {__1}, '{__2}')"));

        static void AutoSavePre() => Guard("AutoSavePre", () => Log(">> SaveSystem.AutoSave()", console: true));

        static void LoadGamePre(string __0) => Guard("LoadGamePre", () => Log($">> SaveSystem.LoadGame('{__0}')", console: true));

        static void LoadGamePost(SaveData __result) => Guard("LoadGamePost", () =>
        {
            Dumps.Save("LoadGame result", __result);
            Log("<< SaveSystem.LoadGame");
        });

        static void LoadPre(string __0, bool __1) => Guard("LoadPre", () =>
        {
            Log($">> SaveSystem.Load('{__0}', {__1})", console: true);
            Dumps.Save("Load enter", SaveData.instance);
        });

        static void LoadPost() => Guard("LoadPost", () =>
        {
            Dumps.Save("Load exit", SaveData.instance);
            Log("<< SaveSystem.Load");
        });

        static void LoadGameDataPre() => Guard("LoadGameDataPre", () =>
        {
            Log(">> SaveSystem.LoadGameData()", console: true);
            Dumps.Save("LoadGameData enter", SaveData.instance);
        });

        static void LoadGameDataPost() => Guard("LoadGameDataPost", () =>
        {
            Dumps.Save("LoadGameData exit", SaveData.instance);
            Log("<< SaveSystem.LoadGameData");
        });

        static void LoadFromBytesPre() => Guard("LoadFromBytesPre", () => Log(">> SaveSystem.LoadFromBytes", console: true));

        static void UnlockFromSavePre(Il2CppSystem.Collections.Generic.Dictionary<string, bool> __0) =>
            Guard("UnlockFromSavePre", () => Log($">> ComputerShop.UnlockFromSave(count={(__0 == null ? "null" : __0.Count.ToString())})"));

        // ---- shop / spawn / rack ----

        static void ShopScreenPost() => Guard("ShopScreenPost", () =>
        {
            Log(">> ComputerShop.ButtonShopScreen", console: true);
            if (ShopDumped) return;
            ShopDumped = true;
            Dumps.Safe("shop cards (shop opened)", Dumps.ShopCards);
        });

        static void BuyPre(int __0, int __1, PlayerManager.ObjectInHand __2, string __3, bool __4) =>
            Guard("BuyPre", () => Log($">> ComputerShop.ButtonBuyShopItem(itemID={__0}, price={__1}, type={__2}, name='{__3}', flag={__4})", console: true));

        static void SpawnPre(GameObject __0, int __1, PlayerManager.ObjectInHand __2) => Guard("SpawnPre", () =>
        {
            var s = __0 == null ? null : __0.GetComponent<Server>();
            var serverText = s == null ? "" : $" server prefabID={s.prefabID} type={s.serverType} size={s.sizeInU}U max={s.maxProcessingSpeed}";
            Log($">> ComputerShop.SpawnPhysicalItem(prefab={Dumps.N(__0)}, arg={__1}, type={__2}){serverText}", console: true);
        });

        static void ShopPrefabForItemPost(int __0, PlayerManager.ObjectInHand __1, GameObject __result) => Guard("ShopPrefabForItemPost", () =>
        {
            if (++_shopPrefabCalls <= CallLogLimit)
                Log($"   ComputerShop.GetPrefabForItem(itemID={__0}, type={__1}) -> {Dumps.N(__result)}");
        });

        static void GetServerPrefabPost(int __0, GameObject __result) => Guard("GetServerPrefabPost", () =>
        {
            if (++_getServerPrefabCalls <= CallLogLimit)
                Log($"   MainGameManager.GetServerPrefab({__0}) -> {Dumps.N(__result)}");
        });

        static void MgmPrefabForItemPost(PlayerManager.ObjectInHand __0, int __1, GameObject __result) => Guard("MgmPrefabForItemPost", () =>
        {
            if (++_mgmPrefabCalls <= CallLogLimit)
                Log($"   MainGameManager.GetPrefabForItem(type={__0}, id={__1}) -> {Dumps.N(__result)}");
        });

        static void InsertedPre(Server __instance, ServerSaveData __0) => Guard("InsertedPre", () =>
            Log($">> Server.ServerInsertedInRack id={__instance.ServerID} prefabID={__instance.prefabID} type={__instance.serverType} " +
                $"size={__instance.sizeInU}U max={__instance.maxProcessingSpeed} fromSave={__0 != null}" +
                (__0 == null ? "" : $" save.prefabID={__0.prefabID} save.rackUID={__0.rackPositionUID}")));

        // ---- unlocks: does unlocking spend XP or just check it? ----

        static string UnlockState(ShopItem item)
        {
            var xp = PlayerManager.instance?.playerClass?.xp;
            return $"'{item.shopItemSO?.itemName}' xpToUnlock={item.shopItemSO?.xpToUnlock} unlocked={item.isUnlocked} playerXP={xp}";
        }

        static void TryUnlockPre(ShopItem __instance) => Guard("TryUnlockPre", () => Log($">> ShopItem.TryUnlock {UnlockState(__instance)}", console: true));
        static void TryUnlockPost(ShopItem __instance) => Guard("TryUnlockPost", () => Log($"<< ShopItem.TryUnlock {UnlockState(__instance)}", console: true));
        static void UnlockButtonPre(ShopItem __instance) => Guard("UnlockButtonPre", () => Log($">> ShopItem.UnlockButton {UnlockState(__instance)}"));

        static int _serializeCalls;
        static void FormatterSerializePre(Il2CppSystem.Object __1) => Guard("FormatterSerializePre", () =>
        {
            if (++_serializeCalls > 50) return;
            var sd = __1?.TryCast<SaveData>();
            Log($">> BinaryFormatter.Serialize(graph={(__1 == null ? "null" : __1.GetIl2CppType().FullName)})", console: sd != null);
            if (sd != null) Dumps.Save("BinaryFormatter.Serialize", sd);
        });

        static void SendTechPre(NetworkSwitch __0, Server __1) => Guard("SendTechPre", () =>
            Log($">> TechnicianManager.SendTechnician server={(__1 == null ? "null" : $"{__1.ServerID} prefabID={__1.prefabID}")} switch={(__0 == null ? "null" : "yes")}", console: true));
    }
}
