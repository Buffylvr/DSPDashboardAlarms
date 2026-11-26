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
    /// rows so the fonts, colours and controls match:
    ///   - "Dashboard alarms play with sound off" (clone of "Mute when running in background")
    ///   - "Dashboard alarm volume" slider + Test button (clone of "Dark Fog Attack Alert Volume" and
    ///     the "Restore All Settings" button)
    ///   - a note that these rows come from the DashboardAlarms mod.
    /// They follow the window's Apply/Cancel flow: loaded from the config on open, saved only on Apply.
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
            UIToggle sourceToggle = window.backgroundMuteComp;
            Transform checkRow = sourceToggle != null ? sourceToggle.transform.parent : null;
            LogLayout(window, checkRow);
            // Expected layout: each row is the label Text, with its control as a child.
            if (checkRow == null || checkRow.GetComponent<Text>() == null || !(checkRow is RectTransform checkRt) || !(checkRow.parent is RectTransform container))
            {
                Plugin.Log?.LogWarning("Settings > Audio layout is not as expected; alarm sound options not added (see layout above)");
                return;
            }

            // Row spacing: distance between the "battle music" row and the "background mute" row.
            float step = -40f;
            Transform prevRow = window.combatMusicEnabledComp != null ? window.combatMusicEnabledComp.transform.parent : null;
            if (prevRow is RectTransform prevRt && prevRow.parent == container && prevRt.anchoredPosition.y != checkRt.anchoredPosition.y)
            {
                step = checkRt.anchoredPosition.y - prevRt.anchoredPosition.y;
            }

            // Start one step past the last row in this container.
            float last = checkRt.anchoredPosition.y;
            foreach (Transform child in container)
            {
                if (child is RectTransform c && child.gameObject.activeSelf && c.anchorMin == checkRt.anchorMin && c.anchorMax == checkRt.anchorMax)
                {
                    last = step < 0 ? Mathf.Min(last, c.anchoredPosition.y) : Mathf.Max(last, c.anchoredPosition.y);
                }
            }
            float y = last + step;

            // 1. checkbox row
            RectTransform checkCopy = CloneRow(checkRow, container, "dashboard-alarms-play-with-sound-off", CheckboxLabel, checkRt.anchoredPosition.x, y);
            toggle = checkCopy.GetComponentInChildren<UIToggle>(true);
            if (toggle != null)
            {
                toggle.toggle.onValueChanged = new Toggle.ToggleEvent(); // drop anything wired to the original
                toggle.isOn = Plugin.PlayWithSoundOff;
            }
            y += step;

            // 2. volume slider row (+ Test button)
            RectTransform volumeRow = window.dfAttackAlertVolumeLabel;
            if (volumeRow != null && volumeRow.parent == container && window.dfAttackAlertvolumeComp != null)
            {
                string sliderPath = PathFrom(volumeRow, window.dfAttackAlertvolumeComp.transform);
                string valuePath = window.dfAttackAlertvolumeComp1 != null ? PathFrom(volumeRow, window.dfAttackAlertvolumeComp1.transform) : null;
                RectTransform volumeCopy = CloneRow(volumeRow, container, "dashboard-alarms-volume", VolumeLabel, volumeRow.anchoredPosition.x, y);
                volumeSlider = sliderPath != null ? volumeCopy.Find(sliderPath)?.GetComponent<Slider>() : null;
                volumeValueText = valuePath != null ? volumeCopy.Find(valuePath)?.GetComponent<Text>() : null;
                if (volumeSlider != null)
                {
                    volumeSlider.onValueChanged = new Slider.SliderEvent(); // drop the game's alert-volume handler
                    volumeSlider.minValue = 0;
                    volumeSlider.maxValue = 100;
                    volumeSlider.wholeNumbers = true;
                    volumeSlider.value = Plugin.AlarmVolumePercent;
                    volumeSlider.onValueChanged.AddListener(_ => RefreshVolumeText());
                    RefreshVolumeText();
                    AddTestButton(window, volumeCopy, (RectTransform)volumeSlider.transform);
                }
                y += step;
            }
            else
            {
                Plugin.Log?.LogWarning("Settings > Audio has no alert-volume row to copy; alarm volume slider not added (config AlarmVolumePercent still works)");
            }

            // 3. note
            RectTransform note = CloneRow(checkRow, container, "dashboard-alarms-note", NoteText, checkRt.anchoredPosition.x, y);
            for (int i = note.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(note.GetChild(i).gameObject); // label only, no checkbox
            }
            Text noteText = note.GetComponent<Text>();
            noteText.fontSize = Mathf.Max(10, Mathf.RoundToInt(noteText.fontSize * 0.8f));
            noteText.fontStyle = FontStyle.Italic;
            Color c0 = noteText.color;
            noteText.color = new Color(c0.r, c0.g, c0.b, c0.a * 0.6f); // dimmer than a real option
            noteText.horizontalOverflow = HorizontalWrapMode.Overflow;

            Plugin.Log?.LogInfo($"Added dashboard alarm sound options to Settings > Audio from y {last + step:0} (row step {step:0}); slider {(volumeSlider != null ? "ok" : "missing")}");
        }

        /// <summary>Copies a settings row, gives it our own (untranslated) label and puts it at y.</summary>
        private static RectTransform CloneRow(Transform row, Transform container, string name, string label, float x, float y)
        {
            GameObject copy = UnityEngine.Object.Instantiate(row.gameObject, container, false);
            copy.name = name;
            RemoveLocalizers(copy);
            copy.GetComponent<Text>().text = label;
            var rt = (RectTransform)copy.transform;
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        /// <summary>A Test button cloned from the window's "Restore All Settings" button, placed right of the slider.</summary>
        private static void AddTestButton(UIOptionWindow window, RectTransform row, RectTransform slider)
        {
            UIButton source = window.revertAllButton;
            if (source == null)
            {
                Plugin.Log?.LogWarning("No settings button to copy; Test button not added");
                return;
            }
            GameObject go = UnityEngine.Object.Instantiate(source.gameObject, slider.parent, false);
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

            // Size it to the slider's height (with some padding) and place it just right of the slider.
            var rt = (RectTransform)go.transform;
            Vector3[] corners = new Vector3[4];
            slider.GetWorldCorners(corners); // 0 bottom-left, 2 top-right
            Transform parent = slider.parent;
            Vector3 bottomRight = parent.InverseTransformPoint(corners[3]);
            Vector3 topRight = parent.InverseTransformPoint(corners[2]);
            float sliderH = Mathf.Abs(topRight.y - bottomRight.y);
            float h = Mathf.Max(26f, sliderH + 10f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(80f, h);
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

        /// <summary>Path of target below root, for Transform.Find on a clone of root. Null if not below it.</summary>
        private static string PathFrom(Transform root, Transform target)
        {
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

        private static void LogLayout(UIOptionWindow window, Transform row)
        {
            var sb = new StringBuilder("Settings > Audio layout: ");
            sb.Append($"checkbox parent '{(row != null ? row.name : "none")}' (Text {(row != null && row.GetComponent<Text>() != null)})");
            if (window.dfAttackAlertVolumeLabel != null)
            {
                sb.Append($"; alert volume row '{window.dfAttackAlertVolumeLabel.name}' slider path '{PathFrom(window.dfAttackAlertVolumeLabel, window.dfAttackAlertvolumeComp?.transform)}'");
            }
            if (row != null && row.parent != null)
            {
                sb.Append($"; container '{row.parent.name}' rows:");
                foreach (Transform child in row.parent)
                {
                    var rt = child as RectTransform;
                    sb.Append($" [{child.name} y={(rt != null ? rt.anchoredPosition.y : float.NaN):0}{(child.gameObject.activeSelf ? "" : " hidden")}]");
                }
            }
            Plugin.Log?.LogInfo(sb.ToString());
        }
    }
}
