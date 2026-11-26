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
                else if (plan is AstroItemProductionStatPlan pp && StatReader.TryReadRates(data, pp, out double prod, out double cons))
                {
                    sample = a.metric == AlarmMetric.Production ? prod : a.metric == AlarmMetric.Consumption ? cons : prod - cons;
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
                    PlaySound(a.sound);
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

        public static void PlaySound(string name)
        {
            try
            {
                VFAudio.Create(string.IsNullOrEmpty(name) ? Sounds.Default : name, null, Vector3.zero, play: true, 0, -1, -1L);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"Could not play sound '{name}': {e.Message}");
            }
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
            // Any finished recount (ours or the dashboard's own) leaves fresh storage numbers.
            stockRequested = false;
            stockFresh = true;
        }

        private static void RequestStockRefreshIfDue(GameData data, long now)
        {
            ProductionExtraInfoCalculator calc = data.statistics.production.extraInfoCalculator;
            if (calc.calculating)
            {
                return;
            }
            long interval = (long)(Math.Max(5f, StockRefreshSeconds) * TicksPerSecond);
            if (lastStockRequestTick >= 0 && now - lastStockRequestTick < interval && now >= lastStockRequestTick && !(stockRequested && now - lastStockRequestTick > interval * 4))
            {
                return;
            }
            bool any = false;
            foreach (AlarmDefinition a in Alarms)
            {
                if (!a.enabled || a.metric != AlarmMetric.Stock)
                {
                    continue;
                }
                if (ResolvePlan(data, a) is AstroStorageStatPlan sp && sp.itemId > 0)
                {
                    sp.CalculateExtraInfo(); // queues the factories; the game counts one per tick
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
            string metric = a.metric == AlarmMetric.Production ? "production"
                : a.metric == AlarmMetric.Consumption ? "consumption"
                : a.metric == AlarmMetric.Net ? "net" : "stock";
            string unit = a.metric == AlarmMetric.Stock ? "" : "/min";
            string cond = a.trigger == AlarmTrigger.Below ? $"< {Format(a.threshold)}{unit}"
                : a.trigger == AlarmTrigger.Above ? $"> {Format(a.threshold)}{unit}"
                : $"{a.dropPercent:0.#}% below {a.averageMinutes:0.#}-min average";
            return $"{item} {metric} ({area}) {cond}";
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
