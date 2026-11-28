// Behaviour: the one MonoBehaviour (injected into IL2CPP by BepInEx's AddComponent). Keys, the panel, the toast line, the
// landing watch. Keys are read two ways and de-duplicated per press: IMGUI key events (always delivered while the game has
// focus) and the Input System's Keyboard (the game's own input, which the panel switches off while it is open).
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using GamePlayer = _Project.Scripts.Player.Player;

namespace GamesOMG.LordOfUndeadTeleport
{
    public sealed class Behaviour : MonoBehaviour
    {
        public Behaviour(IntPtr ptr) : base(ptr) { }

        internal static Plugin Owner;
        Teleporter tp;
        Panel panel;
        string toast;
        bool toastBad;
        float toastUntil;
        bool f7Held, f10Held;
        int lastF7Frame = -1, lastF10Frame = -1;
        bool devicesOff;
        CursorLockMode oldLock;
        bool oldVisible;
#if SURVEY
        Survey survey;
#endif

        void Start()
        {
            var o = Owner;
            tp = new Teleporter(o.Log) { BringSquad = o.BringSquad.Value, SquadRadius = o.SquadRadius.Value };
            panel = new Panel(o.PlaceList)
            {
                OnGo = (t, label) => Teleport(t, label, fromPanel: true),
                PlayerPos = () => { var p = Teleporter.FindPlayer(); return p != null ? p.transform.position : (Vector3?)null; },
                OnBack = () => Say(tp.Back(Teleporter.FindPlayer())),
                OnCopyHere = CopyHere,
                Keys = o.TeleportKeyCode + " open · " + o.BackKeyCode + " back",
                BackKey = o.BackKeyCode.ToString(),
            };
#if SURVEY
            survey = new Survey(o.Log, tp, o.PlaceList);
#endif
            o.Log.LogInfo("ready: " + o.TeleportKey.Value + " teleport / panel, " + o.BackKey.Value + " back; " + o.PlaceList.Count + " places");
        }

        void Say(string msg, bool bad = false)
        {
            if (msg == null) return;
            if (panel.Open) { panel.Status = msg; panel.StatusBad = bad; }
            toast = msg; toastBad = bad; toastUntil = Time.unscaledTime + 5f;
        }

        // F7: a /tp line on the clipboard goes straight there; anything else opens the box
        void PressTeleport()
        {
            if (panel.Open) { ClosePanel(); return; }
            string clip = null;
            try { clip = GUIUtility.systemCopyBuffer; } catch (Exception) { }
            if (TpLine.TryParse(clip, out var t))
            {
                // already standing there (you pressed F7 again after arriving): open the box instead of going nowhere
                var p = Teleporter.FindPlayer();
                if (p != null)
                {
                    var d = p.transform.position;
                    if ((d.x - t.X) * (d.x - t.X) + (d.z - t.Z) * (d.z - t.Z) > 6f * 6f) { Teleport(t, null, fromPanel: false); return; }
                }
                else { Teleport(t, null, fromPanel: false); return; }
            }
            OpenPanel();
        }

        void Teleport(TpTarget t, string label, bool fromPanel)
        {
            var p = Teleporter.FindPlayer();
            string blocked = Teleporter.Blocked(p);
            if (blocked != null) { Say(blocked, true); return; }
            if (!tp.Resolve(p, t, out var land, out var why))
            {
                if (!panel.Open) OpenPanel(t.ToString());   // the line stays in the box, the reason under it
                Say(why, true);
                return;
            }
            if (label == null) label = Named(t);
            string msg = tp.Go(p, land, label ?? t.ToString());
            if (panel.Open) ClosePanel();
            Say(msg);
        }

        // a line copied from the map names its pin: the place whose landing it is
        static string Named(TpTarget t)
        {
            foreach (var pl in Owner.PlaceList)
                if (Math.Abs(pl.X - t.X) < 1.5f && Math.Abs(pl.Z - t.Z) < 1.5f && (!t.Y.HasValue || Math.Abs(pl.Y - t.Y.Value) < 2f))
                    return pl.Title;
            return null;
        }

        void CopyHere()
        {
            var p = Teleporter.FindPlayer();
            if (p == null) { Say("Load a world first.", true); return; }
            var v = p.transform.position;
            string line = new TpTarget(v.x, v.y, v.z).ToString();
            try { GUIUtility.systemCopyBuffer = line; Say("Copied " + line + ". Paste it to a friend or into F7 later."); }
            catch (Exception) { Say("Couldn't reach the clipboard. You're at " + line + ".", true); }
        }

        void OpenPanel(string text = null)
        {
            panel.Show(text);
            if (Owner.BlockGameInput.Value) SetDevices(false);
            oldLock = Cursor.lockState; oldVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        void ClosePanel()
        {
            panel.Hide();
            SetDevices(true);
            Cursor.lockState = oldLock; Cursor.visible = oldVisible;
        }

        void SetDevices(bool on)
        {
            if (on == !devicesOff) return;
            try
            {
                var k = Keyboard.current; var m = Mouse.current;
                if (on) { if (k != null) InputSystem.EnableDevice(k); if (m != null) InputSystem.EnableDevice(m); }
                else { if (k != null) InputSystem.DisableDevice(k); if (m != null) InputSystem.DisableDevice(m); }
                devicesOff = !on;
            }
            catch (Exception e) { Owner.Log.LogWarning("input devices: " + e.Message); devicesOff = false; }
        }

        void Update()
        {
            // the Input System route (off while the panel is open: then OnGUI's events carry the keys)
            try
            {
                var kb = Keyboard.current;
                if (kb != null && !devicesOff)
                {
                    var k7 = kb[Owner.TeleportInputKey];
                    if (k7 != null && k7.wasPressedThisFrame && Time.frameCount != lastF7Frame) { lastF7Frame = Time.frameCount; f7Held = true; PressTeleport(); }
                    var k10 = kb[Owner.BackInputKey];
                    if (k10 != null && k10.wasPressedThisFrame && Time.frameCount != lastF10Frame) { lastF10Frame = Time.frameCount; f10Held = true; Say(tp.Back(Teleporter.FindPlayer())); }
                }
            }
            catch (Exception) { /* no Input System keyboard: OnGUI's events still work */ }

            var p = Teleporter.FindPlayer();
            var w = tp.Watch(p);
            if (w != null) Say(w, true);
            // left the world (main menu, loading) with the box open: give the game its input back
            if (panel.Open && p == null) ClosePanel();
#if SURVEY
            survey.Update(p, Say);
#endif
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e == null) return;
            if (e.type == EventType.KeyUp)
            {
                if (e.keyCode == Owner.TeleportKeyCode) f7Held = false;
                if (e.keyCode == Owner.BackKeyCode) f10Held = false;
            }
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode == Owner.TeleportKeyCode)
                {
                    if (!f7Held && Time.frameCount != lastF7Frame) { lastF7Frame = Time.frameCount; f7Held = true; PressTeleport(); }
                    e.Use();
                    return;
                }
                if (e.keyCode == Owner.BackKeyCode)
                {
                    if (!f10Held && Time.frameCount != lastF10Frame) { lastF10Frame = Time.frameCount; f10Held = true; Say(tp.Back(Teleporter.FindPlayer())); }
                    e.Use();
                    return;
                }
#if SURVEY
                if (e.keyCode == KeyCode.F9 && !panel.Open) { survey.Toggle(Teleporter.FindPlayer(), Say); e.Use(); return; }
#endif
            }
            if (panel.Open)
            {
                bool wasOpen = panel.Open;
                if (panel.Key(e, KeyCode.None)) e.Use();
                if (wasOpen && !panel.Open) { ClosePanel(); return; }
                panel.Draw();
            }
            else if (toast != null && Time.unscaledTime < toastUntil)
            {
                DrawToast();
            }
        }

        GUIStyle toastStyle, toastShadow;
        void DrawToast()
        {
            float s = Mathf.Clamp(Screen.height / 1080f, 0.8f, 2.2f);
            if (toastStyle == null || toastStyle.fontSize != (int)(19 * s))
            {
                toastStyle = new GUIStyle(GUI.skin.label) { fontSize = (int)(19 * s), alignment = TextAnchor.UpperCenter, wordWrap = true };
                toastShadow = new GUIStyle(toastStyle);
                toastShadow.normal.textColor = new Color(0, 0, 0, 0.85f);
            }
            toastStyle.normal.textColor = toastBad ? new Color(1f, 0.70f, 0.62f) : new Color(0.92f, 0.96f, 0.92f);
            float w = Mathf.Min(900 * s, Screen.width * 0.9f);
            var r = new Rect((Screen.width - w) / 2, 70 * s, w, 80 * s);
            string text = "GamesOMG Teleport: " + toast;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, toastShadow);
            GUI.Label(r, text, toastStyle);
        }

        void OnDestroy() { SetDevices(true); }
        void OnApplicationFocus(bool focus) { if (!focus && panel != null && panel.Open) ClosePanel(); }
    }
}
