using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NextGenHardware
{
    // Plain boxes for decoration (light bars, the dark backs of SFP cages): Unity's built-in cube mesh,
    // no collider (so they never block the player's clicks) and no shadows.
    internal static class Shapes
    {
        static Mesh _cube;

        static Mesh Cube
        {
            get
            {
                if (_cube != null) return _cube;
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(tmp);
                return _cube;
            }
        }

        // A box of the given size, centred at localPos in parent's space.
        public static GameObject Box(string name, Transform parent, Material material, Vector3 localPos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Cube;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
    }
}
