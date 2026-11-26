using System.Collections.Generic;

namespace DashboardAlarms
{
    /// <summary>
    /// Reads the live numbers behind a dashboard stat plan.
    ///
    /// Rates come from the game's rolling production statistics: for every factory,
    /// ProductStat.total[1] is items produced over the last minute and total[8] items consumed
    /// over the last minute (the same numbers the 1-minute statistics view shows).
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

        /// <summary>Production and consumption per minute over the plan's area (whole cluster when astroFilter is -1).</summary>
        public static bool TryReadRates(GameData data, AstroItemProductionStatPlan plan, out double production, out double consumption)
        {
            production = 0;
            consumption = 0;
            if (data?.statistics?.production == null || plan.itemId <= 0)
            {
                return false;
            }
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
                production += ps.total[ProductionLastMinute];
                consumption += ps.total[ConsumptionLastMinute];
            }
            return true;
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
