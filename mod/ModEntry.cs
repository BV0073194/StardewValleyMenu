using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Net;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;

namespace ItemSpawnMenuMod
{
    public class ModEntry : Mod
    {
        private string playerName = "";
        private HttpListener listener;
        private Thread serverThread;
        private string localPath = "Mods/BV0073194/StardewValleyMenu/";

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnGameEnd;
            StartWebServer();
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            playerName = Game1.player.Name;
            OpenWebPage();
        }

        private void OnGameEnd(object sender, ReturnedToTitleEventArgs e)
        {
            StopWebServer();
        }

        private void StartWebServer()
        {
            serverThread = new Thread(async () =>
            {
                listener = new HttpListener();
                try
                {
                    listener.Prefixes.Add("http://*:8080/");
                    listener.Start();
                }
                catch (Exception ex)
                {
                    listener.Prefixes.Add("http://localhost:8080/");
                    listener.Start();
                }

                while (listener.IsListening)
                {
                    try
                    {
                        HttpListenerContext context = await listener.GetContextAsync();
                        _ = Task.Run(async () =>
                        {
                            HttpListenerRequest request = context.Request;
                            HttpListenerResponse response = context.Response;

                            string urlPath = request.Url.AbsolutePath.TrimStart('/');

                            // Check if the request is for a static file
                            if (urlPath.EndsWith(".css") || urlPath.EndsWith(".js") || urlPath.EndsWith(".png") || urlPath.EndsWith(".jpg"))
                            {
                                await ServeStaticFile(urlPath, response);
                            }
                            else
                            {
                                // Handle item spawning from query parameters
                                if (request.QueryString.HasKeys())
                                {
                                    string itemId = request.QueryString["item"];
                                    int quantity = int.TryParse(request.QueryString["quantity"], out int qty) ? qty : 1;

                                    for (int i = 0; i < quantity; i++)
                                    {
                                        Item item = ItemRegistry.Create(itemId);
                                        if (item != null)
                                        {
                                            Game1.player.addItemToInventory(item);
                                        }
                                    }
                                }

                                string responseString = await LoadHtmlFromGitHub();
                                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                                response.ContentLength64 = buffer.Length;
                                response.AddHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
                                response.AddHeader("Pragma", "no-cache");
                                response.AddHeader("Expires", "0");
                                response.AddHeader("Access-Control-Allow-Origin", "*");
                                using var output = response.OutputStream;
                                output.Write(buffer, 0, buffer.Length);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Monitor.Log($"Error handling request: {ex.Message}", LogLevel.Warn);
                    }
                }
            });
            serverThread.IsBackground = true;
            serverThread.Start();
        }

        private async Task ServeStaticFile(string urlPath, HttpListenerResponse response)
        {
            string filePath = Path.Combine(localPath, urlPath);

            if (File.Exists(filePath))
            {
                byte[] buffer = await File.ReadAllBytesAsync(filePath);
                response.ContentLength64 = buffer.Length;

                if (urlPath.EndsWith(".css"))
                {
                    response.ContentType = "text/css";
                }
                else if (urlPath.EndsWith(".js"))
                {
                    response.ContentType = "application/javascript";
                }
                else if (urlPath.EndsWith(".png") || urlPath.EndsWith(".jpg"))
                {
                    response.ContentType = "image/png";
                }

                using var output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
            }
            else
            {
                response.StatusCode = 404;
                using var output = response.OutputStream;
                byte[] buffer = Encoding.UTF8.GetBytes("File not found.");
                output.Write(buffer, 0, buffer.Length);
            }
        }

        private void StopWebServer()
        {
            listener?.Stop();
            listener?.Close();
            serverThread?.Interrupt();
            serverThread = null;
        }

        private async Task<string> LoadHtmlFromGitHub()
        {
            using HttpClient client = new HttpClient();
            string url = "https://stardewmenu-bxh8fzbpacfrfhdx.westus2-01.azurewebsites.net/raw-index";
            return await client.GetStringAsync(url);
        }

        private void OpenWebPage()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "http://localhost:8080/",
                UseShellExecute = true
            });
        }
    }
}
