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
using NinjaTrader.NinjaScript.DrawingTools;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class OrangeLineIndicator : Indicator
	{
		private HttpClient httpClient;
		private string meServiceUrl = "https://matching-engine-service.onrender.com"; // Default to cloud service
		private DateTime lastApiCall = DateTime.MinValue;
		private double lastOrangeLine = double.NaN;
		private List<double> priceHistory = new List<double>();
		private int maxHistoryLength = 200; // Keep last 200 bars for analysis
		
		// Orange line data storage
		private Series<double> orangeLineSeries;
		private Series<double> deviationSeries;
		private Series<double> confidenceSeries;
		
		// Signal tracking
		private string lastSignal = "NONE";
		private double lastConfidence = 0;
		private DateTime lastSignalTime = DateTime.MinValue;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Orange Line Trend Indicator - Displays noise-filtered trend line and reversion signals";
				Name										= "Orange Line";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				
				// Default parameters
				ApiCallInterval								= 5; // Seconds between API calls
				ShowSignals									= true;
				ShowDeviation								= true;
				AlertOnSignals								= false;
				
				// Visual settings
				OrangeLineColor								= Brushes.Orange;
				OrangeLineWidth								= 2;
				LongSignalColor								= Brushes.Green;
				ShortSignalColor							= Brushes.Red;
				DeviationColor								= Brushes.Yellow;
			}
			else if (State == State.DataLoaded)
			{
				// Initialize HTTP client
				httpClient = new HttpClient();
				httpClient.Timeout = TimeSpan.FromSeconds(10);
				
				// Initialize data series
				orangeLineSeries = new Series<double>(this);
				deviationSeries = new Series<double>(this);
				confidenceSeries = new Series<double>(this);
				
				Print($"Orange Line Indicator initialized for {Instrument.FullName}");
			}
			else if (State == State.Terminated)
			{
				// Cleanup
				httpClient?.Dispose();
			}
		}

		protected override void OnBarUpdate()
		{
			// Build price history
			priceHistory.Add(Close[0]);
			if (priceHistory.Count > maxHistoryLength)
			{
				priceHistory.RemoveAt(0);
			}
			
			// Only call API if enough time has passed and we have sufficient data
			if (priceHistory.Count >= 20 && 
				(DateTime.Now - lastApiCall).TotalSeconds >= ApiCallInterval)
			{
				// Call API in background to avoid blocking
				Task.Run(() => CallOrangeLineAPI());
			}
			
			// Plot the last known orange line value
			if (!double.IsNaN(lastOrangeLine))
			{
				orangeLineSeries[0] = lastOrangeLine;
				
				// Calculate and store deviation
				double deviation = Close[0] - lastOrangeLine;
				deviationSeries[0] = deviation;
				
				// Draw orange line
				Draw.Line(this, $"OrangeLine_{CurrentBar}", false, 0, lastOrangeLine, 1, lastOrangeLine, 
					OrangeLineColor, DashStyleHelper.Solid, OrangeLineWidth);
				
				// Show deviation if enabled
				if (ShowDeviation)
				{
					string deviationText = $"Dev: {deviation:F2}";
					Draw.TextFixed(this, "DeviationText", deviationText, TextPosition.TopLeft, 
						DeviationColor, new SimpleFont("Arial", 10), Brushes.Transparent, Brushes.Transparent, 0);
				}
				
				// Draw signals if enabled
				if (ShowSignals && lastSignal != "NONE" && 
					(DateTime.Now - lastSignalTime).TotalMinutes < 30) // Show signals for 30 minutes
				{
					DrawSignal(lastSignal, lastConfidence);
				}
			}
		}
		
		private async Task CallOrangeLineAPI()
		{
			try
			{
				lastApiCall = DateTime.Now;
				
				// Prepare request data
				var requestData = new
				{
					instrument = GetInstrumentCode(),
					prices = priceHistory.ToArray(),
					currentBar = CurrentBar
				};
				
				string jsonData = JsonConvert.SerializeObject(requestData);
				var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
				
				// Make API call
				string url = $"{meServiceUrl}/api/orange-line/analyze";
				var response = await httpClient.PostAsync(url, content);
				
				if (response.IsSuccessStatusCode)
				{
					string responseJson = await response.Content.ReadAsStringAsync();
					var result = JsonConvert.DeserializeObject<OrangeLineResponse>(responseJson);
					
					// Update indicator data on UI thread
					Dispatcher.BeginInvoke(new Action(() => {
						UpdateIndicatorData(result);
					}));
				}
				else
				{
					Print($"Orange Line API error: {response.StatusCode} - {response.ReasonPhrase}");
				}
			}
			catch (Exception ex)
			{
				Print($"Orange Line API exception: {ex.Message}");
			}
		}
		
		private void UpdateIndicatorData(OrangeLineResponse result)
		{
			if (result?.orangeLine != null)
			{
				lastOrangeLine = result.orangeLine.Value;
				lastSignal = result.signal ?? "NONE";
				lastConfidence = result.confidence;
				lastSignalTime = DateTime.Now;
				
				// Store confidence for plotting
				confidenceSeries[0] = lastConfidence;
				
				// Print signal information
				if (lastSignal != "NONE")
				{
					Print($"Orange Line Signal: {lastSignal} (Confidence: {lastConfidence:P1}, Deviation: {result.deviation:F2})");
					
					// Alert if enabled
					if (AlertOnSignals)
					{
						Alert("OrangeLineSignal", Priority.Medium, 
							$"{lastSignal} signal on {Instrument.FullName}", 
							NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 10, Brushes.Yellow, Brushes.Black);
					}
				}
			}
		}
		
		private void DrawSignal(string signal, double confidence)
		{
			if (signal == "LONG")
			{
				Draw.ArrowUp(this, $"LongSignal_{CurrentBar}", false, 0, Low[0] - TickSize * 5, LongSignalColor);
				Draw.TextFixed(this, "SignalText", $"LONG ({confidence:P1})", TextPosition.TopRight, 
					LongSignalColor, new SimpleFont("Arial", 12), Brushes.Transparent, Brushes.Transparent, 0);
			}
			else if (signal == "SHORT")
			{
				Draw.ArrowDown(this, $"ShortSignal_{CurrentBar}", false, 0, High[0] + TickSize * 5, ShortSignalColor);
				Draw.TextFixed(this, "SignalText", $"SHORT ({confidence:P1})", TextPosition.TopRight, 
					ShortSignalColor, new SimpleFont("Arial", 12), Brushes.Transparent, Brushes.Transparent, 0);
			}
		}
		
		private string GetInstrumentCode()
		{
			// Map NinjaTrader instrument names to our service codes
			string instrumentName = Instrument.FullName;
			
			if (instrumentName.Contains("MGC") || instrumentName.Contains("Micro Gold"))
				return "MGC";
			else if (instrumentName.Contains("GC") || instrumentName.Contains("Gold"))
				return "GC";
			else if (instrumentName.Contains("MES") || instrumentName.Contains("Micro E-mini S&P"))
				return "MES";
			else if (instrumentName.Contains("MNQ") || instrumentName.Contains("Micro E-mini NASDAQ"))
				return "MNQ";
			else
				return "MGC"; // Default to MGC
		}
		
		#region Properties
		
		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name="API Call Interval (seconds)", Description="How often to call the orange line API", Order=1, GroupName="Parameters")]
		public int ApiCallInterval
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show Signals", Description="Display buy/sell signals on chart", Order=2, GroupName="Parameters")]
		public bool ShowSignals
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show Deviation", Description="Display deviation from orange line", Order=3, GroupName="Parameters")]
		public bool ShowDeviation
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Alert on Signals", Description="Play sound alerts for signals", Order=4, GroupName="Parameters")]
		public bool AlertOnSignals
		{ get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Orange Line Color", Description="Color of the orange line", Order=1, GroupName="Visual")]
		public Brush OrangeLineColor
		{ get; set; }
		
		[Browsable(false)]
		public string OrangeLineColorSerializable
		{
			get { return Serialize.BrushToString(OrangeLineColor); }
			set { OrangeLineColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Orange Line Width", Description="Width of the orange line", Order=2, GroupName="Visual")]
		public int OrangeLineWidth
		{ get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Long Signal Color", Description="Color for long signals", Order=3, GroupName="Visual")]
		public Brush LongSignalColor
		{ get; set; }
		
		[Browsable(false)]
		public string LongSignalColorSerializable
		{
			get { return Serialize.BrushToString(LongSignalColor); }
			set { LongSignalColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Short Signal Color", Description="Color for short signals", Order=4, GroupName="Visual")]
		public Brush ShortSignalColor
		{ get; set; }
		
		[Browsable(false)]
		public string ShortSignalColorSerializable
		{
			get { return Serialize.BrushToString(ShortSignalColor); }
			set { ShortSignalColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Deviation Color", Description="Color for deviation text", Order=5, GroupName="Visual")]
		public Brush DeviationColor
		{ get; set; }
		
		[Browsable(false)]
		public string DeviationColorSerializable
		{
			get { return Serialize.BrushToString(DeviationColor); }
			set { DeviationColor = Serialize.StringToBrush(value); }
		}
		
		#endregion
	}
	
	// Data classes for API response
	public class OrangeLineResponse
	{
		public string instrument { get; set; }
		public string timestamp { get; set; }
		public double currentPrice { get; set; }
		public double? orangeLine { get; set; }
		public double deviation { get; set; }
		public string signal { get; set; }
		public double confidence { get; set; }
		public string reason { get; set; }
		public double[] trendData { get; set; }
		public SignalDetails signalDetails { get; set; }
		public MarketConditions marketConditions { get; set; }
	}
	
	public class SignalDetails
	{
		public double entry { get; set; }
		public double profitTarget { get; set; }
		public double stopLoss { get; set; }
		public double positionSize { get; set; }
		public double standardizedDeviation { get; set; }
	}
	
	public class MarketConditions
	{
		public string recommendedAction { get; set; }
		public double trendStrength { get; set; }
		public double volatility { get; set; }
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OrangeLineIndicator[] cacheOrangeLineIndicator;
		public OrangeLineIndicator OrangeLineIndicator(int apiCallInterval, bool showSignals, bool showDeviation, bool alertOnSignals)
		{
			return OrangeLineIndicator(Input, apiCallInterval, showSignals, showDeviation, alertOnSignals);
		}

		public OrangeLineIndicator OrangeLineIndicator(ISeries<double> input, int apiCallInterval, bool showSignals, bool showDeviation, bool alertOnSignals)
		{
			if (cacheOrangeLineIndicator != null)
				for (int idx = 0; idx < cacheOrangeLineIndicator.Length; idx++)
					if (cacheOrangeLineIndicator[idx] != null && cacheOrangeLineIndicator[idx].ApiCallInterval == apiCallInterval && cacheOrangeLineIndicator[idx].ShowSignals == showSignals && cacheOrangeLineIndicator[idx].ShowDeviation == showDeviation && cacheOrangeLineIndicator[idx].AlertOnSignals == alertOnSignals && cacheOrangeLineIndicator[idx].EqualsInput(input))
						return cacheOrangeLineIndicator[idx];
			return CacheIndicator<OrangeLineIndicator>(new OrangeLineIndicator(){ ApiCallInterval = apiCallInterval, ShowSignals = showSignals, ShowDeviation = showDeviation, AlertOnSignals = alertOnSignals }, input, ref cacheOrangeLineIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OrangeLineIndicator OrangeLineIndicator(int apiCallInterval, bool showSignals, bool showDeviation, bool alertOnSignals)
		{
			return indicator.OrangeLineIndicator(Input, apiCallInterval, showSignals, showDeviation, alertOnSignals);
		}

		public Indicators.OrangeLineIndicator OrangeLineIndicator(ISeries<double> input , int apiCallInterval, bool showSignals, bool showDeviation, bool alertOnSignals)
		{
			return indicator.OrangeLineIndicator(input, apiCallInterval, showSignals, showDeviation, alertOnSignals);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OrangeLineIndicator OrangeLineIndicator(int apiCallInterval, bool showSignals, bool showDeviation, bool alertOnSignals)
		{
			return indicator.OrangeLineIndicator(Input, apiCallInterval, showSignals, showDeviation, alertOnSignals);
		}

		public Indicators.OrangeLineIndicator OrangeLineIndicator(ISeries<double> input , int apiCallInterval, bool showSignals, bool showDeviation, bool alertOnSignals)
		{
			return indicator.OrangeLineIndicator(input, apiCallInterval, showSignals, showDeviation, alertOnSignals);
		}
	}
}

#endregion 