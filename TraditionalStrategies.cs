/*
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	/// <summary>
	/// LEGACY CODE - NO LONGER IN USE
	/// This file has been replaced by ImprovedTraditionalStrategies.cs
	/// Traditional strategy library for meta-labeling approach
	/// These strategies provide the baseline "edge" that RF models will enhance
	/// 
	/// MIGRATION NOTE: MainStrategy and CurvesStrategy now use ImprovedTraditionalStrategies
	/// which provides attribute-based automatic registration and Z-Score enhanced strategies
	/// </summary>
	/// 
		
	public class TraditionalStrategies
	{
		// Random number generator for threshold variations
		private readonly Random random = new Random();
		
		/// <summary>
		/// Apply randomization to a threshold value
		/// </summary>
		private double Randomize(double value, double variationPercent = 0.1)
		{
			// Apply +/- variationPercent randomization
			double variation = value * variationPercent;
			return value + (random.NextDouble() * 2 - 1) * variation;
		}
		
		/// <summary>
		/// Detects huge bars that indicate trend exhaustion - avoid trading near these
		/// </summary>
		private bool IsHugeBarEnvironment(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 20) return false;
				
				var atr20 = strategy.ATR(20)[0];
				var currentRange = strategy.High[0] - strategy.Low[0];
				var prevRange = strategy.High[1] - strategy.Low[1];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 20)[0];
				
				// Huge bar criteria - randomized thresholds for variation
				bool hugeCurrentBar = currentRange > atr20 * Randomize(2.5, 0.2); // 2.5x ATR threshold
				bool hugePrevBar = prevRange > atr20 * Randomize(2.5, 0.2);
				bool highVolume = volume > volumeMA * Randomize(1.8, 0.15); // 1.8x volume threshold
				
				// Check for recent huge bars (within last 3 bars)
				bool recentHugeBars = false;
				for (int i = 0; i <= Math.Min(3, strategy.CurrentBar); i++)
				{
					double barRange = strategy.High[i] - strategy.Low[i];
					if (barRange > atr20 * Randomize(2.2, 0.15))
					{
						recentHugeBars = true;
						break;
					}
				}
				
				// Trend extension indicators
				var ema20 = strategy.EMA(20)[0];
				var ema50 = strategy.EMA(50)[0];
				var close = strategy.Close[0];
				var high = strategy.High[0];
				var low = strategy.Low[0];
				bool extendedMove = false;
				
				// Detect extended moves away from key EMAs
				double emaDistance20 = Math.Abs(close - ema20) / atr20;
				double emaDistance50 = Math.Abs(close - ema50) / atr20;
				
				// Extended if price is > 1.5 ATR away from EMA20 or > 2.5 ATR from EMA50
				if (emaDistance20 > Randomize(1.5, 0.2) || emaDistance50 > Randomize(2.5, 0.3))
				{
					extendedMove = true;
				}
				
				// Additional huge bar characteristics: large upper/lower wicks (rejection patterns)
				double upperWick = high - Math.Max(strategy.Open[0], close);
				double lowerWick = Math.Min(strategy.Open[0], close) - low;
				bool rejectionWicks = upperWick > currentRange * 0.4 || lowerWick > currentRange * 0.4;
				
				// Return true if we have huge bars AND (high volume OR extended move OR rejection wicks)
				return (hugeCurrentBar || hugePrevBar || recentHugeBars) && 
				       (highVolume || extendedMove || rejectionWicks);
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in huge bar detection: {ex.Message}");
				return false; // Default to allowing trades if error
			}
		}
		
		
		
		
		/// <summary>
		/// TIME SESSION MOMENTUM - Pure time-based entries (UNCORRELATED #1)
		/// Uses only time/day patterns - NO price indicators for true independence
		/// </summary>
		public patternFunctionResponse CheckTimeSessionMomentum(Strategy strategy)
		{
			try
			{
				var currentTime = strategy.Time[0].TimeOfDay;
				var dayOfWeek = strategy.Time[0].DayOfWeek;
				
				// US Market Open Momentum (9:30-9:45 AM EST)
				if (currentTime >= TimeSpan.FromHours(9.5) && currentTime <= TimeSpan.FromHours(9.75))
				{
					// Long on Mondays/Tuesdays (fresh week momentum)
					if (dayOfWeek == DayOfWeek.Monday || dayOfWeek == DayOfWeek.Tuesday)
					{
						return CreateSignal(strategy, "long", "TIME_SESSION_MOMENTUM", "Market open Monday/Tuesday momentum", 0.7, 0.7);
					}
					
					// Short on Fridays (week-end profit taking)
					if (dayOfWeek == DayOfWeek.Friday)
					{
						return CreateSignal(strategy, "short", "TIME_SESSION_MOMENTUM", "Friday profit taking", 0.7, 0.7);
					}
				}
				
				// London Close reversion (4:00 PM GMT = 11:00 AM EST)
				if (currentTime >= TimeSpan.FromHours(11) && currentTime <= TimeSpan.FromHours(11.25))
				{
					// Reverse morning moves (if significant)
					var morningMove = strategy.Close[0] - strategy.Open[0];
					if (Math.Abs(morningMove) > strategy.TickSize * Randomize(10, 0.2))
					{
						if (morningMove > 0)
							return CreateSignal(strategy, "short", "TIME_SESSION_MOMENTUM", "London close reversion", 0.6, 0.6);
						else
							return CreateSignal(strategy, "long", "TIME_SESSION_MOMENTUM", "London close reversion", 0.6, 0.6);
					}
				}
				
				// Asian session calm period reversal (2:00-4:00 AM EST)
				if (currentTime >= TimeSpan.FromHours(2) && currentTime <= TimeSpan.FromHours(4))
				{
					// Trade against overnight moves during low volume
					var overnightMove = strategy.Close[0] - strategy.Close[480]; // ~8 hours ago assuming 1-min bars
					if (Math.Abs(overnightMove) > strategy.TickSize * Randomize(15, 0.3))
					{
						if (overnightMove > 0)
							return CreateSignal(strategy, "short", "TIME_SESSION_MOMENTUM", "Asian session reversion", 0.5, 0.5);
						else
							return CreateSignal(strategy, "long", "TIME_SESSION_MOMENTUM", "Asian session reversion", 0.5, 0.5);
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Time Session Momentum: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// STATISTICAL Z-SCORE - Pure mathematical deviation analysis (UNCORRELATED #2)
		/// Uses only statistical Z-scores - NO volume/levels for true independence
		/// </summary>
		public patternFunctionResponse CheckStatisticalZScore(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 50) return null;
				
				// Calculate 20-day price Z-score (standardized deviation)
				var closes = new double[20];
				for (int i = 0; i < 20; i++) 
					closes[i] = strategy.Close[i];
				
				var mean = closes.Average();
				var variance = closes.Select(x => Math.Pow(x - mean, 2)).Average();
				var stdDev = Math.Sqrt(variance);
				
				if (stdDev == 0) return null; // Avoid division by zero
				
				var currentZScore = (strategy.Close[0] - mean) / stdDev;
				
				// Only trade extreme statistical deviations (2.5+ sigma)
				if (Math.Abs(currentZScore) > Randomize(2.5, 0.1))
				{
					if (currentZScore > Randomize(2.5, 0.1)) // Extremely high
					{
						return CreateSignal(strategy, "short", "STATISTICAL_ZSCORE", 
							$"Statistical overextension: {currentZScore:F2} sigma", 0.8, 0.8);
					}
					
					if (currentZScore < -Randomize(2.5, 0.1)) // Extremely low  
					{
						return CreateSignal(strategy, "long", "STATISTICAL_ZSCORE", 
							$"Statistical underextension: {currentZScore:F2} sigma", 0.8, 0.8);
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Statistical Z-Score: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// PRICE VELOCITY ACCELERATION - Pure physics concepts (UNCORRELATED #3)
		/// Uses only velocity/acceleration/jerk - NO volume/ATR for true independence
		/// </summary>
		public patternFunctionResponse CheckPriceVelocityAcceleration(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 10) return null;
				
				// Calculate price velocity (rate of change)
				var velocity1 = strategy.Close[0] - strategy.Close[3]; // 3-bar velocity
				var velocity2 = strategy.Close[3] - strategy.Close[6]; // Previous 3-bar velocity  
				var velocity3 = strategy.Close[6] - strategy.Close[9]; // Earlier 3-bar velocity
				
				// Calculate acceleration (change in velocity)
				var acceleration = velocity1 - velocity2;
				var prevAcceleration = velocity2 - velocity3;
				
				// Look for acceleration reversals (jerk)
				var jerk = acceleration - prevAcceleration;
				var tickSize = strategy.TickSize;
				
				// Trade acceleration reversals (momentum exhaustion)
				var jerkThreshold = tickSize * Randomize(15, 0.3);
				if (Math.Abs(jerk) > jerkThreshold)
				{
					// Positive jerk after negative acceleration = upward turn
					if (jerk > jerkThreshold && acceleration < 0)
					{
						return CreateSignal(strategy, "long", "PRICE_VELOCITY_ACCELERATION", 
							"Upward acceleration reversal", 0.9, 0.93);
					}
					
					// Negative jerk after positive acceleration = downward turn
					if (jerk < -jerkThreshold && acceleration > 0)
					{
						return CreateSignal(strategy, "short", "PRICE_VELOCITY_ACCELERATION", 
							"Downward acceleration reversal", 0.9, 0.93);
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Price Velocity Acceleration: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// INTRADAY MEAN REVERSION - Rolling range behavior (UNCORRELATED #4)
		/// Uses rolling 6-hour window for 24/7 markets - NO session dependency
		/// </summary>
		public patternFunctionResponse CheckIntradayMeanReversion(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 360) return null; // Need 6 hours of data (360 1-min bars)
				
				// For 24/7 markets, use rolling 6-hour window as "session"
				var currentPrice = strategy.Close[0];
				var sixHoursAgo = strategy.Close[360]; // 6 hours back
				var rollingRange = currentPrice - sixHoursAgo;
				
				// Calculate average 6-hour range over last 10 periods (60 hours)
				double totalRange = 0;
				int periods = 10;
				for (int i = 0; i < periods; i++)
				{
					int startBar = 360 + (i * 360); // Each period is 6 hours
					int endBar = i * 360;
					
					if (strategy.CurrentBar < startBar) 
					{
						periods = i; // Adjust periods if not enough data
						break;
					}
					
					var periodStart = strategy.Close[startBar];
					var periodEnd = strategy.Close[endBar];
					totalRange += Math.Abs(periodEnd - periodStart);
				}
				
				if (periods == 0) return null; // Not enough data
				
				var avgSixHourRange = totalRange / periods;
				if (avgSixHourRange == 0) return null; // Avoid division by zero
				
				// Calculate position within rolling range
				var rangePosition = Math.Abs(rollingRange) / avgSixHourRange;
				
				// Trade when we've moved >120% of average 6-hour range
				// Higher threshold because 6-hour ranges are smaller than daily ranges
				if (rangePosition > Randomize(1.2, 0.15))
				{
					// Mean revert back toward 6-hour anchor
					if (rollingRange > 0) // Above 6-hour anchor
					{
						return CreateSignal(strategy, "short", "INTRADAY_MEAN_REVERSION", 
							$"Extended {rangePosition:P0} above 6h anchor", 0.7, 0.7);
					}
					else // Below 6-hour anchor
					{
						return CreateSignal(strategy, "long", "INTRADAY_MEAN_REVERSION", 
							$"Extended {rangePosition:P0} below 6h anchor", 0.7, 0.7);
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Intraday Mean Reversion: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Helper method to get rolling session anchor for 24/7 markets
		/// Uses 4-hour rolling window instead of daily session concept
		/// </summary>
		private double Get24HourAnchorPrice(Strategy strategy)
		{
			try
			{
				// For 24/7 markets like MGC, use rolling 4-hour anchor
				// This creates a "session" concept that works across all time zones
				var currentTime = strategy.Time[0].TimeOfDay;
				var lookbackBars = 240; // 4 hours of 1-minute bars
				
				// Ensure we have enough data
				if (strategy.CurrentBar < lookbackBars)
					lookbackBars = strategy.CurrentBar;
					
				// Find the price from 4 hours ago as our anchor
				return strategy.Close[lookbackBars];
			}
			catch (Exception ex)
			{
				// Log the error and use a reasonable fallback
				var fallbackBars = Math.Min(120, strategy.CurrentBar); // 2 hours fallback
				return strategy.Close[fallbackBars];
			}
		}
		
		/// <summary>
		/// Momentum Breakout - Simple directional momentum detector
		/// Detects strong directional moves with volume confirmation
		/// </summary>
		public patternFunctionResponse CheckMomentumBreakout(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 10) return null;
				
				var close = strategy.Close[0];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 10)[0];
				var atr = strategy.ATR(14)[0];
				
				// Calculate momentum (price change over last 3 bars)
				double momentum = close - strategy.Close[3];
				
				// Avoid momentum trades near huge bars (most likely to be trend exhaustion)
				if (IsHugeBarEnvironment(strategy))
				{
					return null; // Skip momentum signals near huge bars - highest risk for trend reversals
				}
				
				// Long: Strong upward momentum with volume (randomized thresholds)
				if (momentum > atr * Randomize(0.5, 0.1) && 
					volume > volumeMA * Randomize(1.5, 0.2))
				{
					return CreateSignal(strategy, "long", "MOMENTUM_BREAKOUT", "Upward momentum", 0.9, 0.93); // 90% SL, 93% TP for momentum
				}
				
				// Short: Strong downward momentum with volume (randomized thresholds)
				if (momentum < atr * Randomize(-0.5, 0.1) && 
					volume > volumeMA * Randomize(1.5, 0.2))
				{
					return CreateSignal(strategy, "short", "MOMENTUM_BREAKOUT", "Downward momentum", 0.9, 0.93); // 90% SL, 93% TP for momentum
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Momentum Breakout: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Simple Support/Resistance Bounce - Level rejection detector
		/// Detects bounces off key support/resistance levels
		/// </summary>
		public patternFunctionResponse CheckSRBounce(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 20) return null;
				
				var close = strategy.Close[0];
				var high20 = strategy.MAX(strategy.High, 20)[1];
				var low20 = strategy.MIN(strategy.Low, 20)[1];
				var atr = strategy.ATR(14)[0];
				
				// Long: Bounce off 20-period low (randomized proximity)
				if (strategy.Low[0] <= low20 + (atr * Randomize(0.1, 0.05)) && 
					close > strategy.Low[0] + (atr * Randomize(0.2, 0.05)))
				{
					return CreateSignal(strategy, "long", "SR_BOUNCE", "Support bounce", 0.6, 0.6); // 60% of micro contract values for tight SR bounces
				}
				
				// Short: Rejection at 20-period high (randomized proximity)
				if (strategy.High[0] >= high20 - (atr * Randomize(0.1, 0.05)) && 
					close < strategy.High[0] - (atr * Randomize(0.2, 0.05)))
				{
					return CreateSignal(strategy, "short", "SR_BOUNCE", "Resistance rejection", 0.6, 0.6); // 60% of micro contract values for tight SR bounces
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in SR Bounce: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Pullback from Recent Extremes - 3-bar decline from recent high/low
		/// Detects pullbacks from recent highs (bear) and bounces from recent lows (bull)
		/// </summary>
		public patternFunctionResponse CheckPullbackFromExtremes(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 50) return null;
				
				// Find 50-bar high and low (excluding current bar)
				var high50 = strategy.MAX(strategy.High, 50)[1];
				var low50 = strategy.MIN(strategy.Low, 50)[1];
				var atr = strategy.ATR(14)[0];
				
				// Find when the 50-bar high occurred (look back 10 bars for deadzone)
				int highBarIndex = -1;
				for (int i = 1; i <= Math.Min(50, strategy.CurrentBar); i++)
				{
					if (Math.Abs(strategy.High[i] - high50) < strategy.TickSize * 0.5)
					{
						highBarIndex = i;
						break;
					}
				}
				
				// Find when the 50-bar low occurred (look back 10 bars for deadzone)
				int lowBarIndex = -1;
				for (int i = 1; i <= Math.Min(50, strategy.CurrentBar); i++)
				{
					if (Math.Abs(strategy.Low[i] - low50) < strategy.TickSize * 0.5)
					{
						lowBarIndex = i;
						break;
					}
				}
				
				// Short signal: 3 declining bars from recent high (within deadzone)
				if (highBarIndex > 0 && highBarIndex <= Randomize(10, 2)) // Deadzone: within ~10 bars of high
				{
					// Check for 3 consecutive declining closes
					bool threeDeclines = strategy.Close[0] < strategy.Close[1] &&
					                    strategy.Close[1] < strategy.Close[2] &&
					                    strategy.Close[2] < strategy.Close[3];
					
					// Ensure we're still close to the recent high
					var distanceFromHigh = high50 - strategy.Close[0];
					var maxDistance = atr * Randomize(2.0, 0.3); // Allow some distance from high
					
					if (threeDeclines && distanceFromHigh > 0 && distanceFromHigh <= maxDistance)
					{
						return CreateSignal(strategy, "short", "PULLBACK_HIGH", $"3-bar decline from recent high ({highBarIndex} bars ago)", 0.5, 0.53); // Very tight 50% SL, 53% TP for pullbacks
					}
				}
				
				// Long signal: 3 ascending bars from recent low (within deadzone)
				if (lowBarIndex > 0 && lowBarIndex <= Randomize(10, 2)) // Deadzone: within ~10 bars of low
				{
					// Check for 3 consecutive ascending closes
					bool threeAscends = strategy.Close[0] > strategy.Close[1] &&
					                   strategy.Close[1] > strategy.Close[2] &&
					                   strategy.Close[2] > strategy.Close[3];
					
					// Ensure we're still close to the recent low
					var distanceFromLow = strategy.Close[0] - low50;
					var maxDistance = atr * Randomize(2.0, 0.3); // Allow some distance from low
					
					if (threeAscends && distanceFromLow > 0 && distanceFromLow <= maxDistance)
					{
						return CreateSignal(strategy, "long", "PULLBACK_LOW", $"3-bar rise from recent low ({lowBarIndex} bars ago)", 0.5, 0.53); // Very tight 50% SL, 53% TP for pullbacks
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Pullback from Extremes: {ex.Message}");
			}
			
			return null;
		}
		
		public patternFunctionResponse CheckEMAVWAPCross(Strategy strategy)
		{
			try
			{
				// Ensure we have enough bars
				if (strategy.CurrentBar < 20) return null;
				
				// Get standard Strategy indicators (using default periods from CurvesStrategy)
				var ema3 = strategy.EMA(10);
				var vwap = strategy.custom_VWAP(25);
				var atr = strategy.ATR(14)[0];
				var close = strategy.Close[0];
				var tickSize = strategy.TickSize;
				
				// Implement CrossAbove logic: EMA crossed above VWAP in last 10 bars
				bool crossedAbove = false;
				for (int i = 1; i <= Math.Min(10, strategy.CurrentBar); i++)
				{
					if (ema3[i] <= vwap[i] && ema3[0] > vwap[0])
					{
						crossedAbove = true;
						break;
					}
				}
				
				// Implement CrossBelow logic: EMA crossed below VWAP in last 10 bars  
				bool crossedBelow = false;
				for (int i = 1; i <= Math.Min(10, strategy.CurrentBar); i++)
				{
					if (ema3[i] >= vwap[i] && ema3[0] < vwap[0])
					{
						crossedBelow = true;
						break;
					}
				}
				
				// Implement IsRising logic: EMA is rising (current > 2 bars ago)
				bool isRising = strategy.CurrentBar >= 2 && ema3[0] > ema3[2];
				
				// Implement IsFalling logic: EMA is falling (current < 2 bars ago)
				bool isFalling = strategy.CurrentBar >= 2 && ema3[0] < ema3[2];
				
				// Long signal: CrossAbove(EMA3,VWAP1,10) && EMA3[0] - VWAP1[0] > TickSize*3 && IsRising(EMA3)
				if (crossedAbove && (ema3[0] - vwap[0]) > (tickSize * Randomize(3, 0.3)) && isRising)
				{
					// COMMENTED OUT: SignalFeatures Risk Agent Integration
					// // Generate entry signal ID and features
					// string entrySignalId = $"CheckEMAVWAPCross_{strategy.Time[0]:yyyyMMdd_HHmmss}";
					// var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
					// 
					// // Queue features and get Risk Agent approval with micro contract values
					// var mainStrategy = (MainStrategy)strategy;
					// double maxStopLoss = mainStrategy.microContractStoploss * 0.8; // 80% of micro contract SL
					// double maxTakeProfit = mainStrategy.microContractTakeProfit * 0.87; // 87% of micro contract TP
					// bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
					//	strategy.Instrument.FullName, "long", "CheckEMAVWAPCross", 1, maxStopLoss, maxTakeProfit).Result;
					// 
					// if (approved && features != null)
					
					// Simplified: Use direct strategy parameters without Risk Agent
					var mainStrategy = (MainStrategy)strategy;
					if (true) // Always approve for now
					{
						// COMMENTED OUT: Risk Agent pending features
						// var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
						
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterLong,
							signalType = "CheckEMAVWAPCross",
							signalDefinition = "crossedAbove && (ema3[0] - vwap[0]) > (tickSize * Randomize(3, 0.3)) && isRising",
							recStop = mainStrategy.microContractStoploss * 0.8,  // Use direct strategy values
							recTarget = mainStrategy.microContractTakeProfit * 0.87,  // Use direct strategy values
							signalScore = 0.65,  // Default confidence
							recQty = 1,
							patternId = $"CheckEMAVWAPCross_{strategy.Time[0]:yyyyMMdd_HHmmss}",
							patternSubType = "BULLISH_CheckEMAVWAPCross"
							// COMMENTED OUT: signalFeatures = features
						};
					}
				}
				
				// Short signal: CrossBelow(EMA3,VWAP1,10) && EMA3[0] - VWAP1[0] > TickSize*3 && IsFalling(EMA3)
				// Note: The original condition seems wrong for short (should be VWAP - EMA3 > threshold), but keeping as-is for compatibility
				if (crossedBelow && (ema3[0] - vwap[0]) > (tickSize * Randomize(3, 0.3)) && isFalling)
				{
					// COMMENTED OUT: SignalFeatures Risk Agent Integration
					// // Generate entry signal ID and features
					// string entrySignalId = $"CheckEMAVWAPCross_{strategy.Time[0]:yyyyMMdd_HHmmss}";
					// var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
					// 
					// // Queue features and get Risk Agent approval with micro contract values
					// var mainStrategy = (MainStrategy)strategy;
					// double maxStopLoss = mainStrategy.microContractStoploss * 0.8; // 80% of micro contract SL
					// double maxTakeProfit = mainStrategy.microContractTakeProfit * 0.87; // 87% of micro contract TP
					// bool approved = mainStrategy.QueueAndApprove(entrySignalId, features, 
					//	strategy.Instrument.FullName, "short", "CheckEMAVWAPCross", 1, maxStopLoss, maxTakeProfit).Result;
					// 
					// if (approved && features != null)
					
					// Simplified: Use direct strategy parameters without Risk Agent
					var mainStrategy = (MainStrategy)strategy;
					if (true) // Always approve for now
					{
						// COMMENTED OUT: Risk Agent pending features
						// var pending = ((MainStrategy)strategy).GetPendingFeatures(entrySignalId);
						
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterShort,
							signalType = "CheckEMAVWAPCross",
							signalDefinition = "crossedBelow && (ema3[0] - vwap[0]) > (tickSize * Randomize(3, 0.3)) && isFalling",
							recStop = mainStrategy.microContractStoploss * 0.8,  // Use direct strategy values
							recTarget = mainStrategy.microContractTakeProfit * 0.87,  // Use direct strategy values
							signalScore = 0.65,  // Default confidence
							recQty = 1,
							patternId = $"CheckEMAVWAPCross_{strategy.Time[0]:yyyyMMdd_HHmmss}",
							patternSubType = "BEARISH_CheckEMAVWAPCross"
							// COMMENTED OUT: signalFeatures = features
						};
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in EMA VWAP Cross: {ex.Message}");
			}
			
			return null;
		}

		
		/// <summary>
		/// Helper method to create standardized signals WITHOUT Risk Agent approval
		/// Risk Agent approval happens only AFTER consensus logic in the main dispatcher
		/// </summary>
		private patternFunctionResponse CreateSignal(Strategy strategy, string direction, string signalType, string reason, double maxStopLossMultiplier = 1.0, double maxTakeProfitMultiplier = 1.0)
		{
			try
			{
				string entrySignalId = $"{signalType}_{strategy.Time[0]:yyyyMMdd_HHmmss}";
				// COMMENTED OUT: SignalFeatures generation
				// var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
				
				// Get micro contract values from MainStrategy and apply multipliers
				var mainStrategy = (MainStrategy)strategy;
				double maxStopLoss = mainStrategy.microContractStoploss * maxStopLossMultiplier;
				double maxTakeProfit = mainStrategy.microContractTakeProfit * maxTakeProfitMultiplier;
				
				// NO Risk Agent approval here - just create the candidate signal
				// Approval happens AFTER consensus in CheckAllTraditionalStrategies
				// COMMENTED OUT: if (features != null)
				if (true) // Always create signal now
				{
					return new patternFunctionResponse
					{
						newSignal = direction == "long" ? FunctionResponses.EnterLong : FunctionResponses.EnterShort,
						signalType = signalType,
						signalDefinition = reason,
						recStop = maxStopLoss, // Use max values - Risk Agent will adjust later
						recTarget = maxTakeProfit,
						recPullback = 10,
						signalScore = 0.65, // Default confidence - Risk Agent will provide actual confidence
						recQty = 1,
						patternId = entrySignalId,
						// COMMENTED OUT: signalFeatures = features,
						// Store max values for Risk Agent approval later
						maxStopLoss = maxStopLoss,
						maxTakeProfit = maxTakeProfit
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error creating signal: {ex.Message}");
			}
			return null;
		}
		
		/// <summary>
		/// Convert signal type to numeric for ML training
		/// </summary>
		private double GetSignalTypeNumeric(string signalType)
		{
			switch (signalType)
			{
				case "ORDER_FLOW_IMBALANCE": return 1.0;
				case "EMA_CROSS": return 2.0;
				case "RSI_DIVERGENCE": return 3.0;
				case "BREAKOUT": return 4.0;
				case "VWAP_REVERSION": return 5.0;
				case "BB_SQUEEZE": return 6.0;
				default: return 0.0;
			}
		}
		
		/// <summary>
		/// Main strategy dispatcher - checks all traditional strategies and picks highest confidence
		/// This is the method called from BuildNewSignal()
		/// </summary>
		public patternFunctionResponse CheckAllTraditionalStrategies(Strategy strategy, TraditionalStrategyType strategyFilter, double riskAgentConfThreshold = 0.5)
		{
			try
			{
				// Single strategy testing for pure training data
				if (strategyFilter != TraditionalStrategyType.ALL)
				{
					patternFunctionResponse singleSignal = null;
					
					switch (strategyFilter)
					{
						case TraditionalStrategyType.TIME_SESSION_MOMENTUM:
							singleSignal = CheckTimeSessionMomentum(strategy);
							break;
						case TraditionalStrategyType.STATISTICAL_ZSCORE:
							singleSignal = CheckStatisticalZScore(strategy);
							break;
						case TraditionalStrategyType.PRICE_VELOCITY_ACCELERATION:
							singleSignal = CheckPriceVelocityAcceleration(strategy);
							break;
						case TraditionalStrategyType.INTRADAY_MEAN_REVERSION:
							singleSignal = CheckIntradayMeanReversion(strategy);
							break;
						case TraditionalStrategyType.TICK_SEQUENCE_PATTERN:
							singleSignal = CheckTickSequencePattern(strategy);
							break;
						case TraditionalStrategyType.PRICE_DISTRIBUTION_SKEW:
							singleSignal = CheckPriceDistributionSkew(strategy);
							break;
						case TraditionalStrategyType.FOURIER_CYCLE_ANALYSIS:
							singleSignal = CheckFourierCycleAnalysis(strategy);
							break;
						case TraditionalStrategyType.ENTROPY_COMPLEXITY_MEASURE:
							singleSignal = CheckEntropyComplexityMeasure(strategy);
							break;
						case TraditionalStrategyType.LOW_VOLUME_SCALPING:
							singleSignal = CheckLowVolumeScalping(strategy);
							break;
						case TraditionalStrategyType.ZIGZAG_PIVOT_STRATEGY:
							singleSignal = CheckZigZagPivotStrategy(strategy);
							break;
						case TraditionalStrategyType.MGC_PATTERN_FILTER:
							singleSignal = CheckMGCPatternFilter(strategy);
							break;
						default:
							return null;
					}
					
					// Apply Risk Agent approval even for single strategy mode
					if (singleSignal != null)
					{
						try
						{
							var mainStrategy = (MainStrategy)strategy;
							string direction = singleSignal.newSignal == FunctionResponses.EnterLong ? "long" : "short";
							
							// COMMENTED OUT: SignalFeatures Risk Agent Integration
							// bool approved = mainStrategy.QueueAndApprove(singleSignal.patternId, singleSignal.signalFeatures, 
							//	strategy.Instrument.FullName, direction, singleSignal.signalType, 1, 
							//	singleSignal.maxStopLoss, singleSignal.maxTakeProfit).Result;
							
							bool approved = true; // Always approve for now
							if (approved)
							{
								// COMMENTED OUT: Risk Agent pending features
								// var pending = mainStrategy.GetPendingFeatures(singleSignal.patternId);
								// if (pending != null)
								// {
								//	// Update with Risk Agent values
								//	singleSignal.recStop = pending.StopLoss != 0 ? pending.StopLoss : singleSignal.recStop;
								//	singleSignal.recTarget = pending.TakeProfit != 0 ? pending.TakeProfit : singleSignal.recTarget;
								//	singleSignal.recPullback = pending.RecPullback != 0 ? pending.RecPullback : singleSignal.recPullback;
								//	singleSignal.signalScore = pending.Confidence != 0 ? pending.Confidence : singleSignal.signalScore;
								// }
								
								strategy.Print($"[TRADITIONAL-SINGLE] APPROVED: {singleSignal.signalType} with SL: {singleSignal.recStop}, TP: {singleSignal.recTarget}, Confidence: {singleSignal.signalScore:F3}");
								return singleSignal;
							}
							else
							{
								strategy.Print($"[TRADITIONAL-SINGLE] REJECTED by Risk Agent: {singleSignal.signalType}");
								return null;
							}
						}
						catch (Exception ex)
						{
							strategy.Print($"[TRADITIONAL-SINGLE] Error in Risk Agent approval: {ex.Message}");
							return null;
						}
					}
					
					return null;
				}
				
				// Confidence-based strategy selection - evaluate all strategies and pick highest confidence
				var candidateSignals = new List<patternFunctionResponse>();
				
				// Check all UNCORRELATED strategies for potential signals
				var timeSignal = CheckTimeSessionMomentum(strategy);
				if (timeSignal != null) candidateSignals.Add(timeSignal);
				
				var zscoreSignal = CheckStatisticalZScore(strategy);
				if (zscoreSignal != null) candidateSignals.Add(zscoreSignal);
				
				var velocitySignal = CheckPriceVelocityAcceleration(strategy);
				if (velocitySignal != null) candidateSignals.Add(velocitySignal);
				
				var intradaySignal = CheckIntradayMeanReversion(strategy);
				if (intradaySignal != null) candidateSignals.Add(intradaySignal);
				
				var tickSignal = CheckTickSequencePattern(strategy);
				if (tickSignal != null) candidateSignals.Add(tickSignal);
				
				var distributionSignal = CheckPriceDistributionSkew(strategy);
				if (distributionSignal != null) candidateSignals.Add(distributionSignal);
				
				var fourierSignal = CheckFourierCycleAnalysis(strategy);
				if (fourierSignal != null) candidateSignals.Add(fourierSignal);
				
				var entropySignal = CheckEntropyComplexityMeasure(strategy);
				if (entropySignal != null) candidateSignals.Add(entropySignal);
				
				// Check MGC pattern filter (only for MGC instruments)
				var mgcSignal = CheckMGCPatternFilter(strategy);
				if (mgcSignal != null) candidateSignals.Add(mgcSignal);
				
				// If no signals found, return null
				if (candidateSignals.Count == 0)
					return null;
				
				// BEAR/BULL SIGNAL ALIGNMENT CHECK: Require 75% directional consensus
				int longSignals = candidateSignals.Count(s => s.newSignal == FunctionResponses.EnterLong);
				int shortSignals = candidateSignals.Count(s => s.newSignal == FunctionResponses.EnterShort);
				int totalSignals = candidateSignals.Count;
				
				double longPercentage = (double)longSignals / totalSignals;
				double shortPercentage = (double)shortSignals / totalSignals;
				
				// Require 75% consensus in one direction
				double ALIGNMENT_THRESHOLD = 0.75; // Always require 75% regardless of riskAgentConfThreshold
				bool hasDirectionalConsensus = longPercentage >= ALIGNMENT_THRESHOLD || shortPercentage >= ALIGNMENT_THRESHOLD;
				
				if (!hasDirectionalConsensus || candidateSignals.Count < 3)
				{
					strategy.Print($"[TRADITIONAL] REJECTED - Insufficient directional consensus: {longSignals}L/{shortSignals}S from {totalSignals} signals (need {ALIGNMENT_THRESHOLD:P0})");
					return null;
				}
				
				// Pick the signal with highest confidence from the consensus direction
				var bestSignal = candidateSignals.OrderByDescending(s => s.signalScore).First();
				
				string consensusDirection = longPercentage >= ALIGNMENT_THRESHOLD ? "BULLISH" : "BEARISH";
				strategy.Print($"[TRADITIONAL] Selected {bestSignal.signalType} with confidence {bestSignal.signalScore:F3} from {candidateSignals.Count} candidates - {consensusDirection} consensus ({longSignals}L/{shortSignals}S)");
				
				// NOW apply Risk Agent approval to the final selected signal
				try
				{
					var mainStrategy = (MainStrategy)strategy;
					string direction = bestSignal.newSignal == FunctionResponses.EnterLong ? "long" : "short";
					
					// COMMENTED OUT: SignalFeatures Risk Agent Integration
					// bool approved = mainStrategy.QueueAndApprove(bestSignal.patternId, bestSignal.signalFeatures, 
					//	strategy.Instrument.FullName, direction, bestSignal.signalType, 1, 
					//	bestSignal.maxStopLoss, bestSignal.maxTakeProfit).Result;
					
					bool approved = true; // Always approve for now
					if (approved)
					{
						// COMMENTED OUT: Risk Agent pending features
						// var pending = mainStrategy.GetPendingFeatures(bestSignal.patternId);
						// if (pending != null)
						// {
						//	// Update with Risk Agent values
						//	bestSignal.recStop = pending.StopLoss != 0 ?  pending.StopLoss : bestSignal.recStop;
						//	bestSignal.recTarget = pending.TakeProfit != 0 ? pending.TakeProfit:bestSignal.recTarget;
						//	bestSignal.recPullback = pending.RecPullback  != 0 ?   pending.RecPullback : bestSignal.recPullback;
						//	bestSignal.signalScore = pending.Confidence  != 0 ? pending.Confidence :  bestSignal.signalScore;
						// }
						
						strategy.Print($"[TRADITIONAL] FINAL APPROVAL: {bestSignal.signalType} with SL: {bestSignal.recStop}, TP: {bestSignal.recTarget}, Confidence: {bestSignal.signalScore:F3}");
						return bestSignal;
					}
					else
					{
						strategy.Print($"[TRADITIONAL] REJECTED by Risk Agent: {bestSignal.signalType}");
						return null;
					}
				}
				catch (Exception ex)
				{
					strategy.Print($"[TRADITIONAL] Error in Risk Agent approval: {ex.Message}");
					return null;
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in CheckAllTraditionalStrategies: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// TICK SEQUENCE PATTERN - Pure tick patterns (UNCORRELATED #5)
		/// Uses only tick-by-tick bar patterns - NO wicks/EMA for true independence
		/// </summary>
		public patternFunctionResponse CheckTickSequencePattern(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 5) return null;
				
				// Analyze last 5 bars for tick-by-tick patterns
				var upTicks = 0;
				var downTicks = 0;
				var doji = 0;
				
				for (int i = 0; i < 5; i++)
				{
					var open = strategy.Open[i];
					var close = strategy.Close[i];
					var tickDiff = Math.Abs(close - open) / strategy.TickSize;
					
					if (tickDiff < 2) // Doji (< 2 ticks)
						doji++;
					else if (close > open)
						upTicks++;
					else
						downTicks++;
				}
				
				// Look for specific tick sequences that indicate exhaustion
				if (doji >= 3) // 3+ doji in 5 bars = indecision
				{
					// Break indecision in direction of last bar
					if (strategy.Close[0] > strategy.Open[0] && strategy.IsFalling(strategy.EMA(10)))
						return CreateSignal(strategy, "long", "TICK_SEQUENCE_PATTERN", "Doji sequence break up", 0.6, 0.6);
					else if (strategy.Close[0] < strategy.Open[0] && strategy.IsRising(strategy.EMA(10)))
						return CreateSignal(strategy, "short", "TICK_SEQUENCE_PATTERN", "Doji sequence break down", 0.6, 0.6);
				}
				
				// Overwhelming directional sequence = reversal
				if (upTicks >= 4 && strategy.IsRising(strategy.EMA(10))) // 4+ up bars in 5
					return CreateSignal(strategy, "short", "TICK_SEQUENCE_PATTERN", "Overwhelming up sequence reversal", 0.7, 0.7);
				if (downTicks >= 4 && strategy.IsFalling(strategy.EMA(10))) // 4+ down bars in 5
					return CreateSignal(strategy, "long", "TICK_SEQUENCE_PATTERN", "Overwhelming down sequence reversal", 0.7, 0.7);
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Tick Sequence Pattern: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// PRICE DISTRIBUTION SKEW - Statistical distribution analysis (UNCORRELATED #6)
		/// Uses only statistical distribution - NO gap patterns for true independence
		/// </summary>
		public patternFunctionResponse CheckPriceDistributionSkew(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 50) return null;
				
				// Calculate price distribution over last 20 bars
				var prices = new double[20];
				for (int i = 0; i < 20; i++) prices[i] = strategy.Close[i];
				
				// Calculate distribution statistics
				var mean = prices.Average();
				var variance = prices.Select(x => Math.Pow(x - mean, 2)).Average();
				var stdDev = Math.Sqrt(variance);
				
				if (stdDev == 0) return null;
				
				// Calculate skewness (asymmetry of distribution)
				var skewness = prices.Select(x => Math.Pow((x - mean) / stdDev, 3)).Average();
				
				// Calculate kurtosis (tail heaviness)  
				var kurtosis = prices.Select(x => Math.Pow((x - mean) / stdDev, 4)).Average() - 3;
				
				// Trade distribution extremes
				if (Math.Abs(skewness) > Randomize(1.0, 0.1)) // Highly skewed distribution
				{
					if (skewness > Randomize(1.0, 0.1)) // Right-skewed (more high prices)
						return CreateSignal(strategy, "short", "PRICE_DISTRIBUTION_SKEW", 
							$"Right-skewed distribution: {skewness:F2}", 0.8, 0.8);
					if (skewness < -Randomize(1.0, 0.1)) // Left-skewed (more low prices)
						return CreateSignal(strategy, "long", "PRICE_DISTRIBUTION_SKEW", 
							$"Left-skewed distribution: {skewness:F2}", 0.8, 0.8);
				}
				
				// Heavy tails indicate volatility expansion
				if (kurtosis > Randomize(2.0, 0.2)) // Heavy tails
					return CreateSignal(strategy, "short", "PRICE_DISTRIBUTION_SKEW", 
						$"Heavy tail distribution: {kurtosis:F2}", 0.7, 0.7);
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Price Distribution Skew: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// FOURIER CYCLE ANALYSIS - Frequency analysis (UNCORRELATED #7)
		/// Uses only cycle/frequency detection - NO high/low levels for true independence
		/// </summary>
		public patternFunctionResponse CheckFourierCycleAnalysis(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 64) return null; // Need sufficient data
				
				// Simple cycle analysis without full FFT
				var prices = new double[32];
				for (int i = 0; i < 32; i++) prices[i] = strategy.Close[i];
				
				// Detect dominant cycle periods (simplified)
				var cycles = new Dictionary<int, double>();
				
				for (int period = 4; period <= 16; period++) // Test 4-16 bar cycles
				{
					var correlation = CalculateCycleCorrelation(prices, period);
					cycles[period] = correlation;
				}
				
				// Find strongest cycle
				var dominantCycle = cycles.OrderByDescending(x => Math.Abs(x.Value)).First();
				
				// Trade cycle extremes
				if (Math.Abs(dominantCycle.Value) > Randomize(0.6, 0.1)) // Strong cycle detected
				{
					var cyclePosition = strategy.CurrentBar % dominantCycle.Key;
					var expectedDirection = dominantCycle.Value > 0 ? 1 : -1;
					
					// Trade at cycle peaks/troughs
					if (cyclePosition == 0 || cyclePosition == dominantCycle.Key / 2)
					{
						if (expectedDirection > 0)
							return CreateSignal(strategy, "long", "FOURIER_CYCLE_ANALYSIS", 
								$"{dominantCycle.Key}-bar cycle peak", 0.6, 0.6);
						else
							return CreateSignal(strategy, "short", "FOURIER_CYCLE_ANALYSIS", 
								$"{dominantCycle.Key}-bar cycle trough", 0.6, 0.6);
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Fourier Cycle Analysis: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// ENTROPY COMPLEXITY MEASURE - Information theory (UNCORRELATED #8)
		/// Uses only information theory - NO moving averages for true independence
		/// </summary>
		public patternFunctionResponse CheckEntropyComplexityMeasure(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 30) return null;
				
				// Calculate price change entropy (information theory)
				var changes = new double[20];
				for (int i = 1; i < 21; i++)
					changes[i-1] = strategy.Close[i-1] - strategy.Close[i];
				
				// Discretize changes into bins
				var bins = new int[5]; // -2, -1, 0, 1, 2 (relative to tick size)
				var tickSize = strategy.TickSize;
				
				foreach (var change in changes)
				{
					var binIndex = Math.Max(0, Math.Min(4, (int)((change / tickSize) + 2)));
					bins[binIndex]++;
				}
				
				// Calculate Shannon entropy
				var entropy = 0.0;
				var total = changes.Length;
				
				for (int i = 0; i < bins.Length; i++)
				{
					if (bins[i] > 0)
					{
						var probability = (double)bins[i] / total;
						entropy -= probability * Math.Log(probability, 2);
					}
				}
				
				// Trade entropy extremes
				if (entropy < Randomize(1.0, 0.1)) // Low entropy = predictable
					return CreateSignal(strategy, "long", "ENTROPY_COMPLEXITY_MEASURE", 
						$"Low entropy: {entropy:F2} (predictable)", 0.7, 0.7);
						
				if (entropy > Randomize(2.2, 0.1)) // High entropy = chaotic
					return CreateSignal(strategy, "short", "ENTROPY_COMPLEXITY_MEASURE", 
						$"High entropy: {entropy:F2} (chaotic)", 0.7, 0.7);
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Entropy Complexity Measure: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Helper method for cycle correlation calculation
		/// </summary>
		private double CalculateCycleCorrelation(double[] prices, int period)
		{
			// Simplified cycle correlation calculation
			var correlations = new List<double>();
			
			for (int i = period; i < prices.Length; i++)
			{
				correlations.Add(prices[i] - prices[i - period]);
			}
			
			return correlations.Count > 0 ? correlations.Average() : 0;
		}
		
		/// <summary>
		/// LOW VOLUME SCALPING - 1-minute reversal patterns with wick rejection
		/// Focuses on green bars with upper wicks followed by negative confirmation
		/// Uses 120-period volume average for context but allows more entries
		/// </summary>
		public patternFunctionResponse CheckLowVolumeScalping(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 120) return null; // Need 120-period volume average
				
				// Calculate 120-period volume average for context
				var volumeMA120 = strategy.SMA(strategy.Volume, 120)[0];
				var currentVolume = strategy.Volume[0];
				
				// Relaxed volume filter - trade when volume is not extremely high
				// Allow trading up to 120% of average (was 70%)
				if (currentVolume > volumeMA120 * 1.2)
					return null; // Skip only extremely high volume periods
				
				// PATTERN 1: Green Bar with Upper Wick + Negative Confirmation (SHORT SCALP)
				var shortReversalSignal = CheckGreenWickReversal(strategy);
				if (shortReversalSignal != null)
					return shortReversalSignal;
				
				// PATTERN 2: Red Bar with Lower Wick + Positive Confirmation (LONG SCALP)
				var longReversalSignal = CheckRedWickReversal(strategy);
				if (longReversalSignal != null)
					return longReversalSignal;
				
				// PATTERN 3: Volume Exhaustion Reversal - Higher volume bar followed by rejection
				var exhaustionSignal = CheckVolumeExhaustionReversal(strategy, volumeMA120);
				if (exhaustionSignal != null)
					return exhaustionSignal;
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Low Volume Scalping: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Check for green bar with upper wick followed by negative confirmation
		/// </summary>
		private patternFunctionResponse CheckGreenWickReversal(Strategy strategy)
		{
			// Look for green bar with upper wick in recent bars (1-5 bars ago)
			for (int wickBarIndex = 1; wickBarIndex <= 5; wickBarIndex++)
			{
				if (strategy.CurrentBar < wickBarIndex + 1) continue;
				
				var open = strategy.Open[wickBarIndex];
				var high = strategy.High[wickBarIndex];
				var low = strategy.Low[wickBarIndex];
				var close = strategy.Close[wickBarIndex];
				var tickSize = strategy.TickSize;
				
				// Must be green bar (close > open)
				if (close <= open) continue;
				
				// Calculate wick sizes
				var bodySize = close - open;
				var upperWick = high - close;
				var lowerWick = open - low;
				var totalRange = high - low;
				
				// Relaxed upper wick criteria - relative to bar structure:
				// 1. Upper wick > 40% of total bar range (relaxed from 60%)
				// 2. Upper wick > body size (relaxed from 3x)
				// 3. Upper wick > 2 ticks minimum (relaxed from 4)
				var upperWickRatio = totalRange > 0 ? upperWick / totalRange : 0;
				var upperWickToBodyRatio = bodySize > 0 ? upperWick / bodySize : 0;
				var upperWickTicks = upperWick / tickSize;
				
				bool hasSignificantUpperWick = upperWickRatio > 0.4 && // 40% of bar
				                              upperWickToBodyRatio > 1.0 && // Wick larger than body
				                              upperWickTicks > 2; // At least 2 ticks
				
				if (!hasSignificantUpperWick) continue;
				
				// Check for any negative pressure in bars after the wick bar
				int negativeSignals = 0;
				double totalNegativeMove = 0;
				double totalBarsChecked = wickBarIndex; // Total bars from wick to current
				
				for (int i = wickBarIndex - 1; i >= 0; i--) // From wick bar to current
				{
					var confirmClose = strategy.Close[i];
					var confirmOpen = strategy.Open[i];
					var prevClose = i < wickBarIndex - 1 ? strategy.Close[i + 1] : close;
					
					// Count negative signals: red bar OR lower close than previous
					if (confirmClose < confirmOpen || confirmClose < prevClose)
					{
						negativeSignals++;
						totalNegativeMove += Math.Max(Math.Max(confirmOpen - confirmClose, 0), Math.Max(prevClose - confirmClose, 0));
					}
				}
				
				// Require at least 1 negative signal with relative move
				var negativeRatio = totalBarsChecked > 0 ? negativeSignals / totalBarsChecked : 0;
				var avgNegativeMove = negativeSignals > 0 ? totalNegativeMove / negativeSignals : 0;
				var relativeNegativeMove = upperWick > 0 ? avgNegativeMove / upperWick : 0;
				
				if (negativeSignals >= 1 && relativeNegativeMove > 0.1) // At least 10% of wick size
				{
					// Entry timing - current price can be anywhere reasonable relative to wick
					var currentClose = strategy.Close[0];
					var distanceFromWickHigh = high - currentClose; // Distance below wick high
					var maxDistanceFromHigh = upperWick * 1.5; // Within 1.5x wick size
					
					if (distanceFromWickHigh >= 0 && distanceFromWickHigh <= maxDistanceFromHigh)
					{
						return CreateScalpingSignal(strategy, "short", "LOW_VOLUME_SCALPING", 
							$"Green wick reversal: {upperWickTicks:F1}T wick + {negativeSignals}/{totalBarsChecked:F0} neg signals");
					}
				}
			}
			
			return null;
		}
		
		/// <summary>
		/// Check for red bar with lower wick followed by positive confirmation
		/// </summary>
		private patternFunctionResponse CheckRedWickReversal(Strategy strategy)
		{
			// Look for red bar with lower wick in recent bars (1-5 bars ago)
			for (int wickBarIndex = 1; wickBarIndex <= 5; wickBarIndex++)
			{
				if (strategy.CurrentBar < wickBarIndex + 1) continue;
				
				var open = strategy.Open[wickBarIndex];
				var high = strategy.High[wickBarIndex];
				var low = strategy.Low[wickBarIndex];
				var close = strategy.Close[wickBarIndex];
				var tickSize = strategy.TickSize;
				
				// Must be red bar (close < open)
				if (close >= open) continue;
				
				// Calculate wick sizes
				var bodySize = open - close;
				var upperWick = high - open;
				var lowerWick = close - low;
				var totalRange = high - low;
				
				// Relaxed lower wick criteria - relative to bar structure:
				// 1. Lower wick > 40% of total bar range (relaxed from 60%)
				// 2. Lower wick > body size (relaxed from 3x)  
				// 3. Lower wick > 2 ticks minimum (relaxed from 4)
				var lowerWickRatio = totalRange > 0 ? lowerWick / totalRange : 0;
				var lowerWickToBodyRatio = bodySize > 0 ? lowerWick / bodySize : 0;
				var lowerWickTicks = lowerWick / tickSize;
				
				bool hasSignificantLowerWick = lowerWickRatio > 0.4 && // 40% of bar
				                              lowerWickToBodyRatio > 1.0 && // Wick larger than body
				                              lowerWickTicks > 2; // At least 2 ticks
				
				if (!hasSignificantLowerWick) continue;
				
				// Check for any positive pressure in bars after the wick bar
				int positiveSignals = 0;
				double totalPositiveMove = 0;
				double totalBarsChecked = wickBarIndex; // Total bars from wick to current
				
				for (int i = wickBarIndex - 1; i >= 0; i--) // From wick bar to current
				{
					var confirmClose = strategy.Close[i];
					var confirmOpen = strategy.Open[i];
					var prevClose = i < wickBarIndex - 1 ? strategy.Close[i + 1] : close;
					
					// Count positive signals: green bar OR higher close than previous
					if (confirmClose > confirmOpen || confirmClose > prevClose)
					{
						positiveSignals++;
						totalPositiveMove += Math.Max(Math.Max(confirmClose - confirmOpen, 0), Math.Max(confirmClose - prevClose, 0));
					}
				}
				
				// Require at least 1 positive signal with relative move
				var positiveRatio = totalBarsChecked > 0 ? positiveSignals / totalBarsChecked : 0;
				var avgPositiveMove = positiveSignals > 0 ? totalPositiveMove / positiveSignals : 0;
				var relativePositiveMove = lowerWick > 0 ? avgPositiveMove / lowerWick : 0;
				
				if (positiveSignals >= 1 && relativePositiveMove > 0.1) // At least 10% of wick size
				{
					// Entry timing - current price can be anywhere reasonable relative to wick
					var currentClose = strategy.Close[0];
					var distanceFromWickLow = currentClose - low; // Distance above wick low
					var maxDistanceFromLow = lowerWick * 1.5; // Within 1.5x wick size
					
					if (distanceFromWickLow >= 0 && distanceFromWickLow <= maxDistanceFromLow)
					{
						return CreateScalpingSignal(strategy, "long", "LOW_VOLUME_SCALPING", 
							$"Red wick reversal: {lowerWickTicks:F1}T wick + {positiveSignals}/{totalBarsChecked:F0} pos signals");
					}
				}
			}
			
			return null;
		}
		
		/// <summary>
		/// Check for volume exhaustion reversal pattern
		/// </summary>
		private patternFunctionResponse CheckVolumeExhaustionReversal(Strategy strategy, double volumeMA120)
		{
			if (strategy.CurrentBar < 5) return null;
			
			var tickSize = strategy.TickSize;
			
			// Look for higher volume bar followed by current lower volume rejection
			for (int i = 1; i <= 5; i++)
			{
				var higherVolumeBar = strategy.Volume[i];
				var currentVolume = strategy.Volume[0];
				
				// Relative volume comparison - higher volume bar vs current
				if (higherVolumeBar <= currentVolume * 1.2) continue; // At least 20% higher volume
				
				// Higher volume bar should have made some directional move
				var highVolOpen = strategy.Open[i];
				var highVolClose = strategy.Close[i];
				var highVolMove = Math.Abs(highVolClose - highVolOpen);
				var minMoveThreshold = tickSize * 2; // At least 2 ticks (relaxed from 6)
				
				if (highVolMove < minMoveThreshold) continue;
				
				// Current bar should show reversal tendency
				var currentClose = strategy.Close[0];
				var currentOpen = strategy.Open[0];
				
				// If higher volume was up move, look for any down pressure
				if (highVolClose > highVolOpen)
				{
					// Look for down pressure: red bar OR close below recent high
					var recentHigh = Math.Max(highVolClose, Math.Max(strategy.High[0], strategy.High[1]));
					bool hasDownPressure = currentClose < currentOpen || currentClose < recentHigh * 0.999; // Even tiny pullback
					
					if (hasDownPressure)
					{
						var reversalMove = Math.Max(currentOpen - currentClose, recentHigh - currentClose);
						return CreateScalpingSignal(strategy, "short", "LOW_VOLUME_SCALPING", 
							$"Volume exhaustion short: {highVolMove/tickSize:F1}T up -> {reversalMove/tickSize:F1}T down");
					}
				}
				// If higher volume was down move, look for any up pressure  
				else if (highVolClose < highVolOpen)
				{
					// Look for up pressure: green bar OR close above recent low
					var recentLow = Math.Min(highVolClose, Math.Min(strategy.Low[0], strategy.Low[1]));
					bool hasUpPressure = currentClose > currentOpen || currentClose > recentLow * 1.001; // Even tiny bounce
					
					if (hasUpPressure)
					{
						var reversalMove = Math.Max(currentClose - currentOpen, currentClose - recentLow);
						return CreateScalpingSignal(strategy, "long", "LOW_VOLUME_SCALPING", 
							$"Volume exhaustion long: {highVolMove/tickSize:F1}T down -> {reversalMove/tickSize:F1}T up");
					}
				}
			}
			
			return null;
		}
		
		/// <summary>
		/// Helper method to create scalping signals with large positions and ultra-tight risk
		/// </summary>
		private patternFunctionResponse CreateScalpingSignal(Strategy strategy, string direction, string signalType, string reason)
		{
			try
			{
				string entrySignalId = $"{signalType}_{strategy.Time[0]:yyyyMMdd_HHmmss}";
				// COMMENTED OUT: SignalFeatures generation
				// var features = ((MainStrategy)strategy).GenerateFeatures(strategy.Time[0], strategy.Instrument.FullName);
				
				// Get micro contract values and apply ULTRA-TIGHT scalping multipliers
				var mainStrategy = (MainStrategy)strategy;
				double maxStopLoss = mainStrategy.microContractStoploss * 0.25; // 25% of micro contract SL (ultra-tight)
				double maxTakeProfit = mainStrategy.microContractTakeProfit * 0.3; // 30% of micro contract TP (ultra-tight)
				
				// COMMENTED OUT: if (features != null)
				if (true) // Always create scalping signal
				{
					return new patternFunctionResponse
					{
						newSignal = direction == "long" ? FunctionResponses.EnterLong : FunctionResponses.EnterShort,
						signalType = signalType,
						signalDefinition = reason,
						recStop = maxStopLoss,
						recTarget = maxTakeProfit,
						recPullback = 5, // Very tight pullback
						signalScore = 0.75, // Higher confidence for scalping in controlled conditions
						recQty = 3, // LARGE POSITION SIZE: 3x normal quantity for scalping
						patternId = entrySignalId,
					
						maxStopLoss = maxStopLoss,
						maxTakeProfit = maxTakeProfit
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error creating scalping signal: {ex.Message}");
			}
			return null;
		}
		
		/// <summary>
		/// ZIGZAG PIVOT STRATEGY - Support/Resistance and Trend Analysis using ZigZag pivots
		/// Uses 4 recent peaks and valleys to identify equidistant levels and trend patterns
		/// Works on any timeframe with 3-point deviation threshold
		/// </summary>
		public patternFunctionResponse CheckZigZagPivotStrategy(Strategy strategy)
		{
			try
			{
				if (strategy.CurrentBar < 100) return null; // Need sufficient data for ZigZag
				
				// Create ZigZag indicator with 3-point deviation
				var zigZag = strategy.ZigZag(DeviationType.Points, 3, false);
				
				// Get 4 most recent peaks and valleys
				var peaks = GetRecentZigZagPivots(strategy, zigZag, true, 4); // Get peaks (highs)
				var valleys = GetRecentZigZagPivots(strategy, zigZag, false, 4); // Get valleys (lows)
				
				if (peaks.Count < 2 || valleys.Count < 2) return null; // Need at least 2 of each
				
				// Check for equidistant levels (no massive jumps)
				if (!AreEquidistant(peaks) || !AreEquidistant(valleys)) return null;
				
				var currentPrice = strategy.Close[0];
				var atr = strategy.ATR(14)[0];
				
				// PATTERN 1: Trend Continuation
				var trendSignal = CheckZigZagTrend(strategy, peaks, valleys, currentPrice, atr);
				if (trendSignal != null) return trendSignal;
				
				// PATTERN 2: Breakout from Consolidation
				var breakoutSignal = CheckZigZagBreakout(strategy, peaks, valleys, currentPrice, atr);
				if (breakoutSignal != null) return breakoutSignal;
				
				// PATTERN 3: Support/Resistance Bounce
				var bounceSignal = CheckZigZagBounce(strategy, peaks, valleys, currentPrice, atr);
				if (bounceSignal != null) return bounceSignal;
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in ZigZag Pivot Strategy: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Get recent ZigZag pivot points (peaks or valleys)
		/// </summary>
		private List<ZigZagPivot> GetRecentZigZagPivots(Strategy strategy, object zigZag, bool isPeaks, int count)
		{
			var pivots = new List<ZigZagPivot>();
			
			try
			{
				for (int occurrence = 1; occurrence <= count; occurrence++)
				{
					int pivotBar = -1;
					double pivotPrice = 0;
					
					if (isPeaks)
					{
						// Get peak (high) pivot
						pivotBar = ((dynamic)zigZag).HighBar(0, occurrence, 200);
						if (pivotBar != -1)
							pivotPrice = strategy.High[pivotBar];
					}
					else
					{
						// Get valley (low) pivot
						pivotBar = ((dynamic)zigZag).LowBar(0, occurrence, 200);
						if (pivotBar != -1)
							pivotPrice = strategy.Low[pivotBar];
					}
					
					if (pivotBar != -1)
					{
						pivots.Add(new ZigZagPivot
						{
							BarIndex = pivotBar,
							Price = pivotPrice,
							BarsAgo = strategy.CurrentBar - pivotBar,
							IsPeak = isPeaks
						});
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[ZIGZAG] Error getting pivots: {ex.Message}");
			}
			
			return pivots;
		}
		
		/// <summary>
		/// Check if pivot levels are roughly equidistant (no massive jumps)
		/// </summary>
		private bool AreEquidistant(List<ZigZagPivot> pivots)
		{
			if (pivots.Count < 2) return false;
			
			// Calculate distances between consecutive pivots
			var distances = new List<double>();
			for (int i = 0; i < pivots.Count - 1; i++)
			{
				distances.Add(Math.Abs(pivots[i].Price - pivots[i + 1].Price));
			}
			
			if (distances.Count == 0) return false;
			
			// Check if distances are relatively consistent (no distance > 3x the smallest)
			var minDistance = distances.Min();
			var maxDistance = distances.Max();
			
			return maxDistance <= minDistance * 3; // Allow up to 3x variation
		}
		
		/// <summary>
		/// Check for trend continuation patterns
		/// </summary>
		private patternFunctionResponse CheckZigZagTrend(Strategy strategy, List<ZigZagPivot> peaks, List<ZigZagPivot> valleys, double currentPrice, double atr)
		{
			if (peaks.Count < 2 || valleys.Count < 2) return null;
			
			// Sort by recency (most recent first)
			peaks = peaks.OrderBy(p => p.BarsAgo).ToList();
			valleys = valleys.OrderBy(v => v.BarsAgo).ToList();
			
			var recentPeak = peaks[0];
			var prevPeak = peaks[1];
			var recentValley = valleys[0];
			var prevValley = valleys[1];
			
			// UPTREND: Higher highs and higher lows
			bool isUptrend = recentPeak.Price > prevPeak.Price && recentValley.Price > prevValley.Price;
			
			// DOWNTREND: Lower highs and lower lows
			bool isDowntrend = recentPeak.Price < prevPeak.Price && recentValley.Price < prevValley.Price;
			
			var tolerance = atr * 0.5; // Half ATR tolerance for trend continuation
			
			if (isUptrend)
			{
				// Long on pullback to recent valley in uptrend
				if (currentPrice <= recentValley.Price + tolerance && 
					currentPrice > recentValley.Price - tolerance)
				{
					return CreateSignal(strategy, "long", "ZIGZAG_PIVOT_STRATEGY", 
						$"Uptrend continuation: pullback to valley {recentValley.Price:F2}", 0.8, 0.8);
				}
			}
			else if (isDowntrend)
			{
				// Short on bounce to recent peak in downtrend
				if (currentPrice >= recentPeak.Price - tolerance && 
					currentPrice < recentPeak.Price + tolerance)
				{
					return CreateSignal(strategy, "short", "ZIGZAG_PIVOT_STRATEGY", 
						$"Downtrend continuation: bounce to peak {recentPeak.Price:F2}", 0.8, 0.8);
				}
			}
			
			return null;
		}
		
		/// <summary>
		/// Check for breakout from consolidation
		/// </summary>
		private patternFunctionResponse CheckZigZagBreakout(Strategy strategy, List<ZigZagPivot> peaks, List<ZigZagPivot> valleys, double currentPrice, double atr)
		{
			if (peaks.Count < 3 || valleys.Count < 3) return null;
			
			// Check if peaks and valleys are consolidated (similar levels)
			var peakRange = peaks.Max(p => p.Price) - peaks.Min(p => p.Price);
			var valleyRange = valleys.Max(v => v.Price) - valleys.Min(v => v.Price);
			var consolidationThreshold = atr * 2; // Consolidation within 2 ATR
			
			bool isConsolidated = peakRange <= consolidationThreshold && valleyRange <= consolidationThreshold;
			
			if (!isConsolidated) return null;
			
			var highestPeak = peaks.Max(p => p.Price);
			var lowestValley = valleys.Min(v => v.Price);
			var breakoutTolerance = atr * 0.3; // 30% ATR for breakout confirmation
			
			// BULLISH BREAKOUT: Price breaking above consolidated peaks
			if (currentPrice > highestPeak + breakoutTolerance)
			{
				return CreateSignal(strategy, "long", "ZIGZAG_PIVOT_STRATEGY", 
					$"Bullish breakout above {highestPeak:F2} consolidation", 0.85, 0.85);
			}
			
			// BEARISH BREAKOUT: Price breaking below consolidated valleys
			if (currentPrice < lowestValley - breakoutTolerance)
			{
				return CreateSignal(strategy, "short", "ZIGZAG_PIVOT_STRATEGY", 
					$"Bearish breakout below {lowestValley:F2} consolidation", 0.85, 0.85);
			}
			
			return null;
		}
		
		/// <summary>
		/// Check for support/resistance bounces
		/// </summary>
		private patternFunctionResponse CheckZigZagBounce(Strategy strategy, List<ZigZagPivot> peaks, List<ZigZagPivot> valleys, double currentPrice, double atr)
		{
			var bounceDistance = atr * 0.4; // Within 40% ATR of level
			
			// Check bounce off recent peak (resistance)
			foreach (var peak in peaks.Take(2)) // Check 2 most recent peaks
			{
				if (Math.Abs(currentPrice - peak.Price) <= bounceDistance && 
					strategy.Close[0] < strategy.Close[1]) // Price pulling back from resistance
				{
					return CreateSignal(strategy, "short", "ZIGZAG_PIVOT_STRATEGY", 
						$"Resistance bounce off peak {peak.Price:F2}", 0.7, 0.7);
				}
			}
			
			// Check bounce off recent valley (support)
			foreach (var valley in valleys.Take(2)) // Check 2 most recent valleys
			{
				if (Math.Abs(currentPrice - valley.Price) <= bounceDistance && 
					strategy.Close[0] > strategy.Close[1]) // Price bouncing from support
				{
					return CreateSignal(strategy, "long", "ZIGZAG_PIVOT_STRATEGY", 
						$"Support bounce off valley {valley.Price:F2}", 0.7, 0.7);
				}
			}
			
			return null;
		}
	
	/// <summary>
	/// MGC PATTERN FILTER - XGBoost-discovered patterns for MGC
	/// Uses discovered feature ranges to filter high-probability trades
	/// Based on analysis of 6,354 MGC trades
	/// </summary>
	public patternFunctionResponse CheckMGCPatternFilter(Strategy strategy)
	{
		try
		{
			// Only apply to MGC instruments
			string instrumentName = strategy.Instrument?.MasterInstrument?.Name ?? "";
			if (!instrumentName.Contains("MGC"))
				return null;
				
			if (strategy.CurrentBar < 20) return null;
			
			// Calculate discovered pattern features
			double closeToHigh = (strategy.High[0] - strategy.Close[0]) / (strategy.High[0] - strategy.Low[0] + 0.0001);
			double rsi = strategy.RSI(14, 1)[0];
			double ema9 = strategy.EMA(9)[0];
			double ema21 = strategy.EMA(21)[0];
			double emaDiff = (ema9 - ema21) / strategy.Close[0] * 100; // As percentage
			
			// Bollinger Band features
			var bb = strategy.Bollinger(2, 20);
			double bbUpper = bb.Upper[0];
			double priceToBBUpper = (strategy.Close[0] - bbUpper) / bbUpper;
			
			// Debug logging every 100 bars to see actual values
			if (strategy.CurrentBar % 100 == 0)
			{
				strategy.Print($"[MGC-DEBUG] Bar {strategy.CurrentBar} - closeToHigh: {closeToHigh:F3}, RSI: {rsi:F1}, emaDiff: {emaDiff:F3}%, atrPct: {strategy.ATR(14)[0] / strategy.Close[0]:F4}, priceToBBUpper: {priceToBBUpper:F3}");
			}
			
			// ATR-based volatility check
			double atr = strategy.ATR(14)[0];
			double atrPct = atr / strategy.Close[0];
			
			// Volume confirmation
			double volume = strategy.Volume[0];
			double volumeMA = strategy.SMA(strategy.Volume, 20)[0];
			double volumeRatio = volume / (volumeMA + 1);
			
			// PATTERN 1: Bullish MGC Pattern (based on discovered ranges)
			bool bullishPattern = false;
			
			// Track which conditions are met for debugging
			bool closeCondition = closeToHigh <= 0.3;  // Relaxed from 0.1
			bool rsiCondition = rsi < 70 && rsi > 30;
			bool emaCondition = emaDiff >= -0.1 && emaDiff <= 1.0;  // Relaxed from 0.3-0.65
			bool bbCondition = priceToBBUpper >= -0.1 && priceToBBUpper <= 0.5;  // Relaxed
			bool atrCondition = atrPct >= 0.001 && atrPct <= 0.05;  // Much wider range
			bool volumeCondition = volumeRatio >= 0.5;  // Relaxed from 0.8
			bool trendCondition = strategy.EMA(20)[0] > strategy.EMA(50)[0];
			
			// Log near-misses
			int conditionsMet = (closeCondition ? 1 : 0) + (rsiCondition ? 1 : 0) + 
			                   (emaCondition ? 1 : 0) + (bbCondition ? 1 : 0) + 
			                   (atrCondition ? 1 : 0) + (volumeCondition ? 1 : 0) + 
			                   (trendCondition ? 1 : 0);
			
			if (conditionsMet >= 5 && strategy.CurrentBar % 20 == 0)  // Log if 5+ conditions met
			{
				strategy.Print($"[MGC-NEAR-MISS] {conditionsMet}/7 conditions met - Close:{closeCondition} RSI:{rsiCondition} EMA:{emaCondition} BB:{bbCondition} ATR:{atrCondition} Vol:{volumeCondition} Trend:{trendCondition}");
			}
			
			// More flexible: require 5 out of 7 conditions with trend being mandatory
			if (conditionsMet >= 5 && trendCondition)
			{
				bullishPattern = true;
				if (strategy.CurrentBar % 100 == 0)
					strategy.Print($"[MGC-PATTERN] Bullish pattern detected with {conditionsMet}/7 conditions");
			}
			
			// PATTERN 2: Bearish MGC Pattern (inverse of bullish)
			bool bearishPattern = false;
			double closeToLow = (strategy.Close[0] - strategy.Low[0]) / (strategy.High[0] - strategy.Low[0] + 0.0001);
			double priceToBBLower = (strategy.Close[0] - bb.Lower[0]) / bb.Lower[0];
			
			// Track which conditions are met for debugging
			bool bearCloseCondition = closeToLow <= 0.3;  // Relaxed from 0.1
			bool bearRsiCondition = rsi > 30 && rsi < 70;
			bool bearEmaCondition = emaDiff <= 0.1 && emaDiff >= -1.0;  // Relaxed
			bool bearBBCondition = priceToBBLower >= -0.5 && priceToBBLower <= 0.1;  // Relaxed
			bool bearAtrCondition = atrPct >= 0.001 && atrPct <= 0.05;  // Much wider range
			bool bearVolumeCondition = volumeRatio >= 0.5;  // Relaxed from 0.8
			bool bearTrendCondition = strategy.EMA(20)[0] < strategy.EMA(50)[0];
			
			// Log near-misses
			int bearConditionsMet = (bearCloseCondition ? 1 : 0) + (bearRsiCondition ? 1 : 0) + 
			                       (bearEmaCondition ? 1 : 0) + (bearBBCondition ? 1 : 0) + 
			                       (bearAtrCondition ? 1 : 0) + (bearVolumeCondition ? 1 : 0) + 
			                       (bearTrendCondition ? 1 : 0);
			
			if (bearConditionsMet >= 5 && strategy.CurrentBar % 20 == 0)  // Log if 5+ conditions met
			{
				strategy.Print($"[MGC-BEAR-NEAR-MISS] {bearConditionsMet}/7 conditions met - Close:{bearCloseCondition} RSI:{bearRsiCondition} EMA:{bearEmaCondition} BB:{bearBBCondition} ATR:{bearAtrCondition} Vol:{bearVolumeCondition} Trend:{bearTrendCondition}");
			}
			
			// More flexible: require 5 out of 7 conditions with trend being mandatory
			if (bearConditionsMet >= 5 && bearTrendCondition)
			{
				bearishPattern = true;
				if (strategy.CurrentBar % 100 == 0)
					strategy.Print($"[MGC-PATTERN] Bearish pattern detected with {bearConditionsMet}/7 conditions");
			}
			
			// Alternative: Use scoring approach if no perfect pattern match
			// This helps catch good setups that don't meet ALL conditions
			if (!bullishPattern && !bearishPattern)
			{
				// Score-based approach for more trades
				double bullishScore = 0;
				double bearishScore = 0;
				
				// Add volatility filter - skip if market is too volatile
				if (atrPct > 0.035)  // Skip if ATR > 3.5%
				{
					if (strategy.CurrentBar % 100 == 0)
						strategy.Print($"[MGC-SKIP] Market too volatile: ATR% = {atrPct:F3}");
					return null;
				}
				
				// Calculate trend and market structure
				double ema20 = strategy.EMA(20)[0];
				double ema50 = strategy.EMA(50)[0];
				double sma200 = strategy.CurrentBar >= 200 ? strategy.SMA(200)[0] : ema50; // Fallback if not enough bars
				double priceToEma20 = (strategy.Close[0] - ema20) / ema20;
				double ema20ToEma50 = (ema20 - ema50) / Math.Max(ema50, 1);
				
				// Volume trend
				double volumeTrend = 0;
				if (strategy.CurrentBar >= 5)
				{
					double recentVolume = 0;
					double olderVolume = 0;
					for (int i = 0; i < 3; i++)
					{
						recentVolume += strategy.Volume[i];
						olderVolume += strategy.Volume[i + 3];
					}
					volumeTrend = (recentVolume - olderVolume) / (olderVolume + 1);
				}
				
				// Define market regime
				bool strongUptrend = ema20 > ema50 && ema50 > sma200 && priceToEma20 > 0.003;
				bool strongDowntrend = ema20 < ema50 && ema50 < sma200 && priceToEma20 < -0.003;
				bool rangebound = Math.Abs(ema20ToEma50) < 0.002 && Math.Abs(priceToEma20) < 0.003;
				
				// STANCE: Only trade WITH the trend or in clear ranges
				if (strategy.CurrentBar % 50 == 0)
				{
					string regime = strongUptrend ? "UPTREND" : strongDowntrend ? "DOWNTREND" : rangebound ? "RANGE" : "UNCLEAR";
					strategy.Print($"[MGC-REGIME] {regime} - EMA20/50: {ema20ToEma50:F3}, Price/EMA20: {priceToEma20:F3}");
				}
				
				// Bullish scoring with directional reinforcement
				if (closeToHigh <= 0.3) bullishScore += 2;     // Strong bullish candle
				if (rsi > 40 && rsi < 60) bullishScore += 1.5; // Neutral RSI preferred
				if (emaDiff > 0.1) bullishScore += 2;          // Clear positive trend
				if (strategy.Close[0] > ema20 && priceToEma20 > 0.002) bullishScore += 1; // Above EMA20
				if (volumeRatio > 0.8 && volumeTrend > 0.1) bullishScore += 1; // Rising volume
				if (atrPct > 0.008 && atrPct < 0.025) bullishScore += 1;  // Moderate volatility
				
				// DIRECTIONAL REINFORCEMENT FOR BULLS
				if (strongUptrend) 
				{
					bullishScore += 3;  // Major bonus for trend alignment
					if (volumeTrend > 0.2) bullishScore += 1;  // Extra for volume confirmation
				}
				else if (rangebound && strategy.Close[0] < ema20)
				{
					bullishScore += 1.5;  // Range trade from bottom
				}
				else if (strongDowntrend)
				{
					bullishScore = 0;  // NO COUNTER-TREND LONGS
				}
				
				// Bearish scoring with directional reinforcement
				if (closeToLow <= 0.3) bearishScore += 2;      // Strong bearish candle
				if (rsi > 40 && rsi < 60) bearishScore += 1.5; // Neutral RSI preferred
				if (emaDiff < -0.1) bearishScore += 2;         // Clear negative trend
				if (strategy.Close[0] < ema20 && Math.Abs(priceToEma20) > 0.002) bearishScore += 1; // Below EMA20
				if (volumeRatio > 0.8 && volumeTrend > 0.1) bearishScore += 1; // Rising volume
				if (atrPct > 0.008 && atrPct < 0.025) bearishScore += 1;  // Moderate volatility
				
				// DIRECTIONAL REINFORCEMENT FOR BEARS
				if (strongDowntrend) 
				{
					bearishScore += 3;  // Major bonus for trend alignment
					if (volumeTrend > 0.2) bearishScore += 1;  // Extra for volume confirmation
				}
				else if (rangebound && strategy.Close[0] > ema20)
				{
					bearishScore += 1.5;  // Range trade from top
				}
				else if (strongUptrend)
				{
					bearishScore = 0;  // NO COUNTER-TREND SHORTS
				}
				
				// Log high scores for analysis (max possible ~11.5 with trend bonus)
				if ((bullishScore >= 5.0 || bearishScore >= 5.0) && strategy.CurrentBar % 50 == 0)
				{
					strategy.Print($"[MGC-SCORE] Bull: {bullishScore:F1}, Bear: {bearishScore:F1} | Regime: {(strongUptrend ? "UP" : strongDowntrend ? "DOWN" : rangebound ? "RANGE" : "UNCLEAR")}");
				}
				
				// Trigger on high score with trend alignment
				// Need at least 7.0 score (out of ~11.5 possible with trend bonus)
				if (bullishScore >= 7.0)
				{
					// Adjust risk based on current volatility
					double volatilityMultiplier = Math.Min(Math.Max(atrPct / 0.02, 0.5), 1.5); // Normalize around 2% ATR
					double adjustedSL = 0.8 * volatilityMultiplier;  // Tighter stop in low vol, wider in high vol
					double adjustedTP = 0.87 / volatilityMultiplier; // Smaller target in high vol
					
					strategy.Print($"[MGC-RISK] Volatility mult: {volatilityMultiplier:F2}, SL: {adjustedSL:F2}, TP: {adjustedTP:F2}");
					
					string regime = strongUptrend ? "UPTREND" : rangebound ? "RANGE" : "NEUTRAL";
					return CreateSignal(strategy, "long", "MGC_PATTERN_FILTER", 
						$"MGC bullish {regime} score: {bullishScore:F1}, RSI={rsi:F1}, VolTrend={volumeTrend:F2}", 
						adjustedSL, adjustedTP);
				}
				else if (bearishScore >= 7.0)
				{
					// Adjust risk based on current volatility
					double volatilityMultiplier = Math.Min(Math.Max(atrPct / 0.02, 0.5), 1.5);
					double adjustedSL = 0.8 * volatilityMultiplier;
					double adjustedTP = 0.87 / volatilityMultiplier;
					
					strategy.Print($"[MGC-RISK] Volatility mult: {volatilityMultiplier:F2}, SL: {adjustedSL:F2}, TP: {adjustedTP:F2}");
					
					string regime = strongDowntrend ? "DOWNTREND" : rangebound ? "RANGE" : "NEUTRAL";
					return CreateSignal(strategy, "short", "MGC_PATTERN_FILTER", 
						$"MGC bearish {regime} score: {bearishScore:F1}, RSI={rsi:F1}, VolTrend={volumeTrend:F2}", 
						adjustedSL, adjustedTP);
				}
			}
			else
			{
				// Original pattern matched
				if (bullishPattern)
				{
					return CreateSignal(strategy, "long", "MGC_PATTERN_FILTER", 
						$"MGC bullish pattern (strict): RSI={rsi:F1}, ATR%={atrPct:F3}, EMA diff={emaDiff:F2}%", 
						0.8, 0.87);
				}
				
				if (bearishPattern)
				{
					return CreateSignal(strategy, "short", "MGC_PATTERN_FILTER", 
						$"MGC bearish pattern (strict): RSI={rsi:F1}, ATR%={atrPct:F3}, EMA diff={emaDiff:F2}%", 
						0.8, 0.87);
				}
			}
		}
		catch (Exception ex)
		{
			strategy.Print($"[TRADITIONAL] Error in MGC Pattern Filter: {ex.Message}");
		}
		
		return null;
	}
	
	/// <summary>
	/// Helper class for ZigZag pivot data
	/// </summary>
	public class ZigZagPivot
	{
		public int BarIndex { get; set; }
		public double Price { get; set; }
		public int BarsAgo { get; set; }
		public bool IsPeak { get; set; }
	}
	} // End of TraditionalStrategies class
}

*/