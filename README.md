# Dashboard Alarms (Dyson Sphere Program mod)

Adds alarms to the in-game **Dashboard**. Choose a production or storage chart, set a rule, and
the mod warns you with a flashing chart border, a banner at the top of the screen and a game
sound when the rule is broken. The banner and sound work even when the dashboard is closed.

> **Status: v0.2.0, early testing.** The chart menu entry and the alarm window have been seen
> working in game (game version 0.10.35.29151). Alarm firing, the banner, sounds and saving are
> still being tested in game. The alarm logic passes offline tests. Please report problems.

## What you can watch

| Chart type on the dashboard | Alarm can watch |
|---|---|
| Item production chart | Production, consumption or net (production − consumption) per minute; **max production** per minute; **use % of max** (consumption ÷ max production) |
| Item storage chart | Total stock (every storage category the chart shows) |

Each alarm covers the same area as its chart. Set the chart to the whole cluster to watch every
planet.

Production, consumption and net use the chart's own time setting (1 minute, 10 minutes, 1 hour,
...), so the alarm sees the same number the chart shows. A chart set to "total" uses 1 minute.

**Max production** is the game's reference rate: the most that every powered machine making the
item can output per minute, counting proliferator. It is the "reference speed" in the game's
statistics tooltips. It stays steady when output is held back by full belts or low demand, so
**use % of max** shows how close demand is to what your factories can make. For example, Space
Warper, use % of max, Above 90, warns you before demand outgrows your warper factories.

## Triggers (one per alarm)

- **Below**: fires when the value drops below a number, for example white science production < 300k/min.
- **Above**: fires when the value goes above a number, for example hydrogen stock > 5M.
- **% drop from average**: fires when the value falls a set % below its own recent average, for
  example 20% below the 10-minute average. The average freezes while a drop is happening, so a
  long drop keeps alarming. Use **Reset average** to accept a new level as normal.

Every alarm also has a **hold time**: the rule has to stay broken for that many seconds before
the alarm fires. This keeps short dips from setting it off.

Number fields accept `300000`, `300,000`, `300k`, `1.5M` and `2G`.

## When an alarm fires

- The chart's border flashes red on the dashboard. It shows amber while the hold time is running.
- A red banner at the top of the screen lists every raised alarm and its current value.
- The built-in game sounds you picked play (one or more, together), and repeat every N seconds if you set a repeat interval.
- **Ack** silences the alarm and stops the flashing until the value recovers. If it breaks the
  rule again after that, it alarms again.
- **Snooze** silences the alarm completely for its snooze length. If the rule is still broken
  when the snooze ends, it alarms again.
- **Ack all**, **Snooze all** and **List** are on the banner.

## Using it

1. Open the Dashboard and add an item production or item storage chart the normal way.
2. Click the chart's menu button and choose **Add alarm...** at the bottom of the menu.
3. Pick what to watch, the trigger, the threshold, the hold time and the sounds, then press **Create**.
4. Open the same menu entry (**Edit alarm...**) later to change or delete the alarm.

**Ctrl+Shift+A** opens a list of every alarm. From the list you can turn alarms on and off,
edit them or delete them. The hotkey can be changed in the config file.

Alarms are saved with each save file through DSPModSave. If you delete a chart's stat plan, its
alarm is deleted too.

## Config (`BepInEx/config/buffylvr.dsp.dashboardalarms.cfg`)

| Setting | Default | Meaning |
|---|---|---|
| `General.SampleSeconds` | 2 | How often each alarm checks its chart's value |
| `General.StockRefreshSeconds` | 30 | How often stock and max-production alarms ask the game to recount storage and machines. The game counts one planet per tick, so on very large saves a lower number costs more CPU. Minimum 5. |
| `UI.Scale` | 1 | Size of the alarm windows and banner |
| `UI.AlarmListHotkey` | Ctrl+Shift+A | Opens and closes the alarm list |

## How it works

- **Rates:** the mod sums the game's own rolling one-minute production and consumption totals
  (`ProductStat.total`) over the chart's planets. These are the same numbers the statistics
  window shows for "1 min".
- **Stock:** the game only recounts storage when a panel asks for it, normally only while the
  dashboard is open. So stock alarms ask for a recount every `StockRefreshSeconds` and read the
  result once the recount finishes.
- The windows use Unity IMGUI. Invisible panels behind them stop your clicks from also reaching
  the dashboard underneath.

## Requirements

- BepInEx 5 (xiaoye97-BepInEx)
- CommonAPI-DSPModSave

## Building

```
dotnet build -c Release
```

The build references DLLs from your game folder and your BepInEx profile, and none of those
DLLs are in this repo. The default paths are the Steam install location and the r2modman
`Default` profile. To use other paths, create a `Directory.Build.user.props` file (it's
git-ignored):

```xml
<Project>
  <PropertyGroup>
    <DSPDir>D:\SteamLibrary\steamapps\common\Dyson Sphere Program</DSPDir>
    <BepInExDir>$(APPDATA)\r2modmanPlus-local\DysonSphereProgram\profiles\MyProfile\BepInEx</BepInExDir>
  </PropertyGroup>
</Project>
```

The output is `bin/Release/DashboardAlarms.dll`. To install it by hand, copy it to
`BepInEx/plugins/DashboardAlarms/`.

Offline logic tests (no game needed):

```
cd tests/LogicTests && dotnet build -c Release && bin/Release/net472/DashboardAlarms.LogicTests.exe
```

## Known limits (v0.1)

- Only item production and item storage charts can have alarms. Power, Dyson sphere and other
  chart types can't yet.
- The windows use the plain Unity IMGUI style, not the game's own UI style.
- While you type in a number field, some game hotkeys that don't check the game's "typing" flag
  might still react.

## License

MIT
