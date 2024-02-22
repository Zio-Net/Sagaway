using System.Text.Json.Serialization;

namespace SagaReservationDemo.ReservationManager.Actors.BillingDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BillingRequestActionType
{
    Charge,
    Refund
}