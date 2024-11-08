using Newtonsoft.Json;

namespace SurvivalBackend.Utilities
{
    public static class ActualGameClientData
    {
        public class GameClientData
        {
            public required string GameClientVersion { get; set; }
        }

        public static string GetCurrentGameClientVersion()
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "currentgameclientversion.json");
            var jsonData = File.ReadAllText(filePath);

            var gameClientData = JsonConvert.DeserializeObject<GameClientData>(jsonData);

            if (gameClientData == null)
            {
                throw new Exception("Failed to desserialize GameClientData!");
            }

            return gameClientData.GameClientVersion;
        }
    }
}
