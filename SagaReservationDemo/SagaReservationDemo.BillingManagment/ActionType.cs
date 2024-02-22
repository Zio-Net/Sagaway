using System.Text.Json.Serialization;

namespace SagaReservationDemo.BillingManagement;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Charge,
    Refund
}