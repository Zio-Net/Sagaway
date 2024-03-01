# Sagaway - A Distributed Application Saga

## The Saga Pattern

Sagaway embodies the Saga pattern, a sophisticated approach to managing transactions and ensuring consistency within distributed systems. This pattern delineates a sequence of local transactions, each updating the system's state and paving the way for the subsequent step. In the event of a transaction failure, compensating transactions are initiated to reverse the effects of prior operations. Sagas can operate sequentially, where operations follow one another or execute multiple operations simultaneously in parallel.

Implementing Sagas can be straightforward, involving synchronous request-reply interactions with participant services. Yet, embracing asynchronous communication proves superior for optimal integration within a Microservices Architecture (MSA). It entails employing queues or publish/subscribe models and awaiting results. This strategy allows the coordinating service to halt its operations, thereby liberating resources. The orchestration of the Saga resumes once a response is received from any of the participant services, typically through callback mechanisms like queues or a publish/subscribe system. This advanced pattern of Saga management necessitates asynchronous service calls, resource allocation efficiency, and mechanisms to revisit operational states. Additionally, it encompasses handling unacknowledged requests through status checks and retries, along with executing asynchronous compensations.

Despite the inherent complexity, the asynchronous Saga is favored for its contribution to several critical architectural qualities, including scalability, robustness, resilience, high availability, and consistency, all of which are integral to an efficient MSA messaging exchange pattern. Sagaway not only facilitates the creation of straightforward Sagas but also excels by providing a robust foundation for managing asynchronous Sagas, thus embodying the essence of simplicity in software architecture while catering to a broader spectrum of quality attributes.

## The Car Reservation Demo

To understand Saga and the Sagaway framework, first look at the [Sagaway.ReservationDemo](https://github.com/alonf/Sagaway/tree/master/Sagaway.ReservationDemo), which is part of the Sagaway solution.

The Demo Car Reservation System exemplifies a contemporary approach to distributed transaction management within a microservice architecture. At its core, the system is designed to facilitate the reserving and canceling of car bookings while maintaining a consistent and reliable state across various services, each with a distinct responsibility.

The system allows users to reserve cars from an inventory and manage the billing associated with these reservations. Should a user wish to cancel a reservation, the system ensures that the inventory is updated, the reservation is annulled, and any charges are refunded. The complexity of this process lies in the system's distributed nature, where different components need to coordinate state changes consistently despite the potential for network failures, service outages, or other distributed system anomalies.

The intricacy of maintaining a consistent state across services is addressed by implementing Sagas. A Saga is a sequence of local transactions spread across multiple services, with each local transaction updating the system state and triggering the next transaction. If a local transaction in a Saga fails, compensating transactions are triggered to roll back the changes made by previous transactions, ensuring system-wide consistency.

The system is composed of three key services:

1. **Billing Service**: Emulate all financial transactions associated with the car reservations. It ensures that the corresponding charges are processed when a car is reserved. It also takes care of the necessary refunds in the event of a cancellation.

2. **Booking Service**: Manages the reservations themselves. It records the reservation details, such as the customer's name and the class of car reserved. It also ensures that the reservation state is maintained, whether the car is currently reserved or available for others.

3. **Inventory Service**: Keeps track of the cars available for reservation. It updates the inventory in real-time as cars are reserved and released, ensuring that the same car is not double-booked and that reservations are only made for available cars.

The system employs two Sagas:

- **Reservation Saga**: Orchestrates the steps in reserving a car. It begins with sending concurrent requests for the Booking Service to record the reservation and the Inventory Service to decrease the available inventory. Upon the success of those two services, it finally requested the Billing Service to process the payment.

- **Cancellation Saga**: Manages the cancellation of reservations. It reverses the actions of the Reservation Saga. It starts with the Booking Service to release the reservation and, in parallel, updates the Inventory Service to increment the available inventory. And lastly, on the success of the two service requests, it instructs the Billing Service to issue a refund.

Both Sagas are designed to handle failures at any point in the process. For instance, if the Billing Service cannot process a charge, the system will not finalize the reservation and will release any holds on the inventory. Similarly, suppose an error occurs during the cancellation process. In that case, the system will retry the necessary steps to ensure that as long there is a payment from the user, the car is registered to them. Of course, you can decide on any other compensation logic you like.

### Saga Compensation

In the demonstration of the car reservation system, participant services are crafted to emulate potential failures to showcase the compensatory mechanisms of the Sagaway framework. This illustrative setup is essential to understand how the system maintains consistency in adverse scenarios.

The **Billing Service** simulates failures using a random number generator to determine the outcome of billing actions, such as charging for a reservation or processing a refund. In real-world scenarios, failures could stem from network issues, processing errors, or validation failures. Here, the random determination of a billing status—charged, refunded, or not charged—mimics these unpredictable failures. When a charge fails, the Saga must compensate, meaning it should cancel the reservation and release any inventory holds.

The **Inventory Service** demonstrates a different failure scenario by enforcing a constraint where no more than two cars of a particular class can be reserved. If a reservation request exceeds this limit, the service denies the reservation and triggers a failure. It reflects a common real-world limitation where resources are finite and must be managed. Upon such a denial, the Saga's compensatory action would kick in to ensure that partial transactions are rolled back, maintaining the system's integrity. For instance, if a car cannot be reserved because of inventory limits, any previous successful booking transactions should be undone.

### Ensuring Transactional Integrity through Idempotency and Message Sequencing

The Sagaway framework's robust state management capabilities are essential for preserving transactional integrity. A key aspect of maintaining such integrity involves accounting for the potential challenges of idempotency and message sequencing, which are addressed by incorporating a message dispatch time via an HTTP header. This timestamp and a unique message identifier allow the system to disregard stale or duplicate messages, ensuring that only the latest and relevant actions are processed.

Handling Idempotancy when adding or updating a state is more straightforward than handling Idempotency for deleting a state. It might sound strange; deletion operations are inherently idempotent, and the same delete command can be applied multiple times without changing the outcome. The issue arises when messages are received out of order. Such scenarios could inadvertently lead to the recreation of a previously deleted state. This race condition could occur within the asynchronous and compensatory stages of a Saga, especially when multiple instances of a service are involved.

One strategy employed to mitigate a wrong outcome of out-of-order messages is the concept of suspended deletion. By flagging a record for eventual deletion and retaining it for a specified duration, the system ensures that outdated messages do not inadvertently recreate state entries that should no longer exist. This approach can be implemented through periodic cleanup processes purging marked records or setting a Time-To-Live (TTL) attribute on the state. The latter is particularly effective, as it automates the removal process while allowing sufficient time to ensure all aspects of the Saga have concluded.

The TTL duration is carefully calibrated to balance prompt resource liberation with the demands of ongoing Saga operations. For instance, in the car reservations system, once a cancellation is confirmed, the TTL ensures that the vehicle is retained in the "reserved" state only as long as necessary to prevent any out-of-order messages from affecting the availability. Once the TTL expires, the system can confidently release the car into the available inventory, optimizing the asset's utilization and ensuring customer satisfaction. 
You can free resources immediately by utilizing a separate database (or a distributed cache) for the historical messages, their identity, and dispatch time stamps. You need to house-keeping this database.





T

```csharp

sdf

```

There are times when a Saga can't reach to a consistence state, for example, a resource is not available and the Saga concluded the retries. Sagaway provides the mechanism to inform the user about these consistency problems.
