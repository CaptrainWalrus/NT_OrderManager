using System;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    public partial class MainStrategy : Strategy
    {
        /// <summary>
        /// Gets the appropriate series index for order operations
        /// Uses the already calculated smallestBarsInProgress value
        /// </summary>
        private int GetOrderSeriesIndex()
        {
            // smallestBarsInProgress is already calculated in State.DataLoaded
            // It contains the index of the series with the smallest timeframe
            return smallestBarsInProgress;
        }
    }
}