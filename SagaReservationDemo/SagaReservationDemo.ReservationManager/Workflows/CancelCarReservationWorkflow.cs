using System.Reactive;
using Dapr.Workflow;
using DurableTask.Core.Exceptions;
using SagaReservationDemo.ReservationManager.Actors;
using SagaReservationDemo.ReservationManager.Dto.BookingDto;
using SagaReservationDemo.ReservationManager.Dto.ReservationDto;

namespace SagaReservationDemo.ReservationManager.Workflows;

public static class CancelCarReservationWorkflowBuilder
{
    public static IServiceCollection AddCancelCarReservationWorkflow(this IServiceCollection services)
    {
        services.AddDaprWorkflow(options =>
        {
            options.RegisterWorkflow<CancelCarReservationWorkflow>();

            // These are the activities that get invoked by the workflow(s).
            //options.RegisterActivity<NotifyActivity>();
            //options.RegisterActivity<ReserveInventoryActivity>();
            //options.RegisterActivity<ProcessPaymentActivity>();
            //options.RegisterActivity<UpdateInventoryActivity>();
        });
       
        return services;
    }
}

public class CancelCarReservationWorkflow : Workflow<ReservationInfo, CancelCarReservationResult>
{

    public override async Task<CancelCarReservationResult> RunAsync(WorkflowContext context, ReservationInfo reservationInfo)
    {
        int retryCount = 0;
        const int maxRetries = 3;
        bool isCancellationSuccessful = false;

        //we call the activity once, the WF has a built-in retry mechanism, and in the activity we have a retry mechanism as well
        await context.CallActivityAsync(nameof(InitiateCancellationActivity), new { ReservationId = reservationInfo.ReservationId });

        // Wait for a callback or event indicating the cancellation result
        var cancellationResult = await context.WaitForExternalEventAsync<ReservationOperationResult>("booking-cancelled", TimeSpan.FromSeconds(30));
                
                if (cancellationResult.IsSuccess)
                {
                    isCancellationSuccessful = true;
                    break; // Exit loop if cancellation is confirmed
                }
            }
            catch (TimeoutException)
            {
                // Timeout occurred, check the current status of the reservation
                var currentStatus = await context.CallActivityAsync<ReservationState>(nameof(CheckReservationStateActivity), reservationInfo.ReservationId);
                if (!currentStatus.IsReserved)
                {
                    isCancellationSuccessful = true;
                    break; // Exit loop if reservation is confirmed to be cancelled or not found
                }
                // If status is still active or pending, loop will continue to retry cancellation
            }
        }

        if (!isCancellationSuccessful)
        {
            // Handle failure after all retries (e.g., rebooking or compensatory actions)
            // This could involve calling an activity to rebook or to notify the user of failure
            return new CancelCarReservationResult { IsSucceeded = false };
        }


        //cansel the inventory reservation
        retryCount = 0;
        isCancellationSuccessful = false;

        while (retryCount < maxRetries && !isCancellationSuccessful)
        {
            retryCount++;
            try
            {
                // Attempt to cancel the inventory reservation
                await context.CallActivityAsync(nameof(CancelInventoryReservationActivity),
                    new { ReservationId = reservationInfo.ReservationId });

                // Wait for a callback or event indicating the cancellation result
                var cancellationResult =
                    await context.WaitForExternalEventAsync<ReservationOperationResult>("inventory-cancelled",
                        TimeSpan.FromSeconds(30));

                if (cancellationResult.IsSuccess)
                {
                    isCancellationSuccessful = true;
                    break; // Exit loop if cancellation is confirmed
                }
            }
            catch (TimeoutException)
            {
                // Timeout occurred, check the current status of the reservation
                var currentStatus = await context.CallActivityAsync<ReservationState>(nameof(CheckReservationStateActivity), reservationInfo.ReservationId);
                if (!currentStatus.IsReserved)
                {
                    isCancellationSuccessful = true;
                    break; // Exit loop if reservation is confirmed to be cancelled or not found
                }
                // If status is still active or pending, loop will continue to retry cancellation
            }
        }

        if (!isCancellationSuccessful)
        {
            // Handle failure after all retries (e.g., rebooking or compensatory actions)
            // This could involve calling an activity to rebook or to notify the user of failure

            return new CancelCarReservationResult { IsSucceeded = false };
        }

        // End the workflow with a success result
        return new CancelCarReservationResult() { IsSucceeded = true };
    }
}