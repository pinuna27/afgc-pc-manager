using AFGCPCManager.Windows.SingleInstance;

namespace AFGCPCManager.Windows.Tests.SingleInstance;

public sealed class SingleInstanceCoordinatorTests
{
    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void ExpectedPipeFailuresMeanTheOtherInstanceIsUnavailable(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.True(SingleInstanceCoordinator.IsUnavailable(exception));
    }

    [Fact]
    public void ProgrammingErrorsAreNotMistakenForPipeAvailabilityFailures()
    {
        Assert.False(SingleInstanceCoordinator.IsUnavailable(
            new InvalidOperationException()));
    }
}
