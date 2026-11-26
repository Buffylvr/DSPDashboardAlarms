using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DashboardAlarms
{
    /// <summary>Owns every alarm, evaluates them, raises sound, and saves/loads them.</summary>
    public static class AlarmManager
    {
        public const int TicksPerSecond = 60;

        public static readonly List<AlarmDefinition> Alarms = new List<AlarmDefinition>();

        /// <summary>How often each alarm takes a sample, in seconds. Set from config.</summary>
        public static float SampleSeconds = 2f;

        /// <summary>How often stock alarms ask the game to recount storage, in seconds. Set from config.</summary>
        public static float StockRefreshSeconds = 30f;

        private static long lastSampleTick = -1;
        private static long lastStockRequestTick = -1;
        private static bool stockRequested;
        private static bool stockFresh;
        private static ProductionExtraInfoCalculator hookedCalculator;

        public static int RaisedCount
        {
            get
            {
                int n = 0;
                foreach (AlarmDefinition a in Alarms)
                {
                    if (a.IsRaised)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        public static AlarmDefinition Find(int statPlanId)
        {
            foreach (AlarmDefinition a in Alarms)
            {
                if (a.statPlanId == statPlanId)
                {
                    return a;
                }
            }
            return null;
        }

        public static AlarmDefinition CreateFor(StatPlan plan)
        {
            var a = new AlarmDefinition
            {
                statPlanId = plan.id,
                planType = (int)plan.type,
                itemId = StatReader.ItemId(plan),
                astroFilter = StatReader.AstroFilter(plan),
                metric = StatReader.SupportsStock(plan) ? AlarmMetric.Stock : AlarmMetric.Production,
            };
            Alarms.Add(a);
            return a;
        }

        public static void Remove(AlarmDefinition a) => Alarms.Remove(a);

        public static void Clear()
        {
            Alarms.Clear();
            lastSampleTick = -1;
            lastStockRequestTick = -1;
            stockRequested = false;
            stockFresh = false;
            hookedCalculator = null;
        }

        /// <summary>Returns the stat plan the alarm belongs to, or null if it no longer exists / was replaced.</summary>
        public static StatPlan ResolvePlan(GameData data, AlarmDefinition a)
        {
            StatPlanPool pool = data?.statistics?.charts?.statPlans;
            if (pool == null || a.statPlanId <= 0 || a.statPlanId >= pool.cursor)
            {
                return null;
            }
            StatPlan plan = pool[a.statPlanId];
            if (plan == null || plan.id != a.statPlanId || (int)plan.type != a.planType)
            {
                return null;
            }
            // The player may change a chart's item or area in place; follow it.
            a.itemId = StatReader.ItemId(plan);
            a.astroFilter = StatReader.AstroFilter(plan);
            return plan;
        }

        /// <summary>Called every frame from the plugin's Update.</summary>
        public static void Tick()
        {
            GameData data = GameMain.data;
            if (data == null || GameMain.isPaused || !GameMain.isRunning || Alarms.Count == 0)
            {
                return;
            }
            if (data.statistics?.charts?.statPlans == null || data.statistics.production?.extraInfoCalculator == null)
            {
                return; // charts not loaded yet; never drop alarms because of that
            }
            long now = GameMain.gameTick;
            HookCalculator(data);
            RequestStockRefreshIfDue(data, now);

            if (lastSampleTick >= 0 && now - lastSampleTick < (long)(SampleSeconds * TicksPerSecond) && now >= lastSampleTick)
            {
                return;
            }
            lastSampleTick = now;

            bool readStock = stockFresh;
            stockFresh = false;
            for (int i = Alarms.Count - 1; i >= 0; i--)
            {
                AlarmDefinition a = Alarms[i];
                StatPlan plan = ResolvePlan(data, a);
                if (plan == null)
                {
                    // chart's stat plan is gone (chart closed and plan deleted) - drop the alarm
                    Alarms.RemoveAt(i);
                    continue;
                }
                // NaN = no new sample this round (stock waits for a finished recount)
                double sample = double.NaN;
                if (a.metric == AlarmMetric.Stock)
                {
                    if (readStock && plan is AstroStorageStatPlan sp && StatReader.TryReadStock(sp, out double stock))
                    {
                        sample = stock;
                    }
                }
                else if (plan is AstroItemProductionStatPlan pp)
                {
                    if (readStock && Metrics.NeedsMax(a.metric))
                    {
                        a.lastMax = StatReader.ReadMaxProduction(pp);
                        a.hasMax = true;
                    }
                    if (StatReader.TryReadRates(data, pp, out double prod, out double cons))
                    {
                        sample = ComputeSample(a.metric, prod, cons, a.hasMax ? a.lastMax : double.NaN);
                    }
                }

                if (!a.enabled || a.isDraft)
                {
                    // keep showing the live value in the editor, but never alarm
                    if (!double.IsNaN(sample))
                    {
                        a.lastValue = sample;
                        a.hasValue = true;
                    }
                    a.state = AlarmState.Normal;
                    a.conditionSinceTick = -1;
                    continue;
                }
                Evaluate(a, now, sample);
            }
        }

        /// <summary>The number a rate alarm watches. max is NaN until the game has counted the machines.</summary>
        internal static double ComputeSample(AlarmMetric metric, double production, double consumption, double max)
        {
            switch (metric)
            {
                case AlarmMetric.Production: return production;
                case AlarmMetric.Consumption: return consumption;
                case AlarmMetric.Net: return production - consumption;
                case AlarmMetric.MaxProduction: return max;
                case AlarmMetric.UsePercent:
                    // nothing able to make it: no reading rather than a divide by zero
                    return double.IsNaN(max) || max <= 0 ? double.NaN : consumption / max * 100.0;
                default: return double.NaN;
            }
        }

        /// <summary>Feeds one sample (NaN = no new sample, re-check timers only) and advances the alarm's state.</summary>
        internal static void Evaluate(AlarmDefinition a, long now, double sample)
        {
            if (!double.IsNaN(sample))
            {
                long window = (long)(Math.Max(0.5f, a.averageMinutes) * 60f * TicksPerSecond);
                // average is of the samples BEFORE this one, so a sudden drop is compared to the old level
                a.lastAverage = a.history.Mean;
                bool averageReady = a.history.Count > 0 && a.history.Span(now) >= window / 2;
                if (!averageReady)
                {
                    a.lastAverage = double.NaN;
                }
                a.lastValue = sample;
                a.hasValue = true;
                // While a drop is in progress the baseline is frozen; otherwise a lasting drop would
                // drag its own average down and the alarm would clear itself.
                bool dropInProgress = a.trigger == AlarmTrigger.DropPercent && ConditionMet(a);
                if (!dropInProgress)
                {
                    a.history.Add(now, sample, window);
                }
            }
            if (!a.hasValue)
            {
                return;
            }

            bool condition = ConditionMet(a);
            long holdTicks = (long)(Math.Max(0f, a.holdSeconds) * TicksPerSecond);

            if (a.state == AlarmState.Snoozed)
            {
                if (now < a.snoozeUntilTick)
                {
                    return;
                }
                a.state = AlarmState.Normal;
            }

            if (!condition)
            {
                a.state = AlarmState.Normal;
                a.conditionSinceTick = -1;
                return;
            }

            if (a.conditionSinceTick < 0 || a.conditionSinceTick > now)
            {
                a.conditionSinceTick = now;
            }

            switch (a.state)
            {
                case AlarmState.Normal:
                case AlarmState.Pending:
                    if (now - a.conditionSinceTick >= holdTicks)
                    {
                        a.state = AlarmState.Alarming;
                        a.lastSoundTick = -1;
                        Plugin.Log?.LogInfo($"Alarm raised: {Describe(a)} (value {Format(a.lastValue)})");
                    }
                    else
                    {
                        a.state = AlarmState.Pending;
                    }
                    break;
                case AlarmState.Alarming:
                    break;
                case AlarmState.Acknowledged:
                    break;
            }

            if (a.state == AlarmState.Alarming && a.playSound)
            {
                long repeat = (long)(a.repeatSoundSeconds * TicksPerSecond);
                if (a.lastSoundTick < 0 || (repeat > 0 && now - a.lastSoundTick >= repeat) || a.lastSoundTick > now)
                {
                    PlaySounds(a.sounds);
                    a.lastSoundTick = now;
                }
            }
        }

        public static bool ConditionMet(AlarmDefinition a)
        {
            double v = a.lastValue;
            if (double.IsNaN(v))
            {
                return false;
            }
            switch (a.trigger)
            {
                case AlarmTrigger.Below:
                    return v < a.threshold;
                case AlarmTrigger.Above:
                    return v > a.threshold;
                case AlarmTrigger.DropPercent:
                    double avg = a.lastAverage;
                    if (double.IsNaN(avg) || avg <= 0)
                    {
                        return false;
                    }
                    return v < avg * (1.0 - a.dropPercent / 100.0);
                default:
                    return false;
            }
        }

        public static void Acknowledge(AlarmDefinition a)
        {
            if (a.state == AlarmState.Alarming)
            {
                a.state = AlarmState.Acknowledged;
            }
        }

        public static void Snooze(AlarmDefinition a, float minutes) => Snooze(a, minutes, GameMain.gameTick);

        internal static void Snooze(AlarmDefinition a, float minutes, long now)
        {
            a.state = AlarmState.Snoozed;
            a.snoozeUntilTick = now + (long)(Math.Max(0.1f, minutes) * 60f * TicksPerSecond);
            a.conditionSinceTick = -1;
        }

        public static void AcknowledgeAll()
        {
            foreach (AlarmDefinition a in Alarms)
            {
                Acknowledge(a);
            }
        }

        public static void SnoozeAllRaised()
        {
            foreach (AlarmDefinition a in Alarms)
            {
                if (a.IsRaised)
                {
                    Snooze(a, a.snoozeMinutes);
                }
            }
        }

        /// <summary>Alarm volume 0..1 (config). Multiplied by the game's master volume only.</summary>
        public static float AlarmVolume = 1f;

        private static AudioSource alarmSource;

        public static void PlaySounds(List<string> names, bool diagnose = false)
        {
            if (names == null)
            {
                return;
            }
            if (diagnose)
            {
                Plugin.Log?.LogInfo($"Sound test: {names.Count} sound(s) [{string.Join(", ", names.ToArray())}]; "
                    + $"alarm volume {AlarmVolume:0.##} x game master {VFAudio.audioVolume:0.##} (game effects slider {VFAudio.soundVolume:0.##} is not used); "
                    + $"listener volume {AudioListener.volume:0.##}, background mute {VFAudio.backgroundMute}, focused {Application.isFocused}");
            }
            foreach (string n in names)
            {
                PlaySound(n, diagnose);
            }
        }

        /// <summary>
        /// Plays one of the game's sounds through the mod's own 2D AudioSource. The game's own VFAudio
        /// scales by the Sound Effects slider, so players who turn effects off would never hear an
        /// alarm; this uses AlarmVolume x the game's master volume instead.
        /// </summary>
        public static void PlaySound(string name, bool diagnose = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                name = Sounds.Default;
            }
            try
            {
                AudioProto proto = LDB.audios[name];
                if (proto == null)
                {
                    Plugin.Log?.LogWarning($"Sound '{name}' is not one of the game's sounds");
                    return;
                }
                if (proto.audioClip == null)
                {
                    proto.Preload();
                }
                AudioClip clip = proto.audioClip;
                if (clip == null)
                {
                    Plugin.Log?.LogWarning($"Sound '{name}': the game has no clip for it");
                    return;
                }
                AudioSource src = EnsureSource();
                float volume = Mathf.Clamp01(AlarmVolume) * Mathf.Clamp01(VFAudio.audioVolume) * (proto.Volume > 0 ? proto.Volume : 1f);
                src.PlayOneShot(clip, volume);
                if (diagnose)
                {
                    Plugin.Log?.LogInfo($"Sound '{name}': clip {clip.name} ({clip.length:0.##}s), volume {volume:0.###}, playing {src.isPlaying}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"Could not play sound '{name}': {e.GetType().Name}: {e.Message}");
            }
        }

        private static AudioSource EnsureSource()
        {
            if (alarmSource != null)
            {
                return alarmSource;
            }
            var go = new GameObject("DashboardAlarms.Audio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            alarmSource = go.AddComponent<AudioSource>();
            alarmSource.playOnAwake = false;
            alarmSource.spatialBlend = 0f;   // plain 2D: same loudness wherever the player is
            alarmSource.bypassEffects = true;
            alarmSource.bypassListenerEffects = true; // ignore the game's low/high-pass filters
            alarmSource.bypassReverbZones = true;
            alarmSource.priority = 0;        // never dropped when many sounds play
            return alarmSource;
        }

        // ---------------------------------------------------------------- stock refresh

        private static void HookCalculator(GameData data)
        {
            ProductionExtraInfoCalculator calc = data.statistics?.production?.extraInfoCalculator;
            if (calc == null || calc == hookedCalculator)
            {
                return;
            }
            calc.onEndCalculate = (Action<double>)Delegate.Combine(calc.onEndCalculate, new Action<double>(OnCalculatorFinished));
            hookedCalculator = calc;
        }

        private static void OnCalculatorFinished(double _)
        {
            // Only trust a recount that included our request. The dashboard's own recounts may cover
            // just the planets of its current page, and right after a load every other planet still
            // reads 0 until it has been counted once.
            if (stockRequested)
            {
                stockFresh = true;
            }
            stockRequested = false;
        }

        /// <summary>Ask for a recount on the next frame (after the editor switches to a metric that needs one).</summary>
        public static void RequestRecountSoon()
        {
            lastStockRequestTick = -1;
        }

        private static void RequestStockRefreshIfDue(GameData data, long now)
        {
            // No need to wait for an idle calculator: it runs on the main thread between frames, and
            // planets added while it is counting join the running batch, which only finishes (and
            // fires onEndCalculate) once they are counted. Waiting could starve us while the
            // dashboard is open, because the dashboard queues a new batch the moment one ends.
            long interval = (long)(Math.Max(5f, StockRefreshSeconds) * TicksPerSecond);
            if (lastStockRequestTick >= 0 && now - lastStockRequestTick < interval && now >= lastStockRequestTick && !(stockRequested && now - lastStockRequestTick > interval * 4))
            {
                return;
            }
            bool any = false;
            foreach (AlarmDefinition a in Alarms)
            {
                if (!a.enabled || !Metrics.NeedsRecount(a.metric))
                {
                    continue;
                }
                // queues the plan's factories; the game counts one per tick
                StatPlan plan = ResolvePlan(data, a);
                if (plan is AstroStorageStatPlan sp && sp.itemId > 0)
                {
                    sp.CalculateExtraInfo();
                    any = true;
                }
                else if (plan is AstroItemProductionStatPlan pp && pp.itemId > 0)
                {
                    pp.CalculateExtraInfo();
                    any = true;
                }
            }
            if (any)
            {
                stockRequested = true;
                lastStockRequestTick = now;
            }
        }

        // ---------------------------------------------------------------- text helpers

        public static string Describe(AlarmDefinition a)
        {
            GameData data = GameMain.data;
            string item = StatReader.ItemName(a.itemId);
            string area = data != null ? StatReader.AreaName(data, a.astroFilter) : "";
            string metric = Metrics.Name(a.metric);
            string unit = Metrics.Unit(a.metric);
            string cond = a.trigger == AlarmTrigger.Below ? $"< {Format(a.threshold)}{unit}"
                : a.trigger == AlarmTrigger.Above ? $"> {Format(a.threshold)}{unit}"
                : $"{a.dropPercent:0.#}% below {a.averageMinutes:0.#}-min average";
            return $"{item} {metric} ({area}) {cond}";
        }

        /// <summary>A value with its metric's unit, e.g. "22.1k/min", "92.5%", "1.2M".</summary>
        public static string FormatValue(AlarmDefinition a, double v)
        {
            return double.IsNaN(v) ? "-" : Format(v) + Metrics.Unit(a.metric);
        }

        public static string Format(double v)
        {
            if (double.IsNaN(v))
            {
                return "-";
            }
            double abs = Math.Abs(v);
            if (abs >= 1e9)
            {
                return (v / 1e9).ToString("0.##") + "G";
            }
            if (abs >= 1e6)
            {
                return (v / 1e6).ToString("0.##") + "M";
            }
            if (abs >= 1e4)
            {
                return (v / 1e3).ToString("0.#") + "k";
            }
            return v.ToString("0.#");
        }

        // ---------------------------------------------------------------- save / load

        public static void Export(BinaryWriter w)
        {
            w.Write(1); // container version
            int count = 0;
            foreach (AlarmDefinition a in Alarms)
            {
                if (!a.isDraft)
                {
                    count++;
                }
            }
            w.Write(count);
            foreach (AlarmDefinition a in Alarms)
            {
                if (!a.isDraft)
                {
                    a.Write(w);
                }
            }
        }

        public static void Import(BinaryReader r)
        {
            Clear();
            int version = r.ReadInt32();
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                AlarmDefinition a = AlarmDefinition.Read(r);
                a.ResetRuntime();
                Alarms.Add(a);
            }
            _ = version;
            Plugin.Log?.LogInfo($"Loaded {Alarms.Count} dashboard alarm(s)");
        }
    }
}
