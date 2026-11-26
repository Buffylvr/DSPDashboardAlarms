using System;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardAlarms
{
    /// <summary>
    /// Adds a "Dashboard alarms play with sound off" checkbox to the game's Settings > Audio tab, made by
    /// cloning the game's own "Mute when running in background" row. It follows the window's
    /// Apply/Cancel flow: shown from the config when the window opens, saved only on Apply.
    /// </summary>
    public static class OptionsToggle
    {
        private const string RowName = "dashboard-alarms-sound-option";
        private const string Label = "Dashboard alarms play with sound off";

        private static UIToggle toggle;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIOptionWindow), "_OnCreate")]
        public static void UIOptionWindow_OnCreate_Postfix(UIOptionWindow __instance)
        {
            TryBuild(__instance);
        }

        private static bool attempted;

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
                Plugin.Log?.LogWarning("Could not add the alarm sound checkbox to Settings > Audio (the config file setting still works): " + e);
            }
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
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIOptionWindow), nameof(UIOptionWindow.OnApplyClick))]
        public static void UIOptionWindow_OnApplyClick_Prefix(UIOptionWindow __instance)
        {
            if (toggle != null && __instance.applyButton != null && __instance.applyButton.button.interactable)
            {
                Plugin.SetPlayWithSoundOff(toggle.isOn);
            }
        }

        private static void Build(UIOptionWindow window)
        {
            UIToggle source = window.backgroundMuteComp;
            if (source == null)
            {
                Plugin.Log?.LogWarning("Settings window has no background-mute checkbox to copy; alarm sound checkbox not added");
                return;
            }
            // Expected layout: the label Text is the row, and the checkbox is its child.
            Transform row = source.transform.parent;
            Text label = row != null ? row.GetComponent<Text>() : null;
            LogLayout(window, row);
            if (label == null || !(row is RectTransform rowRt) || !(row.parent is RectTransform container))
            {
                Plugin.Log?.LogWarning("Settings > Audio layout is not as expected; alarm sound checkbox not added (see layout above)");
                return;
            }

            // Row spacing: distance between the "battle music" row and the "background mute" row.
            float step = -40f;
            Transform prevRow = window.combatMusicEnabledComp != null ? window.combatMusicEnabledComp.transform.parent : null;
            if (prevRow is RectTransform prevRt && prevRow.parent == row.parent && prevRt.anchoredPosition.y != rowRt.anchoredPosition.y)
            {
                step = rowRt.anchoredPosition.y - prevRt.anchoredPosition.y;
            }

            // Place it one step past the last row in this container.
            float last = rowRt.anchoredPosition.y;
            foreach (Transform child in container)
            {
                if (child is RectTransform c && child.gameObject.activeSelf && c.anchorMin == rowRt.anchorMin && c.anchorMax == rowRt.anchorMax)
                {
                    last = step < 0 ? Mathf.Min(last, c.anchoredPosition.y) : Mathf.Max(last, c.anchoredPosition.y);
                }
            }

            GameObject copy = UnityEngine.Object.Instantiate(row.gameObject, container, false);
            copy.name = RowName;
            Localizer localizer = copy.GetComponent<Localizer>();
            if (localizer != null)
            {
                UnityEngine.Object.DestroyImmediate(localizer); // otherwise it would put the game's label back
            }
            copy.GetComponent<Text>().text = Label;
            var copyRt = (RectTransform)copy.transform;
            copyRt.anchoredPosition = new Vector2(rowRt.anchoredPosition.x, last + step);

            toggle = copy.GetComponentInChildren<UIToggle>(true);
            if (toggle == null)
            {
                UnityEngine.Object.Destroy(copy);
                Plugin.Log?.LogWarning("Copied settings row has no checkbox; alarm sound checkbox not added");
                return;
            }
            toggle.toggle.onValueChanged = new Toggle.ToggleEvent(); // drop anything wired to the original
            toggle.isOn = Plugin.PlayWithSoundOff;
            Plugin.Log?.LogInfo($"Added '{Label}' to Settings > Audio at y {copyRt.anchoredPosition.y:0} (row step {step:0})");
        }

        private static void LogLayout(UIOptionWindow window, Transform row)
        {
            var sb = new StringBuilder("Settings > Audio layout: ");
            sb.Append($"checkbox '{window.backgroundMuteComp.name}' parent '{(row != null ? row.name : "none")}' ");
            sb.Append($"(Text {(row != null && row.GetComponent<Text>() != null)}, Localizer {(row != null && row.GetComponent<Localizer>() != null)})");
            if (row != null && row.parent != null)
            {
                sb.Append($"; container '{row.parent.name}' rows:");
                foreach (Transform child in row.parent)
                {
                    var rt = child as RectTransform;
                    sb.Append($" [{child.name} y={(rt != null ? rt.anchoredPosition.y : float.NaN):0} {(child.gameObject.activeSelf ? "" : "hidden")}]");
                }
            }
            Plugin.Log?.LogInfo(sb.ToString());
        }
    }
}
