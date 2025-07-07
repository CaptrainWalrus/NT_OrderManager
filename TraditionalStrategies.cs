using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
	/// <summary>
	/// Traditional strategy library for meta-labeling approach
	/// These strategies provide the baseline "edge" that RF models will enhance
	/// </summary>
	public static class TraditionalStrategies
	{
		/// <summary>
		/// Order Flow Imbalance - Buy/Sell Pressure Analysis with Wick Analysis
		/// Detects aggressive buying/selling through volume delta and wick/body ratios
		/// </summary>
		public static patternFunctionResponse CheckOrderFlowImbalance(Strategy strategy)
		{
			try
			{
				// Ensure we have enough bars for context
				if (strategy.CurrentBar < 50) return null;
				
				// Get price data
				var close = strategy.Close[0];
				var open = strategy.Open[0];
				var high = strategy.High[0];
				var low = strategy.Low[0];
				var volume = strategy.Volume[0];
				var atr = strategy.ATR(14)[0];
				
				// Calculate candle components
				double body = Math.Abs(close - open);
				double totalRange = high - low;
				double upperWick = high - Math.Max(open, close);
				double lowerWick = Math.Min(open, close) - low;
				
				// Wick ratios - critical for gold
				double bodyRatio = totalRange > 0 ? body / totalRange : 0;
				double upperWickRatio = totalRange > 0 ? upperWick / totalRange : 0;
				double lowerWickRatio = totalRange > 0 ? lowerWick / totalRange : 0;
				double wickToBodyRatio = body > 0 ? (upperWick + lowerWick) / body : 10; // High value if no body
				
				// Wick imbalance - tells us where rejection happened
				double wickImbalance = (upperWick - lowerWick) / totalRange;
				
				// Check market context to avoid chop
				var ema50 = strategy.EMA(50)[0];
				var ema200 = strategy.EMA(200)[0];
				double recentRange = strategy.MAX(strategy.High, 10)[0] - strategy.MIN(strategy.Low, 10)[0];
				bool isChoppy = recentRange < atr * 2;
				
				if (isChoppy) return null; // Skip choppy markets
				
				// Enhanced volume delta calculation using wick analysis
				double volumeDelta = 0;
				double buyVolume = 0;
				double sellVolume = 0;
				
				// Volume distribution based on wick and body analysis
				if (close > open) // Bullish bar
				{
					// Large lower wick = buyers stepped in at lows
					if (lowerWickRatio > 0.4 && upperWickRatio < 0.2)
					{
						buyVolume = volume * 0.8; // Strong buying absorption
						sellVolume = volume * 0.2;
					}
					// Large upper wick = sellers rejected highs
					else if (upperWickRatio > 0.4 && bodyRatio < 0.3)
					{
						buyVolume = volume * 0.4; // Weak bullish - rejection at top
						sellVolume = volume * 0.6;
					}
					// Clean bullish bar (small wicks)
					else if (bodyRatio > 0.6 && upperWickRatio < 0.15)
					{
						buyVolume = volume * 0.75; // Strong directional buying
						sellVolume = volume * 0.25;
					}
					else
					{
						// Standard calculation
						double bullishRatio = (close - open) / totalRange;
						buyVolume = volume * (0.5 + bullishRatio * 0.3);
						sellVolume = volume - buyVolume;
					}
				}
				else if (close < open) // Bearish bar
				{
					// Large upper wick = sellers stepped in at highs
					if (upperWickRatio > 0.4 && lowerWickRatio < 0.2)
					{
						sellVolume = volume * 0.8; // Strong selling pressure
						buyVolume = volume * 0.2;
					}
					// Large lower wick = buyers rejected lows
					else if (lowerWickRatio > 0.4 && bodyRatio < 0.3)
					{
						sellVolume = volume * 0.4; // Weak bearish - rejection at bottom
						buyVolume = volume * 0.6;
					}
					// Clean bearish bar (small wicks)
					else if (bodyRatio > 0.6 && lowerWickRatio < 0.15)
					{
						sellVolume = volume * 0.75; // Strong directional selling
						buyVolume = volume * 0.25;
					}
					else
					{
						// Standard calculation
						double bearishRatio = (open - close) / totalRange;
						sellVolume = volume * (0.5 + bearishRatio * 0.3);
						buyVolume = volume - sellVolume;
					}
				}
				else // Doji
				{
					// Doji with long upper wick = selling pressure
					if (upperWickRatio > lowerWickRatio * 2)
					{
						sellVolume = volume * 0.65;
						buyVolume = volume * 0.35;
					}
					// Doji with long lower wick = buying pressure
					else if (lowerWickRatio > upperWickRatio * 2)
					{
						buyVolume = volume * 0.65;
						sellVolume = volume * 0.35;
					}
					// Balanced doji
					else
					{
						buyVolume = volume * 0.5;
						sellVolume = volume * 0.5;
					}
				}
				
				volumeDelta = buyVolume - sellVolume;
				
				// Calculate cumulative delta with wick analysis and decay
				double cumulativeDelta = volumeDelta;
				double cumulativeWickScore = 0; // Track wick patterns over time
				
				for (int i = 1; i <= 4; i++)
				{
					var pastClose = strategy.Close[i];
					var pastOpen = strategy.Open[i];
					var pastHigh = strategy.High[i];
					var pastLow = strategy.Low[i];
					var pastVolume = strategy.Volume[i];
					var pastRange = pastHigh - pastLow;
					
					// Skip invalid bars
					if (pastRange <= 0) continue;
					
					// Calculate past bar components
					double pastBody = Math.Abs(pastClose - pastOpen);
					double pastUpperWick = pastHigh - Math.Max(pastOpen, pastClose);
					double pastLowerWick = Math.Min(pastOpen, pastClose) - pastLow;
					double pastBodyRatio = pastBody / pastRange;
					double pastUpperWickRatio = pastUpperWick / pastRange;
					double pastLowerWickRatio = pastLowerWick / pastRange;
					
					// Apply decay factor - recent bars matter more
					double decay = 1.0 - (i * 0.15);
					
					// Calculate past bar delta based on wick analysis
					double pastDelta = 0;
					double wickScore = 0;
					
					if (pastClose > pastOpen)
					{
						if (pastLowerWickRatio > 0.4 && pastUpperWickRatio < 0.2)
						{
							pastDelta = pastVolume * 0.6 * decay; // Strong buy absorption
							wickScore = 1.0 * decay;
						}
						else if (pastUpperWickRatio > 0.4)
						{
							pastDelta = -pastVolume * 0.2 * decay; // Rejected at highs
							wickScore = -0.5 * decay;
						}
						else
						{
							pastDelta = pastVolume * 0.3 * decay;
							wickScore = 0.3 * decay;
						}
					}
					else
					{
						if (pastUpperWickRatio > 0.4 && pastLowerWickRatio < 0.2)
						{
							pastDelta = -pastVolume * 0.6 * decay; // Strong sell pressure
							wickScore = -1.0 * decay;
						}
						else if (pastLowerWickRatio > 0.4)
						{
							pastDelta = pastVolume * 0.2 * decay; // Rejected at lows
							wickScore = 0.5 * decay;
						}
						else
						{
							pastDelta = -pastVolume * 0.3 * decay;
							wickScore = -0.3 * decay;
						}
					}
					
					cumulativeDelta += pastDelta;
					cumulativeWickScore += wickScore;
				}
				
				// Calculate average volume for context
				double avgVolume = 0;
				for (int i = 1; i <= 10; i++)
				{
					avgVolume += strategy.Volume[i];
				}
				avgVolume /= 10;
				
				// Calculate trend and momentum context
				bool bullishTrend = close > ema50 && ema50 > ema200;
				bool bearishTrend = close < ema50 && ema50 < ema200;
				var rsi = strategy.RSI(14, 3)[0];
				
				// Strong buying imbalance signal with wick confirmation
				if (volumeDelta > volume * 0.3 && // Current bar shows 30%+ buying
					cumulativeDelta > 0 && // Cumulative buying pressure
					cumulativeWickScore > 0.5 && // Positive wick patterns
					volume > avgVolume * 1.5 && // High volume
					close > open && // Bullish close
					bullishTrend && // With the trend
					rsi > 40 && rsi < 70) // Not oversold/overbought
				{
					// Additional wick-based entry filters
					bool strongEntry = false;
					string entryReason = "";
					
					// Hammer pattern - strong rejection of lows
					if (lowerWickRatio > 0.5 && upperWickRatio < 0.15 && bodyRatio < 0.35)
					{
						strongEntry = true;
						entryReason = "Hammer with volume surge";
					}
					// Bullish engulfing with small upper wick
					else if (bodyRatio > 0.7 && upperWickRatio < 0.1 && close > strategy.High[1])
					{
						strongEntry = true;
						entryReason = "Clean breakout with volume";
					}
					// Lower wick absorption
					else if (lowerWickRatio > 0.4 && wickImbalance < -0.2 && volumeDelta > volume * 0.5)
					{
						strongEntry = true;
						entryReason = "Strong buying absorption at lows";
					}
					
					if (strongEntry)
					{
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterLong,
							signalType = "ORDER_FLOW_IMBALANCE",
							signalDefinition = $"Buy imbalance: {entryReason} (Delta: {(volumeDelta/volume*100):F1}%, WickScore: {cumulativeWickScore:F2})",
							recStop = Math.Max(low - (atr * 1.2), low - lowerWick * 0.5), // Use wick low as reference
							recTarget = close + (atr * 2.5),
							signalScore = 0.85,
							recQty = 1,
							patternId = $"OFI_BUY_{strategy.Time[0]:yyyyMMdd_HHmmss}",
							patternSubType = "WICK_ABSORPTION_BUY",
							signalFeatures = CollectSignalFeatures(strategy, "ORDER_FLOW_IMBALANCE")
						};
					}
				}
				
				// Strong selling imbalance signal with wick confirmation
				if (volumeDelta < volume * -0.3 && // Current bar shows 30%+ selling
					cumulativeDelta < 0 && // Cumulative selling pressure
					cumulativeWickScore < -0.5 && // Negative wick patterns
					volume > avgVolume * 1.5 && // High volume
					close < open && // Bearish close
					bearishTrend && // With the trend
					rsi < 60 && rsi > 30) // Not overbought/oversold
				{
					// Additional wick-based entry filters
					bool strongEntry = false;
					string entryReason = "";
					
					// Shooting star pattern - strong rejection of highs
					if (upperWickRatio > 0.5 && lowerWickRatio < 0.15 && bodyRatio < 0.35)
					{
						strongEntry = true;
						entryReason = "Shooting star with volume surge";
					}
					// Bearish engulfing with small lower wick
					else if (bodyRatio > 0.7 && lowerWickRatio < 0.1 && close < strategy.Low[1])
					{
						strongEntry = true;
						entryReason = "Clean breakdown with volume";
					}
					// Upper wick rejection
					else if (upperWickRatio > 0.4 && wickImbalance > 0.2 && volumeDelta < volume * -0.5)
					{
						strongEntry = true;
						entryReason = "Strong selling rejection at highs";
					}
					
					if (strongEntry)
					{
						return new patternFunctionResponse
						{
							newSignal = FunctionResponses.EnterShort,
							signalType = "ORDER_FLOW_IMBALANCE",
							signalDefinition = $"Sell imbalance: {entryReason} (Delta: {(volumeDelta/volume*100):F1}%, WickScore: {cumulativeWickScore:F2})",
							recStop = Math.Min(high + (atr * 1.2), high + upperWick * 0.5), // Use wick high as reference
							recTarget = close - (atr * 2.5),
							signalScore = 0.85,
							recQty = 1,
							patternId = $"OFI_SELL_{strategy.Time[0]:yyyyMMdd_HHmmss}",
							patternSubType = "WICK_REJECTION_SELL",
							signalFeatures = CollectSignalFeatures(strategy, "ORDER_FLOW_IMBALANCE")
						};
					}
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Order Flow Imbalance: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// EMA Crossover with Volume Delta Confirmation
		/// Classic momentum strategy enhanced with order flow analysis
		/// </summary>
		public static patternFunctionResponse CheckEMACrossover(Strategy strategy)
		{
			try
			{
				// Ensure we have enough bars
				if (strategy.CurrentBar < 21) return null;
				
				var ema9 = strategy.EMA(9)[0];
				var ema21 = strategy.EMA(21)[0];
				var ema9Prev = strategy.EMA(9)[1];
				var ema21Prev = strategy.EMA(21)[1];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 20)[0];
				var atr = strategy.ATR(14)[0];
				var close = strategy.Close[0];
				var open = strategy.Open[0];
				var high = strategy.High[0];
				var low = strategy.Low[0];
				
				// Calculate volume delta for order flow confirmation
				double volumeDelta = 0;
				if (close > open && high > low)
				{
					double bullishRatio = (close - open) / (high - low);
					double buyVolume = volume * (0.5 + bullishRatio * 0.5);
					volumeDelta = buyVolume - (volume - buyVolume);
				}
				else if (close < open && high > low)
				{
					double bearishRatio = (open - close) / (high - low);
					double sellVolume = volume * (0.5 + bearishRatio * 0.5);
					volumeDelta = (volume - sellVolume) - sellVolume;
				}
				
				// Bullish crossover: EMA9 crosses above EMA21 with buying pressure
				if (ema9 > ema21 && ema9Prev <= ema21Prev && 
					volume > volumeMA * 1.2 && volumeDelta > 0)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterLong,
						signalType = "EMA_CROSS",
						signalDefinition = "EMA(9) > EMA(21) && EMA(9)[1] <= EMA(21)[1] && Volume > VolumeMA * 1.2 && VolumeDelta > 0",
						recStop = strategy.Low[0] - (2 * atr),
						recTarget = strategy.Close[0] + (3 * atr),
						signalScore = 0.75,
						recQty = 1,
						patternId = $"EMA_CROSS_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BULLISH_CROSS",
						signalFeatures = CollectSignalFeatures(strategy, "EMA_CROSS")
					};
				}
				
				// Bearish crossover: EMA9 crosses below EMA21 with selling pressure
				if (ema9 < ema21 && ema9Prev >= ema21Prev && 
					volume > volumeMA * 1.2 && volumeDelta < 0)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterShort,
						signalType = "EMA_CROSS",
						signalDefinition = "EMA(9) < EMA(21) && EMA(9)[1] >= EMA(21)[1] && Volume > VolumeMA * 1.2 && VolumeDelta < 0",
						recStop = strategy.High[0] + (2 * atr),
						recTarget = strategy.Close[0] - (3 * atr),
						signalScore = 0.75,
						recQty = 1,
						patternId = $"EMA_CROSS_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BEARISH_CROSS",
						signalFeatures = CollectSignalFeatures(strategy, "EMA_CROSS")
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in EMA Crossover: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// RSI Divergence with Trend Filter
		/// Mean reversion strategy with trend confirmation
		/// </summary>
		public static patternFunctionResponse CheckRSIDivergence(Strategy strategy)
		{
			try
			{
				// Ensure we have enough bars
				if (strategy.CurrentBar < 50) return null;
				
				var rsi = strategy.RSI(14, 3)[0];
				var close = strategy.Close[0];
				var ema50 = strategy.EMA(50)[0];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 20)[0];
				var atr = strategy.ATR(14)[0];
				
				// Bullish divergence: RSI oversold but price above trend
				if (rsi < 30 && close > ema50 && volume > volumeMA)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterLong,
						signalType = "RSI_DIVERGENCE",
						signalDefinition = "RSI(14) < 30 && Close > EMA(50) && Volume > VolumeMA",
						recStop = strategy.Low[0] - (1.5 * atr),
						recTarget = strategy.Close[0] + (2.5 * atr),
						signalScore = 0.70,
						recQty = 1,
						patternId = $"RSI_DIV_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BULLISH_DIVERGENCE",
						signalFeatures = CollectSignalFeatures(strategy, "RSI_DIVERGENCE")
					};
				}
				
				// Bearish divergence: RSI overbought but price below trend
				if (rsi > 70 && close < ema50 && volume > volumeMA)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterShort,
						signalType = "RSI_DIVERGENCE",
						signalDefinition = "RSI(14) > 70 && Close < EMA(50) && Volume > VolumeMA",
						recStop = strategy.High[0] + (1.5 * atr),
						recTarget = strategy.Close[0] - (2.5 * atr),
						signalScore = 0.70,
						recQty = 1,
						patternId = $"RSI_DIV_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BEARISH_DIVERGENCE",
						signalFeatures = CollectSignalFeatures(strategy, "RSI_DIVERGENCE")
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in RSI Divergence: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Breakout Strategy
		/// Price breaks above/below 20-period high/low with volume confirmation
		/// </summary>
		public static patternFunctionResponse CheckBreakoutStrategy(Strategy strategy)
		{
			try
			{
				// Ensure we have enough bars
				if (strategy.CurrentBar < 20) return null;
				
				var high20 = strategy.MAX(strategy.High, 20)[1]; // Exclude current bar
				var low20 = strategy.MIN(strategy.Low, 20)[1];   // Exclude current bar
				var close = strategy.Close[0];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 20)[0];
				var atr = strategy.ATR(14)[0];
				
				// Bullish breakout: Close above 20-period high with volume
				if (close > high20 && volume > volumeMA * 1.5)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterLong,
						signalType = "BREAKOUT",
						signalDefinition = "Close > MAX(High, 20)[1] && Volume > VolumeMA * 1.5",
						recStop = low20,
						recTarget = close + (2 * atr),
						signalScore = 0.80,
						recQty = 1,
						patternId = $"BREAKOUT_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BULLISH_BREAKOUT",
						signalFeatures = CollectSignalFeatures(strategy, "BREAKOUT")
					};
				}
				
				// Bearish breakdown: Close below 20-period low with volume
				if (close < low20 && volume > volumeMA * 1.5)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterShort,
						signalType = "BREAKOUT",
						signalDefinition = "Close < MIN(Low, 20)[1] && Volume > VolumeMA * 1.5",
						recStop = high20,
						recTarget = close - (2 * atr),
						signalScore = 0.80,
						recQty = 1,
						patternId = $"BREAKOUT_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BEARISH_BREAKDOWN",
						signalFeatures = CollectSignalFeatures(strategy, "BREAKOUT")
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Breakout Strategy: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// VWAP Mean Reversion with Momentum Filter
		/// Price mean reversion to VWAP with momentum confirmation
		/// </summary>
		public static patternFunctionResponse CheckVWAPMeanReversion(Strategy strategy)
		{
			try
			{
				// This requires a VWAP indicator - check if available
				// For now, use a simple moving average as proxy
				var vwapProxy = strategy.SMA(20)[0];
				var close = strategy.Close[0];
				var rsi = strategy.RSI(14, 3)[0];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 20)[0];
				var atr = strategy.ATR(14)[0];
				
				double distanceFromVWAP = (close - vwapProxy) / vwapProxy * 100;
				
				// Bullish mean reversion: Price below VWAP, RSI oversold, volume spike
				if (distanceFromVWAP < -1.5 && rsi < 40 && volume > volumeMA * 1.3)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterLong,
						signalType = "VWAP_REVERSION",
						signalDefinition = "Distance from VWAP < -1.5% && RSI < 40 && Volume > VolumeMA * 1.3",
						recStop = close - (2 * atr),
						recTarget = vwapProxy,
						signalScore = 0.65,
						recQty = 1,
						patternId = $"VWAP_REV_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BULLISH_REVERSION",
						signalFeatures = CollectSignalFeatures(strategy, "VWAP_REVERSION")
					};
				}
				
				// Bearish mean reversion: Price above VWAP, RSI overbought, volume spike
				if (distanceFromVWAP > 1.5 && rsi > 60 && volume > volumeMA * 1.3)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterShort,
						signalType = "VWAP_REVERSION",
						signalDefinition = "Distance from VWAP > 1.5% && RSI > 60 && Volume > VolumeMA * 1.3",
						recStop = close + (2 * atr),
						recTarget = vwapProxy,
						signalScore = 0.65,
						recQty = 1,
						patternId = $"VWAP_REV_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BEARISH_REVERSION",
						signalFeatures = CollectSignalFeatures(strategy, "VWAP_REVERSION")
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in VWAP Mean Reversion: {ex.Message}");
			}
			
			return null;
		}
		
		/// <summary>
		/// Bollinger Band Squeeze Strategy
		/// Low volatility followed by directional breakout
		/// </summary>
		public static patternFunctionResponse CheckBollingerSqueeze(Strategy strategy)
		{
			try
			{
				// Ensure we have enough bars
				if (strategy.CurrentBar < 20) return null;
				
				var bb = strategy.Bollinger(2, 20);
				var bbUpper = bb.Upper[0];
				var bbLower = bb.Lower[0];
				var bbMiddle = bb.Middle[0];
				var close = strategy.Close[0];
				var volume = strategy.Volume[0];
				var volumeMA = strategy.SMA(strategy.Volume, 20)[0];
				var atr = strategy.ATR(14)[0];
				
				// Calculate band width (volatility measure)
				double bandWidth = (bbUpper - bbLower) / bbMiddle * 100;
				double bandWidthMA = 0;
				for (int i = 1; i <= 10; i++)
				{
					bandWidthMA += (bb.Upper[i] - bb.Lower[i]) / bb.Middle[i] * 100;
				}
				bandWidthMA /= 10;
				
				// Squeeze condition: Current band width is below average (low volatility)
				bool isSqueeze = bandWidth < bandWidthMA * 0.8;
				
				// Bullish breakout from squeeze
				if (isSqueeze && close > bbUpper && volume > volumeMA * 1.4)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterLong,
						signalType = "BB_SQUEEZE",
						signalDefinition = "BandWidth < AvgBandWidth * 0.8 && Close > BBUpper && Volume > VolumeMA * 1.4",
						recStop = bbMiddle,
						recTarget = close + (3 * atr),
						signalScore = 0.85,
						recQty = 1,
						patternId = $"BB_SQUEEZE_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BULLISH_SQUEEZE_BREAKOUT",
						signalFeatures = CollectSignalFeatures(strategy, "BB_SQUEEZE")
					};
				}
				
				// Bearish breakdown from squeeze
				if (isSqueeze && close < bbLower && volume > volumeMA * 1.4)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterShort,
						signalType = "BB_SQUEEZE",
						signalDefinition = "BandWidth < AvgBandWidth * 0.8 && Close < BBLower && Volume > VolumeMA * 1.4",
						recStop = bbMiddle,
						recTarget = close - (3 * atr),
						signalScore = 0.85,
						recQty = 1,
						patternId = $"BB_SQUEEZE_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BEARISH_SQUEEZE_BREAKDOWN",
						signalFeatures = CollectSignalFeatures(strategy, "BB_SQUEEZE")
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in Bollinger Squeeze: {ex.Message}");
			}
			
			return null;
		}
		
		public static patternFunctionResponse CheckEMAVWAPCross(Strategy strategy)
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
				if (crossedAbove && (ema3[0] - vwap[0]) > (tickSize * 3) && isRising)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterLong,
						signalType = "EMA_VWAP_CROSS",
						signalDefinition = "CrossAbove(EMA3, VWAP1, 10) && EMA3[0] - VWAP1[0] > TickSize * 3 && IsRising(EMA3)",
						recStop = close - (2 * atr), // Conservative stop
						recTarget = close + (3 * atr), // 1.5:1 R/R
						signalScore = 0.85,
						recQty = 1,
						patternId = $"EMA_VWAP_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BULLISH_CROSS",
						signalFeatures = new Dictionary<string, double>
						{
							["ema3_value"] = ema3[0],
							["vwap_value"] = vwap[0],
							["ema_vwap_distance"] = ema3[0] - vwap[0],
							["ema_vwap_distance_ticks"] = (ema3[0] - vwap[0]) / tickSize,
							["close_price"] = close,
							["volume"] = strategy.Volume[0],
							["hour_of_day"] = strategy.Time[0].Hour,
							["signal_score"] = 0.85,
							["is_ema_rising"] = isRising ? 1.0 : 0.0,
							["atr_14"] = atr,
							["rsi_14"] = strategy.RSI(14, 3)[0]
						}
					};
				}
				
				// Short signal: CrossBelow(EMA3,VWAP1,10) && EMA3[0] - VWAP1[0] > TickSize*3 && IsFalling(EMA3)
				// Note: The original condition seems wrong for short (should be VWAP - EMA3 > threshold), but keeping as-is for compatibility
				if (crossedBelow && (ema3[0] - vwap[0]) > (tickSize * 3) && isFalling)
				{
					return new patternFunctionResponse
					{
						newSignal = FunctionResponses.EnterShort,
						signalType = "EMA_VWAP_CROSS", 
						signalDefinition = "CrossBelow(EMA3, VWAP1, 10) && EMA3[0] - VWAP1[0] > TickSize * 3 && IsFalling(EMA3)",
						recStop = close + (2 * atr), // Conservative stop
						recTarget = close - (3 * atr), // 1.5:1 R/R
						signalScore = 0.85,
						recQty = 1,
						patternId = $"EMA_VWAP_{strategy.Time[0]:yyyyMMdd_HHmmss}",
						patternSubType = "BEARISH_CROSS",
						signalFeatures = new Dictionary<string, double>
						{
							["ema3_value"] = ema3[0],
							["vwap_value"] = vwap[0],
							["ema_vwap_distance"] = ema3[0] - vwap[0],
							["ema_vwap_distance_ticks"] = (ema3[0] - vwap[0]) / tickSize,
							["close_price"] = close,
							["volume"] = strategy.Volume[0],
							["hour_of_day"] = strategy.Time[0].Hour,
							["signal_score"] = 0.85,
							["is_ema_falling"] = isFalling ? 1.0 : 0.0,
							["atr_14"] = atr,
							["rsi_14"] = strategy.RSI(14, 3)[0]
						}
					};
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in EMA VWAP Cross: {ex.Message}");
			}
			
			return null;
		}

		
		/// <summary>
		/// Collect comprehensive signal features for ML training
		/// </summary>
		private static Dictionary<string, double> CollectSignalFeatures(Strategy strategy, string signalType)
		{
			var features = new Dictionary<string, double>();
			
			try
			{
				// Basic OHLCV
				features["close"] = strategy.Close[0];
				features["high"] = strategy.High[0];
				features["low"] = strategy.Low[0];
				features["open"] = strategy.Open[0];
				features["volume"] = strategy.Volume[0];
				
				// Technical indicators
				if (strategy.EMA(9).Count > 0) features["ema_9"] = strategy.EMA(9)[0];
				if (strategy.EMA(21).Count > 0) features["ema_21"] = strategy.EMA(21)[0];
				if (strategy.EMA(50).Count > 0) features["ema_50"] = strategy.EMA(50)[0];
				if (strategy.RSI(14, 3).Count > 0) features["rsi_14"] = strategy.RSI(14, 3)[0];
				if (strategy.ATR(14).Count > 0) features["atr_14"] = strategy.ATR(14)[0];
				
				// Volume indicators
				if (strategy.SMA(strategy.Volume, 20).Count > 0) 
					features["volume_sma_20"] = strategy.SMA(strategy.Volume, 20)[0];
				
				// Bollinger Bands
				var bb = strategy.Bollinger(2, 20);
				if (bb.Count > 0)
				{
					features["bb_upper"] = bb.Upper[0];
					features["bb_middle"] = bb.Middle[0];
					features["bb_lower"] = bb.Lower[0];
					features["bb_position"] = (strategy.Close[0] - bb.Lower[0]) / (bb.Upper[0] - bb.Lower[0]);
				}
				
				// Price momentum
				if (strategy.CurrentBar >= 5)
				{
					features["price_change_5"] = strategy.Close[0] - strategy.Close[5];
					features["price_change_pct_5"] = (strategy.Close[0] - strategy.Close[5]) / strategy.Close[5];
				}
				
				// Market timing
				features["hour_of_day"] = strategy.Time[0].Hour;
				features["day_of_week"] = (double)strategy.Time[0].DayOfWeek;
				features["minute_of_hour"] = strategy.Time[0].Minute;
				
				// Signal-specific features
				features["signal_type_numeric"] = GetSignalTypeNumeric(signalType);
				
				// Position and account context
				features["position_quantity"] = strategy.Position.Quantity;
				
				// Volatility measures
				if (strategy.CurrentBar >= 20)
				{
					double sum = 0;
					for (int i = 1; i <= 20; i++)
					{
						sum += Math.Abs(strategy.Close[i] - strategy.Close[i + 1]);
					}
					features["avg_true_range_20"] = sum / 20;
				}
				
				// Order Flow Features
				var close = strategy.Close[0];
				var open = strategy.Open[0];
				var high = strategy.High[0];
				var low = strategy.Low[0];
				var volume = strategy.Volume[0];
				
				// Current bar volume delta
				double volumeDelta = 0;
				if (close > open && high > low)
				{
					double bullishRatio = (close - open) / (high - low);
					double buyVolume = volume * (0.5 + bullishRatio * 0.5);
					volumeDelta = buyVolume - (volume - buyVolume);
				}
				else if (close < open && high > low)
				{
					double bearishRatio = (open - close) / (high - low);
					double sellVolume = volume * (0.5 + bearishRatio * 0.5);
					volumeDelta = (volume - sellVolume) - sellVolume;
				}
				features["volume_delta"] = volumeDelta;
				features["volume_delta_pct"] = volume > 0 ? volumeDelta / volume : 0;
				
				// Cumulative volume delta (5 bars)
				double cumulativeDelta = volumeDelta;
				for (int i = 1; i <= 4 && strategy.CurrentBar >= i; i++)
				{
					var pastClose = strategy.Close[i];
					var pastOpen = strategy.Open[i];
					var pastHigh = strategy.High[i];
					var pastLow = strategy.Low[i];
					var pastVolume = strategy.Volume[i];
					
					if (pastClose > pastOpen && pastHigh > pastLow)
					{
						double pastBullishRatio = (pastClose - pastOpen) / (pastHigh - pastLow);
						double pastBuyVol = pastVolume * (0.5 + pastBullishRatio * 0.5);
						cumulativeDelta += (pastBuyVol - (pastVolume - pastBuyVol));
					}
					else if (pastClose < pastOpen && pastHigh > pastLow)
					{
						double pastBearishRatio = (pastOpen - pastClose) / (pastHigh - pastLow);
						double pastSellVol = pastVolume * (0.5 + pastBearishRatio * 0.5);
						cumulativeDelta += ((pastVolume - pastSellVol) - pastSellVol);
					}
				}
				features["cumulative_delta_5"] = cumulativeDelta;
				
				// Buying/Selling pressure indicators
				features["buying_pressure"] = close > open ? (close - open) / (high - low) : 0;
				features["selling_pressure"] = close < open ? (open - close) / (high - low) : 0;
				
				// Volume profile indicators
				features["volume_at_close"] = volume * Math.Abs(close - open) / (high - low);
				features["volume_above_vwap"] = close > strategy.SMA(20)[0] ? volume : 0;
				features["volume_below_vwap"] = close < strategy.SMA(20)[0] ? volume : 0;
				
				// Tick analysis (approximation)
				features["uptick_volume"] = close > strategy.Close[1] ? volume : 0;
				features["downtick_volume"] = close < strategy.Close[1] ? volume : 0;
				
				// Order flow momentum
				if (strategy.CurrentBar >= 3)
				{
					double volumeRatio3 = strategy.Volume[0] / ((strategy.Volume[1] + strategy.Volume[2] + strategy.Volume[3]) / 3);
					features["volume_spike_3bar"] = volumeRatio3;
				}
				
				// Gold-specific volatility features
				features["price_velocity"] = strategy.CurrentBar >= 3 ? 
					Math.Abs(strategy.Close[0] - strategy.Close[3]) / 3 : 0;
				features["range_expansion"] = (high - low) / strategy.ATR(14)[0];
				features["gap_from_previous"] = Math.Abs(open - strategy.Close[1]);
				
				// Market structure
				features["higher_high"] = high > strategy.High[1] ? 1.0 : 0.0;
				features["lower_low"] = low < strategy.Low[1] ? 1.0 : 0.0;
				features["inside_bar"] = (high < strategy.High[1] && low > strategy.Low[1]) ? 1.0 : 0.0;
				
				// Wick analysis features - critical for gold
				double totalRange = high - low;
				if (totalRange > 0)
				{
					double body = Math.Abs(close - open);
					double upperWick = high - Math.Max(open, close);
					double lowerWick = Math.Min(open, close) - low;
					
					features["body_ratio"] = body / totalRange;
					features["upper_wick_ratio"] = upperWick / totalRange;
					features["lower_wick_ratio"] = lowerWick / totalRange;
					features["wick_to_body_ratio"] = body > 0 ? (upperWick + lowerWick) / body : 10;
					features["wick_imbalance"] = (upperWick - lowerWick) / totalRange; // Positive = upper wick larger
					features["total_wick_ratio"] = (upperWick + lowerWick) / totalRange;
					
					// Candle patterns
					features["is_doji"] = body / totalRange < 0.1 ? 1.0 : 0.0;
					features["is_hammer"] = (lowerWick > body * 2 && upperWick < body * 0.3) ? 1.0 : 0.0;
					features["is_shooting_star"] = (upperWick > body * 2 && lowerWick < body * 0.3) ? 1.0 : 0.0;
					features["is_marubozu"] = (upperWick < totalRange * 0.05 && lowerWick < totalRange * 0.05) ? 1.0 : 0.0;
				}
				
				// Multi-bar wick patterns
				if (strategy.CurrentBar >= 3)
				{
					double wickTrend = 0;
					for (int i = 0; i < 3; i++)
					{
						var pastHigh = strategy.High[i];
						var pastLow = strategy.Low[i];
						var pastOpen = strategy.Open[i];
						var pastClose = strategy.Close[i];
						var pastRange = pastHigh - pastLow;
						
						if (pastRange > 0)
						{
							double pastUpperWick = pastHigh - Math.Max(pastOpen, pastClose);
							double pastLowerWick = Math.Min(pastOpen, pastClose) - pastLow;
							wickTrend += (pastUpperWick - pastLowerWick) / pastRange;
						}
					}
					features["wick_trend_3bar"] = wickTrend / 3;
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error collecting features: {ex.Message}");
			}
			
			return features;
		}
		
		/// <summary>
		/// Convert signal type to numeric for ML training
		/// </summary>
		private static double GetSignalTypeNumeric(string signalType)
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
		/// Main strategy dispatcher - checks all traditional strategies
		/// This is the method called from BuildNewSignal()
		/// </summary>
		public static patternFunctionResponse CheckAllTraditionalStrategies(Strategy strategy, TraditionalStrategyType strategyFilter = TraditionalStrategyType.ALL)
		{
			try
			{
				// Single strategy testing for pure training data
				switch (strategyFilter)
				{
					case TraditionalStrategyType.ORDER_FLOW_IMBALANCE:
						return CheckOrderFlowImbalance(strategy);
					
					case TraditionalStrategyType.BOLLINGER_SQUEEZE:
						return CheckBollingerSqueeze(strategy);
					
					case TraditionalStrategyType.EMA_VWAP_CROSS:
						return CheckEMAVWAPCross(strategy);
					
					case TraditionalStrategyType.BREAKOUT:
						return CheckBreakoutStrategy(strategy);
					
					case TraditionalStrategyType.EMA_CROSSOVER:
						return CheckEMACrossover(strategy);
					
					case TraditionalStrategyType.RSI_DIVERGENCE:
						return CheckRSIDivergence(strategy);
					
					case TraditionalStrategyType.VWAP_MEAN_REVERSION:
						return CheckVWAPMeanReversion(strategy);
					
					case TraditionalStrategyType.ALL:
					default:
						// Check each strategy in order of priority/strength
						// Order Flow Imbalance has highest priority for 1-minute gold
						var signal = CheckOrderFlowImbalance(strategy);
						if (signal != null) return signal;
						
						signal = CheckBollingerSqueeze(strategy);
						if (signal != null) return signal;
						
						signal = CheckEMAVWAPCross(strategy);
						if (signal != null) return signal;
						
						signal = CheckBreakoutStrategy(strategy);
						if (signal != null) return signal;
						
						signal = CheckEMACrossover(strategy);
						if (signal != null) return signal;
						
						signal = CheckRSIDivergence(strategy);
						if (signal != null) return signal;
						
						signal = CheckVWAPMeanReversion(strategy);
						if (signal != null) return signal;
						break;
				}
			}
			catch (Exception ex)
			{
				strategy.Print($"[TRADITIONAL] Error in CheckAllTraditionalStrategies: {ex.Message}");
			}
			
			return null;
		}
	}
}