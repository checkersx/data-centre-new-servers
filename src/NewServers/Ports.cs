using Il2Cpp;
using UnityEngine;

namespace NewServers
{
    // Network ports per Gen. Vanilla server ports are 1 Gbps copper (connectionSpeed 0.2), and a link
    // runs at the slower end, so any server is capped at 0.2 = 20,000 IOPS however many cables it has.
    //   Gen 2:   10 Gbps copper (speed 2, like a switch port)
    //   Gen 3/4: empty SFP cages - take SFP RJ45 / SFP+ / SFP28 modules (speed 2 / 2 / 5)
    // (QSFP cages worked but look too narrow on a server's back panel, and no server needs 40G.)
    // Cages are copied from a vanilla switch's SFP port (mesh, collider, CableLink settings), so module
    // insertion and saving work the way they do on switches. Port positions don't move, so without the
    // mod a saved module or cable lands in the vanilla twin's RJ45 port in the same place.
    internal static class Ports
    {
        public const float Copper10G = 2f;

        static CableLink _switchRj45;
        static bool _searched;

        // An empty SFP cage on a vanilla switch; also the template for the new switch's extra ports.
        public static CableLink SfpDonor { get; private set; }

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
                    MakeCage(link, SfpDonor);
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

            var mf = link.GetComponent<MeshFilter>();
            var donorMf = donor.GetComponent<MeshFilter>();
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
            var t = link.transform;
            if (_switchRj45 != null)
                t.localRotation = donor.transform.localRotation * Quaternion.Inverse(_switchRj45.transform.localRotation) * t.localRotation;
            t.localScale = donor.transform.localScale;
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
                NewServersMod.Log.Warning("Couldn't find a switch SFP port to copy; Gen 3/4 get 10G copper and the new switch is disabled.");
            else
                NewServersMod.Log.Msg($"SFP cages copied from '{SfpDonor.transform.root.name}'.");
        }
    }
}
