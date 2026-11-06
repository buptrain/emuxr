using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GbaXr
{
    public sealed class GbaLayeredSurface : MonoBehaviour
    {
        private readonly Dictionary<int, Texture2D> _textures = new();
        private readonly Dictionary<int, GameObject> _planes = new();
        private readonly Dictionary<int, byte[]> _nativePixels = new();
        private readonly Dictionary<int, byte[]> _unityPixels = new();
        private readonly Dictionary<int, byte[]> _coverage = new();
        private readonly HashSet<int> _sourcesInCurrentLease = new();
        private bool _loggedFirstPresentation;

        /// <summary>
        /// Presents independently verified opaque candidate planes. Returns false
        /// without changing the resolved surface when the lease cannot safely be
        /// rendered as layered content.
        /// </summary>
        internal bool Present(GbaNativeBindings.FrameLease lease, bool useUnsafeVisiblePartition)
        {
            if (lease.Layers == IntPtr.Zero || lease.LayerCount == 0)
            {
                return false;
            }

            var size = Marshal.SizeOf<GbaNativeBindings.LayerView>();
            _sourcesInCurrentLease.Clear();
            for (var i = 0; i < (int)lease.LayerCount; i++)
            {
                var l = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(IntPtr.Add(lease.Layers, i * size));
                float localZ;
                var hasDepth = useUnsafeVisiblePartition
                    ? GbaUnsafeVisiblePartitionProfile.TryGetDepth(l.Source, out localZ)
                    : GbaDepthProfile.TryGetDepth(l.Source, l.Priority, out localZ);
                if (l.Color.Rgba8 == IntPtr.Zero || l.CoverageMask == IntPtr.Zero || !hasDepth)
                {
                    Debug.LogError("GBA XR: a presentation plane is incomplete or unsupported by the selected 2.5D profile; using resolved fallback.");
                    return false;
                }

                _sourcesInCurrentLease.Add(l.Source);
                if (!_textures.TryGetValue(l.Source, out var texture))
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null)
                    {
                        Debug.LogError("GBA XR: URP/Unlit shader is unavailable; using resolved fallback.");
                        return false;
                    }

                    texture = new Texture2D(GbaTestFrameSurface.Width, GbaTestFrameSurface.Height, TextureFormat.RGBA32, false, true)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        name = $"GBA Candidate {l.Source}"
                    };
                    _textures.Add(l.Source, texture);
                    _nativePixels.Add(l.Source, new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4]);
                    _unityPixels.Add(l.Source, new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4]);
                    _coverage.Add(l.Source, new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height]);

                    var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    plane.name = $"GBA layer {l.Source}";
                    plane.transform.SetParent(transform, false);
                    plane.transform.localScale = Vector3.one;
                    Destroy(plane.GetComponent<Collider>());

                    var material = new Material(shader) { name = $"GBA Candidate Material {l.Source}" };
                    material.SetTexture("_BaseMap", texture);
                    // Coverage becomes alpha, so candidates must not paint their
                    // transparent pixels over lower-priority planes.
                    if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
                    if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
                    if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    var renderer = plane.GetComponent<Renderer>();
                    renderer.material = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    _planes.Add(l.Source, plane);
                    Debug.Log($"GBA XR: created 2.5D plane source={l.Source}, localZ={localZ:F3}m, " +
                              $"mode={(useUnsafeVisiblePartition ? "unsafe visible partition" : "verified candidate")}.");
                }

                var nativePixels = _nativePixels[l.Source];
                var coverage = _coverage[l.Source];
                Marshal.Copy(l.Color.Rgba8, nativePixels, 0, nativePixels.Length);
                Marshal.Copy(l.CoverageMask, coverage, 0, coverage.Length);
                for (var pixel = 0; pixel < coverage.Length; pixel++) nativePixels[pixel * 4 + 3] = coverage[pixel];
                GbaTestFrameSurface.CopyTopLeftRgbaToUnityRowOrder(nativePixels, _unityPixels[l.Source], GbaTestFrameSurface.Width, GbaTestFrameSurface.Height);
                texture.LoadRawTextureData(_unityPixels[l.Source]);
                texture.Apply(false, false);

                var candidatePlane = _planes[l.Source];
                candidatePlane.SetActive(true);
                candidatePlane.transform.localPosition = new Vector3(0, 0, localZ);
            }

            foreach (var entry in _planes)
            {
                if (!_sourcesInCurrentLease.Contains(entry.Key)) entry.Value.SetActive(false);
            }

            if (!_loggedFirstPresentation)
            {
                _loggedFirstPresentation = true;
                Debug.Log(useUnsafeVisiblePartition
                    ? $"GBA XR: presenting {lease.LayerCount} unsafe visible-partition plane(s) in the 2.5D compositor."
                    : $"GBA XR: presenting {lease.LayerCount} verified candidate plane(s) in the 2.5D compositor.");
            }

            return true;
        }
        public void Hide() { foreach (var plane in _planes.Values) plane.SetActive(false); }

    }
}
