using System.Text.Json.Serialization;

namespace Sagaway.ReservationDemo.BillingManagement;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Charge,
    Refund
}