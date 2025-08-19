#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Reflection;
using System.Drawing;
using System.IO;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// Flexible variable system for droplet payloads
    /// Allows dynamic creation of standardized vs custom data structures
    /// </summary>
    public class DropletVariable
    {
        public string Name { get; set; }
        public object Value { get; set; }
        public string DataType { get; set; }
        public bool IsStandardized { get; set; } // true = standardized, false = custom
        
        public DropletVariable(string name, object value, string dataType, bool isStandardized = true)
        {
            Name = name;
            Value = value;
            DataType = dataType;
            IsStandardized = isStandardized;
        }
    }

    /// <summary>
    /// Collection of variables that can be sent to droplet
    /// Automatically separates standardized vs custom variables
    /// </summary>
    public class DropletVariableCollection
    {
        private List<DropletVariable> variables = new List<DropletVariable>();
        
        public void AddVariable(string name, object value, string dataType, bool isStandardized = true)
        {
            variables.Add(new DropletVariable(name, value, dataType, isStandardized));
        }
        
        public void AddStandardizedVariable(string name, object value, string dataType)
        {
            AddVariable(name, value, dataType, true);
        }
        
        public void AddCustomVariable(string name, object value, string dataType)
        {
            AddVariable(name, value, dataType, false);
        }
        
        public List<DropletVariable> GetStandardizedVariables()
        {
            return variables.Where(v => v.IsStandardized).ToList();
        }
        
        public List<DropletVariable> GetCustomVariables()
        {
            return variables.Where(v => !v.IsStandardized).ToList();
        }
        
        public Dictionary<string, object> BuildStandardizedPayload()
        {
            var payload = new Dictionary<string, object>();
            foreach (var variable in GetStandardizedVariables())
            {
                payload[variable.Name] = variable.Value;
            }
            return payload;
        }
        
        public Dictionary<string, object> BuildCustomPayload()
        {
            var payload = new Dictionary<string, object>();
            foreach (var variable in GetCustomVariables())
            {
                payload[variable.Name] = variable.Value;
            }
            return payload;
        }
        
        public void Clear()
        {
            variables.Clear();
        }
    }

    /// <summary>
    /// Concise trade outcome data for current strategy design
    /// Replaces PositionOutcomeData with traditional trading values
    /// </summary>
    public class TradeOutcomeData
    {
        // Core trade results
        public double ExitPrice { get; set; }
        public double PnLDollars { get; set; }
        public double PnLPoints { get; set; }
        public int HoldingBars { get; set; }
        public string ExitReason { get; set; }
        public string Instrument { get; set; }
        public string ExitOrderUUID { get; set; }
        // Timestamps
        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }
        
        // Risk management
        public double MaxProfit { get; set; }
        public double MaxLoss { get; set; }
        public double EntryPrice { get; set; }
        public int Quantity { get; set; } = 1;
        
        // Traditional strategy values
        public double StopLoss { get; set; }
        public double TakeProfit { get; set; }
        public double RiskRewardRatio { get; set; }
        public string PatternType { get; set; }
        public double PatternConfidence { get; set; }
        
        // Signal identification
        public string EntrySignalType { get; set; }
        
        // Optional trajectory data
        public Dictionary<int, double> ProfitByBar { get; set; }
        
        // Trade efficiency metrics
        public double EntryEfficiency { get; set; }
        public double ExitEfficiency { get; set; }
        public double TotalEfficiency { get; set; }
        public double MaxAdverseExcursion { get; set; }
        public double MaxFavorableExcursion { get; set; }
        public double NetProfitPercent { get; set; }
        
        // Session context metrics
        public double CumulativeProfit { get; set; }
        public int TradeNumber { get; set; }
        public double CurrentDrawdown { get; set; }
        public int ConsecutiveWins { get; set; }
        public int ConsecutiveLosses { get; set; }
        
        // Trade correlation analysis fields
        public int EntryHour { get; set; }
        public int EntryMinute { get; set; }
        public string DayOfWeek { get; set; }
        public double TradeDurationMinutes { get; set; }
        public double EntryVolume { get; set; }
        public double AtrAtEntry { get; set; }
        public double VwapDistance { get; set; }
        public double EmaDistance { get; set; }
        public int DailyTradeSequence { get; set; }
        public string PreviousTradeResult { get; set; }
        
		public string exitReason {get; set;}
       
    }

    /// <summary>
    /// Builder pattern for creating droplet payloads
    /// Makes it easy to construct standardized vs custom data
    /// </summary>
    public class DropletPayloadBuilder
    {
        private DropletVariableCollection variables = new DropletVariableCollection();
        
        // Standardized variable methods
        public DropletPayloadBuilder AddInstrument(string instrument)
        {
            variables.AddStandardizedVariable("instrument", instrument, "string");
            return this;
        }
        
        public DropletPayloadBuilder AddDirection(string direction)
        {
            variables.AddStandardizedVariable("direction", direction, "string");
            return this;
        }
        
        public DropletPayloadBuilder AddExitPrice(double exitPrice)
        {
            variables.AddStandardizedVariable("exitPrice", exitPrice, "double");
            return this;
        }
        
        public DropletPayloadBuilder AddPnL(double pnlDollars, double pnlPoints)
        {
            variables.AddStandardizedVariable("pnlDollars", pnlDollars, "double");
            variables.AddStandardizedVariable("pnlPoints", pnlPoints, "double");
            return this;
        }
        
        public DropletPayloadBuilder AddHoldingBars(int holdingBars)
        {
            variables.AddStandardizedVariable("holdingBars", holdingBars, "int");
            return this;
        }
        
        public DropletPayloadBuilder AddExitReason(string exitReason)
        {
            variables.AddStandardizedVariable("exitReason", exitReason, "string");
            return this;
        }
        
        public DropletPayloadBuilder AddTimestamps(DateTime entryTime, DateTime exitTime)
        {
            variables.AddStandardizedVariable("entryTime", entryTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), "string");
            variables.AddStandardizedVariable("exitTime", exitTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), "string");
            return this;
        }
        
        public DropletPayloadBuilder AddProfitTrajectory(Dictionary<int, double> profitByBar)
        {
            variables.AddStandardizedVariable("profitByBar", profitByBar, "Dictionary<int,double>");
            return this;
        }
        
        // Custom variable methods
        public DropletPayloadBuilder AddEntryOrderUUID(string entryOrderUUID)
        {
            variables.AddCustomVariable("entryOrderUUID", entryOrderUUID, "string");
            return this;
        }
        
        public DropletPayloadBuilder AddPatternInfo(string patternSubtype, string patternId)
        {
            variables.AddCustomVariable("patternSubtype", patternSubtype, "string");
            variables.AddCustomVariable("patternId", patternId, "string");
            return this;
        }
        
        public DropletPayloadBuilder AddMaxProfitLoss(double maxProfit, double maxLoss)
        {
            variables.AddCustomVariable("maxProfit", maxProfit, "double");
            variables.AddCustomVariable("maxLoss", maxLoss, "double");
            return this;
        }
        
        public DropletPayloadBuilder AddDailyMetrics(double dailyProfit, int tradeNumber)
        {
            variables.AddCustomVariable("dailyProfit", dailyProfit, "double");
            variables.AddCustomVariable("tradeNumber", tradeNumber, "int");
            return this;
        }
        
        public DropletPayloadBuilder AddQuantity(int quantity)
        {
            variables.AddCustomVariable("quantity", quantity, "int");
            return this;
        }
        
        // Generic methods for complete flexibility
        public DropletPayloadBuilder AddStandardized(string name, object value, string dataType)
        {
            variables.AddStandardizedVariable(name, value, dataType);
            return this;
        }
        
        public DropletPayloadBuilder AddCustom(string name, object value, string dataType)
        {
            variables.AddCustomVariable(name, value, dataType);
            return this;
        }
        
        // Build methods
        public Dictionary<string, object> BuildStandardizedPayload()
        {
            return variables.BuildStandardizedPayload();
        }
        
        public Dictionary<string, object> BuildCustomPayload()
        {
            return variables.BuildCustomPayload();
        }
        
        public DropletVariableCollection GetVariables()
        {
            return variables;
        }
        
        public void Reset()
        {
            variables.Clear();
        }
    }

    /// <summary>
    /// Enhanced outcome data class for droplet integration
    /// Extends PositionOutcomeData with builder pattern support
    /// </summary>
    public class DropletOutcomeData : PositionOutcomeData
    {
        public DropletPayloadBuilder CreatePayloadBuilder()
        {
            var builder = new DropletPayloadBuilder();
            
            // Add standardized data
            if (!string.IsNullOrEmpty(this.ExitReason))
                builder.AddExitReason(this.ExitReason);
                
            builder.AddExitPrice(this.ExitPrice)
                   .AddPnL(this.PnLDollars, this.PnLPoints)
                   .AddHoldingBars(this.HoldingBars)
                   .AddTimestamps(this.EntryTime, this.ExitTime);
                   
            if (this.profitByBar != null && this.profitByBar.Count > 0)
                builder.AddProfitTrajectory(this.profitByBar);
                
            // Add custom data
            builder.AddMaxProfitLoss(this.MaxFavorableExcursion, this.MaxAdverseExcursion)
                   .AddQuantity(this.Quantity)
                   .AddDailyMetrics(this.CumulativeProfit, this.TradeNumber);
            
            return builder;
        }
    }
}