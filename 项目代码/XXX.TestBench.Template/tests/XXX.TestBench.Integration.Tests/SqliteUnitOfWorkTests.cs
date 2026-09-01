using Microsoft.Data.Sqlite;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public class SqliteUnitOfWorkTests
{
    [Fact]
    public async Task Ambient_VisibleInsideTransaction_AndRepositoryReusesConnection()
    {
        using var env = TestEnv.Create();
        var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, new Pbkdf2PasswordHasher()).InitializeAsync();

        await using (var uow = new SqliteUnitOfWork(factory))
        {
            await uow.BeginTransactionAsync();
            Assert.NotNull(SqliteAmbient.Current);
            Assert.Equal(System.Data.ConnectionState.Open, SqliteAmbient.Current!.State);
            var repo = new RecipeRepository(factory);
            var r = await repo.GetAsync(1);
            Assert.Null(r);
            await uow.CommitAsync();
        }
        Assert.Null(SqliteAmbient.Current);
    }
}
