using ApprovalTests.Core;

namespace SagawayTests;

public partial class Tests
{
    class AprovvalNamer : IApprovalNamer
    {
        private readonly string _testName;

        public AprovvalNamer(string testName)
        {
            _testName = testName;
        }
        public string SourcePath => "../../../ApprovalFiles";

        public string Name => _testName;
    }
}