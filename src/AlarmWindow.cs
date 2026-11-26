using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DashboardAlarms
{
    /// <summary>
    /// IMGUI windows: the per-chart alarm editor (opened from a chart's menu on the dashboard)
    /// and the alarm banner shown at the top of the screen whenever an alarm is raised.
    /// </summary>
    public static class AlarmWindow
    {
        private const int EditorWindowId = 0x0DA5_A1A0;
        private const int ListWindowId = 0x0DA5_A1A1;

        // editor state
        private static AlarmDefinition editing;
        private static bool editingIsNew;
        private static Rect editorRect = new Rect(0, 0, 440, 470);
        private static string thresholdText = "";
        private static string dropText = "";
        private static string averageText = "";
        private static string holdText = "";
        private static string repeatText = "";
        private static string snoozeText = "";

        // list state
        private static bool listOpen;
        private static Rect listRect = new Rect(0, 0, 620, 360);
        private static Vector2 listScroll;

        private static Rect bannerRect;
        private static GUIStyle bannerStyle;
        private static GUIStyle wrapLabel;
        private static GUIStyle bannerLineStyle;
        private static GUIStyle bannerSnoozedStyle;
        private static GUIStyle bannerTitleStyle;

        public static bool EditorOpen => editing != null;

        /// <summary>True when the mouse is over one of our windows (used to keep clicks from reaching the game).</summary>
        public static bool MouseOverUs
        {
            get
            {
                Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                float s = Scale;
                m /= s;
                return (editing != null && editorRect.Contains(m))
                    || (listOpen && listRect.Contains(m))
                    || (AlarmManager.ShownCount > 0 && bannerRect.Contains(m));
            }
        }

        private static readonly List<Rect> activeRects = new List<Rect>();

        /// <summary>GUI-space rects of everything currently on screen (for the input blocker).</summary>
        public static List<Rect> ActiveRects()
        {
            activeRects.Clear();
            bool visible = GameMain.data != null && GameMain.isRunning && UIRoot.instance != null && UIRoot.instance.uiGame.active;
            if (!visible)
            {
                return activeRects;
            }
            if (editing != null)
            {
                activeRects.Add(editorRect);
            }
            if (listOpen)
            {
                activeRects.Add(listRect);
            }
            if (AlarmManager.ShownCount > 0 && bannerRect.width > 0)
            {
                activeRects.Add(bannerRect);
            }
            return activeRects;
        }

        /// <summary>True when one of our text fields has keyboard focus.</summary>
        public static bool Typing => (editing != null || listOpen) && GUIUtility.keyboardControl != 0;

        private static float Scale => Mathf.Max(0.5f, Plugin.UiScale);

        public static void OpenEditor(StatPlan plan)
        {
            AlarmDefinition existing = AlarmManager.Find(plan.id);
            if (existing != null)
            {
                editing = existing;
                editingIsNew = false;
            }
            else
            {
                editing = AlarmManager.CreateFor(plan);
                editing.isDraft = true; // not live or saved until Create is pressed
                editingIsNew = true;
                if (StatReader.SupportsRate(plan))
                {
                    editing.metric = AlarmMetric.Production;
                }
            }
            thresholdText = editing.threshold.ToString("0.###", CultureInfo.InvariantCulture);
            dropText = editing.dropPercent.ToString("0.###", CultureInfo.InvariantCulture);
            averageText = editing.averageMinutes.ToString("0.###", CultureInfo.InvariantCulture);
            holdText = editing.holdSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            repeatText = editing.repeatSoundSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            snoozeText = editing.snoozeMinutes.ToString("0.###", CultureInfo.InvariantCulture);
            if (editorRect.x <= 0 && editorRect.y <= 0)
            {
                editorRect.x = (Screen.width / Scale - editorRect.width) / 2;
                editorRect.y = (Screen.height / Scale - editorRect.height) / 3;
            }
            GUIUtility.keyboardControl = 0;
        }

        public static void CloseEditor(bool discardNew)
        {
            if (editing != null && editingIsNew && discardNew)
            {
                AlarmManager.Remove(editing);
            }
            editing = null;
            editingIsNew = false;
            GUIUtility.keyboardControl = 0;
        }

        public static void ToggleList()
        {
            listOpen = !listOpen;
            if (listOpen && listRect.x <= 0 && listRect.y <= 0)
            {
                listRect.x = (Screen.width / Scale - listRect.width) / 2;
                listRect.y = (Screen.height / Scale - listRect.height) / 3;
            }
        }

        public static void Reset()
        {
            editing = null;
            editingIsNew = false;
            listOpen = false;
        }

        public static void Draw()
        {
            if (GameMain.data == null || !GameMain.isRunning || UIRoot.instance == null || !UIRoot.instance.uiGame.active)
            {
                return;
            }
            GUISkin oldSkin = GUI.skin;
            GUI.skin = GameSkin.Skin; // the game-themed look for everything drawn below
            EnsureStyles();
            Matrix4x4 old = GUI.matrix;
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1));
            try
            {
                DrawAll();
            }
            finally
            {
                GUI.matrix = old;
                GUI.skin = oldSkin;
            }
        }

        private static void DrawAll()
        {
            DrawBanner();
            if (editing != null)
            {
                if (!AlarmManager.Alarms.Contains(editing))
                {
                    editing = null; // its chart went away
                }
                else
                {
                    editorRect = GUILayout.Window(EditorWindowId, editorRect, DrawEditor, "Dashboard alarm");
                }
            }
            if (listOpen)
            {
                listRect = GUILayout.Window(ListWindowId, listRect, DrawList, "Dashboard alarms");
            }
        }

        private static void EnsureStyles()
        {
            if (bannerStyle != null)
            {
                return;
            }
            bannerStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
            };
            bannerStyle.normal.textColor = Color.white;
            wrapLabel = new GUIStyle(GUI.skin.label) { wordWrap = true };
            bannerLineStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            bannerLineStyle.normal.textColor = Color.white;
            bannerTitleStyle = new GUIStyle(bannerLineStyle) { fontSize = 15, fontStyle = FontStyle.Bold };
            bannerSnoozedStyle = new GUIStyle(bannerLineStyle) { fontStyle = FontStyle.Italic };
            bannerSnoozedStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        }

        // ---------------------------------------------------------------- banner

        private static void DrawBanner()
        {
            int total = AlarmManager.ShownCount;
            if (total == 0)
            {
                bannerRect = Rect.zero;
                bannerContentH = 0; // start from the estimate next time it appears
                return;
            }
            int alarming = AlarmManager.RaisedCount;
            int snoozed = total - alarming;

            // Size everything from the real text so nothing is cut off at any UI scale or font.
            const float pad = 8;
            const float gap = 6;
            float w = Mathf.Min(760, Screen.width / Scale - 40);
            float inner = w - 2 * pad;
            GUIStyle button = GUI.skin.button;

            // alarming first, then snoozed; at most 5 lines
            int shown = Math.Min(total, 5);
            var lines = new List<KeyValuePair<AlarmDefinition, string>>();
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (AlarmDefinition a in AlarmManager.Alarms)
                {
                    bool wanted = pass == 0 ? a.state == AlarmState.Alarming : a.state == AlarmState.Snoozed;
                    if (!wanted || lines.Count >= shown)
                    {
                        continue;
                    }
                    string mark = a.state == AlarmState.Snoozed ? $"(snoozed, {FormatDuration(AlarmManager.SnoozeSecondsLeft(a))} left)  " : "";
                    lines.Add(new KeyValuePair<AlarmDefinition, string>(a, $"{mark}{AlarmManager.Describe(a)}  -  now {AlarmManager.FormatValue(a, a.lastValue)}"));
                }
            }
            float rowsH = 0;
            bool confirmShown = false;
            foreach (KeyValuePair<AlarmDefinition, string> line in lines)
            {
                if (line.Key == confirmRemove)
                {
                    confirmShown = true;
                    rowsH += WrappedHeight(bannerLineStyle, RemoveQuestion(line.Key), inner - bannerLineStyle.margin.horizontal) + gap
                        + button.CalcHeight(new GUIContent(RemoveYes), 80) + gap;
                    continue;
                }
                float labelW = inner - RowButtonsWidth(button, line.Key) - gap;
                rowsH += Mathf.Max(button.CalcHeight(new GUIContent("x"), 80), WrappedHeight(LineStyle(line.Key), line.Value, labelW)) + gap;
            }
            if (!confirmShown)
            {
                confirmRemove = null; // its alarm cleared or scrolled out: forget the question
            }
            float headerH = button.CalcHeight(new GUIContent("List"), 60) + gap;
            float moreH = total > shown ? bannerLineStyle.CalcHeight(new GUIContent("x"), inner) : 0;
            float estimate = headerH + rowsH + moreH;
            // measured height from the last frame wins; the estimate only covers the very first frame
            float h = pad + (bannerContentH > 0 ? bannerContentH : estimate) + pad;
            bannerRect = new Rect((Screen.width / Scale - w) / 2, 70, w, h);

            // flashing red while anything is alarming; steady and dimmer when everything is snoozed
            // a dark game-style panel, washed red, with a bright red accent strip on the left that pulses
            float pulse = alarming > 0 ? Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 4f)) : 0f;
            Color oldBg = GUI.backgroundColor;
            Color oldColor = GUI.color;
            GUI.backgroundColor = alarming > 0
                ? Color.Lerp(new Color(0.24f, 0.05f, 0.07f, 0.95f), new Color(0.48f, 0.07f, 0.08f, 0.97f), pulse)
                : new Color(0.16f, 0.09f, 0.11f, 0.93f);
            GUI.Box(bannerRect, "", bannerStyle);
            GUI.backgroundColor = oldBg;
            GUI.color = alarming > 0
                ? Color.Lerp(new Color(1f, 0.25f, 0.2f, 0.8f), new Color(1f, 0.45f, 0.35f, 1f), pulse)
                : new Color(0.75f, 0.35f, 0.35f, 0.7f);
            GUI.DrawTexture(new Rect(bannerRect.x, bannerRect.y, 4, bannerRect.height), Texture2D.whiteTexture);
            GUI.color = oldColor;

            // the layout area is taller than the panel so content is never clipped while the size catches up
            GUILayout.BeginArea(new Rect(bannerRect.x + pad, bannerRect.y + pad, inner, Mathf.Max(h, estimate + 2 * pad) + 400));
            GUILayout.BeginHorizontal();
            string title = alarming > 0 ? $"{alarming} dashboard alarm{(alarming == 1 ? "" : "s")}" : "Dashboard alarms";
            if (snoozed > 0)
            {
                title += $" ({snoozed} snoozed)";
            }
            GUILayout.Label(title, bannerTitleStyle, GUILayout.ExpandWidth(true));
            // only useful with two or more alarms going off; with one it would just repeat that line's Snooze
            if (alarming >= 2 && GUILayout.Button(SnoozeAllLabel, GUILayout.Width(ButtonWidth(button, SnoozeAllLabel))))
            {
                AlarmManager.SnoozeAllRaised();
            }
            if (GUILayout.Button("List", GUILayout.Width(ButtonWidth(button, "List"))))
            {
                ToggleList();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(gap);

            foreach (KeyValuePair<AlarmDefinition, string> line in lines)
            {
                AlarmDefinition a = line.Key;
                if (a == confirmRemove)
                {
                    float qW = inner - bannerLineStyle.margin.horizontal;
                    GUILayout.Label(RemoveQuestion(a), bannerLineStyle, GUILayout.Width(qW), GUILayout.Height(WrappedHeight(bannerLineStyle, RemoveQuestion(a), qW)));
                    GUILayout.Space(gap);
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(RemoveYes, GUILayout.Width(ButtonWidth(button, RemoveYes))))
                    {
                        AlarmManager.Remove(a);
                        if (editing == a)
                        {
                            CloseEditor(false);
                        }
                        confirmRemove = null;
                    }
                    GUILayout.Space(gap);
                    if (GUILayout.Button(RemoveNo, GUILayout.Width(ButtonWidth(button, RemoveNo))))
                    {
                        confirmRemove = null;
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(gap);
                    continue;
                }
                string action = RowAction(a);
                float labelW = inner - RowButtonsWidth(button, a) - gap;
                GUILayout.BeginHorizontal();
                GUILayout.Label(line.Value, LineStyle(a), GUILayout.Width(labelW), GUILayout.Height(WrappedHeight(LineStyle(a), line.Value, labelW)));
                GUILayout.Space(gap);
                if (GUILayout.Button(action, GUILayout.Width(ButtonWidth(button, action))))
                {
                    if (a.state == AlarmState.Snoozed)
                    {
                        AlarmManager.Unsnooze(a);
                    }
                    else
                    {
                        AlarmManager.Snooze(a, a.snoozeMinutes);
                    }
                }
                GUILayout.Space(gap);
                if (GUILayout.Button(EditLabel, GUILayout.Width(ButtonWidth(button, EditLabel))))
                {
                    StatPlan plan = AlarmManager.ResolvePlan(GameMain.data, a);
                    if (plan != null)
                    {
                        OpenEditor(plan);
                    }
                }
                GUILayout.Space(gap);
                if (GUILayout.Button(RemoveAsk, GUILayout.Width(ButtonWidth(button, RemoveAsk))))
                {
                    confirmRemove = a;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
            if (total > shown)
            {
                GUILayout.Label($"...and {total - shown} more (List shows all)", bannerLineStyle);
            }
            // Measure where the content really ended (GUILayout adds margins the estimate can't see);
            // the next frame sizes the panel from this.
            GUILayout.Label(GUIContent.none, GUIStyle.none, GUILayout.Height(1));
            if (Event.current.type == EventType.Repaint)
            {
                bannerContentH = GUILayoutUtility.GetLastRect().yMax;
            }
            GUILayout.EndArea();
        }

        /// <summary>Height of the banner's content as last drawn (0 until the first repaint).</summary>
        private static float bannerContentH;

        private static readonly Dictionary<GUIStyle, GUIStyle> singleLine = new Dictionary<GUIStyle, GUIStyle>();

        /// <summary>A full-width wrapped label in the editor window, tall enough for every line.</summary>
        private static void Note(string text, GUIStyle style)
        {
            float width = editorRect.width - GUI.skin.window.padding.horizontal - style.margin.horizontal;
            GUILayout.Label(text, style, GUILayout.Height(WrappedHeight(style, text, width)));
        }

        /// <summary>
        /// Height a word-wrapped label needs at this width. GUIStyle.CalcHeight under-measures wrapped text
        /// with the game's font, so the lines are counted here by wrapping word by word with single-line
        /// measurements (which are accurate), with a little slack per line.
        /// </summary>
        private static float WrappedHeight(GUIStyle style, string text, float width)
        {
            if (!singleLine.TryGetValue(style, out GUIStyle one))
            {
                one = new GUIStyle(style) { wordWrap = false };
                singleLine[style] = one;
            }
            float pad = style.padding.horizontal;
            float avail = Mathf.Max(20f, width - pad);
            float lineH = one.CalcSize(new GUIContent("Ag")).y - style.padding.vertical;
            float spaceW = one.CalcSize(new GUIContent("a a")).x - one.CalcSize(new GUIContent("aa")).x;
            int lines = 1;
            float x = 0;
            foreach (string word in (text ?? "").Split(' '))
            {
                if (word.Length == 0)
                {
                    continue;
                }
                float w = one.CalcSize(new GUIContent(word)).x - pad;
                if (x > 0 && x + spaceW + w > avail)
                {
                    lines++;
                    x = w;
                }
                else
                {
                    x += (x > 0 ? spaceW : 0) + w;
                }
            }
            float counted = lines * lineH * 1.15f + style.padding.vertical + 2;
            return Mathf.Max(style.CalcHeight(new GUIContent(text), width), counted);
        }

        // Removing an alarm from the banner: a two-step "Remove..." -> "Delete alarm" / "Keep" so one stray click can't do it.
        private const string RemoveAsk = "Remove...";
        private const string RemoveYes = "Delete alarm";
        private const string RemoveNo = "Keep";
        private static AlarmDefinition confirmRemove;

        private static string RemoveQuestion(AlarmDefinition a) =>
            $"Delete this alarm? It stops watching {AlarmManager.Describe(a)} and won't sound again. "
            + "The chart stays on your dashboard. To be warned again later, add a new alarm from the chart's menu.";

        /// <summary>
        /// Width taken by a line's three buttons, including the margins GUILayout puts around every control
        /// (label margin + each button's margin + our 6px spaces), so the last button never runs past the edge.
        /// </summary>
        private static float RowButtonsWidth(GUIStyle button, AlarmDefinition a)
        {
            float buttons = ButtonWidth(button, RowAction(a)) + ButtonWidth(button, EditLabel) + ButtonWidth(button, RemoveAsk);
            float margins = 3 * button.margin.horizontal + bannerLineStyle.margin.horizontal;
            return buttons + margins + 3 * 6 + 4; // 3 spaces (label|snooze|edit|remove) + a little slack
        }

        private const string EditLabel = "Edit";
        private const string SnoozeAllLabel = "Snooze all";

        /// <summary>The one button on a banner line: Snooze while alarming, Unsnooze while snoozed.</summary>
        private static string RowAction(AlarmDefinition a) => a.state == AlarmState.Snoozed ? "Unsnooze" : SnoozeLabel(a);

        private static GUIStyle LineStyle(AlarmDefinition a) => a.state == AlarmState.Snoozed ? bannerSnoozedStyle : bannerLineStyle;

        private static string FormatDuration(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return s >= 60 ? $"{s / 60}:{s % 60:00}" : $"{s}s";
        }

        private static string SnoozeLabel(AlarmDefinition a) => $"Snooze {a.snoozeMinutes:0.#} min";

        private static float ButtonWidth(GUIStyle style, string text) => style.CalcSize(new GUIContent(text)).x + 12;

        // ---------------------------------------------------------------- editor

        private static void DrawEditor(int id)
        {
            AlarmDefinition a = editing;
            StatPlan plan = AlarmManager.ResolvePlan(GameMain.data, a);
            if (plan == null)
            {
                GUILayout.Label("This chart no longer exists.");
                if (GUILayout.Button("Close"))
                {
                    CloseEditor(true);
                }
                GUI.DragWindow();
                return;
            }

            Note($"Chart: {plan.displayName}  -  {StatReader.ItemName(a.itemId)}, {StatReader.AreaName(GameMain.data, a.astroFilter)}", wrapLabel);
            GUILayout.Space(4);

            a.enabled = GameSkin.Toggle(a.enabled, "Alarm enabled");
            GUILayout.Space(4);

            // metric
            GUILayout.Label("Watch", GameSkin.Header);
            if (plan is AstroItemProductionStatPlan pp)
            {
                int current = Array.IndexOf(RateMetrics, a.metric);
                int m = Choice(current < 0 ? 0 : current, RateMetricLabels, 3);
                if (RateMetrics[m] != a.metric)
                {
                    a.metric = RateMetrics[m];
                    a.ResetRuntime(); // old samples were of a different number
                    if (Metrics.NeedsMax(a.metric))
                    {
                        AlarmManager.RequestRecountSoon();
                    }
                }
                string window = StatReader.RateWindowName(pp);
                string note = pp.timeLevel == 5 ? " (the chart is set to totals, so the alarm uses 1 minute)" : "";
                switch (a.metric)
                {
                    case AlarmMetric.MaxProduction:
                        Note("The most this area's powered machines can make per minute, counting proliferator: the game's reference rate.", GameSkin.DimLabel);
                        break;
                    case AlarmMetric.UsePercent:
                        Note($"Consumption ({window} average{note}) as a % of max production. 90 means you are using 90% of what your machines can make.", GameSkin.DimLabel);
                        break;
                    default:
                        Note($"Uses the chart's {window} average{note}, the same number the chart shows.", GameSkin.DimLabel);
                        break;
                }
            }
            else
            {
                a.metric = AlarmMetric.Stock;
                GUILayout.Label("  Stock (total items in storage, as the chart shows)");
            }
            GUILayout.Space(4);

            // trigger
            GUILayout.Label("Trigger", GameSkin.Header);
            var trigger = (AlarmTrigger)Choice((int)a.trigger, new[] { "Below", "Above", "% drop from average" }, 3);
            if (trigger != a.trigger)
            {
                a.trigger = trigger;
                a.ResetRuntimeTimersOnly();
            }
            if (a.trigger == AlarmTrigger.DropPercent)
            {
                Row("Drop %", ref dropText, v => a.dropPercent = Math.Max(0.1, Math.Min(100, v)));
                Row("Average over (minutes)", ref averageText, v => a.averageMinutes = (float)Math.Max(0.5, v));
                GUILayout.BeginHorizontal();
                GUILayout.Label("The average freezes while a drop is in progress.", GameSkin.DimLabel);
                if (GUILayout.Button("Reset average", GUILayout.Width(ButtonWidth(GUI.skin.button, "Reset average"))))
                {
                    // accept the current level as the new normal
                    a.history.Clear();
                    a.lastAverage = double.NaN;
                    a.ResetRuntimeTimersOnly();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                string thresholdLabel = a.metric == AlarmMetric.Stock ? "Threshold (items)"
                    : a.metric == AlarmMetric.UsePercent ? "Threshold (%)" : "Threshold (per minute)";
                Row(thresholdLabel, ref thresholdText, v => a.threshold = v);
            }
            Row("Hold time (seconds)", ref holdText, v => a.holdSeconds = (float)Math.Max(0, v));
            GUILayout.Space(4);

            // alarm
            a.playSound = GameSkin.Toggle(a.playSound, "Play sound");
            if (a.playSound)
            {
                Note("Sounds (pick one or more; they play together)", GameSkin.DimLabel);
                int n = Sounds.Options.GetLength(0);
                for (int i = 0; i < n; i++)
                {
                    if (i % 3 == 0)
                    {
                        GUILayout.BeginHorizontal();
                    }
                    string sound = Sounds.Options[i, 0];
                    bool on = a.sounds.Contains(sound);
                    if (ChoiceButton(on, Sounds.Options[i, 1]))
                    {
                        if (on)
                        {
                            a.sounds.Remove(sound);
                        }
                        else
                        {
                            // keep the list in menu order so it reads the same everywhere
                            a.sounds.Add(sound);
                            a.sounds.Sort((x, y) => SoundIndex(x).CompareTo(SoundIndex(y)));
                        }
                    }
                    if (i % 3 == 2 || i == n - 1)
                    {
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.BeginHorizontal();
                if (a.sounds.Count == 0)
                {
                    GUILayout.Label("No sound picked: the alarm will be silent.", GameSkin.DimLabel);
                }
                GUILayout.FlexibleSpace();
                GUI.enabled = a.sounds.Count > 0;
                if (GUILayout.Button("Test", GUILayout.Width(60)))
                {
                    AlarmManager.PlaySounds(a.sounds, diagnose: true);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                Row("Repeat sound every (s, 0 = once)", ref repeatText, v => a.repeatSoundSeconds = (float)Math.Max(0, v));
            }
            Row("Snooze length (minutes)", ref snoozeText, v => a.snoozeMinutes = (float)Math.Max(0.1, v));

            GUILayout.Space(6);
            string avg = a.trigger == AlarmTrigger.DropPercent ? $"   average {AlarmManager.FormatValue(a, a.lastAverage)}" : "";
            string max = a.metric == AlarmMetric.UsePercent && a.hasMax ? $"   (max production {AlarmManager.Format(a.lastMax)}/min)" : "";
            if (Metrics.NeedsRecount(a.metric) && !a.hasValue)
            {
                Note("Now: waiting for the game to count machines and storage (a few seconds)...", GameSkin.DimLabel);
            }
            else
            {
                Note($"Now: {AlarmManager.FormatValue(a, a.lastValue)}{max}{avg}   state: {a.state}", wrapLabel);
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(editingIsNew ? "Create" : "Done"))
            {
                a.isDraft = false;
                a.ResetRuntimeTimersOnly();
                CloseEditor(false);
            }
            if (!editingIsNew && GUILayout.Button("Delete alarm"))
            {
                AlarmManager.Remove(a);
                CloseEditor(false);
            }
            if (GUILayout.Button(editingIsNew ? "Cancel" : "Close"))
            {
                CloseEditor(true);
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private static readonly AlarmMetric[] RateMetrics =
        {
            AlarmMetric.Production, AlarmMetric.Consumption, AlarmMetric.Net,
            AlarmMetric.MaxProduction, AlarmMetric.UsePercent,
        };

        private static readonly string[] RateMetricLabels =
        {
            "Production/min", "Consumption/min", "Net/min",
            "Max production/min", "Use % of max",
        };

        private static int SoundIndex(string sound)
        {
            for (int i = 0; i < Sounds.Options.GetLength(0); i++)
            {
                if (Sounds.Options[i, 0] == sound)
                {
                    return i;
                }
            }
            return int.MaxValue;
        }

        /// <summary>A row-wrapped set of buttons where exactly one is selected (and clearly highlighted).</summary>
        private static int Choice(int current, string[] labels, int perRow)
        {
            int result = current;
            for (int i = 0; i < labels.Length; i++)
            {
                if (i % perRow == 0)
                {
                    GUILayout.BeginHorizontal();
                }
                if (ChoiceButton(i == current, labels[i]))
                {
                    result = i;
                }
                if (i % perRow == perRow - 1 || i == labels.Length - 1)
                {
                    GUILayout.EndHorizontal();
                }
            }
            return result;
        }

        /// <summary>A button drawn tinted with a marker when on. Returns true when clicked.</summary>
        private static bool ChoiceButton(bool on, string label)
        {
            // selected: filled cyan with a bright edge (the game's "active tab" look)
            return GUILayout.Button(label, on ? GameSkin.SelectedButton : GUI.skin.button, GUILayout.MinWidth(120));
        }

        private static void Row(string label, ref string text, Action<double> apply)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(230));
            string next = GUILayout.TextField(text, GUILayout.Width(150));
            GUILayout.EndHorizontal();
            if (next != text)
            {
                text = next;
                if (TryParse(next, out double v))
                {
                    apply(v);
                }
            }
        }

        /// <summary>Accepts 1234, 1,234, 1.5k, 2M, 0.5G.</summary>
        public static bool TryParse(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s))
            {
                return false;
            }
            s = s.Trim().Replace(",", "").Replace("_", "");
            double mul = 1;
            char last = char.ToLowerInvariant(s[s.Length - 1]);
            if (last == 'k') { mul = 1e3; s = s.Substring(0, s.Length - 1); }
            else if (last == 'm') { mul = 1e6; s = s.Substring(0, s.Length - 1); }
            else if (last == 'g' || last == 'b') { mul = 1e9; s = s.Substring(0, s.Length - 1); }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                return false;
            }
            value = v * mul;
            return true;
        }

        // ---------------------------------------------------------------- list

        private static void DrawList(int id)
        {
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(280));
            if (AlarmManager.Alarms.Count == 0)
            {
                GUILayout.Label("No alarms yet. Open the Dashboard, click a production or storage chart's menu button, and choose \"Add alarm...\".", GameSkin.DimLabel);
            }
            AlarmDefinition remove = null;
            foreach (AlarmDefinition a in AlarmManager.Alarms)
            {
                GUILayout.BeginHorizontal();
                bool en = GameSkin.Toggle(a.enabled, "", GUILayout.Width(22));
                if (en != a.enabled)
                {
                    a.enabled = en;
                    a.ResetRuntimeTimersOnly();
                }
                GUILayout.Label($"[{a.state}] {AlarmManager.Describe(a)} - now {AlarmManager.FormatValue(a, a.lastValue)}", wrapLabel, GUILayout.ExpandWidth(true));
                if (a.IsRaised && GUILayout.Button("Snooze", GUILayout.Width(ButtonWidth(GUI.skin.button, "Snooze"))))
                {
                    AlarmManager.Snooze(a, a.snoozeMinutes);
                }
                if (a.state == AlarmState.Snoozed && GUILayout.Button("Unsnooze", GUILayout.Width(ButtonWidth(GUI.skin.button, "Unsnooze"))))
                {
                    AlarmManager.Unsnooze(a);
                }
                if (GUILayout.Button("Edit", GUILayout.Width(ButtonWidth(GUI.skin.button, "Edit"))))
                {
                    StatPlan plan = AlarmManager.ResolvePlan(GameMain.data, a);
                    if (plan != null)
                    {
                        OpenEditor(plan);
                    }
                }
                if (GUILayout.Button("Delete", GUILayout.Width(ButtonWidth(GUI.skin.button, "Delete"))))
                {
                    remove = a;
                }
                GUILayout.EndHorizontal();
            }
            if (remove != null)
            {
                if (editing == remove)
                {
                    CloseEditor(false);
                }
                AlarmManager.Remove(remove);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close"))
            {
                listOpen = false;
            }
            GUI.DragWindow();
        }
    }
}
