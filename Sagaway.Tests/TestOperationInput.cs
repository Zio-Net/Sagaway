using System.Text;

namespace SagawayTests;

public partial class Tests
{
    record TestOperationInput
    {
        public Operations OperationNumber { get; init; }
        public Operations Dependencies { get; init; }
        public int MaxRetries { get; init; } = 0;
        public int NumberOfFailures { get; init; } = 0;
        public Dictionary<int, int> CallDelays { get; init; } = new Dictionary<int, int>();
        public int RetryDelay { get; init; } = 0;
        public int RevertMaxRetries { get; init; } = 0;
        public int RevertNumberOfFailures { get; init; } = 0;
        public Dictionary<int, int> RevertCallDelay { get; init; } = new Dictionary<int, int>();
        public int RevertRetryDelay { get; init; } = 0;
        public Dictionary<int, bool> ValidateFunctionResults { get; init; } = new Dictionary<int, bool>();
        public Dictionary<int, bool> RevertValidateFunctionResults { get; init; } = new Dictionary<int, bool>();
        public int CallCounter { get; set; } = 0;
        public int RevertCallCounter { get; set; } = 0;
        public int ValidateCallCounter { get; set; } = 0;
        public int RevertValidateCallCounter { get; set; } = 0;
        public int[] Deactivate { get; internal set; } = new int[0];
        public int[] ThrowException { get; internal set; } = new int [0]; 
        public int[] ValidateThrowException { get; internal set; } = new int[0];
        public int[] RevertThrowException { get; internal set; } = new int[0];
        public int[] RevertValidateThrowException { get; internal set; } = new int[0];
        public bool HasRevert => RevertMaxRetries != 0 || RevertNumberOfFailures != 0 || ((RevertCallDelay?.Keys?.Count ?? 0) > 0);
        public bool HasValidate => (ValidateFunctionResults?.Keys?.Count ?? 0) > 0;
        public bool HasRevertValidate => (RevertValidateFunctionResults?.Keys?.Count ?? 0) > 0;
        public bool HasReportFail { get; internal set; } = false;
        public bool HasFailFast { get; internal set; } = false;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"OperationNumber: {OperationNumber}");
            sb.AppendLine($"Dependencies: {Dependencies}");
            sb.AppendLine($"MaxRetries: {MaxRetries}");
            sb.AppendLine($"NumberOfFailures: {NumberOfFailures}");
            if (CallDelays != null && CallDelays.Any())
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
            if (RevertCallDelay != null && RevertCallDelay.Any())
            {
                sb.Append(string.Join(Environment.NewLine, RevertCallDelay.Select(kvp => $"RevertCallDelay: {kvp.Key} - {kvp.Value}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("RevertCallDelay: empty");
            }
            sb.AppendLine($"RevertRetryDelay: {RevertRetryDelay}");
            if (ValidateFunctionResults != null && ValidateFunctionResults.Any())
            {
                sb.Append(string.Join(Environment.NewLine, ValidateFunctionResults.Select(kvp => $"ValidateFunctionResult: {kvp.Key} - {kvp.Value}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("ValidateFunctionResults: empty");
            }
            if (RevertValidateFunctionResults != null && RevertValidateFunctionResults.Any())
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
            if (Deactivate != null && Deactivate.Any())
            {
                sb.Append(string.Join(Environment.NewLine, Deactivate.Select(kvp => $"Deactivate: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Deactivate: empty");
            }
            if (ThrowException != null && ThrowException.Any())
            {
                sb.Append(string.Join(Environment.NewLine, ThrowException.Select(kvp => $"ThrowException: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("ThrowException: empty");
            }
            if (ValidateThrowException != null && ValidateThrowException.Any())
            {
                sb.Append(string.Join(Environment.NewLine, ValidateThrowException.Select(kvp => $"ValidateThrowException: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("ValidateThrowException: empty");
            }
            if (RevertThrowException != null && RevertThrowException.Any())
            {
                sb.Append(string.Join(Environment.NewLine, RevertThrowException.Select(kvp => $"RevertThrowException: {kvp}")));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("RevertThrowException: empty");
            }
            if (RevertValidateThrowException != null && RevertValidateThrowException.Any())
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
                sb.AppendLine($"Set to fail fast");
            return sb.ToString();
        }
    }
}