using System;
using System.Collections.Generic;

namespace GbaXr
{
    /// <summary>Pure model shared by the renderer and edit-mode tests.</summary>
    public readonly struct LayerDescriptor
    {
        public readonly int Priority;
        public readonly int StableOrder;

        public LayerDescriptor(int priority, int stableOrder)
        {
            Priority = priority;
            StableOrder = stableOrder;
        }
    }

    public static class LayerOrdering
    {
        // Lower priority is foreground, as on the GBA. StableOrder breaks ties deterministically.
        public static List<LayerDescriptor> FrontToBack(IEnumerable<LayerDescriptor> layers)
        {
            var result = new List<LayerDescriptor>(layers);
            result.Sort((left, right) =>
            {
                var priority = left.Priority.CompareTo(right.Priority);
                return priority != 0 ? priority : left.StableOrder.CompareTo(right.StableOrder);
            });
            return result;
        }
    }
}
