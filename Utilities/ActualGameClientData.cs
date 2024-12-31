using Newtonsoft.Json;

namespace SurvivalBackend.Utilities
{
    public static class ActualGameClientData
    {
        #region Structs

        public class GameClientData
        {
            public required string GameClientVersion { get; set; }
        }

        #endregion

        public static string GetCurrentGameClientVersion()
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "currentgameclientversion.json");
            var jsonData = File.ReadAllText(filePath);

            var gameClientData = JsonConvert.DeserializeObject<GameClientData>(jsonData);

            return gameClientData == null ? throw new Exception("Failed to desserialize GameClientData!") : gameClientData.GameClientVersion;
        }
    }
}
