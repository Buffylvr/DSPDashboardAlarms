using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using crecheng.DSPModSave;
using HarmonyLib;
using UnityEngine;

namespace DashboardAlarms
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("crecheng.DSPModSave")]
    [BepInProcess("DSPGAME.exe")]
    public class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string Guid = "buffylvr.dsp.dashboardalarms";
        public const string Name = "Dashboard Alarms";
        public const string Version = "0.2.2";

        internal static ManualLogSource Log;
        internal static float UiScale = 1f;

        private ConfigEntry<float> sampleSeconds;
        private ConfigEntry<float> stockRefreshSeconds;
        private ConfigEntry<float> alarmVolume;
        private ConfigEntry<float> uiScale;
        private ConfigEntry<KeyboardShortcut> listHotkey;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            sampleSeconds = Config.Bind("General", "SampleSeconds", 2f,
                "How often (seconds) every alarm checks its chart's value.");
            stockRefreshSeconds = Config.Bind("General", "StockRefreshSeconds", 30f,
                "How often (seconds) stock and max-production alarms ask the game to recount storage and machines. The game counts one planet per tick, so lower values cost more CPU on big saves. Minimum 5.");
            alarmVolume = Config.Bind("Sound", "AlarmVolume", 1f,
                "Alarm sound volume, 0 to 1. Multiplied by the game's Game Master Volume only, so alarms are heard even with the game's Sound Effects slider off.");
            uiScale = Config.Bind("UI", "Scale", 1f, "Scale of the alarm windows and banner.");
            listHotkey = Config.Bind("UI", "AlarmListHotkey", new KeyboardShortcut(KeyCode.A, KeyCode.LeftControl, KeyCode.LeftShift),
                "Opens/closes the list of all dashboard alarms.");
            ApplyConfig();
            Config.SettingChanged += (_, __) => ApplyConfig();

            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Patches));
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void ApplyConfig()
        {
            AlarmManager.SampleSeconds = Mathf.Max(0.5f, sampleSeconds.Value);
            AlarmManager.StockRefreshSeconds = Mathf.Max(5f, stockRefreshSeconds.Value);
            AlarmManager.AlarmVolume = Mathf.Clamp01(alarmVolume.Value);
            UiScale = Mathf.Clamp(uiScale.Value, 0.5f, 3f);
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        private void Update()
        {
            try
            {
                if (listHotkey.Value.IsDown() && GameMain.isRunning && !VFInput.inputing)
                {
                    AlarmWindow.ToggleList();
                }
                AlarmManager.Tick();
                InputBlocker.Sync(AlarmWindow.ActiveRects(), UiScale);
            }
            catch (Exception e)
            {
                Log.LogError("Alarm tick failed: " + e);
            }
        }

        private void OnGUI()
        {
            try
            {
                AlarmWindow.Draw();
            }
            catch (Exception e)
            {
                Log.LogError("Alarm UI failed: " + e);
            }
        }

        // ---------------------------------------------------------------- DSPModSave

        public void Export(BinaryWriter w) => AlarmManager.Export(w);

        public void Import(BinaryReader r)
        {
            AlarmWindow.Reset();
            AlarmManager.Import(r);
        }

        public void IntoOtherSave()
        {
            // new game, or a save without our data
            AlarmWindow.Reset();
            AlarmManager.Clear();
        }
    }
}
