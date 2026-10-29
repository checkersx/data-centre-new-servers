using System;
using Il2Cpp;
using Il2CppEPOOutline;
using UnityEngine;

namespace NextGenHardware
{
    // Network ports per Gen. Vanilla server ports are 1 Gbps copper (connectionSpeed 0.2), and a link
    // runs at the slower end, so any server is capped at 0.2 = 20,000 IOPS however many cables it has.
    //   Gen 2:   10 Gbps copper (speed 2, like a switch port)
    //   Gen 3/4: empty SFP cages - take SFP RJ45 / SFP+ / SFP28 modules (speed 2 / 2 / 5)
    // (QSFP cages worked but look too narrow on a server's back panel, and no server needs 40G.)
    // Cages are copied from a vanilla switch's SFP port (mesh, collider, CableLink settings), so module
    // insertion and saving work the way they do on switches.
    //
    // The cage mesh sits almost entirely behind its pivot: on the vanilla SFP switches it slots into a hole
    // in the faceplate and you look into its dark inside. Our devices have no hole, so only the rim showed,
    // as a flat white outline. Instead each cage is brought out to where the copper socket's front was, and
    // gets a dark back just inside its opening (hidden while a module is in).
    internal static class Ports
    {
        public const float Copper10G = 2f;

        const string CageBackName = "NextGenHardware.CageBack";
        const float CageWall = 0.001f;        // the back fills the opening inside walls this thick
        const float CageBackRecess = 0.0012f; // how far behind the rim the back sits
        const float CageBackDepth = 0.003f;

        static CableLink _switchRj45;
        static Material _cageBlack;
        static bool _searched;

        // An empty SFP cage on a vanilla switch; also the template for the new switch's extra ports.
        public static CableLink SfpDonor { get; private set; }

        // Front of a port's mesh along the device's depth axis, in the device's space.
        public static float FrontZ(Transform port, Mesh mesh) =>
            port.localPosition.z + (port.localRotation * Vector3.Scale(port.localScale, new Vector3(0f, 0f, mesh.bounds.max.z))).z;

        public static float DonorFront => SfpDonor.GetComponent<MeshFilter>().sharedMesh.bounds.max.z;

        public static string Label(int gen) => gen >= 3 ? "2x SFP" : "2x 10G RJ45";

        public static void Configure(Server server, int gen)
        {
            var links = server.cablelinks;
            if (links == null) return;
            FindDonors();

            for (int i = 0; i < links.Length; i++)
            {
                var link = links[i];
                if (link == null) continue;
                if (gen >= 3 && SfpDonor != null)
                {
                    MakeCage(link, SfpDonor);
                    AddCageBack(link);
                }
                else
                    link.connectionSpeed = Copper10G; // Gen 2, or no cage donor found: fast copper, no 20k cap
            }
        }

        static void MakeCage(CableLink link, CableLink donor)
        {
            link.isSFPPort = true;
            link.sfpTypeSupported = donor.sfpTypeSupported;
            link.sfpTypeInserted = -1;
            link.connectionSpeed = 0f; // until a module goes in
            link.isFibrePort = donor.isFibrePort;
            link.sfpForwardOffset = donor.sfpForwardOffset;
            link.ropeForwardOffset = donor.ropeForwardOffset;

            var t = link.transform;
            var mf = link.GetComponent<MeshFilter>();
            var donorMf = donor.GetComponent<MeshFilter>();
            float? socketFront = mf != null && mf.sharedMesh != null ? FrontZ(t, mf.sharedMesh) : null;
            if (mf != null && donorMf != null) mf.sharedMesh = donorMf.sharedMesh;

            var mr = link.GetComponent<MeshRenderer>();
            var donorMr = donor.GetComponent<MeshRenderer>();
            if (mr != null && donorMr != null) mr.sharedMaterials = donorMr.sharedMaterials;

            var box = link.GetComponent<BoxCollider>();
            var donorBox = donor.GetComponent<BoxCollider>();
            if (box != null && donorBox != null)
            {
                box.size = donorBox.size;
                box.center = donorBox.center;
            }

            // Turn the cage the way the switch turns it relative to its own RJ45 ports (the RJ45 mesh is
            // authored upside down: switch RJ45 ports sit at 180 degrees, SFP cages at 0).
            if (_switchRj45 != null)
                t.localRotation = donor.transform.localRotation * Quaternion.Inverse(_switchRj45.transform.localRotation) * t.localRotation;
            t.localScale = donor.transform.localScale;

            if (socketFront.HasValue && mf != null && mf.sharedMesh != null)
                t.localPosition += new Vector3(0f, 0f, socketFront.Value - FrontZ(t, mf.sharedMesh));
        }

        // Postfix on CableLink.InteractOnHover. The game outlines a hovered port (EPOOutline, created at runtime)
        // by drawing the port's own mesh on top of everything. For a cage that traces its whole 5 cm body hidden
        // inside the device: a long box, or a "floating" rectangle where the back plate is. On our cages,
        // outline the dark back at the opening instead. (While a module is in, the back is hidden and the
        // module's own highlight shows.)
        static bool _outlineLogged;
        public static void OutlinePostfix(CableLink __instance)
        {
            try
            {
                var o = __instance.outlineEffect;
                var back = o == null ? null : __instance.transform.Find(CageBackName);
                var mr = back == null ? null : back.GetComponent<MeshRenderer>();
                var targets = o?.outlineTargets;
                if (mr == null || targets == null) return; // not one of our cages, or not outlined yet
                if (targets.Count == 1 && targets[0]?.renderer != null && targets[0].renderer.GetInstanceID() == mr.GetInstanceID()) return;

                if (!_outlineLogged)
                {
                    _outlineLogged = true;
                    NextGenHardwareMod.Log.Msg($"Cage outline on '{__instance.name}': {targets.Count} target(s) replaced by the cage back.");
                }
                for (int i = targets.Count - 1; i >= 0; i--) o.RemoveTarget(targets[i]);
                o.AddTarget(new OutlineTarget(mr, 0));
            }
            catch (Exception e) { NextGenHardwareMod.Log.Error($"OutlinePostfix: {e.Message}"); }
        }

        // A dark block just inside the cage's opening, standing in for the hole a vanilla switch has there.
        public static void AddCageBack(CableLink link)
        {
            var mesh = link.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || _cageBlack == null) return;
            var b = mesh.bounds;
            Shapes.Box(CageBackName, link.transform, _cageBlack,
                new Vector3(b.center.x, b.center.y, b.max.z - CageBackRecess - CageBackDepth / 2f),
                new Vector3(b.size.x - 2f * CageWall, b.size.y - 2f * CageWall, CageBackDepth));
        }

        // Postfix on CableLink.InsertSFP / RemoveSFP: a module fills the opening, so hide the back meanwhile.
        public static void CageBackPostfix(CableLink __instance)
        {
            try
            {
                var back = __instance.transform.Find(CageBackName);
                if (back != null) back.gameObject.SetActive(__instance.insertedSFP == null && __instance.sfpTypeInserted < 0);
            }
            catch (Exception e) { NextGenHardwareMod.Log.Error($"CageBackPostfix: {e.Message}"); }
        }

        public static void FindDonors()
        {
            if (_searched) return;
            _searched = true;
            var switches = MainGameManager.instance?.switchesPrefabs;
            if (switches == null) return;

            for (int i = 0; i < switches.Length; i++)
            {
                var go = switches[i];
                if (go == null) continue;
                var links = go.GetComponentsInChildren<CableLink>(true);
                for (int k = 0; k < links.Length; k++)
                {
                    var l = links[k];
                    if (l.GetComponent<MeshFilter>() == null) continue;
                    if (!l.isSFPPort) _switchRj45 ??= l;
                    else if (l.sfpTypeSupported == 0) SfpDonor ??= l;
                }
            }

            if (SfpDonor == null)
            {
                NextGenHardwareMod.Log.Warning("Couldn't find a switch SFP port to copy; Gen 3/4 get 10G copper and the new switch is disabled.");
                return;
            }

            // The cage's own dark material ("_Black") for its back.
            var mats = SfpDonor.GetComponent<MeshRenderer>()?.sharedMaterials;
            if (mats != null)
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && (_cageBlack == null || mats[i].name.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0))
                        _cageBlack = mats[i];
            NextGenHardwareMod.Log.Msg($"SFP cages copied from '{SfpDonor.transform.root.name}', cage back material '{_cageBlack?.name}'.");
        }
    }
}
