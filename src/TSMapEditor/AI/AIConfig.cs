using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Configuration for the AI chat service.
    /// Loaded from smartalert.local.json in the editor's directory.
    /// </summary>
    public class AIConfig
    {
        private const string ConfigFileName = "smartalert.local.json";

        [JsonPropertyName("apiEndpoint")]
        public string ApiEndpoint { get; set; } = string.Empty;

        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("modelName")]
        public string ModelName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the configuration has the minimum required fields set.
        /// </summary>
        [JsonIgnore]
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiEndpoint) &&
            !string.IsNullOrWhiteSpace(ApiKey) &&
            !string.IsNullOrWhiteSpace(ModelName);

        /// <summary>
        /// Loads the AI configuration from smartalert.local.json.
        /// Returns a default (unconfigured) instance if the file doesn't exist.
        /// </summary>
        public static AIConfig Load()
        {
            string path = Path.Combine(Environment.CurrentDirectory, ConfigFileName);

            if (!File.Exists(path))
                return new AIConfig();

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AIConfig>(json) ?? new AIConfig();
            }
            catch (Exception)
            {
                return new AIConfig();
            }
        }

        /// <summary>
        /// Saves the AI configuration to smartalert.local.json.
        /// </summary>
        public void Save()
        {
            string path = Path.Combine(Environment.CurrentDirectory, ConfigFileName);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(path, json);
        }
    }
}
