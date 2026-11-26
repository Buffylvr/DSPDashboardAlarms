using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardAlarms
{
    /// <summary>
    /// Adds the mod's sound options to the game's Settings > Audio tab, built by cloning the game's own
    /// labels and controls so the fonts, colours and controls match:
    ///   - "Dashboard alarms play with sound off" (clone of "Mute when running in background")
    ///   - "Dashboard alarm volume" slider + Test button (clone of "Dark Fog Attack Alert Volume" and
    ///     the "Restore All Settings" button)
    ///   - a note that these rows come from the DashboardAlarms mod.
    /// They follow the window's Apply/Cancel flow: loaded from the config on open, saved only on Apply.
    ///
    /// The Audio tab is two columns inside one container: "labels" (every row's Text) and "comps"
    /// (every row's control). A row is a label and a control at the same height.
    /// </summary>
    public static class OptionsToggle
    {
        private const string CheckboxLabel = "Dashboard alarms play with sound off";
        private const string VolumeLabel = "Dashboard alarm volume";
        private const string TestLabel = "Test";
        private const string NoteText = "The two options above are added by the DashboardAlarms mod.";

        private static UIToggle toggle;
        private static Slider volumeSlider;
        private static Text volumeValueText;
        private static bool attempted;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIOptionWindow), "_OnCreate")]
        public static void UIOptionWindow_OnCreate_Postfix(UIOptionWindow __instance)
        {
            TryBuild(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIOptionWindow), "_OnOpen")]
        public static void UIOptionWindow_OnOpen_Postfix(UIOptionWindow __instance)
        {
            // The window may have been created before this mod loaded; build on first open instead.
            if (toggle == null && !attempted)
            {
                TryBuild(__instance);
            }
            if (toggle != null)
            {
                toggle.isOn = Plugin.PlayWithSoundOff;
            }
            if (volumeSlider != null)
            {
                volumeSlider.value = Plugin.AlarmVolumePercent;
                RefreshVolumeText();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIOptionWindow), nameof(UIOptionWindow.OnApplyClick))]
        public static void UIOptionWindow_OnApplyClick_Prefix(UIOptionWindow __instance)
        {
            if (__instance.applyButton == null || !__instance.applyButton.button.interactable)
            {
                return;
            }
            bool withSoundOff = toggle != null ? toggle.isOn : Plugin.PlayWithSoundOff;
            int percent = volumeSlider != null ? Mathf.RoundToInt(volumeSlider.value) : Plugin.AlarmVolumePercent;
            Plugin.SetSoundOptions(withSoundOff, percent);
        }

        private static void TryBuild(UIOptionWindow window)
        {
            attempted = true;
            try
            {
                Build(window);
            }
            catch (Exception e)
            {
                toggle = null;
                volumeSlider = null;
                Plugin.Log?.LogWarning("Could not add the alarm sound options to Settings > Audio (the config file settings still work): " + e);
            }
        }

        private static void Build(UIOptionWindow window)
        {
            UIToggle muteToggle = window.backgroundMuteComp;
            if (muteToggle == null)
            {
                Plugin.Log?.LogWarning("Settings window has no background-mute checkbox to copy; alarm sound options not added");
                return;
            }
            Transform comps = muteToggle.transform.parent;
            Transform container = comps != null ? comps.parent : null;
            Transform labels = container != null ? FindLabelColumn(container, comps) : null;
            LogLayout(labels, comps);
            if (comps == null || labels == null)
            {
                Plugin.Log?.LogWarning("Settings > Audio layout is not as expected; alarm sound options not added (see layout above)");
                return;
            }

            RectTransform muteLabel = LabelAt(labels, CenterY(muteToggle.transform));
            if (muteLabel == null)
            {
                Plugin.Log?.LogWarning("Could not find the label of the background-mute row; alarm sound options not added");
                return;
            }

            // Row spacing (world units, negative = down): battle-music row to background-mute row,
            // else the typical gap between labels.
            float step = 0;
            if (window.combatMusicEnabledComp != null)
            {
                RectTransform combatLabel = LabelAt(labels, CenterY(window.combatMusicEnabledComp.transform));
                if (combatLabel != null && combatLabel != muteLabel)
                {
                    step = CenterY(muteLabel) - CenterY(combatLabel);
                }
            }
            if (step >= 0)
            {
                step = TypicalStep(labels);
            }
            float lastY = float.MaxValue;
            foreach (Transform child in labels)
            {
                if (child.gameObject.activeSelf)
                {
                    lastY = Mathf.Min(lastY, CenterY(child));
                }
            }

            // 1. checkbox row
            float dy = lastY + step - CenterY(muteLabel);
            CloneLabel(muteLabel, labels, "dashboard-alarms-play-with-sound-off", CheckboxLabel, dy);
            Transform toggleRoot = ControlRoot(muteToggle.transform, comps);
            Transform toggleCopy = CloneAt(toggleRoot, comps, "dashboard-alarms-play-with-sound-off", dy);
            toggle = toggleCopy.GetComponentInChildren<UIToggle>(true);
            if (toggle != null)
            {
                toggle.toggle.onValueChanged = new Toggle.ToggleEvent(); // drop anything wired to the original
                toggle.isOn = Plugin.PlayWithSoundOff;
            }

            // 2. volume slider row + Test button
            Slider gameSlider = window.dfAttackAlertvolumeComp;
            if (gameSlider != null)
            {
                RectTransform volLabel = window.dfAttackAlertVolumeLabel != null && window.dfAttackAlertVolumeLabel.parent == labels
                    ? window.dfAttackAlertVolumeLabel
                    : LabelAt(labels, CenterY(gameSlider.transform));
                float sliderRowY = CenterY(volLabel != null ? (Transform)volLabel : gameSlider.transform);
                dy = lastY + 2 * step - sliderRowY;
                if (volLabel != null)
                {
                    CloneLabel(volLabel, labels, "dashboard-alarms-volume", VolumeLabel, dy);
                }
                Transform sliderRoot = ControlRoot(gameSlider.transform, comps);
                Transform sliderCopy = CloneAt(sliderRoot, comps, "dashboard-alarms-volume", dy);
                volumeSlider = FindCopy(sliderRoot, sliderCopy, gameSlider.transform)?.GetComponent<Slider>();

                Text gameValue = window.dfAttackAlertvolumeComp1;
                if (gameValue != null)
                {
                    Transform inSlider = FindCopy(sliderRoot, sliderCopy, gameValue.transform);
                    if (inSlider != null)
                    {
                        volumeValueText = inSlider.GetComponent<Text>();
                    }
                    else
                    {
                        // the percentage text sits beside the slider, not inside it: copy it too
                        Transform valueRoot = ControlRoot(gameValue.transform, comps);
                        if (valueRoot != null && valueRoot != sliderRoot)
                        {
                            Transform valueCopy = CloneAt(valueRoot, comps, "dashboard-alarms-volume-value", dy);
                            volumeValueText = FindCopy(valueRoot, valueCopy, gameValue.transform)?.GetComponent<Text>();
                        }
                    }
                }
                if (volumeSlider != null)
                {
                    volumeSlider.onValueChanged = new Slider.SliderEvent(); // drop the game's alert-volume handler
                    volumeSlider.minValue = 0;
                    volumeSlider.maxValue = 100;
                    volumeSlider.wholeNumbers = true;
                    volumeSlider.value = Plugin.AlarmVolumePercent;
                    volumeSlider.onValueChanged.AddListener(_ => RefreshVolumeText());
                    RefreshVolumeText();
                    AddTestButton(window, comps, (RectTransform)volumeSlider.transform);
                }
            }
            else
            {
                Plugin.Log?.LogWarning("Settings > Audio has no alert-volume slider to copy; alarm volume slider not added (config AlarmVolumePercent still works)");
            }

            // 3. note (label column only)
            dy = lastY + 3 * step - CenterY(muteLabel);
            RectTransform note = CloneLabel(muteLabel, labels, "dashboard-alarms-note", NoteText, dy);
            for (int i = note.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(note.GetChild(i).gameObject);
            }
            Text noteText = note.GetComponent<Text>();
            if (noteText != null)
            {
                noteText.fontSize = Mathf.Max(10, Mathf.RoundToInt(noteText.fontSize * 0.8f));
                noteText.fontStyle = FontStyle.Italic;
                Color c0 = noteText.color;
                noteText.color = new Color(c0.r, c0.g, c0.b, c0.a * 0.6f); // dimmer than a real option
                noteText.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            GrowScrollContent(container, -3 * step);
            Plugin.Log?.LogInfo($"Added dashboard alarm sound options to Settings > Audio (row step {step:0.###}); "
                + $"checkbox {(toggle != null ? "ok" : "missing")}, slider {(volumeSlider != null ? "ok" : "missing")}, value text {(volumeValueText != null ? "ok" : "missing")}");
        }

        /// <summary>The direct child of column that contains t (the whole control, e.g. a slider with its parts).</summary>
        private static Transform ControlRoot(Transform t, Transform column)
        {
            for (; t != null; t = t.parent)
            {
                if (t.parent == column)
                {
                    return t;
                }
            }
            return null;
        }

        /// <summary>In a clone of root, the object matching target in the original (null if target isn't under root).</summary>
        private static Transform FindCopy(Transform root, Transform copy, Transform target)
        {
            string path = PathFrom(root, target);
            if (path == null)
            {
                return null;
            }
            return path.Length == 0 ? copy : copy.Find(path);
        }

        /// <summary>The sibling of the control column whose children are the row labels.</summary>
        private static Transform FindLabelColumn(Transform container, Transform comps)
        {
            Transform named = container.Find("labels");
            if (named != null)
            {
                return named;
            }
            Transform best = null;
            int bestTexts = 0;
            foreach (Transform sibling in container)
            {
                if (sibling == comps)
                {
                    continue;
                }
                int texts = 0;
                foreach (Transform child in sibling)
                {
                    if (child.GetComponent<Text>() != null)
                    {
                        texts++;
                    }
                }
                if (texts > bestTexts)
                {
                    best = sibling;
                    bestTexts = texts;
                }
            }
            return best;
        }

        /// <summary>The active label whose centre is closest to world height y.</summary>
        private static RectTransform LabelAt(Transform labels, float y)
        {
            RectTransform best = null;
            float bestD = float.MaxValue;
            foreach (Transform child in labels)
            {
                if (!child.gameObject.activeSelf || !(child is RectTransform rt))
                {
                    continue;
                }
                float d = Mathf.Abs(CenterY(rt) - y);
                if (d < bestD)
                {
                    best = rt;
                    bestD = d;
                }
            }
            return best;
        }

        /// <summary>Median downward gap between neighbouring labels (world units, negative).</summary>
        private static float TypicalStep(Transform labels)
        {
            var ys = new List<float>();
            foreach (Transform child in labels)
            {
                if (child.gameObject.activeSelf)
                {
                    ys.Add(CenterY(child));
                }
            }
            ys.Sort();
            var gaps = new List<float>();
            for (int i = 1; i < ys.Count; i++)
            {
                if (ys[i] - ys[i - 1] > 0.0001f)
                {
                    gaps.Add(ys[i] - ys[i - 1]);
                }
            }
            if (gaps.Count == 0)
            {
                return -0.4f;
            }
            gaps.Sort();
            return -gaps[gaps.Count / 2];
        }

        /// <summary>World height of the centre of t's rectangle (independent of its pivot).</summary>
        private static float CenterY(Transform t)
        {
            if (t is RectTransform rt)
            {
                return rt.TransformPoint(rt.rect.center).y;
            }
            return t.position.y;
        }

        /// <summary>Copies original into parent, moved down by dy world units.</summary>
        private static Transform CloneAt(Transform original, Transform parent, string name, float dy)
        {
            GameObject copy = UnityEngine.Object.Instantiate(original.gameObject, parent, false);
            copy.name = name;
            RemoveLocalizers(copy);
            copy.transform.position = original.position + new Vector3(0, dy, 0);
            copy.SetActive(true);
            return copy.transform;
        }

        /// <summary>Copies a label, gives it our own (untranslated) text and moves it down by dy.</summary>
        private static RectTransform CloneLabel(Transform original, Transform labels, string name, string text, float dy)
        {
            Transform copy = CloneAt(original, labels, name, dy);
            Text t = copy.GetComponent<Text>();
            if (t == null)
            {
                t = copy.GetComponentInChildren<Text>(true);
            }
            if (t != null)
            {
                t.text = text;
            }
            return (RectTransform)copy;
        }

        /// <summary>If the tab scrolls, make its content taller so the new rows can be reached.</summary>
        private static void GrowScrollContent(Transform container, float worldHeight)
        {
            ScrollRect scroll = container.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null)
            {
                return;
            }
            RectTransform content = scroll.content;
            float local = Mathf.Abs(content.InverseTransformVector(new Vector3(0, worldHeight, 0)).y);
            content.sizeDelta = new Vector2(content.sizeDelta.x, content.sizeDelta.y + local);
        }

        /// <summary>A Test button cloned from the window's "Restore All Settings" button, placed right of the slider.</summary>
        private static void AddTestButton(UIOptionWindow window, Transform column, RectTransform slider)
        {
            UIButton source = window.revertAllButton;
            if (source == null)
            {
                Plugin.Log?.LogWarning("No settings button to copy; Test button not added");
                return;
            }
            GameObject go = UnityEngine.Object.Instantiate(source.gameObject, column, false);
            go.name = "dashboard-alarms-test";
            RemoveLocalizers(go);
            UIButton button = go.GetComponent<UIButton>(); // C# click handlers are not copied by Instantiate
            button.tips.tipTitle = "";
            button.tips.tipText = "";
            button.onClick += _ => PlayPreview();
            foreach (Text t in go.GetComponentsInChildren<Text>(true))
            {
                t.text = TestLabel;
            }

            // Size it to the slider's height (plus padding) and place it just right of the slider.
            var rt = (RectTransform)go.transform;
            Vector3[] corners = new Vector3[4];
            slider.GetWorldCorners(corners); // 0 bottom-left, 2 top-right, 3 bottom-right
            Vector3 bottomRight = column.InverseTransformPoint(corners[3]);
            Vector3 topRight = column.InverseTransformPoint(corners[2]);
            float sliderH = Mathf.Abs(topRight.y - bottomRight.y);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(80f, Mathf.Max(26f, sliderH + 10f));
            rt.localPosition = new Vector3(bottomRight.x + 16f, (bottomRight.y + topRight.y) / 2f, 0f);
        }

        private static void PlayPreview()
        {
            float volume = volumeSlider != null ? volumeSlider.value / 100f : AlarmManager.AlarmVolume;
            bool withSoundOff = toggle != null ? toggle.isOn : Plugin.PlayWithSoundOff;
            AlarmManager.PlaySound(Sounds.Default, volume, withSoundOff, diagnose: true);
        }

        private static void RefreshVolumeText()
        {
            if (volumeValueText == null || volumeSlider == null)
            {
                return;
            }
            // same format as the game's own volume sliders: "50%", or "OFF" (translated) at zero
            volumeValueText.text = volumeSlider.value < 0.5f ? "关".Translate() : (volumeSlider.value / 100f).ToString("0%");
        }

        private static void RemoveLocalizers(GameObject go)
        {
            foreach (Localizer l in go.GetComponentsInChildren<Localizer>(true))
            {
                UnityEngine.Object.DestroyImmediate(l); // otherwise they put the game's text back
            }
        }

        /// <summary>Path of target below root ("" when target is root), for Transform.Find on a clone. Null if not below it.</summary>
        private static string PathFrom(Transform root, Transform target)
        {
            if (root == null)
            {
                return null;
            }
            var names = new List<string>();
            for (Transform t = target; t != null; t = t.parent)
            {
                if (t == root)
                {
                    names.Reverse();
                    return string.Join("/", names.ToArray());
                }
                names.Add(t.name);
            }
            return null;
        }

        private static void LogLayout(Transform labels, Transform comps)
        {
            var sb = new StringBuilder("Settings > Audio layout:");
            foreach (Transform column in new[] { labels, comps })
            {
                if (column == null)
                {
                    sb.Append(" [missing column]");
                    continue;
                }
                sb.Append($" {column.name}:");
                foreach (Transform child in column)
                {
                    sb.Append($" [{child.name} y={CenterY(child):0.###}{(child.gameObject.activeSelf ? "" : " hidden")}]");
                }
            }
            Plugin.Log?.LogInfo(sb.ToString());
        }
    }
}
