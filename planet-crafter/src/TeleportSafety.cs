// ChatGPT C56: inspect the actual loaded world; no move occurs until every check succeeds.
using System;
using SpaceCraft;
using UnityEngine;

namespace GamesOMG.PlanetCrafterTeleport
{
    // What one landing check met (TeleportSafety.TryResolve): GroundY is NaN when the downward ray found no ground.
    internal struct LandingProbe
    {
        public float GroundY, NormalY;
        public Collider Ground, Blocker;
        // 0.3.2: what the check met, for the log line on a refusal: the ray, every hit it passed (and why), the ground, the
        // blocker. Names, scenes, layers and heights only.
        public string Trace;
    }

    internal static class TeleportSafety
    {
        private static bool OwnCollider(PlayerMainController player, Collider collider) =>
            collider.transform == player.transform || collider.transform.IsChildOf(player.transform);

        // Exclusion/include conflicts are deliberately unresolved: never guess the physics policy.
        private static bool TryContact(CharacterController player, Collider other, out bool contact)
        {
            contact = false;
            if (Physics.GetIgnoreCollision(player, other)) return true;
            var rigidbody = other.attachedRigidbody;
            if (rigidbody != null && (rigidbody.includeLayers.value != 0 || rigidbody.excludeLayers.value != 0))
                return false; // Do not guess how body and collider layer overrides combine.
            int theirs = 1 << other.gameObject.layer;
            int ours = 1 << player.gameObject.layer;
            bool pi = (player.includeLayers.value & theirs) != 0;
            bool pe = (player.excludeLayers.value & theirs) != 0;
            bool oi = (other.includeLayers.value & ours) != 0;
            bool oe = (other.excludeLayers.value & ours) != 0;
            if ((pi && (pe || oe)) || (oi && (oe || pe))) return false;
            contact = !pe && !oe && (pi || oi || !Physics.GetIgnoreLayerCollision(player.gameObject.layer, other.gameObject.layer));
            return true;
        }

        // 0.2.2 (2026-11-19: the suit has oxygen, so why refuse water?): water
        // is not a refusal any more. The suit has an oxygen tank and players swim and dive, so a landing under water is
        // a landing: the downward ray passes through a water surface to the bed below, and a water volume around the
        // standing capsule no longer counts as an obstruction. The notice says nothing about it (0.2.4).

        public static bool TryResolve(PlayerMainController player, TeleportRequest request,
            out Vector3 destination, out string reason) => TryResolve(player, request, out destination, out reason, out _);

        // The same check, also saying what it met: the ground the ray stopped on and, for a refusal, the collider in the
        // way. The landing survey (a test-only build) records these; the verdict is the one the mod gives a player.
        public static bool TryResolve(PlayerMainController player, TeleportRequest request,
            out Vector3 destination, out string reason, out LandingProbe probe)
        {
            var trace = new System.Text.StringBuilder();
            bool ok = Resolve(player, request, out destination, out reason, out probe, trace);
            probe.Trace = trace.ToString();
            return ok;
        }

        private static string Where(Collider c)
        {
            if (c == null) return "(gone)";
            var root = c.transform.root;
            return "'" + c.name + "'" + (root != c.transform ? " under '" + root.name + "'" : "") + " [" + c.GetType().Name + ", layer " + c.gameObject.layer
                + ", scene '" + c.gameObject.scene.name + "'" + (c.enabled ? "" : ", disabled") + "]";
        }

        private static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private static bool Resolve(PlayerMainController player, TeleportRequest request,
            out Vector3 destination, out string reason, out LandingProbe probe, System.Text.StringBuilder trace)
        {
            probe = new LandingProbe { GroundY = float.NaN };
            destination = default;
            reason = L.T("land.noGround");
            var body = player.GetComponent<CharacterController>();
            if (body == null || !body.enabled || Vector3.Dot(player.transform.up, Vector3.up) < .99f)
            {
                trace.Append(body == null ? "no CharacterController" : !body.enabled ? "CharacterController disabled" : "player tilted (up.y " + F(player.transform.up.y) + ")");
                reason = L.T("land.onFoot");
                return false;
            }
            trace.Append("player layer " + player.gameObject.layer + " (controller include " + body.includeLayers.value + ", exclude " + body.excludeLayers.value
                + ", at " + F(player.transform.position.x) + ", " + F(player.transform.position.y) + ", " + F(player.transform.position.z) + ")");
            Vector3 scale = player.transform.lossyScale;
            float height = body.height * Mathf.Abs(scale.y);
            float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float skin = body.skinWidth * Mathf.Max(Mathf.Abs(scale.y), 1f);
            Vector3 offset = player.transform.TransformVector(body.center);
            float x = request.X + offset.x, z = request.Z + offset.z;
            float originY = request.Y + .5f, distance = height + 12f;

            if (request.AutomaticHeight)
            {
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                {
                    if (!collider.enabled || collider.isTrigger || OwnCollider(player, collider)) continue;
                    var bounds = collider.bounds;
                    if (x < bounds.min.x || x > bounds.max.x || z < bounds.min.z || z > bounds.max.z) continue;
                    low = Mathf.Min(low, bounds.min.y);
                    high = Mathf.Max(high, bounds.max.y);
                }
                if (float.IsInfinity(low) || float.IsInfinity(high) || high - low > 20000f)
                {
                    trace.Append("; ~ height: no enabled solid collider spans this x, z" + (float.IsInfinity(low) ? "" : " (" + F(low) + " to " + F(high) + ")"));
                    return false;
                }
                originY = high + height + 2f;
                distance = originY - low + 2f;
            }

            var hits = Physics.RaycastAll(new Vector3(x, originY, z), Vector3.down,
                distance, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            trace.Append("; ray from y " + F(originY) + " down " + F(distance) + " m: " + hits.Length + " hit" + (hits.Length == 1 ? "" : "s"));
            RaycastHit ground = default;
            bool found = false;
            foreach (var hit in hits)
            {
                if (hit.collider == null) { trace.Append("; (destroyed collider)"); continue; }
                string at = "; y " + F(hit.point.y) + " " + Where(hit.collider);
                if (OwnCollider(player, hit.collider)) { trace.Append(at + " = the player, passed"); continue; }
                if (!TryContact(body, hit.collider, out bool contact))
                {
                    trace.Append(at + " = layer overrides conflict, refused");
                    probe.Blocker = hit.collider;
                    reason = L.T("land.conflicting");
                    return false;
                }
                if (!contact) { trace.Append(at + " = no contact with the player" + (Physics.GetIgnoreCollision(body, hit.collider) ? " (IgnoreCollision)" : "") + ", passed"); continue; }
                // A water surface is not ground: keep going down to the bed under it.
                if (hit.collider.GetComponentInParent<WaterVolume>() != null) { trace.Append(at + " = water, passed"); continue; }
                trace.Append(at + " = ground (normal y " + F(hit.normal.y) + ")");
                ground = hit;
                found = true;
                break;
            }
            if (!found) return false;
            probe.GroundY = ground.point.y;
            probe.NormalY = ground.normal.y;
            probe.Ground = ground.collider;
            float slopeCos = Mathf.Cos(Mathf.Clamp(body.slopeLimit, 0, 89f) * Mathf.Deg2Rad);
            if (ground.normal.y < slopeCos)
            {
                reason = L.T("land.steep");
                return false;
            }
            if (!LandingGeometry.TryPivotY(ground.point.y, ground.normal.y, height, radius, offset.y, skin, out float y))
            {
                reason = L.T("land.shape");
                return false;
            }
            destination = new Vector3(request.X, y, request.Z);
            Vector3 center = destination + offset;
            float halfSegment = height / 2 - radius;
            Vector3 bottom = center - Vector3.up * halfSegment;
            Vector3 top = center + Vector3.up * halfSegment;
            foreach (var collider in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Collide))
            {
                if (collider == null || OwnCollider(player, collider)) continue;
                // Water around the standing spot is fine (the suit has air). WaterHandler is a
                // render/energy query and may ignore static water volumes, so the volume itself is what is read.
                if (collider.GetComponentInParent<WaterVolume>() != null) continue;
                if (collider.isTrigger) continue;
                if (!TryContact(body, collider, out bool contact) || contact)
                {
                    trace.Append("; standing capsule at y " + F(destination.y) + " blocked by " + Where(collider));
                    probe.Blocker = collider;
                    reason = L.T("land.noRoom");
                    return false;
                }
            }
            return true;
        }
    }
}
