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
                    || (AlarmManager.RaisedCount > 0 && bannerRect.Contains(m));
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
            if (AlarmManager.RaisedCount > 0 && bannerRect.width > 0)
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
            EnsureStyles();
            Matrix4x4 old = GUI.matrix;
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1));

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
            GUI.matrix = old;
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
        }

        // ---------------------------------------------------------------- banner

        private static void DrawBanner()
        {
            int raised = AlarmManager.RaisedCount;
            if (raised == 0)
            {
                bannerRect = Rect.zero;
                return;
            }
            float w = 560;
            float lineH = 26;
            int shown = Math.Min(raised, 5);
            float h = 34 + shown * lineH + 6;
            bannerRect = new Rect((Screen.width / Scale - w) / 2, 70, w, h);

            bool anyAlarming = false;
            foreach (AlarmDefinition a in AlarmManager.Alarms)
            {
                if (a.state == AlarmState.Alarming)
                {
                    anyAlarming = true;
                    break;
                }
            }
            float pulse = anyAlarming ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 4f)) : 0.45f;
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.9f, 0.1f, 0.1f, pulse);
            GUI.Box(bannerRect, "", bannerStyle);
            GUI.Box(bannerRect, "", bannerStyle);
            GUI.backgroundColor = oldBg;

            GUILayout.BeginArea(new Rect(bannerRect.x + 8, bannerRect.y + 4, bannerRect.width - 16, bannerRect.height - 8));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"!! {raised} dashboard alarm{(raised == 1 ? "" : "s")}", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Ack all", GUILayout.Width(70)))
            {
                AlarmManager.AcknowledgeAll();
            }
            if (GUILayout.Button("Snooze all", GUILayout.Width(80)))
            {
                AlarmManager.SnoozeAllRaised();
            }
            if (GUILayout.Button("List", GUILayout.Width(50)))
            {
                ToggleList();
            }
            GUILayout.EndHorizontal();

            int drawn = 0;
            foreach (AlarmDefinition a in AlarmManager.Alarms)
            {
                if (!a.IsRaised)
                {
                    continue;
                }
                if (drawn++ >= shown)
                {
                    break;
                }
                GUILayout.BeginHorizontal();
                string mark = a.state == AlarmState.Acknowledged ? "(ack) " : "";
                GUILayout.Label($"{mark}{AlarmManager.Describe(a)} - now {AlarmManager.FormatValue(a, a.lastValue)}", GUILayout.ExpandWidth(true));
                GUI.enabled = a.state == AlarmState.Alarming;
                if (GUILayout.Button("Ack", GUILayout.Width(44)))
                {
                    AlarmManager.Acknowledge(a);
                }
                GUI.enabled = true;
                if (GUILayout.Button($"Snooze {a.snoozeMinutes:0.#}m", GUILayout.Width(90)))
                {
                    AlarmManager.Snooze(a, a.snoozeMinutes);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

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

            GUILayout.Label($"Chart: {plan.displayName}  -  {StatReader.ItemName(a.itemId)}, {StatReader.AreaName(GameMain.data, a.astroFilter)}", wrapLabel);
            GUILayout.Space(4);

            a.enabled = GUILayout.Toggle(a.enabled, " Alarm enabled");
            GUILayout.Space(4);

            // metric
            GUILayout.Label("Watch");
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
                        GUILayout.Label("The most this area's powered machines can make per minute, counting proliferator: the game's reference rate.", wrapLabel);
                        break;
                    case AlarmMetric.UsePercent:
                        GUILayout.Label($"Consumption ({window} average{note}) as a % of max production. 90 means you are using 90% of what your machines can make.", wrapLabel);
                        break;
                    default:
                        GUILayout.Label($"Uses the chart's {window} average{note}, the same number the chart shows.", wrapLabel);
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
            GUILayout.Label("Trigger");
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
                GUILayout.Label("The average freezes while a drop is in progress.", wrapLabel);
                if (GUILayout.Button("Reset average", GUILayout.Width(110)))
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
            a.playSound = GUILayout.Toggle(a.playSound, " Play sound");
            if (a.playSound)
            {
                GUILayout.Label("Sounds (pick one or more; they play together)", wrapLabel);
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
                    GUILayout.Label("No sound picked: the alarm will be silent.", wrapLabel);
                }
                GUILayout.FlexibleSpace();
                GUI.enabled = a.sounds.Count > 0;
                if (GUILayout.Button("Test", GUILayout.Width(60)))
                {
                    AlarmManager.PlaySounds(a.sounds);
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
                GUILayout.Label("Now: waiting for the game to count machines and storage (a few seconds)...", wrapLabel);
            }
            else
            {
                GUILayout.Label($"Now: {AlarmManager.FormatValue(a, a.lastValue)}{max}{avg}   state: {a.state}", wrapLabel);
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
            Color bg = GUI.backgroundColor;
            Color fg = GUI.contentColor;
            if (on)
            {
                GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
                GUI.contentColor = new Color(1f, 0.92f, 0.45f);
            }
            bool clicked = GUILayout.Button(on ? "● " + label : label, GUILayout.MinWidth(120));
            GUI.backgroundColor = bg;
            GUI.contentColor = fg;
            return clicked;
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
                GUILayout.Label("No alarms yet. Open the Dashboard, click a production or storage chart's menu button, and choose \"Alarm...\".", wrapLabel);
            }
            AlarmDefinition remove = null;
            foreach (AlarmDefinition a in AlarmManager.Alarms)
            {
                GUILayout.BeginHorizontal();
                bool en = GUILayout.Toggle(a.enabled, "", GUILayout.Width(20));
                if (en != a.enabled)
                {
                    a.enabled = en;
                    a.ResetRuntimeTimersOnly();
                }
                GUILayout.Label($"[{a.state}] {AlarmManager.Describe(a)} - now {AlarmManager.FormatValue(a, a.lastValue)}", wrapLabel, GUILayout.ExpandWidth(true));
                if (a.IsRaised && GUILayout.Button("Snooze", GUILayout.Width(60)))
                {
                    AlarmManager.Snooze(a, a.snoozeMinutes);
                }
                if (GUILayout.Button("Edit", GUILayout.Width(44)))
                {
                    StatPlan plan = AlarmManager.ResolvePlan(GameMain.data, a);
                    if (plan != null)
                    {
                        OpenEditor(plan);
                    }
                }
                if (GUILayout.Button("X", GUILayout.Width(24)))
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
