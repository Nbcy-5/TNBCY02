using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace TNBCY
{
    // Game screen: train menu, loading, driver controls, HUD and the frame loop. World / train / camera code is in the other Main*.cs files.
    public partial class Main : Node3D
    {
        public const string Version = "v27";
        static readonly string[] QualityNames = { "Fast", "Balanced", "High", "Ultra" };

        FrontEnd frontEnd; Label loadL, fpsL; HudBar hudBar; Control pauseOverlay; SettingsPanel pauseSettings;
        List<RouteInfo> routesList = new List<RouteInfo>(); List<TrainOption> trainsList = new List<TrainOption>();
        bool paused; float saveT; string curRoute = "", curConsist = "", curConsistName = "", curRouteName = "";
        Scenario sc; Camera3D cam; TrainVis playerVis, aiVis;
        readonly Dictionary<TSignal, StandardMaterial3D[]> sigMats = new Dictionary<TSignal, StandardMaterial3D[]>();
        readonly Dictionary<TNode, Label3D> swTags = new Dictionary<TNode, Label3D>();
        readonly List<Vector3> trackPts = new List<Vector3>();
        AudioStreamPlayer horn;
        TrainSounds sounds;   // speed-driven train sounds, set up in Data/train_sounds.json
        Label nextL, infoL, toastL, endL, updL, aiL;
        TouchControls touch; Control endPanel, updPanel; float toastT, noteT, startT, frameAcc, fpsT; int frameN, stableUp, lastRev; string apkUrl = "";
        bool electric; float pantK;

        static T LoadJson<T>(string path)
        {
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<T>(f.GetAsText(), new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true });
        }
        static StandardMaterial3D Mat(Color c) => new StandardMaterial3D { AlbedoColor = c, Roughness = 0.9f };
        static Transform3D Xf(Basis b, Vector3 p, Vector3 s) => new Transform3D(new Basis(b.X * s.X, b.Y * s.Y, b.Z * s.Z), p);
        static Vector3 Right(Vector3 d) => new Vector3(-d.Z, 0f, d.X);

        MeshInstance3D Box(Node parent, Vector3 pos, Basis rot, Vector3 size, Material m)
        {
            var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m };
            parent.AddChild(mi); mi.Transform = new Transform3D(rot, pos); return mi;
        }
        MultiMesh MM(Mesh mesh, Material m, int n, float visRange = 0f, bool colors = false)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh, InstanceCount = n };
            var inst = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = m };
            AddChild(inst); if (visRange > 0f) Far(inst, visRange); return mm;
        }
        MultiMesh MMBox(Material m, int n, float visRange = 0f) => MM(new BoxMesh { Size = Vector3.One }, m, n, visRange);
        // like MM, but with a fixed hide distance that the graphics level does not change
        MultiMesh MMLod(Mesh mesh, Material m, int n, float end)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = n };
            var inst = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = m };
            AddChild(inst); Lod(inst, 0f, end); return mm;
        }
        Label3D Tag(string text, Vector3 pos, Color c)
        {
            var l = new Label3D { Text = text, FontSize = 64, PixelSize = 0.02f, OutlineSize = 12, Modulate = c, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
            AddChild(l); l.Position = pos; return l;
        }
        void Toast(string m, bool good, float secs = 3f) { toastL.Text = m; toastL.Modulate = good ? new Color(.5f, 1f, .6f) : new Color(1f, .5f, .4f); toastT = secs; }
        void Note(string m) { if (noteT <= 0f) { Toast(m, false); noteT = 2f; } }
        // Rail iron: dark steel-brown, almost no metallic reflection, so the rails never glow white against the sky.
        static StandardMaterial3D IronMat(float metal, float rough) => new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = metal, Roughness = rough, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        static string AspectName(int a) => a == 2 ? " RED" : (a == 1 ? " YELLOW" : " GREEN");

        public override void _Ready()
        {
            Updater.LoadSavedPack();                       // content downloaded by the updater overrides built-in data
            DisplayServer.ScreenSetKeepOn(true);
            DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.SensorLandscape);    // flip the phone either way
            Settings.Load();
            RTPrefetch();                                  // real-track textures (granite ballast, concrete sleepers) are made while the menu is open
            ProcTex.Prefetch();                            // textures are made on worker threads while the menu is open
            GetViewport().Msaa3D = Viewport.Msaa.Msaa4X;
            BuildHud();
            ShowHome();
            AddChild(new Updater { Message = m => Toast(m, true), ApkAvailable = OnApk, Status = t => { if (frontEnd != null) frontEnd.SetUpdateStatus(t); } });
            GetWindow().FocusExited += SaveSession;       // the app going to the background saves the session
        }

        void ShowHome()
        {
            touch.Enabled = false; ApplyHudVisible();
            frontEnd.ShowHome(Session.Load());
        }

        void ResumeSession()
        {
            var s = Session.Load(); if (s == null) return;
            RouteInfo r = routesList.Find(x => x.id == s.route); TrainOption t = trainsList.Find(x => x.file == s.consist);
            if (r == null) r = new RouteInfo { id = s.route, name = s.sub, network = "network_demo" };
            if (t == null) t = new TrainOption { file = s.consist, name = s.title };
            StartGame(r, t, s);
        }

        async void StartGame(RouteInfo route, TrainOption train, SessionData resume)
        {
            try
            {
                frontEnd.Visible = false; loadL.Text = "Loading..."; loadL.Visible = true;
                curRoute = route.id; curConsist = train.file; curConsistName = train.name; curRouteName = route.name;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var net = LoadJson<NetworkSpec>("res://Data/" + route.network + ".json");
                var consist = LoadJson<ConsistSpec>("res://Data/" + train.file + ".json");
                LoadLocoCatalog();
                if (consist.randomElectricLoco && locoList.Count > 0)    // passenger / express / superfast: any electric (WAP) loco
                {
                    var chosen = locoList[(int)(GD.Randi() % (uint)locoList.Count)];
                    int li = consist.vehicles.FindIndex(x => x.powered); if (li < 0) li = 0;
                    var old = consist.vehicles[li];
                    var lv = LocoVehicle(chosen); lv.davisA = old.davisA; lv.davisB = old.davisB; lv.davisC = old.davisC;
                    consist.vehicles[li] = lv; consist.name = chosen.name + " + " + consist.name.Substring(0, consist.name.IndexOf(" (") > 0 ? consist.name.IndexOf(" (") : consist.name.Length);
                }
                ConsistSpec aiConsist = null;
                try { aiConsist = LoadJson<ConsistSpec>("res://Data/consist_ai.json"); } catch { }
                sc = new Scenario(net, consist, aiConsist) { OnEvent = (m, k) => { Toast(m, k == "ok"); RefreshSwitchTags(); } };

                BuildSky();
                cam = new Camera3D { Near = .5f, Far = 2600f, Fov = 72f }; AddChild(cam); cam.MakeCurrent();
                matGravel = TexMat(ProcTex.Gravel()); matConcrete = TexMat(ProcTex.Concrete());
                matConcrete.VertexColorUseAsAlbedo = true;   // sleepers get a slightly different shade each
                matSteel = Solid(new Color(.34f, .27f, .23f), .55f, .45f);
                matRail = IronMat(.20f, .62f); matFit = IronMat(.15f, .65f); matFit.VertexColorUseAsAlbedo = true;
                matWire = Solid(new Color(.18f, .18f, .2f), .4f, .5f); matMast = Solid(new Color(.55f, .57f, .6f), .3f, .6f);
                BuildRealTrack(); BuildScenery(); BuildStations(); BuildSignals(); BuildIndianRailwayObjects(); BuildLineSigns(); BuildWaterPipes(); BuildSwitchTags();
                playerVis = BuildTrain(sc.Train, null, null);
                if (sc.AiTrain != null) aiVis = BuildTrain(sc.AiTrain, new Color(.12f, .32f, .78f), new Color(.82f, .85f, .9f));
                BuildCab(); BuildHorn();
                if (sounds == null) { sounds = new TrainSounds(); AddChild(sounds); }

                electric = false;
                foreach (var c in sc.Train.Cars) { string nm = (c.Spec.name ?? "").ToLowerInvariant(); if (c.Spec.powered && !nm.Contains("demu") && !nm.Contains("diesel")) electric = true; }
                touch.Diesel = !electric;
                bool resumed = resume != null && Session.Restore(sc, resume, touch);
                if (!resumed) { touch.Notch = 0; touch.BrakeStep = 1; touch.Reverser = 0; touch.PantoUp = false; touch.Breaker = false; }
                lastRev = touch.Reverser; pantK = touch.PantoUp ? 1f : 0f; RefreshSwitchTags();
                ride.Reset(); lastEdge = null; saveT = 0f; paused = false;
                ApplyQuality(Settings.Gfx == 0 ? 1 : Settings.Gfx - 1); startT = 0f;
                loadL.Visible = false; touch.Enabled = true; ApplyHudVisible();
                if (resumed) Toast("Session resumed", true, 4f);
                else if (resume != null) Toast("Could not resume - starting a new run", false, 5f);
                else Toast(electric ? "Raise PANTO, close VCB, set reverser FWD, release the brake, then power up" : "Start ENGINE, set reverser FWD, release the brake, then power up", true, 7f);
            }
            catch (System.Exception ex) { GD.PrintErr(ex.ToString()); if (loadL != null) { loadL.Text = "Error: " + ex.Message; loadL.Visible = true; } }
        }

        void OnApk(string notes, string url) { apkUrl = url; updL.Text = "New version available\n" + notes; updPanel.Visible = true; touch.Enabled = false; }

        // ------------------------------------------------------------------ HUD
        void BuildHorn()
        {
            int rate = 22050, n = rate * 7 / 10; var d = new byte[n * 2];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, e = Mathf.Min(1f, t * 20f) * Mathf.Min(1f, (0.7f - t) * 10f);
                float s = e * .25f * (Mathf.Sign(Mathf.Sin(6.2832f * 220f * t)) + Mathf.Sign(Mathf.Sin(6.2832f * 277f * t))) * .5f;
                short v = (short)(s * 32767f); d[i * 2] = (byte)(v & 0xff); d[i * 2 + 1] = (byte)((v >> 8) & 0xff);
            }
            horn = new AudioStreamPlayer { Stream = new AudioStreamWav { Data = d, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false } };
            AddChild(horn); touch.OnHorn = () => horn.Play();
        }

        // ------------------------------------------------------------------ HUD
        static void Place(Control c, float al, float at, float ar, float ab, float ol, float ot, float orr, float ob)
        { c.AnchorLeft = al; c.AnchorTop = at; c.AnchorRight = ar; c.AnchorBottom = ab; c.OffsetLeft = ol; c.OffsetTop = ot; c.OffsetRight = orr; c.OffsetBottom = ob; }
        static StyleBoxFlat Flat(Color c, int r = 8) { var s = new StyleBoxFlat { BgColor = c }; s.SetCornerRadiusAll(r); return s; }

        Label Lbl(string t, int size, HorizontalAlignment a = HorizontalAlignment.Left)
        {
            var l = new Label { Text = t, HorizontalAlignment = a };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", Colors.White);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .75f));
            l.AddThemeConstantOverride("outline_size", 8);
            return l;
        }
        Button Btn(string t, Color c)
        {
            var b = new Button { Text = t, CustomMinimumSize = new Vector2(180, 64), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddThemeFontSizeOverride("font_size", 26);
            b.AddThemeStyleboxOverride("normal", Flat(c)); b.AddThemeStyleboxOverride("hover", Flat(c.Lightened(.1f))); b.AddThemeStyleboxOverride("pressed", Flat(c.Lightened(.25f)));
            return b;
        }
        PanelContainer Panel(out VBoxContainer vb)
        {
            var p = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(480, 220) };
            p.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center); p.GrowHorizontal = Control.GrowDirection.Both; p.GrowVertical = Control.GrowDirection.Both;
            vb = new VBoxContainer(); p.AddChild(vb); return p;
        }


        void BuildHud()
        {
            var layer = new CanvasLayer(); AddChild(layer);
            var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; root.SetAnchorsPreset(Control.LayoutPreset.FullRect); layer.AddChild(root);

            fadeRect = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false }; fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(fadeRect);
            hudBar = new HudBar { Visible = false }; hudBar.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(hudBar);
            fpsL = Lbl("", 18); Place(fpsL, 0, 0, 0, 0, 20, 88, 320, 114); fpsL.Visible = false; root.AddChild(fpsL);
            nextL = Lbl("", 20, HorizontalAlignment.Right); Place(nextL, 1, 0, 1, 0, -700, 88, -20, 116); root.AddChild(nextL);
            infoL = Lbl("", 20); Place(infoL, 0, 0, 0, 0, 20, 118, 640, 146); root.AddChild(infoL);
            aiL = Lbl("", 20, HorizontalAlignment.Right); Place(aiL, 1, 0, 1, 0, -640, 118, -20, 146); root.AddChild(aiL);
            toastL = Lbl("", 32, HorizontalAlignment.Center); Place(toastL, .5f, .3f, .5f, .3f, -620, 0, 620, 60); root.AddChild(toastL);
            loadL = Lbl("", 40, HorizontalAlignment.Center); Place(loadL, 0, .5f, 1, .5f, 0, -30, 0, 30); loadL.Visible = false; root.AddChild(loadL);

            touch = new TouchControls(); touch.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(touch);
            touch.OnSwitch = () => sc.ToggleSwitch();
            touch.OnMenu = OpenPause; touch.OnHudToggle = ApplyHudVisible;
            touch.OnPantoToggle = TogglePanto; touch.OnBreakerToggle = ToggleBreaker;
            touch.OnPrevCoach = () => wantFocus = Mathf.Max(0, focusCar - 1);
            touch.OnNextCoach = () => wantFocus = Mathf.Min(sc.Train.Cars.Count - 1, focusCar + 1);

            try { routesList = LoadJson<RouteList>("res://Data/routes.json").routes; } catch { }
            try { trainsList = LoadJson<TrainList>("res://Data/trains.json").trains; } catch { }
            if (routesList.Count == 0) routesList.Add(new RouteInfo { id = "sc_gt", name = "Secunderabad Jn - Ghatkesar", tag = "Main line", km = 20f, info = "Double line, 25 kV AC", network = "network_demo" });
            if (trainsList.Count == 0) trainsList.Add(new TrainOption { name = "WAP-7 + 12 coaches", file = "consist_demo", desc = "original train", coaches = 12 });
            frontEnd = new FrontEnd { OnStart = (r, t) => StartGame(r, t, null), OnResume = ResumeSession, OnQuit = () => GetTree().Quit(), SettingsChanged = OnSettingsChanged };
            root.AddChild(frontEnd); frontEnd.Setup(routesList, trainsList, Version);
            BuildPause(root);
            endPanel = Panel(out var vb1);
            endL = Lbl("", 34, HorizontalAlignment.Center); vb1.AddChild(endL);
            var again = Btn("Drive again", new Color(.94f, .63f, .19f)); vb1.AddChild(again);
            again.Pressed += () => GetTree().ReloadCurrentScene();
            root.AddChild(endPanel);

            updPanel = Panel(out var vb2);
            updL = Lbl("", 28, HorizontalAlignment.Center); vb2.AddChild(updL);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); vb2.AddChild(row);
            var dl = Btn("Download", new Color(.94f, .63f, .19f)); var later = Btn("Later", new Color(.24f, .36f, .44f)); row.AddChild(dl); row.AddChild(later);
            dl.Pressed += () => OS.ShellOpen(apkUrl);
            later.Pressed += () => { updPanel.Visible = false; touch.Enabled = true; };
            root.AddChild(updPanel);
        }

        void BuildPause(Control root)
        {
            var dim = new ColorRect { Color = new Color(0f, 0f, 0f, .62f), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(dim); pauseOverlay = dim;
            var cc = new CenterContainer(); cc.SetAnchorsPreset(Control.LayoutPreset.FullRect); dim.AddChild(cc);
            var pc = new PanelContainer { CustomMinimumSize = new Vector2(760, 0) };
            pc.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(.05f, .09f, .15f, .96f), new Color(Ui.Gold.R, Ui.Gold.G, Ui.Gold.B, .8f), 22, 2, 16)); cc.AddChild(pc);
            var m = new MarginContainer(); m.AddThemeConstantOverride("margin_left", 28); m.AddThemeConstantOverride("margin_right", 28); m.AddThemeConstantOverride("margin_top", 16); m.AddThemeConstantOverride("margin_bottom", 18); pc.AddChild(m);
            var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 10); m.AddChild(v);
            v.AddChild(Ui.MakeLabel("PAUSED", 34, Ui.GoldHi, true, HorizontalAlignment.Center));
            pauseSettings = new SettingsPanel { Changed = OnSettingsChanged }; pauseSettings.Build(); v.AddChild(pauseSettings);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); v.AddChild(row);
            var resume = Ui.MakeButton("RESUME", 26, true, new Vector2(0, 62)); resume.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(resume); resume.Pressed += ClosePause;
            var home = Ui.MakeButton("SAVE AND HOME", 26, false, new Vector2(0, 62)); home.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(home); home.Pressed += SaveAndHome;
        }

        void OpenPause() { if (sc == null || paused || sc.Ended) return; paused = true; touch.Enabled = false; pauseSettings.Refresh(); pauseOverlay.Visible = true; SaveSession(); }
        void ClosePause() { paused = false; pauseOverlay.Visible = false; touch.Enabled = sc != null && !sc.Ended; }
        void SaveAndHome() { SaveSession(); paused = false; GetTree().ReloadCurrentScene(); }

        void SaveSession()
        {
            if (sc == null || sc.Ended || touch == null) return;
            Session.Save(Session.Capture(sc, touch, curRoute, curConsist, curConsistName, curRouteName));
        }

        // the eye button hides / shows every on-screen control so the view is clear
        void ApplyHudVisible()
        {
            bool v = touch.HudVisible && sc != null;
            hudBar.Visible = v; infoL.Visible = v; aiL.Visible = v; nextL.Visible = v; fpsL.Visible = v && Settings.ShowFps; toastL.Visible = touch.HudVisible;
        }

        void OnSettingsChanged()
        {
            if (sc != null) ApplyQuality(Settings.Gfx == 0 ? quality : Settings.Gfx - 1);
            if (fpsL != null && touch != null) fpsL.Visible = Settings.ShowFps && touch.HudVisible && sc != null;
        }

        void NextStop(out string name, out float dist)
        {
            name = "End of line"; dist = -1f;
            var T = sc.Train; var e = T.FrontSeg.Edge; float acc = -T.FrontOffset;
            for (int hop = 0; hop < 12 && e != null; hop++)
            {
                float best = 1e9f; TStation bs = null;
                foreach (var stn in sc.Graph.Stations)
                {
                    if (stn.Edge != e || stn.Done || !stn.Stop) continue;
                    float d = acc + stn.Offset;
                    if (d >= -40f && d < best) { best = d; bs = stn; }
                }
                if (bs != null) { name = bs.Name; dist = Mathf.Max(0f, best); return; }
                var n = e.To; acc += e.Length; e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
        }

        string NextText()
        {
            var T = sc.Train; var e = T.FrontSeg.Edge; float acc = -T.FrontOffset, best = 1e9f; string txt = "";
            for (int hop = 0; hop < 5 && e != null; hop++)
            {
                if (sc.Graph.SigByEdge.TryGetValue(e, out var list))
                    foreach (var sg in list)
                    {
                        if (sg.Passed) continue;
                        float d = acc + sg.Offset;
                        if (d >= 0f && d < best) { best = d; txt = "Signal " + sg.Id + AspectName(sg.Aspect); }
                    }
                foreach (var stn in sc.Graph.Stations)
                {
                    if (stn.Edge != e || stn.Done || !stn.Stop) continue;
                    float d = acc + stn.Offset;
                    if (d >= -40f && d < best) { best = Mathf.Max(d, 0f); txt = "Station " + stn.Name; }
                }
                var n = e.To; float dn = acc + e.Length;
                if (n.IsSwitch && dn < best) { best = dn; txt = "Switch " + n.Name + " set " + (n.Sel == 0 ? "main" : "loop"); }
                acc += e.Length; e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
            return best > 1e8f ? "Next: end of line" : "Next: " + txt + " in " + Mathf.RoundToInt(best) + " m";
        }

        // -------------------------------------------------------------- cameras
        // ------------------------------------------------------------ driver controls
        void TogglePanto()
        {
            if (touch.Diesel) { touch.PantoUp = !touch.PantoUp; if (!touch.PantoUp) touch.Notch = 0; Toast(touch.PantoUp ? "Engine running" : "Engine stopped", touch.PantoUp); return; }
            if (!touch.PantoUp) { touch.PantoUp = true; Toast("Pantograph raised", true); }
            else { touch.PantoUp = false; touch.Breaker = false; touch.Notch = 0; Toast("Pantograph lowered - breaker opened", false); }
        }

        void ToggleBreaker()
        {
            if (!touch.PantoUp) { Toast("Raise the pantograph first", false); return; }
            if (touch.Breaker) { touch.Breaker = false; touch.Notch = 0; Toast("Breaker (VCB) open", false); }
            else if (touch.Notch > 0) Toast("Set power to 0 before closing the breaker", false);
            else { touch.Breaker = true; Toast("Breaker (VCB) closed - 25 kV on", true); }
        }

        void ApplyControls(float dt)
        {
            var T = sc.Train; var P = T.Phys;
            bool power = electric ? (touch.PantoUp && touch.Breaker) : touch.PantoUp;
            if (touch.Reverser != lastRev)
            {
                if (touch.Reverser == -1) { touch.Reverser = lastRev; Note("Reverse running is not available yet"); }
                else if (T.Speed > .5f || touch.Notch > 0) { touch.Reverser = lastRev; Note("Stop and set power to 0 to change direction"); }
                else lastRev = touch.Reverser;
            }
            if (touch.Notch > 0)
            {
                string why = null;
                if (!power) why = electric ? "No power: raise the pantograph and close the VCB" : "Start the engine first";
                else if (touch.Reverser != 1) why = "Set the reverser to FWD";
                else if (touch.BrakeStep >= 2) why = "Release the train brake before applying power";
                if (why != null) { touch.Notch = 0; Note(why); }
            }
            if (touch.Emergency && touch.Notch > 0) touch.Notch = 0;
            if (!touch.Emergency && P.Emergency && P.Speed > .1f) { touch.ForceEmergency(); Note("Stop before releasing the emergency brake"); }
            P.Emergency = touch.Emergency;
            P.BrakeHandle = Mathf.MoveToward(P.BrakeHandle, touch.BrakeValue, 1.8f * dt);
            float tgt = (power && touch.Reverser == 1) ? touch.Throttle : 0f;
            P.Throttle = Mathf.MoveToward(P.Throttle, tgt, (tgt > P.Throttle ? .4f : 1.2f) * dt);      // traction builds up and drops off gradually
            pantK = Mathf.MoveToward(pantK, touch.PantoUp ? 1f : 0f, dt / 2f);
        }

        // keeps the frame rate smooth: lowers resolution, shadows and draw distance when the phone struggles
        void AutoQuality()
        {
            float avg = frameAcc / Mathf.Max(1, frameN); fpsL.Text = Mathf.RoundToInt(1f / Mathf.Max(avg, .001f)) + " fps  " + QualityNames[quality];
            if (sc != null && startT > 8f && Settings.Gfx == 0)
            {
                if (avg > .030f && quality > 0) { ApplyQuality(quality - 1); stableUp = 0; Toast("Graphics set to " + QualityNames[quality] + " to keep it smooth", true); }
                else if (avg < .0165f && quality < 2) { if (++stableUp >= 4) { ApplyQuality(quality + 1); stableUp = 0; } }
                else stableUp = 0;
            }
            frameAcc = 0f; frameN = 0; fpsT = 0f;
        }

        // ----------------------------------------------------------- simulation
        public override void _Process(double delta)
        {
            float rd = (float)delta; frameAcc += rd; frameN++; fpsT += rd; if (fpsT >= 2f) AutoQuality();
            if (paused) { sounds?.Tick(0f, (float)delta, true); return; }
            if (sc == null || cam == null) return;
            float dt = Mathf.Min(rd, .05f); startT += dt; noteT -= dt;
            saveT += dt; if (saveT >= 8f && !sc.Ended) { saveT = 0f; SaveSession(); }
            var T = sc.Train; var P = T.Phys;
            ApplyControls(dt);
            sc.Update(dt); toastT -= dt; if (toastT <= 0f) toastL.Text = "";

            UpdateTrain(playerVis, activeMode == 0, pantK);
            if (aiVis != null) UpdateTrain(aiVis, false, 1f);
            var cols = new[] { Colors.Red, new Color(1f, .8f, .1f), new Color(.2f, 1f, .3f) };
            foreach (var kv in sigMats)
            {
                int active = kv.Key.Aspect == 2 ? 0 : (kv.Key.Aspect == 1 ? 1 : 2);
                for (int i = 0; i < 3; i++)
                {
                    bool on = i == active;
                    kv.Value[i].AlbedoColor = on ? cols[i] : cols[i].Darkened(.8f);
                    kv.Value[i].EmissionEnabled = on; kv.Value[i].Emission = cols[i];
                }
            }
            UpdateCamera(dt);

            float lim = sc.LimitKmh(T.FrontSeg.Edge), spd = T.Speed * 3.6f;
            sounds?.Tick(spd, dt, sc.Ended);
            hudBar.SpeedKmh = spd; hudBar.LimitKmh = lim; hudBar.Clock = sc.ClockText();
            string nsName; float nsDist; NextStop(out nsName, out nsDist);
            hudBar.NextName = nsName; hudBar.NextDist = nsDist < 0f ? "" : (nsDist < 1000f ? Mathf.RoundToInt(nsDist) + " m" : (nsDist / 1000f).ToString("0.0") + " km");
            nextL.Text = NextText();
            float km = T.Odo / 1000f, tot = sc.Net.routeLengthM / 1000f;
            string rv = touch.Reverser == 1 ? "FWD" : (touch.Reverser == 0 ? "NEU" : "REV");
            infoL.Text = rv + "  N" + touch.Notch + "  Brake " + Mathf.RoundToInt(P.BrakeLevel * 100f) + "%  Stop " + Mathf.RoundToInt(P.BrakingDistance(0f)) + " m  "
                + km.ToString("0.0") + (tot > 0f ? " / " + tot.ToString("0.0") : "") + " km" + (touch.ShowCoachButtons ? "  Coach " + (focusCar + 1) + "/" + T.Cars.Count : "");
            if (sc.AiTrain != null)
            {
                T.PointAt(T.Odo, out var pp, out _); sc.AiTrain.PointAt(sc.AiTrain.Odo, out var pa, out _);
                aiL.Text = "Opposing train: " + sc.Ai.Status + "  " + (pp.DistanceTo(pa) / 1000f).ToString("0.0") + " km";
            }
            var sw = sc.NextSwitch(out float sd);
            touch.SwitchLabel = sw == null ? "none ahead" : sw.Name + (sw.Sel == 0 ? " main" : " loop") + " " + Mathf.RoundToInt(sd) + "m";
            if (sc.Ended && !endPanel.Visible) { endL.Text = sc.EndMessage; endPanel.Visible = true; touch.Enabled = false; Session.Delete(); }
        }
    }
}
