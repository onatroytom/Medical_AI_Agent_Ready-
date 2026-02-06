using Google.Apis.CustomSearchAPI.v1;
using Google.Apis.Services;
using Google.GenAI;
using Google.GenAI.Types;
using Type = Google.GenAI.Types.Type;

namespace MedicalAgentApp
{
    public static class ConfigKey
    {
        // 1. Get this from https://aistudio.google.com/
        public const string GeminiApiKey = "";

        // 2. Get this from https://console.cloud.google.com/
        public const string GoogleApiKey = "";

        // 3. Get this from https://programmablesearchengine.google.com/
        public const string GoogleSearchEngineId = "";
    }

    public class MedicalSearchTool
    {
        public static async Task<string> GetMedicineDetails(string medicineName)
        {
            try
            {
                var service = new CustomSearchAPIService(new BaseClientService.Initializer
                {
                    ApiKey = ConfigKey.GoogleApiKey
                });

                var request = service.Cse.List();
                request.Cx = ConfigKey.GoogleSearchEngineId;
                request.Q = $"{medicineName} uses side effects and similar medicines";
                request.Num = 3;

                var result = await request.ExecuteAsync();
                if (result.Items == null) return "No online information found for this medicine.";

                var summaries = result.Items.Select(item =>
                    $"Source: {item.Title}\nContent: {item.Snippet}");

                return string.Join("\n\n", summaries);
            }
            catch (Exception ex)
            {
                return $"[Search Tool Error]: {ex.Message}";
            }
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            // Simple validation to prevent the 400 error before it happens
            if (ConfigKey.GeminiApiKey.StartsWith("PASTE"))
            {
                Console.WriteLine("ERROR: Please replace the placeholder API Keys in ConfigKey class.");
                return;
            }

            // 1. Initialize Client
            var client = new Client(apiKey: ConfigKey.GeminiApiKey);
            var searchTool = new MedicalSearchTool();

            // 2. Define Function for Tool Calling
            var searchFunction = new FunctionDeclaration
            {
                Name = "search_medicine_info",
                Description = "Searches Google for medicine details, side effects, and alternatives.",
                Parameters = new Schema
                {
                    Type = Type.OBJECT,
                    Properties = new Dictionary<string, Schema>
                    {
                        { "medicineName", new Schema { Type = Type.STRING, Description = "The name of the medicine." } }
                    },
                    Required = new List<string> { "medicineName" }
                }
            };

            // 3. Configure the Agent
            var config = new GenerateContentConfig
            {
                Tools = [
                    new() { FunctionDeclarations = [searchFunction] }
                ],
                SystemInstruction = new Content
                {
                    Parts = [new() { Text = "You are a helpful medical assistant. Always use the search tool to provide up-to-date info on medicines. Summarize findings clearly." }]
                }
            };

            // 4. Start Chat (Standard syntax for the Google.GenAI SDK)
            var googleAI = new Mscc.GenerativeAI.GoogleAI(ConfigKey.GeminiApiKey);

            // Create a generative model instance for gemini-2.5-flash
            var model = googleAI.GenerativeModel("models/gemini-2.5-flash");

            // --- Start the chat session ---
            var chat = model.StartChat();

            Console.WriteLine("--- Medical AI Agent Ready ---");
            Console.WriteLine("Type a medicine name to begin");

            while (true)
            {
                Console.Write("\nMedicine Name: ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "exit") break;

                try
                {
                    var response = await chat.SendMessage(input);
                    var part = response.Candidates[0].Content.Parts[0];

                    if (part.FunctionCall != null)
                    {
                        var call = part.FunctionCall;
                        Console.WriteLine($"[Agent Action]: Searching for '{call}'...");

                        string searchResult = await MedicalSearchTool.GetMedicineDetails(call.ToString());

                        // Send tool results back to the AI
                        var finalResponse = await chat.SendMessage(new List<Part>
                        {
                            new() {
                                FunctionResponse = new FunctionResponse
                                {
                                    Name = call.Name,
                                    Response = new Dictionary<string, object> { { "content", searchResult } }
                                }
                            }
                        });

                        Console.WriteLine($"\nAssistant: {finalResponse.Text}");
                    }
                    else
                    {
                        Console.WriteLine($"\nAssistant: {response.Text}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[System Error]: {ex.Message}");
                    Console.WriteLine("Hint: Check if your Gemini API key is correct and has 'Generative Language API' enabled.");
                }
            }
        }
    }
}