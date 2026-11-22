// GamesOMG Teleport 0.3.0 (Codex C61 F7): the game's distance switches, for DestinationWake. The set is the game's own:
// GlobalCoroutineUpdater<PerformancerDisableIfFarAway, PerformancersManager>._components, the components that ran Start and
// that the game's loop switches by distance (build 26411416). A component that never started is not in it, and the game
// never switches it, so neither does the mod: an object some other rule keeps off (a terraforming stage, say) stays off.
// One private static field, read by name (the after-patch gate lists the type); without it the wake does nothing and the
// landing check runs as 0.2.7's did.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SpaceCraft;
using UnityEngine;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal static class FarSwitches
    {
        private static readonly FieldInfo Registered =
            typeof(GlobalCoroutineUpdater<PerformancerDisableIfFarAway, PerformancersManager>).GetField("_components", BindingFlags.Static | BindingFlags.NonPublic);

        public static bool Available => Registered != null;

        // the game's registered switches now (a copy); empty when the field is gone or the set is not made yet
        public static List<IFarSwitch> Now()
        {
            var list = new List<IFarSwitch>();
            if (Registered == null) return list;
            if (!(Registered.GetValue(null) is IEnumerable set)) return list;
            foreach (var o in set)
                if (o is PerformancerDisableIfFarAway c && c != null) list.Add(new Switch(c));
            return list;
        }

        private sealed class Switch : IFarSwitch
        {
            private readonly PerformancerDisableIfFarAway c;
            private readonly Vector3 at;
            private readonly bool rootActive;

            public Switch(PerformancerDisableIfFarAway c)
            {
                this.c = c;
                at = c.transform.position;
                // the game's StartImpl keeps GetComponentInParent<GroupNetworkBase>(); an inactive group is left alone
                var root = c.GetComponentInParent<GroupNetworkBase>(true);
                rootActive = root == null || root.gameObject.activeInHierarchy;
            }

            public bool ActiveSelf => c != null && c.gameObject.activeSelf;
            public void SetActive(bool on) { if (c != null) c.gameObject.SetActive(on); }
            public float X => at.x;
            public float Y => at.y;
            public float Z => at.z;
            public float Reach => c != null ? c.disableIfPlayerIsFarerThan : 0f;
            public bool RootActive => rootActive;
        }
    }
}
