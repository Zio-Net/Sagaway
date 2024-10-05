namespace Sagaway.Routing.TestActorA;

[Flags]
public enum TestActorOperations 
{
    CallNextService = 1,
    DoneAsync = 2
}