using System;

namespace GbaXr
{
    /// <summary>
    /// Conservative CPU reference compositor for lease planes whose masks form
    /// a disjoint final-visible partition. It intentionally does not infer
    /// hidden pixels or apply depth displacement.
    /// </summary>
    internal static class VisibleSourcePartitionCompositor
    {
        internal const int MaxSourceCount = 6;
        private const int PixelCount = GbaTestFrameSurface.Width * GbaTestFrameSurface.Height;
        private const int RgbaByteCount = PixelCount * 4;

        internal readonly struct Plane
        {
            internal Plane(int source, byte[] rgba, byte[] coverage)
            {
                Source = source;
                Rgba = rgba;
                Coverage = coverage;
            }

            internal int Source { get; }
            internal byte[] Rgba { get; }
            internal byte[] Coverage { get; }
        }

        internal static bool TryComposite(Plane[] planes, byte[] destination, out string error)
        {
            if (planes == null || planes.Length == 0 || planes.Length > MaxSourceCount ||
                destination == null || destination.Length != RgbaByteCount)
            {
                error = "The partition shape is invalid.";
                return false;
            }

            var counts = new byte[PixelCount];
            var seenSources = 0;
            Array.Clear(destination, 0, destination.Length);
            for (var layer = 0; layer < planes.Length; layer++)
            {
                var plane = planes[layer];
                if (plane.Source < 0 || plane.Source >= MaxSourceCount ||
                    plane.Rgba == null || plane.Rgba.Length != RgbaByteCount ||
                    plane.Coverage == null || plane.Coverage.Length != PixelCount ||
                    (seenSources & (1 << plane.Source)) != 0)
                {
                    error = "A partition plane is invalid or duplicates a source.";
                    return false;
                }

                seenSources |= 1 << plane.Source;
                for (var pixel = 0; pixel < PixelCount; pixel++)
                {
                    if (plane.Coverage[pixel] == 0)
                    {
                        continue;
                    }
                    if (counts[pixel] != 0)
                    {
                        error = $"Partition coverage overlaps at pixel {pixel}.";
                        return false;
                    }

                    var offset = pixel * 4;
                    destination[offset] = plane.Rgba[offset];
                    destination[offset + 1] = plane.Rgba[offset + 1];
                    destination[offset + 2] = plane.Rgba[offset + 2];
                    destination[offset + 3] = plane.Rgba[offset + 3];
                    counts[pixel] = 1;
                }
            }

            for (var pixel = 0; pixel < PixelCount; pixel++)
            {
                if (counts[pixel] != 1)
                {
                    error = $"Partition coverage is incomplete at pixel {pixel}.";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}
