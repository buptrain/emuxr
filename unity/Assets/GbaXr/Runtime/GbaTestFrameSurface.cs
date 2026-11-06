using System;
using UnityEngine;

namespace GbaXr
{
    /// <summary>
    /// Uploads a 240×160 RGBA frame to this object's renderer without filtering.
    /// It is the managed zero-parallax target for the future native frame lease.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public sealed class GbaTestFrameSurface : MonoBehaviour
    {
        public const int Width = 240;
        public const int Height = 160;

        private Texture2D _frameTexture;
        private Renderer _targetRenderer;
        private readonly byte[] _unityRowOrderRgba = new byte[Width * Height * 4];

        private void Awake()
        {
            _targetRenderer = GetComponent<Renderer>();
            _frameTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "GBA Test Frame"
            };

            ApplyTexture(_frameTexture);
            Upload(CreateDiagnosticFrame());
        }

        private void OnDestroy()
        {
            if (_frameTexture != null)
            {
                Destroy(_frameTexture);
            }
        }

        /// <summary>Copies one tightly packed 240×160 RGBA32 frame to the GPU texture.</summary>
        public void Upload(Color32[] pixels)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }
            if (pixels.Length != Width * Height)
            {
                throw new ArgumentException($"Expected {Width * Height} pixels, received {pixels.Length}.", nameof(pixels));
            }

            _frameTexture.SetPixels32(pixels);
            _frameTexture.Apply(false, false);
        }

        /// <summary>Uploads one tightly packed 240×160 RGBA8 frame.</summary>
        public void UploadRgba32(byte[] rgbaPixels)
        {
            if (rgbaPixels == null)
            {
                throw new ArgumentNullException(nameof(rgbaPixels));
            }
            if (rgbaPixels.Length != Width * Height * 4)
            {
                throw new ArgumentException($"Expected {Width * Height * 4} RGBA bytes, received {rgbaPixels.Length}.", nameof(rgbaPixels));
            }

            // mGBA/bridge rows are top-to-bottom; Texture2D raw data is
            // addressed from Unity's bottom-left origin. Keep the native
            // resolved bytes untouched for golden checks and flip only here.
            CopyTopLeftRgbaToUnityRowOrder(rgbaPixels, _unityRowOrderRgba, Width, Height);
            _frameTexture.LoadRawTextureData(_unityRowOrderRgba);
            _frameTexture.Apply(false, false);
        }

        internal static void CopyTopLeftRgbaToUnityRowOrder(byte[] source, byte[] destination, int width, int height)
        {
            var rowBytes = width * 4;
            if (source == null || destination == null || source.Length != rowBytes * height || destination.Length != rowBytes * height)
            {
                throw new ArgumentException("Source and destination must be tightly packed RGBA images of the requested dimensions.");
            }

            for (var y = 0; y < height; y++)
            {
                Buffer.BlockCopy(source, y * rowBytes, destination, (height - 1 - y) * rowBytes, rowBytes);
            }
        }

        public static Color32[] CreateDiagnosticFrame()
        {
            var pixels = new Color32[Width * Height];
            var dark = new Color32(12, 18, 34, 255);
            var light = new Color32(222, 232, 255, 255);
            var cyan = new Color32(55, 208, 232, 255);
            var magenta = new Color32(229, 82, 183, 255);

            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var border = x < 4 || x >= Width - 4 || y < 4 || y >= Height - 4;
                    var checker = ((x / 16) + (y / 16)) % 2 == 0;
                    var color = border ? light : checker ? dark : new Color32(25, 39, 67, 255);

                    if (x < 32 && y < 32)
                    {
                        color = cyan;
                    }
                    else if (x >= Width - 32 && y >= Height - 32)
                    {
                        color = magenta;
                    }

                    pixels[y * Width + x] = color;
                }
            }

            return pixels;
        }

        private void ApplyTexture(Texture2D texture)
        {
            var material = _targetRenderer.material;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
        }
    }
}
