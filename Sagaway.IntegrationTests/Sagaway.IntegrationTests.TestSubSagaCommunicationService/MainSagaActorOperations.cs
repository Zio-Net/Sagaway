namespace Sagaway.IntegrationTests.TestSubSagaCommunicationService;

[Flags]
public enum MainSagaActorOperations
{
    CallSubSaga = 1,
    EndSaga = 2
}