using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardAlarms
{
    [HarmonyPatch]
    public static class Patches
    {
        /// <summary>Adds "Alarm..." to the menu of production and storage charts on the dashboard.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIChart), nameof(UIChart.SetPopupMenuButtons))]
        public static void UIChart_SetPopupMenuButtons_Postfix(UIChart __instance, UIPopupMenu popupMenu)
        {
            try
            {
                if (popupMenu == null || __instance.chartData == null || __instance.charts == null)
                {
                    return;
                }
                StatPlan plan = __instance.charts.statPlans[__instance.chartData.statPlanId];
                if (plan == null || !StatReader.IsSupported(plan))
                {
                    return;
                }
                AlarmDefinition existing = AlarmManager.Find(plan.id);
                string label = existing == null ? "Add alarm..." : existing.enabled ? "Edit alarm..." : "Edit alarm (off)...";
                UIPopupMenuButton btn = popupMenu.AddMenuButton(label, -1, showSeparateLine: true);
                int planId = plan.id;
                btn.onMenuButtonClick = (Action<int>)Delegate.Combine(btn.onMenuButtonClick, new Action<int>(_ =>
                {
                    StatPlan p = GameMain.data?.statistics?.charts?.statPlans[planId];
                    if (p != null && p.id == planId)
                    {
                        AlarmWindow.OpenEditor(p);
                    }
                    UIRoot.instance?.uiGame?.dashboard?.CloseChartPopupMenu();
                }));
                btn.SetState(runtime: true);
                popupMenu.SetState(runtime: true);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError("Adding alarm menu entry failed: " + e);
            }
        }

        /// <summary>
        /// Colours the border of a chart whose alarm is pending or raised. Patched on the two chart
        /// subclasses (not UIChart itself) because their one-line base calls could be inlined.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIChartAstroItemProduction), "_OnUpdate")]
        public static void UIChartAstroItemProduction_OnUpdate_Postfix(UIChart __instance) => UIChart_OnUpdate_Postfix(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIChartAstroStorage), "_OnUpdate")]
        public static void UIChartAstroStorage_OnUpdate_Postfix(UIChart __instance) => UIChart_OnUpdate_Postfix(__instance);

        private static void UIChart_OnUpdate_Postfix(UIChart __instance)
        {
            try
            {
                ChartOverlay.Update(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError("Chart overlay update failed: " + e);
            }
        }

        /// <summary>Keeps clicks and typing in our windows from reaching the game.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(VFInput), nameof(VFInput.UpdateGameStates))]
        public static void VFInput_UpdateGameStates_Postfix()
        {
            if (AlarmWindow.MouseOverUs)
            {
                VFInput.onGUI = true;
                VFInput.onGUIOperate = true;
            }
            if (AlarmWindow.Typing)
            {
                VFInput.inputing = true;
            }
        }
    }

    /// <summary>A border drawn over a dashboard chart: amber while pending, flashing red when raised.</summary>
    public static class ChartOverlay
    {
        private const string ObjectName = "dashboard-alarm-overlay";
        private const float Thickness = 3f;

        public static void Update(UIChart chart)
        {
            if (chart.chartData == null || chart.rectTrans == null)
            {
                return;
            }
            AlarmDefinition a = AlarmManager.Find(chart.chartData.statPlanId);
            Transform existing = chart.rectTrans.Find(ObjectName);
            AlarmState state = a != null && a.enabled ? a.state : AlarmState.Normal;
            bool show = state == AlarmState.Pending || state == AlarmState.Alarming || state == AlarmState.Acknowledged;
            if (!show)
            {
                if (existing != null && existing.gameObject.activeSelf)
                {
                    existing.gameObject.SetActive(false);
                }
                return;
            }
            GameObject root = existing != null ? existing.gameObject : Create(chart.rectTrans);
            if (!root.activeSelf)
            {
                root.SetActive(true);
            }
            root.transform.SetAsLastSibling();

            Color c;
            if (state == AlarmState.Pending)
            {
                c = new Color(1f, 0.7f, 0.1f, 0.8f);
            }
            else if (state == AlarmState.Acknowledged)
            {
                c = new Color(1f, 0.15f, 0.1f, 0.75f);
            }
            else
            {
                float t = Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 5f));
                c = new Color(1f, 0.1f, 0.05f, 0.35f + 0.65f * t);
            }
            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                bool fill = images[i].gameObject.name == "fill";
                images[i].color = fill ? new Color(c.r, c.g, c.b, state == AlarmState.Alarming ? c.a * 0.12f : 0.04f) : c;
            }
        }

        private static GameObject Create(RectTransform parent)
        {
            var root = new GameObject(ObjectName, typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, false);
            Stretch(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            AddEdge(root.transform, "fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddEdge(root.transform, "top", new Vector2(0, 1), Vector2.one, new Vector2(0, -Thickness), Vector2.zero);
            AddEdge(root.transform, "bottom", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, Thickness));
            AddEdge(root.transform, "left", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(Thickness, 0));
            AddEdge(root.transform, "right", new Vector2(1, 0), Vector2.one, new Vector2(-Thickness, 0), Vector2.zero);
            return root;
        }

        private static void AddEdge(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt, anchorMin, anchorMax, offsetMin, offsetMax);
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = Color.clear;
        }

        private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            rt.localScale = Vector3.one;
        }
    }
}
