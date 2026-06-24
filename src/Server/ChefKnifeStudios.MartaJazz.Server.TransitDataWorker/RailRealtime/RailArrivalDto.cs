using System.Text.Json.Serialization;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.RailRealtime;

public class RailArrivalDto
{
    [JsonPropertyName("TRAIN_ID")]
    public string? TrainId { get; set; }

    [JsonPropertyName("LINE")]
    public string? Line { get; set; }

    [JsonPropertyName("LATITUDE")]
    public string? Latitude { get; set; }

    [JsonPropertyName("LONGITUDE")]
    public string? Longitude { get; set; }

    [JsonPropertyName("IS_REALTIME")]
    public string? IsRealtime { get; set; }

    [JsonPropertyName("EVENT_TIME")]
    public string? EventTime { get; set; }

    [JsonPropertyName("STATION")]
    public string? Station { get; set; }

    [JsonPropertyName("NEXT_ARR")]
    public string? NextArr { get; set; }

    [JsonPropertyName("WAITING_SECONDS")]
    public string? WaitingSeconds { get; set; }

    [JsonPropertyName("DESTINATION")]
    public string? Destination { get; set; }

    [JsonPropertyName("DIRECTION")]
    public string? Direction { get; set; }

    [JsonPropertyName("DELAY")]
    public string? Delay { get; set; }
}
