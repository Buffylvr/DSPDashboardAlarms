using System.Collections.Generic;

namespace DashboardAlarms
{
    /// <summary>
    /// Reads the live numbers behind a dashboard stat plan.
    ///
    /// Rates come from the game's rolling production statistics: for every factory,
    /// ProductStat.total[level + 1] is items produced over the chart's time window and
    /// total[level + 8] items consumed, divided down to per minute exactly as the chart does.
    ///
    /// Storage counts are not kept up to date by the game; they are recomputed on demand by
    /// ProductionExtraInfoCalculator, one factory per tick, normally only while the dashboard
    /// is open. AlarmManager asks for that refresh itself for stock alarms.
    /// </summary>
    public static class StatReader
    {
        private const int ProductionLastMinute = 1;   // StatPlan.kProductionStat_OneMin + 1
        private const int ConsumptionLastMinute = 8;  // + 7 for the consumption half

        private static readonly List<int> factoryScratch = new List<int>();

        public static bool SupportsRate(StatPlan plan) => plan is AstroItemProductionStatPlan;

        public static bool SupportsStock(StatPlan plan) => plan is AstroStorageStatPlan;

        public static bool IsSupported(StatPlan plan) => SupportsRate(plan) || SupportsStock(plan);

        public static int ItemId(StatPlan plan)
        {
            switch (plan)
            {
                case AstroItemProductionStatPlan p: return p.itemId;
                case AstroStorageStatPlan s: return s.itemId;
                default: return 0;
            }
        }

        public static int AstroFilter(StatPlan plan)
        {
            switch (plan)
            {
                case AstroItemProductionStatPlan p: return p.astroFilter;
                case AstroStorageStatPlan s: return s.astroFilter;
                default: return 0;
            }
        }

        // Same divisors the chart uses (UIChartAstroItemProduction.ShowInText) to turn a level's total into a per-minute rate.
        private static readonly double[] LevelMinutes = { 1, 10, 60, 600, 6000 };
        private static readonly string[] LevelNames = { "1-minute", "10-minute", "1-hour", "10-hour", "100-hour" };

        /// <summary>The chart's time setting (0..4); the "total" setting (5) falls back to 1 minute.</summary>
        public static int RateLevel(AstroItemProductionStatPlan plan) => plan.timeLevel >= 0 && plan.timeLevel <= 4 ? plan.timeLevel : 0;

        public static string RateWindowName(AstroItemProductionStatPlan plan) => LevelNames[RateLevel(plan)];

        /// <summary>
        /// Production and consumption per minute over the plan's area (whole cluster when astroFilter is -1),
        /// averaged over the chart's own time window so the value matches what the chart shows.
        /// </summary>
        public static bool TryReadRates(GameData data, AstroItemProductionStatPlan plan, out double production, out double consumption)
        {
            production = 0;
            consumption = 0;
            if (data?.statistics?.production == null || plan.itemId <= 0)
            {
                return false;
            }
            int level = RateLevel(plan);
            int prodIndex = ProductionLastMinute + level;
            int consIndex = ConsumptionLastMinute + level;
            FactoryProductionStat[] pool = data.statistics.production.factoryStatPool;
            CollectFactories(data, plan.astroFilter, factoryScratch);
            foreach (int fi in factoryScratch)
            {
                if (fi < 0 || fi >= pool.Length || pool[fi] == null)
                {
                    continue;
                }
                FactoryProductionStat fs = pool[fi];
                int idx = fs.productIndices[plan.itemId];
                if (idx <= 0)
                {
                    continue;
                }
                ProductStat ps = fs.productPool[idx];
                production += ps.total[prodIndex];
                consumption += ps.total[consIndex];
            }
            production /= LevelMinutes[level];
            consumption /= LevelMinutes[level];
            return true;
        }

        /// <summary>
        /// The game's reference production rate per minute: what every powered machine making the item
        /// could output (with the best unlocked proliferator where it is used). Only valid after the
        /// game's extra-info recount has run for the plan's factories.
        /// </summary>
        public static double ReadMaxProduction(AstroItemProductionStatPlan plan)
        {
            plan.GetItemsCyclicRefSpeed(out float productRefSpeed, out float _);
            return productRefSpeed;
        }

        /// <summary>Sum of every storage category the storage chart shows (boxes, logistics, belts, other, player).</summary>
        public static bool TryReadStock(AstroStorageStatPlan plan, out double stock)
        {
            stock = 0;
            if (plan.itemId <= 0 || plan.detailedStorageCounts == null)
            {
                return false;
            }
            long[] counts = plan.GetDetailedStorageCount();
            long total = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                total += counts[i];
            }
            stock = total;
            return true;
        }

        /// <summary>Same area rules as AstroItemProductionStatPlan / AstroStorageStatPlan.</summary>
        public static void CollectFactories(GameData data, int astroFilter, List<int> into)
        {
            into.Clear();
            if (astroFilter == -1)
            {
                for (int i = 0; i < data.factoryCount; i++)
                {
                    into.Add(i);
                }
                return;
            }
            if (astroFilter == 0)
            {
                if (data.localPlanet?.factory != null)
                {
                    into.Add(data.localPlanet.factoryIndex);
                }
                return;
            }
            if (astroFilter % 100 == 0)
            {
                StarData star = data.galaxy.StarById(astroFilter / 100);
                if (star == null)
                {
                    return;
                }
                for (int i = 0; i < star.planetCount; i++)
                {
                    if (star.planets[i].factory != null)
                    {
                        into.Add(star.planets[i].factoryIndex);
                    }
                }
                return;
            }
            PlanetData planet = data.galaxy.PlanetById(astroFilter);
            if (planet?.factory != null)
            {
                into.Add(planet.factoryIndex);
            }
        }

        public static string ItemName(int itemId)
        {
            ItemProto proto = itemId > 0 ? LDB.items.Select(itemId) : null;
            return proto != null ? proto.name : "item " + itemId;
        }

        public static string AreaName(GameData data, int astroFilter)
        {
            if (astroFilter == -1)
            {
                return "Cluster";
            }
            if (astroFilter == 0)
            {
                return "Local planet";
            }
            if (astroFilter % 100 == 0)
            {
                return data.galaxy.StarById(astroFilter / 100)?.displayName ?? "Star " + astroFilter / 100;
            }
            return data.galaxy.PlanetById(astroFilter)?.displayName ?? "Planet " + astroFilter;
        }
    }
}
