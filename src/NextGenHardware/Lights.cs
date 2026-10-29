using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace NextGenHardware
{
    // Light bars across the front of upgraded servers, more of them each Gen:
    //   Gen 2: one bar along the bottom edge
    //   Gen 3: bars along the bottom and top edges
    //   Gen 4: a full frame (bottom, top and both sides) with a light sweeping across it, left to right
    // Bars are boxes in the family's colour, made from a copy of the game's green power-light material so
    // they glow the way the game's own lights do. They go out while the server is off or broken.
    // Every Gen 4 server of a family shares one material per slice of the frame, so the sweep costs the
    // same however many of them there are.
    internal static class Lights
    {
        const string RootName = "NextGenHardware.Lights";
        const float BarSize = 0.0025f;    // height of a horizontal bar, width of a vertical one
        const float BarDepth = 0.002f;
        const float Proud = 0.0005f;      // how far the bars stand in front of the server's front face
        const float SteadyInset = 0.012f; // Gen 2/3 bars stop this short of the sides
        const int Slices = 8;             // Gen 4: the bottom and top bars are cut into this many slices
        const float SweepSeconds = 2.4f;

        // Gen 4 brightness away from the sweep, as a share of the game LED's strength. The game's LED is
        // so bright (emission 30, ignoring exposure) that anything above ~0.1 of it already burns out to
        // white, so the frame idles at a coloured glow and the sweep brightens it to the same white-hot
        // level as the steady Gen 2/3 bars.
        const float IdleGlow = 0.04f;
        const float SweepWidth = 0.35f;   // smaller = a wider band

        static readonly Dictionary<string, Color> Colours = new()
        {
            ["Yellow"] = new Color(1f, 0.72f, 0.08f),
            ["Blue"] = new Color(0.1f, 0.4f, 1f),
            ["Green"] = new Color(0.15f, 1f, 0.3f),
            ["Purple"] = new Color(0.7f, 0.2f, 1f),
        };

        static readonly Dictionary<string, Material> Steady = new();
        static readonly Dictionary<string, (Color colour, Material[] slices)> Sweeps = new();
        static Material _base;
        static float _emission = 1f;
        static bool _baseSearched, _animateFailed;

        static int EmissiveColor => Shader.PropertyToID("_EmissiveColor");
        static int BaseColor => Shader.PropertyToID("_BaseColor");
        static int UnlitColor => Shader.PropertyToID("_UnlitColor");

        public static void Add(GameObject server, string colourKey, string label, int gen)
        {
            try
            {
                var body = server.GetComponent<MeshFilter>()?.sharedMesh;
                if (body == null || Base() == null) return;
                if (!Colours.TryGetValue(colourKey, out var colour)) colour = Color.white;

                var root = new GameObject(RootName).transform;
                root.SetParent(server.transform, false);

                // The server's front faces -z; +x is the right-hand side seen from the front.
                var b = body.bounds;
                float z = b.min.z - Proud + BarDepth / 2f;
                float bottom = b.min.y + BarSize / 2f, top = b.max.y - BarSize / 2f;

                if (gen < 4)
                {
                    var m = SteadyMaterial(colourKey, label, colour);
                    float x0 = b.min.x + SteadyInset, x1 = b.max.x - SteadyInset;
                    HBar(root, m, x0, x1, bottom, z);
                    if (gen >= 3) HBar(root, m, x0, x1, top, z);
                    return;
                }

                // Sides run the full height; the bottom and top bars fit between them, one slice per material.
                var slices = SweepMaterials(colourKey, label, colour);
                VBar(root, slices[0], b.min.x + BarSize / 2f, b.min.y, b.max.y, z);
                VBar(root, slices[Slices - 1], b.max.x - BarSize / 2f, b.min.y, b.max.y, z);
                float left = b.min.x + BarSize, width = (b.size.x - 2f * BarSize) / Slices;
                for (int i = 0; i < Slices; i++)
                {
                    float x0 = left + i * width;
                    HBar(root, slices[i], x0, x0 + width, bottom, z);
                    HBar(root, slices[i], x0, x0 + width, top, z);
                }
            }
            catch (Exception e) { NextGenHardwareMod.Log.Error($"Lights for {label} Gen {gen}: {e}"); }
        }

        static void HBar(Transform root, Material m, float x0, float x1, float y, float z) =>
            Shapes.Box("Bar", root, m, new Vector3((x0 + x1) / 2f, y, z), new Vector3(x1 - x0, BarSize, BarDepth));

        static void VBar(Transform root, Material m, float x, float y0, float y1, float z) =>
            Shapes.Box("Bar", root, m, new Vector3(x, (y0 + y1) / 2f, z), new Vector3(BarSize, y1 - y0, BarDepth));

        // The game's power-on light. Its emission strength is what the game's lights look right at.
        static Material Base()
        {
            if (_baseSearched) return _base;
            _baseSearched = true;
            _base = MainGameManager.instance?.greenLightMaterial;
            if (_base == null)
            {
                NextGenHardwareMod.Log.Warning("Couldn't find the game's power-light material; servers get no light bars.");
                return null;
            }
            if (_base.HasProperty(EmissiveColor))
            {
                var e = _base.GetColor(EmissiveColor);
                float peak = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                if (peak > 0.0001f) _emission = peak;
            }
            NextGenHardwareMod.Log.Msg($"Light bars copy '{_base.name}' (shader '{_base.shader?.name}', emissive={_base.HasProperty(EmissiveColor)}, " +
                                       $"strength {_emission:G3}, exposure weight {(_base.HasProperty("_EmissiveExposureWeight") ? _base.GetFloat("_EmissiveExposureWeight").ToString("G3") : "n/a")}).");
            return _base;
        }

        static Material SteadyMaterial(string colourKey, string label, Color colour)
        {
            if (!Steady.TryGetValue(colourKey, out var m))
                Steady[colourKey] = m = Make($"NextGenHardware.Led.{label}", colour, 1f);
            return m;
        }

        static Material[] SweepMaterials(string colourKey, string label, Color colour)
        {
            if (Sweeps.TryGetValue(colourKey, out var sweep)) return sweep.slices;
            var slices = new Material[Slices];
            for (int i = 0; i < Slices; i++)
                slices[i] = Make($"NextGenHardware.Led.{label}.Sweep{i}", colour, IdleGlow);
            Sweeps[colourKey] = (colour, slices);
            return slices;
        }

        // A flat-coloured copy of the power light: its icon textures are dropped, its colour replaced.
        // (Names avoid "BrushedAluminium", which RackColors recolours.)
        static Material Make(string name, Color colour, float level)
        {
            var m = new Material(_base) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            var textures = m.GetTexturePropertyNames();
            for (int i = 0; i < textures.Length; i++) m.SetTexture(textures[i], (Texture)null);
            m.DisableKeyword("_EMISSIVE_COLOR_MAP");
            m.DisableKeyword("_MASKMAP");
            m.DisableKeyword("_NORMALMAP");
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, colour * 0.35f);
            SetLevel(m, colour, level);
            return m;
        }

        static void SetLevel(Material m, Color colour, float level)
        {
            if (m.HasProperty(EmissiveColor)) m.SetColor(EmissiveColor, colour * (_emission * level));
            if (m.HasProperty(UnlitColor)) m.SetColor(UnlitColor, colour * level);
        }

        // Each frame: a bright band runs across the Gen 4 slices, starting off the left edge and
        // finishing off the right one.
        public static void Animate()
        {
            if (Sweeps.Count == 0 || _animateFailed) return;
            try
            {
                float t = Time.time / SweepSeconds;
                float pos = (t - Mathf.Floor(t)) * (Slices + 4f) - 2f;
                foreach (var (colour, slices) in Sweeps.Values)
                    for (int i = 0; i < Slices; i++)
                    {
                        float d = i - pos;
                        SetLevel(slices[i], colour, IdleGlow + (1f - IdleGlow) * Mathf.Exp(-SweepWidth * d * d));
                    }
            }
            catch (Exception e)
            {
                _animateFailed = true;
                NextGenHardwareMod.Log.Error($"Light sweep stopped: {e}");
            }
        }

        // Postfix on the Server methods that switch it on or off, break or repair it.
        public static void SyncPostfix(Server __instance)
        {
            try
            {
                var lights = __instance.transform.Find(RootName);
                if (lights != null) lights.gameObject.SetActive(__instance.isOn && !__instance.isBroken);
            }
            catch (Exception e) { NextGenHardwareMod.Log.Error($"Lights.SyncPostfix: {e.Message}"); }
        }
    }
}
