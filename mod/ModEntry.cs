using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Threading;
using System.Diagnostics;

namespace ItemSpawnMenuMod
{
    public class ModEntry : Mod
    {
        private string sessionId;
        private readonly string serverUrl = "https://stardewmenu-bxh8fzbpacfrfhdx.westus2-01.azurewebsites.net";
        private string UUID_CLIENT;
        private ClientWebSocket wsClient;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnGameEnd;
        }

        private async void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            await StartSession();
        }

        private async void OnGameEnd(object sender, ReturnedToTitleEventArgs e)
        {
            await EndSession();
            await wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }

        private async Task StartSession()
        {
            using HttpClient client = new HttpClient();
            var response = await client.PostAsync($"{serverUrl}/session/start", null);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                UUID_CLIENT = ExtractUUID(responseContent);
                if (UUID_CLIENT != null)
                {
                    sessionId = UUID_CLIENT;
                    OpenWebPage();
                    InitializeWebSocket();
                }
                else
                {
                    Monitor.Log("Invalid session response: UUID is missing", LogLevel.Error);
                }
            }
            else
            {
                Monitor.Log($"Failed to start session: {response.StatusCode} {response.ReasonPhrase}", LogLevel.Error);
            }
        }

        private string ExtractUUID(string responseContent)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(responseContent))
                {
                    if (doc.RootElement.TryGetProperty("UUID", out JsonElement uuidElement))
                    {
                        return uuidElement.GetString();
                    }
                    else
                    {
                        Monitor.Log("UUID key not found in the response.", LogLevel.Error);
                        return null;
                    }
                }
            }
            catch (JsonException ex)
            {
                Monitor.Log($"Error parsing JSON: {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        private async Task EndSession()
        {
            using HttpClient client = new HttpClient();
            var content = new StringContent(JsonSerializer.Serialize(new { UUID = sessionId }), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{serverUrl}/session/end", content);
            if (response.IsSuccessStatusCode)
            {
                Monitor.Log("Session ended successfully", LogLevel.Info);
            }
            else
            {
                Monitor.Log("Failed to end session", LogLevel.Error);
            }
        }

        private void OpenWebPage()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"{serverUrl}/?UUID={UUID_CLIENT}",
                UseShellExecute = true
            });
        }

        public async Task SpawnItem(string itemId, int quantity = 1)
        {
            Monitor.Log($"Attempting to spawn item: {itemId} (x{quantity})", LogLevel.Info);

            // Validate itemId and map to in-game ID if necessary
            if (string.IsNullOrEmpty(itemId))
            {
                Monitor.Log("Invalid item ID", LogLevel.Warn);
                return;
            }

            // Assuming the mapping function and item spawning works
            var item = new StardewValley.Object(itemId, quantity); // Replace with actual item ID logic
            Game1.player.addItemToInventory(item);

            Monitor.Log($"Successfully spawned item: {itemId} (x{quantity})", LogLevel.Info);
        }

        private async void InitializeWebSocket()
        {
            wsClient = new ClientWebSocket();

            try
            {
                Uri wsUri = new Uri($"wss://stardewmenu-bxh8fzbpacfrfhdx.westus2-01.azurewebsites.net/?UUID={UUID_CLIENT}");
                await wsClient.ConnectAsync(wsUri, CancellationToken.None);
                Monitor.Log("Connected to WebSocket server", LogLevel.Info);

                // Start receiving messages
                ReceiveMessages();
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error connecting to WebSocket: {ex.Message}", LogLevel.Error);
            }
        }

        private async void ReceiveMessages()
        {
            var buffer = new byte[1024];

            while (wsClient.State == WebSocketState.Open)
            {
                try
                {
                    var result = await wsClient.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        // Log the raw incoming WebSocket message
                        Monitor.Log($"Received WebSocket message: {message}", LogLevel.Info);

                        // Check if the message contains 'spawnItem'
                        if (message.Contains("spawnItem"))
                        {
                            // Deserialize the message into a JSON object
                            var itemData = JsonSerializer.Deserialize<ItemSpawnMessage>(message);

                            if (itemData != null)
                            {
                                // Log the item data
                                Monitor.Log($"Spawning item: {itemData.ItemId} (x{itemData.Quantity})", LogLevel.Info);

                                // Call SpawnItem with the item data
                                await SpawnItem(itemData.ItemId, itemData.Quantity);
                            }
                            else
                            {
                                Monitor.Log("Failed to deserialize item data", LogLevel.Error);
                            }
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        Monitor.Log("WebSocket closed", LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    Monitor.Log($"Error while receiving WebSocket message: {ex.Message}", LogLevel.Error);
                }
            }
        }
    }

    public class ItemSpawnMessage
    {
        public string ItemId { get; set; }
        public int Quantity { get; set; }
    }
}
