using System;
using System.Linq;
using System.IO;
using DashboardAlarms;

// Offline checks of the alarm state machine and save format. No game code runs here.
internal static class Program
{
    private static int failures;
    private const int S = AlarmManager.TicksPerSecond;

    private static int Main()
    {
        BelowWithHoldTime();
        AboveTrigger();
        DipShorterThanHoldDoesNotFire();
        PercentDropFromAverage();
        PercentDropNeedsWarmup();
        SnoozeClearsWhenFixed();
        SnoozeSuppressesThenReturns();
        DisabledAlarmStaysQuiet();
        SaveRoundTrip();
        LoadsVersion1Save();
        NewMetrics();
        NumberParsing();
        HarmonyTargetsResolve();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static AlarmDefinition New(AlarmTrigger trigger, double threshold, float hold)
    {
        return new AlarmDefinition { trigger = trigger, threshold = threshold, holdSeconds = hold, playSound = false };
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name} {detail}");
        if (!ok)
        {
            failures++;
        }
    }

    private static void BelowWithHoldTime()
    {
        AlarmDefinition a = New(AlarmTrigger.Below, 300_000, 30);
        long t = 0;
        AlarmManager.Evaluate(a, t, 400_000);
        Check("below: normal above threshold", a.state == AlarmState.Normal);
        t += 2 * S; AlarmManager.Evaluate(a, t, 250_000);
        Check("below: pending once under", a.state == AlarmState.Pending);
        t += 28 * S; AlarmManager.Evaluate(a, t, 250_000);
        Check("below: still pending at 28s", a.state == AlarmState.Pending, a.state.ToString());
        t += 2 * S; AlarmManager.Evaluate(a, t, 250_000);
        Check("below: alarming after 30s hold", a.state == AlarmState.Alarming, a.state.ToString());
        t += 2 * S; AlarmManager.Evaluate(a, t, 310_000);
        Check("below: clears when back above", a.state == AlarmState.Normal);
    }

    private static void AboveTrigger()
    {
        AlarmDefinition a = New(AlarmTrigger.Above, 1000, 0);
        AlarmManager.Evaluate(a, 0, 999);
        Check("above: normal under", a.state == AlarmState.Normal);
        AlarmManager.Evaluate(a, 2 * S, 1001);
        Check("above: fires immediately with 0 hold", a.state == AlarmState.Alarming);
    }

    private static void DipShorterThanHoldDoesNotFire()
    {
        AlarmDefinition a = New(AlarmTrigger.Below, 100, 10);
        long t = 0;
        AlarmManager.Evaluate(a, t, 50);
        t += 8 * S; AlarmManager.Evaluate(a, t, 50);
        t += 1 * S; AlarmManager.Evaluate(a, t, 150); // recovers before 10s
        t += 1 * S; AlarmManager.Evaluate(a, t, 50);  // dips again: timer restarts
        t += 8 * S; AlarmManager.Evaluate(a, t, 50);
        Check("hold: short dips never fire", a.state == AlarmState.Pending, a.state.ToString());
        t += 3 * S; AlarmManager.Evaluate(a, t, 50);
        Check("hold: continuous 11s fires", a.state == AlarmState.Alarming, a.state.ToString());
    }

    private static void PercentDropFromAverage()
    {
        AlarmDefinition a = New(AlarmTrigger.DropPercent, 0, 0);
        a.dropPercent = 20;
        a.averageMinutes = 2;
        long t = 0;
        for (int i = 0; i < 60; i++) // 2 minutes of steady 1000
        {
            AlarmManager.Evaluate(a, t, 1000);
            t += 2 * S;
        }
        Check("drop: steady value is normal", a.state == AlarmState.Normal);
        AlarmManager.Evaluate(a, t, 850);
        Check("drop: 15% drop is under the 20% trigger", a.state == AlarmState.Normal, $"avg {a.lastAverage}");
        t += 2 * S;
        AlarmManager.Evaluate(a, t, 700);
        Check("drop: 30% drop fires", a.state == AlarmState.Alarming, $"avg {a.lastAverage:0} state {a.state}");
        for (int i = 0; i < 300; i++) // the drop lasts 10 more minutes (5x the average window)
        {
            t += 2 * S;
            AlarmManager.Evaluate(a, t, 700);
        }
        Check("drop: a lasting drop keeps alarming (baseline frozen)", a.state == AlarmState.Alarming, $"avg {a.lastAverage:0} state {a.state}");
        t += 2 * S;
        AlarmManager.Evaluate(a, t, 990);
        Check("drop: recovery clears it", a.state == AlarmState.Normal, $"avg {a.lastAverage:0}");
        a.history.Clear(); // "Reset average" button
        for (int i = 0; i < 60; i++)
        {
            t += 2 * S;
            AlarmManager.Evaluate(a, t, 700);
        }
        Check("drop: after reset, the new level becomes normal", a.state == AlarmState.Normal, $"avg {a.lastAverage:0}");
    }

    private static void PercentDropNeedsWarmup()
    {
        AlarmDefinition a = New(AlarmTrigger.DropPercent, 0, 0);
        a.dropPercent = 20;
        a.averageMinutes = 10;
        AlarmManager.Evaluate(a, 0, 1000);
        AlarmManager.Evaluate(a, 2 * S, 100);
        Check("drop: no alarm before half the average window has data", a.state == AlarmState.Normal);
    }

    private static void SnoozeClearsWhenFixed()
    {
        AlarmDefinition a = New(AlarmTrigger.Below, 100, 0);
        AlarmManager.Evaluate(a, 0, 10);
        AlarmManager.Snooze(a, 10, 0);
        AlarmManager.Evaluate(a, 2 * S, 10);
        Check("snooze: shown in banner while snoozed", a.IsShown && !a.IsRaised);
        AlarmManager.Evaluate(a, 4 * S, 200);
        Check("snooze: value recovers during snooze -> normal", a.state == AlarmState.Normal && !a.IsShown, a.state.ToString());
        AlarmManager.Evaluate(a, 6 * S, 10);
        Check("snooze: a new occurrence alarms again", a.state == AlarmState.Alarming, a.state.ToString());
    }

    private static void SnoozeSuppressesThenReturns()
    {
        AlarmDefinition a = New(AlarmTrigger.Below, 100, 60); // 60s hold must not delay the return
        AlarmManager.Evaluate(a, 0, 10);
        AlarmManager.Evaluate(a, 60 * S, 10);
        Check("snooze: alarming before snooze", a.state == AlarmState.Alarming, a.state.ToString());
        AlarmManager.Snooze(a, 1, 60 * S);
        AlarmManager.Evaluate(a, 90 * S, 10);
        Check("snooze: suppressed during snooze", a.state == AlarmState.Snoozed);
        AlarmManager.Evaluate(a, 121 * S, 10);
        Check("snooze: alarms again right after it ends (no new hold)", a.state == AlarmState.Alarming, a.state.ToString());

        AlarmDefinition b = New(AlarmTrigger.Below, 100, 0);
        AlarmManager.Evaluate(b, 0, 10);
        AlarmManager.Snooze(b, 10, 0);
        AlarmManager.Unsnooze(b, 2 * S);
        AlarmManager.Evaluate(b, 2 * S, 10);
        Check("snooze: unsnooze alarms again at once", b.state == AlarmState.Alarming, b.state.ToString());

        AlarmDefinition c = New(AlarmTrigger.Below, 100, 30);
        AlarmManager.Evaluate(c, 0, 10); // pending, not alarming yet
        AlarmManager.Snooze(c, 10, 0);
        Check("snooze: only an alarming alarm can be snoozed", c.state == AlarmState.Pending, c.state.ToString());
    }

    private static void DisabledAlarmStaysQuiet()
    {
        AlarmDefinition a = New(AlarmTrigger.Below, 100, 0);
        a.enabled = false;
        // Tick() skips disabled alarms before Evaluate; ConditionMet alone must not change state.
        a.lastValue = 10;
        Check("disabled: state untouched", a.state == AlarmState.Normal && AlarmManager.ConditionMet(a));
    }

    private static void SaveRoundTrip()
    {
        var a = new AlarmDefinition
        {
            statPlanId = 42, planType = 1, itemId = 6006, astroFilter = -1, enabled = true,
            metric = AlarmMetric.Net, trigger = AlarmTrigger.DropPercent, threshold = 12345.5, dropPercent = 17.5,
            averageMinutes = 7, holdSeconds = 45, playSound = true, repeatSoundSeconds = 0, snoozeMinutes = 15,
        };
        a.sounds.Clear();
        a.sounds.Add("df-alarm");
        a.sounds.Add("ui-error");
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            a.Write(w);
        }
        ms.Position = 0;
        AlarmDefinition b;
        using (var r = new BinaryReader(ms))
        {
            b = AlarmDefinition.Read(r);
        }
        bool same = b.statPlanId == 42 && b.planType == 1 && b.itemId == 6006 && b.astroFilter == -1 && b.enabled
            && b.metric == AlarmMetric.Net && b.trigger == AlarmTrigger.DropPercent && b.threshold == 12345.5
            && b.dropPercent == 17.5 && b.averageMinutes == 7 && b.holdSeconds == 45 && b.playSound
            && b.sounds.Count == 2 && b.sounds[0] == "df-alarm" && b.sounds[1] == "ui-error" && b.repeatSoundSeconds == 0 && b.snoozeMinutes == 15;
        Check("save: definition round-trips", same);
    }

    /// <summary>A save written by v0.1.0 (format 1, one sound string) must still load.</summary>
    private static void LoadsVersion1Save()
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Write(1);            // version
            w.Write(7); w.Write(1); w.Write(1210); w.Write(-1); // plan id, type, item, area
            w.Write(true);         // enabled
            w.Write((int)AlarmMetric.Consumption);
            w.Write((int)AlarmTrigger.Above);
            w.Write(5000.0);       // threshold
            w.Write(20.0);         // drop %
            w.Write(10f);          // average minutes
            w.Write(30f);          // hold
            w.Write(true);         // play sound
            w.Write("ui-error");   // the single v1 sound
            w.Write(15f);          // repeat
            w.Write(5f);           // snooze
        }
        ms.Position = 0;
        AlarmDefinition b;
        using (var r = new BinaryReader(ms))
        {
            b = AlarmDefinition.Read(r);
            Check("save v1: reads to the end", r.BaseStream.Position == r.BaseStream.Length);
        }
        Check("save v1: fields and single sound load", b.statPlanId == 7 && b.itemId == 1210 && b.metric == AlarmMetric.Consumption
            && b.threshold == 5000 && b.sounds.Count == 1 && b.sounds[0] == "ui-error" && b.repeatSoundSeconds == 15 && b.snoozeMinutes == 5);
    }

    private static void NewMetrics()
    {
        double use = AlarmManager.ComputeSample(AlarmMetric.UsePercent, 22153, 22656, 32000);
        Check("metric: use % = consumption / max", Math.Abs(use - 70.8) < 0.01, use.ToString("0.###"));
        Check("metric: use % waits for max", double.IsNaN(AlarmManager.ComputeSample(AlarmMetric.UsePercent, 1, 1, double.NaN)));
        Check("metric: use % with no machines gives no reading", double.IsNaN(AlarmManager.ComputeSample(AlarmMetric.UsePercent, 0, 5, 0)));
        Check("metric: max production", AlarmManager.ComputeSample(AlarmMetric.MaxProduction, 1, 2, 32000) == 32000);
        Check("metric: net", AlarmManager.ComputeSample(AlarmMetric.Net, 100, 30, double.NaN) == 70);

        // "warn when I'm using 90% of what I can make": above 90 with a hold
        AlarmDefinition a = New(AlarmTrigger.Above, 90, 30);
        a.metric = AlarmMetric.UsePercent;
        long t = 0;
        AlarmManager.Evaluate(a, t, AlarmManager.ComputeSample(a.metric, 0, 28000, 32000)); // 87.5%
        Check("use %: 87.5% is fine", a.state == AlarmState.Normal);
        t += 2 * S; AlarmManager.Evaluate(a, t, AlarmManager.ComputeSample(a.metric, 0, 29500, 32000)); // 92.2%
        t += 30 * S; AlarmManager.Evaluate(a, t, AlarmManager.ComputeSample(a.metric, 0, 29500, 32000));
        Check("use %: 92% held 30s alarms", a.state == AlarmState.Alarming, a.state.ToString());
    }

    /// <summary>Every [HarmonyPatch] must name a real game method with matching parameter names.</summary>
    private static void HarmonyTargetsResolve()
    {
        try
        {
            // Resolve each patch's target the same way Harmony does, without detouring anything.
            foreach (var m in Array.ConvertAll(new[] { typeof(Patches), typeof(OptionsToggle) }, t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)).SelectMany(x => x))
            {
                var attrs = m.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false);
                if (attrs.Length == 0)
                {
                    continue;
                }
                var info = ((HarmonyLib.HarmonyPatch)attrs[0]).info;
                var target = HarmonyLib.AccessTools.Method(info.declaringType, info.methodName);
                bool ok = target != null && target.DeclaringType == info.declaringType;
                string detail = target == null ? "NOT FOUND" : $"{target.DeclaringType.Name}.{target.Name}({string.Join(", ", Array.ConvertAll(target.GetParameters(), p => p.ParameterType.Name + " " + p.Name))})";
                // injected args (other than __instance) must match an original parameter name
                foreach (var p in m.GetParameters())
                {
                    if (p.Name.StartsWith("__") || target == null)
                    {
                        continue;
                    }
                    if (Array.Find(target.GetParameters(), q => q.Name == p.Name) == null)
                    {
                        ok = false;
                        detail += $" (no original parameter named {p.Name})";
                    }
                }
                Check($"harmony: {m.Name}", ok, detail);
            }
        }
        catch (Exception e)
        {
            Check("harmony: targets resolve", false, e.GetType().Name + ": " + e.Message);
        }
    }

    private static void NumberParsing()
    {
        Check("parse: 1.5k", AlarmWindow.TryParse("1.5k", out double v1) && v1 == 1500, v1.ToString());
        Check("parse: 2M", AlarmWindow.TryParse("2M", out double v2) && v2 == 2_000_000, v2.ToString());
        Check("parse: 300,000", AlarmWindow.TryParse("300,000", out double v3) && v3 == 300_000, v3.ToString());
        Check("parse: junk rejected", !AlarmWindow.TryParse("abc", out _));
    }
}
