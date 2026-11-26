# Changelog

## 0.3.0
- New checkbox in the game's **Settings > Audio** tab: "Dashboard alarms play with sound off" (on by default).
  On: alarms play at `Sound.AlarmVolume` even with the game's master or sound effects volume down or off.
  Off: alarms follow the game's Game Master x Sound Effects volume. Saved on Apply; also `Sound.PlayWithSoundOff` in the config.

## 0.2.3
- Banner sized from its text: long alarm lines wrap onto extra lines and the banner grows to fit; buttons are
  as wide as their labels. Same for the list window's buttons ("X" is now "Delete").
- Confirmed in game: an alarm fires and shows the banner.

## 0.2.2
- Alarm sounds no longer use the game's Sound Effects slider, so they are heard with effects turned off. They play
  through the mod's own 2D audio source at `Sound.AlarmVolume` (new, default 1) x the game's Game Master Volume.

## 0.2.1
- Sounds play as plain 2D sounds at the listener, so the player's position can't make them inaudible.
- The Test button writes a sound report to the BepInEx log (sound found, volumes, listener, whether it is playing).
- A sound the game refuses is now logged instead of failing silently.

## 0.2.0
- New "Watch" choices on production charts: **Max production/min** (the game's reference rate: what every
  powered machine making the item can output, counting proliferator) and **Use % of max** (consumption as
  a % of max production), e.g. "alarm when I'm using more than 90% of what I can make".
- Production, consumption and net now use the chart's own time window (1 min, 10 min, 1 h, ...), so the
  alarm's value matches the number the chart shows. Charts set to "total" use 1 minute.
- Play sound: pick one or more sounds; they play together. Old saves keep their single sound.
- The selected choice in each button group is now clearly highlighted.
- Storage and max-production recounts are only trusted when they include the alarm's own planets, and are
  requested even while the dashboard keeps the game's counter busy.
- The editor now loads in game (v0.1.0 menu entry and window confirmed working).

## 0.1.0
- First version: alarms on item production (production/consumption/net per minute) and item storage (stock) dashboard charts.
- Below / above / % drop from average triggers, per-alarm hold time.
- Flashing chart border, top-of-screen banner, built-in game sound with optional repeat.
- Acknowledge and snooze, per alarm or all at once; alarm list (Ctrl+Shift+A).
- Saved per save file through DSPModSave.
