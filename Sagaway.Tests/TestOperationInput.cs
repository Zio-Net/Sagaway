using System.Text;

namespace Sagaway.Tests;

public partial class Tests
{
    // ReSharper disable  PropertyCanBeMadeInitOnly.Local
    record TestOperationInput
    {
        public Operations OperationNumber { get; init; }
        public Operations Dependencies { get; init; }
        public int MaxRetries { get; init; }
        public int NumberOfFailures { get; init; }
        public Dictionary<int, int> CallDelays { get; init; } = new();
        public int RetryDelay { get; init; }
        public bool UseExponentialBackoff { get; init; }
        public int RevertMaxRetries { get; init; }
        public int RevertNumberOfFailures { get; init; }
        public Dictionary<int, int> RevertCallDelay { get; init; } = new();
        public int RevertRetryDelay { get; init; }
        public bool UseRevertExponentialBackoff { get; init; }
        public Dictionary<int, bool> ValidateFunctionResults { get; init; } = new();
        public Dictionary<int, bool> RevertValidateFunctionResults { get; init; } = new();
        public int CallCounter { get; set; }
        public int RevertCallCounter { get; set; }
        public int ValidateCallCounter { get; set; }
        public int RevertValidateCallCounter { get; set; }
        public int[] Deactivate { get; internal set; } = Array.Empty<int>();
        public int[] ThrowException { get; internal set; } = Array.Empty<int>(); 
        public int[] ValidateThrowException { get; internal set; } = Array.Empty<int>();
        public int[] RevertThrowException { get; internal set; } = Array.Empty<int>();
        public int[] RevertValidateThrowException { get; internal set; } = Array.Empty<int>();
        public bool HasRevert => RevertMaxRetries != 0 || RevertNumberOfFailures != 0 || (RevertCallDelay.Keys.Count) > 0;
        public bool HasValidate => (ValidateFunctionResults.Keys.Count) > 0;
        public bool HasRevertValidate => (RevertValidateFunctionResults.Keys.Count) > 0;
        public bool HasReportFail { get; internal set; }
        public bool HasFailFast { get; internal set; }
        public bool HasFastSuccess { get; internal set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"OperationNumber: {OperationNumber}");
            sb.AppendLine($"Dependencies: {Dependencies}");
            sb.AppendLine($"MaxRetries: {MaxRetries}");
            sb.AppendLine($"NumberOfFailures: {NumberOfFailures}");
            if (CallDelays.Any())
            {
                sb.Append(string.Join(Environment.NewLine, CallDelays.Select(kvp => $"CallDelay: {kvp.Key} - {kvp.Value}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("CallDelays: empty");
            }
            sb.AppendLine($"RetryDelay: {RetryDelay}");
            sb.AppendLine($"RevertMaxRetries: {RevertMaxRetries}");
            sb.AppendLine($"RevertNumberOfFailures: {RevertNumberOfFailures}");
            if (RevertCallDelay.Any())
            {
                sb.Append(string.Join(Environment.NewLine, RevertCallDelay.Select(kvp => $"RevertCallDelay: {kvp.Key} - {kvp.Value}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("RevertCallDelay: empty");
            }
            sb.AppendLine($"RevertRetryDelay: {RevertRetryDelay}");
            if (ValidateFunctionResults.Any())
            {
                sb.Append(string.Join(Environment.NewLine, ValidateFunctionResults.Select(kvp => $"ValidateFunctionResult: {kvp.Key} - {kvp.Value}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("ValidateFunctionResults: empty");
            }
            if (RevertValidateFunctionResults.Any())
            {
                sb.Append(string.Join(Environment.NewLine, RevertValidateFunctionResults.Select(kvp => $"RevertValidateFunctionResult: {kvp.Key} - {kvp.Value}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("RevertValidateFunctionResults: empty");
            }
            sb.AppendLine($"CallCounter: {CallCounter}");
            sb.AppendLine($"RevertCallCounter: {RevertCallCounter}");
            sb.AppendLine($"ValidateCallCounter: {ValidateCallCounter}");
            sb.AppendLine($"RevertValidateCallCounter: {RevertValidateCallCounter}");
            if (Deactivate.Any())
            {
                sb.Append(string.Join(Environment.NewLine, Deactivate.Select(kvp => $"Deactivate: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Deactivate: empty");
            }
            if (ThrowException.Any())
            {
                sb.Append(string.Join(Environment.NewLine, ThrowException.Select(kvp => $"ThrowException: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("ThrowException: empty");
            }
            if (ValidateThrowException.Any())
            {
                sb.Append(string.Join(Environment.NewLine, ValidateThrowException.Select(kvp => $"ValidateThrowException: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("ValidateThrowException: empty");
            }
            if (RevertThrowException.Any())
            {
                sb.Append(string.Join(Environment.NewLine, RevertThrowException.Select(kvp => $"RevertThrowException: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("RevertThrowException: empty");
            }
            if (RevertValidateThrowException.Any())
            {
                sb.Append(string.Join(Environment.NewLine, RevertValidateThrowException.Select(kvp => $"RevertValidateThrowException: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("RevertValidateThrowException: empty");
            }
            sb.AppendLine($"HasReportFail: {HasReportFail}");
            sb.AppendLine($"HasRevert: {HasRevert}");
            sb.AppendLine($"HasValidate: {HasValidate}");
            sb.AppendLine($"HasRevertValidate: {HasRevertValidate}");
            if (HasFailFast)
                sb.AppendLine("Set to fail fast");
            if (HasFastSuccess)
                sb.AppendLine("Set to fast success");
            return sb.ToString();
        }
    }
}