using ApprovalTests;
using Xunit.Abstractions;

namespace Sagaway.IntegrationTests.TestProject;

public static class ApprovalVerifyWithDump
{
    //extend ApprovalVerify to allow for a custom file name
    public static void Verify(string text, ITestOutputHelper logger, Func<string, string>? scrubber = null)
    {
        try
        {
            Approvals.Verify(text, scrubber);
        }
        catch (ApprovalTests.Core.Exceptions.ApprovalMismatchException mismatchException)
        {
            //dump the content of the expected file to the console
            logger.WriteLine("*********************************************************");
            logger.WriteLine("Expected:");
            //open the file and dump the content to the console
            using var expectedReader = new StreamReader(mismatchException.Approved);
            var expected = expectedReader.ReadToEnd();
            logger.WriteLine(expected);
            logger.WriteLine("*********************************************************");
            logger.WriteLine("");
            logger.WriteLine("");
            //dump the content of the received file to the console
            logger.WriteLine("=========================================================");
            logger.WriteLine("Received:");
            //open the file and dump the content to the console
            using var receivedReader2 = new StreamReader(mismatchException.Received);
            var received = receivedReader2.ReadToEnd();
            logger.WriteLine(received);
            logger.WriteLine("=========================================================");

            throw;
        }
    }
}