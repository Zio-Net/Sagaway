using ApprovalTests.Core;

namespace Sagaway.IntegrationTests.TestProject;

class AprovalNamer(string testName) : IApprovalNamer
{
    public string SourcePath => "../../../ApprovalFiles";

    public string Name => testName;
}