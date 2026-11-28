// Survey (TEST BUILD ONLY: dotnet build -p:Survey=true; never in a release zip, release.ps1 refuses it).
// The in-game proof of every landing: F9 starts it, F9 again stops it. For each place in the embedded list it does what a
// player's F7 does (Resolve: ground ray + the game's NavMesh), teleports, waits 1.5 s, then measures where the game put you:
//   grounded (PlayerMovement.IsGrounded), drop under the landing, drift off it, the death-fall collider, in water
//   (WaterStateComp), and whether the NavMesh has a walking path from where you stand to the pin itself, and how long.
// A landing that fails is searched again IN GAME: rings of 3 to 8 m round the pin, each spot snapped to the NavMesh on the
// pin's own level and kept only with a complete path to the pin; the first that passes is stood on and measured too.
// Rows go to BepInEx/gamesomg-tp-survey.tsv (one per place, appended as it goes, so a stopped survey resumes where it was).
// gamesomg-site tools/lord-of-undead-map/tp/survey.py turns that file into the map's landings.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.AI;
using GamePlayer = _Project.Scripts.Player.Player;

namespace GamesOMG.LordOfUndeadTeleport
{
    internal sealed class Survey
    {
        const float Settle = 1.5f;
        const string Header = "id\tlayer\tname\tmode\tplanned\twent\tstood\tgrounded\tdrop_m\tdrift_m\tdeath_fall\tin_water\tpath\tpath_m\tpin_m\tresult\tnote";
        readonly ManualLogSource log;
        readonly Teleporter tp;
        readonly List<Place> places;
        readonly string file = Path.Combine(Paths.BepInExRootPath, "gamesomg-tp-survey.tsv");
        bool running;
        int index, ok, bad, tries;
        float measureAt;
        Place cur;
        Vector3 went;
        string mode;
        HashSet<string> done = new HashSet<string>();
        string firstFail;               // why the planned spot failed, kept on the row of the spot found in game
        bool godWas;
        GamePlayer godOn;               // whose infinite health the survey switched on (put back when it stops)

        // the game's own god-mode health flag (PlayerStats.InfinityHealth, what its console's "set godmode 1" sets), so a
        // level-1 character survives standing in every bandit camp and troll den; put back when the survey stops
        void God(GamePlayer p, bool on)
        {
            try
            {
                if (on) { godWas = p.Stats.InfinityHealth; p.Stats.InfinityHealth = true; godOn = p; }
                else if (godOn != null) { godOn.Stats.InfinityHealth = godWas; godOn = null; }
            }
            catch (Exception e) { log.LogWarning("survey god mode: " + e.Message); }
        }

        public Survey(ManualLogSource log, Teleporter tp, List<Place> places) { this.log = log; this.tp = tp; this.places = places; }

        public void Toggle(GamePlayer p, Action<string, bool> say)
        {
            if (running) { running = false; cur = null; God(p, false); say("Survey stopped. F9 resumes where it was.", false); return; }
            if (p == null) { say("Load a world first.", true); return; }
            done = new HashSet<string>();
            if (File.Exists(file))
            {
                foreach (var l in File.ReadAllLines(file))
                    if (l.Length > 0 && !l.StartsWith("id\t")) done.Add(l.Split('\t')[0]);
            }
            else File.WriteAllText(file, Header + "\n");
            index = 0; running = true; cur = null; ok = bad = 0;
            God(p, true);
            say("Survey started: " + (places.Count - done.Count) + " landings to stand on (" + done.Count + " already in the file). F9 stops.", false);
            log.LogInfo("survey -> " + file);
        }

        void Write(string row, bool pass)
        {
            File.AppendAllText(file, row + "\n");
            if (pass) ok++; else bad++;
            done.Add(cur.Id);
            cur = null;
        }

        public void Update(GamePlayer p, Action<string, bool> say)
        {
            if (!running) return;
            if (p == null) { running = false; cur = null; godOn = null; say("Survey stopped: no world.", true); return; }
            if (cur != null)
            {
                if (Time.unscaledTime < measureAt) return;
                bool pass = Measure(p, out string row);
                if (pass || tries >= 1) { Write(row, pass); return; }
                // the planned spot failed: find one in game, stand on it, measure that instead
                tries++;
                firstFail = row.Split('\t')[16];
                log.LogInfo("survey " + cur.Id + " planned failed (" + firstFail + "); searching in game");
                if (GameLanding(cur, out var alt)) { mode = "game"; Go(p, alt); return; }
                Write(row, false);
                return;
            }
            while (index < places.Count && done.Contains(places[index].Id)) index++;
            if (index >= places.Count)
            {
                running = false;
                God(p, false);
                say("Survey done: " + ok + " pass, " + bad + " fail. File: " + file, bad > 0);
                log.LogInfo("survey done: pass " + ok + ", fail " + bad);
                return;
            }
            cur = places[index++];
            tries = 0;
            firstFail = null;
            mode = "planned";
            if ((ok + bad) % 25 == 0) say("Survey: " + (ok + bad) + " stood on (" + ok + " pass, " + bad + " fail), " + (places.Count - done.Count) + " to go. F9 stops.", false);
            if (tp.Resolve(p, cur.Target, out var land, out string why)) { Go(p, land); return; }
            tries = 1;
            firstFail = "planned refused: " + why;
            if (GameLanding(cur, out var alt2)) { mode = "game"; Go(p, alt2); return; }
            went = default;
            Write(Row(cur, "refused", "", "", false, 0, 0, false, false, "none", 0, 0, false, why), false);
        }

        void Go(GamePlayer p, Vector3 where)
        {
            went = where;
            p.Respawn.TeleportTo(where);
            measureAt = Time.unscaledTime + Settle;
        }

        bool Measure(GamePlayer p, out string row)
        {
            Vector3 pos = p.transform.position;
            bool grounded = false, death = false, water = false;
            try { grounded = p.Movement.IsGrounded; } catch (Exception) { }
            try { death = p.Movement.IsFallingInDeathCollider; } catch (Exception) { }
            try { var w = p.WaterStateComp; water = w != null && w._inWater != null && w._inWater.CurrentValue != null; } catch (Exception) { }
            float drop = went.y - pos.y;
            float drift = Mathf.Sqrt((pos.x - went.x) * (pos.x - went.x) + (pos.z - went.z) * (pos.z - went.z));
            var pin = new Vector3(cur.PX, cur.PY, cur.PZ);
            float pinM = Vector3.Distance(pos, pin);
            string path = PathTo(pos, pin, out float pathM);
            bool pass = grounded && !death && !water && drop < 1.5f && drift < 3f && path == "complete" && pathM <= 25f && pinM >= 1.5f;
            string note = !grounded ? "not grounded" : death ? "death fall" : water ? "in water" : drop >= 1.5f ? "dropped" :
                          drift >= 3f ? "drifted" : path != "complete" ? "no walking path to the pin (" + path + ")" :
                          pathM > 25f ? "the walk to the pin is long" : pinM < 1.5f ? "on top of the pin" : "";
            if (mode == "game" && firstFail != null) note = (note.Length > 0 ? note + "; " : "") + "planned failed: " + firstFail;
            row = Row(cur, mode, Teleporter.Fmt(went), Teleporter.Fmt(pos), grounded, drop, drift, death, water, path, pathM, pinM, pass, note);
            return pass;
        }

        static string PathTo(Vector3 from, Vector3 pin, out float length)
        {
            length = 0;
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 2f, -1)) return "no navmesh here";
            if (!NavMesh.SamplePosition(pin, out NavMeshHit b, 4f, -1)) return "no navmesh at pin";
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, -1, path)) return "none";
            var c = path.corners;
            for (int i = 1; i < c.Length; i++) length += Vector3.Distance(c[i - 1], c[i]);
            return path.status == NavMeshPathStatus.PathComplete ? "complete" : path.status == NavMeshPathStatus.PathPartial ? "partial" : "invalid";
        }

        static bool GameLanding(Place pl, out Vector3 spot)
        {
            spot = default;
            var pin = new Vector3(pl.PX, pl.PY, pl.PZ);
            foreach (float r in new[] { 3f, 4f, 5f, 6f, 8f })
                for (int k = 0; k < 16; k++)
                {
                    float a = k * (float)Math.PI / 8f;
                    var c = pin + new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
                    if (!NavMesh.SamplePosition(c, out NavMeshHit h, 1.5f, -1)) continue;
                    if (Math.Abs(h.position.y - pin.y) > 3f) continue;
                    if (Vector3.Distance(h.position, pin) < 2.5f) continue;     // never ON it
                    if (PathTo(h.position, pin, out float m) != "complete" || m > 2.5f * r + 5f) continue;
                    spot = h.position + new Vector3(0, 0.05f, 0);
                    return true;
                }
            return false;
        }

        static string Row(Place p, string mode, string went, string stood, bool grounded, float drop, float drift, bool death,
                          bool water, string path, float pathM, float pinM, bool pass, string note)
        {
            string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
            return string.Join("\t", p.Id, p.Layer, p.Name, mode, Teleporter.Fmt(new Vector3(p.X, p.Y, p.Z)), went, stood,
                grounded ? "1" : "0", F(drop), F(drift), death ? "1" : "0", water ? "1" : "0", path, F(pathM), F(pinM),
                pass ? "OK" : "FAIL", (note ?? "").Replace('\t', ' '));
        }
    }
}
