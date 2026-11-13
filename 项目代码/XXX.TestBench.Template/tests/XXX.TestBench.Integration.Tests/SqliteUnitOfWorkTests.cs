using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Time;
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

        var clock = new SystemClock();
        var productRepo = new ProductRepository(factory);
        var pointRepo = new TestPointRepository(factory);
        var type = new ProductType { Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        await productRepo.AddTypeAsync(type);
        var point = new XXX.TestBench.Core.Domain.TestPoints.TestItemPoint
        {
            ProductTypeId = type.Id,
            Name = "耐压试验",
            ExecutorCode = "PressureExecutor",
            ResultKind = "PassFail",
            IsEnabled = true,
            SortOrder = 1,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        await pointRepo.AddAsync(point);

        await using (var uow = new SqliteUnitOfWork(factory))
        {
            await uow.BeginTransactionAsync();
            Assert.NotNull(SqliteAmbient.Current);
            Assert.Equal(System.Data.ConnectionState.Open, SqliteAmbient.Current!.Connection.State);
            var r = await pointRepo.GetAsync(point.Id);
            Assert.NotNull(r);
            Assert.Equal(point.Id, r!.Id);
            await uow.CommitAsync();
        }
        Assert.Null(SqliteAmbient.Current);
    }
}
