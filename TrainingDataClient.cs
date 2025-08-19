using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;

namespace NinjaTrader.NinjaScript.Strategies
{
	/// <summary>
	/// Lightweight HTTP client for sending training data to microservice
	/// Fire-and-forget approach to keep NinjaTrader performance optimal
	/// </summary>
	public class TrainingDataClient
	{
		private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(2) };
		private const string TRAINING_SERVICE_URL = "http://localhost:3011";
		
		private string sessionId;
		private bool isSessionStarted = false;
		
		/// <summary>
		/// Start a new training session
		/// </summary>
		public async Task<bool> StartSession(string sessionId, object metadata = null)
		{
			try
			{
				this.sessionId = sessionId;
				
				var request = new
				{
					sessionId = sessionId,
					metadata = metadata ?? new { instrument = "Unknown", strategy = "MainStrategy" }
				};
				
				var json = JsonConvert.SerializeObject(request);
				var content = new StringContent(json, Encoding.UTF8, "application/json");
				
				var response = await httpClient.PostAsync($"{TRAINING_SERVICE_URL}/api/start-session", content);
				isSessionStarted = response.IsSuccessStatusCode;
				
				if (isSessionStarted)
				{
					Console.WriteLine($"[TRAINING-CLIENT] Started session: {sessionId}");
				}
				else
				{
					Console.WriteLine($"[TRAINING-CLIENT] Failed to start session: {sessionId}");
				}
				
				return isSessionStarted;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[TRAINING-CLIENT] Error starting session: {ex.Message}");
				return false;
			}
		}
		
		/// <summary>
		/// End the current training session and flush data
		/// </summary>
		public async Task<bool> EndSession()
		{
			try
			{
				if (!isSessionStarted || string.IsNullOrEmpty(sessionId))
				{
					return true; // Nothing to end
				}
				
				var request = new { sessionId = this.sessionId };
				var json = JsonConvert.SerializeObject(request);
				var content = new StringContent(json, Encoding.UTF8, "application/json");
				
				var response = await httpClient.PostAsync($"{TRAINING_SERVICE_URL}/api/end-session", content);
				bool success = response.IsSuccessStatusCode;
				
				if (success)
				{
					Console.WriteLine($"[TRAINING-CLIENT] Ended session: {sessionId}");
				}
				else
				{
					Console.WriteLine($"[TRAINING-CLIENT] Failed to end session: {sessionId}");
				}
				
				isSessionStarted = false;
				sessionId = null;
				
				return success;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[TRAINING-CLIENT] Error ending session: {ex.Message}");
				return false;
			}
		}
		
		/// <summary>
		/// Send position outcome to training service (fire-and-forget)
		/// </summary>
		public void SendOutcome(PositionOutcome outcome)
		{
			try
			{
				// Add sessionId to outcome
				var outcomeWithSession = new
				{
					SessionId = this.sessionId ?? "default",
					outcome.SignalType,
					outcome.SignalDefinition,
					// COMMENTED OUT: SignalFeatures mapping
					// SignalFeatures = outcome.EntryFeatures, // Map EntryFeatures to SignalFeatures
					outcome.EntryTime,
					outcome.ExitTime,
					outcome.EntryPrice,
					outcome.ExitPrice,
					outcome.RealizedPnL,
					outcome.MaxProfit,
					outcome.MaxLoss,
					outcome.WinLoss,
					outcome.SignalScore,
					outcome.Instrument,
					outcome.PatternId,
					outcome.PatternSubtype,
					outcome.BacktestId
				};
				
				// Fire-and-forget - don't await
				_ = Task.Run(async () =>
				{
					try
					{
						var json = JsonConvert.SerializeObject(outcomeWithSession);
						var content = new StringContent(json, Encoding.UTF8, "application/json");
						
						await httpClient.PostAsync($"{TRAINING_SERVICE_URL}/api/collect-outcome", content);
						Console.WriteLine($"[TRAINING-CLIENT] Sent outcome: {outcome.SignalType} - {outcome.RealizedPnL:F2}");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[TRAINING-CLIENT] Error sending outcome: {ex.Message}");
					}
				});
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[TRAINING-CLIENT] Error queuing outcome: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Send filtered signal to training service (fire-and-forget)
		/// </summary>
		public void SendFilteredSignal(object filteredSignalData)
		{
			try
			{
				// Add sessionId to filtered signal
				var filteredSignalWithSession = new
				{
					SessionId = this.sessionId ?? "default",
					timestamp = DateTime.UtcNow,
					data = filteredSignalData
				};
				
				// Fire-and-forget - don't await
				_ = Task.Run(async () =>
				{
					try
					{
						var json = JsonConvert.SerializeObject(filteredSignalWithSession);
						var content = new StringContent(json, Encoding.UTF8, "application/json");
						
						await httpClient.PostAsync($"{TRAINING_SERVICE_URL}/api/collect-filtered-signal", content);
						Console.WriteLine($"[TRAINING-CLIENT] Sent filtered signal");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[TRAINING-CLIENT] Error sending filtered signal: {ex.Message}");
					}
				});
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[TRAINING-CLIENT] Error queuing filtered signal: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Check service health
		/// </summary>
		public async Task<bool> IsServiceHealthy()
		{
			try
			{
				var response = await httpClient.GetAsync($"{TRAINING_SERVICE_URL}/health");
				return response.IsSuccessStatusCode;
			}
			catch
			{
				return false;
			}
		}
		
		/// <summary>
		/// Get service statistics
		/// </summary>
		public async Task<string> GetServiceStats()
		{
			try
			{
				var response = await httpClient.GetAsync($"{TRAINING_SERVICE_URL}/api/stats");
				if (response.IsSuccessStatusCode)
				{
					return await response.Content.ReadAsStringAsync();
				}
				return "Service unavailable";
			}
			catch (Exception ex)
			{
				return $"Error: {ex.Message}";
			}
		}
	}
}