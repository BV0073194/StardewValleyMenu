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
using StardewValley.Tools;

namespace BMenu
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
        private Item SpawnItemByQualifiedId(string qualifiedItemId)
        {
            // Use the ItemRegistry to get item metadata
            var itemMetadata = ItemRegistry.GetMetadata(qualifiedItemId);
            if (itemMetadata == null)
            {
                Monitor.Log($"Item with QualifiedItemId '{qualifiedItemId}' not found.", LogLevel.Error);
                return null;
            }

            // Create an instance of the item
            return itemMetadata.CreateItem();
        }

        public async Task SpawnItem(string itemId, int quantity = 1)
        {
            Monitor.Log($"Attempting to spawn item: {itemId} (x{quantity})", LogLevel.Info);

            // Validate itemId and quantity
            if (string.IsNullOrEmpty(itemId) || quantity <= 0)
            {
                Monitor.Log("Invalid item ID or quantity", LogLevel.Warn);
                return;
            }

            // Spawn the base item
            Item spawnedItem = SpawnItemByQualifiedId(itemId);

            if (spawnedItem != null)
            {
                // Check if the item is stackable
                if (spawnedItem is StardewValley.Object obj && obj.maximumStackSize() > 1)
                {
                    // Set the stack size to the requested quantity
                    obj.Stack = Math.Min(quantity, obj.maximumStackSize());

                    // Add the item to the player's inventory
                    Game1.player.addItemToInventory(obj);

                    // If more items are needed, spawn additional stacks
                    quantity -= obj.Stack;
                    while (quantity > 0)
                    {
                        StardewValley.Object additionalStack = (StardewValley.Object)obj.getOne();
                        additionalStack.Stack = Math.Min(quantity, obj.maximumStackSize());
                        Game1.player.addItemToInventory(additionalStack);
                        quantity -= additionalStack.Stack;
                    }
                }
                else
                {
                    // Non-stackable items: add multiple copies
                    for (int i = 0; i < quantity; i++)
                    {
                        Game1.player.addItemToInventory(spawnedItem.getOne());
                    }
                }

                Monitor.Log($"Successfully spawned item: {itemId} (x{quantity})", LogLevel.Info);
            }
            else
            {
                Monitor.Log($"Failed to spawn item: {itemId}", LogLevel.Error);
            }
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
                            try
                            {
                                // Extract itemId and quantity manually from the JSON message
                                string itemId = ExtractJsonValue(message, "itemId");
                                string quantityStr = ExtractJsonValue(message, "quantity");

                                if (!string.IsNullOrEmpty(itemId) && int.TryParse(quantityStr, out int quantity))
                                {
                                    // Log the extracted item data
                                    Monitor.Log($"Spawning item: {itemId} (x{quantity})", LogLevel.Info);

                                    // Call SpawnItem with the item data
                                    await SpawnItem(itemId, quantity);
                                }
                                else
                                {
                                    Monitor.Log("Invalid itemId or quantity extracted", LogLevel.Error);
                                }
                            }
                            catch (Exception ex)
                            {
                                Monitor.Log($"Error extracting item data from message: {ex.Message}", LogLevel.Error);
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

        private string ExtractJsonValue(string json, string key)
        {
            try
            {
                // Search for the key in the JSON message
                int keyIndex = json.IndexOf($"\"{key}\":", StringComparison.OrdinalIgnoreCase);
                if (keyIndex == -1)
                    return null;

                // Move to the start of the value after the key
                keyIndex += key.Length + 3;

                // Find the end of the value
                int valueEndIndex = json.IndexOfAny(new char[] { ',', '}' }, keyIndex);

                // Extract the value
                string value = json.Substring(keyIndex, valueEndIndex - keyIndex).Trim('"');

                return value;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error extracting value for key '{key}': {ex.Message}", LogLevel.Error);
                return null;
            }
        }

    }

    public class ItemSpawnMessage
    {
        public string ItemId { get; set; }
        public int Quantity { get; set; }
    }
}
