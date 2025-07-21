#region Using declarations
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaTrader.NinjaScript.Strategies.OrganizedStrategy;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.OrganizedStrategy
{
    /// <summary>
    /// ProjectX API Client for BlueSky integration
    /// Handles authentication, order management, and position queries
    /// </summary>
    public class ProjectXApiClient
    {
        private readonly HttpClient httpClient;
        private string baseUrl = "https://api.blusky.projectx.com";
        private string authToken;
        private int accountId;
        private DateTime tokenExpiry = DateTime.MinValue;

        public ProjectXApiClient()
        {
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("accept", "text/plain");
            httpClient.Timeout = TimeSpan.FromSeconds(30);
        }


        /// <summary>
        /// Authenticate with ProjectX using API key
        /// </summary>
        public async Task<bool> AuthenticateAsync(string userName, string apiKey, int projectXAccountId)
        {
            try
            {
                accountId = projectXAccountId;
                
                var loginRequest = new
                {
                    userName = userName,
                    apiKey = apiKey
                };

                var json = JsonConvert.SerializeObject(loginRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/Auth/loginKey", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = JsonConvert.DeserializeObject<ProjectXAuthResponse>(responseContent);
                    
                    if (authResponse.success)
                    {
                        authToken = authResponse.token;
                        tokenExpiry = DateTime.Now.AddHours(8); // Assume 8-hour token life
                        
                        // Set auth header for future requests
                        httpClient.DefaultRequestHeaders.Authorization = 
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
                            
                        Console.WriteLine($"✅ ProjectX authentication successful for account {accountId}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"❌ ProjectX auth failed: {authResponse.errorMessage}");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine($"❌ ProjectX auth HTTP error: {response.StatusCode} - {responseContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ProjectX authentication exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if token is still valid and refresh if needed
        /// </summary>
        private async Task<bool> EnsureAuthenticated()
        {
            if (string.IsNullOrEmpty(authToken) || DateTime.Now > tokenExpiry.AddMinutes(-30))
            {
                Console.WriteLine("⚠️ ProjectX token expired or missing - re-authentication required");
                return false; // Caller needs to re-authenticate
            }
            return true;
        }

    

        /// <summary>
        /// Place a single order on ProjectX
        /// </summary>
        public async Task<ProjectXOrderResponse> PlaceOrderAsync(ProjectXOrder order)
        {
            try
            {
                if (!await EnsureAuthenticated())
                {
                    return new ProjectXOrderResponse 
                    { 
                        success = false, 
                        errorMessage = "Authentication required",
                        errorCode = 401
                    };
                }

                var json = JsonConvert.SerializeObject(order);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/Order/place", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var orderResponse = JsonConvert.DeserializeObject<ProjectXOrderResponse>(responseContent);
                    
                    if (orderResponse.success)
                    {
                        Console.WriteLine($"✅ ProjectX order placed: {orderResponse.orderId} ({order.customTag})");
                    }
                    else
                    {
                        Console.WriteLine($"❌ ProjectX order failed: {orderResponse.errorMessage} ({order.customTag})");
                    }
                    
                    return orderResponse;
                }
                else
                {
                    Console.WriteLine($"❌ ProjectX order HTTP error: {response.StatusCode} - {responseContent}");
                    return new ProjectXOrderResponse 
                    { 
                        success = false, 
                        errorMessage = $"HTTP {response.StatusCode}: {responseContent}",
                        errorCode = (int)response.StatusCode
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ProjectX order exception: {ex.Message}");
                return new ProjectXOrderResponse 
                { 
                    success = false, 
                    errorMessage = ex.Message,
                    errorCode = 500
                };
            }
        }

        /// <summary>
        /// Cancel an order by ID
        /// </summary>
        public async Task<bool> CancelOrderAsync(long orderId)
        {
            try
            {
                if (!await EnsureAuthenticated())
                    return false;

                var cancelRequest = new { orderId = orderId };
                var json = JsonConvert.SerializeObject(cancelRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/Order/cancel", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var cancelResponse = JsonConvert.DeserializeObject<ProjectXOrderResponse>(responseContent);
                    
                    if (cancelResponse.success)
                    {
                        Console.WriteLine($"✅ ProjectX order cancelled: {orderId}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"❌ ProjectX cancel failed: {cancelResponse.errorMessage}");
                        return false;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ProjectX cancel exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get status of a specific order
        /// </summary>
        public async Task<ProjectXOrderStatus> GetOrderStatusAsync(long orderId)
        {
            try
            {
                if (!await EnsureAuthenticated())
                    return null;

                var searchRequest = new { orderId = orderId };
                var json = JsonConvert.SerializeObject(searchRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/Order/search", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var searchResponse = JsonConvert.DeserializeObject<ProjectXOrderSearchResponse>(responseContent);
                    
                    if (searchResponse.success && searchResponse.orders?.Count > 0)
                    {
                        var order = searchResponse.orders[0];
                        return new ProjectXOrderStatus
                        {
                            orderId = order.id,
                            state = order.orderState,
                            filledQuantity = order.filled,
                            averageFillPrice = order.averageFillPrice,
                            timestamp = order.timestamp
                        };
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ProjectX order status exception: {ex.Message}");
                return null;
            }
        }

    

        /// <summary>
        /// Get all open positions for the account
        /// </summary>
        public async Task<List<ProjectXPosition>> GetOpenPositionsAsync()
        {
            try
            {
                if (!await EnsureAuthenticated())
                    return new List<ProjectXPosition>();

                var searchRequest = new { accountId = accountId };
                var json = JsonConvert.SerializeObject(searchRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/Position/searchOpen", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var positionResponse = JsonConvert.DeserializeObject<ProjectXPositionSearchResponse>(responseContent);
                    
                    if (positionResponse.success)
                    {
                        Console.WriteLine($"📊 ProjectX positions retrieved: {positionResponse.positions?.Count ?? 0}");
                        return positionResponse.positions ?? new List<ProjectXPosition>();
                    }
                    else
                    {
                        Console.WriteLine($"❌ ProjectX positions failed: {positionResponse.errorMessage}");
                    }
                }
                
                return new List<ProjectXPosition>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ProjectX positions exception: {ex.Message}");
                return new List<ProjectXPosition>();
            }
        }

        /// <summary>
        /// Get current market price for a contract
        /// </summary>
        public async Task<ProjectXMarketData> GetCurrentPriceAsync(string contractId)
        {
            try
            {
                if (!await EnsureAuthenticated())
                    return null;

                // Note: This endpoint may vary - using contract search as fallback
                var searchRequest = new { contractId = contractId };
                var json = JsonConvert.SerializeObject(searchRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{baseUrl}/api/Contract/searchById", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Parse market data from contract search response
                    // This is a simplified implementation - may need adjustment based on actual API
                    var contractResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    
                    if (contractResponse.success == true)
                    {
                        return new ProjectXMarketData
                        {
                            contractId = contractId,
                            currentPrice = 0.0, // Extract from response
                            bid = 0.0,
                            ask = 0.0,
                            timestamp = DateTime.Now
                        };
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ProjectX market data exception: {ex.Message}");
                return null;
            }
        }

       


        private class ProjectXAuthResponse
        {
            public string token { get; set; }
            public bool success { get; set; }
            public int errorCode { get; set; }
            public string errorMessage { get; set; }
        }

        private class ProjectXOrderSearchResponse
        {
            public List<ProjectXOrderInfo> orders { get; set; }
            public bool success { get; set; }
            public int errorCode { get; set; }
            public string errorMessage { get; set; }
        }

        private class ProjectXOrderInfo
        {
            public int id { get; set; }
            public string orderState { get; set; }
            public int filled { get; set; }
            public decimal averageFillPrice { get; set; }
            public DateTime timestamp { get; set; }
        }

        private class ProjectXPositionSearchResponse
        {
            public List<ProjectXPosition> positions { get; set; }
            public bool success { get; set; }
            public int errorCode { get; set; }
            public string errorMessage { get; set; }
        }





        public void Dispose()
        {
            httpClient?.Dispose();
        }

    }
}