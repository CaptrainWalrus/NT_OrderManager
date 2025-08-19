#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// Portable API service for relaying trade data to user's DigitalOcean droplet
    /// Designed to be lightweight and deployable anywhere (Vercel, Lambda, Heroku, etc.)
    /// </summary>
    public class DropletService : IDisposable
    {
        private readonly HttpClient client;
        private readonly string dropletEndpoint;
        private readonly string apiKey;
        private readonly Action<string> logger;
        private bool disposed = false;

        public DropletService(string endpoint, string key, Action<string> logAction = null)
        {
            dropletEndpoint = endpoint?.TrimEnd('/') ?? "https://user-droplet.digitalocean.com";
            apiKey = key ?? "";
            logger = logAction ?? ((msg) => { }); // Default no-op logger
            
            client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30); // Increased timeout for cloud relay cold starts
            
            // Set authorization header if API key provided
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }
            
            Log("DropletService initialized");
        }

        private void Log(string message)
        {
            logger?.Invoke($"[CLOUD-RELAY] {DateTime.Now:HH:mm:ss} {message}");
        }

        /// <summary>
        /// Send standardized trade outcome data to droplet
        /// Following YourTradeJournal vocabulary: processAdapterGenerated for standard format
        /// </summary>
        public async Task<bool> SendStandardizedOutcome(PositionOutcomeData outcomeData, string instrument, string direction, string entryType, string sessionId = null)
        {
            if (disposed) return false;

            try
            {
                var payload = new
                {
                    action = "processAdapterGenerated",
                    adapterType = "standardized",
                    data = new
                    {
                        instrument = instrument,
                        direction = direction,
                        entryType = entryType,
                        sessionId = sessionId, // Add sessionID to data payload
                        exitPrice = outcomeData.ExitPrice,
                        pnlDollars = outcomeData.PnLDollars,
                        pnlPoints = outcomeData.PnLPoints,
                        holdingBars = outcomeData.HoldingBars,
                        exitReason = outcomeData.ExitReason,
                        entryTime = outcomeData.EntryTime,
                        exitTime = outcomeData.ExitTime,
                        profitByBar = outcomeData.profitByBar
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                return await SendToDroplet("/api/process", payload);
            }
            catch (Exception ex)
            {
                Log($"Error sending standardized outcome: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send custom trade data to droplet with user-defined format
        /// Following YourTradeJournal vocabulary: processAdapterGenerated for custom format
        /// </summary>
        public async Task<bool> SendCustomOutcome(Dictionary<string, object> customData, string customAdapterType = "custom")
        {
            if (disposed) return false;

            try
            {
                var payload = new
                {
                    action = "processAdapterGenerated", 
                    adapterType = customAdapterType,
                    data = customData,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                return await SendToDroplet("/api/process", payload);
            }
            catch (Exception ex)
            {
                Log($"Error sending custom outcome: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sync session data with droplet for coordination
        /// Following YourTradeJournal vocabulary: syncSessionRemote
        /// </summary>
        public async Task<bool> SyncSession(string sessionId, Dictionary<string, object> sessionData)
        {
            if (disposed) return false;

            try
            {
                var payload = new
                {
                    action = "syncSessionRemote",
                    sessionId = sessionId,
                    data = sessionData,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                return await SendToDroplet("/api/session/sync", payload);
            }
            catch (Exception ex)
            {
                Log($"Error syncing session: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send session configuration (risk management parameters) to cloud relay
        /// </summary>
        public async Task<bool> SendSessionConfig(string sessionId, Dictionary<string, object> configData)
        {
            if (disposed) return false;

            try
            {
                var payload = new
                {
                    sessionId = sessionId,
                    config = configData,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                return await SendToDroplet("/api/session-config", payload);
            }
            catch (Exception ex)
            {
                Log($"Error sending session config: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Core method to send data to user's droplet
        /// </summary>
        private async Task<bool> SendToDroplet(string endpoint, object payload)
        {
            try
            {
                var json = JsonConvert.SerializeObject(payload, Formatting.None);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                Log($"Sending to {dropletEndpoint}{endpoint}");
                
                var response = await client.PostAsync($"{dropletEndpoint}{endpoint}", content);
                
                if (response.IsSuccessStatusCode)
                {
                    Log($"Successfully sent data to droplet");
                    return true;
                }
                else
                {
                    Log($"Droplet returned {response.StatusCode}: {response.ReasonPhrase}");
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                Log($"Network error sending to droplet: {ex.Message}");
                Log($"Inner exception: {ex.InnerException?.Message ?? "None"}");
                Log($"Target endpoint: {dropletEndpoint}{endpoint}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                Log($"Timeout sending to droplet: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Unexpected error sending to droplet: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            
            disposed = true;
            client?.Dispose();
            Log("DropletService disposed");
        }
    }
}