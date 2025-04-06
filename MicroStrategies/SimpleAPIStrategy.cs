//This namespace holds strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Windows.Media;
    using NinjaTrader.Cbi;
    using NinjaTrader.Gui;
    using NinjaTrader.Data;
    using NinjaTrader.NinjaScript;
    using NinjaTrader.Core.FloatingPoint;
    using System.Threading.Tasks;

    public class SimpleCurvesStrategy : Strategy
    {
        // Reference to CurvesV2Service - the only dependency we need
        private NinjaTrader.NinjaScript.Strategies.CurvesV2Service curvesService;
        
        // Simple tracking variables
        private DateTime lastSignalTime = DateTime.MinValue;
        private double signalThresholdBull = 75;
        private double signalThresholdBear = 75;
        private double ratioThreshold = 2.0;
        
        [NinjaScriptProperty]
        [Display(Name="Stop Loss Dollars", Order=1, GroupName="Trade Parameters")]
        public int StopLossCurrency { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Take Profit Dollars", Order=2, GroupName="Trade Parameters")]
        public int TakeProfitCurrency { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Use Trailing Stop", Order=3, GroupName="Trade Parameters")]
        public bool UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Trailing Stop Dollars", Order=4, GroupName="Trade Parameters")]
        public int TrailingStopCurrency { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Bull Signal Threshold", Order=5, GroupName="Signal Parameters")]
        public double BullSignalThreshold { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Bear Signal Threshold", Order=6, GroupName="Signal Parameters")]
        public double BearSignalThreshold { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Signal Ratio Threshold", Order=7, GroupName="Signal Parameters")]
        public double SignalRatioThreshold { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Signal Spacing Minutes", Order=8, GroupName="Signal Parameters")]
        public int SignalSpacingMinutes { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="API Endpoint", Order=9, GroupName="API Configuration")]
        public string ApiEndpoint { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="WebSocket Endpoint", Order=10, GroupName="API Configuration")]
        public string WebSocketEndpoint { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Maximum Positions", Order=5, GroupName="Trade Parameters")]
        public int MaxPositions { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Position Spacing Ticks", Order=6, GroupName="Trade Parameters")]
        public int PositionSpacingTicks { get; set; }
        
        private List<DateTime> entryTimes = new List<DateTime>();
        private List<double> entryPrices = new List<double>();
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Simple strategy that uses CurvesAPI for signals";
                Name = "SimpleCurvesStrategy";
                
                // Default parameter values
                StopLossCurrency = 20;
                TakeProfitCurrency = 60;
                UseTrailingStop = false;
                TrailingStopCurrency = 15;
                BullSignalThreshold = 75;
                BearSignalThreshold = 75;
                SignalRatioThreshold = 2.0;
                SignalSpacingMinutes = 5;
                
                // Default API endpoints - replace with your actual endpoints
                ApiEndpoint = "https://api.curvesv2.com";
                WebSocketEndpoint = "wss://ws.curvesv2.com";
                
                // Standard strategy settings
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                MaxPositions = 4;
                PositionSpacingTicks = 10;
            }
            else if (State == State.Configure)
            {
				ClearOutputWindow();
				
				
                // Apply configuration settings
                signalThresholdBull = BullSignalThreshold;
                signalThresholdBear = BearSignalThreshold;
                ratioThreshold = SignalRatioThreshold;
				
				 if (!IsInStrategyAnalyzer)
	            {
	                AddDataSeries(BarsPeriodType.Tick, 150);
	            }
	            
	            if (IsInStrategyAnalyzer)
	            {
	                AddDataSeries(Instrument.FullName, BarsPeriodType.Second, 30);
	            }
            }
            else if (State == State.DataLoaded)
            {
                // Initialize CurvesV2Service with minimal configuration
                try
                {
                    Print("Initializing CurvesV2Service...");
                    
                    // Create a new config directly
                    var config = new NinjaTrader.NinjaScript.Strategies.OrganizedStrategy.CurvesV2Config();
                    
                    // Set configuration values from properties
                    // Use reflection to set properties if they exist
                    try
                    {
                        var apiUrlProperty = config.GetType().GetProperty("ApiUrl");
                        if (apiUrlProperty != null)
                            apiUrlProperty.SetValue(config, ApiEndpoint);
                        
                        var wsUrlProperty = config.GetType().GetProperty("WebSocketUrl");
                        if (wsUrlProperty != null)
                            wsUrlProperty.SetValue(config, WebSocketEndpoint);
                        
                        var syncModeProperty = config.GetType().GetProperty("EnableSyncMode");
                        if (syncModeProperty != null)
                            syncModeProperty.SetValue(config, true);
                        
                        // Add other config properties as needed
                    }
                    catch (Exception ex)
                    {
                        Print($"Warning: Could not set config properties: {ex.Message}");
                    }
                    
                    curvesService = new CurvesV2Service(config, msg => Print(msg));
                    
                    // Generate a unique session ID for this strategy instance
                    curvesService.sessionID = Guid.NewGuid().ToString();
                    
                    Print("CurvesV2Service initialized.");
                    
                    // Connect in sync mode for simplicity
                    Task.Run(async () => {
                        try {
                            bool connected = await curvesService.ConnectWebSocketAsync();
                            Print($"CurvesV2Service WebSocket connected: {connected}");
                        }
                        catch (Exception ex) {
                            Print($"Error connecting to CurvesV2Service: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Print($"Error initializing CurvesV2Service: {ex.Message}");
                    curvesService = null;
                }
            }
            else if (State == State.Terminated)
            {
                // Clean up
                if (curvesService != null)
                {
                    Print("Disposing CurvesV2Service...");
                    curvesService.Dispose();
                    curvesService = null;
                    
                    // Reset static data
                    CurvesV2Service.ResetStaticData();
                    Print("CurvesV2Service disposed.");
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar < BarsRequiredToTrade) return;
            
            // Skip if CurvesService not available
            if (curvesService == null || !curvesService.IsConnected)
            {
                if (CurrentBar % 10 == 0) // Only log occasionally
                    Print("OnBarUpdate: CurvesV2Service not available or not connected");
                return;
            }
            
			if (CurrentBar % 10 == 0) // Only log occasionally
            {
				Print($"Time:{Time[0]}");
            }
			// Send bar data to CurvesAPI - simplified "fire and forget" approach
            try
            {
                // Extract instrument code (just the symbol part)
                string instrumentCode = Instrument?.FullName?.Split(' ')?.FirstOrDefault() ?? "";
                
                // Send bar data
                bool barSent = curvesService.SendBarFireAndForget(
                    instrumentCode,
                    Time[0],
                    Open[0], 
                    High[0],
                    Low[0],
                    Close[0],
                    Volume[0],
                    IsInStrategyAnalyzer ? "backtest" : "1m"
                );
                
                // Check for signals
                if (barSent)
                {
                    curvesService.CheckSignalsFireAndForget(Time[0].ToString(), instrumentCode);
                    
                    // Process signals based on static properties updated by the service
                    ProcessSignals();
                }
            }
            catch (Exception ex)
            {
                Print($"Error in OnBarUpdate: {ex.Message}");
            }
        }
        
        private void ProcessSignals()
        {
            double currentBullStrength = CurvesV2Service.CurrentBullStrength;
            double currentBearStrength = CurvesV2Service.CurrentBearStrength;
            bool signalsFresh = CurvesV2Service.SignalsAreFresh;
            
            // Apply the same logic as in BuildNewSignal
            TimeSpan timeSinceLastSignal = Time[0] - lastSignalTime;
            
            // Check if we can add more positions
            int currentPositionCount = Position.Quantity > 0 ? 
                Position.Quantity : (Position.Quantity < 0 ? Math.Abs(Position.Quantity) : 0);
            
            // Check for signal spacing (throttle)
            if (timeSinceLastSignal.TotalMinutes < SignalSpacingMinutes)
                return;
                
            // Clear old entry records (closed positions)
            CleanupEntryRecords();
            
            // Only consider additional entries if we have less than max positions
            if (currentPositionCount < MaxPositions && signalsFresh)
            {
                // Generate a unique signal name using timestamp
                string signalName = $"Signal_{DateTime.Now.Ticks}";
                
                // Check for Long signal
                if (currentBullStrength > currentBearStrength * ratioThreshold && 
                    currentBullStrength >= signalThresholdBull)
                {
                    // If we have a position, check direction compatibility
                    if (currentPositionCount > 0)
                    {
                        // Only add to long positions
                        if (Position.MarketPosition != MarketPosition.Long)
                            return;
                            
                        // Check price spacing with existing positions
                        if (!IsPriceSpacingOK(Close[0], true))
                            return;
                    }
                    
                    Print($"LONG SIGNAL: Bull={currentBullStrength}, Bear={currentBearStrength}, Count={currentPositionCount+1}");
                    EnterLong(1, signalName);
                    
                    // Apply stop loss based on trailing setting
                    if (UseTrailingStop)
                        SetStopLoss(signalName, CalculationMode.Ticks, TrailingStopCurrency, true);
                    else
                        SetStopLoss(signalName, CalculationMode.Ticks, StopLossCurrency, false);
                        
                    SetProfitTarget(signalName, CalculationMode.Ticks, TakeProfitCurrency);
                    
                    // Track this entry
                    entryTimes.Add(Time[0]);
                    entryPrices.Add(Close[0]);
                    
                    lastSignalTime = Time[0];
                }
                // Check for Short signal
                else if (currentBearStrength > currentBullStrength * ratioThreshold && 
                         currentBearStrength >= signalThresholdBear)
                {
                    // If we have a position, check direction compatibility
                    if (currentPositionCount > 0)
                    {
                        // Only add to short positions
                        if (Position.MarketPosition != MarketPosition.Short)
                            return;
                            
                        // Check price spacing with existing positions
                        if (!IsPriceSpacingOK(Close[0], false))
                            return;
                    }
                    
                    Print($"SHORT SIGNAL: Bull={currentBullStrength}, Bear={currentBearStrength}, Count={currentPositionCount+1}");
                    EnterShort(1, signalName);
                    
                    // Apply stop loss based on trailing setting
                    if (UseTrailingStop)
                        SetStopLoss(signalName, CalculationMode.Ticks, TrailingStopCurrency, true);
                    else
                        SetStopLoss(signalName, CalculationMode.Ticks, StopLossCurrency, false);
                        
                    SetProfitTarget(signalName, CalculationMode.Ticks, TakeProfitCurrency);
                    
                    // Track this entry
                    entryTimes.Add(Time[0]);
                    entryPrices.Add(Close[0]);
                    
                    lastSignalTime = Time[0];
                }
            }
            
            // Output signal strength periodically 
            if (CurrentBar % 20 == 0)
            {
                Print($"Signal Strength: Bull={currentBullStrength}, Bear={currentBearStrength}, Fresh={signalsFresh}, Positions={currentPositionCount}");
            }
        }

        private bool IsPriceSpacingOK(double currentPrice, bool isLong)
        {
            // Make sure we have proper spacing from existing positions
            foreach (double price in entryPrices)
            {
                double priceDiffInTicks = Math.Abs(currentPrice - price) / TickSize;
                
                // For long positions, we want to be higher than existing entries
                if (isLong && currentPrice < price)
                    return false;
                    
                // For short positions, we want to be lower than existing entries
                if (!isLong && currentPrice > price)
                    return false;
                    
                // Check minimum spacing
                if (priceDiffInTicks < PositionSpacingTicks)
                    return false;
            }
            
            return true;
        }

        private void CleanupEntryRecords()
        {
            // If we have no position, clear all records
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                entryTimes.Clear();
                entryPrices.Clear();
                return;
            }
            
            // If records size doesn't match position size, reset
            if (entryPrices.Count != Math.Abs(Position.Quantity))
            {
                entryTimes.Clear();
                entryPrices.Clear();
                
                // If we have a position, add the current average price
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    for (int i = 0; i < Math.Abs(Position.Quantity); i++)
                    {
                        entryTimes.Add(Time[0]);
                        entryPrices.Add(Position.AveragePrice);
                    }
                }
            }
        }
    }
}