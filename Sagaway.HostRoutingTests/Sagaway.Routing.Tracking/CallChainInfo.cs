namespace Sagaway.Routing.Tracking;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

public class CallChainInfo
{
    // Original call instructions (remains unchanged)
    public string OriginalCallInstructions { get; set; }

    // Current call instructions that change as steps are executed

    public string CallInstructions { get; set; }

    // Track actual calls as a single string (renamed to CallChainHistory)
    public string CallChainHistory { get; set; }

    // New property for Test Name
    public string TestName => OriginalCallInstructions.Split("]")[0][1..];

    // ReSharper disable once UnusedMember.Global
    public CallChainInfo()
    {
        OriginalCallInstructions = string.Empty;
        CallInstructions = string.Empty;
        CallChainHistory = string.Empty;
    }

    // Constructor that initializes both the original and current instructions
        public CallChainInfo(string callInstructions)
    {
        OriginalCallInstructions = callInstructions ?? throw new ArgumentNullException(nameof(callInstructions));

        // Extract the test name from the instructions
        var testParts = callInstructions.Split("]");

        // Set the initial CallChainHistory with the test name
        CallChainHistory = $"[{TestName}]";

        // Initialize the remaining CallInstructions to the part after the test name
        CallInstructions = testParts[1];
    }

    // Helper to remove and process the next step in the chain
    public string PopNextInstruction()
    {
        if (string.IsNullOrEmpty(CallInstructions))
            throw new InvalidOperationException("Call instructions are empty!");

        // Extract the next call from instructions
        var nextCall = CallInstructions.Split("->", 2)[0];
        CallInstructions = CallInstructions.Contains("->")
            ? CallInstructions.Substring(CallInstructions.IndexOf("->", StringComparison.Ordinal) + 2)
            : string.Empty;

        // Add the call to the call chain history (append with ->)
        CallChainHistory += nextCall;

        // Append -> if there are more calls in the instructions
        if (!string.IsNullOrEmpty(CallInstructions))
        {
            CallChainHistory += "->";
        }

        return nextCall;
    }

    // Reset the CallChainInfo to its original state if needed
    public void Reset()
    {
        CallInstructions = OriginalCallInstructions.Split("]")[1];
        var testName = OriginalCallInstructions.Split("]")[0][1..];
        CallChainHistory = $"[{testName}]";
    }
}
