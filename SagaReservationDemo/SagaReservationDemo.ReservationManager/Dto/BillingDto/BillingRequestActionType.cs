using System.Text.Json.Serialization;

namespace SagaReservationDemo.ReservationManager.Dto.BillingDto;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BillingRequestActionType
{
    Charge,
    Refund
}