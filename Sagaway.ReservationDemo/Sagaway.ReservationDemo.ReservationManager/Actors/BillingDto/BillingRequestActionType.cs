using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.ReservationManager.Actors.BillingDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BillingRequestActionType
{
    Charge,
    Refund
}