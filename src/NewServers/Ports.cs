using Il2Cpp;
using UnityEngine;

namespace NewServers
{
    // Network ports per Gen. Vanilla server ports are 1 Gbps copper (connectionSpeed 0.2), and a link
    // runs at the slower end, so any server is capped at 0.2 = 20,000 IOPS however many cables it has.
    //   Gen 2: 10 Gbps copper (speed 2, like a switch port)
    //   Gen 3: empty SFP cages  - takes SFP+/SFP28 modules (speed 2 / 5)
    //   Gen 4: empty QSFP cages - takes QSFP+ modules (speed 8)
    // Cages are copied from a vanilla switch's ports (mesh, collider, CableLink settings), so module
    // insertion and saving work the way they do on switches. Port positions don't move, so without the
    // mod a saved module or cable lands in the vanilla twin's RJ45 port in the same place.
    internal static class Ports
    {
        public const float Copper10G = 2f;

        static CableLink _sfpDonor, _qsfpDonor, _switchRj45;
        static bool _searched;

        public static string Label(int gen) => gen switch
        {
            2 => "2x 10G RJ45",
            3 => "2x SFP",
            4 => "2x QSFP",
            _ => "",
        };

        public static void Configure(Server server, int gen)
        {
            var links = server.cablelinks;
            if (links == null) return;
            FindDonors();

            var donor = gen switch { 3 => _sfpDonor, 4 => _qsfpDonor, _ => null };
            for (int i = 0; i < links.Length; i++)
            {
                var link = links[i];
                if (link == null) continue;
                if (donor == null)
                {
                    // Gen 2, or a cage donor wasn't found: fast copper so the server isn't capped at 20k.
                    link.connectionSpeed = Copper10G;
                    continue;
                }
                MakeCage(link, donor);
            }
        }

        static void MakeCage(CableLink link, CableLink donor)
        {
            link.isSFPPort = true;
            link.sfpTypeSupported = donor.sfpTypeSupported;
            link.sfpTypeInserted = -1;
            link.connectionSpeed = donor.connectionSpeed; // 0 until a module goes in
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

            // Turn the cage the way the switch turns it relative to its own RJ45 ports, in case the
            // meshes were authored differently.
            var t = link.transform;
            if (_switchRj45 != null)
                t.localRotation = donor.transform.localRotation * Quaternion.Inverse(_switchRj45.transform.localRotation) * t.localRotation;
            t.localScale = donor.transform.localScale;
        }

        static void FindDonors()
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
                    else if (l.sfpTypeSupported == 0) _sfpDonor ??= l;
                    else if (l.sfpTypeSupported == 1) _qsfpDonor ??= l;
                }
            }

            if (_sfpDonor == null || _qsfpDonor == null)
                NewServersMod.Log.Warning($"Couldn't find switch ports to copy (SFP {(_sfpDonor != null)}, QSFP {(_qsfpDonor != null)}); " +
                                          "those Gens get 10G copper instead.");
            else
                NewServersMod.Log.Msg($"Port cages copied from '{_sfpDonor.transform.root.name}' (SFP) and '{_qsfpDonor.transform.root.name}' (QSFP).");
        }
    }
}
