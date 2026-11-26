using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardAlarms
{
    /// <summary>
    /// IMGUI windows do not stop Unity UI (uGUI) from also receiving the same clicks, so a click on
    /// our window would also drag the dashboard or press a chart under it. This places invisible
    /// raycast-catching panels on the game's UI canvas exactly behind our windows.
    /// </summary>
    public static class InputBlocker
    {
        private static readonly List<RectTransform> blockers = new List<RectTransform>();
        private static Canvas canvas;

        /// <summary>Call once per frame with the GUI-space rects (top-left origin, before scaling) to block.</summary>
        public static void Sync(IList<Rect> guiRects, float scale)
        {
            if (!EnsureCanvas())
            {
                return;
            }
            for (int i = 0; i < guiRects.Count; i++)
            {
                RectTransform rt = i < blockers.Count ? blockers[i] : Create();
                if (rt == null)
                {
                    // destroyed with the UI (e.g. back to main menu); rebuild next frame
                    blockers.Clear();
                    canvas = null;
                    return;
                }
                Place(rt, guiRects[i], scale);
            }
            for (int i = guiRects.Count; i < blockers.Count; i++)
            {
                if (blockers[i] != null && blockers[i].gameObject.activeSelf)
                {
                    blockers[i].gameObject.SetActive(false);
                }
            }
        }

        public static void HideAll()
        {
            foreach (RectTransform rt in blockers)
            {
                if (rt != null && rt.gameObject.activeSelf)
                {
                    rt.gameObject.SetActive(false);
                }
            }
        }

        private static bool EnsureCanvas()
        {
            if (canvas != null)
            {
                return true;
            }
            blockers.Clear();
            UIGame uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            if (uiGame == null)
            {
                return false;
            }
            Canvas c = uiGame.GetComponentInParent<Canvas>();
            canvas = c != null ? c.rootCanvas : null;
            return canvas != null;
        }

        private static RectTransform Create()
        {
            var go = new GameObject("dashboard-alarm-input-blocker", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            Image img = go.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0); // invisible but still catches raycasts
            img.raycastTarget = true;
            blockers.Add(rt);
            return rt;
        }

        private static void Place(RectTransform rt, Rect gui, float scale)
        {
            if (!rt.gameObject.activeSelf)
            {
                rt.gameObject.SetActive(true);
            }
            rt.SetAsLastSibling();
            // GUI space (top-left origin, scaled) -> screen space (bottom-left origin)
            Vector2 screenMin = new Vector2(gui.xMin * scale, Screen.height - gui.yMax * scale);
            Vector2 screenMax = new Vector2(gui.xMax * scale, Screen.height - gui.yMin * scale);
            var canvasRt = (RectTransform)canvas.transform;
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenMin, cam, out Vector2 localMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenMax, cam, out Vector2 localMax);
            rt.localPosition = new Vector3((localMin.x + localMax.x) / 2, (localMin.y + localMax.y) / 2, 0);
            rt.sizeDelta = new Vector2(Mathf.Abs(localMax.x - localMin.x), Mathf.Abs(localMax.y - localMin.y));
        }
    }
}
