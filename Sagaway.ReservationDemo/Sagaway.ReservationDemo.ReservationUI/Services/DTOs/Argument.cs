using System.Text.Json.Nodes;

namespace Sagaway.ReservationDemo.ReservationUI.Services.DTOs;

public record Argument
{
    public string Sender { get; set; } = string.Empty;
    public JsonObject Text { get; set; } = new JsonObject();
}