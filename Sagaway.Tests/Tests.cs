using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ApprovalTests;
using ApprovalTests.Reporters;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Sagaway.Tests;

/****************************************************************************************************************************
Testing the Sagaway Saga Framework is a complex process. Each time the Saga calls a method, the method can throw an 
exception or return without doing the necessary action. When the Saga does not get a result, it asks for the call's 
outcome. Calling a method to validate the result may also lead to errors and exceptions. And finally, when the Saga 
decides to revert an action, the revert process may throw an exception or fail in any other way. The Saga can retry 
each call before giving up. 
The Saga testing framework simulates all these situations. 
Each test tun checks different Saga processes, from a single action up to five actions. Each action simulates a case 
of failure, retries, timeouts, error results, exceptions, and so forth.
Since many permutations exist, each action behavior is encoded as a list of commands in a string.
Be aware that the testing framework does not check the validity of the combination of each action command.
Here is a list of all action codes:
O# => operation number
D# => dependency on another operation
R# => max retries
UR# => max revert retries
F# => The number of times the operation fails
FF => The operation marked to fail fast
FS => The operation marked to succeed fast
RF => Report Failure (even if it before undo)
W#.# => The Time the operation waits for each call before returning a failure or success. (W Call.Wait)
UF# => The number of times the revert operation fails
UW#.# => The Time the revert operation waits for each call before return a failure or success (UW Call.Wait)
V#.# => Validate function. The first number is the call number; the second number is the result S/F (Success/Fail)
RV#.# => Revert Validate function. The first number is the call number; the second number is the result S/F (Success/Fail)
RW# => Between retry call delay. The number is the delay in seconds, use e instead of a number to use the exponential backoff
RRW# => Between revert retry call delay. The number is the delay in seconds, use e instead of a number to use the exponential backoff
S# => Deactivate the Saga on the number calls
T# => Throw exception on the number calls
UT# => Throw exception on the revert operation on the number calls
VT# => Throw exception on the validate function on the number calls
RVT# => Throw exception on the revert validate function on the number calls
****************************************************************************************************************************/


[UseReporter(typeof(VisualStudioReporter))]
public partial class Tests
{
    readonly ILogger<Tests> _logger;
    private readonly ITestOutputHelper _testOutputHelper;
    private ISaga<Operations>? _saga;

    // ReSharper disable once ConvertToPrimaryConstructor
    public Tests(ILogger<Tests> logger, ITestOutputHelper testOutputHelper)
    {
        _logger = logger;
        _testOutputHelper = testOutputHelper;
    }

    private async Task ReminderCallBack(string reminder)
    {
        if (_saga != null)
            await _saga.ReportReminderAsync(reminder);
    }

    private async Task InformOpFinished(Operations op, bool isSuccess, SagaFastOutcome sagaFastOutcome)
    {
        if (_saga != null)
            await _saga.ReportOperationOutcomeAsync(op, isSuccess, sagaFastOutcome);
    }

    private async Task InformRevertOpFinished(Operations op, bool isSuccess)
    {
        if (_saga != null)
            await _saga.ReportUndoOperationOutcomeAsync(op, isSuccess);
    }


    
    [Theory]
    [Trait("Saga", "Validation")]
    [InlineData("test_validate_no_retry", "O1", "O2|RW2|V1.S")]
    [InlineData("test_validate_no_retry_interval", "O1", "O2|R1|V1.S", "O3")]
    public async Task TestValidationAsync(string testName, string op1, string op2 = "", string op3 = "", string op4 = "", string op5 = "")
    {
        try
        {
            await TestRunAsync(testName, op1, op2, op3, op4, op5);
        }
        catch (Exception ex)
        {
            Approvals.RegisterDefaultNamerCreation(() => new AprovalNamer(testName));
            ApprovalVerifyWithDump.Verify(testName + Environment.NewLine + ex.Message, _testOutputHelper);
            return;
        }

        throw new Exception("Test Failed");
    }

    [Trait("Saga", "Validation")]
    [Fact]
    public void TestValidationNoOperation()
    {
        try
        {
            // ReSharper disable once UnusedVariable
            var builder = Saga<Operations>.Create("test", new SagaTestHost(ReminderCallBack), _logger)
             .WithOnSuccessCompletionCallback(_ => { })
             .WithOnRevertedCallback(_ => { })
             .WithOnFailedRevertedCallback(_ => { })
             .Build();
        }
        catch (Exception ex)
        {
            Approvals.RegisterDefaultNamerCreation(() => new AprovalNamer(nameof(TestValidationNoOperation)));
            ApprovalVerifyWithDump.Verify(nameof(TestValidationNoOperation) + Environment.NewLine + ex.Message, _testOutputHelper);
            return;
        }
        throw new Exception("Test Failed");
    }

    [Trait("Saga", "Validation")]
    [Fact]
    public void TestValidationNoOnSuccessCompletionCallback()
    {
        try
        {
            // ReSharper disable once UnusedVariable
            var builder = Saga<Operations>.Create("test", new SagaTestHost(ReminderCallBack), _logger)
             .WithOnRevertedCallback(_ => { })
             .WithOnFailedRevertedCallback(_ => { })
             .WithOperation(Operations.Op1)
             .WithDoOperation(() => Task.CompletedTask)
             .Build();
        }
        catch (Exception ex)
        {
            Approvals.RegisterDefaultNamerCreation(() => new AprovalNamer(nameof(TestValidationNoOnSuccessCompletionCallback)));
            ApprovalVerifyWithDump.Verify(nameof(TestValidationNoOnSuccessCompletionCallback) + Environment.NewLine + ex.Message, _testOutputHelper);
            return;
        }
        throw new Exception("Test Failed");
    }

    [Trait("Saga", "Validation")]
    [Fact]
    public void TestValidationNoOnRevertedCallback()
    {
        try
        {
            // ReSharper disable once UnusedVariable
            var builder = Saga<Operations>.Create("test", new SagaTestHost(ReminderCallBack), _logger)
             .WithOnSuccessCompletionCallback(_ => { })
             .WithOnFailedRevertedCallback(_ => { })
             .WithOperation(Operations.Op1)
             .WithDoOperation(() => Task.CompletedTask)
             .Build();
        }
        catch (Exception ex)
        {
            Approvals.RegisterDefaultNamerCreation(() => new AprovalNamer(nameof(TestValidationNoOnRevertedCallback)));
            ApprovalVerifyWithDump.Verify(nameof(TestValidationNoOnRevertedCallback) + Environment.NewLine + ex.Message, _testOutputHelper);
            return;
        }
        throw new Exception("Test Failed");
    }

    [Trait("Saga", "Validation")]
    [Fact]
    public void TestValidationNoOnFailedRevertedCallback()
    {
        try
        {
            // ReSharper disable once UnusedVariable
            var builder = Saga<Operations>.Create("test", new SagaTestHost(ReminderCallBack), _logger)
             .WithOnSuccessCompletionCallback(_ => { })
             .WithOnRevertedCallback(_ => { })
             .WithOperation(Operations.Op1)
             .WithDoOperation(() => Task.CompletedTask)
             .Build();
        }
        catch (Exception ex)
        {
            Approvals.RegisterDefaultNamerCreation(() => new AprovalNamer(nameof(TestValidationNoOnFailedRevertedCallback)));
            ApprovalVerifyWithDump.Verify(nameof(TestValidationNoOnFailedRevertedCallback) + Environment.NewLine + ex.Message, _testOutputHelper);
            return;
        }
        throw new Exception("Test Failed");
    }

    [Trait("Saga", "Validation")]
    [Fact]
    public void TestValidationOperationNoDoOperation()
    {
        try
        {
            // ReSharper disable once UnusedVariable
            var builder = Saga<Operations>.Create("test", new SagaTestHost(ReminderCallBack), _logger)
             .WithOnSuccessCompletionCallback(_ => { })
             .WithOnRevertedCallback(_ => { })
             .WithOnFailedRevertedCallback(_ => { })
             .WithOperation(Operations.Op1)
             .Build();
        }
        catch (Exception ex)
        {
            Approvals.RegisterDefaultNamerCreation(() => new AprovalNamer(nameof(TestValidationOperationNoDoOperation)));
            ApprovalVerifyWithDump.Verify(nameof(TestValidationOperationNoDoOperation) + Environment.NewLine + ex.Message, _testOutputHelper);
            return;
        }
        throw new Exception("Test Failed");
    }

    
    [Theory]
    [Trait("Saga", "Dependencies")]
    [InlineData("test_O1_After_O2_and_O3", "O1|D2|D3", "O2", "O3")]
    [InlineData("test_O5_O4_O3_O2_O1", "O1|D2", "O2|D3", "O3|D4", "O4|D5", "O5")]
    [InlineData("test_O1_O2_O3_O5_O4", "O1", "O2", "O3|D1|D2", "O4|D5", "O5")]
    public async Task TestDependenciesAsync(string testName, string op1, string op2 = "", string op3 = "", string op4 = "", string op5 = "")
    {
        await TestRunAsync(testName, op1, op2, op3, op4, op5);
    }

    [Theory]
    [Trait("Saga", "Single Operation")]
    [InlineData("test_single_success", "O1")]
    [InlineData("test_single_fail_no_retry", "O1|F1")]
    [InlineData("test_single_fail_no_retry_report_failure", "O1|F1|RF")]
    [InlineData("test_single_fail_and_retry", "O1|R2|F1|RW10")]
    [InlineData("test_single_fail_and_exponential_retry", "O1|R2|F1|RWe")]
    [InlineData("test_single_fail_fast_and_retry", "O1|R2|FF|RW10")]
    [InlineData("test_single_long_wait_validate_true", "O1|R1|RW2|W1.5|V1.S")]
    [InlineData("test_single_long_wait_validate_false", "O1|R1|RW2|W1.5|V1.F")]
    [InlineData("test_single_long_wait_validate_false_with_revert_fails", "O1|R1|RW2|F2|W1.5|V1.F|RRW2|UR1|UF2")]
    [InlineData("test_single_long_wait_validate_false_with_revert", "O1|R1|RW2|F2|W1.5|V1.F|RRW2|UR2|UF1")]
    [InlineData("test_single_throw_exception", "O1|T1")]
    [InlineData("test_single_throw_exception_report_fail", "O1|T1|RF")]
    [InlineData("test_single_throw_exception_and_retry", "O1|T1|R1|RW2")]
    [InlineData("test_single_throw_exception_and_exponential_retry", "O1|T1|R1|RWe")]
    [InlineData("test_single_throw_exception_twice_and_retry_three_times", "O1|T1|T2|R3|RW2")]
    [InlineData("test_single_long_wait_validate_exception", "O1|R1|RW2|W1.5|V1.S|V2.S|VT1")]
    [InlineData("test_single_long_wait_validate_false_with_revert_exception", "O1|R1|RW2|F2|W1.5|W2.5|RRW2|V1.F|V2.F|UR2|UF2|RVT1|RVT2")]
    public async Task TestSingleAsync(string testName, string op1)
    {
        await TestRunAsync(testName, op1);
    }

    [Theory]
    [Trait("Saga", "Two Operations")]
    [InlineData("test_two_first_success_second_fail_no_retry", "O1", "O2|F1")]
    [InlineData("test_two_first_success_second_fail_no_retry_report_failure", "O1|RF", "O2|F1")]
    [InlineData("test_two_success_after_5_retries", "O1|F4|R5|RW1", "O2|F4|R5|RW1")]
    [InlineData("test_two_second_failed_first_depends_on_first", "O2|F1", "O1|D2")]
    [InlineData("test_two_second_failed_first_fast_second", "O2|FF", "O1|D2")]
    [InlineData("test_two_first_failed_fast_after_running_second", "O2", "O1|D2|FF")]
    [InlineData("test_tow_first_fast_succeeded_second_not_run", "O1|FS", "O2|D1")]
    [InlineData("test_two_both_failed_revert_fails_2_times", "O1|F1|RRW1|UR3|UF2", "O2|F1|RRW1|UR3|UF2")]
    [InlineData("test_two_first_deactivate_saga", "O1|S1|R2|RW1|W1.4|V1.S", "O2|W1.6")]
    [InlineData("test_two_ops_throw_exception_on_first", "O1|T1", "O2")]
    [InlineData("test_two_ops_throw_exception_on_second", "O1", "O2|T1")]
    [InlineData("test_two_throw_exception_and_retry", "O1|T1|R1|RW2", "O2|T1|R1|RW4")]
    [InlineData("test_two_throw_exception_twice_and_retry_three_times", "O1|T1|T2|R3|RW1", "O2|T1|T2|R3|W1.3|RW4")]
    [InlineData("test_two_long_wait_validate_exception", "O1|R1|RW2|W1.5|VT1|V1.S")]
    [InlineData("test_two_long_wait_validate_false_with_revert_exception", "O1", "O2|R1|RW2|F2|W1.5|W2.5|RRW2|V1.F|V2.F|UR2|UF3|RVT1|RVT2")]
    public async Task TestTwoAsync(string testName, string op1, string op2 = "")
    {
        await TestRunAsync(testName, op1, op2);
    }

    [Theory]
    [Trait("Saga", "Five Operations")]
    [InlineData("test_five_success", "O1", "O2", "O3", "O4", "O5")]
    [InlineData("test_five_deactivated_each", "O1|S1|R2|RW1|W1.10|V1.S", "O2|S1|R2|RW3|W1.10|V1.S", "O3|S1|R2|RW5|W1.10|V1.S", "O4|S1|R2|RW7|W1.10|V1.S", "O5|S1|R2|RW9|W1.10|V1.S")]
    [InlineData("test_five_deactivated_each_exponential_wait", "O1|S1|R2|RWe|W1.10|V1.S", "O2|S1|R2|RWe|W1.10|V1.S", "O3|S1|R2|RWe|W1.10|V1.S", "O4|S1|R2|RWe|W1.10|V1.S", "O5|S1|R2|RWe|W1.10|V1.S")]
    [InlineData("test_five_throw_on_fifth", "O1", "O2", "O3", "O4", "O5|T1")]
    [InlineData("test_five_throw_on_fifth_report_failure", "O1|RF", "O2|W1.1", "O3|W1.2", "O4|W1.3", "O5|W1.4|T1")]
    [InlineData("test_five_failfast_on_third", "O1", "O2|W1.1", "O3|W1.2|FF", "O4|W1.3", "O5|W1.4")]
    [InlineData("test_five_fast_success_on_third", "O1", "O2|W1.1", "O3|W1.2|D1|D2|FS", "O4|W1.3|D3", "O5|W1.4|D3")]
    [InlineData("test_five_fast_success_on_third_with_second_done", "O1", "O2", "O3|W1.2|D1|FS", "O4|W1.3|D3", "O5|W1.4|D3")]
    public async Task TestFiveAsync(string testName, string op1, string op2 = "", string op3 = "", string op4 = "", string op5 = "")
    {
        await TestRunAsync(testName, op1, op2, op3, op4, op5);
    }

    private async Task TestRunAsync(string testName, params string[] operations)
    {
        var sagaHost = new SagaTestHost(ReminderCallBack);

        var builder = Saga<Operations>.Create("test", sagaHost, _logger);
        var sb = new StringBuilder();
        sb.AppendLine($"Preparing test {testName}");

        builder.WithOnSuccessCompletionCallback(log => sb.AppendLine($"OnSuccessCompletionCallback: Success.{Environment.NewLine}Run Log:{Environment.NewLine}" + log + Environment.NewLine));

        var buildOperations = BuildTestOperations(operations).ToList();
        
        if (buildOperations.Any(o => o.HasReportFail))
        {
            builder.WithOnFailedCallback(log => sb.AppendLine($"OnFailCallback: Fail.{Environment.NewLine}Run Log:{Environment.NewLine}" + log + Environment.NewLine));
        }
        else
        {
            builder.WithOnRevertedCallback(log => sb.AppendLine($"OnRevertedCallback: Reverted.{Environment.NewLine}Run Log:{Environment.NewLine}" + log + Environment.NewLine))
            .WithOnFailedRevertedCallback(log => sb.AppendLine($"OnFailedRevertedCallback: FailedReverted.{Environment.NewLine}Run Log:{Environment.NewLine}" + log + Environment.NewLine));
        }

        foreach (var testOperation in buildOperations)
        {
            AddSagaToBuilder(testOperation, builder, sb);
        }
        _saga = builder.Build();
        _saga.OnSagaCompleted += (_, args) => sb.AppendLine($"OnSagaCompleted: Id: {args.SagaId} Status: {args.Status}");

        await _saga.InformActivatedAsync();
        await _saga!.RunAsync();

        var stopWatch = new Stopwatch();
        stopWatch.Start();
        while (_saga.InProgress)
        {
            await Task.Delay(1000);
            
            if (!Debugger.IsAttached && stopWatch.Elapsed > TimeSpan.FromSeconds(20))
            {
                sb.AppendLine("*** timeout ***");
                break;
            }
        }
        await Task.Delay(500);

        sb.AppendLine();
        sb.AppendLine("*** Telemetry ***");
        sb.Append(((TestTelemetryAdapter)sagaHost.TelemetryAdapter).GenerateSagaTraceResult());

        Approvals.RegisterDefaultNamerCreation(() => new AprovalNamer(testName));
        ApprovalVerifyWithDump.Verify(sb.ToString(), _testOutputHelper, RemoveDynamic);
    }
    private void AddSagaToBuilder(TestOperationInput testOperation, Saga<Operations>.SagaBuilder builder, StringBuilder sb)
    {
        sb.AppendLine(testOperation.ToString());
        var operationBuilder = builder.WithOperation(testOperation.OperationNumber)
        .WithPreconditions(testOperation.Dependencies)
        .WithDoOperation(async () =>
        {
            await DoOperation(sb, testOperation);
        })
        .WithMaxRetries(testOperation.MaxRetries);
        
        if (testOperation.HasValidate)
        {
            operationBuilder.WithValidateFunction(async () => await Validate(sb, testOperation));
        }
        
        if (testOperation.RetryDelay > 0)
            operationBuilder.WithRetryIntervalTime(TimeSpan.FromSeconds(testOperation.RetryDelay));
        else if (testOperation.UseExponentialBackoff)
            operationBuilder.WithRetryIntervalTime(ExponentialBackoff.InSeconds(1,32, 0));

        if (!testOperation.HasRevert) 
            return;

        var undoOperationBuilder = operationBuilder.WithUndoOperation(async () =>
            {
                await Revert(sb, testOperation);

            })
            .WithMaxRetries(testOperation.RevertMaxRetries);

        if (testOperation.HasRevertValidate)
        {
            undoOperationBuilder.WithValidateFunction(async () => await RevertValidate(sb, testOperation));
        }

        if (testOperation.RevertRetryDelay > 0)
            undoOperationBuilder.WithUndoRetryInterval(TimeSpan.FromSeconds(testOperation.RevertRetryDelay));
        else if (testOperation.UseRevertExponentialBackoff)
            undoOperationBuilder.WithUndoRetryInterval(ExponentialBackoff.InSeconds(1,32, 0));
    }
    
    private async Task DoOperation(StringBuilder sb, TestOperationInput testOperation)
    {
        bool isSuccess;
        try
        {
            bool shouldThrow = testOperation.ThrowException.Contains(testOperation.CallCounter + 1);
            if (shouldThrow)
                throw new Exception($"Throwing exception on call {testOperation.CallCounter + 1}");

            if (testOperation.Deactivate.Contains(testOperation.CallCounter + 1))
            {
                await _saga!.InformDeactivatedAsync();
            }
            testOperation.CallDelays.TryGetValue(testOperation.CallCounter + 1, out var delay);
            await Task.Delay(delay * 1000);

            isSuccess = testOperation.CallCounter >= testOperation.NumberOfFailures;

            sb.AppendLine($"Calling {testOperation.OperationNumber}: Success {isSuccess}");
        }
        finally
        {
            testOperation.CallCounter++;
        }

        var sagaFastOutput = testOperation.HasFailFast ? SagaFastOutcome.Failure : testOperation.HasFastSuccess ? SagaFastOutcome.Success : SagaFastOutcome.None;
        await InformOpFinished(testOperation.OperationNumber, isSuccess && !testOperation.HasFailFast, sagaFastOutput);
    }
    private static async Task<bool> Validate(StringBuilder sb, TestOperationInput testOperation)
    {
        try
        {
            bool shouldThrow = testOperation.ValidateThrowException.Contains(testOperation.ValidateCallCounter + 1);
            if (shouldThrow)
                throw new Exception($"Throwing exception on validate {testOperation.ValidateCallCounter + 1}");

            testOperation.ValidateFunctionResults.TryGetValue(testOperation.ValidateCallCounter + 1, out var validate);
            sb.AppendLine($"{testOperation.OperationNumber} Validate returns {validate}");

            return await Task.FromResult(validate);
        }
        finally
        {
            testOperation.ValidateCallCounter++;
        }
    }

    private async Task Revert(StringBuilder sb, TestOperationInput testOperation)
    {
        bool isSuccess;
        try
        {
            bool shouldThrow = testOperation.RevertThrowException.Contains(testOperation.RevertCallCounter + 1);
            if (shouldThrow)
                throw new Exception($"Throwing exception on revert {testOperation.RevertCallCounter + 1}");

            testOperation.RevertCallDelay.TryGetValue(testOperation.RevertCallCounter + 1, out var delay);
            await Task.Delay(delay * 1000);

            isSuccess = testOperation.RevertCallCounter >= testOperation.RevertNumberOfFailures;
            sb.AppendLine($"Calling revert for {testOperation.OperationNumber}: Success {isSuccess}");
        }
        finally
        {
            testOperation.RevertCallCounter++;
        }
        await InformRevertOpFinished(testOperation.OperationNumber, isSuccess);
    }
    
    private static async Task<bool> RevertValidate(StringBuilder sb, TestOperationInput testOperation)
    {
        try
        {
            bool shouldThrow = testOperation.RevertValidateThrowException.Contains(testOperation.RevertValidateCallCounter + 1);
            if (shouldThrow)
                throw new Exception($"Throwing exception on revert validate {testOperation.RevertValidateCallCounter + 1}");

            testOperation.RevertValidateFunctionResults.TryGetValue(testOperation.RevertValidateCallCounter, out var validate);
            sb.AppendLine($"{testOperation.OperationNumber} Revert validate returns {validate}");
            return await Task.FromResult(validate);
        }
        finally
        {
            testOperation.RevertValidateCallCounter++;
        }
    }


    private static IEnumerable<TestOperationInput> BuildTestOperations(IEnumerable<string> operations)
    {
        foreach (var operation in operations)
        {
            if (String.IsNullOrEmpty(operation))
                continue;
            var entries = operation.Split("|");
            var operationEnumNumber = ToOperationsEnum(NumberOfElements("O"));
            // ReSharper disable once RedundantAssignment
            var dependencies = entries.Where(e => e.StartsWith("D")).Select(e => int.Parse(e[1..])).Aggregate((Operations)0, (e, a) => e |= ToOperationsEnum(a));
            var maxRetries = NumberOfElements("R");
            var numberOfFailures = NumberOfElements("F");
            var failFast = entries.Any(e=> e == "FF");
            var fastSuccess = entries.Any(e=> e == "FS");
            var delays = ExtractDelayByIteration("W");
            var retryDelay = NumberOfElements("RW");
            bool useExponentialBackoff = UseExponentialBackoff("RW");
            var revertMaxRetry = NumberOfElements("UR");
            var numberOfRevertFailures = NumberOfElements("UF");
            var revertDelays = ExtractDelayByIteration("UW");
            var revertRetryDelay = NumberOfElements("RRW");
            var useRevertExponentialBackoff = UseExponentialBackoff("RRW");
            var validateFunctionResults = entries.Where(e => e.StartsWith("V") && IsDigit(e[1])).Select(e => e[1..].Split(".")).ToDictionary(k => int.Parse(k[0]), v => v[1].StartsWith("S"));
            var revertValidateFunctionResults = entries.Where(e => e.StartsWith("RV") && IsDigit(e[2])).Select(e => e[2..].Split(".")).ToDictionary(k => int.Parse(k[0]), v => v[1].StartsWith("S"));
            var deactivate = entries.Where(e => e.StartsWith("S")).Select(e => int.Parse(e[1..])).ToArray();
            var throwException = entries.Where(e => e.StartsWith("T")).Select(e => int.Parse(e[1..])).ToArray();
            var validateThrowException = entries.Where(e => e.StartsWith("VT")).Select(e => int.Parse(e[2..])).ToArray(); 
            var revertThrowException = entries.Where(e => e.StartsWith("UT")).Select(e => int.Parse(e[2..])).ToArray(); 
            var revertValidateThrowException = entries.Where(e => e.StartsWith("RVT")).Select(e => int.Parse(e[3..])).ToArray();
            var hasReportFail = entries.FirstOrDefault(e => e.StartsWith("RF")) != null;
            
            int NumberOfElements(string elementName)
            {
                var n = entries.Where(e => e.StartsWith(elementName) && IsDigit(e[elementName.Length..][0])).Select(e => int.Parse(e[elementName.Length..])).FirstOrDefault();
                return n;
            }

            bool UseExponentialBackoff(string elementName)
            {
                var e = entries.FirstOrDefault(e => e.StartsWith(elementName) && e.EndsWith("e")) != null;
                return e;
            }

            static bool IsDigit(char c) => c is >= '0' and <= '9';

            Dictionary<int, int> ExtractDelayByIteration(string elementName)
            {
                var delayByIteration = entries.Where(e => e.StartsWith(elementName)).Select(e => e[elementName.Length..].Split(".")).ToDictionary(k => int.Parse(k[0]), v => int.Parse(v[1]));
                return delayByIteration;
            }

            static Operations ToOperationsEnum(int i) => (Operations)(1 << i - 1);

            var testOperation = new TestOperationInput()
            {
                OperationNumber = operationEnumNumber,
                Dependencies = dependencies,
                MaxRetries = maxRetries,
                NumberOfFailures = numberOfFailures,
                CallDelays = delays,
                RetryDelay = retryDelay,
                UseExponentialBackoff = useExponentialBackoff,
                RevertMaxRetries = revertMaxRetry,
                RevertNumberOfFailures = numberOfRevertFailures,
                RevertCallDelay = revertDelays,
                RevertRetryDelay = revertRetryDelay,
                UseRevertExponentialBackoff = useRevertExponentialBackoff,
                ValidateFunctionResults = validateFunctionResults,
                RevertValidateFunctionResults = revertValidateFunctionResults,
                Deactivate = deactivate,
                ThrowException = throwException,
                ValidateThrowException = validateThrowException,
                RevertThrowException = revertThrowException,
                RevertValidateThrowException = revertValidateThrowException,
                HasReportFail = hasReportFail,
                HasFailFast = failFast,
                HasFastSuccess = fastSuccess
            };
            yield return testOperation;
        }
    }
        
    private string RemoveDynamic(string text)
    {
        string pattern = @"\[\d{1,2}:\d{1,2}:\d{1,2}\]";
        string replacement = "[*time*]";
        string result = Regex.Replace(text, pattern, replacement);
       
        return result;
    }
}