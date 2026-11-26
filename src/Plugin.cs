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
        public const string Version = "0.4.0";

        internal static ManualLogSource Log;
        internal static float UiScale = 1f;

        private ConfigEntry<float> sampleSeconds;
        private ConfigEntry<float> stockRefreshSeconds;
        private ConfigEntry<int> alarmVolumePercent;
        private ConfigEntry<bool> playWithSoundOff;
        private ConfigEntry<float> uiScale;

        private static Plugin instance;

        /// <summary>Alarms ignore the game's volume sliders (default on).</summary>
        internal static bool PlayWithSoundOff = true;

        /// <summary>Alarm volume 0..100 as shown on the Settings slider.</summary>
        internal static int AlarmVolumePercent => Mathf.RoundToInt(AlarmManager.AlarmVolume * 100f);

        /// <summary>Saves the Settings > Audio values (called on Apply).</summary>
        internal static void SetSoundOptions(bool playWithSoundOff, int volumePercent)
        {
            volumePercent = Mathf.Clamp(volumePercent, 0, 100);
            PlayWithSoundOff = playWithSoundOff;
            AlarmManager.AlarmVolume = volumePercent / 100f;
            if (instance == null)
            {
                return;
            }
            if (instance.playWithSoundOff.Value != playWithSoundOff)
            {
                instance.playWithSoundOff.Value = playWithSoundOff; // saved to the config file
            }
            if (instance.alarmVolumePercent.Value != volumePercent)
            {
                instance.alarmVolumePercent.Value = volumePercent;
            }
        }
        private ConfigEntry<KeyboardShortcut> listHotkey;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            instance = this;
            sampleSeconds = Config.Bind("General", "SampleSeconds", 2f,
                "How often (seconds) every alarm checks its chart's value.");
            stockRefreshSeconds = Config.Bind("General", "StockRefreshSeconds", 30f,
                "How often (seconds) stock and max-production alarms ask the game to recount storage and machines. The game counts one planet per tick, so lower values cost more CPU on big saves. Minimum 5.");
            alarmVolumePercent = Config.Bind("Sound", "AlarmVolumePercent", 50,
                new ConfigDescription("Alarm sound volume in percent. Also the \"Dashboard alarm volume\" slider in the game's Settings > Audio tab.",
                    new AcceptableValueRange<int>(0, 100)));
            playWithSoundOff = Config.Bind("Sound", "PlayWithSoundOff", true,
                "true: alarms play at AlarmVolumePercent even when the game's master or sound effects volume is turned down or off. "
                + "false: alarms follow the game's Game Master x Sound Effects volume (silent when effects are off). "
                + "Also shown as a checkbox in the game's Settings > Audio tab.");
            uiScale = Config.Bind("UI", "Scale", 1f, "Scale of the alarm windows and banner.");
            listHotkey = Config.Bind("UI", "AlarmListHotkey", new KeyboardShortcut(KeyCode.A, KeyCode.LeftControl, KeyCode.LeftShift),
                "Opens/closes the list of all dashboard alarms.");
            ApplyConfig();
            Config.SettingChanged += (_, __) => ApplyConfig();

            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Patches));
            harmony.PatchAll(typeof(OptionsToggle));
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void ApplyConfig()
        {
            AlarmManager.SampleSeconds = Mathf.Max(0.5f, sampleSeconds.Value);
            AlarmManager.StockRefreshSeconds = Mathf.Max(5f, stockRefreshSeconds.Value);
            AlarmManager.AlarmVolume = Mathf.Clamp(alarmVolumePercent.Value, 0, 100) / 100f;
            PlayWithSoundOff = playWithSoundOff.Value;
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
