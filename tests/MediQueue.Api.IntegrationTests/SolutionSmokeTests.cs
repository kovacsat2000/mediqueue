namespace MediQueue.Api.IntegrationTests;

/// <summary>
/// Placeholder so that <c>dotnet test</c> and the CI step have something to run
/// from the first commit. Replaced in P8 by the real integration suite, which
/// runs against a PostgreSQL container via <c>WebApplicationFactory</c> and
/// Testcontainers.
/// </summary>
public class SolutionSmokeTests
{
    [Fact]
    public void Solution_builds_and_tests_run()
    {
        true.ShouldBeTrue();
    }
}
