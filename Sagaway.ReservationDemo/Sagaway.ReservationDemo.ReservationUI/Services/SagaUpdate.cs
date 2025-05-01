using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Sagaway.ReservationDemo.ReservationUI.Services
{
    public class SagaUpdate
    {
        public Guid ReservationId { get; set; }
        public required string Outcome { get; set; }
        public required string Log { get; set; }
        public required string CustomerName { get; set; }
        public required string CarClass { get; set; }
    }
}
