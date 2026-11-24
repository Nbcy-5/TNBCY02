using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    public class RouteInfo
    {
        public string id = "", name = "", tag = "", info = "", network = "network_demo";
        public float km;
        public List<string> stations = new List<string>(); public List<float> stationKm = new List<float>();
        public bool available = true;
    }
    public class RouteList { public List<RouteInfo> routes = new List<RouteInfo>(); }
    public class TrainOption { public string name = "", file = "", desc = "", kind = ""; public int coaches = 8; }
    public class TrainList { public List<TrainOption> trains = new List<TrainOption>(); }

    // Home screen with animated railway backdrop, route and train selection, settings.
    public partial class FrontEnd : Control
    {
        public System.Action<RouteInfo, TrainOption> OnStart;
        public System.Action OnResume, OnQuit, SettingsChanged;
        Control home, routesScr, trainsScr, settingsScr;
        Button resumeB; Label resumeSub, trainsSub, verL, updStatL;
        HBoxContainer routeRow, trainRow;
        SettingsPanel settings;
        List<RouteInfo> routes = new List<RouteInfo>(); List<TrainOption> trains = new List<TrainOption>();
        RouteInfo chosen; float t; string version = "";
        Texture2D logoTex;

        public void SetUpdateStatus(string text) { if (updStatL != null) updStatL.Text = text; }

        public override void _Process(double delta) { t += (float)delta; if (Visible) QueueRedraw(); }

        // ------------------------------------------------------------- backdrop
        void Sky(float y0, float y1, Color a, Color b)
        {
            DrawPolygon(new[] { new Vector2(0f, y0), new Vector2(Size.X, y0), new Vector2(Size.X, y1), new Vector2(0f, y1) }, new[] { a, a, b, b });
        }

        public override void _Draw()
        {
            float w = Size.X, h = Size.Y, hz = h * .50f, cx = w * .5f, unit = w * .14f;
            Sky(0f, hz * .55f, new Color(.03f, .07f, .16f), new Color(.10f, .15f, .33f));
            Sky(hz * .55f, hz * .85f, new Color(.10f, .15f, .33f), new Color(.58f, .30f, .40f));
            Sky(hz * .85f, hz, new Color(.58f, .30f, .40f), new Color(1f, .66f, .32f));
            for (int i = 4; i >= 1; i--) DrawCircle(new Vector2(cx, hz - 4f), 38f + i * 34f, new Color(1f, .62f, .25f, .05f));
            DrawCircle(new Vector2(cx, hz - 4f), 46f, new Color(1f, .86f, .55f, .95f));
            for (int layer = 0; layer < 2; layer++)
            {
                var pts = new List<Vector2> { new Vector2(0f, hz + 2f) }; float amp = layer == 0 ? 46f : 28f, ph = layer == 0 ? 1.3f : 4.1f;
                for (int i = 0; i <= 40; i++) { float x = w * i / 40f; pts.Add(new Vector2(x, hz - amp * (.5f + .5f * Mathf.Sin(x * .0042f + ph)) - amp * .35f * Mathf.Sin(x * .011f + ph * 2f))); }
                pts.Add(new Vector2(w, hz + 2f));
                DrawColoredPolygon(pts.ToArray(), layer == 0 ? new Color(.16f, .15f, .27f) : new Color(.08f, .10f, .17f));
            }
            DrawRect(new Rect2(0f, hz, w, h - hz), new Color(.05f, .07f, .10f));

            float sp = .6f, frac = (t * 2.2f) % 1f; const int N = 64;
            for (int tr = -1; tr <= 1; tr++)
            {
                float L = tr * 3.7f;
                DrawColoredPolygon(new[] { new Vector2(cx + L * unit / 60f - 3f, hz), new Vector2(cx + L * unit / 60f + 3f, hz), new Vector2(cx + (L + 2.1f) * unit, h), new Vector2(cx + (L - 2.1f) * unit, h) }, new Color(.15f, .14f, .13f));
                for (int i = 0; i < N; i++)
                {
                    float z = 1f + (i - frac) * sp; if (z < 1f) continue;
                    float y = hz + (h - hz) / z, hw = 1.65f * unit / z, a = Mathf.Clamp(1.3f - z / 34f, 0f, 1f);
                    float xm = cx + L * unit / z;
                    DrawLine(new Vector2(xm - hw, y), new Vector2(xm + hw, y), new Color(.45f, .43f, .40f, a), Mathf.Max(1.2f, 9f / z), true);
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    float zf = 70f;
                    var a0 = new Vector2(cx + (L + side) * unit, h); var a1 = new Vector2(cx + (L + side) * unit / zf, hz + (h - hz) / zf);
                    DrawLine(a0, a1, new Color(.70f, .73f, .78f, .9f), 3.2f, true); DrawLine(a0, a1, new Color(1f, 1f, 1f, .55f), 1.2f, true);
                }
            }
            // overhead masts and wire
            Vector2 prevL = Vector2.Zero, prevR = Vector2.Zero; bool havePrev = false;
            for (int j = 0; j < 22; j++)
            {
                float z = 2f + (j - frac) * 2.4f; if (z < 1.2f) { havePrev = false; continue; }
                float yb = hz + (h - hz) / z, ht = (h - hz) / z * .24f, a = Mathf.Clamp(1.3f - z / 48f, 0f, 1f), wd = Mathf.Max(1.2f, 7f / z);
                var bl = new Vector2(cx - 5.8f * unit / z, yb); var br = new Vector2(cx + 5.8f * unit / z, yb);
                DrawLine(bl, bl + new Vector2(0f, -ht), new Color(.55f, .6f, .68f, a), wd, true); DrawLine(br, br + new Vector2(0f, -ht), new Color(.55f, .6f, .68f, a), wd, true);
                var tl = bl + new Vector2(0f, -ht); var tr2 = br + new Vector2(0f, -ht);
                DrawLine(tl, tr2, new Color(.55f, .6f, .68f, a * .8f), wd, true);
                if (havePrev) { DrawLine(prevL, tl, new Color(.8f, .85f, .9f, a * .45f), 1.2f, true); DrawLine(prevR, tr2, new Color(.8f, .85f, .9f, a * .45f), 1.2f, true); }
                prevL = tl; prevR = tr2; havePrev = true;
            }
            Sky(hz - 30f, hz + 50f, new Color(1f, .70f, .40f, 0f), new Color(1f, .62f, .32f, .22f));
            Sky(h * .5f, h, new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, .62f));
            Sky(0f, h * .22f, new Color(0f, 0f, 0f, .35f), new Color(0f, 0f, 0f, 0f));
        }

        // ------------------------------------------------------------ building
        // v27: SetAnchorsAndOffsetsPreset (not SetAnchorsPreset) so the screen really fills its parent; SetAnchorsPreset keeps the old 0 x 0 size.
        Control Screen() { var c = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false }; c.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(c); return c; }

        Button BackButton(Control parent, System.Action act)
        {
            var b = Ui.MakeButton("<  BACK", 22, false, new Vector2(150, 52)); Ui.Place(b, 0, 0, 0, 0, 30, 26, 180, 78); parent.AddChild(b); b.Pressed += act; return b;
        }

        public void Setup(List<RouteInfo> r, List<TrainOption> tr, string ver)
        {
            routes = r; trains = tr; version = ver;
            MouseFilter = MouseFilterEnum.Stop; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            logoTex = ResourceLoader.Exists("res://logo.png") ? GD.Load<Texture2D>("res://logo.png") : null;
            BuildHome(); BuildRoutes(); BuildTrains(); BuildSettings();
        }

        void BuildHome()
        {
            home = Screen();
            var logo = new TextureRect { Texture = logoTex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
            Ui.Place(logo, .5f, 0, .5f, 0, -380, 18, 380, 232); home.AddChild(logo);
            var vb = new VBoxContainer(); vb.AddThemeConstantOverride("separation", 14); Ui.Place(vb, .5f, 0, .5f, 0, -250, 258, 250, 600); home.AddChild(vb);
            var play = Ui.MakeButton("PLAY", 34, true, new Vector2(500, 74)); vb.AddChild(play); play.Pressed += () => Open(routesScr);
            resumeB = Ui.MakeButton("RESUME SESSION", 30, false, new Vector2(500, 74)); vb.AddChild(resumeB); resumeB.Pressed += () => { if (OnResume != null) OnResume(); };
            resumeSub = Ui.MakeLabel("", 16, Ui.Dim, false, HorizontalAlignment.Center); vb.AddChild(resumeSub);
            var set = Ui.MakeButton("SETTINGS", 30, false, new Vector2(500, 74)); vb.AddChild(set); set.Pressed += () => Open(settingsScr);
            verL = Ui.MakeLabel("TNBCY  " + version, 16, Ui.Dim, false); Ui.Place(verL, 0, 1, 0, 1, 26, -44, 400, -12); home.AddChild(verL);
            updStatL = Ui.MakeLabel("", 15, Ui.Dim, false); Ui.Place(updStatL, 0, 1, 1, 1, 26, -70, -26, -46); home.AddChild(updStatL);   // what the update check found
            var quit = Ui.MakeButton("EXIT", 20, false, new Vector2(120, 48)); Ui.Place(quit, 1, 1, 1, 1, -150, -72, -30, -24); home.AddChild(quit); quit.Pressed += () => { if (OnQuit != null) OnQuit(); };
        }

        void Header(Control parent, string title, out Label sub)
        {
            var l = Ui.MakeLabel(title, 38, Ui.GoldHi, true, HorizontalAlignment.Center); Ui.Place(l, .5f, 0, .5f, 0, -400, 20, 400, 76); parent.AddChild(l);
            sub = Ui.MakeLabel("", 20, Ui.Dim, false, HorizontalAlignment.Center); Ui.Place(sub, .5f, 0, .5f, 0, -400, 78, 400, 112); parent.AddChild(sub);
        }

        PanelContainer Card(Vector2 min, out VBoxContainer v)
        {
            var p = new PanelContainer { CustomMinimumSize = min, MouseFilter = MouseFilterEnum.Pass };
            p.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(.05f, .09f, .15f, .90f), new Color(Ui.Gold.R, Ui.Gold.G, Ui.Gold.B, .75f), 22, 2, 16));
            var m = new MarginContainer(); m.AddThemeConstantOverride("margin_left", 22); m.AddThemeConstantOverride("margin_right", 22); m.AddThemeConstantOverride("margin_top", 18); m.AddThemeConstantOverride("margin_bottom", 20);
            p.AddChild(m); v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 10); m.AddChild(v); return p;
        }

        ScrollContainer Scroller(Control parent, out HBoxContainer row)
        {
            var sc = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
            Ui.Place(sc, 0, 0, 1, 1, 30, 130, -30, -30); parent.AddChild(sc);
            var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; sc.AddChild(center);
            row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 26); center.AddChild(row); return sc;
        }

        void BuildRoutes()
        {
            routesScr = Screen(); Label sub; Header(routesScr, "SELECT ROUTE", out sub); sub.Text = "Choose where you want to drive";
            BackButton(routesScr, () => Open(home));
            Scroller(routesScr, out routeRow);
            foreach (var r in routes)
            {
                var ri = r; VBoxContainer v; var card = Card(new Vector2(500, 430), out v);
                v.AddChild(Ui.MakeLabel(ri.tag.ToUpperInvariant(), 16, ri.available ? Ui.Gold : Ui.Dim, true));
                var name = Ui.MakeLabel(ri.name, 28, Colors.White, true); name.AutowrapMode = TextServer.AutowrapMode.WordSmart; name.CustomMinimumSize = new Vector2(440, 0); v.AddChild(name);
                if (ri.available && ri.stations.Count > 0)
                {
                    var g = new RouteGlyph { Names = ri.stations, Km = ri.stationKm, Total = ri.km, CustomMinimumSize = new Vector2(440, 150) }; v.AddChild(g);
                }
                var info = Ui.MakeLabel(ri.info, 17, Ui.Dim); info.AutowrapMode = TextServer.AutowrapMode.WordSmart; info.CustomMinimumSize = new Vector2(440, 0); v.AddChild(info);
                var sp = new Control { SizeFlagsVertical = SizeFlags.ExpandFill }; v.AddChild(sp);
                if (ri.available)
                {
                    v.AddChild(Ui.MakeLabel(ri.km.ToString("0") + " km  ·  " + ri.stations.Count + " stations", 20, Colors.White, true));
                    var go = Ui.MakeButton("SELECT ROUTE", 24, true, new Vector2(440, 60)); v.AddChild(go); go.Pressed += () => { chosen = ri; BuildTrainCards(); Open(trainsScr); };
                }
                else { var soon = Ui.MakeButton("COMING SOON", 24, false, new Vector2(440, 60)); soon.Disabled = true; v.AddChild(soon); }
                routeRow.AddChild(card);
            }
        }

        void BuildTrains()
        {
            trainsScr = Screen(); Header(trainsScr, "SELECT TRAIN", out trainsSub);
            BackButton(trainsScr, () => Open(routesScr));
            Scroller(trainsScr, out trainRow);
        }

        void BuildTrainCards()
        {
            foreach (var c in trainRow.GetChildren()) c.QueueFree();
            trainsSub.Text = chosen != null ? chosen.name : "";
            foreach (var tr in trains)
            {
                var to = tr; VBoxContainer v; var card = Card(new Vector2(330, 400), out v);
                v.AddChild(Ui.MakeLabel((string.IsNullOrEmpty(to.kind) ? "TRAIN" : to.kind).ToUpperInvariant(), 15, Ui.Gold, true));
                var name = Ui.MakeLabel(to.name, 26, Colors.White, true); name.AutowrapMode = TextServer.AutowrapMode.WordSmart; name.CustomMinimumSize = new Vector2(286, 0); v.AddChild(name);
                v.AddChild(new TrainGlyph { Coaches = to.coaches, CustomMinimumSize = new Vector2(286, 90) });
                var d = Ui.MakeLabel(to.desc, 17, Ui.Dim); d.AutowrapMode = TextServer.AutowrapMode.WordSmart; d.CustomMinimumSize = new Vector2(286, 0); v.AddChild(d);
                v.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
                var go = Ui.MakeButton("DRIVE", 26, true, new Vector2(286, 62)); v.AddChild(go); go.Pressed += () => { if (OnStart != null) OnStart(chosen, to); };
                trainRow.AddChild(card);
            }
        }

        void BuildSettings()
        {
            settingsScr = Screen(); Label sub; Header(settingsScr, "SETTINGS", out sub); sub.Text = "Saved automatically";
            BackButton(settingsScr, () => Open(home));
            var p = new PanelContainer(); Ui.Place(p, .5f, 0, .5f, 0, -400, 124, 400, 124); p.GrowVertical = GrowDirection.End;
            p.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(.05f, .09f, .15f, .92f), new Color(Ui.Gold.R, Ui.Gold.G, Ui.Gold.B, .75f), 22, 2, 16));
            var m = new MarginContainer(); m.AddThemeConstantOverride("margin_left", 30); m.AddThemeConstantOverride("margin_right", 30); m.AddThemeConstantOverride("margin_top", 18); m.AddThemeConstantOverride("margin_bottom", 18); p.AddChild(m);
            settings = new SettingsPanel { Changed = () => { if (SettingsChanged != null) SettingsChanged(); } }; settings.Build(); m.AddChild(settings);
            settingsScr.AddChild(p);
        }

        // ---------------------------------------------------------------- flow
        void Open(Control c)
        {
            home.Visible = c == home; routesScr.Visible = c == routesScr; trainsScr.Visible = c == trainsScr; settingsScr.Visible = c == settingsScr;
            if (c == settingsScr) settings.Refresh();
        }

        public void ShowHome(SessionData s)
        {
            Visible = true; MouseFilter = MouseFilterEnum.Stop;
            bool has = s != null;
            resumeB.Disabled = !has;
            resumeSub.Text = has ? s.title + "  -  " + s.sub + "  -  " + s.clock : "No saved session yet";
            Open(home);
        }
    }
}
