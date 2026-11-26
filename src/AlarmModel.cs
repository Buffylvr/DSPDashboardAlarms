using System.Collections.Generic;
using System.IO;

namespace DashboardAlarms
{
    /// <summary>Which number of the watched dashboard chart the alarm looks at.</summary>
    public enum AlarmMetric
    {
        Production = 0,    // items produced per minute
        Consumption = 1,   // items consumed per minute
        Net = 2,           // production - consumption per minute
        Stock = 3,         // items held in storage (storage charts only)
        MaxProduction = 4, // the game's reference rate: most the area's powered machines can make per minute
        UsePercent = 5,    // consumption as a % of MaxProduction
    }

    public enum AlarmTrigger
    {
        Below = 0,        // value < threshold
        Above = 1,        // value > threshold
        DropPercent = 2,  // value has fallen dropPercent % below its recent average
    }

    public enum AlarmState
    {
        Normal = 0,       // condition not met
        Pending = 1,      // condition met, waiting out the hold time
        Alarming = 2,     // flashing + sound
        Snoozed = 4,      // silenced until snoozeUntilTick; still listed (dimmed) in the banner, cleared if the value recovers
    }

    public static class Metrics
    {
        /// <summary>Metrics that need the game's on-demand recount (ProductionExtraInfoCalculator).</summary>
        public static bool NeedsRecount(AlarmMetric m) => m == AlarmMetric.Stock || NeedsMax(m);

        public static bool NeedsMax(AlarmMetric m) => m == AlarmMetric.MaxProduction || m == AlarmMetric.UsePercent;

        public static string Unit(AlarmMetric m) => m == AlarmMetric.Stock ? "" : m == AlarmMetric.UsePercent ? "%" : "/min";

        public static string Name(AlarmMetric m)
        {
            switch (m)
            {
                case AlarmMetric.Production: return "production";
                case AlarmMetric.Consumption: return "consumption";
                case AlarmMetric.Net: return "net";
                case AlarmMetric.Stock: return "stock";
                case AlarmMetric.MaxProduction: return "max production";
                case AlarmMetric.UsePercent: return "use % of max production";
                default: return m.ToString();
            }
        }
    }

    /// <summary>One alarm, attached to one dashboard stat plan. Saved with the game.</summary>
    public class AlarmDefinition
    {
        // ---- identity of the stat plan it watches (validated on load so a recycled id is not misattached)
        public int statPlanId;
        public int planType;
        public int itemId;
        public int astroFilter;

        // ---- settings
        public bool enabled = true;
        public AlarmMetric metric = AlarmMetric.Production;
        public AlarmTrigger trigger = AlarmTrigger.Below;
        public double threshold;
        public double dropPercent = 20;
        public float averageMinutes = 10f;
        public float holdSeconds = 30f;
        public bool playSound = true;
        /// <summary>Every sound here plays together when the alarm sounds.</summary>
        public List<string> sounds = new List<string> { Sounds.Default };
        public float repeatSoundSeconds = 30f; // 0 = sound once per alarm
        public float snoozeMinutes = 10f;      // default length the Snooze button uses

        // ---- runtime (not saved)
        public AlarmState state;
        public long conditionSinceTick = -1;
        public long snoozeUntilTick;
        public long lastSoundTick = -1;
        public double lastValue = double.NaN;
        public double lastAverage = double.NaN;
        public bool hasValue;
        /// <summary>Last max production read from the game (per minute); valid when hasMax.</summary>
        public double lastMax = double.NaN;
        public bool hasMax;
        /// <summary>Being created in the editor: not evaluated or saved until Create is pressed.</summary>
        public bool isDraft;
        public readonly RollingAverage history = new RollingAverage();

        /// <summary>Actively alarming (sound + flashing).</summary>
        public bool IsRaised => state == AlarmState.Alarming;

        /// <summary>Listed in the banner: alarming, or snoozed while the problem is still there.</summary>
        public bool IsShown => state == AlarmState.Alarming || state == AlarmState.Snoozed;

        /// <summary>1: single sound. 2: list of sounds.</summary>
        public const int SaveVersion = 2;

        public void Write(BinaryWriter w)
        {
            w.Write(SaveVersion);
            w.Write(statPlanId);
            w.Write(planType);
            w.Write(itemId);
            w.Write(astroFilter);
            w.Write(enabled);
            w.Write((int)metric);
            w.Write((int)trigger);
            w.Write(threshold);
            w.Write(dropPercent);
            w.Write(averageMinutes);
            w.Write(holdSeconds);
            w.Write(playSound);
            w.Write(sounds.Count);
            foreach (string s in sounds)
            {
                w.Write(s ?? Sounds.Default);
            }
            w.Write(repeatSoundSeconds);
            w.Write(snoozeMinutes);
        }

        public static AlarmDefinition Read(BinaryReader r)
        {
            int version = r.ReadInt32();
            var a = new AlarmDefinition
            {
                statPlanId = r.ReadInt32(),
                planType = r.ReadInt32(),
                itemId = r.ReadInt32(),
                astroFilter = r.ReadInt32(),
                enabled = r.ReadBoolean(),
                metric = (AlarmMetric)r.ReadInt32(),
                trigger = (AlarmTrigger)r.ReadInt32(),
                threshold = r.ReadDouble(),
                dropPercent = r.ReadDouble(),
                averageMinutes = r.ReadSingle(),
                holdSeconds = r.ReadSingle(),
                playSound = r.ReadBoolean(),
            };
            a.sounds.Clear();
            if (version >= 2)
            {
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    a.sounds.Add(r.ReadString());
                }
            }
            else
            {
                a.sounds.Add(r.ReadString());
            }
            a.repeatSoundSeconds = r.ReadSingle();
            a.snoozeMinutes = r.ReadSingle();
            return a;
        }

        /// <summary>After an edit: drop alarm state and hold timer but keep the sample history.</summary>
        public void ResetRuntimeTimersOnly()
        {
            state = AlarmState.Normal;
            conditionSinceTick = -1;
            snoozeUntilTick = 0;
            lastSoundTick = -1;
        }

        public void ResetRuntime()
        {
            state = AlarmState.Normal;
            conditionSinceTick = -1;
            snoozeUntilTick = 0;
            lastSoundTick = -1;
            lastValue = double.NaN;
            lastAverage = double.NaN;
            hasValue = false;
            history.Clear();
        }
    }

    /// <summary>Time-stamped samples kept for the "drop from average" trigger.</summary>
    public class RollingAverage
    {
        private readonly Queue<KeyValuePair<long, double>> samples = new Queue<KeyValuePair<long, double>>();
        private double sum;

        public int Count => samples.Count;

        public void Clear()
        {
            samples.Clear();
            sum = 0;
        }

        public void Add(long tick, double value, long windowTicks)
        {
            samples.Enqueue(new KeyValuePair<long, double>(tick, value));
            sum += value;
            while (samples.Count > 0 && samples.Peek().Key < tick - windowTicks)
            {
                sum -= samples.Dequeue().Value;
            }
        }

        /// <summary>Span in ticks covered by the samples held.</summary>
        public long Span(long nowTick) => samples.Count == 0 ? 0 : nowTick - samples.Peek().Key;

        public double Mean => samples.Count == 0 ? double.NaN : sum / samples.Count;
    }

    public static class Sounds
    {
        public const string Default = "warning-popup-1";

        /// <summary>Built-in game sounds offered in the alarm window (audio name, label).</summary>
        public static readonly string[,] Options =
        {
            { "warning-popup-1", "Warning popup" },
            { "warning-popup-0", "Tutorial warning" },
            { "df-alarm", "Dark Fog alarm" },
            { "ui-error", "UI error" },
            { "research-complete", "Research complete" },
        };
    }
}
