using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NewServers
{
    // A middle step between the starter switch (16x 10G RJ45) and the 16x SFP + 4x QSFP switch:
    // the starter switch with 4 SFP cages added at the far right (viewed from the back).
    // Registered like the servers: a template appended to MainGameManager.switchesPrefabs, whose index
    // is also its switchType and shop itemID. Saves keep it as a vanilla starter switch (see SaveCompat).
    internal static class Switches
    {
        public const string Key = "switch-16rj45-4sfp";
        public const int TwinIndex = 0;         // Switch16CU
        const string PortName = "SFP_Slot.NewServers";

        // All switch bodies share one mesh frame (0.498 x 0.0445 x 0.689). These are the positions of the
        // QSFP block on Switch4xQSXP16xSFP, using that switch's SFP row heights and SFP cage pitch.
        static readonly Vector3[] ExtraPorts =
        {
            new(-0.1628f, 0.0054f, 0.3463f), new(-0.1628f, -0.0110f, 0.3463f),
            new(-0.1825f, 0.0054f, 0.3463f), new(-0.1825f, -0.0110f, 0.3463f),
        };

        public static GameObject Prefab { get; private set; }
        public static ShopItemSO SO { get; private set; }
        public static ShopItemSO TwinSO { get; private set; }
        public static int Index { get; private set; } = -1;
        public static int TwinPrefabID { get; private set; }
        public static int SfpSwitchItemID { get; private set; } = -1; // Switch4xSFP: must be unlocked first
        public static string Label => "16x RJ45 + 4x SFP switch";
        public static bool Ready => Prefab != null;

        static int _vanillaCount = -1;
        static bool _failed;

        public static bool IsOurs(NetworkSwitch s) => Ready && s != null && s.switchType == Index;

        public static bool EnsureRegistered()
        {
            var mgm = MainGameManager.instance;
            var prefabs = mgm?.switchesPrefabs;
            if (prefabs == null || _failed) return false;

            if (!Ready && !Build(prefabs)) { _failed = true; return false; }

            if (prefabs.Length == _vanillaCount + 1 && prefabs[Index] != null && prefabs[Index].GetInstanceID() == Prefab.GetInstanceID())
                return true;
            if (prefabs.Length != _vanillaCount)
            {
                NewServersMod.Log.Error($"switchesPrefabs has {prefabs.Length} entries, expected {_vanillaCount}; the new switch is not registered.");
                return false;
            }

            var extended = new Il2CppReferenceArray<GameObject>(_vanillaCount + 1);
            for (int i = 0; i < _vanillaCount; i++) extended[i] = prefabs[i];
            extended[Index] = Prefab;
            mgm.switchesPrefabs = extended;
            NewServersMod.Log.Msg($"Registered the {Label} at switch index {Index} (${SO.price}, {SO.xpToUnlock} XP).");
            return true;
        }

        static bool Build(Il2CppReferenceArray<GameObject> prefabs)
        {
            Ports.FindDonors();
            var donor = Ports.SfpDonor;
            var twin = prefabs.Length > TwinIndex ? prefabs[TwinIndex] : null;
            var twinSwitch = twin == null ? null : twin.GetComponent<NetworkSwitch>();
            var shopSOs = Resources.FindObjectsOfTypeAll<ShopItemSO>().Where(so => so != null && so.itemType == PlayerManager.ObjectInHand.Switch).ToList();
            var twinSO = twinSwitch?.shopItemSO ?? shopSOs.FirstOrDefault(so => so.itemID == TwinIndex);
            var sfpSwitchSO = FindSfpOnlySwitchSO(prefabs, shopSOs);
            if (donor == null || twinSwitch == null || twinSO == null || sfpSwitchSO == null)
            {
                NewServersMod.Log.Warning($"New switch disabled: missing {(donor == null ? "SFP port " : "")}{(twinSwitch == null ? "starter switch " : "")}" +
                                          $"{(twinSO == null ? "starter switch shop item " : "")}{(sfpSwitchSO == null ? "4x SFP switch shop item" : "")}.");
                return false;
            }

            _vanillaCount = prefabs.Length;
            Index = prefabs.Length;
            TwinSO = twinSO;
            TwinPrefabID = twinSwitch.prefabID;
            SfpSwitchItemID = sfpSwitchSO.itemID;

            Prefab = Object.Instantiate(twin, Registry.Holder);
            Prefab.name = $"{twin.name}.4SFP";
            var nsw = Prefab.GetComponent<NetworkSwitch>();

            var ports = new List<CableLink>();
            var existing = nsw.cableLinkSwitchPorts;
            for (int i = 0; i < existing.Length; i++) ports.Add(existing[i]);
            for (int i = 0; i < ExtraPorts.Length; i++)
            {
                var go = Object.Instantiate(donor.gameObject, Prefab.transform);
                go.name = $"{PortName}.{ports.Count + 1}";
                go.transform.localPosition = ExtraPorts[i];
                go.transform.localRotation = donor.transform.localRotation;
                go.transform.localScale = donor.transform.localScale;

                // The copy still points at the donor switch; aim it at ours and reset it to an empty cage.
                var link = go.GetComponent<CableLink>();
                link.parentSwitch = nsw;
                link.parentServer = null;
                link.parentPatchPanel = null;
                link.parentInternet = null;
                link.typeOfLink = CableLink.TypeOfLink.Switch;
                link.isSFPPort = true;
                link.sfpTypeSupported = 0;
                link.sfpTypeInserted = -1;
                link.insertedSFP = null;
                link.connectionSpeed = 0f;
                ports.Add(link);
            }
            nsw.cableLinkSwitchPorts = new Il2CppReferenceArray<CableLink>(ports.ToArray());

            NewServersMod.Log.Msg($"Starter switch: switchType={twinSwitch.switchType} prefabID={twinSwitch.prefabID}.");
            nsw.switchType = Index;
            if (twinSwitch.prefabID == twinSwitch.switchType) nsw.prefabID = Index;

            // Price: the starter switch plus the 4x SFP switch, plus 25% for doing both jobs in 1U.
            // XP: the 4x SFP switch's, plus 25%.
            SO = Object.Instantiate(twinSO);
            SO.name = $"ShopItemSO_NewServers_{Key}";
            SO.itemID = Index;
            SO.price = Balance.RoundTo10((twinSO.price + sfpSwitchSO.price) * 1.25);
            SO.xpToUnlock = Balance.RoundTo10(sfpSwitchSO.xpToUnlock * 1.25);
            SO.hideFlags = HideFlags.DontUnloadUnusedAsset;
            nsw.shopItemSO = SO;
            return true;
        }

        // The vanilla switch with only SFP ports (Switch4xSFP).
        static ShopItemSO FindSfpOnlySwitchSO(Il2CppReferenceArray<GameObject> prefabs, List<ShopItemSO> shopSOs)
        {
            for (int i = 0; i < prefabs.Length; i++)
            {
                var s = prefabs[i] == null ? null : prefabs[i].GetComponent<NetworkSwitch>();
                var ports = s?.cableLinkSwitchPorts;
                if (ports == null || ports.Length == 0) continue;
                bool allSfp = true;
                for (int k = 0; k < ports.Length; k++)
                    if (ports[k] == null || !ports[k].isSFPPort || ports[k].sfpTypeSupported != 0) { allSfp = false; break; }
                if (!allSfp) continue;
                return s.shopItemSO ?? shopSOs.FirstOrDefault(so => so.itemID == i);
            }
            return null;
        }

        // "16 x 10Gbps RJ45\n<size=-3>EOL ...</size>" -> "16 x 10Gbps RJ45 + 4 x SFP\n<size=-3>EOL ...</size>"
        public static string MakeName(string vanillaText)
        {
            if (string.IsNullOrWhiteSpace(vanillaText)) return Label;
            var lines = vanillaText.Split('\n');
            lines[0] += " + 4 x SFP";
            return string.Join("\n", lines);
        }
    }
}
