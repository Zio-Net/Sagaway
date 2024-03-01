using ApprovalTests.Core;

namespace Sagaway.Tests;

public partial class Tests
{
    class AprovalNamer : IApprovalNamer
    {
        private readonly string _testName;

        public AprovalNamer(string testName)
        {
            _testName = testName;
        }
        public string SourcePath => "../../../ApprovalFiles";

        public string Name => _testName;
    }
}