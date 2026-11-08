using System.Net;
using Gms.Shared.Problems;
using Npgsql;

namespace Gms.Api.Tests.Infrastructure;

public static class ProblemAssert
{
    /// <summary>Memastikan respons adalah Problem Details dengan status dan type yang diharapkan, serta traceId terisi.</summary>
    public static GmsProblemDetails Is(ApiResponse response, HttpStatusCode status, string type)
    {
        Assert.True(response.Status == status, $"Status diharapkan {(int)status} tetapi {(int)response.Status}: {response.Body}");
        var problem = response.Problem;
        Assert.Equal(type, problem.Type);
        Assert.Equal((int)status, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId), "traceId wajib ada pada setiap Problem Details");
        return problem;
    }

    /// <summary>Mencari PostgresException di rantai inner exception.</summary>
    public static PostgresException Postgres(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is PostgresException pg)
            {
                return pg;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        throw new Xunit.Sdk.XunitException($"Tidak ada PostgresException pada rantai exception: {exception}");
    }
}
